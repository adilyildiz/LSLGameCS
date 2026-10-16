using FluentAssertions;
using LslGame.Models;

namespace LslGame.Tests;

public class ProfileManagerTests
{
    [Fact]
    public void ProfileSavesAndLoadsCorrectly()
    {
        string subject = "test_sub_99";
        var profile = new BciProfile
        {
            SubjectName = subject,
            TrainingAccuracy = 0.92,
            Filters = [[1.0, 2.0, 3.0], [4.0, 5.0, 6.0]],
            Weights = [0.5, -0.5],
            Bias = 0.1,
            Class1 = "left",
            Class2 = "right",
            Components = 1,
            ChannelCount = 3
        };

        ProfileManager.SaveProfile(profile);

        var loaded = ProfileManager.LoadProfile(subject);
        loaded.Should().NotBeNull();
        loaded!.SubjectName.Should().Be(subject);
        loaded.TrainingAccuracy.Should().Be(0.92);
        loaded.Weights.Should().Equal([0.5, -0.5]);
        loaded.Bias.Should().Be(0.1);
        loaded.Filters.Should().HaveCount(2);

        var (csp, lda) = loaded.ToModel();
        csp.Components.Should().Be(1);
        lda.Class1.Should().Be("left");
        lda.Class2.Should().Be("right");
        lda.GetBias().Should().Be(0.1);

        // Cleanup
        string path = System.IO.Path.Combine(ProfileManager.ProfilesDirectory, $"{subject}.json");
        if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
    }
}