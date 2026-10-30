using VoltTray.Core.Models;

namespace VoltTray.Core.Views;

public interface IExitDialogView
{
    ExitDialogResult Show(UITheme theme, nint ownerHandle = 0);
}
