using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace LslGame.Core;

/// <summary>Top-level application configuration (mirrors config/lslgame.yaml).</summary>
public sealed class AppConfig
{
    public EegConfig Eeg { get; set; } = new();
    public FilterConfig Filter { get; set; } = new();
    public EpochConfig Epoch { get; set; } = new();
    public ClassifierConfig Classifier { get; set; } = new();
    public GameConfig Game { get; set; } = new();

    /// <summary>Loads config from a YAML file. Falls back to defaults if file not found.</summary>
    public static AppConfig Load(string? path = null)
    {
        path ??= FindConfigFile();
        if (path is null || !File.Exists(path))
            return new AppConfig();

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        using var reader = new StreamReader(path);
        return deserializer.Deserialize<AppConfig>(reader) ?? new AppConfig();
    }

    private static string? FindConfigFile()
    {
        // Search up from working directory
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "config", "lslgame.yaml");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}

public sealed class EegConfig
{
    public int Channels { get; set; } = LslStreamInfo.EegChannels;
    public double SampleRate { get; set; } = LslStreamInfo.EegSampleRate;
    public IList<string> Labels { get; set; } = LslStreamInfo.EegLabels.ToList();
}

public sealed class FilterConfig
{
    public double LowCutHz  { get; set; } = 8.0;
    public double HighCutHz { get; set; } = 30.0;
    public double NotchHz   { get; set; } = 50.0;
    public int    Order     { get; set; } = 4;
}

public sealed class EpochConfig
{
    public double TStartS { get; set; } = 0.5;
    public double TEndS   { get; set; } = 2.5;
    public bool   Baseline { get; set; } = true;
}

public sealed class ClassifierConfig
{
    public int    CspComponents { get; set; } = 4;
    public int    CvFolds       { get; set; } = 5;
    public double ConfidenceThreshold { get; set; } = 0.6;
    public int    SmoothingWindow    { get; set; } = 3;
}

public sealed class GameConfig
{
    public int    WindowWidth  { get; set; } = 1024;
    public int    WindowHeight { get; set; } = 768;
    public int    TargetFps    { get; set; } = 60;
    public string Title        { get; set; } = "LSL BCI Ball Game";
}
