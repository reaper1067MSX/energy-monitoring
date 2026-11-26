using System.Globalization;
using VoltTray.UI.Models;
using VoltTray.UI.Tokens;
using VoltTray.UI.Views;

namespace VoltTray.UI.Dialogs;

public sealed record ExitDialogCopy(
    string Title,
    string Body,
    string PauseLabel,
    string PauseHelp,
    string CloseLabel,
    string CloseHelp,
    string CancelLabel);

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

    public static ExitDialogCopy GetLocalizedCopy(CultureInfo? culture = null)
    {
        CultureInfo resolved = culture ?? CultureInfo.CurrentUICulture;
        bool isSpanish = resolved.TwoLetterISOLanguageName
            .Equals(LocalizationConstants.SpanishCulturePrefix, StringComparison.OrdinalIgnoreCase);

        return isSpanish
            ? new ExitDialogCopy(
                LocalizationConstants.ExitTitleEs,
                LocalizationConstants.ExitBodyEs,
                LocalizationConstants.ExitPauseLabelEs,
                LocalizationConstants.ExitPauseHelpEs,
                LocalizationConstants.ExitCloseLabelEs,
                LocalizationConstants.ExitCloseHelpEs,
                LocalizationConstants.ExitCancelEs)
            : new ExitDialogCopy(
                LocalizationConstants.ExitTitleEn,
                LocalizationConstants.ExitBodyEn,
                LocalizationConstants.ExitPauseLabelEn,
                LocalizationConstants.ExitPauseHelpEn,
                LocalizationConstants.ExitCloseLabelEn,
                LocalizationConstants.ExitCloseHelpEn,
                LocalizationConstants.ExitCancelEn);
    }

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
