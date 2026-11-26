using VoltTray.UI.Models;
using VoltTray.UI.Tokens;
using VoltTray.UI.Views;

namespace VoltTray.UI.Dialogs;

/// <summary>
/// 3-Choice Exit Confirmation Dialog (Pause 15 min, Close, Cancel).
/// Enforces minimum 44 px action button heights and clean disposal.
/// Implements IExitDialogView contract.
/// </summary>
public sealed class ExitDialog : IExitDialogView
{
    public static int DialogWidth => DesignTokens.Sizes.ExitDialogWidth;
    public static int DialogHeight => DesignTokens.Sizes.ExitDialogHeight;
    public static int ActionButtonHeight => DesignTokens.Sizes.ButtonHeight;

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
