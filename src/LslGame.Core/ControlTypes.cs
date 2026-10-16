namespace LslGame.Core;

public enum ControlMode
{
    Discrete,
    Continuous
}

public enum Direction
{
    Left,
    Right,
    Up,
    Down,
    Rest
}

public sealed class ControlCommand
{
    public ControlMode Mode { get; init; } = ControlMode.Discrete;
    public bool HasSignal { get; init; } = true;
    public Direction Direction { get; init; } = Direction.Rest;
    public float Confidence { get; init; }
    public float Vx { get; init; }
    public float Vy { get; init; }
    public bool IsSimulator { get; init; }
    public float[]? Raw { get; init; }
}
