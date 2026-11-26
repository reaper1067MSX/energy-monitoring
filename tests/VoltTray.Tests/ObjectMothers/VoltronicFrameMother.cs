using System.Text;
using VoltTray.Infrastructure.Protocol;

namespace VoltTray.Tests.ObjectMothers;

/// <summary>
/// Object Mother providing standardized Voltronic Protocol 'T' (QS-Hex) binary frames
/// and test vectors for parser, communication, and adversarial testing.
/// </summary>
public static class VoltronicFrameMother
{
    /// <summary>
    /// Canonical golden frame bytes verified against physical Forza FX-2200 LCD UPS:
    /// In: 117.3V, Out: 117.3V, Load: 14% (126W), Freq: 60.0Hz, Bat: 27.41V, Status: LineNormal (0x08), Beeper Muted.
    /// </summary>
    public static readonly byte[] GoldenFrame =
    [
        0x23, 0x73, 0x01, 0x20, 0x34, 0x20, 0x73, 0x01, 0x20, 0x34, 0x20,
        0x0E, 0x20, 0x50, 0x0C, 0x20, 0x12, 0xC0, 0x20, 0xE9, 0x20, 0x3C, 0x20,
        0x08, 0x20, 0xA1, 0x0D
    ];

    /// <summary>Live NT-2401PRO 28-byte QS\r frame (59.97 Hz, 0xA1 ratings, null bytes in T0/T2/T6).</summary>
    public static readonly byte[] Nt2401ProLiveFrame =
    [
        0x23, 0x72, 0x00, 0x20, 0x34, 0x20, 0x72, 0x00, 0x20, 0x34, 0x20,
        0x0C, 0x20, 0x50, 0x0B, 0x20, 0x12, 0xC0, 0x00, 0x20, 0xE9, 0x20,
        0x3C, 0x20, 0x09, 0x20, 0xA1, 0x0D
    ];

    /// <summary>50 Hz upstream frame (T5=0x5FD7, T6=0x12C000 -> 50.08 Hz, Status2=0x02).</summary>
    public static readonly byte[] Upstream50HzFrame = VoltronicProtocolConstants.BuildBinaryFrame(
        statusByte2: 0x02,
        freq1High: 0x5F,
        freq1Low: 0xD7,
        freq2Bytes: [0x12, 0xC0, 0x00]);

    /// <summary>4-byte DSP timer frame (T5=0x00190000, T6=0x05DC0000 -> 60.0 Hz).</summary>
    public static readonly byte[] Dsp4ByteTimerFrame = VoltronicProtocolConstants.BuildBinaryFrame(
        freq1Bytes: [0x00, 0x19, 0x00, 0x00],
        freq2Bytes: [0x05, 0xDC, 0x00, 0x00]);

    public static string GoldenFrameString => Encoding.Latin1.GetString(GoldenFrame);

    public static byte[] CreateFrame(
        byte statusByte1 = VoltronicProtocolConstants.StatusLineNormal,
        byte statusByte2 = VoltronicProtocolConstants.StatusByte2Nominal,
        byte loadPercentage = VoltronicProtocolConstants.NominalLoadPercentage,
        byte inVHexHigh = VoltronicProtocolConstants.NominalInputVoltageHexHigh,
        byte inVHexLow = VoltronicProtocolConstants.NominalInputVoltageHexLow,
        byte inVRatio = VoltronicProtocolConstants.NominalInputVoltageRatio,
        byte outVHexHigh = VoltronicProtocolConstants.NominalOutputVoltageHexHigh,
        byte outVHexLow = VoltronicProtocolConstants.NominalOutputVoltageHexLow,
        byte outVRatio = VoltronicProtocolConstants.NominalOutputVoltageRatio)
    {
        return VoltronicProtocolConstants.BuildBinaryFrame(
            statusByte1: statusByte1,
            statusByte2: statusByte2,
            loadPercentage: loadPercentage,
            inVHexHigh: inVHexHigh,
            inVHexLow: inVHexLow,
            inVRatio: inVRatio,
            outVHexHigh: outVHexHigh,
            outVHexLow: outVHexLow,
            outVRatio: outVRatio);
    }

    public static byte[] CreateSelfTestFrame()
    {
        return CreateFrame(statusByte1: VoltronicProtocolConstants.StatusSelfTest);
    }

    public static byte[] CreateIdleFrame()
    {
        return CreateFrame(statusByte1: VoltronicProtocolConstants.StatusLineNormal);
    }

    public static byte[] CreateOnBatteryFrame(byte loadPercentage = 14)
    {
        return CreateFrame(
            statusByte1: VoltronicProtocolConstants.StatusOnBattery,
            loadPercentage: loadPercentage,
            inVHexHigh: 0x00,
            inVHexLow: 0x00);
    }

    public static byte[] CreateBatteryLowFrame()
    {
        return CreateFrame(
            statusByte1: VoltronicProtocolConstants.StatusBatteryLow,
            loadPercentage: 25,
            inVHexHigh: 0x00,
            inVHexLow: 0x00);
    }

    public static byte[] CreateAvrFrame(double inV = 98.0, double outV = 118.0)
    {
        return CreateFrame(statusByte1: VoltronicProtocolConstants.StatusAvr);
    }

    public static byte[] CreateFaultFrame()
    {
        return CreateFrame(statusByte1: VoltronicProtocolConstants.StatusFaultMask);
    }

    public static byte[] CreateCorruptedMissingHash()
    {
        byte[] frame = (byte[])GoldenFrame.Clone();
        frame[0] = 0x20; // Replaces '#' with space
        return frame;
    }

    public static byte[] CreateCorruptedMissingCr()
    {
        byte[] frame = (byte[])GoldenFrame.Clone();
        frame[^1] = 0x20; // Replaces '\r' with space
        return frame;
    }

    public static byte[] CreateTruncatedFrame()
    {
        return [VoltronicProtocolConstants.FrameHeaderHash, 0x73, 0x01, VoltronicProtocolConstants.CarriageReturn];
    }

    public static byte[] CreateNakFrame()
    {
        return [VoltronicProtocolConstants.NakHeader, 0x41, 0x4B, VoltronicProtocolConstants.CarriageReturn];
    }
}
