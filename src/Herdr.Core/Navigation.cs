namespace Herdr;

public sealed record Shortcut(string Id, string Label, string Group);

public sealed class Navigation(HerdrClient client)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public static readonly Shortcut[] All =
    [
        new("zoom", "Zoom pane", "Main"), new("cycle", "Cycle panes", "Main"),
        new("cycle-tabs", "Cycle tabs", "Main"),
        new("split-right", "Split right", "Layout"), new("split-down", "Split down", "Layout"),
        new("pane-prev", "Previous pane", "Layout"), new("pane-next", "Next pane", "Layout"),
        new("close-pane", "Close pane", "Layout"),
        new("tab-new", "New tab", "Tabs"), new("tab-prev", "Previous tab", "Tabs"), new("tab-next", "Next tab", "Tabs"),
        new("workspace-new", "New workspace", "Workspaces"), new("workspace-prev", "Previous workspace", "Workspaces"), new("workspace-next", "Next workspace", "Workspaces"),
    ];

    public async Task ExecuteAsync(string id)
    {
        if (id == "close-pane") throw new InvalidOperationException("Close pane requires a long press and confirmation");
        await this.gate.WaitAsync();
        try
        {
            var s = await client.GetSnapshotAsync();
            if (id is not ("cycle" or "pane-next" or "pane-prev")) _ = client.ActivateTerminalAsync();
            if (id == "zoom")
            {
                await client.RequestAsync("pane.zoom", new { pane_id = s.FocusedPaneId, mode = "toggle" });
            }
            else if (id is "split-right" or "split-down")
                await client.RequestAsync("pane.split", new { target_pane_id = s.FocusedPaneId, direction = id == "split-right" ? "right" : "down", focus = true }, TimeSpan.FromSeconds(10));
            else if (id is "cycle" or "pane-next" or "pane-prev")
            {
                if (s.FocusedTabId is null || !s.Layouts.TryGetValue(s.FocusedTabId, out var layout)) return;
                var target = Neighbor(layout.Panes.Select(p => p.PaneId).ToList(), layout.FocusedPaneId, id == "pane-prev" ? -1 : 1);
                if (target is not null)
                {
                    await client.FocusPaneAsync(target);
                    Log.Info("Pane navigation: focused the next pane in the current tab");
                }
                else
                {
                    _ = client.ActivateTerminalAsync();
                    Log.Info("Pane navigation: current tab has no other pane to focus");
                }
            }
            else if (id == "tab-new" && s.FocusedWorkspaceId is not null)
                await client.RequestAsync("tab.create", new { workspace_id = s.FocusedWorkspaceId, focus = true }, TimeSpan.FromSeconds(10));
            else if (id is "tab-next" or "tab-prev" or "cycle-tabs")
            {
                var ids = s.Tabs.Where(t => t.WorkspaceId == s.FocusedWorkspaceId).OrderBy(t => t.Number).Select(t => t.TabId).ToList();
                var target = Neighbor(ids, s.FocusedTabId, id == "tab-prev" ? -1 : 1);
                if (target is not null) await client.RequestAsync("tab.focus", new { tab_id = target });
            }
            else if (id == "workspace-new")
                await client.RequestAsync("workspace.create", new { source_workspace_id = s.FocusedWorkspaceId, focus = true }, TimeSpan.FromSeconds(10));
            else if (id is "workspace-next" or "workspace-prev")
            {
                var ids = s.Workspaces.Values.OrderBy(w => w.Number).Select(w => w.WorkspaceId).ToList();
                var target = Neighbor(ids, s.FocusedWorkspaceId, id == "workspace-prev" ? -1 : 1);
                if (target is not null) await client.RequestAsync("workspace.focus", new { workspace_id = target });
            }
            else if (!All.Any(a => a.Id == id)) throw new ArgumentException("Unknown shortcut", nameof(id));
        }
        finally { this.gate.Release(); }
    }

    public static string? Neighbor(IReadOnlyList<string> items, string? current, int direction)
    {
        var index = current is null ? -1 : items.ToList().IndexOf(current);
        return index < 0 || items.Count < 2 ? null : items[(index + direction + items.Count) % items.Count];
    }
}
