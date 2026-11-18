namespace Loupedeck.HerdrKeypad;

using Herdr;

public sealed class HerdrKeypadPlugin : Plugin
{
    private readonly CancellationTokenSource stop = new();
    private readonly List<FixedSlotCommand> fixedSlots = [];
    public HerdrClient Client { get; } = new();
    public AgentBoard Board { get; }
    public Navigation Navigation { get; }
    public FixedSlotActions FixedSlotActions { get; }
    public override bool UsesApplicationApiOnly => true;
    public override bool HasNoApplication => true;

    public HerdrKeypadPlugin()
    {
        Herdr.Log.Info = text => this.Log.Info(text);
        Herdr.Log.Warning = text => this.Log.Warning(text);
        Herdr.Log.Error = text => this.Log.Error(text);
        this.Board = new AgentBoard(this.Client);
        this.Navigation = new Navigation(this.Client);
        this.FixedSlotActions = new FixedSlotActions(this.Client);
    }

    public override void Load()
    {
        this.Log.Info($"Agent focus: {(Config.Load().ExperimentalGoToAgentFocus ? "experimental hybrid Go to spike" : "server API")}");
        if (Config.Load().ExperimentalGoToAgentFocus)
            this.Log.Warning("Experimental hybrid agent-focus enabled (2026-11-18 v5, default-on); immediate Go to typing, configurable input delay, duplicate presses dropped");
        foreach (var action in Navigation.All) this.AddDynamicAction(new ShortcutCommand(this, action));
        for (var slot = 1; slot <= FixedSlots.Count; slot++)
        {
            this.fixedSlots.Add(new FixedSlotCommand(this, slot, isSpace: false));
            this.fixedSlots.Add(new FixedSlotCommand(this, slot, isSpace: true));
        }
        foreach (var action in this.fixedSlots) this.AddDynamicAction(action);
        this.Board.Changed += this.RedrawSlots;
        this.Client.AgentFocusChanged += this.Board.NavigationChanged;
        _ = Task.Run(() => this.Board.RunAsync(this.stop.Token));
    }
    public override void Unload()
    {
        this.Board.Changed -= this.RedrawSlots;
        this.Client.AgentFocusChanged -= this.Board.NavigationChanged;
        this.stop.Cancel();
    }

    private void RedrawSlots()
    {
        foreach (var action in this.fixedSlots) action.Refresh();
    }
}

public sealed class HerdrApplication : ClientApplication
{
    protected override string GetProcessName() => "";
    protected override string GetBundleName() => "";
    public override ClientApplicationStatus GetApplicationStatus() => ClientApplicationStatus.Unknown;
}
