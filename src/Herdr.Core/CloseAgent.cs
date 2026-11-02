namespace Herdr;

public sealed record CloseTarget(string PaneId, string TerminalId, string WorkspaceId, string? Kind);

public sealed class CloseAgent(HerdrClient client)
{
    public async Task<bool> CloseIfStillPresentAsync(CloseTarget target)
    {
        if (string.IsNullOrWhiteSpace(target.TerminalId)) return false;
        var snapshot = await client.GetSnapshotAsync();
        var current = snapshot.Agents.FirstOrDefault(a => a.PaneId == target.PaneId);
        if (current is null || current.TerminalId != target.TerminalId || current.WorkspaceId != target.WorkspaceId || current.Kind != target.Kind) return false;
        await client.RequestAsync("pane.close", new { pane_id = target.PaneId });
        return true;
    }
}
