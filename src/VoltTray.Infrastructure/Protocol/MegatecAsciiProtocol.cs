using System.Globalization;
using System.Text;
using VoltTray.Core.Models;
using VoltTray.Core.Ports;
using VoltTray.Core.Services;
using VoltTray.Shared;

namespace VoltTray.Infrastructure.Protocol;

/// <summary>
/// Megatec ASCII protocol strategy supporting dialect 'V' (QS\r -> (...)) and 'Q1' (Q1\r -> (...)),
/// including F\r / I\r capability probing and qx_multiply_battvolt per-cell voltage scaling.
/// </summary>
public sealed class MegatecAsciiProtocol : IUpsProtocolStrategy
{
    private const int InputVoltageTokenIndex = 0;
    private const int FaultVoltageTokenIndex = 1;
    private const int OutputVoltageTokenIndex = 2;
    private const int LoadPercentageTokenIndex = 3;
    private const int FrequencyTokenIndex = 4;
    private const int BatteryVoltageTokenIndex = 5;
    private const int TemperatureTokenIndex = 6;
    private const int StatusFlagsTokenIndex = 7;

    private const int NominalAcTokenIndex = 0;
    private const int NominalDcTokenIndex = 2;
    private const int NominalFreqTokenIndex = 3;

    private readonly IBatteryAutonomyEstimator _estimator;
    private readonly double _capacityFactor;
    private readonly SelfTestLatch _selfTestLatch = new();

    public string DialectId { get; }
    public string StatusQueryCommand { get; }

    public MegatecAsciiProtocol(
        string dialectId,
        string statusQueryCommand,
        IBatteryAutonomyEstimator? estimator = null,
        double capacityFactor = 1.0)
    {
        if (dialectId != VoltronicProtocolConstants.DialectV &&
            dialectId != VoltronicProtocolConstants.DialectQ1)
        {
            throw new ArgumentException($"Unsupported Megatec ASCII dialect '{dialectId}'.", nameof(dialectId));
        }

        DialectId = dialectId;
        StatusQueryCommand = statusQueryCommand;
        _estimator = estimator ?? new PeukertEstimator();
        _capacityFactor = capacityFactor;
    }

    public UpsTelemetry? Parse(
        ReadOnlySpan<byte> frame,
        UpsCapabilities capabilities,
        UpsTelemetry? previous,
        DateTimeOffset now)
    {
        if (!TryExtractAsciiBody(frame, VoltronicProtocolConstants.FrameHeaderParen, out string body))
        {
            return previous;
        }

        string[] tokens = body.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < VoltronicProtocolConstants.MegatecStatusTokenCount)
        {
            return previous;
        }

        if (!double.TryParse(tokens[InputVoltageTokenIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out double inputVoltageAc) ||
            !double.TryParse(tokens[FaultVoltageTokenIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out double faultVoltageAc) ||
            !double.TryParse(tokens[OutputVoltageTokenIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out double outputVoltageAc) ||
            !int.TryParse(tokens[LoadPercentageTokenIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out int rawLoad) ||
            !double.TryParse(tokens[FrequencyTokenIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out double frequencyHz) ||
            !double.TryParse(tokens[BatteryVoltageTokenIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedBatteryVoltage) ||
            !TryParseStatusFlagsByte(tokens[StatusFlagsTokenIndex], out byte statusByte1))
        {
            return previous;
        }

        double? temperatureCelsius = double.TryParse(
            tokens[TemperatureTokenIndex],
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out double parsedTemperature)
            ? parsedTemperature
            : null;

        double scaledBatteryVoltageDc = ApplyPerCellMultiplierIfNeeded(parsedBatteryVoltage, capabilities.Bank.NominalVoltageDc);
        double cellVoltageDc = Math.Round(
            scaledBatteryVoltageDc / ElectricalConstants.NominalLeadAcidCellVolts,
            ElectricalConstants.BatteryVoltageDecimalPrecision);
        int loadPercentage = Math.Clamp(
            rawLoad,
            ElectricalConstants.MinimumLoadPercentage,
            ElectricalConstants.MaximumLoadPercentage);
        int? activeLoadWatts = capabilities.RatedWatts is int ratedWatts
            ? (int)Math.Round(
                (double)loadPercentage * ratedWatts / ElectricalConstants.PercentageScaleDivisor,
                MidpointRounding.AwayFromZero)
            : null;

        PowerStatus powerStatus = VoltronicStatusMapper.DecodePowerStatus(statusByte1, inputVoltageAc, outputVoltageAc);
        var (state, isBeeperMuted, _) = VoltronicParser.DecodeStatusBits(statusByte1, inputVoltageAc, outputVoltageAc);
        bool isTestInProgress = _selfTestLatch.Update(powerStatus.IsSelfTestActive, now);
        int batteryPercentage = _estimator.CalculateSoCPercentage(scaledBatteryVoltageDc, capabilities.Bank.SeriesBatteryCount);
        bool isHardwareLow = powerStatus.IsBatteryLow && powerStatus.IsOnBattery;
        int? runtimeMinutes = _estimator.EstimateRuntimeMinutes(loadPercentage, capabilities, _capacityFactor, isHardwareLow);

        return new UpsTelemetry(
            InputVoltageAc: inputVoltageAc,
            FaultReferenceVoltageAc: faultVoltageAc,
            OutputVoltageAc: outputVoltageAc,
            LoadPercentage: loadPercentage,
            ActiveLoadWatts: activeLoadWatts,
            FrequencyHz: frequencyHz,
            BatteryVoltageDc: scaledBatteryVoltageDc,
            Cell1VoltageDc: cellVoltageDc,
            Cell2VoltageDc: cellVoltageDc,
            BatteryPercentage: batteryPercentage,
            EstimatedRuntimeMinutes: runtimeMinutes,
            TemperatureCelsius: temperatureCelsius,
            State: state,
            IsBeeperMuted: isBeeperMuted,
            IsTestInProgress: isTestInProgress,
            BankSpec: capabilities.Bank,
            Timestamp: now,
            PowerStatus: powerStatus);
    }

    public void ResetSelfTest()
    {
        _selfTestLatch.Reset();
    }

    public static bool IsValidMegatecStatusFrame(ReadOnlySpan<byte> frame)
    {
        if (!TryExtractAsciiBody(frame, VoltronicProtocolConstants.FrameHeaderParen, out string body))
        {
            return false;
        }

        string[] tokens = body.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length >= VoltronicProtocolConstants.MegatecStatusTokenCount &&
               TryParseStatusFlagsByte(tokens[StatusFlagsTokenIndex], out _);
    }

    public static bool TryParseRatingsResponse(
        ReadOnlySpan<byte> response,
        UpsCapabilities currentCapabilities,
        out UpsCapabilities updatedCapabilities)
    {
        updatedCapabilities = currentCapabilities;
        if (!TryExtractAsciiBody(response, VoltronicProtocolConstants.FrameHeaderHash, out string body))
        {
            return false;
        }

        string[] tokens = body.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < VoltronicProtocolConstants.RatingResponseMinTokenCount ||
            !double.TryParse(tokens[NominalAcTokenIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out double nominalAc) ||
            !double.TryParse(tokens[NominalDcTokenIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out double nominalDc) ||
            !double.TryParse(tokens[NominalFreqTokenIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out double nominalFrequencyHz))
        {
            return false;
        }

        int seriesCount = nominalDc > 0
            ? Math.Max(1, (int)Math.Round(nominalDc / ElectricalConstants.NominalBlockVoltageDc, MidpointRounding.AwayFromZero))
            : currentCapabilities.Bank.SeriesBatteryCount;

        BatteryBankSpec updatedBank = nominalDc > 0
            ? currentCapabilities.Bank with { SeriesBatteryCount = seriesCount, NominalVoltageDc = nominalDc }
            : currentCapabilities.Bank;

        updatedCapabilities = currentCapabilities with
        {
            NominalVoltageAc = nominalAc,
            NominalFrequencyHz = nominalFrequencyHz,
            Bank = updatedBank
        };
        return true;
    }

    public static bool TryParseVendorInfoResponse(
        ReadOnlySpan<byte> response,
        UpsCapabilities currentCapabilities,
        out UpsCapabilities updatedCapabilities)
    {
        updatedCapabilities = currentCapabilities;
        if (!TryExtractAsciiBody(response, VoltronicProtocolConstants.FrameHeaderHash, out string body))
        {
            return false;
        }

        string[] parts = body.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        updatedCapabilities = currentCapabilities with { ModelName = string.Join(" ", parts) };
        return true;
    }

    private static double ApplyPerCellMultiplierIfNeeded(double batteryVoltageDc, double nominalBankVoltageDc)
    {
        if (batteryVoltageDc > 0 &&
            batteryVoltageDc <= VoltronicProtocolConstants.PerCellVoltageThresholdDc &&
            nominalBankVoltageDc > 0)
        {
            double multiplier = nominalBankVoltageDc / ElectricalConstants.NominalLeadAcidCellVolts;
            return Math.Round(batteryVoltageDc * multiplier, ElectricalConstants.BatteryVoltageDecimalPrecision);
        }

        return Math.Round(batteryVoltageDc, ElectricalConstants.BatteryVoltageDecimalPrecision);
    }

    private static bool TryExtractAsciiBody(ReadOnlySpan<byte> frame, byte expectedPrefix, out string body)
    {
        body = string.Empty;
        int startIndex = 0;
        while (startIndex < frame.Length && frame[startIndex] == VoltronicProtocolConstants.NullByte)
        {
            startIndex++;
        }

        if (startIndex >= frame.Length || frame[startIndex] != expectedPrefix)
        {
            return false;
        }

        int crIndex = frame[startIndex..].IndexOf(VoltronicProtocolConstants.CarriageReturn);
        if (crIndex <= 1)
        {
            return false;
        }

        body = Encoding.ASCII.GetString(frame.Slice(startIndex + 1, crIndex - 1));
        return true;
    }

    private static bool TryParseStatusFlagsByte(string statusFlagsToken, out byte statusByte1)
    {
        statusByte1 = 0;
        if (statusFlagsToken.Length != VoltronicProtocolConstants.MegatecStatusFlagsLength)
        {
            return false;
        }

        for (int index = 0; index < statusFlagsToken.Length; index++)
        {
            char flagChar = statusFlagsToken[index];
            if (flagChar != '0' && flagChar != '1')
            {
                return false;
            }

            if (flagChar == '1')
            {
                statusByte1 |= (byte)(1 << (VoltronicProtocolConstants.MegatecStatusFlagsLength - 1 - index));
            }
        }

        return true;
    }
}
