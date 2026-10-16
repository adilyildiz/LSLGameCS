namespace LslGame.Processing;

/// <summary>
/// Real-time IIR filters implemented as cascaded second-order sections (SOS).
/// SOS coefficients exported directly from scipy.signal for bit-level numerical parity.
/// Matches Python src/processing/filters.py.
/// </summary>
public sealed class SosFilter
{
    private readonly double[,] _sos; // [n_sections, 6]: b0 b1 b2 a0 a1 a2

    public int SectionCount { get; }

    public SosFilter(double[,] sos)
    {
        _sos = (double[,])sos.Clone();
        SectionCount = sos.GetLength(0);
    }

    /// <summary>
    /// Applies the SOS filter to a single sample using transposed direct-form II:
    ///   y[n] = b0*x[n] + z1[n-1]
    ///   z1[n] = b1*x[n] - a1*y[n] + z2[n-1]
    ///   z2[n] = b2*x[n] - a2*y[n]
    /// </summary>
    public double ProcessSample(double x, double[,] state)
    {
        for (int s = 0; s < SectionCount; s++)
        {
            double b0 = _sos[s, 0], b1 = _sos[s, 1], b2 = _sos[s, 2];
            double a1 = _sos[s, 4], a2 = _sos[s, 5];

            double y = b0 * x + state[s, 0];
            state[s, 0] = b1 * x - a1 * y + state[s, 1];
            state[s, 1] = b2 * x - a2 * y;
            x = y;
        }
        return x;
    }

    /// <summary>Creates fresh per-channel state array (zeros).</summary>
    public double[,] CreateState() => new double[SectionCount, 2];

    // ---- Golden SOS filters for 250 Hz ----

    /// <summary>
    /// Butterworth bandpass 8–30 Hz, order 4, fs=250 Hz.
    /// scipy.signal.butter(4, [8.0/125.0, 30.0/125.0], btype='bandpass', output='sos')
    /// </summary>
    public static SosFilter BandpassMu250Hz()
    {
        double[,] sos =
        {
            { 0.003111903604586019, 0.006223807209172038, 0.003111903604586019, 1.0, -1.2672931915575452, 0.493789184143714 },
            { 1.0,                  2.0,                  1.0,                  1.0, -1.6370396227024675, 0.7019629536078331 },
            { 1.0,                 -2.0,                  1.0,                  1.0, -1.2863770965150896, 0.7317912749111386 },
            { 1.0,                 -2.0,                  1.0,                  1.0, -1.8683294067657878, 0.9091564065038225 }
        };
        return new SosFilter(sos);
    }

    /// <summary>
    /// IIR notch at 50 Hz, Q=30, fs=250 Hz.
    /// scipy.signal.iirnotch(50.0, 30.0, fs=250.0) -> tf2sos
    /// </summary>
    public static SosFilter Notch50Hz250Hz()
    {
        double[,] sos =
        {
            { 0.9794827609814495, -0.6053536376811252, 0.9794827609814492, 1.0, -0.6053536376811253, 0.958965521962899 }
        };
        return new SosFilter(sos);
    }

    /// <summary>
    /// IIR notch at 100 Hz, Q=30, fs=250 Hz.
    /// scipy.signal.iirnotch(100.0, 30.0, fs=250.0) -> tf2sos
    /// </summary>
    public static SosFilter Notch100Hz250Hz()
    {
        double[,] sos =
        {
            { 0.95977356895352, 1.5529462560705858, 0.95977356895352, 1.0, 1.552946256070586, 0.9195471379070398 }
        };
        return new SosFilter(sos);
    }
}

/// <summary>
/// Manages per-channel filter state for a multi-channel signal.
/// </summary>
public sealed class MultiChannelFilter
{
    private readonly SosFilter _filter;
    private readonly double[][,] _states;

    public MultiChannelFilter(SosFilter filter, int nChannels)
    {
        _filter = filter;
        _states = new double[nChannels][,];
        for (int ch = 0; ch < nChannels; ch++)
            _states[ch] = filter.CreateState();
    }

    public void ProcessSample(Span<double> sample)
    {
        for (int ch = 0; ch < sample.Length; ch++)
            sample[ch] = _filter.ProcessSample(sample[ch], _states[ch]);
    }

    public void Reset()
    {
        foreach (var s in _states) Array.Clear(s);
    }
}

/// <summary>
/// Stateful filter cascading Notch 50Hz, Notch 100Hz, and Bandpass 8-30Hz across channels.
/// Gives identical results whether applied block-by-block or all at once (error &lt; 1e-6).
/// Matches Python StatefulFilter in src/processing/filters.py.
/// </summary>
public sealed class StatefulFilter
{
    private readonly int _nChannels;
    private readonly MultiChannelFilter _notch50;
    private readonly MultiChannelFilter _notch100;
    private readonly MultiChannelFilter _bandpass;

    public StatefulFilter(int nChannels = 7)
    {
        _nChannels = nChannels;
        _notch50   = new MultiChannelFilter(SosFilter.Notch50Hz250Hz(), nChannels);
        _notch100  = new MultiChannelFilter(SosFilter.Notch100Hz250Hz(), nChannels);
        _bandpass  = new MultiChannelFilter(SosFilter.BandpassMu250Hz(), nChannels);
    }

    public void Reset()
    {
        _notch50.Reset();
        _notch100.Reset();
        _bandpass.Reset();
    }

    public void FilterSample(Span<double> sample)
    {
        _notch50.ProcessSample(sample);
        _notch100.ProcessSample(sample);
        _bandpass.ProcessSample(sample);
    }

    public double[,] FilterBlock(double[,] block)
    {
        int nSamples = block.GetLength(0);
        int nCh = block.GetLength(1);
        var result = new double[nSamples, nCh];
        var row = new double[nCh];

        for (int i = 0; i < nSamples; i++)
        {
            for (int ch = 0; ch < nCh; ch++)
                row[ch] = block[i, ch];

            FilterSample(row);

            for (int ch = 0; ch < nCh; ch++)
                result[i, ch] = row[ch];
        }

        return result;
    }
}

