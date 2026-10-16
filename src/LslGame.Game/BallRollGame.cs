using System.Numerics;
using Raylib_cs;
using LslGame.Core;
using LslGame.Control;
using SharpLSL;

namespace LslGame.Game;

/// <summary>
/// ASCII marker publisher for BCI_Markers stream.
/// </summary>
public sealed class MarkerPublisher : IDisposable
{
    private readonly StreamOutlet _outlet;
    private bool _disposed;

    public MarkerPublisher(string streamName = LslStreamInfo.MarkerName, string sourceId = "Ball_Roll_Markers_CS")
    {
        var info = new StreamInfo(
            name:          streamName,
            type:          LslStreamInfo.MarkerType,
            channelCount:  1,
            nominalSrate:  0.0,
            channelFormat: ChannelFormat.String,
            sourceId:      sourceId);
        _outlet = new StreamOutlet(info);
    }

    public void Push(string marker, double? timestamp = null)
    {
        Markers.AssertAscii(marker);
        double ts = timestamp ?? LSL.GetLocalClock();
        _outlet.PushSample([marker], ts);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _outlet.Dispose();
    }
}

/// <summary>
/// Fully headless testable ball roll game logic.
/// Matches Python BallRollGameLogic in src/game/ball_roll.py.
/// </summary>
public sealed class BallRollGameLogic
{
    public float ArenaWidth { get; }
    public float ArenaHeight { get; }
    public float BallSpeed { get; }
    public float BallRadius { get; }
    public float TargetRadius { get; }
    public float TrialTimeout { get; }
    public float InterTrialPause { get; }

    private readonly MarkerPublisher? _markerPub;
    private readonly Random _rng;

    public float CenterX => ArenaWidth / 2f;
    public float CenterY => ArenaHeight / 2f;

    public float BallX { get; private set; }
    public float BallY { get; private set; }

    public int Hits { get; private set; }
    public int Misses { get; private set; }
    public int Score { get; private set; }
    public int TrialCount { get; private set; }
    public string State { get; private set; } = "active"; // "active" or "pause"
    public float TrialElapsed { get; private set; }
    public float PauseElapsed { get; private set; }
    public string? LastOutcome { get; private set; } // "hit", "miss", "timeout"

    public string TargetSide { get; private set; } = "left";
    public float TargetX { get; private set; }
    public float TargetY { get; private set; }

    public double Accuracy
    {
        get
        {
            int total = Hits + Misses;
            return total == 0 ? 0.0 : ((double)Hits / total) * 100.0;
        }
    }

    public bool IsPaused => State == "pause";

    private readonly List<string> _targetHistory = [];
    private readonly List<string> _targetQueue = [];

    public BallRollGameLogic(
        float arenaWidth = 800f,
        float arenaHeight = 500f,
        float ballSpeed = 240f,
        float ballRadius = 22f,
        float targetRadius = 35f,
        float trialTimeout = 10f,
        float interTrialPause = 1.5f,
        MarkerPublisher? markerPub = null,
        int? randomSeed = null)
    {
        ArenaWidth = arenaWidth;
        ArenaHeight = arenaHeight;
        BallSpeed = ballSpeed;
        BallRadius = ballRadius;
        TargetRadius = targetRadius;
        TrialTimeout = trialTimeout;
        InterTrialPause = interTrialPause;
        _markerPub = markerPub;
        _rng = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();

        BallX = CenterX;
        BallY = CenterY;

        _markerPub?.Push(Markers.GameStart);
        SpawnNewTarget();
    }

    private List<string> GenerateBlock()
    {
        string[][] permutations =
        [
            ["left", "left", "right", "right"],
            ["left", "right", "left", "right"],
            ["left", "right", "right", "left"],
            ["right", "left", "left", "right"],
            ["right", "left", "right", "left"],
            ["right", "right", "left", "left"]
        ];

        var validPerms = new List<string[]>();
        foreach (var p in permutations)
        {
            var combined = new List<string>();
            if (_targetHistory.Count >= 3)
                combined.AddRange(_targetHistory.TakeLast(3));
            else
                combined.AddRange(_targetHistory);
            combined.AddRange(p);

            bool hasRunGt3 = false;
            int run = 1;
            for (int i = 1; i < combined.Count; i++)
            {
                if (combined[i] == combined[i - 1])
                {
                    run++;
                    if (run > 3) { hasRunGt3 = true; break; }
                }
                else run = 1;
            }

            if (!hasRunGt3) validPerms.Add(p);
        }

        var chosen = validPerms.Count > 0
            ? validPerms[_rng.Next(validPerms.Count)]
            : permutations[_rng.Next(permutations.Length)];

        return new List<string>(chosen);
    }

    public string SpawnNewTarget(string? side = null)
    {
        if (side == null)
        {
            if (_targetQueue.Count == 0)
                _targetQueue.AddRange(GenerateBlock());

            side = _targetQueue[0];
            _targetQueue.RemoveAt(0);
        }

        TargetSide = side;
        _targetHistory.Add(side);

        float margin = 100f;
        TargetX = TargetSide == "left" ? margin : ArenaWidth - margin;
        TargetY = CenterY;
        BallX = CenterX;
        BallY = CenterY;

        TrialCount++;
        TrialElapsed = 0f;
        PauseElapsed = 0f;
        State = "active";

        if (_markerPub != null)
        {
            _markerPub.Push(Markers.TrialStart);
            string cueMarker = TargetSide == "left" ? Markers.CueLeft : Markers.CueRight;
            _markerPub.Push(cueMarker);
        }

        return TargetSide;
    }

    public bool Update(float dt, ControlCommand cmd)
    {
        if (State == "pause")
        {
            PauseElapsed += dt;
            BallX = CenterX;
            BallY = CenterY;
            if (PauseElapsed >= InterTrialPause)
                SpawnNewTarget();
            return false;
        }

        TrialElapsed += dt;
        if (TrialElapsed >= TrialTimeout)
        {
            Misses++;
            LastOutcome = "timeout";
            if (_markerPub != null)
            {
                _markerPub.Push(Markers.TrialTimeout);
                _markerPub.Push(Markers.TrialEnd);
            }
            State = "pause";
            PauseElapsed = 0f;
            BallX = CenterX;
            BallY = CenterY;
            return false;
        }

        float vx = 0f;
        if (cmd.HasSignal)
        {
            if (MathF.Abs(cmd.Vx) > 0.01f)
                vx = cmd.Vx;
            else if (cmd.Direction == Direction.Left)
                vx = -1f;
            else if (cmd.Direction == Direction.Right)
                vx = 1f;
        }

        BallX += vx * BallSpeed * dt;

        float minX = BallRadius;
        float maxX = ArenaWidth - BallRadius;
        BallX = Math.Clamp(BallX, minX, maxX);

        float distToTarget = MathF.Abs(BallX - TargetX);
        float hitThreshold = BallRadius + TargetRadius;

        if (distToTarget <= hitThreshold)
        {
            Hits++;
            Score = Hits;
            LastOutcome = "hit";
            if (_markerPub != null)
            {
                _markerPub.Push(Markers.TargetHit);
                _markerPub.Push(Markers.TrialEnd);
            }
            State = "pause";
            PauseElapsed = 0f;
            BallX = CenterX;
            BallY = CenterY;
            return true;
        }

        bool isWrongWall = (TargetSide == "right" && BallX <= minX) ||
                           (TargetSide == "left" && BallX >= maxX);
        if (isWrongWall)
        {
            Misses++;
            LastOutcome = "miss";
            if (_markerPub != null)
            {
                _markerPub.Push(Markers.TargetMiss);
                _markerPub.Push(Markers.TrialEnd);
            }
            State = "pause";
            PauseElapsed = 0f;
            BallX = CenterX;
            BallY = CenterY;
            return false;
        }

        return false;
    }
}

/// <summary>
/// Raylib-cs based Ball Roll Game UI.
/// Matches Python BallRollGame in src/game/ball_roll.py.
/// </summary>
public sealed class BallRollGame : IDisposable
{
    private readonly GameConfig _cfg;
    private readonly BallRollGameLogic _logic;
    private readonly ControlReceiver? _controlReceiver;
    private readonly MarkerPublisher? _markerPub;
    private readonly bool _ownsReceiver;
    private readonly bool _ownsMarkerPub;
    private bool _disposed;

    // Palette matching Python game
    private static readonly Color BgColor       = new(20, 20, 30, 255);
    private static readonly Color ArenaColor    = new(30, 32, 45, 255);
    private static readonly Color BallColor     = new(80, 200, 255, 255);
    private static readonly Color BallHalo      = new(80, 200, 255, 60);
    private static readonly Color TargetColor   = new(255, 200, 50, 255);
    private static readonly Color TargetHalo    = new(255, 200, 50, 50);
    private static readonly Color TextColor     = new(240, 240, 240, 255);
    private static readonly Color CueColor      = new(0, 220, 255, 255);
    private static readonly Color HitColor      = new(80, 240, 80, 255);
    private static readonly Color MissColor     = new(255, 80, 80, 255);
    private static readonly Color TimeoutColor  = new(255, 160, 40, 255);

    public BallRollGame(
        GameConfig? config = null,
        ControlReceiver? receiver = null,
        MarkerPublisher? markerPub = null)
    {
        _cfg = config ?? new GameConfig();

        if (markerPub == null)
        {
            _markerPub = new MarkerPublisher();
            _ownsMarkerPub = true;
        }
        else
        {
            _markerPub = markerPub;
            _ownsMarkerPub = false;
        }

        _logic = new BallRollGameLogic(
            arenaWidth: _cfg.WindowWidth,
            arenaHeight: _cfg.WindowHeight,
            markerPub: _markerPub);

        if (receiver == null)
        {
            try
            {
                _controlReceiver = new ControlReceiver(timeout: 2.0);
                _ownsReceiver = true;
            }
            catch
            {
                Console.WriteLine("[BallRollGame] Warning: BCI_Control stream not detected. Running without receiver.");
                _controlReceiver = null;
            }
        }
        else
        {
            _controlReceiver = receiver;
            _ownsReceiver = false;
        }
    }

    public void Run()
    {
        bool ownsWindow = false;
        if (!Raylib.IsWindowReady())
        {
            Raylib.InitWindow((int)_logic.ArenaWidth, (int)_logic.ArenaHeight, _cfg.Title);
            Raylib.SetTargetFPS(_cfg.TargetFps);
            ownsWindow = true;
        }
        else
        {
            Raylib.SetWindowTitle(_cfg.Title);
        }

        while (!Raylib.WindowShouldClose())
        {
            if (Raylib.IsKeyPressed(KeyboardKey.Escape))
                break;

            float dt = Raylib.GetFrameTime();

            // Keyboard direct fallback / override
            var cmd = _controlReceiver?.GetCommand(timeout: 0.0) ?? new ControlCommand();
            if (Raylib.IsKeyDown(KeyboardKey.Left) || Raylib.IsKeyDown(KeyboardKey.A))
                cmd = new ControlCommand { Direction = Direction.Left, Vx = -1f, HasSignal = true };
            else if (Raylib.IsKeyDown(KeyboardKey.Right) || Raylib.IsKeyDown(KeyboardKey.D))
                cmd = new ControlCommand { Direction = Direction.Right, Vx = 1f, HasSignal = true };

            _logic.Update(dt, cmd);
            Draw(cmd);
        }

        _markerPub?.Push(Markers.GameEnd);
        if (ownsWindow)
        {
            Raylib.CloseWindow();
        }
    }

    private void Draw(ControlCommand cmd)
    {
        Raylib.BeginDrawing();
        Raylib.ClearBackground(BgColor);

        // Arena subtle border
        Raylib.DrawRectangleLines(10, 10, (int)_logic.ArenaWidth - 20, (int)_logic.ArenaHeight - 20, new Color(40, 45, 65, 255));

        // 1. Draw the Rolling Plane / Runway Track
        DrawTrack();

        // 2. Target (Beacon Glow + Dock + Body)
        var targetPos = new Vector2(_logic.TargetX, _logic.TargetY);
        float targetPulse = (float)(Math.Sin(Raylib.GetTime() * 4.0) * 3.0);
        Raylib.DrawCircleV(targetPos, _logic.TargetRadius + 14f + targetPulse, TargetHalo);
        Raylib.DrawCircleV(targetPos, _logic.TargetRadius, TargetColor);
        Raylib.DrawCircleLines((int)targetPos.X, (int)targetPos.Y, _logic.TargetRadius, new Color(255, 240, 160, 220));

        // 3. Ball (Ground shadow + Halo + Body + Specular highlight)
        var ballPos = new Vector2(_logic.BallX, _logic.BallY);
        // Ground shadow directly under the ball onto the track plane
        Raylib.DrawEllipse((int)ballPos.X, (int)(ballPos.Y + _logic.BallRadius - 1), _logic.BallRadius * 0.9f, 6f, new Color(8, 10, 18, 200));

        // Ball glow & sphere
        Raylib.DrawCircleV(ballPos, _logic.BallRadius + 10f, BallHalo);
        Raylib.DrawCircleV(ballPos, _logic.BallRadius, BallColor);
        Raylib.DrawCircleLines((int)ballPos.X, (int)ballPos.Y, _logic.BallRadius, new Color(200, 245, 255, 200));
        // Specular highlight for 3D sphere feel
        Raylib.DrawCircle((int)(ballPos.X - 5), (int)(ballPos.Y - 5), 4.5f, new Color(255, 255, 255, 140));

        // 4. Cue Direction / Arrow (Center top)
        string cueText = _logic.TargetSide == "left" ? "SOLA GİT" : "SAĞA GİT";
        var textSize = GameFonts.MeasureText(cueText, 24);
        float cueX = _logic.CenterX - textSize.X / 2f;
        GameFonts.DrawText(cueText, cueX, 85, 24, CueColor);

        // Arrow triangle
        float arrowY = 97;
        if (_logic.TargetSide == "left")
        {
            var p1 = new Vector2(cueX - 25, arrowY);
            var p2 = new Vector2(cueX - 10, arrowY - 10);
            var p3 = new Vector2(cueX - 10, arrowY + 10);
            Raylib.DrawTriangle(p1, p3, p2, CueColor);
        }
        else
        {
            var p1 = new Vector2(cueX + textSize.X + 25, arrowY);
            var p2 = new Vector2(cueX + textSize.X + 10, arrowY + 10);
            var p3 = new Vector2(cueX + textSize.X + 10, arrowY - 10);
            Raylib.DrawTriangle(p1, p3, p2, CueColor);
        }

        // 5. HUD - Row 1
        string stats = $"İsabet: {_logic.Hits}  |  İska: {_logic.Misses}  |  Doğruluk: %{_logic.Accuracy:F0}";
        GameFonts.DrawText(stats, 25, 20, 18, TextColor);

        string cmdStr = cmd.HasSignal
            ? $"BCI Komut: {(cmd.Direction == Direction.Left ? "SOL" : cmd.Direction == Direction.Right ? "SAĞ" : "DİNLENME")}"
            : "BCI: SİNYAL YOK";
        GameFonts.DrawText(cmdStr, _logic.ArenaWidth - 260, 20, 18, cmd.HasSignal ? Color.LightGray : MissColor);

        // 6. HUD - Row 2
        string targetLabel = $"Hedef: {(_logic.TargetSide == "left" ? "SOL" : "SAĞ")}";
        GameFonts.DrawText(targetLabel, 25, 48, 18, TextColor);

        if (cmd.IsSimulator)
            GameFonts.DrawText("SİMÜLASYON", _logic.ArenaWidth - 260, 48, 18, Color.Yellow);

        // Return hint at bottom
        GameFonts.DrawText("[ESC] Menüye Dön", 25, _logic.ArenaHeight - 32, 14, new Color(130, 135, 155, 255));

        // Outcome feedback banner during pause
        if (_logic.IsPaused && _logic.LastOutcome != null)
        {
            string outcomeText = _logic.LastOutcome switch
            {
                "hit"     => "İSABET!",
                "miss"    => "ISKA!",
                "timeout" => "ZAMAN AŞIMI!",
                _         => "Dinlenme..."
            };
            Color outcomeColor = _logic.LastOutcome switch
            {
                "hit"     => HitColor,
                "miss"    => MissColor,
                "timeout" => TimeoutColor,
                _         => TextColor
            };
            var bannerSize = GameFonts.MeasureText(outcomeText, 32);
            GameFonts.DrawText(outcomeText, _logic.CenterX - bannerSize.X / 2f, _logic.CenterY - 40, 32, outcomeColor);
        }

        Raylib.EndDrawing();
    }

    private void DrawTrack()
    {
        float cy = _logic.CenterY;
        float cx = _logic.CenterX;
        float trackX = 40f;
        float trackW = _logic.ArenaWidth - 80f;
        float trackH = 104f;
        float trackY = cy - trackH / 2f;

        // 1. Track Outer Bed / Platform (Glassmorphic tech slab)
        var outerRect = new Rectangle(trackX, trackY, trackW, trackH);
        Raylib.DrawRectangleRounded(outerRect, 0.15f, 6, new Color(24, 28, 44, 255));
        Raylib.DrawRectangleRoundedLines(outerRect, 0.15f, 6, new Color(42, 48, 72, 255));

        // 2. Inner Rolling Trench / Channel (deeper groove)
        float trenchH = 70f;
        float trenchY = cy - trenchH / 2f;
        var trenchRect = new Rectangle(trackX + 6, trenchY, trackW - 12, trenchH);
        Raylib.DrawRectangleRounded(trenchRect, 0.12f, 6, new Color(15, 18, 30, 255));
        Raylib.DrawRectangleRoundedLines(trenchRect, 0.12f, 6, new Color(30, 36, 56, 255));

        // 3. Glowing Neon Guide Rails (Top and Bottom)
        float railTopY = cy - 42f;
        float railBotY = cy + 42f;
        float railStartX = trackX + 15;
        float railEndX = trackX + trackW - 15;

        // Glow halos for rails
        Raylib.DrawLineEx(new Vector2(railStartX, railTopY), new Vector2(railEndX, railTopY), 4f, new Color(0, 180, 240, 45));
        Raylib.DrawLineEx(new Vector2(railStartX, railBotY), new Vector2(railEndX, railBotY), 4f, new Color(0, 180, 240, 45));

        // Crisp neon rail cores
        Raylib.DrawLineEx(new Vector2(railStartX, railTopY), new Vector2(railEndX, railTopY), 1.5f, new Color(0, 210, 255, 170));
        Raylib.DrawLineEx(new Vector2(railStartX, railBotY), new Vector2(railEndX, railBotY), 1.5f, new Color(0, 210, 255, 170));

        // 4. Metric ticks / graduation marks along the track rails
        for (float x = trackX + 60; x <= trackX + trackW - 60; x += 30f)
        {
            Raylib.DrawLineV(new Vector2(x, railTopY), new Vector2(x, railTopY + 5), new Color(70, 90, 130, 140));
            Raylib.DrawLineV(new Vector2(x, railBotY), new Vector2(x, railBotY - 5), new Color(70, 90, 130, 140));
        }

        // 5. Centerline: Dashed guiding stripe along CenterY
        for (float x = trackX + 60; x < trackX + trackW - 60; x += 28f)
        {
            Raylib.DrawLineV(new Vector2(x, cy), new Vector2(x + 14, cy), new Color(50, 68, 105, 120));
        }

        // 6. Center Origin Dock / Neutral Starting Zone
        Raylib.DrawCircleLines((int)cx, (int)cy, 28f, new Color(0, 200, 255, 60));
        Raylib.DrawCircleLines((int)cx, (int)cy, 12f, new Color(0, 200, 255, 40));
        Raylib.DrawLineV(new Vector2(cx, cy - 20), new Vector2(cx, cy + 20), new Color(0, 200, 255, 50));

        // 7. Dynamic Directional Runway Chevrons (Flowing towards target)
        bool isLeft = _logic.TargetSide == "left";
        float time = (float)Raylib.GetTime();
        float pulse = (float)(Math.Sin(time * 5.0) * 0.5 + 0.5);

        float flowStart = cx;
        float flowEnd = isLeft ? 100f : _logic.ArenaWidth - 100f;
        int numChevrons = 5;

        for (int i = 1; i <= numChevrons; i++)
        {
            float t = (float)i / (numChevrons + 1);
            float chX = flowStart + (flowEnd - flowStart) * t;

            // Offset with time to create animated flowing runner effect
            float animOffset = isLeft ? -((time * 40f) % 30f) : ((time * 40f) % 30f);
            float drawX = chX + animOffset;

            if (drawX > 120 && drawX < _logic.ArenaWidth - 120)
            {
                int alpha = (int)(80 + 90 * pulse);
                var arrowColor = new Color(0, 210, 255, alpha);
                float size = 8f;
                if (isLeft)
                {
                    Raylib.DrawLineEx(new Vector2(drawX + size, cy - size), new Vector2(drawX, cy), 2f, arrowColor);
                    Raylib.DrawLineEx(new Vector2(drawX, cy), new Vector2(drawX + size, cy + size), 2f, arrowColor);
                }
                else
                {
                    Raylib.DrawLineEx(new Vector2(drawX - size, cy - size), new Vector2(drawX, cy), 2f, arrowColor);
                    Raylib.DrawLineEx(new Vector2(drawX, cy), new Vector2(drawX - size, cy + size), 2f, arrowColor);
                }
            }
        }

        // 8. Goal Sockets / Receptacle Docks on Left and Right
        DrawGoalDock(100f, cy, isLeft);
        DrawGoalDock(_logic.ArenaWidth - 100f, cy, !isLeft);
    }

    private void DrawGoalDock(float x, float y, bool isActiveTarget)
    {
        // Receptacle base ring
        Color dockColor = isActiveTarget ? new Color(255, 200, 50, 160) : new Color(45, 55, 80, 120);
        Color dockGlow = isActiveTarget ? new Color(255, 200, 50, 40) : new Color(20, 25, 40, 80);

        Raylib.DrawCircleV(new Vector2(x, y), 48f, dockGlow);
        Raylib.DrawCircleLines((int)x, (int)y, 48f, dockColor);
        Raylib.DrawCircleLines((int)x, (int)y, 44f, new Color((int)dockColor.R, (int)dockColor.G, (int)dockColor.B, 60));

        // Subtly label target zone
        if (isActiveTarget)
        {
            string label = "HEDEF BÖLGESİ";
            float lw = GameFonts.MeasureText(label, 13).X;
            GameFonts.DrawText(label, x - lw / 2f, y + 54, 13, TargetColor);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsReceiver) _controlReceiver?.Dispose();
        if (_ownsMarkerPub) _markerPub?.Dispose();
    }
}