using System.Diagnostics;
using System.Text.Json;

namespace Herdr;

// Each remote server has its own pane IDs and state-change sequence counter.
// Keep the original IDs for API requests and use machine-qualified keys in UI actions.
public sealed class RemoteAgents
{
    private readonly Func<string[], Task<string>> run;
    private readonly object gate = new();
    private IReadOnlyList<Agent> agents = [];
    private Task? refresh;
    private DateTime nextRefresh;
    private string? lastError;

    public RemoteAgents() : this(RunAsync) { }
    internal RemoteAgents(Func<string[], Task<string>> run) => this.run = run;

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
                        return await SnapshotAsync(id, label);
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

    public async Task<IReadOnlyList<Agent>> SnapshotAsync(string machineId, string label)
    {
        using var document = JsonDocument.Parse(await run(["--machine", machineId, "api", "snapshot"]));
        var root = document.RootElement;
        if (root.TryGetProperty("result", out var result)) root = result;
        var snapshot = root.GetProperty("snapshot").Deserialize<RawSnapshot>(HerdrClient.Json)
            ?? throw new HerdrException("Remote snapshot missing");
        return snapshot.Agents.Select(a => a with { MachineId = machineId, MachineLabel = label }).ToList();
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
