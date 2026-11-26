namespace VoltTray.Infrastructure.Protocol;

/// <summary>
/// Centralized pure protocol and framing constants for Voltronic Taurus Protocol 'T' (QS-Hex) and related dialects.
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
    public const byte NakLetterA = 0x41;
    public const byte NakLetterK = 0x4B;
    public const byte EscapePrefix = FrameHeaderParen;

    // Framing & Delimiter Characters
    public const char FrameHeaderHashChar = '#';
    public const char FrameHeaderParenChar = '(';
    public const char CarriageReturnChar = '\r';
    public const char SpaceDelimiterChar = ' ';

    // Escape Codes (De-escape mapping following 0x28)
    public const byte EscapeCodeCr = 0x00;
    public const byte EscapeCodeDc1 = 0x01;
    public const byte EscapeCodeDc3 = 0x02;
    public const byte EscapeCodeLf = 0x03;
    public const byte EscapeCodeSpace = 0x04;

    // Status Byte 1 Bit Masks (Bit 0..7)
    public const byte StatusBeeperActiveMask = 0x01;
    public const byte StatusSelfTestActiveMask = 0x04;
    public const byte StatusLineInteractiveMask = 0x08;
    public const byte StatusFaultMask = 0x10;
    public const byte StatusAvrBoostBuckMask = 0x20;
    public const byte StatusBatteryLowMask = 0x40;
    public const byte StatusOnBatteryMask = 0x80;

    // Common Pre-composed Status Byte 1 Values
    public const byte StatusLineNormal = StatusLineInteractiveMask;
    public const byte StatusSelfTest = StatusLineInteractiveMask | StatusSelfTestActiveMask;
    public const byte Nt2401ProStatusByte1BeeperOn = StatusLineInteractiveMask | StatusBeeperActiveMask;
    public const byte StatusOnBattery = StatusOnBatteryMask;
    public const byte StatusBatteryLow = StatusOnBatteryMask | StatusBatteryLowMask;
    public const byte StatusAvr = StatusLineInteractiveMask | StatusAvrBoostBuckMask;

    // Status Byte 2 Values & Masks
    public const byte StatusByte2Nominal = 0xA1;
    public const byte StatusByte2European12V50Hz220V = 0x02;
    public const byte StatusByte2Nominal48V50Hz230V = 0x63;
    public const byte StatusByte2Nominal36V50HzUnknownAc = 0x47;
    public const byte StatusByte2Freq60HzMask = 0x80;
    public const byte StatusByte2BatteryBankMask = 0x60;
    public const int StatusByte2BatteryBankShift = 5;
    public const byte StatusByte2NominalAcMask = 0x07;

    // Read-Only Wire Inquiry Commands
    public const string ProtocolIdentityCommand = "M\r";
    public const string QueryStatusCommand = "QS\r";
    public const string MegatecStatusCommand = "Q1\r";
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

    // Mutating / Control Wire Commands & Prefixes
    public const string QueryCommandPrefix = "Q";
    public const string QueryStatusCommandPrefix = "QS";
    public const string SelfTestCommand = "T\r";
    public const string SelfTestCommandPrefix = "T";
    public const string BeeperToggleCommand = "Q\r";
    public const string CancelSelfTestCommand = "CT\r";
    public const string LongSelfTestCommand = "TL\r";
    public const string ShutdownRestorePrefix = "S";
    public const string CalibrationCommand = "C\r";

    // Frame Validation & Parsing Limits
    public const int MinRawFrameLength = 3;
    public const int MinTokenCount = 9;

    // Nominal Physical & Scaling Constants
    public const double VoltageDivisor = 51.0 * 256.0;
    public const double BatteryVoltageDivisor = 510.0;
    public const double DefaultRatedWatts = 900.0;
    public const double SelfTestDurationSeconds = 15.0;
    public const double MainsDivergenceThreshold = 0.05;
    public const double DefaultNominalFrequencyHz = 60.0;
    public const double MinValidFrequencyHz = 45.0;
    public const double MaxValidFrequencyHz = 70.0;
    public const double DefaultAmbientTemperatureCelsius = 25.0;

    // Nominal Taurus Protocol 'T' (FX-2200) & NT-2401PRO Binary Frame Presets
    public const byte NominalInputVoltageHexHigh = 0x73;
    public const byte NominalInputVoltageHexLow = 0x01;
    public const byte NominalInputVoltageRatio = 0x34;
    public const byte NominalOutputVoltageHexHigh = 0x73;
    public const byte NominalOutputVoltageHexLow = 0x01;
    public const byte NominalOutputVoltageRatio = 0x34;
    public const byte Nt2401ProVoltageHexHigh = 0x72;
    public const byte Nt2401ProVoltageHexLow = 0x00;
    public const byte FaultBatteryLowZeroVoltageByte = 0x00;
    public const byte NominalLoadPercentage = 14;
    public const byte Nt2401ProLoadPercentage = 12;
    public const byte BatteryLowLoadPercentage = 25;
    public const byte NominalFrequencyPart1High = 0x50;
    public const byte NominalFrequencyPart1Low = 0x0C;
    public const byte Nt2401ProFrequencyPart1Low = 0x0B;
    public const byte NominalFrequencyPart2High = 0x12;
    public const byte NominalFrequencyPart2Low = 0xC0;
    public const ulong FrequencyClock1228800Hz = 1_228_800;
    public const int FrequencyClockByteWidth = 3;
    public const ulong FrequencyTicks50HzReference = 0x5FD7;
    public const int FrequencyTicks2ByteWidth = 2;
    public const ulong Dsp4ByteTimerTicks = 0x00190000;
    public const ulong Dsp4ByteTimerClockHz = 0x05DC0000;
    public const int DspTimerByteWidth = 4;
    public const byte NominalBatteryVoltagePart1 = 0xE9;
    public const byte NominalBatteryVoltagePart2 = 0x3C;
}
