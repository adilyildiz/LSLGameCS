using SharpLSL;
using LslGame.Core;

namespace LslGame.Simulation;

/// <summary>
/// Publishes synthetic 11-channel X.on EEG at 250 Hz via LSL.
/// Matches Python src/simulation/synthetic_eeg.py.
///
/// Signal model:
///   - 11 channels: F3, F4, C3, Cz, C4, P3, P4, BIP (EEG, uV) + accX, accY, accZ (AUX, mg0)
///   - Paul Kellet 1/f pink noise base (~10 uV rms)
///   - 10 Hz mu rhythm on C3, Cz, C4
///   - Contralateral ERD modulation based on cue (left -> C4 drops, right -> C3 drops, etc.)
///   - ~1g gravity component on accZ (~1000 mg)
/// </summary>
public sealed class SyntheticEegPublisher : IDisposable
{
    private readonly StreamOutlet _outlet;
    private readonly Random _rng;
    private readonly double _sampleRateHz;
    private readonly int _nChannels;
    private bool _disposed;

    // Kellet pink noise state per channel: [nChannels, 7]
    private readonly double[,] _kelletState;

    // Time tracking for 10 Hz mu rhythm
    private double _time;

    // Mu band suppression state
    private string _currentCue = Markers.CueRest;
    private readonly Lock _cueLock = new();

    public SyntheticEegPublisher(int seed = 42, string streamName = LslStreamInfo.EegName)
    {
        _nChannels    = LslStreamInfo.EegTotalChannels;
        _sampleRateHz = LslStreamInfo.EegSampleRate;
        _rng          = new Random(seed);
        _kelletState  = new double[_nChannels, 7];

        var info = new StreamInfo(
            name:          streamName,
            type:          LslStreamInfo.EegType,
            channelCount:  _nChannels,
            nominalSrate:  _sampleRateHz,
            channelFormat: ChannelFormat.Float,
            sourceId:      LslStreamInfo.SimSourceId);

        // XML metadata matching X.on device
        var desc = info.Description;
        desc.AppendChildValue("manufacturer", "Brain Products");
        var chans = desc.AppendChild("channels");

        for (int i = 0; i < LslStreamInfo.EegLabels.Count; i++)
        {
            var lbl = LslStreamInfo.EegLabels[i];
            var ch = chans.AppendChild("channel");
            ch.AppendChildValue("label", lbl);
            if (i < 8)
            {
                ch.AppendChildValue("type", "EEG");
                ch.AppendChildValue("unit", "uV");
            }
            else
            {
                ch.AppendChildValue("type", "AUX");
                ch.AppendChildValue("unit", "mg0");
            }
        }

        _outlet = new StreamOutlet(info, chunkSize: 1, maxBuffered: 360);
    }

    /// <summary>Sets the current cue to modulate the synthetic signal (ERD).</summary>
    public void SetCue(string cue)
    {
        Markers.AssertAscii(cue);
        lock (_cueLock) _currentCue = cue;
    }

    /// <summary>Generates one multi-channel sample at current time step.</summary>
    public void GenerateSample(Span<float> sample)
    {
        string cue;
        lock (_cueLock) cue = _currentCue;

        double dt = 1.0 / _sampleRateHz;
        _time += dt;

        // Determine ERD factors for C3, Cz, C4 (default 1.0)
        double c3Factor = 1.0;
        double c4Factor = 1.0;
        double czFactor = 1.0;

        if (cue is Markers.CueLeft or "left")
            c4Factor = 0.6; // Contralateral ERD on C4
        else if (cue is Markers.CueRight or "right")
            c3Factor = 0.6; // Contralateral ERD on C3
        else if (cue is Markers.CueDown or "down")
            czFactor = 0.6;
        else if (cue is Markers.CueUp or Markers.CueBothHands or "both" or "up")
        {
            c3Factor = 0.6;
            c4Factor = 0.6;
        }

        double muPhase = 2.0 * Math.PI * 10.0 * _time;
        double muSine = Math.Sin(muPhase);

        for (int ch = 0; ch < _nChannels; ch++)
        {
            if (ch < 8) // EEG channels
            {
                double pink = NextKelletPink(ch) * 8.0; // ~8-10 uV rms
                double mu = 0.0;

                if (ch == LslStreamInfo.IdxC3)
                    mu = muSine * 12.0 * c3Factor;
                else if (ch == LslStreamInfo.IdxC4)
                    mu = muSine * 12.0 * c4Factor;
                else if (ch == LslStreamInfo.IdxCz)
                    mu = muSine * 12.0 * czFactor;

                sample[ch] = (float)(pink + mu);
            }
            else if (ch == LslStreamInfo.IdxAccZ) // Z-axis accelerometer (~1g gravity)
            {
                double noise = NextGaussian() * 15.0;
                sample[ch] = (float)(1000.0 + noise);
            }
            else // X/Y accelerometer
            {
                double noise = NextGaussian() * 15.0;
                sample[ch] = (float)noise;
            }
        }
    }

    private double NextKelletPink(int ch)
    {
        double white = NextGaussian();
        _kelletState[ch, 0] = 0.99886 * _kelletState[ch, 0] + white * 0.0555179;
        _kelletState[ch, 1] = 0.99332 * _kelletState[ch, 1] + white * 0.0750759;
        _kelletState[ch, 2] = 0.96900 * _kelletState[ch, 2] + white * 0.1538520;
        _kelletState[ch, 3] = 0.86650 * _kelletState[ch, 3] + white * 0.3104856;
        _kelletState[ch, 4] = 0.55000 * _kelletState[ch, 4] + white * 0.5329522;
        _kelletState[ch, 5] = -0.7616 * _kelletState[ch, 5] - white * 0.0168980;
        double pink =
            _kelletState[ch, 0] + _kelletState[ch, 1] + _kelletState[ch, 2] +
            _kelletState[ch, 3] + _kelletState[ch, 4] + _kelletState[ch, 5] +
            _kelletState[ch, 6] + white * 0.5362;
        _kelletState[ch, 6] = white * 0.115926;
        return pink * 0.11; // Normalize to roughly unit variance
    }

    private double NextGaussian()
    {
        // Box-Muller transform
        double u1 = 1.0 - _rng.NextDouble();
        double u2 = 1.0 - _rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }

    /// <summary>Runs the EEG publishing loop at 250 Hz until <paramref name="ct"/> is cancelled.</summary>
    public Task RunAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        var sample = new float[_nChannels];
        int intervalUs = (int)(1_000_000.0 / _sampleRateHz);
        long startTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        long samplesSent = 0;

        while (!ct.IsCancellationRequested)
        {
            GenerateSample(sample);
            _outlet.PushSample(sample);
            samplesSent++;

            // Precise pacing using stopwatch
            double targetSeconds = (double)samplesSent / _sampleRateHz;
            while (true)
            {
                double elapsedSeconds = (double)(System.Diagnostics.Stopwatch.GetTimestamp() - startTicks) / System.Diagnostics.Stopwatch.Frequency;
                double remainingMs = (targetSeconds - elapsedSeconds) * 1000.0;
                if (remainingMs <= 0.0) break;
                if (remainingMs > 2.0) Thread.Sleep(1);
                else Thread.SpinWait(10);
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




