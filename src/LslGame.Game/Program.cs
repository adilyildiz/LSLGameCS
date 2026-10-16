using LslGame.Control;
using LslGame.Core;
using LslGame.Game;
using LslGame.Simulation;

var config = AppConfig.Load();

Console.WriteLine("========================================");
Console.WriteLine("    LSL BCI Ball Game (.NET 10 / C#)    ");
Console.WriteLine("========================================");
Console.WriteLine($"Ekran: {config.Game.WindowWidth}x{config.Game.WindowHeight} @ {config.Game.TargetFps} FPS");

bool useSim = args.Contains("--sim");
bool useKeyboard = args.Contains("--keyboard");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

SyntheticEegPublisher? simPublisher = null;
KeyboardControlSource? keySource = null;

if (useSim)
{
    Console.WriteLine("[Program] Sentetik EEG yayincisi baslatiliyor (X.on 250 Hz)...");
    simPublisher = new SyntheticEegPublisher();
    _ = simPublisher.RunAsync(cts.Token);
}

if (useKeyboard)
{
    Console.WriteLine("[Program] Klavye kontrol kaynagi baslatiliyor (BCI_Control 20 Hz)...");
    keySource = new KeyboardControlSource();
    keySource.StartConsoleLoop(cts.Token);
}

try
{
    using var game = new BallRollGame(config.Game);
    game.Run();
}
finally
{
    cts.Cancel();
    keySource?.Dispose();
    simPublisher?.Dispose();
}

