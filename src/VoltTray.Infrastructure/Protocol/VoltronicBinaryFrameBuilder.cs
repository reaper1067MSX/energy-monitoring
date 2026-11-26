namespace VoltTray.Infrastructure.Protocol;

/// <summary>
/// Builds and escapes binary Voltronic Protocol 'T' (QS-Hex) telemetry frames for testing and simulation.
/// </summary>
public static class VoltronicBinaryFrameBuilder
{
    private const int BitsPerByte = 8;
    private const int MaxBigEndianByteWidth = 8;
    private const byte LowByteMask = 0xFF;
    private const int DefaultFrameBufferCapacity = 32;

    /// <summary>
    /// Appends a byte, applying Voltronic Protocol 'T' escaping if it matches a delimiter or control character.
    /// </summary>
    public static void AppendEscapedByte(List<byte> target, byte rawByte)
    {
        switch (rawByte)
        {
            case VoltronicProtocolConstants.CarriageReturn:
                target.Add(VoltronicProtocolConstants.FrameHeaderParen);
                target.Add(VoltronicProtocolConstants.EscapeCodeCr);
                break;
            case VoltronicProtocolConstants.SpaceDelimiter:
                target.Add(VoltronicProtocolConstants.FrameHeaderParen);
                target.Add(VoltronicProtocolConstants.EscapeCodeSpace);
                break;
            case VoltronicProtocolConstants.LineFeed:
                target.Add(VoltronicProtocolConstants.FrameHeaderParen);
                target.Add(VoltronicProtocolConstants.EscapeCodeLf);
                break;
            case VoltronicProtocolConstants.Dc1:
                target.Add(VoltronicProtocolConstants.FrameHeaderParen);
                target.Add(VoltronicProtocolConstants.EscapeCodeDc1);
                break;
            case VoltronicProtocolConstants.Dc3:
                target.Add(VoltronicProtocolConstants.FrameHeaderParen);
                target.Add(VoltronicProtocolConstants.EscapeCodeDc3);
                break;
            default:
                target.Add(rawByte);
                break;
        }
    }

    /// <summary>
    /// Encodes an unsigned integer into an arbitrary-width (1..8 byte) big-endian byte array.
    /// </summary>
    public static byte[] EncodeBigEndianBytes(ulong value, int byteWidth)
    {
        if (byteWidth <= 0 || byteWidth > MaxBigEndianByteWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(byteWidth), "Byte width must be between 1 and 8.");
        }

        byte[] encoded = new byte[byteWidth];
        for (int index = byteWidth - 1; index >= 0; index--)
        {
            encoded[index] = (byte)(value & LowByteMask);
            value >>= BitsPerByte;
        }

        return encoded;
    }

    /// <summary>
    /// Builds a Voltronic Taurus Protocol 'T' (QS-Hex) binary telemetry frame.
    /// </summary>
    public static byte[] Build(
        byte statusByte1 = VoltronicProtocolConstants.StatusLineNormal,
        byte statusByte2 = VoltronicProtocolConstants.StatusByte2Nominal,
        byte loadPercentage = VoltronicProtocolConstants.NominalLoadPercentage,
        byte inVHexHigh = VoltronicProtocolConstants.NominalInputVoltageHexHigh,
        byte inVHexLow = VoltronicProtocolConstants.NominalInputVoltageHexLow,
        byte inVRatio = VoltronicProtocolConstants.NominalInputVoltageRatio,
        byte outVHexHigh = VoltronicProtocolConstants.NominalOutputVoltageHexHigh,
        byte outVHexLow = VoltronicProtocolConstants.NominalOutputVoltageHexLow,
        byte outVRatio = VoltronicProtocolConstants.NominalOutputVoltageRatio,
        byte freq1High = VoltronicProtocolConstants.NominalFrequencyPart1High,
        byte freq1Low = VoltronicProtocolConstants.NominalFrequencyPart1Low,
        byte freq2High = VoltronicProtocolConstants.NominalFrequencyPart2High,
        byte freq2Low = VoltronicProtocolConstants.NominalFrequencyPart2Low,
        byte bat1 = VoltronicProtocolConstants.NominalBatteryVoltagePart1,
        byte bat2 = VoltronicProtocolConstants.NominalBatteryVoltagePart2,
        byte[]? freq1Bytes = null,
        byte[]? freq2Bytes = null)
    {
        var frame = new List<byte>(DefaultFrameBufferCapacity);
        frame.Add(VoltronicProtocolConstants.FrameHeaderHash);

        AppendEscapedByte(frame, inVHexHigh);
        AppendEscapedByte(frame, inVHexLow);
        frame.Add(VoltronicProtocolConstants.SpaceDelimiter);

        AppendEscapedByte(frame, inVRatio);
        frame.Add(VoltronicProtocolConstants.SpaceDelimiter);

        AppendEscapedByte(frame, outVHexHigh);
        AppendEscapedByte(frame, outVHexLow);
        frame.Add(VoltronicProtocolConstants.SpaceDelimiter);

        AppendEscapedByte(frame, outVRatio);
        frame.Add(VoltronicProtocolConstants.SpaceDelimiter);

        AppendEscapedByte(frame, loadPercentage);
        frame.Add(VoltronicProtocolConstants.SpaceDelimiter);

        AppendFrequencyTokenBytes(frame, freq1Bytes, freq1High, freq1Low);
        frame.Add(VoltronicProtocolConstants.SpaceDelimiter);

        AppendFrequencyTokenBytes(frame, freq2Bytes, freq2High, freq2Low);
        frame.Add(VoltronicProtocolConstants.SpaceDelimiter);

        AppendEscapedByte(frame, bat1);
        frame.Add(VoltronicProtocolConstants.SpaceDelimiter);

        AppendEscapedByte(frame, bat2);
        frame.Add(VoltronicProtocolConstants.SpaceDelimiter);

        AppendEscapedByte(frame, statusByte1);
        frame.Add(VoltronicProtocolConstants.SpaceDelimiter);

        AppendEscapedByte(frame, statusByte2);
        frame.Add(VoltronicProtocolConstants.CarriageReturn);

        return frame.ToArray();
    }

    private static void AppendFrequencyTokenBytes(
        List<byte> target,
        byte[]? overrideBytes,
        byte highByte,
        byte lowByte)
    {
        if (overrideBytes is { Length: > 0 })
        {
            foreach (byte tokenByte in overrideBytes)
            {
                AppendEscapedByte(target, tokenByte);
            }

            return;
        }

        AppendEscapedByte(target, highByte);
        AppendEscapedByte(target, lowByte);
    }
}
