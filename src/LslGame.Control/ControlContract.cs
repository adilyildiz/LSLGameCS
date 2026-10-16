using SharpLSL;
using LslGame.Core;

namespace LslGame.Control;

/// <summary>
/// Defines the LSL control contract between BCI decoder / simulator and game.
/// Matches Python src/control/contract.py and docs/control_contract.md.
/// </summary>
public static class ControlContract
{
    // Timing constraints
    public const double MaxAlignmentJitterMs = 12.0;
    public const double MinAlignmentJitterMs = -4.0;
    public const float  DefaultThreshold    = 0.5f;
    public const double DefaultStaleAfter    = 0.3;  // seconds

    // Legacy int codes for backward-compatibility
    public const int CmdNone  = 0;
    public const int CmdLeft  = 1;
    public const int CmdRight = 2;
    public const int CmdDown  = 3;
    public const int CmdUp    = 4;
    public const int CmdStop  = 5;
    public const int CmdBoth  = 6;

    public static bool IsAlignmentValid(double jitterMs) =>
        jitterMs >= MinAlignmentJitterMs && jitterMs <= MaxAlignmentJitterMs;

    public static int DirectionToCommand(Direction dir) => dir switch
    {
        Direction.Left  => CmdLeft,
        Direction.Right => CmdRight,
        Direction.Down  => CmdDown,
        Direction.Up    => CmdUp,
        _               => CmdNone
    };

    public static Direction CommandToDirection(int cmd) => cmd switch
    {
        CmdLeft  => Direction.Left,
        CmdRight => Direction.Right,
        CmdDown  => Direction.Down,
        CmdUp    => Direction.Up,
        _        => Direction.Rest
    };

    public static int MarkerToCommand(string marker) => marker switch
    {
        Markers.CueLeft or "left"             => CmdLeft,
        Markers.CueRight or "right"           => CmdRight,
        Markers.CueDown or "down"             => CmdDown,
        Markers.CueUp or "up"                 => CmdUp,
        Markers.CueRest or "stop" or "rest"   => CmdStop,
        Markers.CueBothHands or "both"        => CmdBoth,
        _                                     => CmdNone
    };

    public static string CommandToMarker(int cmd) => cmd switch
    {
        CmdLeft  => "left",
        CmdRight => "right",
        CmdDown  => "down",
        CmdUp    => "up",
        CmdStop  => "stop",
        CmdBoth  => "both",
        _        => string.Empty
    };

    public static bool IsValidCommand(int cmd) => cmd is >= CmdNone and <= CmdBoth;

    public static StreamInfo CreateStreamInfo(
        ControlMode mode = ControlMode.Discrete,
        double srate = LslStreamInfo.ControlSampleRate,
        string sourceId = "BCI_Control_Sender")
    {
        int channels = mode == ControlMode.Discrete
            ? LslStreamInfo.DiscreteChannels
            : LslStreamInfo.ContinuousChannels;

        var info = new StreamInfo(
            name:          LslStreamInfo.ControlName,
            type:          LslStreamInfo.ControlType,
            channelCount:  channels,
            nominalSrate:  srate,
            channelFormat: ChannelFormat.Float,
            sourceId:      sourceId);

        var desc = info.Description;
        desc.AppendChildValue("mode", mode == ControlMode.Discrete ? "discrete" : "continuous");
        var chans = desc.AppendChild("channels");
        var labels = mode == ControlMode.Discrete
            ? LslStreamInfo.DiscreteChannelLabels
            : LslStreamInfo.ContinuousChannelLabels;

        foreach (var lbl in labels)
        {
            var ch = chans.AppendChild("channel");
            ch.AppendChildValue("label", lbl);
        }

        return info;
    }
}

/// <summary>
/// Publishes BCI_Control stream samples (discrete probabilities or continuous velocity).
/// Mirrors Python ControlSender.
/// </summary>
public sealed class ControlSender : IDisposable
{
    private readonly StreamOutlet _outlet;
    private readonly float[] _buffer;
    private bool _disposed;

    public ControlMode Mode { get; }
    public double SampleRate { get; }

    public ControlSender(
        ControlMode mode = ControlMode.Discrete,
        double srate = LslStreamInfo.ControlSampleRate,
        string sourceId = "BCI_Control_Sender")
    {
        Mode = mode;
        SampleRate = srate;
        var info = ControlContract.CreateStreamInfo(mode, srate, sourceId);
        _outlet = new StreamOutlet(info);
        _buffer = new float[mode == ControlMode.Discrete ? 5 : 2];
    }

    public void SendDiscrete(float pLeft, float pRight, float pUp = 0f, float pDown = 0f, float pRest = 0f)
    {
        if (Mode != ControlMode.Discrete)
            throw new InvalidOperationException("ControlSender is configured for continuous mode.");

        _buffer[0] = pLeft;
        _buffer[1] = pRight;
        _buffer[2] = pUp;
        _buffer[3] = pDown;
        _buffer[4] = pRest;
        _outlet.PushSample(_buffer);
    }

    public void SendContinuous(float vx, float vy)
    {
        if (Mode != ControlMode.Continuous)
            throw new InvalidOperationException("ControlSender is configured for discrete mode.");

        _buffer[0] = Math.Clamp(vx, -1f, 1f);
        _buffer[1] = Math.Clamp(vy, -1f, 1f);
        _outlet.PushSample(_buffer);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _outlet.Dispose();
    }
}

/// <summary>
/// Receives and interprets BCI_Control LSL stream into structured commands.
/// Mirrors Python ControlReceiver.
/// </summary>
public sealed class ControlReceiver : IDisposable
{
    private readonly StreamInlet _inlet;
    private readonly float[] _sampleBuffer;
    private float[]? _lastSample;
    private double _lastSampleTime;
    private bool _disposed;

    public ControlMode Mode { get; }
    public float Threshold { get; set; }
    public double StaleAfter { get; set; }
    public bool IsSimulator { get; }

    public ControlReceiver(
        StreamInfo? streamInfo = null,
        double timeout = 5.0,
        float threshold = ControlContract.DefaultThreshold,
        double staleAfter = ControlContract.DefaultStaleAfter,
        string? sourceId = null)
    {
        Threshold = threshold;
        StaleAfter = staleAfter;

        StreamInfo resolvedInfo;
        if (streamInfo != null)
        {
            resolvedInfo = streamInfo;
        }
        else
        {
            string predicate = sourceId != null
                ? $"source_id='{sourceId}'"
                : $"name='{LslStreamInfo.ControlName}'";

            var results = LSL.Resolve(predicate, 1, 1, timeout);
            if (results.Length == 0)
            {
                // Fallback to type
                results = LSL.Resolve($"type='{LslStreamInfo.ControlType}'", 1, 1, timeout);
            }
            if (results.Length == 0)
                throw new TimeoutException($"'{LslStreamInfo.ControlName}' LSL stream not found within {timeout:F1}s.");

            resolvedInfo = results[0];
        }

        _inlet = new StreamInlet(resolvedInfo);
        int chCount = resolvedInfo.ChannelCount;
        Mode = chCount == 2 ? ControlMode.Continuous : ControlMode.Discrete;
        _sampleBuffer = new float[chCount];

        string name = resolvedInfo.Name ?? string.Empty;
        string srcId = resolvedInfo.SourceId ?? string.Empty;
        IsSimulator = name.Contains("-SIM-", StringComparison.OrdinalIgnoreCase) ||
                      srcId.Contains("SIM", StringComparison.OrdinalIgnoreCase) ||
                      srcId.Contains("KEYBOARD", StringComparison.OrdinalIgnoreCase);
    }

    public (float[]? sample, double timestamp) PullLatest(double timeout = 0.0)
    {
        double ts = 0.0;
        float[]? lastPulled = null;

        // Drain any pending samples to get the latest
        while (true)
        {
            double pulledTs = _inlet.PullSample(_sampleBuffer, timeout);
            if (pulledTs <= 0.0) break;
            ts = pulledTs;
            lastPulled ??= new float[_sampleBuffer.Length];
            Array.Copy(_sampleBuffer, lastPulled, _sampleBuffer.Length);
            timeout = 0.0; // only block for the first sample if requested
        }

        return (lastPulled, ts);
    }

    public ControlCommand GetCommand(double timeout = 0.0, float? customThreshold = null)
    {
        float th = customThreshold ?? Threshold;
        var (sample, sampleTs) = PullLatest(timeout);

        double now = LSL.GetLocalClock();
        float[]? activeSample = null;
        bool isStale;

        if (sample != null)
        {
            double ts = sampleTs > 0.0 ? sampleTs : now;
            _lastSample = sample;
            _lastSampleTime = ts;
            isStale = (now - ts) > StaleAfter;
            activeSample = isStale ? null : sample;
        }
        else
        {
            if (_lastSample != null && (now - _lastSampleTime) <= StaleAfter)
            {
                activeSample = _lastSample;
                isStale = false;
            }
            else
            {
                activeSample = null;
                isStale = true;
            }
        }

        if (activeSample == null || isStale)
        {
            return new ControlCommand
            {
                Mode = Mode,
                HasSignal = false,
                Direction = Direction.Rest,
                Confidence = 0f,
                Vx = 0f,
                Vy = 0f,
                IsSimulator = IsSimulator,
                Raw = activeSample
            };
        }

        if (Mode == ControlMode.Discrete)
        {
            // [p_left, p_right, p_up, p_down, p_rest]
            int maxIdx = 0;
            float maxP = activeSample[0];
            for (int i = 1; i < activeSample.Length; i++)
            {
                if (activeSample[i] > maxP)
                {
                    maxP = activeSample[i];
                    maxIdx = i;
                }
            }

            Direction chosenDir = maxIdx switch
            {
                0 => Direction.Left,
                1 => Direction.Right,
                2 => Direction.Up,
                3 => Direction.Down,
                _ => Direction.Rest
            };

            float vx = 0f;
            float vy = 0f;

            if (chosenDir != Direction.Rest && maxP >= th)
            {
                if (chosenDir == Direction.Left) vx = -maxP;
                else if (chosenDir == Direction.Right) vx = maxP;
                else if (chosenDir == Direction.Up) vy = maxP;
                else if (chosenDir == Direction.Down) vy = -maxP;

                return new ControlCommand
                {
                    Mode = Mode,
                    HasSignal = true,
                    Direction = chosenDir,
                    Confidence = maxP,
                    Vx = vx,
                    Vy = vy,
                    IsSimulator = IsSimulator,
                    Raw = activeSample
                };
            }

            float restConf = activeSample.Length > 4 ? activeSample[4] : (1f - maxP);
            return new ControlCommand
            {
                Mode = Mode,
                HasSignal = true,
                Direction = Direction.Rest,
                Confidence = restConf,
                Vx = 0f,
                Vy = 0f,
                IsSimulator = IsSimulator,
                Raw = activeSample
            };
        }
        else
        {
            // Continuous: [vx, vy]
            float vx = activeSample[0];
            float vy = activeSample.Length > 1 ? activeSample[1] : 0f;
            float mag = MathF.Sqrt(vx * vx + vy * vy);

            Direction dir = Direction.Rest;
            if (mag >= th)
            {
                if (MathF.Abs(vx) > MathF.Abs(vy))
                    dir = vx > 0 ? Direction.Right : Direction.Left;
                else
                    dir = vy > 0 ? Direction.Up : Direction.Down;
            }

            return new ControlCommand
            {
                Mode = Mode,
                HasSignal = true,
                Direction = dir,
                Confidence = mag,
                Vx = vx,
                Vy = vy,
                IsSimulator = IsSimulator,
                Raw = activeSample
            };
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _inlet.Dispose();
    }
}

