namespace LslGame.Processing;

/// <summary>
/// Epoched EEG dataset container.
/// Matches Python src/processing/epochs.py.
/// </summary>
public sealed class Epochs
{
    public double[,,] Data { get; } // [nEpochs, nTimes, nChannels]
    public double[] Times { get; } // [nTimes]
    public string[] Labels { get; } // [nEpochs]
    public int[] RunIndices { get; } // [nEpochs]
    public int[] TrialIndices { get; } // [nEpochs]
    public double SampleRate { get; }
    public string[] ChNames { get; }
    public double[,,]? AccData { get; } // [nEpochs, nTimes, 3]

    public int Count => Labels.Length;
    public int TimesCount => Times.Length;
    public int ChannelCount => ChNames.Length;

    public Epochs(
        double[,,] data,
        double[] times,
        string[] labels,
        int[] runIndices,
        int[] trialIndices,
        double sampleRate,
        string[] chNames,
        double[,,]? accData = null)
    {
        Data = data;
        Times = times;
        Labels = labels;
        RunIndices = runIndices;
        TrialIndices = trialIndices;
        SampleRate = sampleRate;
        ChNames = chNames;
        AccData = accData;
    }

    public Epochs Subset(IReadOnlyList<int> indices)
    {
        int nSub = indices.Count;
        int nTimes = TimesCount;
        int nCh = ChannelCount;

        var subData = new double[nSub, nTimes, nCh];
        var subLabels = new string[nSub];
        var subRuns = new int[nSub];
        var subTrials = new int[nSub];
        double[,,]? subAcc = AccData != null ? new double[nSub, nTimes, 3] : null;

        for (int i = 0; i < nSub; i++)
        {
            int src = indices[i];
            subLabels[i] = Labels[src];
            subRuns[i] = RunIndices[src];
            subTrials[i] = TrialIndices[src];

            for (int t = 0; t < nTimes; t++)
            {
                for (int ch = 0; ch < nCh; ch++)
                    subData[i, t, ch] = Data[src, t, ch];

                if (subAcc != null && AccData != null)
                {
                    for (int a = 0; a < 3; a++)
                        subAcc[i, t, a] = AccData[src, t, a];
                }
            }
        }

        return new Epochs(subData, Times, subLabels, subRuns, subTrials, SampleRate, ChNames, subAcc);
    }
}
