namespace LslGame.Core;

/// <summary>
/// ASCII marker names used in BCI_Markers LSL stream.
/// All names MUST remain pure ASCII (plan rule §10).
/// </summary>
public static class Markers
{
    // ---- Calibration cue markers (sent by paradigm) ----
    public const string CueLeft       = "cue_left";
    public const string CueRight      = "cue_right";
    public const string CueUp         = "cue_up";
    public const string CueDown       = "cue_down";
    public const string CueRest       = "cue_rest";
    public const string CueFeet       = "cue_feet";
    public const string CueBothHands  = "cue_both_hands";
    public const string CueArithmetic = "cue_arithmetic";

    // ---- Calibration lifecycle markers ----
    public const string SessionStart  = "session_start";
    public const string SessionEnd    = "session_end";
    public const string RunStart      = "run_start";
    public const string RunEnd        = "run_end";
    public const string TrialStart    = "trial_start";
    public const string TrialEnd      = "trial_end";
    public const string FixationStart = "fixation_start";
    public const string ImageryStart  = "imagery_start";
    public const string ImageryEnd    = "imagery_end";
    public const string RestStart     = "rest_start";
    public const string PauseStart    = "pause_start";
    public const string PauseEnd      = "pause_end";

    // ---- Game event markers (sent by ball roll game) ----
    public const string GameStart     = "game_start";
    public const string GameEnd       = "game_end";
    public const string TargetHit     = "target_hit";
    public const string TargetMiss    = "target_miss";
    public const string TrialTimeout  = "trial_timeout";

    // Legacy / simple cue aliases for backward-compatibility
    public const string LeftCue       = "cue_left";
    public const string RightCue      = "cue_right";
    public const string DownCue       = "cue_down";
    public const string UpCue         = "cue_up";
    public const string StopCue       = "cue_rest";
    public const string BothCue       = "cue_both_hands";

    public static readonly IReadOnlyList<string> AllCalibrationCues =
    [
        CueLeft, CueRight, CueUp, CueDown, CueRest, CueFeet, CueBothHands, CueArithmetic
    ];

    public static readonly IReadOnlyList<string> AllCues = AllCalibrationCues;

    /// <summary>Validates that a marker contains only ASCII characters.</summary>
    public static bool IsAscii(string marker) =>
        !string.IsNullOrEmpty(marker) && marker.All(c => c < 128);

    /// <summary>Throws if marker contains non-ASCII characters or is empty.</summary>
    public static void AssertAscii(string marker)
    {
        if (string.IsNullOrEmpty(marker) || !IsAscii(marker))
            throw new ArgumentException($"Marker '{marker}' contains non-ASCII characters or is empty. Plan §10 violation.", nameof(marker));
    }
}

