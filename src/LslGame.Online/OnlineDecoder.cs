using LslGame.Control;
using LslGame.Models;
using LslGame.Processing;

namespace LslGame.Online;

/// <summary>
/// Core online decoder that filters streaming EEG chunks and classifies them in rolling windows.
/// Matches Python OnlineDecoder in src/online/decoder.py.
/// </summary>
public sealed class OnlineDecoder : IDisposable
{
    private readonly CspModel _csp;
    private readonly LdaClassifier _lda;
    private readonly StatefulFilter _filter;
    private readonly ControlSender? _sender;
    private readonly bool _ownsSender;

    public double SampleRate { get; }
    public int ChannelCount { get; }
    public int WindowSamples { get; }
    public double Smoothing { get; set; }
    public double ConfidenceThreshold { get; set; }

    // Rolling window buffer [samples, channels]
    private readonly List<double[]> _windowBuffer = [];
    private readonly int _warmupSamples;
    private int _seenSamples;

    // Smoothed probabilities [p_left, p_right, p_rest]
    private double _smoothedPLeft = 0.0;
    private double _smoothedPRight = 0.0;

    public bool IsReady => _windowBuffer.Count >= WindowSamples && _seenSamples >= (WindowSamples + _warmupSamples);

    public OnlineDecoder(
        CspModel csp,
        LdaClassifier lda,
        double srate = 250.0,
        int nChannels = 7,
        double windowSec = 3.0,
        double smoothing = 0.5,
        double confThreshold = 0.6,
        ControlSender? sender = null)
    {
        _csp = csp;
        _lda = lda;
        SampleRate = srate;
        ChannelCount = nChannels;
        WindowSamples = (int)Math.Round(windowSec * srate);
        Smoothing = smoothing;
        ConfidenceThreshold = confThreshold;
        _warmupSamples = (int)Math.Round(1.0 * srate);

        _filter = new StatefulFilter(nChannels);

        if (sender != null)
        {
            _sender = sender;
            _ownsSender = false;
        }
    }

    /// <summary>
    /// Feeds a new chunk of raw EEG data [nChunkSamples, nChannels].
    /// Filters chunk causally and appends to rolling window.
    /// </summary>
    public void PushChunk(double[,] chunk)
    {
        int nSamples = chunk.GetLength(0);
        int nCh = chunk.GetLength(1);
        if (nCh != ChannelCount)
            throw new ArgumentException($"Expected {ChannelCount} channels, but received {nCh}.", nameof(chunk));

        var filtered = _filter.FilterBlock(chunk);

        for (int i = 0; i < nSamples; i++)
        {
            var row = new double[ChannelCount];
            for (int ch = 0; ch < ChannelCount; ch++)
                row[ch] = filtered[i, ch];

            _windowBuffer.Add(row);
            _seenSamples++;
        }

        // Keep rolling buffer length at WindowSamples
        while (_windowBuffer.Count > WindowSamples)
            _windowBuffer.RemoveAt(0);
    }

    /// <summary>
    /// Processes the current rolling window and returns discrete probabilities [pLeft, pRight, pRest].
    /// </summary>
    public (float PLeft, float PRight, float PRest) ClassifyCurrentWindow()
    {
        if (!IsReady)
            return (0f, 0f, 1f); // default to rest during warmup

        // Build window matrix [WindowSamples, ChannelCount]
        var window = new double[WindowSamples, ChannelCount];
        for (int i = 0; i < WindowSamples; i++)
        {
            var row = _windowBuffer[i];
            for (int ch = 0; ch < ChannelCount; ch++)
                window[i, ch] = row[ch];
        }

        // Extract CSP features
        var features = _csp.TransformSample(window);

        // Classify with LDA
        double pClass1 = _lda.PredictProbaClass1(features);
        double pLeft = _lda.Class1 == "left" ? pClass1 : (1.0 - pClass1);
        double pRight = 1.0 - pLeft;

        // Apply exponential smoothing
        _smoothedPLeft = Smoothing * _smoothedPLeft + (1.0 - Smoothing) * pLeft;
        _smoothedPRight = Smoothing * _smoothedPRight + (1.0 - Smoothing) * pRight;

        double maxP = Math.Max(_smoothedPLeft, _smoothedPRight);
        float outLeft, outRight, outRest;

        if (maxP >= ConfidenceThreshold)
        {
            if (_smoothedPLeft > _smoothedPRight)
            {
                outLeft = (float)_smoothedPLeft;
                outRight = (float)(1.0 - _smoothedPLeft);
                outRest = 0f;
            }
            else
            {
                outRight = (float)_smoothedPRight;
                outLeft = (float)(1.0 - _smoothedPRight);
                outRest = 0f;
            }
        }
        else
        {
            // Below confidence threshold -> Rest
            outLeft = 0f;
            outRight = 0f;
            outRest = 1f;
        }

        _sender?.SendDiscrete(outLeft, outRight, pUp: 0f, pDown: 0f, pRest: outRest);

        return (outLeft, outRight, outRest);
    }

    public void Dispose()
    {
        if (_ownsSender) _sender?.Dispose();
    }
}