using FluentAssertions;
using LslGame.Core;
using LslGame.Calibration;

namespace LslGame.Tests;

public class CalibrationTests
{
    [Fact]
    public void BalancedSequenceSatisfiesConstraints()
    {
        var classes = new[] { "left", "right", "rest" };
        var seq = CalibrationStateMachine.GenerateBalancedSequence(classes, count: 21, seed: 42, maxStreak: 3);

        seq.Should().HaveCount(21);
        seq.Count(x => x == "left").Should().Be(7);
        seq.Count(x => x == "right").Should().Be(7);
        seq.Count(x => x == "rest").Should().Be(7);

        // Check max consecutive streak <= 3
        int streak = 1;
        for (int i = 1; i < seq.Count; i++)
        {
            if (seq[i] == seq[i - 1])
            {
                streak++;
                streak.Should().BeLessThanOrEqualTo(3);
            }
            else streak = 1;
        }
    }

    [Fact]
    public void CalibrationSessionEmitsAsciiMarkersAndTransitionsCorrectly()
    {
        var timing = new CalibrationTiming
        {
            FixationSec = 1.0,
            CueSec = 0.5,
            ImagerySec = 1.0,
            RestMinSec = 0.5,
            RestMaxSec = 0.5,
            InterRunPauseSec = 2.0
        };

        var sm = new CalibrationStateMachine(
            classes: ["left", "right"],
            totalRuns: 1,
            trialsPerRun: 2,
            timing: timing,
            seed: 123);

        double t = 0.0;
        var startMarkers = sm.StartSession(t);
        startMarkers.Should().Contain(Markers.SessionStart);

        var runMarkers = sm.StartRun(t);
        runMarkers.Should().Contain(Markers.RunStart);
        runMarkers.Should().Contain(Markers.TrialStart);
        runMarkers.Should().Contain(Markers.FixationStart);

        var allEmitted = new List<string>();
        allEmitted.AddRange(startMarkers);
        allEmitted.AddRange(runMarkers);

        // Advance virtual clock until run completes
        while (!sm.IsCompleted && t < 100.0)
        {
            t += 0.25;
            var markers = sm.Update(t);
            allEmitted.AddRange(markers);
        }

        sm.IsCompleted.Should().BeTrue();
        sm.TrialsHistory.Should().HaveCount(2);

        allEmitted.Should().Contain(Markers.ImageryStart);
        allEmitted.Should().Contain(Markers.ImageryEnd);
        allEmitted.Should().Contain(Markers.RestStart);
        allEmitted.Should().Contain(Markers.TrialEnd);
        allEmitted.Should().Contain(Markers.RunEnd);
        allEmitted.Should().Contain(Markers.SessionEnd);

        // Validate pure ASCII constraint
        foreach (var m in allEmitted)
            Markers.AssertAscii(m);
    }
}