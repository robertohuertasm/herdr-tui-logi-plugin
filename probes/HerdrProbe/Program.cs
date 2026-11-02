using Herdr;

var snapshot = await new HerdrClient().GetSnapshotAsync();
Console.WriteLine($"Agents: {snapshot.Agents.Count}; tabs: {snapshot.Tabs.Count}; workspaces: {snapshot.Workspaces.Count}");
Console.WriteLine("States: " + string.Join(", ", snapshot.Agents.GroupBy(a => a.AgentStatus).Select(g => $"{g.Key}={g.Count()}")));
Console.WriteLine($"Agents with sequence: {snapshot.Agents.Count(a => a.StateChangeSeq.HasValue)}");
