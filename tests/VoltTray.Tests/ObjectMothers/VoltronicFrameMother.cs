using System.Text;
using VoltTray.Infrastructure.Protocol;

namespace VoltTray.Tests.ObjectMothers;

/// <summary>
/// Object Mother providing standardized Voltronic Protocol 'T' (QS-Hex) binary frames
/// and test vectors built exclusively from <see cref="VoltronicBinaryFrameBuilder"/> and <see cref="VoltronicProtocolConstants"/>.
/// </summary>
public static class VoltronicFrameMother
{
    private const byte DefaultOnBatteryLoadPercentage = 14;
    private const double DefaultAvrInputVoltage = 98.0;
    private const double DefaultAvrOutputVoltage = 118.0;

    /// <summary>
    /// Canonical 27-byte golden frame verified against physical Forza FX-2200 LCD UPS:
    /// In: 117.3V, Out: 117.3V, Load: 14% (126W), Freq: 60.0Hz, Bat: 27.41V, Status: LineNormal (0x08), Beeper Muted.
    /// </summary>
    public static readonly byte[] GoldenFrame = VoltronicBinaryFrameBuilder.Build();

    /// <summary>
    /// Live NT-2401PRO 28-byte QS\r frame (59.97 Hz, 0xA1 ratings, null bytes in T0/T2/T6).
    /// </summary>
    public static readonly byte[] Nt2401ProLiveFrame = VoltronicBinaryFrameBuilder.Build(
        statusByte1: VoltronicProtocolConstants.Nt2401ProStatusByte1BeeperOn,
        statusByte2: VoltronicProtocolConstants.StatusByte2Nominal,
        loadPercentage: VoltronicProtocolConstants.Nt2401ProLoadPercentage,
        inVHexHigh: VoltronicProtocolConstants.Nt2401ProVoltageHexHigh,
        inVHexLow: VoltronicProtocolConstants.Nt2401ProVoltageHexLow,
        outVHexHigh: VoltronicProtocolConstants.Nt2401ProVoltageHexHigh,
        outVHexLow: VoltronicProtocolConstants.Nt2401ProVoltageHexLow,
        freq1Low: VoltronicProtocolConstants.Nt2401ProFrequencyPart1Low,
        freq2Bytes: VoltronicBinaryFrameBuilder.EncodeBigEndianBytes(
            VoltronicProtocolConstants.FrequencyClock1228800Hz,
            VoltronicProtocolConstants.FrequencyClockByteWidth));

    /// <summary>
    /// 50 Hz upstream frame (T5=0x5FD7, T6=0x12C000 -> 50.08 Hz, Status2=0x02).
    /// </summary>
    public static readonly byte[] Upstream50HzFrame = VoltronicBinaryFrameBuilder.Build(
        statusByte2: VoltronicProtocolConstants.StatusByte2European12V50Hz220V,
        freq1Bytes: VoltronicBinaryFrameBuilder.EncodeBigEndianBytes(
            VoltronicProtocolConstants.FrequencyTicks50HzReference,
            VoltronicProtocolConstants.FrequencyTicks2ByteWidth),
        freq2Bytes: VoltronicBinaryFrameBuilder.EncodeBigEndianBytes(
            VoltronicProtocolConstants.FrequencyClock1228800Hz,
            VoltronicProtocolConstants.FrequencyClockByteWidth));

    /// <summary>
    /// 4-byte DSP timer frame (T5=0x00190000, T6=0x05DC0000 -> 60.0 Hz).
    /// </summary>
    public static readonly byte[] Dsp4ByteTimerFrame = VoltronicBinaryFrameBuilder.Build(
        freq1Bytes: VoltronicBinaryFrameBuilder.EncodeBigEndianBytes(
            VoltronicProtocolConstants.Dsp4ByteTimerTicks,
            VoltronicProtocolConstants.DspTimerByteWidth),
        freq2Bytes: VoltronicBinaryFrameBuilder.EncodeBigEndianBytes(
            VoltronicProtocolConstants.Dsp4ByteTimerClockHz,
            VoltronicProtocolConstants.DspTimerByteWidth));

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
        return VoltronicBinaryFrameBuilder.Build(
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

    public static byte[] CreateOnBatteryFrame(byte loadPercentage = DefaultOnBatteryLoadPercentage)
    {
        return CreateFrame(
            statusByte1: VoltronicProtocolConstants.StatusOnBattery,
            loadPercentage: loadPercentage,
            inVHexHigh: VoltronicProtocolConstants.FaultBatteryLowZeroVoltageByte,
            inVHexLow: VoltronicProtocolConstants.FaultBatteryLowZeroVoltageByte);
    }

    public static byte[] CreateBatteryLowFrame()
    {
        return CreateFrame(
            statusByte1: VoltronicProtocolConstants.StatusBatteryLow,
            loadPercentage: VoltronicProtocolConstants.BatteryLowLoadPercentage,
            inVHexHigh: VoltronicProtocolConstants.FaultBatteryLowZeroVoltageByte,
            inVHexLow: VoltronicProtocolConstants.FaultBatteryLowZeroVoltageByte);
    }

    public static byte[] CreateAvrFrame(
        double inV = DefaultAvrInputVoltage,
        double outV = DefaultAvrOutputVoltage)
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
        frame[0] = VoltronicProtocolConstants.SpaceDelimiter;
        return frame;
    }

    public static byte[] CreateCorruptedMissingCr()
    {
        byte[] frame = (byte[])GoldenFrame.Clone();
        frame[^1] = VoltronicProtocolConstants.SpaceDelimiter;
        return frame;
    }

    public static byte[] CreateTruncatedFrame()
    {
        return
        [
            VoltronicProtocolConstants.FrameHeaderHash,
            VoltronicProtocolConstants.NominalInputVoltageHexHigh,
            VoltronicProtocolConstants.NominalInputVoltageHexLow,
            VoltronicProtocolConstants.CarriageReturn
        ];
    }

    public static byte[] CreateNakFrame()
    {
        return
        [
            VoltronicProtocolConstants.NakHeader,
            VoltronicProtocolConstants.NakLetterA,
            VoltronicProtocolConstants.NakLetterK,
            VoltronicProtocolConstants.CarriageReturn
        ];
    }
}
