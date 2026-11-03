namespace Loupedeck.HerdrKeypad;

using Herdr;

public sealed class AgentsFolder : PluginDynamicFolder
{
    private string[] names = [];
    private CloseTarget? armed;
    private DateTime armedUntil;
    private bool longPressHandled;
    private string? cyclePressed;
    private string? lastFolderStatus;
    public AgentsFolder()
    {
        this.DisplayName = "Herdr agents";
        this.Description = "Live agents in Herdr";
        this.GroupName = "Herdr";
    }
    private HerdrKeypadPlugin Owner => (HerdrKeypadPlugin)this.Plugin;
    public override bool Load()
    {
        this.Owner.Board.Changed += this.Changed;
        return true;
    }
    public override bool Unload() { this.Owner.Board.Changed -= this.Changed; return true; }
    public override bool Activate() { this.armed = null; this.cyclePressed = null; this.names = []; this.ButtonActionNamesChanged(); return true; }
    public override bool Deactivate() { this.armed = null; this.cyclePressed = null; return true; }
    public override PluginDynamicFolderNavigation GetNavigationArea(DeviceType deviceType) => PluginDynamicFolderNavigation.None;
    public override string GetButtonDisplayName(PluginImageSize imageSize) => "Agents";
    public override BitmapImage GetButtonImage(PluginImageSize imageSize) => KeyFaces.State(this.FolderStatus(), imageSize);

    private string FolderStatus() => this.Owner.Board.Online
        ? AgentPriority.MostUrgent(this.Owner.Board.Snapshot.Recent)?.AgentStatus ?? "none"
        : "offline";
    public override IEnumerable<string> GetButtonPressActionNames(DeviceType deviceType)
    {
        yield return this.CreateCommandName("action:cycle");
        yield return this.CreateCommandName("action:cycle-tabs");
        foreach (var a in this.Owner.Board.Snapshot.Recent)
        {
            var tag = HashCode.Combine(a.Name, a.DisplayAgent, a.TerminalTitleStripped, a.Kind);
            yield return this.CreateCommandName($"agent:{(this.armed?.PaneId == a.PaneId ? "confirm" : a.AgentStatus + "-" + tag.ToString("x8"))}:{a.PaneId}");
        }
    }
    private Agent? Find(string parameter)
    {
        if (!parameter.StartsWith("agent:", StringComparison.Ordinal)) return null;
        var colon = parameter.IndexOf(':', 6);
        return colon < 0 ? null : this.Owner.Board.Snapshot.Agents.FirstOrDefault(a => a.PaneId == parameter[(colon + 1)..]);
    }
    public override string GetCommandDisplayName(string actionParameter, PluginImageSize imageSize)
    {
        if (actionParameter == "action:cycle") return "Cycle panes";
        if (actionParameter == "action:cycle-tabs") return "Cycle tabs";
        var agent = this.Find(actionParameter);
        if (agent is null) return " ";
        if (actionParameter.StartsWith("agent:confirm:", StringComparison.Ordinal)) return "Close?";
        var index = this.Owner.Board.Snapshot.Recent.ToList().FindIndex(a => a.PaneId == agent.PaneId);
        return FixedSlots.AgentLabel(agent, index + 1);
    }
    public override BitmapImage GetCommandImage(string actionParameter, PluginImageSize imageSize) =>
        actionParameter.StartsWith("agent:confirm:", StringComparison.Ordinal) ? KeyFaces.Confirm(imageSize) :
        actionParameter.StartsWith("agent:", StringComparison.Ordinal) ? KeyFaces.State(this.Find(actionParameter)?.AgentStatus ?? "unknown", imageSize) :
        actionParameter.StartsWith("action:", StringComparison.Ordinal) ? KeyFaces.Shortcut(actionParameter[7..], this.GetCommandDisplayName(actionParameter, imageSize), imageSize) : KeyFaces.Glyph(" ", imageSize);
    public override bool ProcessButtonEvent2(string actionParameter, DeviceButtonEvent2 buttonEvent)
    {
        if (actionParameter is "action:cycle" or "action:cycle-tabs")
        {
            switch (buttonEvent.EventType)
            {
                case DeviceButtonEventType.Press: this.cyclePressed = actionParameter; return true;
                case DeviceButtonEventType.LongPress:
                case DeviceButtonEventType.RepeatPress: return true;
                case DeviceButtonEventType.Release:
                    if (this.cyclePressed == actionParameter) this.RunCommand(actionParameter);
                    this.cyclePressed = null;
                    return true;
                default: return false;
            }
        }
        if (!actionParameter.StartsWith("agent:", StringComparison.Ordinal)) return base.ProcessButtonEvent2(actionParameter, buttonEvent);
        switch (buttonEvent.EventType)
        {
            case DeviceButtonEventType.Press: this.longPressHandled = false; return true;
            case DeviceButtonEventType.LongPress:
                this.longPressHandled = true;
                if (this.Find(actionParameter) is { TerminalId.Length: > 0 } agent)
                {
                    var target = new CloseTarget(agent.PaneId, agent.TerminalId, agent.WorkspaceId, agent.Kind);
                    this.armed = target;
                    this.armedUntil = DateTime.UtcNow.AddSeconds(3);
                    this.Changed();
                    _ = this.ExpireAsync(target);
                }
                return true;
            case DeviceButtonEventType.RepeatPress: return true;
            case DeviceButtonEventType.Release:
                if (!this.longPressHandled) this.RunCommand(actionParameter);
                this.longPressHandled = false;
                return true;
            default: return false;
        }
    }
    public override void RunCommand(string actionParameter)
    {
        if (actionParameter is "action:cycle" or "action:cycle-tabs")
        {
            var id = actionParameter[7..];
            this.Plugin.Log.Info($"{id} pressed in Agents folder");
            _ = this.ExecuteAsync(id);
        }
        else if (this.Find(actionParameter) is { } agent)
        {
            var target = this.armed;
            this.armed = null;
            if (target is not null && target.PaneId == agent.PaneId && actionParameter.StartsWith("agent:confirm:", StringComparison.Ordinal) && DateTime.UtcNow <= this.armedUntil)
                _ = this.CloseAsync(target);
            else _ = this.FocusAsync(agent.PaneId);
            this.Changed();
        }
    }
    private async Task ExpireAsync(CloseTarget target)
    {
        await Task.Delay(TimeSpan.FromSeconds(3));
        if (ReferenceEquals(this.armed, target)) { this.armed = null; this.Changed(); }
    }
    private async Task CloseAsync(CloseTarget target)
    {
        try
        {
            if (!await new CloseAgent(this.Owner.Client).CloseIfStillPresentAsync(target))
                this.Plugin.Log.Warning("Herdr agent changed; close cancelled");
        }
        catch (Exception e) { this.Plugin.Log.Error($"Could not close Herdr pane: {e.Message}"); }
    }
    private async Task ExecuteAsync(string id)
    {
        try { await this.Owner.Navigation.ExecuteAsync(id); }
        catch (Exception e) { this.Plugin.Log.Error($"Could not run Herdr shortcut: {e.Message}"); }
    }
    private async Task FocusAsync(string paneId)
    {
        try { await this.Owner.Client.FocusPaneAsync(paneId); }
        catch (Exception e) { this.Plugin.Log.Error($"Could not focus Herdr pane: {e.Message}"); }
    }
    private void Changed()
    {
        // The host caches the folder key independently of its contents.
        try
        {
            var status = this.FolderStatus();
            if (status != this.lastFolderStatus)
            {
                this.Plugin.OnActionImageChanged("#DynamicFolder", this.Name, true);
                this.lastFolderStatus = status;
            }
        }
        catch (Exception e) { this.Plugin.Log.Error(e, "Could not refresh Herdr folder status"); }

        try
        {
            var next = this.GetButtonPressActionNames(DeviceType.All).ToArray();
            if (!next.SequenceEqual(this.names)) { this.names = next; this.ButtonActionNamesChanged(); }
            foreach (var name in next) this.CommandImageChanged(name);
        }
        catch (Exception e) { this.Plugin.Log.Error(e, "Could not refresh Herdr agents"); }
    }
}
