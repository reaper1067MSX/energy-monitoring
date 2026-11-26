using System;
using System.Collections.Generic;
using VoltTray.Core.Models;
using VoltTray.Core.Ports;
using VoltTray.Core.Services;

namespace VoltTray.Infrastructure.Protocol;

/// <summary>Decoded hardware nominal ratings from Protocol 'T' Status Byte 2 (T10).</summary>
public readonly record struct UpsStatusByte2Ratings(
    double NominalFrequencyHz,
    int BatteryBankSeriesCount,
    double NominalBatteryVoltageDc,
    double? NominalOutputVoltageAc);

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
        if (raw == null || raw.Length < VoltronicProtocolConstants.MinRawFrameLength) return previous;

        int start = 0;
        while (start < raw.Length && raw[start] == VoltronicProtocolConstants.NullByte) start++;
        if (start >= raw.Length || raw[start] != VoltronicProtocolConstants.FrameHeaderHash) return previous; // Must start with '#'

        List<byte[]> tokens = new();
        List<byte> current = new();
        bool hasCr = false;

        for (int i = start + 1; i < raw.Length; i++)
        {
            byte b = raw[i];
            if (b == VoltronicProtocolConstants.CarriageReturn) { hasCr = true; break; }
            if (b == VoltronicProtocolConstants.SpaceDelimiter)
            {
                if (current.Count > 0) { tokens.Add(current.ToArray()); current.Clear(); }
                continue;
            }
            if (b == VoltronicProtocolConstants.FrameHeaderParen && i + 1 < raw.Length)
            {
                byte esc = raw[++i];
                current.Add(esc switch
                {
                    VoltronicProtocolConstants.EscapeCodeCr => VoltronicProtocolConstants.CarriageReturn,
                    VoltronicProtocolConstants.EscapeCodeDc1 => VoltronicProtocolConstants.Dc1,
                    VoltronicProtocolConstants.EscapeCodeDc3 => VoltronicProtocolConstants.Dc3,
                    VoltronicProtocolConstants.EscapeCodeLf => VoltronicProtocolConstants.LineFeed,
                    VoltronicProtocolConstants.EscapeCodeSpace => VoltronicProtocolConstants.SpaceDelimiter,
                    _ => esc
                });
                continue;
            }
            current.Add(b);
        }
        if (current.Count > 0) tokens.Add(current.ToArray());
        if (!hasCr || tokens.Count < VoltronicProtocolConstants.MinTokenCount) return previous;

        double inV = DecodeVoltage(tokens[0], tokens[1]);
        double outV = DecodeVoltage(tokens[2], tokens[3]);
        int loadPct = Math.Clamp(tokens[4].Length > 0 ? tokens[4][0] : 0, 0, 100);
        int activeWatts = (int)Math.Round((double)loadPct * VoltronicProtocolConstants.DefaultRatedWatts / 100.0, MidpointRounding.AwayFromZero);

        double freq = tokens.Count >= 7
            ? DecodeFrequencyHz(tokens[5], tokens[6])
            : VoltronicProtocolConstants.DefaultNominalFrequencyHz;

        int b1 = tokens[7].Length > 0 ? tokens[7][0] : 0;
        int b2 = tokens[8].Length > 0 ? tokens[8][0] : 0;
        double batV = Math.Round((b1 * b2) / VoltronicProtocolConstants.BatteryVoltageDivisor, 2);
        double cellV = Math.Round(batV / 2.0, 2);

        byte statusByte1 = tokens.Count >= 10 && tokens[9].Length > 0 ? tokens[9][0] : (byte)0;
        DateTimeOffset timestamp = now ?? DateTimeOffset.UtcNow;

        bool bit2 = (statusByte1 & VoltronicProtocolConstants.StatusSelfTestActiveMask) != 0;
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
                isTestInProgress = (timestamp - _selfTestStartTime.Value).TotalSeconds < VoltronicProtocolConstants.SelfTestDurationSeconds;
            }
        }
        else
        {
            _lastBit2 = false;
            _selfTestStartTime = null;
        }

        var (state, isBeeperMuted, _) = DecodeStatusBits(statusByte1, inV, outV);
        int batteryPct = _estimator.CalculateSoCPercentage(batV);
        bool isHardwareLow = (statusByte1 & VoltronicProtocolConstants.StatusBatteryLowMask) != 0 &&
                             (statusByte1 & VoltronicProtocolConstants.StatusOnBatteryMask) != 0;
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
            TemperatureCelsius: VoltronicProtocolConstants.DefaultAmbientTemperatureCelsius,
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
            if (input[i] == VoltronicProtocolConstants.FrameHeaderParen && i + 1 < input.Length)
            {
                byte esc = input[++i];
                result.Add(esc switch
                {
                    VoltronicProtocolConstants.EscapeCodeCr => VoltronicProtocolConstants.CarriageReturn,
                    VoltronicProtocolConstants.EscapeCodeDc1 => VoltronicProtocolConstants.Dc1,
                    VoltronicProtocolConstants.EscapeCodeDc3 => VoltronicProtocolConstants.Dc3,
                    VoltronicProtocolConstants.EscapeCodeLf => VoltronicProtocolConstants.LineFeed,
                    VoltronicProtocolConstants.EscapeCodeSpace => VoltronicProtocolConstants.SpaceDelimiter,
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

    /// <summary>Decodes an arbitrary-width (1..8 byte) big-endian unsigned integer token.</summary>
    public static ulong DecodeBigEndianUInt64(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty || bytes.Length > 8) return 0;
        ulong value = 0;
        foreach (byte b in bytes) value = (value << 8) | b;
        return value;
    }

    /// <summary>Decodes grid frequency (Hz) from arbitrary-width T5 period ticks and T6 timer clock Hz.</summary>
    public static double DecodeFrequencyHz(ReadOnlySpan<byte> periodTicks, ReadOnlySpan<byte> timerClockHz, double fallbackHz = VoltronicProtocolConstants.DefaultNominalFrequencyHz)
    {
        ulong val1 = DecodeBigEndianUInt64(periodTicks), val2 = DecodeBigEndianUInt64(timerClockHz);
        if (val1 == 0 || val2 == 0) return fallbackHz;
        double hz = Math.Round((double)val2 / val1, 2);
        return hz >= VoltronicProtocolConstants.MinValidFrequencyHz && hz <= VoltronicProtocolConstants.MaxValidFrequencyHz ? hz : fallbackHz;
    }

    /// <summary>Decodes nominal ratings bitmask from Protocol 'T' Status Byte 2 (T10).</summary>
    public static UpsStatusByte2Ratings DecodeStatusByte2Ratings(byte statusByte2)
    {
        double nominalHz = (statusByte2 & VoltronicProtocolConstants.StatusByte2Freq60HzMask) != 0 ? 60.0 : 50.0;
        int banks = ((statusByte2 & VoltronicProtocolConstants.StatusByte2BatteryBankMask) >> VoltronicProtocolConstants.StatusByte2BatteryBankShift) + 1;
        double? nominalAc = (statusByte2 & VoltronicProtocolConstants.StatusByte2NominalAcMask) switch { 0 => 110.0, 1 => 120.0, 2 => 220.0, 3 => 230.0, 4 => 240.0, _ => null };
        return new UpsStatusByte2Ratings(nominalHz, banks, banks * 12.0, nominalAc);
    }

    public static double DecodeVoltage(byte[] hexVal, byte[] ratio)
    {
        if (hexVal == null || ratio == null || ratio.Length == 0) return 0.0;
        ulong val = DecodeBigEndianUInt64(hexVal);
        return Math.Round((double)val * ratio[0] / VoltronicProtocolConstants.VoltageDivisor, 1);
    }

    public static (UpsOperationalState State, bool IsBeeperMuted, bool IsTestInProgress) DecodeStatusBits(byte statusByte1, double inV = 120.0, double outV = 120.0)
    {
        UpsOperationalState state;
        if ((statusByte1 & VoltronicProtocolConstants.StatusFaultMask) != 0)
            state = UpsOperationalState.Fault;
        else if ((statusByte1 & VoltronicProtocolConstants.StatusBatteryLowMask) != 0 && (statusByte1 & VoltronicProtocolConstants.StatusOnBatteryMask) != 0)
            state = UpsOperationalState.BatteryLow;
        else if ((statusByte1 & VoltronicProtocolConstants.StatusOnBatteryMask) != 0)
            state = UpsOperationalState.OnBattery;
        else if ((statusByte1 & VoltronicProtocolConstants.StatusAvrBoostBuckMask) != 0 || (outV > 0 && Math.Abs(inV - outV) / outV > VoltronicProtocolConstants.MainsDivergenceThreshold))
            state = UpsOperationalState.ChargingAvr;
        else
            state = UpsOperationalState.LineNormal;

        bool isBeeperMuted = (statusByte1 & VoltronicProtocolConstants.StatusBeeperActiveMask) == 0;
        bool isTestInProgress = (statusByte1 & VoltronicProtocolConstants.StatusSelfTestActiveMask) != 0;
        return (state, isBeeperMuted, isTestInProgress);
    }

    public void ResetSelfTest()
    {
        _lastBit2 = false;
        _selfTestStartTime = null;
    }
}
