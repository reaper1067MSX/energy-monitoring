using System.Globalization;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using VoltTray.Core.Telemetry;
using VoltTray.UI.Tokens;

namespace VoltTray.UI.Dialogs;

/// <summary>
/// Native first-run / setup UPS model selector presenting strictly the two supported Forza profiles:
/// Forza NT-2401PRO (2400 VA / 1200 W) and Forza FX-2200 LCD (Legacy, 2200 VA / 1200 W).
/// </summary>
internal sealed class ModelSelectorDialog : IModelSelectorDialogView
{
    private readonly bool _enableNativeDialog;

    public Func<UpsModelPreset?, UpsModelPreset?>? MockDialogHandler { get; set; }

    public ModelSelectorDialog(bool enableNativeDialog = false)
    {
        _enableNativeDialog = enableNativeDialog;
    }

    public unsafe UpsModelPreset? Show(UpsModelPreset? currentPreset = null, nint ownerHandle = 0)
    {
        if (MockDialogHandler != null)
        {
            return MockDialogHandler(currentPreset);
        }

        if (!_enableNativeDialog)
        {
            return null;
        }

        bool isSpanish = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            .Equals(LocalizationConstants.SpanishCulturePrefix, StringComparison.OrdinalIgnoreCase);

        string title = isSpanish ? LocalizationConstants.ModelSelectorTitleEs : LocalizationConstants.ModelSelectorTitleEn;
        string body = isSpanish ? LocalizationConstants.ModelSelectorBodyEs : LocalizationConstants.ModelSelectorBodyEn;
        string nt2401 = isSpanish ? LocalizationConstants.ModelSelectorNt2401Es : LocalizationConstants.ModelSelectorNt2401En;
        string fx2200 = isSpanish ? LocalizationConstants.ModelSelectorFx2200Es : LocalizationConstants.ModelSelectorFx2200En;
        string cancel = isSpanish ? LocalizationConstants.ModelSelectorCancelEs : LocalizationConstants.ModelSelectorCancelEn;

        string prompt = $"{body}\n\n• Yes: {nt2401}\n• No: {fx2200}\n• Cancel: {cancel}";

        MESSAGEBOX_RESULT response;
        fixed (char* pPrompt = prompt)
        fixed (char* pTitle = title)
        {
            response = PInvoke.MessageBox(
                new HWND(ownerHandle),
                new PCWSTR(pPrompt),
                new PCWSTR(pTitle),
                MESSAGEBOX_STYLE.MB_YESNOCANCEL | MESSAGEBOX_STYLE.MB_ICONQUESTION | MESSAGEBOX_STYLE.MB_TOPMOST);
        }

        return response switch
        {
            MESSAGEBOX_RESULT.IDYES => UpsModelPreset.ForzaNt2401Pro,
            MESSAGEBOX_RESULT.IDNO => UpsModelPreset.ForzaFx2200Lcd,
            _ => null
        };
    }
}
