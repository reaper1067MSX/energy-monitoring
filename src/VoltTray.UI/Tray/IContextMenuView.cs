namespace VoltTray.UI.Tray;

public interface IContextMenuView
{
    ContextMenuAction Show(ContextMenuState state, nint ownerHandle = 0);
}
