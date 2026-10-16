using FluentAssertions;
using LslGame.Processing;

namespace LslGame.Tests;

public class ProcessingTests
{
    [Fact]
    public void SpatialLaplacianReducesCommonComponent()
    {
        // 1 sample, 7 channels: F3, F4, C3, Cz, C4, P3, P4
        // If F3=10, P3=10, Cz=10, and C3=10, C3' should be 10 - 10 = 0.
        double[,] data =
        {
            { 10.0, 10.0, 10.0, 10.0, 10.0, 10.0, 10.0 }
        };

        var filtered = Spatial.ApplyLaplacian(data);

        // C3 is channel 2, C4 is channel 4
        filtered[0, 2].Should().BeApproximately(0.0, 1e-9);
        filtered[0, 4].Should().BeApproximately(0.0, 1e-9);
    }

    [Fact]
    public void SpatialCarMakesMeanZeroAcrossChannels()
    {
        double[,] data =
        {
            { 5.0, 15.0, 25.0, 35.0, 45.0, 55.0, 65.0 }
        };

        var filtered = Spatial.ApplyCar(data);

        double mean = 0.0;
        for (int ch = 0; ch < 7; ch++)
            mean += filtered[0, ch];
        mean /= 7.0;

        mean.Should().BeApproximately(0.0, 1e-9);
    }

    [Fact]
    public void ArtifactDetectionRejectsHighAmplitudeEpoch()
    {
        int nEpochs = 2, nTimes = 100, nCh = 7;
        var data = new double[nEpochs, nTimes, nCh];
        var times = new double[nTimes];
        var labels = new[] { "left", "right" };
        var runs = new[] { 0, 0 };
        var trials = new[] { 0, 1 };
        var chNames = Spatial.DefaultChNames;

        // Epoch 0 is clean (10 uV)
        for (int t = 0; t < nTimes; t++)
            for (int ch = 0; ch < nCh; ch++)
                data[0, t, ch] = 10.0;

        // Epoch 1 has high amplitude artifact (200 uV)
        for (int t = 0; t < nTimes; t++)
            for (int ch = 0; ch < nCh; ch++)
                data[1, t, ch] = 200.0;

        var epochs = new Epochs(data, times, labels, runs, trials, 250.0, chNames);
        var report = Artifacts.DetectArtifacts(epochs, amplitudeThresholdUv: 95.0);

        report.RejectedIndices.Should().Contain(1);
        report.CleanIndices.Should().Contain(0);
        report.Reasons[1].Should().Contain("amplitude");
    }

    [Fact]
    public void StatefulFilterIsConsistentBlockVsSequential()
    {
        var filter1 = new StatefulFilter(nChannels: 1);
        var filter2 = new StatefulFilter(nChannels: 1);

        int totalSamples = 500;
        var data = new double[totalSamples, 1];
        for (int i = 0; i < totalSamples; i++)
            data[i, 0] = Math.Sin(2.0 * Math.PI * 10.0 * (i / 250.0)); // 10 Hz sine

        // Filter all at once
        var outFull = filter1.FilterBlock(data);

        // Filter block-by-block (chunks of 25)
        var outChunked = new double[totalSamples, 1];
        int chunkSize = 25;
        for (int start = 0; start < totalSamples; start += chunkSize)
        {
            var chunk = new double[chunkSize, 1];
            for (int i = 0; i < chunkSize; i++)
                chunk[i, 0] = data[start + i, 0];

            var filteredChunk = filter2.FilterBlock(chunk);
            for (int i = 0; i < chunkSize; i++)
                outChunked[start + i, 0] = filteredChunk[i, 0];
        }

        // Compare: numerical difference should be < 1e-9
        for (int i = 0; i < totalSamples; i++)
        {
            outChunked[i, 0].Should().BeApproximately(outFull[i, 0], 1e-9);
        }
    }
}