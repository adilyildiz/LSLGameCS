using LslGame.Core;
using LslGame.Game;

var config = AppConfig.Load();

Console.WriteLine("=== LSL BCI Ball Game (C#) ===");
Console.WriteLine($"Window: {config.Game.WindowWidth}x{config.Game.WindowHeight}");
Console.WriteLine("Press Ctrl+C to exit.");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

using var game = new BallRollGame(config.Game);
game.Run();
