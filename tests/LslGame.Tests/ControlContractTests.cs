using FluentAssertions;
using LslGame.Core;
using LslGame.Control;

namespace LslGame.Tests;

/// <summary>Tests for ControlContract (Öncelik 1 — no LSL required).</summary>
public class ControlContractTests
{
    [Fact]
    public void AllCueMarkersArePureAscii()
    {
        foreach (var cue in Markers.AllCues)
            Markers.IsAscii(cue).Should().BeTrue(because: $"marker '{cue}' must be ASCII");
    }

    [Theory]
    [InlineData("left",  ControlContract.CmdLeft)]
    [InlineData("right", ControlContract.CmdRight)]
    [InlineData("down",  ControlContract.CmdDown)]
    [InlineData("up",    ControlContract.CmdUp)]
    [InlineData("stop",  ControlContract.CmdStop)]
    [InlineData("both",  ControlContract.CmdBoth)]
    public void MarkerToCommandRoundTrip(string marker, int expectedCmd)
    {
        ControlContract.MarkerToCommand(marker).Should().Be(expectedCmd);
        ControlContract.CommandToMarker(expectedCmd).Should().Be(marker);
    }

    [Fact]
    public void UnknownMarkerReturnsCmdNone()
    {
        ControlContract.MarkerToCommand("unknown_marker").Should().Be(ControlContract.CmdNone);
    }

    [Theory]
    [InlineData(-4.0,  true)]   // lower bound
    [InlineData(0.0,   true)]
    [InlineData(12.0,  true)]   // upper bound
    [InlineData(-4.1,  false)]  // out of range
    [InlineData(12.1,  false)]  // out of range
    public void AlignmentJitterBounds(double jitterMs, bool expected)
    {
        ControlContract.IsAlignmentValid(jitterMs).Should().Be(expected);
    }

    [Fact]
    public void AssertAsciiThrowsForNonAsciiMarker()
    {
        var act = () => Markers.AssertAscii("müzik");
        act.Should().Throw<ArgumentException>();
    }
}
