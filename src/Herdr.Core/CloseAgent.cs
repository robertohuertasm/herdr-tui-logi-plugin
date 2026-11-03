namespace Herdr;

public sealed record CloseTarget(string PaneId, string TerminalId, string WorkspaceId, string? Kind);

public sealed class CloseAgent(HerdrClient client)
{
    public async Task<bool> CloseIfStillPresentAsync(CloseTarget target)
    {
        if (string.IsNullOrWhiteSpace(target.TerminalId)) return false;
        var snapshot = await client.SnapshotForAgentAsync(target.PaneId);
        var current = snapshot.Agents.FirstOrDefault(a => a.Key == target.PaneId);
        if (current is null || current.TerminalId != target.TerminalId || current.WorkspaceId != target.WorkspaceId || current.Kind != target.Kind) return false;
        await client.CloseAgentPaneAsync(target.PaneId);
        return true;
    }
}
