using VoltTray.UI.Models;

namespace VoltTray.UI.Dialogs;

public interface IExitDialogView
{
    ExitDialogResult Show(UITheme theme, nint ownerHandle = 0);
}
