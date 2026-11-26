using System;
using System.Collections.Generic;
using VoltTray.Core.Models;
using VoltTray.Core.Ports;
using VoltTray.Core.Services;

namespace VoltTray.Infrastructure.Protocol;

public sealed class VoltronicParser
{
    private const int BitsPerByte = 8;
    private const int MaxBigEndianByteWidth = 8;
    private const double SingleBatteryBankVolts = 12.0;
    private const double NominalFrequency50Hz = 50.0;
    private const double NominalFrequency60Hz = 60.0;

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
        if (raw == null || raw.Length < VoltronicProtocolConstants.MinRawFrameLength)
        {
            return previous;
        }

        if (!TryTokenizeHashFrame(raw, out IReadOnlyList<byte[]> tokens) ||
            tokens.Count < VoltronicProtocolConstants.MinTokenCount)
        {
            return previous;
        }

        double inputVoltageAc = DecodeVoltage(tokens[0], tokens[1]);
        double outputVoltageAc = DecodeVoltage(tokens[2], tokens[3]);
        int loadPercentage = Math.Clamp(tokens[4].Length > 0 ? tokens[4][0] : 0, 0, 100);
        int activeLoadWatts = (int)Math.Round(
            (double)loadPercentage * VoltronicProtocolConstants.DefaultRatedWatts / 100.0,
            MidpointRounding.AwayFromZero);

        double frequencyHz = tokens.Count >= 7
            ? DecodeFrequencyHz(tokens[5], tokens[6])
            : VoltronicProtocolConstants.DefaultNominalFrequencyHz;

        int batteryHighByte = tokens[7].Length > 0 ? tokens[7][0] : 0;
        int batteryLowByte = tokens[8].Length > 0 ? tokens[8][0] : 0;
        double batteryVoltageDc = Math.Round(
            (batteryHighByte * batteryLowByte) / VoltronicProtocolConstants.BatteryVoltageDivisor,
            2);
        double cellVoltageDc = Math.Round(batteryVoltageDc / 2.0, 2);

        byte statusByte1 = tokens.Count >= 10 && tokens[9].Length > 0 ? tokens[9][0] : (byte)0;
        DateTimeOffset timestamp = now ?? DateTimeOffset.UtcNow;
        bool isTestInProgress = UpdateSelfTestLatch(statusByte1, timestamp);

        var (state, isBeeperMuted, _) = DecodeStatusBits(statusByte1, inputVoltageAc, outputVoltageAc);
        int batteryPercentage = _estimator.CalculateSoCPercentage(batteryVoltageDc);
        bool isHardwareLow = (statusByte1 & VoltronicProtocolConstants.StatusBatteryLowMask) != 0 &&
                             (statusByte1 & VoltronicProtocolConstants.StatusOnBatteryMask) != 0;
        int runtimeMinutes = _estimator.EstimateRuntimeMinutes(loadPercentage, _capacityFactor, isHardwareLow);

        return new UpsTelemetry(
            InputVoltageAc: inputVoltageAc,
            FaultReferenceVoltageAc: inputVoltageAc,
            OutputVoltageAc: outputVoltageAc,
            LoadPercentage: loadPercentage,
            ActiveLoadWatts: activeLoadWatts,
            FrequencyHz: frequencyHz,
            BatteryVoltageDc: batteryVoltageDc,
            Cell1VoltageDc: cellVoltageDc,
            Cell2VoltageDc: cellVoltageDc,
            BatteryPercentage: batteryPercentage,
            EstimatedRuntimeMinutes: runtimeMinutes,
            TemperatureCelsius: VoltronicProtocolConstants.DefaultAmbientTemperatureCelsius,
            State: state,
            IsBeeperMuted: isBeeperMuted,
            IsTestInProgress: isTestInProgress,
            BankSpec: new BatteryBankSpec(),
            Timestamp: timestamp);
    }

    /// <summary>
    /// Validates '#' header and '\r' terminator, then splits and de-escapes space-delimited Protocol 'T' tokens.
    /// </summary>
    public static bool TryTokenizeHashFrame(ReadOnlySpan<byte> rawFrame, out IReadOnlyList<byte[]> tokens)
    {
        List<byte[]> parsedTokens = new();
        tokens = parsedTokens;

        int startIndex = 0;
        while (startIndex < rawFrame.Length && rawFrame[startIndex] == VoltronicProtocolConstants.NullByte)
        {
            startIndex++;
        }

        if (startIndex >= rawFrame.Length || rawFrame[startIndex] != VoltronicProtocolConstants.FrameHeaderHash)
        {
            return false;
        }

        List<byte> currentToken = new();
        bool hasCarriageReturn = false;

        for (int index = startIndex + 1; index < rawFrame.Length; index++)
        {
            byte currentByte = rawFrame[index];
            if (currentByte == VoltronicProtocolConstants.CarriageReturn)
            {
                hasCarriageReturn = true;
                break;
            }

            if (currentByte == VoltronicProtocolConstants.SpaceDelimiter)
            {
                if (currentToken.Count > 0)
                {
                    parsedTokens.Add(currentToken.ToArray());
                    currentToken.Clear();
                }

                continue;
            }

            if (currentByte == VoltronicProtocolConstants.FrameHeaderParen && index + 1 < rawFrame.Length)
            {
                byte escapeCode = rawFrame[++index];
                currentToken.Add(DeescapeByte(escapeCode));
                continue;
            }

            currentToken.Add(currentByte);
        }

        if (currentToken.Count > 0)
        {
            parsedTokens.Add(currentToken.ToArray());
        }

        return hasCarriageReturn && parsedTokens.Count > 0;
    }

    public static byte[] Deescape(byte[] input)
    {
        List<byte> result = new();
        for (int index = 0; index < input.Length; index++)
        {
            if (input[index] == VoltronicProtocolConstants.FrameHeaderParen && index + 1 < input.Length)
            {
                byte escapeCode = input[++index];
                result.Add(DeescapeByte(escapeCode));
            }
            else
            {
                result.Add(input[index]);
            }
        }

        return result.ToArray();
    }

    /// <summary>
    /// Decodes an arbitrary-width (1 to 8 bytes) big-endian unsigned integer token.
    /// </summary>
    public static ulong DecodeBigEndianUInt64(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty || bytes.Length > MaxBigEndianByteWidth)
        {
            return 0;
        }

        ulong value = 0;
        foreach (byte currentByte in bytes)
        {
            value = (value << BitsPerByte) | currentByte;
        }

        return value;
    }

    /// <summary>
    /// Decodes grid frequency (Hz) from arbitrary-width T5 period ticks and T6 timer clock Hz.
    /// </summary>
    public static double DecodeFrequencyHz(
        ReadOnlySpan<byte> periodTicks,
        ReadOnlySpan<byte> timerClockHz,
        double fallbackHz = VoltronicProtocolConstants.DefaultNominalFrequencyHz)
    {
        ulong decodedPeriodTicks = DecodeBigEndianUInt64(periodTicks);
        ulong decodedTimerClockHz = DecodeBigEndianUInt64(timerClockHz);
        if (decodedPeriodTicks == 0 || decodedTimerClockHz == 0)
        {
            return fallbackHz;
        }

        double frequencyHz = Math.Round((double)decodedTimerClockHz / decodedPeriodTicks, 2);
        bool isWithinValidRange =
            frequencyHz >= VoltronicProtocolConstants.MinValidFrequencyHz &&
            frequencyHz <= VoltronicProtocolConstants.MaxValidFrequencyHz;

        return isWithinValidRange ? frequencyHz : fallbackHz;
    }

    /// <summary>
    /// Decodes nominal ratings bitmask from Protocol 'T' Status Byte 2 (token 10).
    /// </summary>
    public static UpsStatusByte2Ratings DecodeStatusByte2Ratings(byte statusByte2)
    {
        double nominalFrequencyHz = (statusByte2 & VoltronicProtocolConstants.StatusByte2Freq60HzMask) != 0
            ? NominalFrequency60Hz
            : NominalFrequency50Hz;

        int batteryBankSeriesCount =
            ((statusByte2 & VoltronicProtocolConstants.StatusByte2BatteryBankMask) >>
             VoltronicProtocolConstants.StatusByte2BatteryBankShift) + 1;

        double? nominalOutputVoltageAc = (statusByte2 & VoltronicProtocolConstants.StatusByte2NominalAcMask) switch
        {
            0 => 110.0,
            1 => 120.0,
            2 => 220.0,
            3 => 230.0,
            4 => 240.0,
            _ => null
        };

        return new UpsStatusByte2Ratings(
            nominalFrequencyHz,
            batteryBankSeriesCount,
            batteryBankSeriesCount * SingleBatteryBankVolts,
            nominalOutputVoltageAc);
    }

    public static double DecodeVoltage(byte[] hexVal, byte[] ratio)
    {
        if (hexVal == null || ratio == null || ratio.Length == 0)
        {
            return 0.0;
        }

        ulong rawValue = DecodeBigEndianUInt64(hexVal);
        return Math.Round((double)rawValue * ratio[0] / VoltronicProtocolConstants.VoltageDivisor, 1);
    }

    public static (UpsOperationalState State, bool IsBeeperMuted, bool IsTestInProgress) DecodeStatusBits(
        byte statusByte1,
        double inV = 120.0,
        double outV = 120.0)
    {
        UpsOperationalState state;
        if ((statusByte1 & VoltronicProtocolConstants.StatusFaultMask) != 0)
        {
            state = UpsOperationalState.Fault;
        }
        else if ((statusByte1 & VoltronicProtocolConstants.StatusBatteryLowMask) != 0 &&
                 (statusByte1 & VoltronicProtocolConstants.StatusOnBatteryMask) != 0)
        {
            state = UpsOperationalState.BatteryLow;
        }
        else if ((statusByte1 & VoltronicProtocolConstants.StatusOnBatteryMask) != 0)
        {
            state = UpsOperationalState.OnBattery;
        }
        else if ((statusByte1 & VoltronicProtocolConstants.StatusAvrBoostBuckMask) != 0 ||
                 (outV > 0 && Math.Abs(inV - outV) / outV > VoltronicProtocolConstants.MainsDivergenceThreshold))
        {
            state = UpsOperationalState.ChargingAvr;
        }
        else
        {
            state = UpsOperationalState.LineNormal;
        }

        bool isBeeperMuted = (statusByte1 & VoltronicProtocolConstants.StatusBeeperActiveMask) == 0;
        bool isTestInProgress = (statusByte1 & VoltronicProtocolConstants.StatusSelfTestActiveMask) != 0;
        return (state, isBeeperMuted, isTestInProgress);
    }

    public void ResetSelfTest()
    {
        _lastBit2 = false;
        _selfTestStartTime = null;
    }

    private bool UpdateSelfTestLatch(byte statusByte1, DateTimeOffset timestamp)
    {
        bool isSelfTestBitActive = (statusByte1 & VoltronicProtocolConstants.StatusSelfTestActiveMask) != 0;
        if (!isSelfTestBitActive)
        {
            _lastBit2 = false;
            _selfTestStartTime = null;
            return false;
        }

        if (!_lastBit2)
        {
            _selfTestStartTime = timestamp;
            _lastBit2 = true;
            return true;
        }

        return _selfTestStartTime.HasValue &&
               (timestamp - _selfTestStartTime.Value).TotalSeconds < VoltronicProtocolConstants.SelfTestDurationSeconds;
    }

    private static byte DeescapeByte(byte escapeCode)
    {
        return escapeCode switch
        {
            VoltronicProtocolConstants.EscapeCodeCr => VoltronicProtocolConstants.CarriageReturn,
            VoltronicProtocolConstants.EscapeCodeDc1 => VoltronicProtocolConstants.Dc1,
            VoltronicProtocolConstants.EscapeCodeDc3 => VoltronicProtocolConstants.Dc3,
            VoltronicProtocolConstants.EscapeCodeLf => VoltronicProtocolConstants.LineFeed,
            VoltronicProtocolConstants.EscapeCodeSpace => VoltronicProtocolConstants.SpaceDelimiter,
            _ => escapeCode
        };
    }
}
