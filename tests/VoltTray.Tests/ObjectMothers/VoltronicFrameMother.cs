using System.Text;
using VoltTray.Infrastructure.Protocol;

namespace VoltTray.Tests.ObjectMothers;

/// <summary>
/// Object Mother providing standardized Voltronic Protocol 'T' (QS-Hex) binary frames
/// built exclusively from <see cref="VoltronicBinaryFrameBuilder"/>, <see cref="VoltronicFrameSpecification"/>,
/// and named protocol/fixture constants. Returns fresh array instances on every access.
/// </summary>
public static class VoltronicFrameMother
{
    private const byte DefaultOnBatteryLoadPercentage = 14;
    private const double DefaultAvrInputVoltageAc = 98.0;
    private const double DefaultAvrOutputVoltageAc = 118.0;
    private const int VoltageTokenByteWidth = 2;

    private static readonly VoltronicFrameSpecification GoldenSpecification = new();

    private static readonly VoltronicFrameSpecification Nt2401ProLiveSpecification = new()
    {
        StatusByte1 = VoltronicFixtureConstants.Nt2401ProStatusByte1BeeperOn,
        StatusByte2 = VoltronicProtocolConstants.StatusByte2Nominal,
        LoadPercentage = VoltronicFixtureConstants.Nt2401ProLoadPercentage,
        InputVoltageBytes =
        [
            VoltronicFixtureConstants.Nt2401ProVoltageHexHigh,
            VoltronicFixtureConstants.Nt2401ProVoltageHexLow
        ],
        OutputVoltageBytes =
        [
            VoltronicFixtureConstants.Nt2401ProVoltageHexHigh,
            VoltronicFixtureConstants.Nt2401ProVoltageHexLow
        ],
        FrequencyPeriodTicksBytes =
        [
            VoltronicProtocolConstants.NominalFrequencyPart1High,
            VoltronicFixtureConstants.Nt2401ProFrequencyPart1Low
        ],
        FrequencyTimerClockBytes = VoltronicBinaryFrameBuilder.EncodeBigEndianBytes(
            VoltronicFixtureConstants.FrequencyClock1228800Hz,
            VoltronicFixtureConstants.FrequencyClockByteWidth)
    };

    private static readonly VoltronicFrameSpecification Upstream50HzSpecification = new()
    {
        StatusByte2 = VoltronicFixtureConstants.StatusByte2European12V50Hz220V,
        FrequencyPeriodTicksBytes = VoltronicBinaryFrameBuilder.EncodeBigEndianBytes(
            VoltronicFixtureConstants.FrequencyTicks50HzReference,
            VoltronicFixtureConstants.FrequencyTicks2ByteWidth),
        FrequencyTimerClockBytes = VoltronicBinaryFrameBuilder.EncodeBigEndianBytes(
            VoltronicFixtureConstants.FrequencyClock1228800Hz,
            VoltronicFixtureConstants.FrequencyClockByteWidth)
    };

    private static readonly VoltronicFrameSpecification Dsp4ByteTimerSpecification = new()
    {
        FrequencyPeriodTicksBytes = VoltronicBinaryFrameBuilder.EncodeBigEndianBytes(
            VoltronicFixtureConstants.Dsp4ByteTimerTicks,
            VoltronicFixtureConstants.DspTimerByteWidth),
        FrequencyTimerClockBytes = VoltronicBinaryFrameBuilder.EncodeBigEndianBytes(
            VoltronicFixtureConstants.Dsp4ByteTimerClockHz,
            VoltronicFixtureConstants.DspTimerByteWidth)
    };

    /// <summary>
    /// Canonical 27-byte golden frame verified against physical Forza FX-2200 LCD UPS.
    /// </summary>
    public static byte[] GoldenFrame => VoltronicBinaryFrameBuilder.Build(GoldenSpecification);

    /// <summary>
    /// Live NT-2401PRO 28-byte QS\r frame (59.97 Hz, 0xA1 ratings, null bytes in T0/T2/T6).
    /// </summary>
    public static byte[] Nt2401ProLiveFrame => VoltronicBinaryFrameBuilder.Build(Nt2401ProLiveSpecification);

    /// <summary>
    /// 50 Hz upstream frame (50.08 Hz, Status2 = 0x02).
    /// </summary>
    public static byte[] Upstream50HzFrame => VoltronicBinaryFrameBuilder.Build(Upstream50HzSpecification);

    /// <summary>
    /// 4-byte DSP timer frame (60.0 Hz).
    /// </summary>
    public static byte[] Dsp4ByteTimerFrame => VoltronicBinaryFrameBuilder.Build(Dsp4ByteTimerSpecification);

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
        var specification = new VoltronicFrameSpecification
        {
            StatusByte1 = statusByte1,
            StatusByte2 = statusByte2,
            LoadPercentage = loadPercentage,
            InputVoltageBytes = [inVHexHigh, inVHexLow],
            InputVoltageRatio = inVRatio,
            OutputVoltageBytes = [outVHexHigh, outVHexLow],
            OutputVoltageRatio = outVRatio
        };

        return VoltronicBinaryFrameBuilder.Build(specification);
    }

    public static byte[] CreateSelfTestFrame()
    {
        return CreateFrame(statusByte1: VoltronicFixtureConstants.StatusSelfTest);
    }

    public static byte[] CreateIdleFrame()
    {
        return CreateFrame(statusByte1: VoltronicProtocolConstants.StatusLineNormal);
    }

    public static byte[] CreateOnBatteryFrame(byte loadPercentage = DefaultOnBatteryLoadPercentage)
    {
        return CreateFrame(
            statusByte1: VoltronicFixtureConstants.StatusOnBattery,
            loadPercentage: loadPercentage,
            inVHexHigh: VoltronicFixtureConstants.FaultBatteryLowZeroVoltageByte,
            inVHexLow: VoltronicFixtureConstants.FaultBatteryLowZeroVoltageByte);
    }

    public static byte[] CreateBatteryLowFrame()
    {
        return CreateFrame(
            statusByte1: VoltronicFixtureConstants.StatusBatteryLow,
            loadPercentage: VoltronicFixtureConstants.BatteryLowLoadPercentage,
            inVHexHigh: VoltronicFixtureConstants.FaultBatteryLowZeroVoltageByte,
            inVHexLow: VoltronicFixtureConstants.FaultBatteryLowZeroVoltageByte);
    }

    public static byte[] CreateAvrFrame(
        double inputVoltageAc = DefaultAvrInputVoltageAc,
        double outputVoltageAc = DefaultAvrOutputVoltageAc)
    {
        var specification = new VoltronicFrameSpecification
        {
            StatusByte1 = VoltronicFixtureConstants.StatusAvr,
            InputVoltageBytes = EncodeVoltageAcBytes(inputVoltageAc),
            OutputVoltageBytes = EncodeVoltageAcBytes(outputVoltageAc)
        };

        return VoltronicBinaryFrameBuilder.Build(specification);
    }

    public static byte[] CreateFaultFrame()
    {
        return CreateFrame(statusByte1: VoltronicProtocolConstants.StatusFaultMask);
    }

    public static byte[] CreateCorruptedMissingHash()
    {
        byte[] frame = GoldenFrame;
        frame[0] = VoltronicProtocolConstants.SpaceDelimiter;
        return frame;
    }

    public static byte[] CreateCorruptedMissingCr()
    {
        byte[] frame = GoldenFrame;
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
            VoltronicFixtureConstants.NakLetterA,
            VoltronicFixtureConstants.NakLetterK,
            VoltronicProtocolConstants.CarriageReturn
        ];
    }

    private static byte[] EncodeVoltageAcBytes(double voltageAc)
    {
        double clampedVoltage = Math.Max(0.0, voltageAc);
        ulong rawCount = (ulong)Math.Round(
            clampedVoltage * VoltronicProtocolConstants.VoltageDivisor /
            VoltronicProtocolConstants.NominalInputVoltageRatio,
            MidpointRounding.AwayFromZero);

        return VoltronicBinaryFrameBuilder.EncodeBigEndianBytes(rawCount, VoltageTokenByteWidth);
    }
}
