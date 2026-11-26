namespace VoltTray.Infrastructure.Protocol;

/// <summary>
/// Builds and escapes binary Voltronic Protocol 'T' (QS-Hex) telemetry frames from a <see cref="VoltronicFrameSpecification"/>.
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
    /// Builds a Voltronic Taurus Protocol 'T' (QS-Hex) binary telemetry frame from the provided specification.
    /// </summary>
    public static byte[] Build(VoltronicFrameSpecification specification)
    {
        ArgumentNullException.ThrowIfNull(specification);

        var frame = new List<byte>(DefaultFrameBufferCapacity);
        frame.Add(VoltronicProtocolConstants.FrameHeaderHash);

        AppendEscapedToken(frame, specification.InputVoltageBytes);
        frame.Add(VoltronicProtocolConstants.SpaceDelimiter);

        AppendEscapedByte(frame, specification.InputVoltageRatio);
        frame.Add(VoltronicProtocolConstants.SpaceDelimiter);

        AppendEscapedToken(frame, specification.OutputVoltageBytes);
        frame.Add(VoltronicProtocolConstants.SpaceDelimiter);

        AppendEscapedByte(frame, specification.OutputVoltageRatio);
        frame.Add(VoltronicProtocolConstants.SpaceDelimiter);

        AppendEscapedByte(frame, specification.LoadPercentage);
        frame.Add(VoltronicProtocolConstants.SpaceDelimiter);

        AppendEscapedToken(frame, specification.FrequencyPeriodTicksBytes);
        frame.Add(VoltronicProtocolConstants.SpaceDelimiter);

        AppendEscapedToken(frame, specification.FrequencyTimerClockBytes);
        frame.Add(VoltronicProtocolConstants.SpaceDelimiter);

        AppendEscapedByte(frame, specification.BatteryVoltageHighByte);
        frame.Add(VoltronicProtocolConstants.SpaceDelimiter);

        AppendEscapedByte(frame, specification.BatteryVoltageLowByte);
        frame.Add(VoltronicProtocolConstants.SpaceDelimiter);

        AppendEscapedByte(frame, specification.StatusByte1);
        frame.Add(VoltronicProtocolConstants.SpaceDelimiter);

        AppendEscapedByte(frame, specification.StatusByte2);
        frame.Add(VoltronicProtocolConstants.CarriageReturn);

        return frame.ToArray();
    }

    private static void AppendEscapedToken(List<byte> target, byte[] tokenBytes)
    {
        foreach (byte tokenByte in tokenBytes)
        {
            AppendEscapedByte(target, tokenByte);
        }
    }
}
