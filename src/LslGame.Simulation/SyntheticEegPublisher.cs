using System.Numerics;
using SharpLSL;
using LslGame.Core;

namespace LslGame.Simulation;

/// <summary>
/// Publishes synthetic 11-channel EEG at 250 Hz via LSL.
/// Mirrors Python simulation/synthetic_eeg.py — Öncelik 1.
///
/// Signal model:
///   - Pink noise base
///   - Left motor imagery → mu suppression C3 (8-12 Hz)
///   - Right motor imagery → mu suppression C4 (8-12 Hz)
/// </summary>
public sealed class SyntheticEegPublisher : IDisposable
{
    private readonly StreamOutlet _outlet;
    private readonly Random _rng;
    private readonly double _sampleRateHz;
    private readonly int _nChannels;
    private bool _disposed;

    // Mu band suppression state
    private string _currentCue = Markers.StopCue;
    private readonly object _cueLock = new();

    public SyntheticEegPublisher(int seed = 42)
    {
        _nChannels    = LslStreamInfo.EegChannels;
        _sampleRateHz = LslStreamInfo.EegSampleRate;
        _rng = new Random(seed);

        var info = new StreamInfo(
            name:            LslStreamInfo.EegName,
            type:            LslStreamInfo.EegType,
            channelCount:    _nChannels,
            nominalSrate:    _sampleRateHz,
            channelFormat:   ChannelFormat.Float,
            sourceId:        "synthetic_eeg_cs");

        // Channel metadata (mirrors Python)
        var chans = info.Description.AppendChild("channels");
        foreach (var label in LslStreamInfo.EegLabels)
        {
            var ch = chans.AppendChild("channel");
            ch.AppendChildValue("label", label);
            ch.AppendChildValue("unit", "microvolts");
            ch.AppendChildValue("type", "EEG");
        }

        _outlet = new StreamOutlet(info, chunkSize: 1, maxBuffered: 360);
        Console.WriteLine($"[SyntheticEEG] Outlet created: {_nChannels}ch @ {_sampleRateHz} Hz");
    }

    /// <summary>Sets the current cue to modulate the synthetic signal.</summary>
    public void SetCue(string cue)
    {
        Markers.AssertAscii(cue);
        lock (_cueLock) _currentCue = cue;
    }

    /// <summary>Runs the EEG publishing loop until <paramref name="ct"/> is cancelled.</summary>
    public Task RunAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        var sample   = new float[_nChannels];
        var interval = TimeSpan.FromSeconds(1.0 / _sampleRateHz);
        var next     = DateTime.UtcNow;

        // Channel indices
        int c3Idx = Array.IndexOf(LslStreamInfo.EegLabels.ToArray(), "C3");
        int c4Idx = Array.IndexOf(LslStreamInfo.EegLabels.ToArray(), "C4");

        while (!ct.IsCancellationRequested)
        {
            string cue;
            lock (_cueLock) cue = _currentCue;

            for (int ch = 0; ch < _nChannels; ch++)
            {
                // Pink noise approximation (sum of coloured octaves)
                sample[ch] = (float)(PinkNoise() * 10.0);  // ~10 µV rms
            }

            // ERD: suppress mu power (~10 µV → ~5 µV) on contralateral side
            if (cue == Markers.LeftCue && c4Idx >= 0)
                sample[c4Idx] *= 0.5f;
            if (cue == Markers.RightCue && c3Idx >= 0)
                sample[c3Idx] *= 0.5f;
            if (cue == Markers.BothCue)
            {
                if (c3Idx >= 0) sample[c3Idx] *= 0.5f;
                if (c4Idx >= 0) sample[c4Idx] *= 0.5f;
            }

            _outlet.PushSample(sample);

            // Busy-wait for precise timing
            next += interval;
            var sleepMs = (next - DateTime.UtcNow).TotalMilliseconds;
            if (sleepMs > 0) Thread.Sleep((int)sleepMs);
        }
    }, ct);

    // ---- Simple pink noise generator (Voss-McCartney) ----
    private readonly double[] _pinkState = new double[8];
    private int _pinkCounter;

    private double PinkNoise()
    {
        _pinkCounter = (_pinkCounter + 1) & 0xFF;
        int lsb = _pinkCounter & -_pinkCounter;  // lowest set bit
        int idx = BitOperations.TrailingZeroCount(lsb) & 7;
        _pinkState[idx] = (_rng.NextDouble() - 0.5) * 2.0;
        return _pinkState.Sum() / 8.0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _outlet.Dispose();
    }
}



