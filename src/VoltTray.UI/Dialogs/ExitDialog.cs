using System.Globalization;
using VoltTray.UI.Interop;
using VoltTray.UI.Models;
using VoltTray.UI.Tokens;

namespace VoltTray.UI.Dialogs;

/// <summary>
/// 3-Choice Exit Confirmation Dialog (Pause 15 min, Close, Cancel).
/// Enforces minimum 44 px action button heights and clean disposal.
/// Implements IExitDialogView contract.
/// </summary>
public sealed class ExitDialog : IExitDialogView
{
    private readonly bool _enableNativeDialog;

    public static int DialogWidth => DesignTokens.Sizes.ExitDialogWidth;
    public static int DialogHeight => DesignTokens.Sizes.ExitDialogHeight;
    public static int ActionButtonHeight => DesignTokens.Sizes.ButtonHeight;

    public Func<UITheme, ExitDialogResult>? MockDialogHandler { get; set; }

    public ExitDialog(bool enableNativeDialog = false)
    {
        _enableNativeDialog = enableNativeDialog;
    }

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

        if (!_enableNativeDialog)
        {
            return ExitDialogResult.Cancel;
        }

        ExitDialogCopy copy = GetLocalizedCopy();
        string prompt = $"{copy.Body}\n\n• Yes: {copy.PauseLabel} ({copy.PauseHelp})\n• No: {copy.CloseLabel} ({copy.CloseHelp})\n• Cancel: {copy.CancelLabel}";
        int response = GdiNative.MessageBox(
            ownerHandle,
            prompt,
            copy.Title,
            Win32UiConstants.MbYesNoCancel | Win32UiConstants.MbIconQuestion | Win32UiConstants.MbTopmost);

        return response switch
        {
            Win32UiConstants.IdYes => ExitDialogResult.Pause,
            Win32UiConstants.IdNo => ExitDialogResult.Close,
            _ => ExitDialogResult.Cancel
        };
    }
}
