namespace LslGame.Core;

/// <summary>
/// ASCII marker names used in BCI_Markers LSL stream.
/// All names MUST remain pure ASCII (plan rule §10).
/// </summary>
public static class Markers
{
    // Calibration cue markers (sent by paradigm)
    public const string LeftCue    = "left";
    public const string RightCue   = "right";
    public const string DownCue    = "down";
    public const string UpCue      = "up";
    public const string StopCue    = "stop";
    public const string BothCue    = "both";

    // Session lifecycle markers
    public const string TrialStart  = "trial_start";
    public const string TrialEnd    = "trial_end";
    public const string SessionStart = "session_start";
    public const string SessionEnd   = "session_end";

    // Feedback / game markers
    public const string FeedbackStart = "feedback_start";
    public const string FeedbackEnd   = "feedback_end";

    // Quality / artefact
    public const string BadEpoch    = "bad_epoch";

    // Allowed cue set (keyboard keys l r d u s b)
    public static readonly IReadOnlyList<string> AllCues =
        [LeftCue, RightCue, DownCue, UpCue, StopCue, BothCue];

    /// <summary>Validates that a marker contains only ASCII characters.</summary>
    public static bool IsAscii(string marker) =>
        marker.All(c => c < 128);

    /// <summary>Throws if marker contains non-ASCII characters.</summary>
    public static void AssertAscii(string marker)
    {
        if (!IsAscii(marker))
            throw new ArgumentException($"Marker '{marker}' contains non-ASCII characters. Plan §10 violation.", nameof(marker));
    }
}
