namespace Loupedeck.HerdrKeypad;

using Herdr;

// Both entry points share menu navigation, shortcut rendering and close gestures.
internal sealed class ShortcutMenu
{
    private readonly HerdrKeypadPlugin owner;
    private readonly Action refresh;
    private readonly Action? backToAgents;
    private readonly ClosePaneControl closePane;
    private string? level;
    private bool commandPressed;

    public ShortcutMenu(HerdrKeypadPlugin owner, Action refresh, Action? backToAgents = null)
    {
        this.owner = owner;
        this.refresh = refresh;
        this.backToAgents = backToAgents;
        this.closePane = new ClosePaneControl(owner, refresh, text => owner.Log.Warning(text));
    }

    public void Reset() { this.closePane.Cancel(); this.commandPressed = false; this.level = null; }
    public IEnumerable<string> Parameters()
    {
        if (this.level is null)
        {
            yield return this.backToAgents is not null ? "back" : "blank";
            yield return "action:zoom";
            yield return "action:cycle";
            yield return "action:cycle-tabs";
            yield return "action:go-to";
            foreach (var group in new[] { "Layout", "Tabs", "Workspaces" }) yield return "group:" + group;
        }
        else
        {
            yield return "back";
            foreach (var action in Herdr.Navigation.All.Where(a => a.Group == this.level))
                yield return "action:" + (action.Id == "close-pane" && this.closePane.IsArmed ? "close-pane-confirm" : action.Id);
        }
    }
    public string GetCommandDisplayName(string actionParameter, PluginImageSize imageSize) => actionParameter switch
    {
        "back" => "Back",
        "action:close-pane-confirm" => "Close?",
        _ when actionParameter.StartsWith("group:", StringComparison.Ordinal) => actionParameter[6..],
        _ when actionParameter.StartsWith("action:", StringComparison.Ordinal) => Herdr.Navigation.All.FirstOrDefault(a => a.Id == actionParameter[7..])?.Label ?? " ",
        _ => " ",
    };
    public BitmapImage GetCommandImage(string actionParameter, PluginImageSize imageSize)
    {
        if (actionParameter == "action:close-pane-confirm") return KeyFaces.Confirm(imageSize);
        if (actionParameter == "back") return KeyFaces.Icon("chevron-left", "Back", imageSize);
        if (actionParameter.StartsWith("group:", StringComparison.Ordinal)) return KeyFaces.Group(actionParameter[6..], imageSize);
        if (actionParameter.StartsWith("action:", StringComparison.Ordinal))
            return KeyFaces.Shortcut(actionParameter[7..], this.GetCommandDisplayName(actionParameter, imageSize), imageSize);
        return KeyFaces.Glyph(" ", imageSize);
    }
    public bool ProcessButtonEvent2(string actionParameter, DeviceButtonEvent2 buttonEvent)
    {
        if (actionParameter == "blank") return true;
        if (IsClosePane(actionParameter)) return this.level == "Layout" && this.closePane.Handle(buttonEvent) == true;
        switch (buttonEvent.EventType)
        {
            case DeviceButtonEventType.Press: this.commandPressed = true; return true;
            case DeviceButtonEventType.LongPress:
            case DeviceButtonEventType.RepeatPress: return true;
            case DeviceButtonEventType.Release:
                if (this.commandPressed) this.RunCommand(actionParameter);
                this.commandPressed = false;
                return true;
            default: return false;
        }
    }
    public void RunCommand(string actionParameter)
    {
        if (actionParameter == "back")
        {
            this.closePane.Cancel();
            if (this.level is null) this.backToAgents?.Invoke();
            else { this.level = null; this.Refresh(); }
        }
        else if (actionParameter.StartsWith("group:", StringComparison.Ordinal)) { this.closePane.Cancel(); this.level = actionParameter[6..]; this.owner.Log.Info($"Shortcuts folder opened {this.level}"); this.Refresh(); }
        else if (IsClosePane(actionParameter)) { }
        else if (actionParameter.StartsWith("action:", StringComparison.Ordinal))
        {
            this.closePane.Cancel();
            this.Refresh();
            this.owner.Log.Info($"Shortcuts folder ran {actionParameter[7..]}");
            _ = this.ExecuteAsync(actionParameter[7..]);
        }
    }
    private static bool IsClosePane(string parameter) => parameter is "action:close-pane" or "action:close-pane-confirm";
    private void Refresh() => this.refresh();
    private async Task ExecuteAsync(string id)
    {
        try { await this.owner.Navigation.ExecuteAsync(id); }
        catch (Exception e) { this.owner.Log.Error($"Could not run Herdr shortcut: {e.Message}"); }
    }
}
