using System.Text.Json;

namespace Herdr.Tests;

public class RemoteAgentsTests
{
    private const string Snapshot = """{"snapshot":{"agents":[{"pane_id":"w1:p1","terminal_id":"term","workspace_id":"w1","agent":"codex","agent_status":"working"}]}}""";

    [Fact]
    public async Task RemoteSnapshotQualifiesIdentityAndRoutesFocusWithoutAShell()
    {
        var calls = new List<string[]>();
        var remote = new RemoteAgents(args =>
        {
            calls.Add(args);
            return Task.FromResult(Snapshot);
        });
        var agent = Assert.Single(await remote.SnapshotAsync("machine-1", "Build"));
        Assert.Equal("w1:p1", agent.PaneId);
        Assert.NotEqual(new Agent { PaneId = "w1:p1" }.Key, agent.Key);
        Assert.True(HerdrClient.RemoteTarget(agent.Key, out var machine, out var pane));
        await remote.PaneAsync(machine, pane, "focus");
        Assert.Equal(new[] { "--machine", "machine-1", "agent", "focus", "w1:p1" }, calls[1]);
        Assert.Equal(FixedSlots.AgentLabel(agent with { MachineId = null, MachineLabel = null }, 1), FixedSlots.AgentLabel(agent, 1));
    }

    [Fact]
    public async Task FailedMachineDoesNotHideHealthyMachinesAndDisabledProfilesAreSkipped()
    {
        using var log = new LogCapture();
        var remote = new RemoteAgents(args => args[0] == "machine"
            ? Task.FromResult("""[{"id":"good","label":"Build","enabled":true},{"id":"bad","label":"Offline","enabled":true},{"id":"disabled","enabled":false}]""")
            : args[1] == "good" ? Task.FromResult(Snapshot) : throw new HerdrException("unavailable"));
        await remote.RefreshAsync();
        Assert.Equal("good", Assert.Single(remote.Read()).MachineId);
        Assert.Single(log.Warnings);
    }

    [Fact]
    public void EqualPaneIdsOnDifferentServersHaveIndependentRecency()
    {
        var tracker = new RecencyTracker();
        var local = new Agent { PaneId = "w1:p1", AgentStatus = "working" };
        var remote = local with { MachineId = "remote" };
        tracker.Rank([local, remote]);
        Assert.Null(tracker.Rank([local with { AgentStatus = "blocked" }, remote])[0].MachineId);
        Assert.Equal("remote", tracker.Rank([local with { AgentStatus = "blocked" }, remote with { AgentStatus = "done" }])[0].MachineId);
    }

    [Fact]
    public async Task CloseChecksFreshRemoteIdentityAndNeverUsesTheLocalSocket()
    {
        var calls = new List<string[]>();
        var remote = new RemoteAgents(args => { calls.Add(args); return Task.FromResult(Snapshot); });
        var client = new HerdrClient("/nonexistent-local-socket", remoteAgents: remote);
        var target = new CloseTarget("remote:machine-1:w1:p1", "replaced", "w1", "codex");
        Assert.False(await new CloseAgent(client).CloseIfStillPresentAsync(target));
        Assert.Single(calls);
        Assert.True(await new CloseAgent(client).CloseIfStillPresentAsync(target with { TerminalId = "term" }));
        Assert.Equal(new[] { "--machine", "machine-1", "pane", "close", "w1:p1" }, calls[2]);
    }
}
