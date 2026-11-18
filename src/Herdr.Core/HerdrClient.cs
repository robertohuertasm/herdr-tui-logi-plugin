using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Herdr;

public sealed class HerdrException(string message) : Exception(message);

public sealed class HerdrClient
{
    public static readonly string DefaultSocketPath =
        Environment.GetEnvironmentVariable("HERDR_SOCKET_PATH")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config/herdr/herdr.sock");

    // The herdr socket protocol this plugin was written and tested against (herdr 0.8.2).
    public const int TestedProtocol = 22;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);
    // tab.create spawns a shell and waits for it to report ready, which takes
    // longer than the other requests.
    private static readonly TimeSpan TabCreateTimeout = TimeSpan.FromSeconds(10);

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly RecencyTracker recency = new();
    private readonly RecencyTracker combinedRecency = new();
    private readonly RemoteAgents remote;
    private readonly Terminal terminal;
    private readonly SemaphoreSlim agentFocusGate = new(1, 1);
    private DateTime goToSettlingUntil;
    private string? lastFocusedAgentKey;
    private string? pendingAgentKey;
    private int nextId;
    private bool warnedProtocol;

    public HerdrClient(string? socketPath = null, Terminal? terminal = null, RemoteAgents? remoteAgents = null)
    {
        SocketPath = socketPath ?? DefaultSocketPath;
        this.terminal = terminal ?? new Terminal();
        remote = remoteAgents ?? new RemoteAgents();
    }

    public string SocketPath { get; }
    public string? PendingAgentKey => Volatile.Read(ref pendingAgentKey);
    public event Action? AgentFocusChanged;

    // One request per connection: herdr answers with a single JSON line.
    public async Task<JsonElement> RequestAsync(string method, object? parameters = null, TimeSpan? timeout = null)
    {
        var id = $"logi_{Interlocked.Increment(ref nextId)}";
        using var cts = new CancellationTokenSource(timeout ?? RequestTimeout);
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        string? line;
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(SocketPath), cts.Token);
            await using var stream = new NetworkStream(socket, ownsSocket: false);
            var request = JsonSerializer.Serialize(new { id, method, @params = parameters ?? new { } }) + "\n";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(request), cts.Token);
            // The reader decodes as a stream, so a multi-byte character split
            // across reads survives.
            using var reader = new StreamReader(stream, Encoding.UTF8);
            line = await reader.ReadLineAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new HerdrException($"herdr {method}: timed out");
        }
        if (line is null) throw new HerdrException($"herdr {method}: connection closed before a response arrived");

        JsonElement message;
        try
        {
            using var document = JsonDocument.Parse(line);
            message = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new HerdrException($"herdr {method}: malformed response");
        }
        if (message.ValueKind != JsonValueKind.Object) throw new HerdrException($"herdr {method}: malformed response");
        if (message.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
        {
            throw new HerdrException($"herdr {method}: {error.GetRawText()}");
        }
        return message.TryGetProperty("result", out var result) ? result : default;
    }

    public async Task<Snapshot> GetSnapshotAsync()
    {
        var result = await RequestAsync("session.snapshot");
        var snapshot = result.ValueKind == JsonValueKind.Object && result.TryGetProperty("snapshot", out var s)
            ? s.Deserialize<RawSnapshot>(Json)
            : null;
        if (snapshot is null) throw new HerdrException("herdr session.snapshot: response has no snapshot");
        if (snapshot.Protocol != TestedProtocol && !warnedProtocol)
        {
            warnedProtocol = true;
            Log.Warning(
                $"herdr {snapshot.Version} speaks socket protocol {snapshot.Protocol}; this plugin was tested against protocol {TestedProtocol}");
        }
        var workspaces = new Dictionary<int, Workspace>();
        foreach (var w in snapshot.Workspaces) workspaces[w.Number] = w;
        var numberOf = new Dictionary<string, int>();
        foreach (var w in workspaces.Values) numberOf[w.WorkspaceId] = w.Number;
        var agents = snapshot.Agents
            .OrderBy(a => numberOf.GetValueOrDefault(a.WorkspaceId))
            .ThenBy(a => a.PaneId, Comparer<string>.Create(ComparePaneIds))
            .ToList();
        var layouts = new Dictionary<string, Layout>();
        foreach (var l in snapshot.Layouts) layouts[l.TabId] = l;
        return new Snapshot
        {
            FocusedWorkspaceId = snapshot.FocusedWorkspaceId,
            FocusedTabId = snapshot.FocusedTabId,
            FocusedPaneId = snapshot.FocusedPaneId,
            Tabs = snapshot.Tabs,
            Panes = snapshot.Panes,
            Workspaces = workspaces,
            Agents = agents,
            Recent = agents.Any(a => a.StateChangeSeq.HasValue)
                ? agents.OrderByDescending(a => a.StateChangeSeq ?? 0).ToList()
                : recency.Rank(agents),
            Layouts = layouts,
        };
    }

    public async Task<Snapshot> GetAgentSnapshotAsync(bool refreshRemotes = false)
    {
        if (refreshRemotes) await remote.RefreshAsync();
        var remotes = remote.Read();
        Snapshot local;
        try { local = await GetSnapshotAsync(); }
        catch when (remotes.Count > 0) { local = Snapshot.Empty; }
        var agents = local.Agents.Concat(remotes).ToList();
        // Sequence counters from different servers cannot be compared. Observe
        // transitions locally when several servers contribute to the list.
        var recent = remotes.Count > 0 ? combinedRecency.Rank(agents) : local.Recent;
        return local with { Agents = agents, Recent = recent };
    }

    internal async Task<Snapshot> SnapshotForAgentAsync(string key)
    {
        if (!RemoteTarget(key, out var machine, out _)) return await GetSnapshotAsync();
        return new Snapshot { Agents = await remote.SnapshotAsync(machine, machine) };
    }

    internal static bool RemoteTarget(string key, out string machine, out string pane)
    {
        machine = pane = "";
        if (!key.StartsWith("remote:", StringComparison.Ordinal)) return false;
        var separator = key.IndexOf(':', 7);
        if (separator <= 7 || separator == key.Length - 1) throw new HerdrException("Invalid remote agent key");
        machine = key[7..separator];
        pane = key[(separator + 1)..];
        return true;
    }

    internal Task CloseAgentPaneAsync(string key) => RemoteTarget(key, out var machine, out var pane)
        ? remote.PaneAsync(machine, pane, "close")
        : RequestAsync("pane.close", new { pane_id = key });

    // Pane ids look like `w1:p10`; compare their numeric parts so p10 sorts after p2.
    public static int ComparePaneIds(string a, string b)
    {
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
            {
                var startA = i;
                var startB = j;
                while (i < a.Length && char.IsAsciiDigit(a[i])) i++;
                while (j < b.Length && char.IsAsciiDigit(b[j])) j++;
                var numberA = a[startA..i].TrimStart('0');
                var numberB = b[startB..j].TrimStart('0');
                var byNumber = numberA.Length != numberB.Length
                    ? numberA.Length.CompareTo(numberB.Length)
                    : string.CompareOrdinal(numberA, numberB);
                if (byNumber != 0) return byNumber;
            }
            else
            {
                if (a[i] != b[j]) return a[i].CompareTo(b[j]);
                i++;
                j++;
            }
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }

    public async Task FocusWorkspaceAsync(string workspaceId)
    {
        _ = terminal.ActivateAsync();
        await RequestAsync("workspace.focus", new { workspace_id = workspaceId });
    }

    // Exposed so a tile can raise the terminal itself before it does other
    // async work (NewTabTile), instead of relying on a focus call to do it.
    public Task ActivateTerminalAsync() => terminal.ActivateAsync();

    public Task SendShortcutAsync(string sequence) => terminal.SendShortcutAsync(sequence);

    public Task<string> GetGoToAgentQueryAsync(string agentKey) => GetGoToAgentQueryAsync(agentKey, null);

    private async Task<string> GetGoToAgentQueryAsync(string agentKey, JsonElement? machineProfiles)
    {
        var local = RequestAsync("session.snapshot");
        var remotes = remote.GoToEndpointsAsync(machineProfiles);
        await Task.WhenAll(local, remotes);
        var endpoints = new[] { new GoToEndpoint(null, "Local", GoToAgentFocus.Snapshot(await local)) }
            .Concat(await remotes).ToList();
        return GoToAgentFocus.Query(agentKey, endpoints);
    }

    public Task FocusAgentAsync(string agentKey) => FocusAgentAsync(agentKey, Config.Load());

    internal async Task FocusAgentAsync(string agentKey, Config config)
    {
        if (!await agentFocusGate.WaitAsync(0))
        {
            Log.Info("Agent navigation in progress; additional press ignored");
            return;
        }
        try
        {
            if (config.ExperimentalGoToAgentFocus && DateTime.UtcNow < goToSettlingUntil)
            {
                Log.Info("Agent navigation settling; additional press ignored");
                return;
            }
            SetPendingAgent(agentKey);
            if (!config.ExperimentalGoToAgentFocus) { await FocusPaneAsync(agentKey); return; }
            JsonElement? profiles = null;
            try { profiles = await remote.MachineProfilesAsync(); }
            catch (Exception e) { Log.Warning($"Could not read selected machine; using Go to: {e.Message}"); }
            if (profiles.HasValue && GoToAgentFocus.IsOnSelectedMachine(agentKey, profiles.Value))
            {
                try
                {
                    // Local checks are cheap. For remote navigation, only pay
                    // for a fresh snapshot when repeating the previous target;
                    // changing between remote agents keeps its one-call route.
                    if ((!RemoteTarget(agentKey, out _, out _) || lastFocusedAgentKey == agentKey)
                        && await IsAgentAlreadyFocusedAsync(agentKey))
                    {
                        lastFocusedAgentKey = agentKey;
                        Log.Info("Requested agent is already focused; navigation skipped");
                        return;
                    }
                    await FocusPaneAsync(agentKey);
                    lastFocusedAgentKey = agentKey;
                    Log.Info($"Hybrid agent focus: direct API on selected machine for {agentKey}");
                    return;
                }
                catch (Exception e) { Log.Warning($"Direct agent focus failed; using Go to: {e.Message}"); }
            }
            // Match the standalone Go to button's responsiveness. Catalog
            // reads overlap opening AND filling the picker. Only Enter waits
            // for uniqueness validation; typing a filter selects nothing.
            var query = agentKey;
            if (RemoteTarget(agentKey, out _, out var targetPane)) query = targetPane;
            GoToAgentFocus.ValidateQuery(query);
            var preparing = OpenAndTypeGoToAsync(query, config);
            var planning = GetGoToAgentQueryAsync(agentKey, profiles);
            await Task.WhenAll(preparing, planning);
            if (await planning != query) throw new HerdrException("Go to target changed during validation");
            await terminal.ConfirmGoToAgentAsync(await preparing, config.GoToInputDelayMs);
            lastFocusedAgentKey = agentKey;
            // Selection is saved before activation finishes. Avoid the fast
            // path immediately after our own keyboard-driven machine switch.
            goToSettlingUntil = DateTime.UtcNow.AddSeconds(1);
            Log.Info($"Hybrid agent focus: Go to submitted for {agentKey}; visible focus is not acknowledged by the API");
        }
        finally { SetPendingAgent(null); agentFocusGate.Release(); }
    }

    private async Task<string> OpenAndTypeGoToAsync(string query, Config config)
    {
        var application = await terminal.OpenGoToAsync(config.GoToShortcut);
        await terminal.TypeGoToAgentQueryAsync(application, query, config.GoToInputDelayMs);
        return application;
    }

    private async Task<bool> IsAgentAlreadyFocusedAsync(string agentKey)
    {
        try
        {
            if (RemoteTarget(agentKey, out var machine, out var pane)) return await remote.IsAgentFocusedAsync(machine, pane);
            var snapshot = await GetSnapshotAsync();
            return snapshot.FocusedPaneId == agentKey && snapshot.Agents.Any(a => a.PaneId == agentKey);
        }
        catch (Exception e)
        {
            Log.Info($"Could not verify current agent; keeping direct focus: {e.Message}");
            return false;
        }
    }

    private void SetPendingAgent(string? key)
    {
        if (Volatile.Read(ref pendingAgentKey) == key) return;
        Volatile.Write(ref pendingAgentKey, key);
        try { AgentFocusChanged?.Invoke(); }
        catch (Exception e) { Log.Warning($"Could not update pending agent display: {e.Message}"); }
    }

    public Task CreateTabAsync(string workspaceId) =>
        RequestAsync("tab.create", new { workspace_id = workspaceId, focus = true }, TabCreateTimeout);

    public async Task FocusPaneAsync(string paneId)
    {
        _ = terminal.ActivateAsync();
        if (RemoteTarget(paneId, out var machine, out var pane))
            await remote.PaneAsync(machine, pane, "focus");
        else await RequestAsync("pane.focus", new { pane_id = paneId });
    }

    public static string? NextPaneInFocusedWorkspace(Snapshot snapshot, string workspaceId)
    {
        if (snapshot.FocusedWorkspaceId != workspaceId) return null;
        var workspace = snapshot.Workspaces.Values.FirstOrDefault(w => w.WorkspaceId == workspaceId);
        if (workspace is null || !snapshot.Layouts.TryGetValue(workspace.ActiveTabId, out var layout)) return null;
        if (layout.Panes.Count < 2) return null;
        var index = layout.Panes.ToList().FindIndex(p => p.PaneId == layout.FocusedPaneId);
        return layout.Panes[(index + 1) % layout.Panes.Count].PaneId;
    }
}
