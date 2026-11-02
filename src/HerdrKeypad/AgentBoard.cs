namespace Loupedeck.HerdrKeypad;

using Herdr;

public sealed class AgentBoard(HerdrClient client)
{
    private Snapshot snapshot = Snapshot.Empty;
    private bool online;
    public Snapshot Snapshot => Volatile.Read(ref this.snapshot);
    public bool Online => Volatile.Read(ref this.online);
    public event Action? Changed;

    public async Task RunAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                var next = await client.GetSnapshotAsync();
                Volatile.Write(ref this.snapshot, next);
                Volatile.Write(ref this.online, true);
            }
            catch (Exception e)
            {
                Herdr.Log.Warning($"Herdr unavailable: {e.Message}");
                Volatile.Write(ref this.snapshot, Snapshot.Empty);
                Volatile.Write(ref this.online, false);
            }
            this.Changed?.Invoke();
            try { await Task.Delay(TimeSpan.FromSeconds(1), stop); }
            catch (OperationCanceledException) { break; }
        }
    }
}
