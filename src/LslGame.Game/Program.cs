using LslGame.Core;
using LslGame.Game;

var config = AppConfig.Load();

Console.WriteLine("========================================");
Console.WriteLine("    LSL BCI Ball Game (.NET 10 / C#)    ");
Console.WriteLine("========================================");

var launcher = new Launcher(config.Game);
launcher.Run();


