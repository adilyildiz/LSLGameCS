namespace LslGame.Processing;

public sealed class ArtifactReport
{
    public List<int> RejectedIndices { get; init; } = [];
    public List<int> CleanIndices { get; init; } = [];
    public Dictionary<int, List<string>> Reasons { get; init; } = [];
    public int TotalEpochs { get; init; }
    public double RejectionRatio => TotalEpochs == 0 ? 0.0 : (double)RejectedIndices.Count / TotalEpochs;
}

/// <summary>
/// Unsupervised artifact detection and rejection.
/// Matches Python src/processing/artifacts.py.
/// </summary>
public static class Artifacts
{
    public static ArtifactReport DetectArtifacts(
        Epochs epochs,
        double amplitudeThresholdUv = 95.0,
        double peakToPeakThresholdUv = 160.0,
        double blinkThresholdUv = 85.0,
        double accStdThresholdMg = 35.0)
    {
        int nEpochs = epochs.Count;
        int nTimes = epochs.TimesCount;
        int nCh = epochs.ChannelCount;
        var reasons = new Dictionary<int, List<string>>();

        int f3Idx = Array.IndexOf(epochs.ChNames, "F3");
        if (f3Idx < 0) f3Idx = 0;
        int f4Idx = Array.IndexOf(epochs.ChNames, "F4");
        if (f4Idx < 0) f4Idx = Math.Min(1, nCh - 1);

        for (int i = 0; i < nEpochs; i++)
        {
            var epReasons = new List<string>();
            double maxAbs = 0.0;

            // Check amplitude and peak-to-peak per channel
            for (int ch = 0; ch < nCh; ch++)
            {
                double chMin = double.MaxValue;
                double chMax = double.MinValue;
                for (int t = 0; t < nTimes; t++)
                {
                    double val = epochs.Data[i, t, ch];
                    double absVal = Math.Abs(val);
                    if (absVal > maxAbs) maxAbs = absVal;
                    if (val < chMin) chMin = val;
                    if (val > chMax) chMax = val;
                }
                if ((chMax - chMin) > peakToPeakThresholdUv)
                {
                    if (!epReasons.Contains("amplitude")) epReasons.Add("amplitude");
                }
            }

            if (maxAbs > amplitudeThresholdUv && !epReasons.Contains("amplitude"))
                epReasons.Add("amplitude");

            // Blink check on F3 and F4
            double f3Max = 0.0;
            double f4Max = 0.0;
            for (int t = 0; t < nTimes; t++)
            {
                double absF3 = Math.Abs(epochs.Data[i, t, f3Idx]);
                if (absF3 > f3Max) f3Max = absF3;
                double absF4 = Math.Abs(epochs.Data[i, t, f4Idx]);
                if (absF4 > f4Max) f4Max = absF4;
            }
            if (f3Max > blinkThresholdUv || f4Max > blinkThresholdUv)
                epReasons.Add("blink");

            // Accelerometer movement check
            if (epochs.AccData != null)
            {
                for (int a = 0; a < 3; a++)
                {
                    double mean = 0.0;
                    for (int t = 0; t < nTimes; t++) mean += epochs.AccData[i, t, a];
                    mean /= nTimes;
                    double variance = 0.0;
                    for (int t = 0; t < nTimes; t++)
                    {
                        double diff = epochs.AccData[i, t, a] - mean;
                        variance += diff * diff;
                    }
                    double std = Math.Sqrt(variance / nTimes);
                    if (std > accStdThresholdMg && !epReasons.Contains("movement"))
                        epReasons.Add("movement");
                }
            }

            if (epReasons.Count > 0)
                reasons[i] = epReasons;
        }

        var rejected = reasons.Keys.OrderBy(k => k).ToList();
        var clean = Enumerable.Range(0, nEpochs).Where(k => !reasons.ContainsKey(k)).ToList();

        return new ArtifactReport
        {
            RejectedIndices = rejected,
            CleanIndices = clean,
            Reasons = reasons,
            TotalEpochs = nEpochs
        };
    }

    public static (Epochs CleanEpochs, ArtifactReport Report) RejectArtifacts(
        Epochs epochs,
        double amplitudeThresholdUv = 95.0,
        double peakToPeakThresholdUv = 160.0,
        double blinkThresholdUv = 85.0,
        double accStdThresholdMg = 35.0)
    {
        var report = DetectArtifacts(epochs, amplitudeThresholdUv, peakToPeakThresholdUv, blinkThresholdUv, accStdThresholdMg);
        var cleanEpochs = epochs.Subset(report.CleanIndices);
        return (cleanEpochs, report);
    }
}