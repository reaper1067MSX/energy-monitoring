namespace VoltTray.UI.Tray;

public sealed record ContextMenuState(
    bool IsPaused,
    bool IsBeeperMuted,
    bool IsTestInProgress,
    bool IsDarkTheme = true,
    bool IsCompactIconMode = false);
