namespace VoltTray.UI.Dialogs;

public sealed record ExitDialogCopy(
    string Title,
    string Body,
    string PauseLabel,
    string PauseHelp,
    string CloseLabel,
    string CloseHelp,
    string CancelLabel);
