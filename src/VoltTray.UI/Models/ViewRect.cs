namespace VoltTray.UI.Models;

public readonly record struct ViewRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}
