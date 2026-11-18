using System.Text.Json;
using System.Text.RegularExpressions;

namespace Herdr;

internal sealed record GoToEndpoint(string? MachineId, string Label, JsonElement Snapshot);

// Experimental keyboard automation, not a client-focus acknowledgement. Be
// conservative: every enabled endpoint must be readable and the ID must match
// only the intended pane, including ordinary shell panes and ancestor labels.
internal static class GoToAgentFocus
{
    // This is the client's saved desired selection, not a live view ACK. Missing
    // selection metadata is unknown rather than an implicit Local selection.
    public static bool IsOnSelectedMachine(string key, JsonElement profiles)
    {
        if (profiles.ValueKind != JsonValueKind.Array) return false;
        string? targetMachine = null;
        if (HerdrClient.RemoteTarget(key, out var machine, out _)) targetMachine = machine;
        string? selectedMachine = null;
        foreach (var profile in profiles.EnumerateArray())
        {
            if (profile.ValueKind != JsonValueKind.Object
                || !profile.TryGetProperty("selected", out var selected)
                || selected.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
            if (!selected.GetBoolean()) continue;
            if (selectedMachine is not null
                || !profile.TryGetProperty("enabled", out var enabled) || enabled.ValueKind != JsonValueKind.True
                || !profile.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String
                || string.IsNullOrEmpty(id.GetString())) return false;
            selectedMachine = id.GetString();
        }
        return selectedMachine == targetMachine;
    }

    public static void ValidateQuery(string query)
    {
        if (!Regex.IsMatch(query, @"\Aw[0-9A-Za-z]+:p[0-9A-Za-z]+\z"))
            throw new HerdrException("Go to spike requires a public Herdr pane ID");
    }

    public static string Query(string key, IReadOnlyList<GoToEndpoint> endpoints)
    {
        string? machine = null;
        var pane = key;
        if (HerdrClient.RemoteTarget(key, out var remote, out var remotePane)) { machine = remote; pane = remotePane; }
        ValidateQuery(pane);
        var endpoint = endpoints.SingleOrDefault(e => e.MachineId == machine)
            ?? throw new HerdrException("Go to target machine is unavailable");
        if (!Items(endpoint.Snapshot, "agents").Any(a => Text(a, "pane_id") == pane)
            || !Items(endpoint.Snapshot, "panes").Any(p => Text(p, "pane_id") == pane))
            throw new HerdrException("Go to target agent is no longer present");

        var matches = new List<(string? Machine, string Pane)>();
        foreach (var origin in endpoints)
        {
            foreach (var candidate in Items(origin.Snapshot, "panes"))
            {
                var workspace = Items(origin.Snapshot, "workspaces")
                    .FirstOrDefault(w => Text(w, "workspace_id") == Text(candidate, "workspace_id"));
                var tab = Items(origin.Snapshot, "tabs")
                    .FirstOrDefault(t => Text(t, "tab_id") == Text(candidate, "tab_id"));
                var agent = Items(origin.Snapshot, "agents")
                    .FirstOrDefault(a => Text(a, "pane_id") == Text(candidate, "pane_id"));
                var fields = new[] { origin.Label, Text(workspace, "label"), Text(workspace, "branch"), Text(tab, "label"),
                    Text(candidate, "pane_id"), Text(candidate, "label"), Text(candidate, "cwd"), Text(candidate, "foreground_cwd"),
                    Text(agent, "name"), Text(agent, "title"), Text(agent, "terminal_title_stripped"),
                    Text(agent, "display_agent"), Text(agent, "agent") };
                if (fields.Any(f => f.Contains(pane, StringComparison.OrdinalIgnoreCase)))
                    matches.Add((origin.MachineId, Text(candidate, "pane_id")));
            }
        }
        if (matches.Count != 1 || matches[0] != (machine, pane))
            throw new HerdrException($"Go to spike cancelled: {pane} matches {matches.Count} panes; use native Go to manually");
        return pane;
    }

    internal static JsonElement Snapshot(JsonElement response)
    {
        if (response.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
            throw new HerdrException($"Go to snapshot failed: {error}");
        if (response.TryGetProperty("result", out var result)) response = result;
        return response.GetProperty("snapshot").Clone();
    }

    private static IEnumerable<JsonElement> Items(JsonElement value, string key) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var items) && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray() : [];

    private static string Text(JsonElement value, string key) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var text) && text.ValueKind == JsonValueKind.String
            ? text.GetString() ?? "" : "";
}
