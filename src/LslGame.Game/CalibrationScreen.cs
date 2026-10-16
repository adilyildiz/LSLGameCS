using System.Numerics;
using Raylib_cs;
using LslGame.Calibration;
using LslGame.Core;
using LslGame.Models;
using LslGame.Processing;
using SharpLSL;

namespace LslGame.Game;

/// <summary>
/// Raylib-based Graz-BCI Calibration Screen and automatic profile trainer.
/// Records live EEG, presents visual Graz cues, extracts epochs, trains CSP+LDA, and saves profile.
/// </summary>
public sealed class CalibrationScreen
{
    private readonly int _width;
    private readonly int _height;
    private readonly string _subjectName;
    private readonly CalibrationStateMachine _sm;
    private readonly MarkerPublisher _markerPub;

    // Recorded samples: (timestamp, sample[7])
    private readonly List<(double Timestamp, double[] Sample)> _recordedEeg = [];
    private readonly Lock _recordLock = new();
    private StreamInlet? _eegInlet;
    private readonly float[] _rawBuffer = new float[LslStreamInfo.EegTotalChannels];

    // Colors matching theme
    private static readonly Color BgColor       = new(15, 18, 28, 255);
    private static readonly Color CrossColor    = new(240, 240, 240, 255);
    private static readonly Color CueColor      = new(0, 220, 255, 255);
    private static readonly Color TextColor     = new(240, 240, 240, 255);
    private static readonly Color DimText       = new(160, 160, 180, 255);
    private static readonly Color SuccessColor  = new(80, 240, 80, 255);
    private static readonly Color BarBg         = new(40, 45, 60, 255);
    private static readonly Color BarFill       = new(0, 180, 230, 255);

    public CalibrationScreen(
        string subjectName = "sub-01",
        int width = 800,
        int height = 500,
        int totalRuns = 2,
        int trialsPerRun = 10)
    {
        _subjectName = subjectName;
        _width = width;
        _height = height;

        _markerPub = new MarkerPublisher(sourceId: $"Calib_Markers_{subjectName}");

        var timing = new CalibrationTiming
        {
            FixationSec = 2.0,
            CueSec = 1.0,
            ImagerySec = 4.0,
            RestMinSec = 1.5,
            RestMaxSec = 2.5,
            InterRunPauseSec = 10.0
        };

        _sm = new CalibrationStateMachine(
            classes: ["left", "right"],
            totalRuns: totalRuns,
            trialsPerRun: trialsPerRun,
            timing: timing,
            seed: 42);

        TryConnectEeg();
    }

    private void TryConnectEeg()
    {
        try
        {
            var results = LSL.Resolve($"name='{LslStreamInfo.EegName}'", 1, 1, 1.0);
            if (results.Length == 0)
                results = LSL.Resolve($"type='{LslStreamInfo.EegType}'", 1, 1, 1.0);

            if (results.Length > 0)
                _eegInlet = new StreamInlet(results[0]);
        }
        catch
        {
            _eegInlet = null;
        }
    }

    private void PollEeg()
    {
        if (_eegInlet == null) return;
        while (true)
        {
            double ts = _eegInlet.PullSample(_rawBuffer, 0.0);
            if (ts <= 0.0) break;

            // Keep first 7 EEG channels
            var eeg7 = new double[7];
            for (int ch = 0; ch < 7; ch++)
                eeg7[ch] = _rawBuffer[ch];

            lock (_recordLock)
            {
                _recordedEeg.Add((ts, eeg7));
            }
        }
    }

    public BciProfile? RunSession()
    {
        bool started = false;
        BciProfile? trainedProfile = null;
        string statusMessage = "Kalibrasyonu baslatmak icin [SPACE] tusuna basin.";

        while (!Raylib.WindowShouldClose())
        {
            PollEeg();
            double now = LSL.GetLocalClock();

            if (!started)
            {
                if (Raylib.IsKeyPressed(KeyboardKey.Space))
                {
                    started = true;
                    var startMarkers = _sm.StartSession(now);
                    foreach (var m in startMarkers) _markerPub.Push(m, now);

                    var runMarkers = _sm.StartRun(now);
                    foreach (var m in runMarkers) _markerPub.Push(m, now);
                }
            }
            else if (!_sm.IsCompleted)
            {
                var emitted = _sm.Update(now);
                foreach (var m in emitted) _markerPub.Push(m, now);

                if (_sm.IsCompleted && trainedProfile == null)
                {
                    statusMessage = "Veriler isleniyor ve model egitiliyor...";
                    trainedProfile = TrainModel();
                }
            }
            else
            {
                // Completed
                if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.Space))
                    break;
            }

            Draw(started, trainedProfile, statusMessage);
        }

        return trainedProfile;
    }

    private BciProfile? TrainModel()
    {
        List<(double Timestamp, double[] Sample)> recorded;
        lock (_recordLock)
        {
            recorded = new List<(double, double[])>(_recordedEeg);
        }

        if (recorded.Count < 500)
        {
            Console.WriteLine("[CalibrationScreen] Yetersiz EEG verisi kaydedildi.");
            return null;
        }

        // Filter continuous EEG causally
        var filter = new StatefulFilter(7);
        int nTotal = recorded.Count;
        var rawMatrix = new double[nTotal, 7];
        for (int i = 0; i < nTotal; i++)
            for (int ch = 0; ch < 7; ch++)
                rawMatrix[i, ch] = recorded[i].Sample[ch];

        var filteredMatrix = filter.FilterBlock(rawMatrix);

        // Epoching: imagery_start + 0.5s to 3.5s (3 seconds = 750 samples at 250 Hz)
        int epochSamples = (int)(3.0 * 250.0);
        var epochsList = new List<double[,]>();
        var labelsList = new List<string>();

        foreach (var trial in _sm.TrialsHistory)
        {
            if (!trial.Timestamps.TryGetValue("imagery_start", out double imgStart))
                continue;

            double windowStart = imgStart + 0.5;
            double windowEnd = imgStart + 3.5;

            // Find sample index in recorded
            int startIdx = -1;
            for (int i = 0; i < recorded.Count; i++)
            {
                if (recorded[i].Timestamp >= windowStart)
                {
                    startIdx = i;
                    break;
                }
            }

            if (startIdx >= 0 && (startIdx + epochSamples) <= filteredMatrix.GetLength(0))
            {
                var ep = new double[epochSamples, 7];
                for (int t = 0; t < epochSamples; t++)
                    for (int ch = 0; ch < 7; ch++)
                        ep[t, ch] = filteredMatrix[startIdx + t, ch];

                epochsList.Add(ep);
                labelsList.Add(trial.ClassName);
            }
        }

        if (epochsList.Count < 4)
        {
            Console.WriteLine("[CalibrationScreen] Epok sayisi egitim icin yetersiz.");
            return null;
        }

        // Convert to 3D array
        int nEpochs = epochsList.Count;
        var epochs3D = new double[nEpochs, epochSamples, 7];
        for (int i = 0; i < nEpochs; i++)
            for (int t = 0; t < epochSamples; t++)
                for (int ch = 0; ch < 7; ch++)
                    epochs3D[i, t, ch] = epochsList[i][t, ch];

        var csp = new CspModel(components: 2);
        csp.Fit(epochs3D, labelsList.ToArray());

        var feats = new double[nEpochs][];
        for (int i = 0; i < nEpochs; i++)
            feats[i] = csp.TransformSample(epochsList[i]);

        var lda = new LdaClassifier();
        lda.Fit(feats, labelsList.ToArray());

        // Compute training accuracy
        int correct = 0;
        for (int i = 0; i < nEpochs; i++)
        {
            double p1 = lda.PredictProbaClass1(feats[i]);
            string pred = p1 >= 0.5 ? lda.Class1 : lda.Class2;
            if (pred == labelsList[i]) correct++;
        }
        double accuracy = (double)correct / nEpochs;

        var profile = BciProfile.FromModel(_subjectName, csp, lda, accuracy, 7);
        ProfileManager.SaveProfile(profile);

        return profile;
    }

    private void Draw(bool started, BciProfile? trainedProfile, string statusMessage)
    {
        Raylib.BeginDrawing();
        Raylib.ClearBackground(BgColor);

        float cx = _width / 2f;
        float cy = _height / 2f;

        // Header
        string title = $"GRAZ-BCI KALIBRASYON PROTOKOLU [{_subjectName}]";
        int titleW = Raylib.MeasureText(title, 22);
        Raylib.DrawText(title, (int)(cx - titleW / 2), 24, 22, TextColor);

        if (!started)
        {
            string info1 = "Ekrandaki yone (SOL veya SAG) odaklanarak motor imgeleme yapin.";
            string info2 = "Sol ok ciktiginda SOL ELINIZI, sag ok ciktiginda SAG ELINIZI hayal edin.";
            int w1 = Raylib.MeasureText(info1, 16);
            int w2 = Raylib.MeasureText(info2, 16);
            Raylib.DrawText(info1, (int)(cx - w1 / 2), (int)(cy - 50), 16, DimText);
            Raylib.DrawText(info2, (int)(cx - w2 / 2), (int)(cy - 20), 16, DimText);

            int startW = Raylib.MeasureText(statusMessage, 20);
            Raylib.DrawText(statusMessage, (int)(cx - startW / 2), (int)(cy + 50), 20, CueColor);
        }
        else if (!_sm.IsCompleted)
        {
            // Graz-BCI stage rendering
            switch (_sm.Stage)
            {
                case CalibrationStage.Fixation:
                    // Fixation cross
                    int arm = 30;
                    int thick = 4;
                    Raylib.DrawRectangle((int)(cx - arm), (int)(cy - thick / 2), arm * 2, thick, CrossColor);
                    Raylib.DrawRectangle((int)(cx - thick / 2), (int)(cy - arm), thick, arm * 2, CrossColor);
                    break;

                case CalibrationStage.Cue:
                case CalibrationStage.Imagery:
                    string cls = _sm.CurrentClass ?? "rest";
                    bool isLeft = cls == "left";
                    string cueText = isLeft ? "SOL EL" : (cls == "right" ? "SAG EL" : "DINLENME");
                    int cueW = Raylib.MeasureText(cueText, 28);
                    Raylib.DrawText(cueText, (int)(cx - cueW / 2), (int)(cy - 80), 28, CueColor);

                    // Direction Arrow
                    float arrowW = 40;
                    float arrowH = 50;
                    if (isLeft)
                    {
                        var p1 = new Vector2(cx - 30, cy);
                        var p2 = new Vector2(cx - 30 + arrowW, cy - arrowH / 2);
                        var p3 = new Vector2(cx - 30 + arrowW, cy + arrowH / 2);
                        Raylib.DrawTriangle(p1, p3, p2, CueColor);
                    }
                    else if (cls == "right")
                    {
                        var p1 = new Vector2(cx + 30, cy);
                        var p2 = new Vector2(cx + 30 - arrowW, cy + arrowH / 2);
                        var p3 = new Vector2(cx + 30 - arrowW, cy - arrowH / 2);
                        Raylib.DrawTriangle(p1, p3, p2, CueColor);
                    }

                    if (_sm.Stage == CalibrationStage.Imagery)
                    {
                        string imgHint = "Motor Imgeleme Devam Ediyor...";
                        int hintW = Raylib.MeasureText(imgHint, 18);
                        Raylib.DrawText(imgHint, (int)(cx - hintW / 2), (int)(cy + 60), 18, Color.LightGray);
                    }
                    break;

                case CalibrationStage.Rest:
                    string restStr = "Dinlenme...";
                    int restW = Raylib.MeasureText(restStr, 24);
                    Raylib.DrawText(restStr, (int)(cx - restW / 2), (int)(cy - 12), 24, DimText);
                    break;

                case CalibrationStage.InterRunPause:
                    string pauseStr = "Run Tamamlandi! Dinlenme Molasi...";
                    int pauseW = Raylib.MeasureText(pauseStr, 22);
                    Raylib.DrawText(pauseStr, (int)(cx - pauseW / 2), (int)(cy - 12), 22, Color.Yellow);
                    break;
            }

            // Progress HUD
            string hud = $"Run: {_sm.CurrentRun + 1}/{_sm.TotalRuns} | Deneme: {_sm.CurrentTrialInRun + 1}/{_sm.TrialsPerRun}";
            Raylib.DrawText(hud, 30, _height - 50, 18, DimText);

            // Progress Bar
            float progress = (float)_sm.GlobalTrialIndex / Math.Max(1, _sm.TotalRuns * _sm.TrialsPerRun);
            int barW = _width - 60;
            Raylib.DrawRectangle(30, _height - 24, barW, 8, BarBg);
            Raylib.DrawRectangle(30, _height - 24, (int)(barW * progress), 8, BarFill);
        }
        else
        {
            // Completed
            string congrats = "TEBRIKLER! KALIBRASYON TAMAMLANDI";
            int cW = Raylib.MeasureText(congrats, 24);
            Raylib.DrawText(congrats, (int)(cx - cW / 2), (int)(cy - 60), 24, SuccessColor);

            if (trainedProfile != null)
            {
                string accText = $"Model Egitildi: {trainedProfile.SubjectName}.json | Dogruluk: %{trainedProfile.TrainingAccuracy * 100:F0}";
                int accW = Raylib.MeasureText(accText, 18);
                Raylib.DrawText(accText, (int)(cx - accW / 2), (int)(cy - 10), 18, TextColor);
            }

            string cont = "Ana menuye donmek icin [ENTER] veya [SPACE] basin.";
            int contW = Raylib.MeasureText(cont, 18);
            Raylib.DrawText(cont, (int)(cx - contW / 2), (int)(cy + 50), 18, CueColor);
        }

        Raylib.EndDrawing();
    }
}