using VoltTray.Core.Telemetry;

namespace VoltTray.UI.Dialogs;

public interface IModelSelectorDialogView
{
    Func<UpsModelPreset?, UpsModelPreset?>? MockDialogHandler { get; set; }
    UpsModelPreset? Show(UpsModelPreset? currentPreset = null, nint ownerHandle = 0);
}
