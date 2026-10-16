using MathNet.Numerics.Statistics;
using LslGame.Core;

namespace LslGame.SignalQuality;

/// <summary>
/// Real-time signal quality metrics for EEG channels.
/// Mirrors Python signal_quality/quality_metrics.py — Öncelik 2.
/// </summary>
public sealed class QualityMetrics
{
    private readonly int _nChannels;
    private readonly double _sampleRate;

    // Rolling buffers (circular) — 4 seconds
    private readonly float[][] _buffers;
    private readonly int[] _heads;
    private readonly int _bufLen;
    private bool _full;

    public QualityMetrics(int nChannels = LslStreamInfo.EegChannels,
                          double sampleRate = LslStreamInfo.EegSampleRate,
                          double windowS = 4.0)
    {
        _nChannels  = nChannels;
        _sampleRate = sampleRate;
        _bufLen     = (int)(windowS * sampleRate);
        _buffers    = new float[nChannels][];
        _heads      = new int[nChannels];
        for (int i = 0; i < nChannels; i++)
            _buffers[i] = new float[_bufLen];
    }

    /// <summary>Updates buffers with a new sample (one value per channel).</summary>
    public void Update(ReadOnlySpan<float> sample)
    {
        for (int ch = 0; ch < _nChannels; ch++)
        {
            _buffers[ch][_heads[ch]] = sample[ch];
            _heads[ch] = (_heads[ch] + 1) % _bufLen;
        }
        if (!_full && _heads[0] == 0) _full = true;
    }

    /// <summary>RMS amplitude per channel (µV). NaN if buffer not yet full.</summary>
    public double[] RmsPerChannel()
    {
        var result = new double[_nChannels];
        if (!_full) { Array.Fill(result, double.NaN); return result; }

        for (int ch = 0; ch < _nChannels; ch++)
        {
            double sum = 0;
            foreach (var v in _buffers[ch]) sum += v * v;
            result[ch] = Math.Sqrt(sum / _bufLen);
        }
        return result;
    }

    /// <summary>
    /// Returns true if all channels have RMS within acceptable range (2–100 µV).
    /// </summary>
    public bool IsGood()
    {
        if (!_full) return false;
        var rms = RmsPerChannel();
        return rms.All(r => r is >= 2.0 and <= 100.0);
    }

    /// <summary>Per-channel bad-channel detection (amplitude too high or too low).</summary>
    public bool[] BadChannels()
    {
        var bad = new bool[_nChannels];
        if (!_full) return bad;
        var rms = RmsPerChannel();
        for (int ch = 0; ch < _nChannels; ch++)
            bad[ch] = rms[ch] is < 0.5 or > 150.0;
        return bad;
    }
}
