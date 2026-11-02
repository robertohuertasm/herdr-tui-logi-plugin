using System.Text.Json;

namespace Herdr.Tests;

public class AgentTests
{
    [Fact]
    public async Task SnapshotRanksAgentsByStateChangeAndKeepsTheirLocation()
    {
        using var server = new FakeHerdr();
        server.Behaviour = (socket, _) => FakeHerdr.SendAndClose(socket,
            """{"result":{"snapshot":{"version":"0.9.5","protocol":22,"workspaces":[{"workspace_id":"w1","number":1}],"agents":[{"pane_id":"p1","workspace_id":"w1","tab_id":"t1","terminal_id":"a","agent":"codex","agent_status":"working","state_change_seq":3},{"pane_id":"p2","workspace_id":"w1","tab_id":"t2","terminal_id":"b","agent":"claude","agent_status":"blocked","state_change_seq":8}],"layouts":[]}}}""" + "\n");
        var snapshot = await new HerdrClient(server.SocketPath).GetSnapshotAsync();
        Assert.Equal(new[] { "p2", "p1" }, snapshot.Recent.Select(a => a.PaneId));
        Assert.Equal("blocked", snapshot.Recent[0].AgentStatus);
        Assert.Equal("t2", snapshot.Recent[0].TabId);
        Assert.Equal("b", snapshot.Recent[0].TerminalId);
    }

    [Fact]
    public async Task FocusSendsTheSelectedPaneId()
    {
        using var server = new FakeHerdr();
        server.Behaviour = (socket, line) =>
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            Assert.Equal("pane.focus", root.GetProperty("method").GetString());
            Assert.Equal("w1:p2", root.GetProperty("params").GetProperty("pane_id").GetString());
            return FakeHerdr.SendAndClose(socket, """{"result":{}}""" + "\n");
        };
        var client = new HerdrClient(server.SocketPath, new Terminal());
        await client.RequestAsync("pane.focus", new { pane_id = "w1:p2" });
    }
}
