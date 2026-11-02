using System.Text.Json;

namespace Herdr.Tests;

public class CloseAgentTests
{
    [Theory]
    [InlineData("same", true)]
    [InlineData("replacement", false)]
    public async Task CloseChecksCurrentTerminalBeforeSendingClose(string terminal, bool expected)
    {
        using var server = new FakeHerdr();
        var methods = new List<string>();
        server.Behaviour = (socket, line) =>
        {
            using var request = JsonDocument.Parse(line);
            var method = request.RootElement.GetProperty("method").GetString()!;
            methods.Add(method);
            if (method == "session.snapshot")
                return FakeHerdr.SendAndClose(socket, JsonSerializer.Serialize(new { result = new { snapshot = new { version = "0.9.5", protocol = 22, workspaces = Array.Empty<object>(), agents = new[] { new { pane_id = "p1", workspace_id = "w1", terminal_id = terminal, agent = "codex" } }, layouts = Array.Empty<object>() } } }) + "\n");
            return FakeHerdr.SendAndClose(socket, """{"result":{}}""" + "\n");
        };
        var close = new CloseAgent(new HerdrClient(server.SocketPath));
        Assert.Equal(expected, await close.CloseIfStillPresentAsync(new("p1", "same", "w1", "codex")));
        Assert.Equal(expected ? new[] { "session.snapshot", "pane.close" } : new[] { "session.snapshot" }, methods);
    }
}
