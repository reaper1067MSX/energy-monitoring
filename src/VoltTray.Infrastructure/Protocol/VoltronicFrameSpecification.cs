namespace VoltTray.Infrastructure.Protocol;

/// <summary>
/// Immutable specification describing every token field of a Voltronic Protocol 'T' (QS-Hex) binary frame.
/// </summary>
public sealed record VoltronicFrameSpecification
{
    public byte[] InputVoltageBytes { get; init; } =
    [
        VoltronicProtocolConstants.NominalInputVoltageHexHigh,
        VoltronicProtocolConstants.NominalInputVoltageHexLow
    ];

    public byte InputVoltageRatio { get; init; } = VoltronicProtocolConstants.NominalInputVoltageRatio;

    public byte[] OutputVoltageBytes { get; init; } =
    [
        VoltronicProtocolConstants.NominalOutputVoltageHexHigh,
        VoltronicProtocolConstants.NominalOutputVoltageHexLow
    ];

    public byte OutputVoltageRatio { get; init; } = VoltronicProtocolConstants.NominalOutputVoltageRatio;

    public byte LoadPercentage { get; init; } = VoltronicProtocolConstants.NominalLoadPercentage;

    public byte[] FrequencyPeriodTicksBytes { get; init; } =
    [
        VoltronicProtocolConstants.NominalFrequencyPart1High,
        VoltronicProtocolConstants.NominalFrequencyPart1Low
    ];

    public byte[] FrequencyTimerClockBytes { get; init; } =
    [
        VoltronicProtocolConstants.NominalFrequencyPart2High,
        VoltronicProtocolConstants.NominalFrequencyPart2Low
    ];

    public byte BatteryVoltageHighByte { get; init; } = VoltronicProtocolConstants.NominalBatteryVoltagePart1;

    public byte BatteryVoltageLowByte { get; init; } = VoltronicProtocolConstants.NominalBatteryVoltagePart2;

    public byte StatusByte1 { get; init; } = VoltronicProtocolConstants.StatusLineNormal;

    public byte StatusByte2 { get; init; } = VoltronicProtocolConstants.StatusByte2Nominal;
}
