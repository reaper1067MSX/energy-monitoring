using VoltTray.Core.Models;

namespace VoltTray.Infrastructure.Protocol;

/// <summary>
/// Maps Voltronic/Megatec status bytes into decoupled domain PowerStatus flags
/// and Status Byte 2 hardware nominal ratings.
/// </summary>
internal static class VoltronicStatusMapper
{
    public static PowerStatus DecodePowerStatus(
        byte statusByte1,
        double inputVoltageAc,
        double outputVoltageAc)
    {
        bool isOnBattery = (statusByte1 & VoltronicProtocolConstants.StatusOnBatteryMask) != 0;
        bool isAvrFlagSet = (statusByte1 & VoltronicProtocolConstants.StatusAvrBoostBuckMask) != 0;
        bool isMainsDivergent = !isOnBattery &&
                                outputVoltageAc > 0 &&
                                Math.Abs(inputVoltageAc - outputVoltageAc) / outputVoltageAc >
                                VoltronicProtocolConstants.MainsDivergenceThreshold;

        return new PowerStatus(
            IsOnBattery: isOnBattery,
            IsBatteryLow: (statusByte1 & VoltronicProtocolConstants.StatusBatteryLowMask) != 0,
            IsAvrActive: isAvrFlagSet || isMainsDivergent,
            IsFault: (statusByte1 & VoltronicProtocolConstants.StatusFaultMask) != 0,
            IsLineInteractive: (statusByte1 & VoltronicProtocolConstants.StatusLineInteractiveMask) != 0,
            IsSelfTestActive: (statusByte1 & VoltronicProtocolConstants.StatusSelfTestActiveMask) != 0,
            IsShutdownActive: (statusByte1 & VoltronicProtocolConstants.StatusShutdownActiveMask) != 0,
            IsBeeperActive: (statusByte1 & VoltronicProtocolConstants.StatusBeeperActiveMask) != 0);
    }

    public static UpsCapabilities RefineCapabilitiesFromStatusByte2(
        UpsCapabilities currentCapabilities,
        byte statusByte2)
    {
        UpsStatusByte2Ratings ratings = VoltronicParser.DecodeStatusByte2Ratings(statusByte2);
        BatteryBankSpec refinedBank = currentCapabilities.Bank with
        {
            SeriesBatteryCount = ratings.BatteryBankSeriesCount,
            NominalVoltageDc = ratings.NominalBatteryVoltageDc
        };

        return currentCapabilities with
        {
            Bank = refinedBank,
            NominalFrequencyHz = currentCapabilities.NominalFrequencyHz ?? ratings.NominalFrequencyHz,
            NominalVoltageAc = currentCapabilities.NominalVoltageAc ?? ratings.NominalOutputVoltageAc
        };
    }
}
