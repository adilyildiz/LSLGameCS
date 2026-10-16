namespace LslGame.Core;

/// <summary>
/// LSL stream names, types and channel definitions shared across the system.
/// Matches Python config/device_xon.yaml and docs/control_contract.md.
/// </summary>
public static class LslStreamInfo
{
    // EEG raw stream (published by X.on amplifier or synthetic publisher)
    public const string EegName           = "EEG";
    public const string SimEegName        = "X.on-SIM-0000";
    public const string EegType           = "EEG";
    public const int    EegTotalChannels  = 11;
    public const int    EegChannels       = EegTotalChannels;
    public const int    EegSignalChannels = 7;
    public const double EegSampleRate     = 250.0;
    public const string SimSourceId       = "SIM-0000 250";

    // Marker stream (BCI_Markers)
    public const string MarkerName        = "BCI_Markers";
    public const string MarkerType        = "Markers";
    public const int    MarkerChannels    = 1;
    public const double MarkerIrregular   = 0.0;
    public const string MarkerSourceId    = "BCI_Markers_Source";

    // Control output stream (BCI_Control)
    public const string ControlName       = "BCI_Control";
    public const string ControlType       = "Control";
    public const double ControlSampleRate = 20.0; // 20 Hz
    public const int    DiscreteChannels  = 5;    // [p_left, p_right, p_up, p_down, p_rest]
    public const int    ContinuousChannels = 2;   // [vx, vy]

    // X.on channel labels: 7 EEG + 1 BIP + 3 AUX (accelerometer)
    public static readonly IReadOnlyList<string> EegLabels =
    [
        "F3", "F4", "C3", "Cz", "C4", "P3", "P4", "BIP", "accX", "accY", "accZ"
    ];

    public static readonly IReadOnlyList<string> DiscreteChannelLabels =
    [
        "p_left", "p_right", "p_up", "p_down", "p_rest"
    ];

    public static readonly IReadOnlyList<string> ContinuousChannelLabels =
    [
        "vx", "vy"
    ];

    // Channel index helpers
    public const int IdxF3   = 0;
    public const int IdxF4   = 1;
    public const int IdxC3   = 2;
    public const int IdxCz   = 3;
    public const int IdxC4   = 4;
    public const int IdxP3   = 5;
    public const int IdxP4   = 6;
    public const int IdxBip  = 7;
    public const int IdxAccX = 8;
    public const int IdxAccY = 9;
    public const int IdxAccZ = 10;
}

