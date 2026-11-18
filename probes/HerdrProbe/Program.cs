using Herdr;

if (args is ["--agent-catalog"])
{
    var catalog = await new HerdrClient().GetAgentSnapshotAsync(refreshRemotes: true);
    Console.WriteLine($"Combined agents: {catalog.Agents.Count}");
    foreach (var group in catalog.Agents.GroupBy(a => a.MachineLabel ?? "Local"))
        Console.WriteLine($"{group.Key}: {group.Count()}");
    Console.WriteLine("Read-only discovery; no keyboard input or focus request sent.");
    return;
}

if (args is ["--go-to-plan", var key])
{
    Console.WriteLine($"Unique Go to query: {await new HerdrClient().GetGoToAgentQueryAsync(key)}");
    Console.WriteLine("Dry run only; no keyboard input or focus request sent.");
    return;
}

var snapshot = await new HerdrClient().GetSnapshotAsync();
Console.WriteLine($"Agents: {snapshot.Agents.Count}; tabs: {snapshot.Tabs.Count}; workspaces: {snapshot.Workspaces.Count}");
Console.WriteLine("States: " + string.Join(", ", snapshot.Agents.GroupBy(a => a.AgentStatus).Select(g => $"{g.Key}={g.Count()}")));
Console.WriteLine($"Agents with sequence: {snapshot.Agents.Count(a => a.StateChangeSeq.HasValue)}");
