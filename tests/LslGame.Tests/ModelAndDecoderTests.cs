using FluentAssertions;
using LslGame.Models;
using LslGame.Online;

namespace LslGame.Tests;

public class ModelAndDecoderTests
{
    [Fact]
    public void CspAndLdaFitAndClassifyContrastingSignals()
    {
        int nEpochs = 20;
        int nTimes = 200;
        int nChannels = 4;

        var epochs = new double[nEpochs, nTimes, nChannels];
        var labels = new string[nEpochs];
        var rng = new Random(42);

        for (int i = 0; i < nEpochs; i++)
        {
            bool isLeft = (i % 2 == 0);
            labels[i] = isLeft ? "left" : "right";

            for (int t = 0; t < nTimes; t++)
            {
                // Class "left": high amplitude on channel 0, low on channel 3
                // Class "right": low amplitude on channel 0, high on channel 3
                double noise0 = (rng.NextDouble() - 0.5) * 2.0;
                double noise3 = (rng.NextDouble() - 0.5) * 2.0;

                epochs[i, t, 0] = isLeft ? noise0 * 10.0 : noise0 * 1.0;
                epochs[i, t, 1] = (rng.NextDouble() - 0.5) * 2.0;
                epochs[i, t, 2] = (rng.NextDouble() - 0.5) * 2.0;
                epochs[i, t, 3] = isLeft ? noise3 * 1.0 : noise3 * 10.0;
            }
        }

        var csp = new CspModel(components: 1);
        csp.Fit(epochs, labels);

        var features = new double[nEpochs][];
        for (int i = 0; i < nEpochs; i++)
        {
            var singleEpoch = new double[nTimes, nChannels];
            for (int t = 0; t < nTimes; t++)
                for (int ch = 0; ch < nChannels; ch++)
                    singleEpoch[t, ch] = epochs[i, t, ch];

            features[i] = csp.TransformSample(singleEpoch);
        }

        var lda = new LdaClassifier();
        lda.Fit(features, labels);

        // Test accuracy on training set
        int correct = 0;
        for (int i = 0; i < nEpochs; i++)
        {
            double pClass1 = lda.PredictProbaClass1(features[i]);
            string predicted = pClass1 >= 0.5 ? lda.Class1 : lda.Class2;
            if (predicted == labels[i]) correct++;
        }

        double acc = (double)correct / nEpochs;
        acc.Should().BeGreaterThanOrEqualTo(0.85);
    }

    [Fact]
    public void OnlineDecoderDefaultsToRestDuringWarmup()
    {
        var csp = new CspModel(components: 1);
        var lda = new LdaClassifier();

        // Fit dummy model
        double[,,] dummyEpochs = new double[4, 100, 7];
        string[] dummyLabels = ["left", "right", "left", "right"];
        csp.Fit(dummyEpochs, dummyLabels);

        double[][] dummyFeats = [new[] { 1.0, -1.0 }, new[] { -1.0, 1.0 }, new[] { 1.0, -1.0 }, new[] { -1.0, 1.0 }];
        lda.Fit(dummyFeats, dummyLabels);

        using var decoder = new OnlineDecoder(csp, lda, srate: 250.0, nChannels: 7, windowSec: 1.0);

        // Push small chunk (less than window and warmup)
        var chunk = new double[50, 7];
        decoder.PushChunk(chunk);

        decoder.IsReady.Should().BeFalse();
        var (pLeft, pRight, pRest) = decoder.ClassifyCurrentWindow();

        pRest.Should().Be(1f);
        pLeft.Should().Be(0f);
        pRight.Should().Be(0f);
    }
}