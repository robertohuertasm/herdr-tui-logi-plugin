using System.Text.Json;

namespace Herdr.Tests;

public class ClosePaneTests
{
    [Fact]
    public void GestureRequiresHoldThenAnotherPressAndRelease()
    {
        var gesture = new ClosePaneGesture();
        Assert.Equal(ClosePaneGestureAction.None, gesture.Process(ClosePaneButtonEvent.Press, false));
        Assert.Equal(ClosePaneGestureAction.None, gesture.Process(ClosePaneButtonEvent.Release, false));
        Assert.Equal(ClosePaneGestureAction.None, gesture.Process(ClosePaneButtonEvent.Press, false));
        Assert.Equal(ClosePaneGestureAction.Arm, gesture.Process(ClosePaneButtonEvent.LongPress, false));
        Assert.Equal(ClosePaneGestureAction.None, gesture.Process(ClosePaneButtonEvent.RepeatPress, true));
        Assert.Equal(ClosePaneGestureAction.None, gesture.Process(ClosePaneButtonEvent.Release, true));
        Assert.Equal(ClosePaneGestureAction.None, gesture.Process(ClosePaneButtonEvent.Release, true));
        Assert.Equal(ClosePaneGestureAction.None, gesture.Process(ClosePaneButtonEvent.Press, true));
        Assert.Equal(ClosePaneGestureAction.Confirm, gesture.Process(ClosePaneButtonEvent.Release, true));
    }

    [Theory]
    [InlineData("p1", "terminal1", true)]
    [InlineData("p2", "terminal1", false)]
    [InlineData("p1", "replacement", false)]
    public async Task ConfirmationOnlyClosesTheSameFocusedPane(string focused, string terminal, bool expected)
    {
        using var server = new FakeHerdr();
        var methods = new List<string>();
        server.Behaviour = (socket, line) =>
        {
            using var request = JsonDocument.Parse(line);
            var method = request.RootElement.GetProperty("method").GetString()!;
            methods.Add(method);
            if (method == "session.snapshot")
            {
                var reply = new { result = new { snapshot = new { version = "0.9.5", protocol = 22, focused_pane_id = focused,
                    workspaces = Array.Empty<object>(), agents = Array.Empty<object>(), layouts = Array.Empty<object>(),
                    panes = new[] { new { pane_id = "p1", terminal_id = terminal, workspace_id = "w1", tab_id = "t1" } } } } };
                return FakeHerdr.SendAndClose(socket, JsonSerializer.Serialize(reply) + "\n");
            }
            Assert.Equal("p1", request.RootElement.GetProperty("params").GetProperty("pane_id").GetString());
            return FakeHerdr.SendAndClose(socket, """{"result":{}}""" + "\n");
        };
        var confirmation = new ClosePaneConfirmation(new HerdrClient(server.SocketPath));
        Assert.False(await confirmation.ConfirmAsync());
        Assert.True(confirmation.Arm(Snapshot()));
        Assert.True(confirmation.IsArmed);
        Assert.Equal(expected, await confirmation.ConfirmAsync());
        Assert.False(await confirmation.ConfirmAsync());
        Assert.Equal(expected ? new[] { "session.snapshot", "pane.close" } : new[] { "session.snapshot" }, methods);
    }

    [Fact]
    public async Task ExpiredOrCancelledConfirmationNeverConnects()
    {
        var clock = new TestClock();
        var confirmation = new ClosePaneConfirmation(new HerdrClient("/nonexistent.sock"), clock);
        Assert.True(confirmation.Arm(Snapshot()));
        clock.Advance(ClosePaneConfirmation.Window);
        Assert.False(confirmation.IsArmed);
        Assert.False(await confirmation.ConfirmAsync());
        Assert.True(confirmation.Arm(Snapshot()));
        confirmation.Cancel();
        Assert.False(await confirmation.ConfirmAsync());
        Assert.False(confirmation.Arm(Snapshot() with { Panes = [] }));
    }

    private static Snapshot Snapshot() => new()
    {
        FocusedPaneId = "p1",
        Panes = [new Pane { PaneId = "p1", TerminalId = "terminal1", WorkspaceId = "w1", TabId = "t1" }],
    };

    private sealed class TestClock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => this.ticks;
        public void Advance(TimeSpan duration) => this.ticks += duration.Ticks;
    }
}
