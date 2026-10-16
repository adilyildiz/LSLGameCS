using System.Numerics;
using Raylib_cs;
using LslGame.Core;
using LslGame.Control;
using SharpLSL;

namespace LslGame.Game;

/// <summary>
/// BCI-controlled ball rolling game using Raylib-cs.
/// Mirrors Python game/ball_roll.py — Öncelik 2.
///
/// Subscribes to BCI_Control LSL stream and moves ball accordingly.
/// Publishes BCI_Markers with feedback markers.
/// </summary>
public sealed class BallRollGame : IDisposable
{
    private readonly GameConfig _cfg;
    private readonly StreamInlet _controlInlet;
    private readonly StreamOutlet _markerOutlet;
    private bool _disposed;

    // Ball state
    private Vector2 _pos;
    private Vector2 _vel;
    private const float Speed     = 200f;  // pixels/s
    private const float Damping   = 0.92f;
    private const float BallRadius = 20f;

    // Colour scheme
    private static readonly Color BgColor   = new(15, 15, 30, 255);
    private static readonly Color BallColor = new(80, 200, 255, 255);
    private static readonly Color GridColor = new(30, 30, 60, 255);

    public BallRollGame(GameConfig? config = null)
    {
        _cfg = config ?? new GameConfig();

        // Resolve BCI_Control inlet
        Console.WriteLine("[BallRollGame] Resolving BCI_Control stream...");
        var results = LSL.Resolve($"name='{LslStreamInfo.ControlName}'", 1, 1, 5.0);
        if (results.Length == 0)
            throw new TimeoutException("BCI_Control stream not found.");
        _controlInlet = new StreamInlet(results[0]);

        // Marker outlet
        var mInfo = new StreamInfo(
            LslStreamInfo.MarkerName, LslStreamInfo.MarkerType,
            LslStreamInfo.MarkerChannels, LslStreamInfo.MarkerIrregular,
            ChannelFormat.String, "ball_roll_game_cs");
        _markerOutlet = new StreamOutlet(mInfo);
    }

    public void Run()
    {
        Raylib.InitWindow(_cfg.WindowWidth, _cfg.WindowHeight, _cfg.Title);
        Raylib.SetTargetFPS(_cfg.TargetFps);

        _pos = new Vector2(_cfg.WindowWidth / 2f, _cfg.WindowHeight / 2f);
        _vel = Vector2.Zero;

        PushMarker(Markers.SessionStart);

        while (!Raylib.WindowShouldClose())
        {
            PollControl();
            Update(Raylib.GetFrameTime());
            Draw();
        }

        PushMarker(Markers.SessionEnd);
        Raylib.CloseWindow();
    }

    private void PollControl()
    {
        var sample = new int[1];
        while (_controlInlet.PullSample(sample, 0.0) != 0.0)
        {
            int cmd = sample[0];
            PushMarker(Markers.FeedbackStart);
            switch (cmd)
            {
                case ControlContract.CmdLeft:  _vel.X -= Speed; break;
                case ControlContract.CmdRight: _vel.X += Speed; break;
                case ControlContract.CmdUp:    _vel.Y -= Speed; break;
                case ControlContract.CmdDown:  _vel.Y += Speed; break;
                case ControlContract.CmdStop:  _vel = Vector2.Zero; break;
                case ControlContract.CmdBoth:
                    _vel.X = 0; _vel.Y -= Speed; break;
            }
        }
    }

    private void Update(float dt)
    {
        _vel.X *= Damping;
        _vel.Y *= Damping;
        _pos   += _vel * dt;

        // Bounce off walls
        float w = _cfg.WindowWidth, h = _cfg.WindowHeight;
        if (_pos.X < BallRadius)          { _pos.X = BallRadius;      _vel.X = MathF.Abs(_vel.X); }
        if (_pos.X > w - BallRadius)      { _pos.X = w - BallRadius;  _vel.X = -MathF.Abs(_vel.X); }
        if (_pos.Y < BallRadius)          { _pos.Y = BallRadius;      _vel.Y = MathF.Abs(_vel.Y); }
        if (_pos.Y > h - BallRadius)      { _pos.Y = h - BallRadius;  _vel.Y = -MathF.Abs(_vel.Y); }
    }

    private void Draw()
    {
        Raylib.BeginDrawing();
        Raylib.ClearBackground(BgColor);

        // Grid lines
        int step = 64;
        for (int x = 0; x < _cfg.WindowWidth; x += step)
            Raylib.DrawLine(x, 0, x, _cfg.WindowHeight, GridColor);
        for (int y = 0; y < _cfg.WindowHeight; y += step)
            Raylib.DrawLine(0, y, _cfg.WindowWidth, y, GridColor);

        // Ball with glow
        Raylib.DrawCircleV(_pos, BallRadius + 6, new Color(80, 200, 255, 40));
        Raylib.DrawCircleV(_pos, BallRadius + 3, new Color(80, 200, 255, 80));
        Raylib.DrawCircleV(_pos, BallRadius,      BallColor);

        // HUD
        Raylib.DrawText("BCI Ball Game", 10, 10, 20, Color.White);
        Raylib.DrawText($"vel: ({_vel.X:+000;-000}, {_vel.Y:+000;-000})", 10, 36, 16, Color.Gray);
        Raylib.DrawFPS(10, _cfg.WindowHeight - 24);

        Raylib.EndDrawing();
    }

    private void PushMarker(string marker)
    {
        Markers.AssertAscii(marker);
        _markerOutlet.PushSample(new[] { marker });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _controlInlet.Dispose();
        _markerOutlet.Dispose();
    }
}



