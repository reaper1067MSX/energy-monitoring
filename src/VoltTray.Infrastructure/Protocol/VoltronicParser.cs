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
    private const int MinimumLoadPercentage = 0;
    private const int MaximumLoadPercentage = 100;
    private const double PercentageScaleDivisor = 100.0;
    private const double BatteryCellCount = 2.0;
    private const double SingleBatteryBankVolts = 12.0;
    private const double NominalFrequency50Hz = 50.0;
    private const double NominalFrequency60Hz = 60.0;
    private const double DefaultFallbackVoltageAc = 120.0;
    private const int FrequencyDecimalPrecision = 2;
    private const int VoltageDecimalPrecision = 1;
    private const int BatteryVoltageDecimalPrecision = 2;

    private const int NominalAcCode110V = 0;
    private const int NominalAcCode120V = 1;
    private const int NominalAcCode220V = 2;
    private const int NominalAcCode230V = 3;
    private const int NominalAcCode240V = 4;
    private const double NominalAc110Volts = 110.0;
    private const double NominalAc120Volts = 120.0;
    private const double NominalAc220Volts = 220.0;
    private const double NominalAc230Volts = 230.0;
    private const double NominalAc240Volts = 240.0;

    private readonly IBatteryAutonomyEstimator _estimator;
    private readonly double _capacityFactor;
    private bool _wasSelfTestBitActive;
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

        double inputVoltageAc = DecodeVoltage(
            tokens[VoltronicProtocolConstants.InputVoltageTokenIndex],
            tokens[VoltronicProtocolConstants.InputVoltageRatioTokenIndex]);
        double outputVoltageAc = DecodeVoltage(
            tokens[VoltronicProtocolConstants.OutputVoltageTokenIndex],
            tokens[VoltronicProtocolConstants.OutputVoltageRatioTokenIndex]);

        int rawLoad = tokens[VoltronicProtocolConstants.LoadPercentageTokenIndex].Length > 0
            ? tokens[VoltronicProtocolConstants.LoadPercentageTokenIndex][0]
            : MinimumLoadPercentage;
        int loadPercentage = Math.Clamp(rawLoad, MinimumLoadPercentage, MaximumLoadPercentage);

        int activeLoadWatts = (int)Math.Round(
            (double)loadPercentage * VoltronicProtocolConstants.DefaultRatedWatts / PercentageScaleDivisor,
            MidpointRounding.AwayFromZero);

        double frequencyHz = tokens.Count >= VoltronicProtocolConstants.MinFrequencyTokenCount
            ? DecodeFrequencyHz(
                tokens[VoltronicProtocolConstants.FrequencyPeriodTokenIndex],
                tokens[VoltronicProtocolConstants.FrequencyClockTokenIndex])
            : VoltronicProtocolConstants.DefaultNominalFrequencyHz;

        int batteryVoltageHighByte = tokens[VoltronicProtocolConstants.BatteryVoltageHighTokenIndex].Length > 0
            ? tokens[VoltronicProtocolConstants.BatteryVoltageHighTokenIndex][0]
            : 0;
        int batteryVoltageLowByte = tokens[VoltronicProtocolConstants.BatteryVoltageLowTokenIndex].Length > 0
            ? tokens[VoltronicProtocolConstants.BatteryVoltageLowTokenIndex][0]
            : 0;
        double batteryVoltageDc = Math.Round(
            (batteryVoltageHighByte * batteryVoltageLowByte) / VoltronicProtocolConstants.BatteryVoltageDivisor,
            BatteryVoltageDecimalPrecision);
        double cellVoltageDc = Math.Round(batteryVoltageDc / BatteryCellCount, BatteryVoltageDecimalPrecision);

        byte statusByte1 = tokens.Count >= VoltronicProtocolConstants.MinStatusByte1TokenCount &&
                           tokens[VoltronicProtocolConstants.StatusByte1TokenIndex].Length > 0
            ? tokens[VoltronicProtocolConstants.StatusByte1TokenIndex][0]
            : (byte)0;
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
    /// Decodes grid frequency (Hz) from T5 period ticks and T6 timer clock Hz, returning null if empty, zero, or out of range.
    /// </summary>
    public static double? TryDecodeFrequencyHz(
        ReadOnlySpan<byte> periodTicksToken,
        ReadOnlySpan<byte> timerClockToken)
    {
        if (periodTicksToken.IsEmpty || timerClockToken.IsEmpty)
        {
            return null;
        }

        ulong decodedPeriodTicks = DecodeBigEndianUInt64(periodTicksToken);
        ulong decodedTimerClockHz = DecodeBigEndianUInt64(timerClockToken);
        if (decodedPeriodTicks == 0 || decodedTimerClockHz == 0)
        {
            return null;
        }

        double frequencyHz = Math.Round(
            (double)decodedTimerClockHz / decodedPeriodTicks,
            FrequencyDecimalPrecision);

        bool isWithinValidRange =
            frequencyHz >= VoltronicProtocolConstants.MinValidFrequencyHz &&
            frequencyHz <= VoltronicProtocolConstants.MaxValidFrequencyHz;

        return isWithinValidRange ? frequencyHz : null;
    }

    /// <summary>
    /// Decodes grid frequency (Hz) from arbitrary-width T5 period ticks and T6 timer clock Hz, falling back on invalid tokens.
    /// </summary>
    public static double DecodeFrequencyHz(
        ReadOnlySpan<byte> periodTicks,
        ReadOnlySpan<byte> timerClockHz,
        double fallbackHz = VoltronicProtocolConstants.DefaultNominalFrequencyHz)
    {
        return TryDecodeFrequencyHz(periodTicks, timerClockHz) ?? fallbackHz;
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
            NominalAcCode110V => NominalAc110Volts,
            NominalAcCode120V => NominalAc120Volts,
            NominalAcCode220V => NominalAc220Volts,
            NominalAcCode230V => NominalAc230Volts,
            NominalAcCode240V => NominalAc240Volts,
            _ => null
        };

        return new UpsStatusByte2Ratings(
            nominalFrequencyHz,
            batteryBankSeriesCount,
            batteryBankSeriesCount * SingleBatteryBankVolts,
            nominalOutputVoltageAc);
    }

    public static double DecodeVoltage(byte[] decodedVoltageRawCount, byte[] ratio)
    {
        if (decodedVoltageRawCount == null || ratio == null || ratio.Length == 0)
        {
            return 0.0;
        }

        ulong rawValue = DecodeBigEndianUInt64(decodedVoltageRawCount);
        return Math.Round(
            (double)rawValue * ratio[0] / VoltronicProtocolConstants.VoltageDivisor,
            VoltageDecimalPrecision);
    }

    public static (UpsOperationalState State, bool IsBeeperMuted, bool IsTestInProgress) DecodeStatusBits(
        byte statusByte1,
        double inputVoltageAc = DefaultFallbackVoltageAc,
        double outputVoltageAc = DefaultFallbackVoltageAc)
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
                 (outputVoltageAc > 0 &&
                  Math.Abs(inputVoltageAc - outputVoltageAc) / outputVoltageAc > VoltronicProtocolConstants.MainsDivergenceThreshold))
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
        _wasSelfTestBitActive = false;
        _selfTestStartTime = null;
    }

    private bool UpdateSelfTestLatch(byte statusByte1, DateTimeOffset timestamp)
    {
        bool isSelfTestBitActive = (statusByte1 & VoltronicProtocolConstants.StatusSelfTestActiveMask) != 0;
        if (!isSelfTestBitActive)
        {
            _wasSelfTestBitActive = false;
            _selfTestStartTime = null;
            return false;
        }

        if (!_wasSelfTestBitActive)
        {
            _selfTestStartTime = timestamp;
            _wasSelfTestBitActive = true;
            return true;
        }

        return _selfTestStartTime.HasValue &&
               (timestamp - _selfTestStartTime.Value).TotalSeconds < VoltronicProtocolConstants.SelfTestMaxDurationSeconds;
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
