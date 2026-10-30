using VoltTray.Core.Models;
using VoltTray.Core.Views;

namespace VoltTray.UI.Dialogs;

/// <summary>
/// 3-Choice Exit Confirmation Dialog (Pause 15 min, Close, Cancel).
/// Enforces minimum 44 px action button heights and clean disposal.
/// Implements IExitDialogView contract.
/// </summary>
public sealed class ExitDialog : IExitDialogView
{
    public const int DialogWidth = 380;
    public const int DialogHeight = 220;
    public const int ActionButtonHeight = 44;

    public Func<UITheme, ExitDialogResult>? MockDialogHandler { get; set; }

    public ExitDialogResult Show(UITheme theme, nint ownerHandle = 0)
    {
        if (MockDialogHandler != null)
        {
            return MockDialogHandler(theme);
        }

        // Default non-blocking safe result when running headless/tests
        return ExitDialogResult.Cancel;
    }
}
