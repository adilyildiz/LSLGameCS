using FluentAssertions;
using LslGame.SignalQuality;
using LslGame.Core;

namespace LslGame.Tests;

/// <summary>Tests for QualityMetrics (no LSL required).</summary>
public class QualityMetricsTests
{
    [Fact]
    public void IsGoodReturnsFalseBeforeBufferFull()
    {
        var qm = new QualityMetrics(windowS: 1.0);
        qm.IsGood().Should().BeFalse();
    }

    [Fact]
    public void RmsIsNanBeforeBufferFull()
    {
        var qm = new QualityMetrics(windowS: 1.0);
        var rms = qm.RmsPerChannel();
        rms.All(double.IsNaN).Should().BeTrue();
    }

    [Fact]
    public void RmsCorrectAfterFilling()
    {
        int nCh = LslStreamInfo.EegChannels;
        double fs = LslStreamInfo.EegSampleRate;
        var qm = new QualityMetrics(nCh, fs, windowS: 1.0);

        // Fill with constant signal of 10 µV per channel
        float[] sample = new float[nCh];
        Array.Fill(sample, 10f);
        int n = (int)(fs * 1.0) + 1;
        for (int i = 0; i < n; i++) qm.Update(sample);

        var rms = qm.RmsPerChannel();
        foreach (var r in rms)
            r.Should().BeApproximately(10.0, precision: 0.1);
    }

    [Fact]
    public void IsGoodTrueForNormalAmplitude()
    {
        int nCh = LslStreamInfo.EegChannels;
        double fs = LslStreamInfo.EegSampleRate;
        var qm = new QualityMetrics(nCh, fs, windowS: 1.0);

        float[] sample = new float[nCh];
        Array.Fill(sample, 20f);  // 20 µV — normal
        int n = (int)(fs * 1.0) + 1;
        for (int i = 0; i < n; i++) qm.Update(sample);

        qm.IsGood().Should().BeTrue();
    }

    [Fact]
    public void BadChannelDetectedForFlatline()
    {
        int nCh = LslStreamInfo.EegChannels;
        double fs = LslStreamInfo.EegSampleRate;
        var qm = new QualityMetrics(nCh, fs, windowS: 1.0);

        // Channel 0 flatlines (0 µV), rest are 20 µV
        int n = (int)(fs * 1.0) + 1;
        for (int i = 0; i < n; i++)
        {
            var s = new float[nCh];
            Array.Fill(s, 20f);
            s[0] = 0f;
            qm.Update(s);
        }

        qm.BadChannels()[0].Should().BeTrue();
        qm.BadChannels()[1..].Should().AllSatisfy(b => b.Should().BeFalse());
    }
}
