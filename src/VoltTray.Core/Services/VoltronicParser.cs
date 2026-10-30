using System;
using System.Collections.Generic;
using VoltTray.Core.Models;
using VoltTray.Core.Ports;

namespace VoltTray.Core.Services;

public sealed class VoltronicParser
{
    private readonly IBatteryAutonomyEstimator _estimator;
    private readonly double _capacityFactor;
    private bool _lastBit2;
    private DateTimeOffset? _selfTestStartTime;

    public VoltronicParser(IBatteryAutonomyEstimator? estimator = null, double capacityFactor = 1.0)
    {
        _estimator = estimator ?? new PeukertEstimator();
        _capacityFactor = capacityFactor;
    }

    public UpsTelemetry? Parse(byte[]? raw, UpsTelemetry? previous = null, DateTimeOffset? now = null)
    {
        if (raw == null || raw.Length < 3) return previous;

        int start = 0;
        while (start < raw.Length && raw[start] == 0) start++;
        if (start >= raw.Length || raw[start] != 0x23) return previous; // Must start with '#' (0x23)

        List<byte[]> tokens = new();
        List<byte> current = new();
        bool hasCr = false;

        for (int i = start + 1; i < raw.Length; i++)
        {
            byte b = raw[i];
            if (b == 0x0D) { hasCr = true; break; }
            if (b == 0x20)
            {
                if (current.Count > 0) { tokens.Add(current.ToArray()); current.Clear(); }
                continue;
            }
            if (b == 0x28 && i + 1 < raw.Length)
            {
                byte esc = raw[++i];
                current.Add(esc switch
                {
                    0x00 => 0x0D,
                    0x01 => 0x11,
                    0x02 => 0x13,
                    0x03 => 0x0A,
                    0x04 => 0x20,
                    _ => esc
                });
                continue;
            }
            current.Add(b);
        }
        if (current.Count > 0) tokens.Add(current.ToArray());
        if (!hasCr || tokens.Count < 9) return previous;

        double inV = DecodeVoltage(tokens[0], tokens[1]);
        double outV = DecodeVoltage(tokens[2], tokens[3]);
        int loadPct = Math.Clamp(tokens[4].Length > 0 ? tokens[4][0] : 0, 0, 100);
        int activeWatts = (int)Math.Round((double)loadPct * 900.0 / 100.0, MidpointRounding.AwayFromZero);

        double freq = 60.0;
        if (tokens.Count >= 7 && tokens[6].Length >= 2)
        {
            int f1 = (tokens[5][0] << 8) | (tokens[5].Length > 1 ? tokens[5][1] : 0);
            int f2 = (tokens[6][0] << 8) | (tokens[6].Length > 1 ? tokens[6][1] : 0);
            if (f2 > 0) freq = Math.Round((double)f1 / (f2 / 1000.0) / 1000.0, 1);
            if (freq < 45.0 || freq > 70.0) freq = 60.0;
        }

        int b1 = tokens[7].Length > 0 ? tokens[7][0] : 0;
        int b2 = tokens[8].Length > 0 ? tokens[8][0] : 0;
        double batV = Math.Round((b1 * b2) / 510.0, 2);
        double cellV = Math.Round(batV / 2.0, 2);

        byte statusByte1 = tokens.Count >= 10 && tokens[9].Length > 0 ? tokens[9][0] : (byte)0;
        DateTimeOffset timestamp = now ?? DateTimeOffset.UtcNow;

        bool bit2 = (statusByte1 & 0x04) != 0;
        bool isTestInProgress = false;
        if (bit2)
        {
            if (!_lastBit2)
            {
                _selfTestStartTime = timestamp;
                _lastBit2 = true;
                isTestInProgress = true;
            }
            else if (_selfTestStartTime.HasValue)
            {
                isTestInProgress = (timestamp - _selfTestStartTime.Value).TotalSeconds < 15.0;
            }
        }
        else
        {
            _lastBit2 = false;
            _selfTestStartTime = null;
        }

        var (state, isBeeperMuted, _) = DecodeStatusBits(statusByte1, inV, outV);
        int batteryPct = _estimator.CalculateSoCPercentage(batV);
        bool isHardwareLow = (statusByte1 & 0x40) != 0 && (statusByte1 & 0x80) != 0;
        int runtimeMinutes = _estimator.EstimateRuntimeMinutes(loadPct, _capacityFactor, isHardwareLow);

        return new UpsTelemetry(
            InputVoltageAc: inV,
            FaultReferenceVoltageAc: inV,
            OutputVoltageAc: outV,
            LoadPercentage: loadPct,
            ActiveLoadWatts: activeWatts,
            FrequencyHz: freq,
            BatteryVoltageDc: batV,
            Cell1VoltageDc: cellV,
            Cell2VoltageDc: cellV,
            BatteryPercentage: batteryPct,
            EstimatedRuntimeMinutes: runtimeMinutes,
            TemperatureCelsius: 25.0,
            State: state,
            IsBeeperMuted: isBeeperMuted,
            IsTestInProgress: isTestInProgress,
            BankSpec: new BatteryBankSpec(),
            Timestamp: timestamp);
    }

    public static byte[] Deescape(byte[] input)
    {
        List<byte> result = new();
        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] == 0x28 && i + 1 < input.Length)
            {
                byte esc = input[++i];
                result.Add(esc switch
                {
                    0x00 => 0x0D,
                    0x01 => 0x11,
                    0x02 => 0x13,
                    0x03 => 0x0A,
                    0x04 => 0x20,
                    _ => esc
                });
            }
            else
            {
                result.Add(input[i]);
            }
        }
        return result.ToArray();
    }

    public static double DecodeVoltage(byte[] hexVal, byte[] ratio)
    {
        if (hexVal == null || ratio == null || ratio.Length == 0) return 0.0;
        int r = ratio[0];
        int val = hexVal.Length >= 2 ? (hexVal[0] << 8) | hexVal[1] : hexVal.Length == 1 ? hexVal[0] : 0;
        return Math.Round((double)(val * r) / (51.0 * 256.0), 1);
    }

    public static (UpsOperationalState State, bool IsBeeperMuted, bool IsTestInProgress) DecodeStatusBits(byte statusByte1, double inV = 120.0, double outV = 120.0)
    {
        UpsOperationalState state;
        if ((statusByte1 & 0x10) != 0)
            state = UpsOperationalState.Fault;
        else if ((statusByte1 & 0x40) != 0 && (statusByte1 & 0x80) != 0)
            state = UpsOperationalState.BatteryLow;
        else if ((statusByte1 & 0x80) != 0)
            state = UpsOperationalState.OnBattery;
        else if ((statusByte1 & 0x20) != 0 || (outV > 0 && Math.Abs(inV - outV) / outV > 0.05))
            state = UpsOperationalState.ChargingAvr;
        else
            state = UpsOperationalState.LineNormal;

        bool isBeeperMuted = (statusByte1 & 0x01) == 0;
        bool isTestInProgress = (statusByte1 & 0x04) != 0;
        return (state, isBeeperMuted, isTestInProgress);
    }

    public void ResetSelfTest()
    {
        _lastBit2 = false;
        _selfTestStartTime = null;
    }
}
