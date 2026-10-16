using SharpLSL;
using LslGame.Core;

namespace LslGame.Acquisition;

/// <summary>
/// Connects to the EEG LSL stream and raises an event for every sample.
/// Mirrors Python acquisition/stream_receiver.py.
/// </summary>
public sealed class StreamReceiver : IDisposable
{
    private readonly StreamInlet _inlet;
    private readonly int _nChannels;
    private bool _disposed;

    /// <summary>Fired for every received sample (values, LSL timestamp).</summary>
    public event Action<float[], double>? SampleReceived;

    public StreamReceiver(string streamName = LslStreamInfo.EegName, double resolveTimeoutS = 5.0)
    {
        var results = LSL.Resolve($"name='{streamName}'", 1, 1, resolveTimeoutS);
        if (results.Length == 0)
            throw new TimeoutException($"No LSL stream named '{streamName}' found within {resolveTimeoutS}s.");

        _inlet = new StreamInlet(results[0]);
        _nChannels = results[0].ChannelCount;
    }

    public int ChannelCount => _nChannels;

    /// <summary>Runs the receive loop until <paramref name="ct"/> is cancelled.</summary>
    public Task RunAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        _inlet.OpenStream(5.0);
        while (!ct.IsCancellationRequested)
        {
            var sample = new float[_nChannels];
            double ts = _inlet.PullSample(sample, 0.1);
            if (ts != 0.0) SampleReceived?.Invoke(sample, ts);
        }
    }, ct);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _inlet.CloseStream();
        _inlet.Dispose();
    }
}
