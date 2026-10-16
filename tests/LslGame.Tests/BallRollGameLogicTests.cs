using FluentAssertions;
using LslGame.Core;
using LslGame.Game;

namespace LslGame.Tests;

public class BallRollGameLogicTests
{
    [Fact]
    public void InitialStateIsActiveAndSpawnsValidTarget()
    {
        var logic = new BallRollGameLogic(randomSeed: 42);
        logic.State.Should().Be("active");
        logic.TargetSide.Should().Match(s => s == "left" || s == "right");
        logic.BallX.Should().Be(logic.CenterX);
        logic.BallY.Should().Be(logic.CenterY);
        logic.Score.Should().Be(0);
        logic.Hits.Should().Be(0);
        logic.Misses.Should().Be(0);
    }

    [Fact]
    public void BallMovesLeftWhenCommandIsLeft()
    {
        var logic = new BallRollGameLogic(randomSeed: 42);
        float initialX = logic.BallX;
        var cmd = new ControlCommand { Direction = Direction.Left, HasSignal = true };

        logic.Update(0.1f, cmd);

        logic.BallX.Should().BeLessThan(initialX);
    }

    [Fact]
    public void BallMovesRightWhenCommandIsRight()
    {
        var logic = new BallRollGameLogic(randomSeed: 42);
        float initialX = logic.BallX;
        var cmd = new ControlCommand { Direction = Direction.Right, HasSignal = true };

        logic.Update(0.1f, cmd);

        logic.BallX.Should().BeGreaterThan(initialX);
    }

    [Fact]
    public void TargetHitOccursWhenBallReachesTarget()
    {
        var logic = new BallRollGameLogic(randomSeed: 42);
        string side = logic.TargetSide;
        var cmd = new ControlCommand
        {
            Direction = side == "left" ? Direction.Left : Direction.Right,
            HasSignal = true
        };

        bool hit = false;
        // Step until hit (max 3 seconds)
        for (int i = 0; i < 60; i++)
        {
            if (logic.Update(0.05f, cmd))
            {
                hit = true;
                break;
            }
        }

        hit.Should().BeTrue();
        logic.Hits.Should().Be(1);
        logic.Score.Should().Be(1);
        logic.LastOutcome.Should().Be("hit");
        logic.IsPaused.Should().BeTrue();
    }

    [Fact]
    public void WrongWallCollisionTriggersMiss()
    {
        var logic = new BallRollGameLogic(randomSeed: 42);
        string side = logic.TargetSide;
        // Move towards WRONG wall
        var cmd = new ControlCommand
        {
            Direction = side == "left" ? Direction.Right : Direction.Left,
            HasSignal = true
        };

        // Step until miss
        for (int i = 0; i < 60; i++)
        {
            logic.Update(0.05f, cmd);
            if (logic.IsPaused) break;
        }

        logic.Misses.Should().Be(1);
        logic.LastOutcome.Should().Be("miss");
        logic.IsPaused.Should().BeTrue();
    }

    [Fact]
    public void TimeoutTriggersAfterTrialTimeout()
    {
        var logic = new BallRollGameLogic(trialTimeout: 1.0f, randomSeed: 42);
        var restCmd = new ControlCommand { Direction = Direction.Rest, HasSignal = true };

        logic.Update(0.5f, restCmd);
        logic.State.Should().Be("active");

        logic.Update(0.6f, restCmd);
        logic.State.Should().Be("pause");
        logic.LastOutcome.Should().Be("timeout");
        logic.Misses.Should().Be(1);
    }
}