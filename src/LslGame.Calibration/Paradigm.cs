using LslGame.Core;

namespace LslGame.Calibration;

public enum CalibrationStage
{
    Idle,
    RunReady,
    Fixation,
    Cue,
    Imagery,
    Rest,
    InterRunPause,
    SessionEnd
}

public sealed class CalibrationTiming
{
    public double FixationSec { get; init; } = 2.0;
    public double CueSec { get; init; } = 1.0;
    public double ImagerySec { get; init; } = 4.0;
    public double RestMinSec { get; init; } = 1.5;
    public double RestMaxSec { get; init; } = 3.0;
    public double InterRunPauseSec { get; init; } = 60.0;
}

public sealed class TrialInfo
{
    public int RunIndex { get; init; }
    public int TrialInRun { get; init; }
    public int GlobalTrialIndex { get; init; }
    public string ClassName { get; init; } = "rest";
    public double RestDuration { get; init; }
    public Dictionary<string, double> Timestamps { get; init; } = [];
}

/// <summary>
/// Headless Graz-BCI calibration state machine.
/// Matches Python CalibrationStateMachine in src/calibration/paradigm.py.
/// </summary>
public sealed class CalibrationStateMachine
{
    public CalibrationStage Stage { get; private set; } = CalibrationStage.Idle;
    public CalibrationTiming Timing { get; }
    public IReadOnlyList<string> Classes { get; }
    public int TotalRuns { get; }
    public int TrialsPerRun { get; }
    public int MaxStreak { get; }

    public int CurrentRun { get; private set; }
    public int CurrentTrialInRun { get; private set; }
    public int GlobalTrialIndex { get; private set; }
    public double StageStartTime { get; private set; }
    public double StageDuration { get; private set; }

    public bool IsSessionActive { get; private set; }
    public bool IsRunActive { get; private set; }
    public bool IsPaused { get; private set; }
    public bool IsCompleted => Stage == CalibrationStage.SessionEnd;
    public bool Aborted { get; private set; }

    private readonly Random _rng;
    private readonly List<List<string>> _runSequences = [];
    private readonly List<TrialInfo> _trialsHistory = [];
    private readonly Dictionary<string, double> _currentTrialTimestamps = [];
    private double _currentRestDuration;

    public IReadOnlyList<TrialInfo> TrialsHistory => _trialsHistory;

    public string? CurrentClass
    {
        get
        {
            if (CurrentRun >= 0 && CurrentRun < _runSequences.Count)
            {
                var seq = _runSequences[CurrentRun];
                if (CurrentTrialInRun >= 0 && CurrentTrialInRun < seq.Count)
                    return seq[CurrentTrialInRun];
            }
            return null;
        }
    }

    public CalibrationStateMachine(
        IReadOnlyList<string>? classes = null,
        int totalRuns = 4,
        int trialsPerRun = 20,
        int maxStreak = 3,
        CalibrationTiming? timing = null,
        int? seed = null)
    {
        Classes = classes ?? ["left", "right", "rest"];
        TotalRuns = totalRuns;
        TrialsPerRun = trialsPerRun;
        MaxStreak = maxStreak;
        Timing = timing ?? new CalibrationTiming();
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();

        for (int r = 0; r < TotalRuns; r++)
        {
            int? runSeed = seed.HasValue ? seed.Value + r * 1000 : null;
            var seq = GenerateBalancedSequence(Classes, TrialsPerRun, runSeed, MaxStreak);
            _runSequences.Add(seq);
        }
    }

    public static List<string> GenerateBalancedSequence(
        IReadOnlyList<string> classes,
        int count = 20,
        int? seed = null,
        int maxStreak = 3)
    {
        if (classes.Count == 0)
            throw new ArgumentException("Class list cannot be empty.", nameof(classes));
        if (count <= 0)
            throw new ArgumentException("Trial count must be positive.", nameof(count));

        var rng = seed.HasValue ? new Random(seed.Value) : new Random();
        int nClasses = classes.Count;
        int baseCount = count / nClasses;
        int remainder = count % nClasses;

        var counts = new Dictionary<string, int>();
        foreach (var cls in classes) counts[cls] = baseCount;

        var indices = Enumerable.Range(0, nClasses).OrderBy(_ => rng.Next()).ToList();
        for (int i = 0; i < remainder; i++)
            counts[classes[indices[i]]]++;

        var pool = new List<string>();
        foreach (var kv in counts)
            for (int i = 0; i < kv.Value; i++) pool.Add(kv.Key);

        // Rejection sampling
        for (int attempt = 0; attempt < 2000; attempt++)
        {
            var candidate = pool.OrderBy(_ => rng.Next()).ToList();
            bool valid = true;
            int streak = 1;
            for (int i = 1; i < candidate.Count; i++)
            {
                if (candidate[i] == candidate[i - 1])
                {
                    streak++;
                    if (streak > maxStreak) { valid = false; break; }
                }
                else streak = 1;
            }
            if (valid) return candidate;
        }

        return pool.OrderBy(_ => rng.Next()).ToList();
    }

    private double SampleRestDuration() =>
        Timing.RestMinSec + _rng.NextDouble() * (Timing.RestMaxSec - Timing.RestMinSec);

    public List<string> StartSession(double timestamp)
    {
        IsSessionActive = true;
        Stage = CalibrationStage.RunReady;
        CurrentRun = 0;
        CurrentTrialInRun = 0;
        GlobalTrialIndex = 0;
        return [Markers.SessionStart];
    }

    public List<string> StartRun(double timestamp)
    {
        if (!IsSessionActive)
            throw new InvalidOperationException("Session must be started before starting a run.");

        var markers = new List<string>();
        if (Stage == CalibrationStage.InterRunPause || IsPaused)
        {
            markers.Add(Markers.PauseEnd);
            IsPaused = false;
        }

        markers.Add(Markers.RunStart);
        IsRunActive = true;
        CurrentTrialInRun = 0;

        _currentRestDuration = SampleRestDuration();
        _currentTrialTimestamps.Clear();
        _currentTrialTimestamps["trial_start"] = timestamp;
        _currentTrialTimestamps["fixation_start"] = timestamp;

        Stage = CalibrationStage.Fixation;
        StageStartTime = timestamp;
        StageDuration = Timing.FixationSec;

        markers.Add(Markers.TrialStart);
        markers.Add(Markers.FixationStart);
        return markers;
    }

    public bool ShouldTransition(double currentTime) =>
        IsRunActive && (currentTime - StageStartTime) >= StageDuration;

    public (CalibrationStage NextStage, List<string> Markers) CommitTransition(double timestamp)
    {
        if (!IsRunActive) return (Stage, []);

        string cls = CurrentClass ?? "rest";
        var markers = new List<string>();

        if (Stage == CalibrationStage.Fixation)
        {
            Stage = CalibrationStage.Cue;
            StageStartTime = timestamp;
            StageDuration = Timing.CueSec;
            string marker = $"cue_{cls}";
            _currentTrialTimestamps["cue"] = timestamp;
            markers.Add(marker);
        }
        else if (Stage == CalibrationStage.Cue)
        {
            Stage = CalibrationStage.Imagery;
            StageStartTime = timestamp;
            StageDuration = Timing.ImagerySec;
            _currentTrialTimestamps["imagery_start"] = timestamp;
            markers.Add(Markers.ImageryStart);
        }
        else if (Stage == CalibrationStage.Imagery)
        {
            Stage = CalibrationStage.Rest;
            StageStartTime = timestamp;
            StageDuration = _currentRestDuration;
            _currentTrialTimestamps["rest_start"] = timestamp;
            markers.Add(Markers.ImageryEnd);
            markers.Add(Markers.RestStart);
        }
        else if (Stage == CalibrationStage.Rest)
        {
            _currentTrialTimestamps["trial_end"] = timestamp;
            markers.Add(Markers.TrialEnd);

            _trialsHistory.Add(new TrialInfo
            {
                RunIndex = CurrentRun,
                TrialInRun = CurrentTrialInRun,
                GlobalTrialIndex = GlobalTrialIndex,
                ClassName = cls,
                RestDuration = _currentRestDuration,
                Timestamps = new Dictionary<string, double>(_currentTrialTimestamps)
            });

            CurrentTrialInRun++;
            GlobalTrialIndex++;

            if (CurrentTrialInRun < TrialsPerRun)
            {
                _currentRestDuration = SampleRestDuration();
                _currentTrialTimestamps.Clear();
                _currentTrialTimestamps["trial_start"] = timestamp;
                _currentTrialTimestamps["fixation_start"] = timestamp;

                Stage = CalibrationStage.Fixation;
                StageStartTime = timestamp;
                StageDuration = Timing.FixationSec;

                markers.Add(Markers.TrialStart);
                markers.Add(Markers.FixationStart);
            }
            else
            {
                markers.Add(Markers.RunEnd);
                IsRunActive = false;

                if (CurrentRun + 1 < TotalRuns)
                {
                    Stage = CalibrationStage.InterRunPause;
                    StageStartTime = timestamp;
                    StageDuration = Timing.InterRunPauseSec;
                    IsPaused = true;
                    markers.Add(Markers.PauseStart);
                    CurrentRun++;
                }
                else
                {
                    Stage = CalibrationStage.SessionEnd;
                    IsSessionActive = false;
                    markers.Add(Markers.SessionEnd);
                }
            }
        }

        return (Stage, markers);
    }

    public List<string> Update(double currentTime)
    {
        var allMarkers = new List<string>();
        while (IsRunActive && ShouldTransition(currentTime))
        {
            double transTs = StageStartTime + StageDuration;
            var (_, markers) = CommitTransition(transTs);
            allMarkers.AddRange(markers);
        }
        return allMarkers;
    }

    public List<string> Abort(double timestamp)
    {
        var markers = new List<string>();
        if (IsSessionActive || IsRunActive || IsPaused)
        {
            Aborted = true;
            IsSessionActive = false;
            IsRunActive = false;
            IsPaused = false;
            Stage = CalibrationStage.SessionEnd;
            markers.Add(Markers.SessionEnd);
        }
        return markers;
    }
}