using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

namespace LslGame.Models;

/// <summary>
/// Common Spatial Patterns (CSP) transformer for two-class motor imagery.
/// Matches MNE / SciPy CSP implementation.
/// </summary>
public sealed class CspModel
{
    public int Components { get; }
    private Matrix<double>? _filters; // [2*m, n_channels]

    public CspModel(int components = 2)
    {
        Components = components;
    }

    /// <summary>
    /// Fits CSP spatial filters on epoched data.
    /// epochs: [n_epochs, n_times, n_channels]
    /// labels: [n_epochs] (must contain exactly two unique classes, e.g. "left", "right")
    /// </summary>
    public void Fit(double[,,] epochs, string[] labels)
    {
        int nEpochs = epochs.GetLength(0);
        int nTimes = epochs.GetLength(1);
        int nChannels = epochs.GetLength(2);

        var classes = labels.Distinct().ToList();
        if (classes.Count < 2)
            throw new ArgumentException("CSP requires at least two distinct classes.", nameof(labels));

        string c1 = classes[0];
        string c2 = classes[1];

        var cov1 = Matrix<double>.Build.Dense(nChannels, nChannels, 0.0);
        var cov2 = Matrix<double>.Build.Dense(nChannels, nChannels, 0.0);
        int count1 = 0, count2 = 0;

        for (int i = 0; i < nEpochs; i++)
        {
            // Extract epoch matrix [nChannels, nTimes]
            var X = Matrix<double>.Build.Dense(nChannels, nTimes);
            for (int ch = 0; ch < nChannels; ch++)
                for (int t = 0; t < nTimes; t++)
                    X[ch, t] = epochs[i, t, ch];

            // Normalize: C = (X * X^T) / trace(X * X^T)
            var C = X * X.Transpose();
            double trace = C.Trace();
            if (trace > 1e-12) C /= trace;

            if (labels[i] == c1)
            {
                cov1 += C;
                count1++;
            }
            else if (labels[i] == c2)
            {
                cov2 += C;
                count2++;
            }
        }

        if (count1 > 0) cov1 /= count1;
        if (count2 > 0) cov2 /= count2;

        // Composite covariance
        var composite = cov1 + cov2;

        // Eigendecomposition of composite
        var evd = composite.Evd();
        var eigenValues = evd.EigenValues.Real();
        var eigenVectors = evd.EigenVectors;

        // Whitening matrix: P = diag(lambda^(-1/2)) * V^T
        var P = Matrix<double>.Build.Dense(nChannels, nChannels);
        for (int ch = 0; ch < nChannels; ch++)
        {
            double val = eigenValues[ch];
            double invSqrt = val > 1e-10 ? 1.0 / Math.Sqrt(val) : 0.0;
            for (int j = 0; j < nChannels; j++)
                P[ch, j] = invSqrt * eigenVectors[j, ch];
        }

        // Whiten cov1
        var S1 = P * cov1 * P.Transpose();

        // Eigendecomposition of S1
        var evdS1 = S1.Evd();
        var evalsS1 = evdS1.EigenValues.Real().ToArray();
        var evecsS1 = evdS1.EigenVectors;

        // Sort eigenvectors by eigenvalues descending
        var sortedIndices = Enumerable.Range(0, nChannels)
            .OrderByDescending(idx => evalsS1[idx])
            .ToArray();

        // Pick top m and bottom m filters
        int m = Math.Min(Components, nChannels / 2);
        var selectedIdx = new List<int>();
        for (int k = 0; k < m; k++) selectedIdx.Add(sortedIndices[k]);
        for (int k = nChannels - m; k < nChannels; k++) selectedIdx.Add(sortedIndices[k]);

        int nFilters = selectedIdx.Count;
        var B_sub = Matrix<double>.Build.Dense(nFilters, nChannels);
        for (int row = 0; row < nFilters; row++)
        {
            int colIdx = selectedIdx[row];
            for (int ch = 0; ch < nChannels; ch++)
                B_sub[row, ch] = evecsS1[ch, colIdx];
        }

        // Projection W = B_sub * P
        _filters = B_sub * P;
    }

    /// <summary>
    /// Transforms single epoch [nTimes, nChannels] into log-variance feature vector [2*m].
    /// </summary>
    public double[] TransformSample(double[,] epoch)
    {
        if (_filters == null)
            throw new InvalidOperationException("CSP model is not fitted yet.");

        int nTimes = epoch.GetLength(0);
        int nChannels = epoch.GetLength(1);
        int nFilters = _filters.RowCount;

        // X: [nChannels, nTimes]
        var X = Matrix<double>.Build.Dense(nChannels, nTimes);
        for (int ch = 0; ch < nChannels; ch++)
            for (int t = 0; t < nTimes; t++)
                X[ch, t] = epoch[t, ch];

        // Z = W * X -> [nFilters, nTimes]
        var Z = _filters * X;

        var vars = new double[nFilters];
        double totalVar = 0.0;
        for (int f = 0; f < nFilters; f++)
        {
            double mean = 0.0;
            for (int t = 0; t < nTimes; t++) mean += Z[f, t];
            mean /= nTimes;
            double v = 0.0;
            for (int t = 0; t < nTimes; t++)
            {
                double d = Z[f, t] - mean;
                v += d * d;
            }
            v /= Math.Max(1, nTimes - 1);
            vars[f] = Math.Max(1e-12, v);
            totalVar += vars[f];
        }

        var features = new double[nFilters];
        for (int f = 0; f < nFilters; f++)
            features[f] = Math.Log(vars[f] / totalVar);

        return features;
    }
}

/// <summary>
/// Linear Discriminant Analysis (LDA) with sigmoid probability outputs.
/// </summary>
public sealed class LdaClassifier
{
    private double[]? _weights;
    private double _bias;
    public string Class1 { get; private set; } = "left";
    public string Class2 { get; private set; } = "right";

    public void Fit(double[][] X, string[] y)
    {
        var classes = y.Distinct().ToList();
        if (classes.Count < 2)
            throw new ArgumentException("LDA requires at least two distinct classes.", nameof(y));

        Class1 = classes[0];
        Class2 = classes[1];

        int nSamples = X.Length;
        int nFeatures = X[0].Length;

        var mean1 = new double[nFeatures];
        var mean2 = new double[nFeatures];
        int count1 = 0, count2 = 0;

        for (int i = 0; i < nSamples; i++)
        {
            if (y[i] == Class1)
            {
                for (int f = 0; f < nFeatures; f++) mean1[f] += X[i][f];
                count1++;
            }
            else
            {
                for (int f = 0; f < nFeatures; f++) mean2[f] += X[i][f];
                count2++;
            }
        }

        for (int f = 0; f < nFeatures; f++)
        {
            mean1[f] /= Math.Max(1, count1);
            mean2[f] /= Math.Max(1, count2);
        }

        // Pooled covariance
        var S = Matrix<double>.Build.Dense(nFeatures, nFeatures, 0.0);
        for (int i = 0; i < nSamples; i++)
        {
            var m = y[i] == Class1 ? mean1 : mean2;
            for (int r = 0; r < nFeatures; r++)
                for (int c = 0; c < nFeatures; c++)
                    S[r, c] += (X[i][r] - m[r]) * (X[i][c] - m[c]);
        }
        S /= Math.Max(1, nSamples - 2);

        // Regularization / shrinkage
        for (int f = 0; f < nFeatures; f++)
            S[f, f] += 1e-4;

        var S_inv = S.Inverse();

        // Weight vector w = S_inv * (mean1 - mean2)
        var diff = Vector<double>.Build.Dense(nFeatures);
        for (int f = 0; f < nFeatures; f++)
            diff[f] = mean1[f] - mean2[f];

        var w = S_inv * diff;
        _weights = w.ToArray();

        // Bias = -0.5 * (mean1 + mean2)^T * w
        double b = 0.0;
        for (int f = 0; f < nFeatures; f++)
            b += -0.5 * (mean1[f] + mean2[f]) * _weights[f];
        _bias = b;
    }

    /// <summary>
    /// Predicts posterior probability for Class1 [0..1].
    /// </summary>
    public double PredictProbaClass1(double[] features)
    {
        if (_weights == null)
            throw new InvalidOperationException("LDA classifier is not fitted.");

        double score = _bias;
        for (int f = 0; f < _weights.Length; f++)
            score += _weights[f] * features[f];

        // Sigmoid
        return 1.0 / (1.0 + Math.Exp(-Math.Clamp(score, -20.0, 20.0)));
    }
}