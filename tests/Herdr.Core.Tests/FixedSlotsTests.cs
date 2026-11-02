using System.Text.Json;

namespace Herdr.Tests;

public class FixedSlotsTests
{
    [Fact]
    public void RecentAndSpaceSlotsHandleMissingEntries()
    {
        var first = new Agent { PaneId = "p1", AgentStatus = "blocked", Kind = "codex" };
        var second = new Agent { PaneId = "p2", AgentStatus = "working", Kind = "claude" };
        var space = new Workspace { WorkspaceId = "w2", Number = 2, Label = "api" };
        var snapshot = new Snapshot
        {
            Recent = [first, second],
            Workspaces = new Dictionary<int, Workspace> { [2] = space },
        };
        Assert.Same(first, FixedSlots.Recent(snapshot, 1));
        Assert.Same(second, FixedSlots.Recent(snapshot, 2));
        Assert.Null(FixedSlots.Recent(snapshot, 3));
        Assert.Null(FixedSlots.Recent(snapshot, 10));
        Assert.Same(space, FixedSlots.Space(snapshot, 2));
        Assert.Null(FixedSlots.Space(snapshot, 1));
        Assert.Equal("codex", FixedSlots.AgentLabel(first, 1));
        Assert.Equal("api", FixedSlots.SpaceLabel(space, 2));
    }

    [Fact]
    public async Task SpacePressFocusesWorkspaceThenCyclesOnlyItsActiveTab()
    {
        using var server = new FakeHerdr();
        var focused = false;
        var commands = new List<(string Method, string Target)>();
        server.Behaviour = (socket, line) =>
        {
            using var request = JsonDocument.Parse(line);
            var root = request.RootElement;
            var method = root.GetProperty("method").GetString()!;
            if (method == "session.snapshot")
            {
                var reply = new { result = new { snapshot = new
                {
                    version = "0.9.5", protocol = 22, focused_workspace_id = focused ? "w2" : "w1",
                    workspaces = new[] { new { workspace_id = "w2", number = 2, active_tab_id = "t2" } },
                    agents = Array.Empty<object>(),
                    layouts = new[] { new { tab_id = "t2", focused_pane_id = "p2", panes = new[] { new { pane_id = "p2" }, new { pane_id = "p3" } } } },
                } } };
                return FakeHerdr.SendAndClose(socket, JsonSerializer.Serialize(reply) + "\n");
            }
            var key = method == "workspace.focus" ? "workspace_id" : "pane_id";
            commands.Add((method, root.GetProperty("params").GetProperty(key).GetString()!));
            if (method == "workspace.focus") focused = true;
            return FakeHerdr.SendAndClose(socket, """{"result":{}}""" + "\n");
        };
        var actions = new FixedSlotActions(new HerdrClient(server.SocketPath, new NoTerminal()));
        await actions.FocusSpaceAsync(2);
        await actions.FocusSpaceAsync(2);
        Assert.Equal(new[] { ("workspace.focus", "w2"), ("pane.focus", "p3") }, commands);
    }

    private sealed class NoTerminal : Terminal
    {
        protected override Task<string> RunAsync(string command, params string[] args) => Task.FromResult("");
    }
}
