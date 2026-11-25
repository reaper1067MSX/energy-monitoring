namespace VoltTray.Infrastructure.Protocol;

/// <summary>
/// Centralized protocol and framing constants for Voltronic Taurus Protocol 'T' (QS-Hex) and related protocols.
/// </summary>
public static class VoltronicProtocolConstants
{
    // Framing & Delimiter Bytes
    public const byte FrameHeaderHash = 0x23; // '#' (35)
    public const byte FrameHeaderParen = 0x28; // '(' (40)
    public const byte CarriageReturn = 0x0D; // '\r' (13)
    public const byte SpaceDelimiter = 0x20; // ' ' (32)
    public const byte LineFeed = 0x0A; // '\n' (10)
    public const byte Dc1 = 0x11; // DC1 / XON (17)
    public const byte Dc3 = 0x13; // DC3 / XOFF (19)
    public const byte NullByte = 0x00; // '\0' (0)
    public const byte NakHeader = 0x4E; // 'N' (78)
    public const byte EscapePrefix = FrameHeaderParen; // 0x28

    // Framing & Delimiter Characters
    public const char FrameHeaderHashChar = '#';
    public const char FrameHeaderParenChar = '(';
    public const char CarriageReturnChar = '\r';
    public const char SpaceDelimiterChar = ' ';

    // Escape Codes (De-escape mapping following 0x28)
    public const byte EscapeCodeCr = 0x00; // 0x28 0x00 -> 0x0D (\r)
    public const byte EscapeCodeDc1 = 0x01; // 0x28 0x01 -> 0x11 (DC1)
    public const byte EscapeCodeDc3 = 0x02; // 0x28 0x02 -> 0x13 (DC3)
    public const byte EscapeCodeLf = 0x03; // 0x28 0x03 -> 0x0A (\n)
    public const byte EscapeCodeSpace = 0x04; // 0x28 0x04 -> 0x20 (' ')

    // Status Byte 1 Bit Masks (Bit 0..7)
    public const byte StatusBeeperActiveMask = 0x01; // Bit 0: 1 = audible alarm on, 0 = muted
    public const byte StatusSelfTestActiveMask = 0x04; // Bit 2: 1 = self-test in progress
    public const byte StatusLineInteractiveMask = 0x08; // Bit 3: 1 = UPS online / line-interactive
    public const byte StatusFaultMask = 0x10; // Bit 4: 1 = UPS hardware fault
    public const byte StatusAvrBoostBuckMask = 0x20; // Bit 5: 1 = AVR Boost/Buck active
    public const byte StatusBatteryLowMask = 0x40; // Bit 6: 1 = battery voltage low
    public const byte StatusOnBatteryMask = 0x80; // Bit 7: 1 = utility power fail (on battery)

    // Common Pre-composed Status Byte 1 Values
    public const byte StatusLineNormal = StatusLineInteractiveMask; // 0x08
    public const byte StatusSelfTest = StatusLineInteractiveMask | StatusSelfTestActiveMask; // 0x0C
    public const byte StatusOnBattery = StatusOnBatteryMask; // 0x80
    public const byte StatusBatteryLow = StatusOnBatteryMask | StatusBatteryLowMask; // 0xC0
    public const byte StatusAvr = StatusLineInteractiveMask | StatusAvrBoostBuckMask; // 0x28

    // Status Byte 2 Values
    public const byte StatusByte2Nominal = 0xA1; // 0xA1 (bit 7=1, bit 5=1, bit 0=1)

    // Standard Protocol Commands
    public const string QueryStatusCommand = "QS\r";
    public const string QueryCommandPrefix = "Q";
    public const string QueryStatusCommandPrefix = "QS";
    public const string SelfTestCommand = "T\r";
    public const string SelfTestCommandPrefix = "T";
    public const string BeeperToggleCommand = "Q\r";

    // Frame Validation & Parsing Limits
    public const int MinRawFrameLength = 3;
    public const int MinTokenCount = 9;

    // Nominal Physical & Scaling Constants
    public const double VoltageDivisor = 51.0 * 256.0; // 13056.0
    public const double BatteryVoltageDivisor = 510.0;
    public const double DefaultRatedWatts = 900.0;
    public const double SelfTestDurationSeconds = 15.0;
    public const double MainsDivergenceThreshold = 0.05;
    public const double DefaultNominalFrequencyHz = 60.0;
    public const double MinValidFrequencyHz = 45.0;
    public const double MaxValidFrequencyHz = 70.0;
    public const double DefaultAmbientTemperatureCelsius = 25.0;

    // Nominal Taurus Protocol 'T' QS-Hex Binary Frame Bytes
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

    /// <summary>
    /// Appends a byte, applying Voltronic Protocol 'T' escaping if the byte matches a delimiter or control character.
    /// </summary>
    public static void AppendEscapedByte(List<byte> list, byte b)
    {
        switch (b)
        {
            case CarriageReturn: // 0x0D -> 0x28 0x00
                list.Add(FrameHeaderParen);
                list.Add(EscapeCodeCr);
                break;
            case SpaceDelimiter: // 0x20 -> 0x28 0x04
                list.Add(FrameHeaderParen);
                list.Add(EscapeCodeSpace);
                break;
            case LineFeed: // 0x0A -> 0x28 0x03
                list.Add(FrameHeaderParen);
                list.Add(EscapeCodeLf);
                break;
            case Dc1: // 0x11 -> 0x28 0x01
                list.Add(FrameHeaderParen);
                list.Add(EscapeCodeDc1);
                break;
            case Dc3: // 0x13 -> 0x28 0x02
                list.Add(FrameHeaderParen);
                list.Add(EscapeCodeDc3);
                break;
            default:
                list.Add(b);
                break;
        }
    }

    /// <summary>
    /// Builds a Voltronic Taurus Protocol 'T' (QS-Hex) binary telemetry frame.
    /// </summary>
    public static byte[] BuildBinaryFrame(
        byte statusByte1 = StatusLineNormal,
        byte statusByte2 = StatusByte2Nominal,
        byte loadPercentage = NominalLoadPercentage,
        byte inVHexHigh = NominalInputVoltageHexHigh,
        byte inVHexLow = NominalInputVoltageHexLow,
        byte inVRatio = NominalInputVoltageRatio,
        byte outVHexHigh = NominalOutputVoltageHexHigh,
        byte outVHexLow = NominalOutputVoltageHexLow,
        byte outVRatio = NominalOutputVoltageRatio,
        byte freq1High = NominalFrequencyPart1High,
        byte freq1Low = NominalFrequencyPart1Low,
        byte freq2High = NominalFrequencyPart2High,
        byte freq2Low = NominalFrequencyPart2Low,
        byte bat1 = NominalBatteryVoltagePart1,
        byte bat2 = NominalBatteryVoltagePart2)
    {
        var list = new List<byte>(28);
        list.Add(FrameHeaderHash);

        AppendEscapedByte(list, inVHexHigh);
        AppendEscapedByte(list, inVHexLow);
        list.Add(SpaceDelimiter);

        AppendEscapedByte(list, inVRatio);
        list.Add(SpaceDelimiter);

        AppendEscapedByte(list, outVHexHigh);
        AppendEscapedByte(list, outVHexLow);
        list.Add(SpaceDelimiter);

        AppendEscapedByte(list, outVRatio);
        list.Add(SpaceDelimiter);

        AppendEscapedByte(list, loadPercentage);
        list.Add(SpaceDelimiter);

        AppendEscapedByte(list, freq1High);
        AppendEscapedByte(list, freq1Low);
        list.Add(SpaceDelimiter);

        AppendEscapedByte(list, freq2High);
        AppendEscapedByte(list, freq2Low);
        list.Add(SpaceDelimiter);

        AppendEscapedByte(list, bat1);
        list.Add(SpaceDelimiter);

        AppendEscapedByte(list, bat2);
        list.Add(SpaceDelimiter);

        AppendEscapedByte(list, statusByte1);
        list.Add(SpaceDelimiter);

        AppendEscapedByte(list, statusByte2);
        list.Add(CarriageReturn);

        return list.ToArray();
    }
}
