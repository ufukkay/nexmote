namespace NexMote.Shared.Contracts;

public sealed record RemoteInputEvent(
    Guid SessionId,
    string Kind,
    int X = 0,
    int Y = 0,
    string? Button = null,
    bool IsDown = false,
    int KeyCode = 0,
    int WheelDelta = 0);
