using VoltTray.UI.Models;

namespace VoltTray.UI.Views;

public interface IExitDialogView
{
    ExitDialogResult Show(UITheme theme, nint ownerHandle = 0);
}
