namespace Herdr.Tests;

public class AgentCloseConfirmationTests
{
    [Fact]
    public void HeldAgentStaysTargetedAfterReorder()
    {
        var confirmation = new AgentCloseConfirmation();
        var snapshot = new Snapshot { Recent = [Agent("p1", "terminal1"), Agent("p2", "terminal2")] };
        Assert.True(confirmation.Arm(FixedSlots.Recent(snapshot, 1)));
        // Slot 1 now shows another agent; the pinned target must not follow it.
        snapshot = snapshot with { Recent = [Agent("p2", "terminal2"), Agent("p1", "terminal1")] };
        Assert.Equal("p2", FixedSlots.Recent(snapshot, 1)?.PaneId);
        Assert.Equal(new CloseTarget("p1", "terminal1", "w1", "codex"), confirmation.Take());
        Assert.Null(confirmation.Take());
    }

    [Fact]
    public void ExpiredCancelledOrUnidentifiedAgentIsNeverTargeted()
    {
        var clock = new TestClock();
        var confirmation = new AgentCloseConfirmation(clock);
        Assert.True(confirmation.Arm(Agent("p1", "terminal1")));
        clock.Advance(ClosePaneConfirmation.Window);
        Assert.False(confirmation.IsArmed);
        Assert.True(confirmation.Expire());
        Assert.Null(confirmation.Take());
        Assert.True(confirmation.Arm(Agent("p1", "terminal1")));
        confirmation.Cancel();
        Assert.Null(confirmation.Take());
        Assert.False(confirmation.Arm(null));
        Assert.False(confirmation.Arm(Agent("p1", "")));
    }

    [Fact]
    public void RemoteAgentTargetUsesItsRoutedKey()
    {
        var confirmation = new AgentCloseConfirmation();
        Assert.True(confirmation.Arm(Agent("p1", "terminal1") with { MachineId = "m1" }));
        Assert.Equal("remote:m1:p1", confirmation.Take()?.PaneId);
    }

    private static Agent Agent(string pane, string terminal) =>
        new() { PaneId = pane, TerminalId = terminal, WorkspaceId = "w1", Kind = "codex" };

    private sealed class TestClock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => this.ticks;
        public void Advance(TimeSpan duration) => this.ticks += duration.Ticks;
    }
}
