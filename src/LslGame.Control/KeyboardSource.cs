using SharpLSL;
using LslGame.Core;

namespace LslGame.Control;

/// <summary>
/// Reads keyboard input and pushes commands to the BCI_Control LSL outlet.
/// Mirrors Python control/keyboard_source.py — Öncelik 2.
///
/// Keys: l=left  r=right  d=down  u=up  s=stop  b=both  q=quit
/// </summary>
public sealed class KeyboardSource : IDisposable
{
    private readonly StreamOutlet _outlet;
    private bool _disposed;

    private static readonly IReadOnlyDictionary<ConsoleKey, int> KeyMap =
        new Dictionary<ConsoleKey, int>
        {
            [ConsoleKey.L] = ControlContract.CmdLeft,
            [ConsoleKey.R] = ControlContract.CmdRight,
            [ConsoleKey.D] = ControlContract.CmdDown,
            [ConsoleKey.U] = ControlContract.CmdUp,
            [ConsoleKey.S] = ControlContract.CmdStop,
            [ConsoleKey.B] = ControlContract.CmdBoth,
        };

    public KeyboardSource()
    {
        var info = new StreamInfo(
            name:       LslStreamInfo.ControlName,
            type:       LslStreamInfo.ControlType,
            channelCount: LslStreamInfo.ControlChannels,
            nominalSrate: LslStreamInfo.ControlIrregular,
            channelFormat: ChannelFormat.Int32,
            sourceId:   "keyboard_source_cs");
        _outlet = new StreamOutlet(info);
    }

    /// <summary>
    /// Runs the keyboard loop until 'q' is pressed or <paramref name="ct"/> is cancelled.
    /// </summary>
    public Task RunAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        Console.WriteLine("[KeyboardSource] Keys: L R D U S B  |  Q=quit");
        while (!ct.IsCancellationRequested)
        {
            if (!Console.KeyAvailable) { Thread.Sleep(10); continue; }
            var key = Console.ReadKey(intercept: true).Key;
            if (key == ConsoleKey.Q) break;
            if (KeyMap.TryGetValue(key, out int cmd))
            {
                _outlet.PushSample(new[] { cmd });
                var marker = ControlContract.CommandToMarker(cmd);
                Console.WriteLine($"[KeyboardSource] pushed cmd={cmd} ({marker})");
            }
        }
    }, ct);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _outlet.Dispose();
    }
}
