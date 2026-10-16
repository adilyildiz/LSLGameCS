using LslGame.Core;

namespace LslGame.Control;

/// <summary>
/// Converts keyboard input into BCI_Control stream samples.
/// Used for independent testing and debugging of the game engine.
/// Mirrors Python control/keyboard_source.py — Öncelik 2.
/// </summary>
public sealed class KeyboardControlSource : IDisposable
{
    private readonly ControlSender _sender;
    private readonly Lock _lock = new();
    private bool _keyLeft;
    private bool _keyRight;
    private bool _keyUp;
    private bool _keyDown;
    private bool _disposed;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loopTask;

    public ControlMode Mode => _sender.Mode;
    public double SampleRate => _sender.SampleRate;

    public KeyboardControlSource(
        ControlMode mode = ControlMode.Discrete,
        double srate = LslStreamInfo.ControlSampleRate,
        string sourceId = "Keyboard_Control_Source")
    {
        _sender = new ControlSender(mode, srate, sourceId);
    }

    public void SetKeys(bool left = false, bool right = false, bool up = false, bool down = false)
    {
        lock (_lock)
        {
            _keyLeft = left;
            _keyRight = right;
            _keyUp = up;
            _keyDown = down;
        }
    }

    public void Step()
    {
        bool l, r, u, d;
        lock (_lock)
        {
            l = _keyLeft;
            r = _keyRight;
            u = _keyUp;
            d = _keyDown;
        }

        if (Mode == ControlMode.Discrete)
        {
            if (l)
                _sender.SendDiscrete(pLeft: 1f, pRight: 0f, pUp: 0f, pDown: 0f, pRest: 0f);
            else if (r)
                _sender.SendDiscrete(pLeft: 0f, pRight: 1f, pUp: 0f, pDown: 0f, pRest: 0f);
            else if (u)
                _sender.SendDiscrete(pLeft: 0f, pRight: 0f, pUp: 1f, pDown: 0f, pRest: 0f);
            else if (d)
                _sender.SendDiscrete(pLeft: 0f, pRight: 0f, pUp: 0f, pDown: 1f, pRest: 0f);
            else
                _sender.SendDiscrete(pLeft: 0f, pRight: 0f, pUp: 0f, pDown: 0f, pRest: 1f);
        }
        else
        {
            float vx = 0f;
            float vy = 0f;
            if (l) vx -= 1f;
            if (r) vx += 1f;
            if (u) vy += 1f;
            if (d) vy -= 1f;
            _sender.SendContinuous(vx, vy);
        }
    }

    public void StartConsoleLoop(CancellationToken externalCt = default)
    {
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, externalCt);
        var ct = linkedCts.Token;

        _loopTask = Task.Run(async () =>
        {
            int intervalMs = (int)(1000.0 / SampleRate);
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Max(1, intervalMs)));

            Console.WriteLine("[KeyboardControlSource] Use Left/Right/Up/Down arrows or A/D/W/S. Press Q to exit.");

            while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true).Key;
                    if (key == ConsoleKey.Q) break;

                    SetKeys(
                        left:  key is ConsoleKey.LeftArrow or ConsoleKey.A or ConsoleKey.L,
                        right: key is ConsoleKey.RightArrow or ConsoleKey.D or ConsoleKey.R,
                        up:    key is ConsoleKey.UpArrow or ConsoleKey.W or ConsoleKey.U,
                        down:  key is ConsoleKey.DownArrow or ConsoleKey.S);
                }

                Step();
            }
        }, ct);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _sender.Dispose();
        _cts.Dispose();
    }
}

