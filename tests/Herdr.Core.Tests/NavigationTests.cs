using System.Text.Json;

namespace Herdr.Tests;

public class NavigationTests
{
    [Theory]
    [InlineData("p1", 1, "p2")]
    [InlineData("p1", -1, "p2")]
    [InlineData("missing", 1, null)]
    public void NeighborWrapsOnlyWhenCurrentExists(string current, int direction, string? expected) =>
        Assert.Equal(expected, Navigation.Neighbor(["p1", "p2"], current, direction));

    [Fact]
    public async Task CycleUsesOnlyFocusedTabLayout()
    {
        using var server = new FakeHerdr();
        var focused = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.Behaviour = (socket, line) =>
        {
            using var request = JsonDocument.Parse(line);
            if (request.RootElement.GetProperty("method").GetString() == "session.snapshot")
                return FakeHerdr.SendAndClose(socket, """{"result":{"snapshot":{"version":"0.9.5","protocol":22,"focused_workspace_id":"w1","focused_tab_id":"t1","workspaces":[{"workspace_id":"w1","number":1}],"tabs":[{"tab_id":"t1","workspace_id":"w1","number":1},{"tab_id":"t2","workspace_id":"w1","number":2}],"agents":[],"layouts":[{"tab_id":"t1","focused_pane_id":"p1","panes":[{"pane_id":"p1"},{"pane_id":"p2"}]},{"tab_id":"t2","focused_pane_id":"p3","panes":[{"pane_id":"p3"}]}]}}}""" + "\n");
            focused.TrySetResult(request.RootElement.GetProperty("params").GetProperty("pane_id").GetString()!);
            return FakeHerdr.SendAndClose(socket, """{"result":{}}""" + "\n");
        };
        await new Navigation(new HerdrClient(server.SocketPath, new NoTerminal())).ExecuteAsync("cycle");
        Assert.Equal("p2", await focused.Task);
    }

    [Fact]
    public async Task CycleTabsWrapsWithinTheFocusedWorkspace()
    {
        using var server = new FakeHerdr();
        var selected = new TaskCompletionSource<(string Method, string Tab)>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.Behaviour = (socket, line) =>
        {
            using var request = JsonDocument.Parse(line);
            var root = request.RootElement;
            if (root.GetProperty("method").GetString() == "session.snapshot")
                return FakeHerdr.SendAndClose(socket, """{"result":{"snapshot":{"version":"0.9.5","protocol":22,"focused_workspace_id":"w1","focused_tab_id":"t2","workspaces":[{"workspace_id":"w1","number":1},{"workspace_id":"w2","number":2}],"tabs":[{"tab_id":"other","workspace_id":"w2","number":1},{"tab_id":"t2","workspace_id":"w1","number":2},{"tab_id":"t1","workspace_id":"w1","number":1}],"agents":[],"layouts":[]}}}""" + "\n");
            selected.TrySetResult((root.GetProperty("method").GetString()!, root.GetProperty("params").GetProperty("tab_id").GetString()!));
            return FakeHerdr.SendAndClose(socket, """{"result":{}}""" + "\n");
        };
        await new Navigation(new HerdrClient(server.SocketPath, new NoTerminal())).ExecuteAsync("cycle-tabs");
        Assert.Equal(("tab.focus", "t1"), await selected.Task);
    }

    [Fact]
    public void AgentLabelUsesSessionTitleWhenNoExplicitNameIsPresent()
    {
        var agent = new Agent { Kind = "codex", TerminalTitleStripped = "Fix login" };
        Assert.Equal("Fix login", FixedSlots.AgentLabel(agent, 1));
    }

    private sealed class NoTerminal : Terminal
    {
        protected override Task<string> RunAsync(string command, params string[] args) => Task.FromResult("");
    }
}
