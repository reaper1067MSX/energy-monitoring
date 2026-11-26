namespace VoltTray.UI.Atoms;

public interface INativeIconFactory
{
    IntPtr CreateFromArgbPixels(uint[] pixels, int size);

    void DestroyIcon(IntPtr iconHandle);
}
