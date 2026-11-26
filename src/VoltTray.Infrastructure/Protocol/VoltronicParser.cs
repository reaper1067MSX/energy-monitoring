using VoltTray.Core.Battery;
using VoltTray.Core.Telemetry;
using System;
using System.Collections.Generic;
using VoltTray.Shared;

namespace VoltTray.Infrastructure.Protocol;

public sealed class VoltronicParser
{
    private static readonly double[] NominalAcVoltagesByCode = [110.0, 120.0, 220.0, 230.0, 240.0];

    private readonly IBatteryAutonomyEstimator _estimator;
    private readonly double _capacityFactor;
    private readonly SelfTestLatch _selfTestLatch = new();

    public VoltronicParser(IBatteryAutonomyEstimator? estimator = null, double capacityFactor = 1.0)
    {
        _estimator = estimator ?? new PeukertEstimator();
        _capacityFactor = capacityFactor;
    }

    public UpsTelemetry? Parse(byte[]? raw, UpsTelemetry? previous = null, DateTimeOffset? now = null)
    {
        if (raw == null)
        {
            return previous;
        }

        return Parse(raw.AsSpan(), capabilities: null, previous, now);
    }

    public UpsTelemetry? Parse(
        ReadOnlySpan<byte> raw,
        UpsCapabilities? capabilities,
        UpsTelemetry? previous = null,
        DateTimeOffset? now = null)
    {
        if (raw.Length < VoltronicProtocolConstants.MinRawFrameLength)
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
            : ElectricalConstants.MinimumLoadPercentage;
        int loadPercentage = Math.Clamp(
            rawLoad,
            ElectricalConstants.MinimumLoadPercentage,
            ElectricalConstants.MaximumLoadPercentage);

        double frequencyHz = tokens.Count >= VoltronicProtocolConstants.MinFrequencyTokenCount
            ? DecodeFrequencyHz(
                tokens[VoltronicProtocolConstants.FrequencyPeriodTokenIndex],
                tokens[VoltronicProtocolConstants.FrequencyClockTokenIndex])
            : ElectricalConstants.NominalFrequency60Hz;

        int batteryVoltageHighByte = tokens[VoltronicProtocolConstants.BatteryVoltageHighTokenIndex].Length > 0
            ? tokens[VoltronicProtocolConstants.BatteryVoltageHighTokenIndex][0]
            : 0;
        int batteryVoltageLowByte = tokens[VoltronicProtocolConstants.BatteryVoltageLowTokenIndex].Length > 0
            ? tokens[VoltronicProtocolConstants.BatteryVoltageLowTokenIndex][0]
            : 0;
        double batteryVoltageDc = Math.Round(
            (batteryVoltageHighByte * batteryVoltageLowByte) / VoltronicProtocolConstants.BatteryVoltageDivisor,
            ElectricalConstants.BatteryVoltageDecimalPrecision);
        double cellVoltageDc = Math.Round(
            batteryVoltageDc / ElectricalConstants.NominalLeadAcidCellVolts,
            ElectricalConstants.BatteryVoltageDecimalPrecision);

        byte statusByte1 = tokens.Count >= VoltronicProtocolConstants.MinStatusByte1TokenCount &&
                           tokens[VoltronicProtocolConstants.StatusByte1TokenIndex].Length > 0
            ? tokens[VoltronicProtocolConstants.StatusByte1TokenIndex][0]
            : (byte)0;
        DateTimeOffset timestamp = now ?? DateTimeOffset.UtcNow;
        bool isTestInProgress = _selfTestLatch.Update(
            (statusByte1 & VoltronicProtocolConstants.StatusSelfTestActiveMask) != 0,
            timestamp);

        UpsCapabilities effectiveCapabilities = capabilities ?? new UpsCapabilities(
            ModelName: null,
            NominalVoltageAc: null,
            NominalFrequencyHz: null,
            RatedWatts: (int)VoltronicProtocolConstants.DefaultRatedWatts,
            Bank: new BatteryBankSpec(),
            DialectVerified: false);

        if (tokens.Count >= VoltronicProtocolConstants.MinStatusByte2TokenCount &&
            tokens[VoltronicProtocolConstants.StatusByte2TokenIndex].Length > 0)
        {
            effectiveCapabilities = VoltronicStatusMapper.RefineCapabilitiesFromStatusByte2(
                effectiveCapabilities,
                tokens[VoltronicProtocolConstants.StatusByte2TokenIndex][0]);
        }

        int? activeLoadWatts = effectiveCapabilities.RatedWatts is int ratedWatts
            ? (int)Math.Round(
                (double)loadPercentage * ratedWatts / ElectricalConstants.PercentageScaleDivisor,
                MidpointRounding.AwayFromZero)
            : null;

        PowerStatus powerStatus = VoltronicStatusMapper.DecodePowerStatus(statusByte1, inputVoltageAc, outputVoltageAc);
        var (state, isBeeperMuted, _) = DecodeStatusBits(statusByte1, inputVoltageAc, outputVoltageAc);
        int batteryPercentage = _estimator.CalculateSoCPercentage(batteryVoltageDc, effectiveCapabilities.Bank.SeriesBatteryCount);
        bool isHardwareLow = powerStatus.IsBatteryLow && powerStatus.IsOnBattery;
        int? runtimeMinutes = _estimator.EstimateRuntimeMinutes(loadPercentage, effectiveCapabilities, _capacityFactor, isHardwareLow);

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
            TemperatureCelsius: null,
            State: state,
            IsBeeperMuted: isBeeperMuted,
            IsTestInProgress: isTestInProgress,
            BankSpec: effectiveCapabilities.Bank,
            Timestamp: timestamp,
            PowerStatus: powerStatus);
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
        if (bytes.IsEmpty || bytes.Length > ElectricalConstants.MaxBigEndianByteWidth)
        {
            return 0;
        }

        ulong value = 0;
        foreach (byte currentByte in bytes)
        {
            value = (value << ElectricalConstants.BitsPerByte) | currentByte;
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
            ElectricalConstants.FrequencyDecimalPrecision);

        bool isWithinValidRange =
            frequencyHz >= ElectricalConstants.MinPlausibleFrequencyHz &&
            frequencyHz <= ElectricalConstants.MaxPlausibleFrequencyHz;

        return isWithinValidRange ? frequencyHz : null;
    }

    /// <summary>
    /// Decodes grid frequency (Hz) from arbitrary-width T5 period ticks and T6 timer clock Hz, falling back on invalid tokens.
    /// </summary>
    public static double DecodeFrequencyHz(
        ReadOnlySpan<byte> periodTicks,
        ReadOnlySpan<byte> timerClockHz,
        double fallbackHz = ElectricalConstants.NominalFrequency60Hz)
    {
        return TryDecodeFrequencyHz(periodTicks, timerClockHz) ?? fallbackHz;
    }

    /// <summary>
    /// Decodes nominal ratings bitmask from Protocol 'T' Status Byte 2 (token 10).
    /// </summary>
    public static UpsStatusByte2Ratings DecodeStatusByte2Ratings(byte statusByte2)
    {
        double nominalFrequencyHz = (statusByte2 & VoltronicProtocolConstants.StatusByte2Freq60HzMask) != 0
            ? ElectricalConstants.NominalFrequency60Hz
            : ElectricalConstants.NominalFrequency50Hz;

        int batteryBankSeriesCount =
            ((statusByte2 & VoltronicProtocolConstants.StatusByte2BatteryBankMask) >>
             VoltronicProtocolConstants.StatusByte2BatteryBankShift) + 1;

        int nominalAcCode = statusByte2 & VoltronicProtocolConstants.StatusByte2NominalAcMask;
        double? nominalOutputVoltageAc =
            nominalAcCode >= 0 && nominalAcCode < NominalAcVoltagesByCode.Length
                ? NominalAcVoltagesByCode[nominalAcCode]
                : null;

        return new UpsStatusByte2Ratings(
            nominalFrequencyHz,
            batteryBankSeriesCount,
            batteryBankSeriesCount * ElectricalConstants.NominalBlockVoltageDc,
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
            ElectricalConstants.VoltageDecimalPrecision);
    }

    public static (UpsOperationalState State, bool IsBeeperMuted, bool IsTestInProgress) DecodeStatusBits(
        byte statusByte1,
        double inputVoltageAc = ElectricalConstants.DefaultFallbackVoltageAc,
        double outputVoltageAc = ElectricalConstants.DefaultFallbackVoltageAc)
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
        _selfTestLatch.Reset();
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
