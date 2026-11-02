namespace Herdr;

public static class FixedSlots
{
    public const int Count = 9;

    public static Agent? Recent(Snapshot snapshot, int slot) =>
        slot is >= 1 and <= Count && slot <= snapshot.Recent.Count ? snapshot.Recent[slot - 1] : null;

    public static Workspace? Space(Snapshot snapshot, int slot) =>
        slot is >= 1 and <= Count ? snapshot.Workspaces.GetValueOrDefault(slot) : null;

    public static string AgentLabel(Agent agent, int slot) =>
        FirstNonBlank(agent.Name, agent.DisplayAgent, agent.TerminalTitleStripped, agent.Kind) ?? $"Recent {slot}";

    public static string SpaceLabel(Workspace workspace, int slot) =>
        FirstNonBlank(workspace.Label) ?? $"Space {slot}";

    private static string? FirstNonBlank(params string?[] values) => values.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
}

// Serializing presses ensures a second Space press sees the focus left by the first.
public sealed class FixedSlotActions(HerdrClient client)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task FocusRecentAsync(string paneId)
    {
        await this.gate.WaitAsync();
        try { await client.FocusPaneAsync(paneId); }
        finally { this.gate.Release(); }
    }

    public async Task FocusSpaceAsync(int slot)
    {
        await this.gate.WaitAsync();
        try
        {
            var snapshot = await client.GetSnapshotAsync();
            var workspace = FixedSlots.Space(snapshot, slot);
            if (workspace is null) return;
            var paneId = HerdrClient.NextPaneInFocusedWorkspace(snapshot, workspace.WorkspaceId);
            if (paneId is not null) await client.FocusPaneAsync(paneId);
            else await client.FocusWorkspaceAsync(workspace.WorkspaceId);
        }
        finally { this.gate.Release(); }
    }
}
