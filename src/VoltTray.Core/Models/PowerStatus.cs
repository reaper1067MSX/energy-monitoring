namespace VoltTray.Core.Models;

public readonly record struct PowerStatus(
    bool IsOnBattery,
    bool IsBatteryLow,
    bool IsAvrActive,
    bool IsFault,
    bool IsLineInteractive,
    bool IsSelfTestActive,
    bool IsShutdownActive,
    bool IsBeeperActive);
