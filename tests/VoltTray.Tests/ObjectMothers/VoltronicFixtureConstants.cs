using VoltTray.Infrastructure.Protocol;

namespace VoltTray.Tests.ObjectMothers;

/// <summary>
/// Test-fixture constants for Voltronic Protocol 'T' variants, Status Byte 2 presets, and mutating command vectors.
/// </summary>
public static class VoltronicFixtureConstants
{
    public const byte NakLetterA = 0x41;
    public const byte NakLetterK = 0x4B;

    // Status Byte 1 Test Presets
    public const byte StatusSelfTest =
        VoltronicProtocolConstants.StatusLineInteractiveMask | VoltronicProtocolConstants.StatusSelfTestActiveMask;
    public const byte Nt2401ProStatusByte1BeeperOn =
        VoltronicProtocolConstants.StatusLineInteractiveMask | VoltronicProtocolConstants.StatusBeeperActiveMask;
    public const byte StatusOnBattery = VoltronicProtocolConstants.StatusOnBatteryMask;
    public const byte StatusBatteryLow =
        VoltronicProtocolConstants.StatusOnBatteryMask | VoltronicProtocolConstants.StatusBatteryLowMask;
    public const byte StatusAvr =
        VoltronicProtocolConstants.StatusLineInteractiveMask | VoltronicProtocolConstants.StatusAvrBoostBuckMask;

    // Status Byte 2 Presets
    public const byte StatusByte2European12V50Hz220V = 0x02;
    public const byte StatusByte2Nominal48V50Hz230V = 0x63;
    public const byte StatusByte2Nominal36V50HzUnknownAc = 0x47;

    // NT-2401PRO, 50 Hz & 4-Byte DSP Test Vectors
    public const byte Nt2401ProVoltageHexHigh = 0x72;
    public const byte Nt2401ProVoltageHexLow = 0x00;
    public const byte Nt2401ProLoadPercentage = 12;
    public const byte Nt2401ProFrequencyPart1Low = 0x0B;
    public const ushort Nt2401ProFrequencyPeriodTicks = 0x500B;
    public const byte FaultBatteryLowZeroVoltageByte = 0x00;
    public const byte BatteryLowLoadPercentage = 25;
    public const ulong FrequencyClock1228800Hz = 1_228_800;
    public const int FrequencyClockByteWidth = 3;
    public const ulong FrequencyTicks50HzReference = 0x5FD7;
    public const int FrequencyTicks2ByteWidth = 2;
    public const ulong Dsp4ByteTimerTicks = 0x00190000;
    public const ulong Dsp4ByteTimerClockHz = 0x05DC0000;
    public const int DspTimerByteWidth = 4;
    public const ushort TestDescriptorVersionBcd = 0x0100;

    // Blocked / Mutating Wire Commands (Verify Rejection)
    public const string CancelSelfTestCommand = "CT\r";
    public const string LongSelfTestCommand = "TL\r";
    public const string ShutdownRestoreTestCommand = "S01R0001";
    public const string CalibrationCommand = "C\r";
    public const string UnknownPeCommand = "PE\r";
}
