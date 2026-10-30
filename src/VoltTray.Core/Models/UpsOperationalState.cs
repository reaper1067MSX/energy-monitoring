namespace VoltTray.Core.Models;

public enum UpsOperationalState
{
    LineNormal = 0,
    OnBattery = 1,
    BatteryLow = 2,
    ChargingAvr = 3,
    Fault = 4,
    Paused = 5,
    CommunicationsLost = 6
}
