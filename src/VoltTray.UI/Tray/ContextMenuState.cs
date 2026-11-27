namespace VoltTray.UI.Tray;

public sealed record ContextMenuState(
    bool IsPaused,
    bool IsBeeperMuted,
    bool IsTestInProgress);
