namespace LslGame.Core;

/// <summary>
/// LSL stream names and types shared between C# and Python components.
/// Mirrors control/contract.py constants.
/// </summary>
public static class LslStreamInfo
{
    // EEG raw stream (published by amp or synthetic publisher)
    public const string EegName      = "EEG";
    public const string EegType      = "EEG";
    public const int    EegChannels  = 11;
    public const double EegSampleRate = 250.0;

    // Marker stream (BCI_Markers)
    public const string MarkerName   = "BCI_Markers";
    public const string MarkerType   = "Markers";
    public const int    MarkerChannels = 1;
    public const double MarkerIrregular = 0.0;  // irregular rate

    // Control output stream (BCI_Control)
    public const string ControlName  = "BCI_Control";
    public const string ControlType  = "Control";
    public const int    ControlChannels = 1;
    public const double ControlIrregular = 0.0;

    // EEG channel labels (Laplacian, Cz-centred)
    public static readonly IReadOnlyList<string> EegLabels =
        ["Fz", "FC3", "FC1", "FCz", "FC2", "FC4",
         "C3",  "C1",  "Cz",  "C2",  "C4"];
}
