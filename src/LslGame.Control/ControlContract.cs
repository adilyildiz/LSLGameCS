namespace LslGame.Control;

/// <summary>
/// Defines the LSL control contract between BCI decoder and game.
/// Mirrors Python control/contract.py — Öncelik 1.
/// </summary>
public sealed class ControlContract
{
    // ---- Command codes (int payload in BCI_Control stream) ----
    public const int CmdNone   = 0;
    public const int CmdLeft   = 1;
    public const int CmdRight  = 2;
    public const int CmdDown   = 3;
    public const int CmdUp     = 4;
    public const int CmdStop   = 5;
    public const int CmdBoth   = 6;

    // ---- Timing constraints (seconds) ----
    /// <summary>Maximum allowed marker→EEG alignment jitter (ms).</summary>
    public const double MaxAlignmentJitterMs = 12.0;
    public const double MinAlignmentJitterMs = -4.0;

    // ---- Command ↔ marker mapping ----
    private static readonly IReadOnlyDictionary<string, int> MarkerToCmd =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [Core.Markers.LeftCue]  = CmdLeft,
            [Core.Markers.RightCue] = CmdRight,
            [Core.Markers.DownCue]  = CmdDown,
            [Core.Markers.UpCue]    = CmdUp,
            [Core.Markers.StopCue]  = CmdStop,
            [Core.Markers.BothCue]  = CmdBoth,
        };

    private static readonly IReadOnlyDictionary<int, string> CmdToMarker =
        MarkerToCmd.ToDictionary(kv => kv.Value, kv => kv.Key);

    public static int MarkerToCommand(string marker) =>
        MarkerToCmd.TryGetValue(marker, out var cmd) ? cmd : CmdNone;

    public static string CommandToMarker(int cmd) =>
        CmdToMarker.TryGetValue(cmd, out var marker) ? marker : string.Empty;

    public static bool IsValidCommand(int cmd) => cmd is >= CmdNone and <= CmdBoth;

    /// <summary>Checks alignment jitter is within contract bounds (ms).</summary>
    public static bool IsAlignmentValid(double jitterMs) =>
        jitterMs >= MinAlignmentJitterMs && jitterMs <= MaxAlignmentJitterMs;
}
