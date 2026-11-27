namespace VoltTray.Infrastructure.Protocol;

/// <summary>
/// Centralized pure protocol constants for Voltronic Protocol 'T' (QS-Hex) and Megatec ASCII dialects.
/// </summary>
public static class VoltronicProtocolConstants
{
    // Framing & Delimiter Bytes
    public const byte FrameHeaderHash = 0x23;
    public const byte FrameHeaderParen = 0x28;
    public const byte CarriageReturn = 0x0D;
    public const byte SpaceDelimiter = 0x20;
    public const byte LineFeed = 0x0A;
    public const byte Dc1 = 0x11;
    public const byte Dc3 = 0x13;
    public const byte NullByte = 0x00;
    public const byte NakHeader = 0x4E;

    // Framing & Delimiter Characters
    public const char CarriageReturnChar = '\r';

    // Escape Codes (De-escape mapping following 0x28)
    public const byte EscapeCodeCr = 0x00;
    public const byte EscapeCodeDc1 = 0x01;
    public const byte EscapeCodeDc3 = 0x02;
    public const byte EscapeCodeLf = 0x03;
    public const byte EscapeCodeSpace = 0x04;

    // Protocol 'T' Token Indices & Minimum Token Counts
    public const int InputVoltageTokenIndex = 0;
    public const int InputVoltageRatioTokenIndex = 1;
    public const int OutputVoltageTokenIndex = 2;
    public const int OutputVoltageRatioTokenIndex = 3;
    public const int LoadPercentageTokenIndex = 4;
    public const int FrequencyPeriodTokenIndex = 5;
    public const int FrequencyClockTokenIndex = 6;
    public const int MinFrequencyTokenCount = 7;
    public const int BatteryVoltageHighTokenIndex = 7;
    public const int BatteryVoltageLowTokenIndex = 8;
    public const int StatusByte1TokenIndex = 9;
    public const int MinStatusByte1TokenCount = 10;
    public const int StatusByte2TokenIndex = 10;
    public const int MinStatusByte2TokenCount = 11;

    // Status Byte 1 Bit Masks (Bit 0..7)
    public const byte StatusBeeperActiveMask = 0x01;
    public const byte StatusShutdownActiveMask = 0x02;
    public const byte StatusSelfTestActiveMask = 0x04;
    public const byte StatusLineInteractiveMask = 0x08;
    public const byte StatusFaultMask = 0x10;
    public const byte StatusAvrBoostBuckMask = 0x20;
    public const byte StatusBatteryLowMask = 0x40;
    public const byte StatusOnBatteryMask = 0x80;
    public const byte StatusLineNormal = StatusLineInteractiveMask;

    // Protocol Dialect Identifiers
    public const string DialectT = "T";
    public const string DialectP = "P";
    public const string DialectV = "V";
    public const string DialectQ1 = "Q1";
    public const char DialectCharT = 'T';
    public const char DialectCharP = 'P';
    public const char DialectCharV = 'V';
    public const byte LowByteMask = 0xFF;
    public const int DefaultFrameBufferCapacity = 32;

    // Megatec ASCII Constants
    public const int MegatecStatusTokenCount = 8;
    public const int MegatecStatusFlagsLength = 8;
    public const int RatingResponseMinTokenCount = 4;
    public const double PerCellVoltageThresholdDc = 3.0;

    // Status Byte 2 Masks & Nominal Value
    public const byte StatusByte2Nominal = 0xA1;
    public const byte StatusByte2Freq60HzMask = 0x80;
    public const byte StatusByte2BatteryBankMask = 0x60;
    public const int StatusByte2BatteryBankShift = 5;
    public const byte StatusByte2NominalAcMask = 0x07;

    // Read-Only Wire Inquiry Commands
    public const string ProtocolIdentityCommand = "M\r";
    public const string QueryStatusCommand = "QS\r";
    public const string StandardAsciiStatusCommand = "Q1\r";
    public const string ProtocolIdCommand = "QPI\r";
    public const string DeviceIdCommand = "QID\r";
    public const string RatedInformationCommand = "QRI\r";
    public const string ModelDescriptorCommand = "QMD\r";
    public const string FirmwareVersionCommand = "QVFW\r";
    public const string BatteryStatusCommand = "QBV\r";
    public const string GeneralStatusCommand = "QGS\r";
    public const string OperatingModeCommand = "QMOD\r";
    public const string FeatureFlagsCommand = "QFLAG\r";
    public const string NominalRatingsCommand = "F\r";
    public const string VendorInformationCommand = "I\r";

    // Production Control Commands & Framing Prefixes
    public const string QueryCommandPrefix = "Q";
    public const string SelfTestCommand = "T\r";
    public const string BeeperToggleCommand = "Q\r";

    // Frame Validation & Parsing Limits
    public const int MinRawFrameLength = 3;
    public const int MinTokenCount = 9;

    // Nominal Physical & Scaling Constants
    public const double VoltageDivisor = 51.0 * 256.0;
    public const double BatteryVoltageDivisor = 510.0;
    public const double DefaultRatedWatts = 1200.0;
    public const double MainsDivergenceThreshold = 0.05;

    // Default Simulation Baseline Frame Bytes (Forza FX-2200 Profile)
    public const byte NominalInputVoltageHexHigh = 0x73;
    public const byte NominalInputVoltageHexLow = 0x01;
    public const byte NominalInputVoltageRatio = 0x34;
    public const byte NominalOutputVoltageHexHigh = 0x73;
    public const byte NominalOutputVoltageHexLow = 0x01;
    public const byte NominalOutputVoltageRatio = 0x34;
    public const byte NominalLoadPercentage = 14;
    public const byte NominalFrequencyPart1High = 0x50;
    public const byte NominalFrequencyPart1Low = 0x0C;
    public const byte NominalFrequencyPart2High = 0x12;
    public const byte NominalFrequencyPart2Low = 0xC0;
    public const byte NominalBatteryVoltagePart1 = 0xE9;
    public const byte NominalBatteryVoltagePart2 = 0x3C;
}
