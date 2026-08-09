namespace NexMote.Shared.Contracts;

public sealed record DisplayItem(int Index, string Name, int Width, int Height);

public sealed record RemoteScreenInfo(
    int Left,
    int Top,
    int Width,
    int Height,
    int ActiveDisplayIndex = 0,
    DisplayItem[]? Displays = null);
