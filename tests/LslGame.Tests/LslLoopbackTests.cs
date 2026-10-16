using FluentAssertions;
using LslGame.Acquisition;
using LslGame.Core;
using LslGame.Simulation;

namespace LslGame.Tests;

/// <summary>Real LSL loopback: SyntheticEegPublisher -> StreamReceiver (no mocks, time-limited).</summary>
public class LslLoopbackTests
{
    [Fact(Timeout = 30000)]
    public async Task PublisherToReceiverDeliversSamples()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var publisher = new SyntheticEegPublisher();
        var pubTask = publisher.RunAsync(cts.Token);

        using var receiver = new StreamReceiver(resolveTimeoutS: 5.0);
        receiver.ChannelCount.Should().Be(LslStreamInfo.EegChannels);

        int count = 0;
        receiver.SampleReceived += (s, _) =>
        {
            s.Length.Should().Be(LslStreamInfo.EegChannels);
            if (Interlocked.Increment(ref count) >= 100) cts.Cancel();
        };
        var rxTask = receiver.RunAsync(cts.Token);

        try { await Task.WhenAll(pubTask, rxTask); } catch (OperationCanceledException) { }
        count.Should().BeGreaterThanOrEqualTo(100);
    }
}
