using System.Text.Json;

namespace LslGame.Models;

/// <summary>
/// Serializable profile containing trained CSP spatial filters and LDA classifier weights.
/// </summary>
public sealed class BciProfile
{
    public string SubjectName { get; set; } = "sub-01";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public double TrainingAccuracy { get; set; }
    public double[][] Filters { get; set; } = [];
    public double[] Weights { get; set; } = [];
    public double Bias { get; set; }
    public string Class1 { get; set; } = "left";
    public string Class2 { get; set; } = "right";
    public int Components { get; set; } = 2;
    public int ChannelCount { get; set; } = 7;

    public (CspModel csp, LdaClassifier lda) ToModel()
    {
        var csp = new CspModel(Components);
        csp.LoadFiltersArray(Filters);

        var lda = new LdaClassifier();
        lda.LoadParameters(Weights, Bias, Class1, Class2);

        return (csp, lda);
    }

    public static BciProfile FromModel(
        string subjectName,
        CspModel csp,
        LdaClassifier lda,
        double accuracy,
        int channelCount = 7)
    {
        return new BciProfile
        {
            SubjectName = subjectName,
            CreatedAt = DateTime.UtcNow,
            TrainingAccuracy = accuracy,
            Filters = csp.GetFiltersArray() ?? [],
            Weights = lda.GetWeights() ?? [],
            Bias = lda.GetBias(),
            Class1 = lda.Class1,
            Class2 = lda.Class2,
            Components = csp.Components,
            ChannelCount = channelCount
        };
    }
}

public static class ProfileManager
{
    public static string ProfilesDirectory => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles");

    public static void SaveProfile(BciProfile profile)
    {
        Directory.CreateDirectory(ProfilesDirectory);
        string path = Path.Combine(ProfilesDirectory, $"{profile.SubjectName}.json");
        string json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    public static BciProfile? LoadProfile(string subjectName)
    {
        string path = Path.Combine(ProfilesDirectory, $"{subjectName}.json");
        if (!File.Exists(path)) return null;
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<BciProfile>(json);
    }

    public static List<string> ListProfiles()
    {
        if (!Directory.Exists(ProfilesDirectory)) return [];
        return Directory.GetFiles(ProfilesDirectory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList()!;
    }
}