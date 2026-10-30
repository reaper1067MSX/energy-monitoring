namespace VoltTray.Core.Models;

public enum TaskbarEdge
{
    Bottom = 0,
    Top = 1,
    Left = 2,
    Right = 3
}

public enum ExitDialogResult
{
    Cancel = 0,
    Pause = 1,
    Close = 2
}

public readonly record struct ViewRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}
