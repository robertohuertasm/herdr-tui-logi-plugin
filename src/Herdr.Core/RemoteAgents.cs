using System.Diagnostics;
using System.Text.Json;

namespace Herdr;

// Each remote server has its own pane IDs and state-change sequence counter.
// Keep the original IDs for API requests and use machine-qualified keys in UI actions.
public sealed class RemoteAgents
{
    private readonly Func<string[], Task<string>> run;
    private readonly Func<string[], Task<string>> runSsh;
    private readonly object gate = new();
    private IReadOnlyList<Agent> agents = [];
    private Task? refresh;
    private DateTime nextRefresh;
    private string? lastError;

    public RemoteAgents() : this(RunAsync) { }
    internal RemoteAgents(Func<string[], Task<string>> run, Func<string[], Task<string>>? runSsh = null)
    {
        this.run = run;
        this.runSsh = runSsh ?? (args => RunProcessAsync("/usr/bin/ssh", args));
    }

    internal async Task<JsonElement> MachineProfilesAsync()
    {
        using var profiles = JsonDocument.Parse(await run(["machine", "list", "--json"]));
        return profiles.RootElement.Clone();
    }

    internal async Task<IReadOnlyList<GoToEndpoint>> GoToEndpointsAsync(JsonElement? machineProfiles = null)
    {
        var profiles = machineProfiles ?? await MachineProfilesAsync();
        using var limit = new SemaphoreSlim(4);
        return await Task.WhenAll(profiles.EnumerateArray()
            .Where(p => p.GetProperty("enabled").GetBoolean()).Select(async profile =>
            {
                await limit.WaitAsync();
                try
                {
                    var id = profile.GetProperty("id").GetString()!;
                    var label = profile.GetProperty("label").GetString() ?? id;
                    return await ReadEndpointAsync(id, label, profile);
                }
                finally { limit.Release(); }
            }));
    }

    public IReadOnlyList<Agent> Read()
    {
        lock (gate)
        {
            if ((refresh is null || refresh.IsCompleted) && DateTime.UtcNow >= nextRefresh)
            {
                nextRefresh = DateTime.UtcNow.AddSeconds(10);
                refresh = Task.Run(RefreshAsync);
            }
            return agents;
        }
    }

    internal async Task RefreshAsync()
    {
        lock (gate) nextRefresh = DateTime.UtcNow.AddSeconds(10);
        var errors = new List<string>();
        var found = new List<Agent>();
        try
        {
            using var profiles = JsonDocument.Parse(await run(["machine", "list", "--json"]));
            // Bound parallel SSH processes so a large catalog cannot flood the host.
            using var limit = new SemaphoreSlim(4);
            var results = await Task.WhenAll(profiles.RootElement.EnumerateArray()
                .Where(p => p.GetProperty("enabled").GetBoolean())
                .Select(async p =>
                {
                    await limit.WaitAsync();
                    try
                    {
                        var id = p.GetProperty("id").GetString()!;
                        var label = p.GetProperty("label").GetString() ?? id;
                        return Agents(await ReadEndpointAsync(id, label, p));
                    }
                    catch (Exception e)
                    {
                        lock (errors) errors.Add(e.Message);
                        return (IReadOnlyList<Agent>)[];
                    }
                    finally { limit.Release(); }
                }));
            found.AddRange(results.SelectMany(a => a));
        }
        catch (Exception e) { errors.Add(e.Message); }
        lock (gate) agents = found;
        var error = errors.Count == 0 ? null : string.Join("; ", errors.Order());
        if (error != lastError)
        {
            if (error is not null) Log.Warning($"Herdr remote agents: {error}");
            lastError = error;
        }
    }

    public async Task<IReadOnlyList<Agent>> SnapshotAsync(string machineId, string label) =>
        Agents(await ReadEndpointAsync(machineId, label));

    private static IReadOnlyList<Agent> Agents(GoToEndpoint endpoint)
    {
        var snapshot = endpoint.Snapshot.Deserialize<RawSnapshot>(HerdrClient.Json)
            ?? throw new HerdrException("Remote snapshot missing");
        return snapshot.Agents.Select(a => a with { MachineId = endpoint.MachineId, MachineLabel = endpoint.Label }).ToList();
    }

    internal async Task<bool> IsAgentFocusedAsync(string machineId, string paneId)
    {
        var endpoint = await ReadEndpointAsync(machineId, machineId);
        return endpoint.Snapshot.TryGetProperty("focused_pane_id", out var focused)
            && focused.GetString() == paneId && Agents(endpoint).Any(a => a.PaneId == paneId);
    }

    private async Task<GoToEndpoint> ReadEndpointAsync(string id, string label, JsonElement? savedProfile = null)
    {
        string json;
        try { json = await run(["--machine", id, "api", "snapshot"]); }
        catch (HerdrException e) when (e.Message.Contains("does not support machine API forwarding", StringComparison.Ordinal))
        {
            // Shared by discovery, validation and the Go to catalog. This only
            // reads the older server; no install, restart or mutating SSH call.
            var profile = savedProfile ?? (await MachineProfilesAsync()).EnumerateArray()
                .First(p => p.GetProperty("id").GetString() == id && p.GetProperty("enabled").GetBoolean());
            var target = profile.GetProperty("target").GetString()!;
            var session = profile.GetProperty("session").GetString() ?? "default";
            var quotedSession = "'" + session.Replace("'", "'\\''") + "'";
            json = await runSsh(["-o", "BatchMode=yes", "-o", "ConnectTimeout=5", "--", target,
                $"herdr --session {quotedSession} api snapshot"]);
        }
        using var document = JsonDocument.Parse(json);
        return new GoToEndpoint(id, label, GoToAgentFocus.Snapshot(document.RootElement));
    }

    public Task<string> PaneAsync(string machineId, string paneId, string action) =>
        run(["--machine", machineId, action == "focus" ? "agent" : "pane", action, paneId]);

    private static async Task<string> RunAsync(string[] arguments)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var path = Environment.GetEnvironmentVariable("HERDR_BIN_PATH");
        if (string.IsNullOrWhiteSpace(path))
            path = new[] { Path.Combine(home, ".local/bin/herdr"), "/opt/homebrew/bin/herdr", "/usr/local/bin/herdr" }
                .FirstOrDefault(File.Exists) ?? "herdr";
        return await RunProcessAsync(path, arguments);
    }

    private static async Task<string> RunProcessAsync(string path, string[] arguments)
    {
        var start = new ProcessStartInfo(path) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        // GUI hosts often lack the user's shell PATH; Herdr needs ssh for forwarding.
        start.Environment["PATH"] = $"/usr/bin:/bin:/usr/sbin:/sbin:/opt/homebrew/bin:/usr/local/bin:{Environment.GetEnvironmentVariable("PATH")}";
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new HerdrException("Could not start Herdr CLI");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new HerdrException("Herdr machine request timed out");
        }
        var text = await output;
        var diagnostic = await error;
        if (process.ExitCode != 0) throw new HerdrException(diagnostic.Trim());
        return text;
    }
}
