namespace VoltTray.UI.Popover;

public interface IPopoverSurfaceRenderer : IDisposable
{
    byte[] RenderBgra32(PopoverRenderState state);

    byte[] RenderPng(PopoverRenderState state);
}
