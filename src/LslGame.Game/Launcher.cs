using System.Numerics;
using Raylib_cs;
using LslGame.Control;
using LslGame.Core;
using LslGame.Models;
using LslGame.Online;
using LslGame.Simulation;
using SharpLSL;

namespace LslGame.Game;

/// <summary>
/// Unified graphical Launcher / Main Menu for calibration, profile management, and BCI gameplay.
/// </summary>
public sealed class Launcher
{
    private readonly GameConfig _cfg;
    private SyntheticEegPublisher? _bgSimPublisher;
    private readonly CancellationTokenSource _cts = new();

    private static readonly Color BgColor       = new(15, 18, 28, 255);
    private static readonly Color CardBg        = new(25, 30, 45, 255);
    private static readonly Color CardHover     = new(35, 42, 65, 255);
    private static readonly Color AccentColor   = new(0, 210, 255, 255);
    private static readonly Color TextColor     = new(240, 240, 240, 255);
    private static readonly Color DimText       = new(150, 155, 175, 255);
    private static readonly Color SuccessColor  = new(80, 240, 100, 255);
    private static readonly Color WarningColor  = new(255, 180, 50, 255);

    public Launcher(GameConfig? config = null)
    {
        _cfg = config ?? new GameConfig();
    }

    public void Run()
    {
        Raylib.InitWindow(_cfg.WindowWidth, _cfg.WindowHeight, "LSL BCI Ball Game - Ana Menu");
        Raylib.SetTargetFPS(_cfg.TargetFps);

        // Ensure at least a default synthetic simulator runs if no hardware EEG is found
        EnsureEegStream();

        int selectedIndex = 0;
        string[] menuItems =
        [
            "1. Yeni Kalibrasyon Yap ve Profil Kaydet",
            "2. Kayitli Profil Sec ve BCI Oyunu Oyna",
            "3. Hizli Oyna (Klavye / Test Modu)",
            "4. Cikis"
        ];

        string statusMessage = "";

        while (!Raylib.WindowShouldClose())
        {
            // Navigation
            if (Raylib.IsKeyPressed(KeyboardKey.Down) || Raylib.IsKeyPressed(KeyboardKey.S))
                selectedIndex = (selectedIndex + 1) % menuItems.Length;
            if (Raylib.IsKeyPressed(KeyboardKey.Up) || Raylib.IsKeyPressed(KeyboardKey.W))
                selectedIndex = (selectedIndex - 1 + menuItems.Length) % menuItems.Length;

            if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.Space))
            {
                if (selectedIndex == 0) // Kalibrasyon
                {
                    RunCalibrationFlow();
                }
                else if (selectedIndex == 1) // Profil ile oyna
                {
                    RunProfileGameFlow();
                }
                else if (selectedIndex == 2) // Hizli baslat
                {
                    RunQuickGameFlow();
                }
                else if (selectedIndex == 3) // Cikis
                {
                    break;
                }
            }

            DrawMenu(menuItems, selectedIndex, statusMessage);
        }

        _cts.Cancel();
        _bgSimPublisher?.Dispose();
        Raylib.CloseWindow();
    }

    private void EnsureEegStream()
    {
        try
        {
            var results = LSL.Resolve($"type='{LslStreamInfo.EegType}'", 1, 1, 0.5);
            if (results.Length == 0)
            {
                Console.WriteLine("[Launcher] Donanim EEG akisi bulunamadi, sentetik X.on simulatiru baslatiliyor...");
                _bgSimPublisher = new SyntheticEegPublisher();
                _ = _bgSimPublisher.RunAsync(_cts.Token);
            }
        }
        catch
        {
            _bgSimPublisher = new SyntheticEegPublisher();
            _ = _bgSimPublisher.RunAsync(_cts.Token);
        }
    }

    private void RunCalibrationFlow()
    {
        var existing = ProfileManager.ListProfiles();
        string subject = $"sub-{(existing.Count + 1):D2}";

        var calib = new CalibrationScreen(
            subjectName: subject,
            width: _cfg.WindowWidth,
            height: _cfg.WindowHeight,
            totalRuns: 2,
            trialsPerRun: 10);

        calib.RunSession();
    }

    private void RunProfileGameFlow()
    {
        var profiles = ProfileManager.ListProfiles();
        if (profiles.Count == 0)
        {
            // Run calibration directly if no profile exists
            RunCalibrationFlow();
            profiles = ProfileManager.ListProfiles();
            if (profiles.Count == 0) return;
        }

        // Use latest profile
        string chosenProfile = profiles[^1];
        var profile = ProfileManager.LoadProfile(chosenProfile);
        if (profile == null) return;

        var (csp, lda) = profile.ToModel();

        // Start online decoder
        using var sender = new ControlSender();
        using var decoder = new OnlineDecoder(csp, lda, sender: sender);

        // Feed online decoder from EEG inlet in background thread
        using var decodeCts = new CancellationTokenSource();
        var decodeTask = Task.Run(() =>
        {
            var results = LSL.Resolve($"type='{LslStreamInfo.EegType}'", 1, 1, 2.0);
            if (results.Length == 0) return;

            using var inlet = new StreamInlet(results[0]);
            var buffer = new float[results[0].ChannelCount];

            while (!decodeCts.IsCancellationRequested)
            {
                double ts = inlet.PullSample(buffer, 0.05);
                if (ts > 0.0)
                {
                    var chunk = new double[1, 7];
                    for (int ch = 0; ch < 7; ch++)
                        chunk[0, ch] = buffer[ch];

                    decoder.PushChunk(chunk);
                    decoder.ClassifyCurrentWindow();
                }
            }
        }, decodeCts.Token);

        // Run game
        using (var game = new BallRollGame(_cfg))
        {
            game.Run();
        }

        decodeCts.Cancel();
        decodeTask.Wait(1000);
    }

    private void RunQuickGameFlow()
    {
        using var keySource = new KeyboardControlSource();
        keySource.StartConsoleLoop(_cts.Token);

        using var game = new BallRollGame(_cfg);
        game.Run();
    }

    private void DrawMenu(string[] items, int selected, string status)
    {
        Raylib.BeginDrawing();
        Raylib.ClearBackground(BgColor);

        float cx = _cfg.WindowWidth / 2f;

        // Title
        string title = "LSL BCI MOTOR IMAGERY OYUNU";
        int titleW = Raylib.MeasureText(title, 28);
        Raylib.DrawText(title, (int)(cx - titleW / 2), 50, 28, TextColor);

        string sub = ".NET 10 / Raylib-cs / X.on Graz-BCI Port";
        int subW = Raylib.MeasureText(sub, 16);
        Raylib.DrawText(sub, (int)(cx - subW / 2), 85, 16, DimText);

        // Profiles info
        var profiles = ProfileManager.ListProfiles();
        string profCount = profiles.Count > 0
            ? $"Kayitli Profiller: {string.Join(", ", profiles)} (Aktif: {profiles[^1]})"
            : "Kayitli Profil: Yok (Ilk olarak kalibrasyon onerilir)";
        int pW = Raylib.MeasureText(profCount, 15);
        Raylib.DrawText(profCount, (int)(cx - pW / 2), 125, 15, profiles.Count > 0 ? SuccessColor : WarningColor);

        // Menu cards
        int startY = 170;
        int cardH = 50;
        int cardW = 500;
        int startX = (int)(cx - cardW / 2);

        for (int i = 0; i < items.Length; i++)
        {
            int y = startY + i * (cardH + 15);
            bool isHovered = (i == selected);

            var color = isHovered ? CardHover : CardBg;
            Raylib.DrawRectangleRounded(new Rectangle(startX, y, cardW, cardH), 0.2f, 4, color);

            if (isHovered)
            {
                Raylib.DrawRectangleRoundedLines(new Rectangle(startX, y, cardW, cardH), 0.2f, 4, AccentColor);
                Raylib.DrawCircle(startX + 25, y + cardH / 2, 6, AccentColor);
            }

            int textX = startX + 45;
            int textY = y + 16;
            Raylib.DrawText(items[i], textX, textY, 18, isHovered ? TextColor : DimText);
        }

        // Instructions
        string hint = "[YUKARI/ASAGI] veya [W/S] ile secin, [ENTER] veya [SPACE] ile baslatin.";
        int hintW = Raylib.MeasureText(hint, 15);
        Raylib.DrawText(hint, (int)(cx - hintW / 2), _cfg.WindowHeight - 40, 15, DimText);

        Raylib.EndDrawing();
    }
}