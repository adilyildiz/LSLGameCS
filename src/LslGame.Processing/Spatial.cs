namespace LslGame.Processing;

/// <summary>
/// Spatial filtering methods: Small Laplacian and CAR (Common Average Reference).
/// Matches Python src/processing/spatial.py.
/// </summary>
public static class Spatial
{
    public static readonly string[] DefaultChNames = ["F3", "F4", "C3", "Cz", "C4", "P3", "P4"];

    /// <summary>
    /// Applies Small Laplacian filter on (nSamples x nChannels) EEG data.
    /// C3' = C3 - mean(F3, P3, Cz)
    /// C4' = C4 - mean(F4, P4, Cz)
    /// </summary>
    public static double[,] ApplyLaplacian(double[,] data, IReadOnlyList<string>? chNames = null)
    {
        var list = (chNames ?? DefaultChNames).ToList();
        int c3 = list.IndexOf("C3");
        int c4 = list.IndexOf("C4");
        int cz = list.IndexOf("Cz");
        int f3 = list.IndexOf("F3");
        int f4 = list.IndexOf("F4");
        int p3 = list.IndexOf("P3");
        int p4 = list.IndexOf("P4");

 int nSamples = data.GetLength(0);
 int nChannels = data.GetLength(1);
 var outData = (double[,])data.Clone();

 for (int i = 0; i < nSamples; i++)
 {
 double c3Lap = outData[i, c3] - (outData[i, f3] + outData[i, p3] + outData[i, cz]) / 3.0;
 double c4Lap = outData[i, c4] - (outData[i, f4] + outData[i, p4] + outData[i, cz]) / 3.0;
 outData[i, c3] = c3Lap;
 outData[i, c4] = c4Lap;
 }
 return outData;
 }

 /// <summary>
 /// Common Average Reference (CAR): subtracts the instantaneous mean across channels.
 /// </summary>
 public static double[,] ApplyCar(double[,] data)
 {
 int nSamples = data.GetLength(0);
 int nChannels = data.GetLength(1);
 var outData = new double[nSamples, nChannels];

 for (int i = 0; i < nSamples; i++)
 {
 double sum = 0.0;
 for (int ch = 0; ch < nChannels; ch++)
 sum += data[i, ch];
 double mean = sum / nChannels;
 for (int ch = 0; ch < nChannels; ch++)
 outData[i, ch] = data[i, ch] - mean;
 }
 return outData;
 }
}
