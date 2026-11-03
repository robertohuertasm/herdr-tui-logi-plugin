namespace Loupedeck.HerdrKeypad;

using Herdr;

public sealed class ShortcutCommand : PluginDynamicCommand
{
    private readonly HerdrKeypadPlugin owner;
    private readonly Shortcut shortcut;
    private readonly ClosePaneControl? closePane;
    public ShortcutCommand(HerdrKeypadPlugin owner, Shortcut shortcut)
        : base(shortcut.Label, shortcut.Id == "close-pane" ? "Hold, then press again to confirm closing the focused pane" : $"{shortcut.Label} in Herdr", "Herdr shortcuts", DeviceType.All)
    {
        this.owner = owner;
        this.shortcut = shortcut;
        this.Name = "herdr_keypad_" + shortcut.Id;
        if (shortcut.Id == "close-pane")
            this.closePane = new ClosePaneControl(owner, this.ActionImageChanged, text => this.Log.Warning(text));
    }
    protected override bool ProcessButtonEvent2(string actionParameter, DeviceButtonEvent2 buttonEvent) =>
        this.closePane is null ? base.ProcessButtonEvent2(actionParameter, buttonEvent) : this.closePane.Handle(buttonEvent);
    protected override void RunCommand(string actionParameter)
    {
        if (this.closePane is null) _ = this.Execute();
    }
    private async Task Execute()
    {
        try { await owner.Navigation.ExecuteAsync(shortcut.Id); }
        catch (Exception e) { this.Log.Error($"Could not run {shortcut.Id}: {e.Message}"); }
    }
    protected override string GetCommandDisplayName(string actionParameter, PluginImageSize imageSize) => this.closePane?.IsArmed == true ? "Close?" : shortcut.Label;
    protected override BitmapImage GetCommandImage(string actionParameter, PluginImageSize imageSize) =>
        this.closePane?.IsArmed == true ? KeyFaces.Confirm(imageSize) : KeyFaces.Shortcut(shortcut.Id, shortcut.Label, imageSize);
}

internal static class KeyFaces
{
    private static readonly Dictionary<string, BitmapImage?> Icons = [];

    public static BitmapImage Shortcut(string id, string fallback, PluginImageSize size) => Icon(id switch
    {
        "zoom" => "screen-full",
        "go-to" => "arrow-circle-right",
        "cycle" => "sync",
        "cycle-tabs" => "multiple-windows",
        "split-right" => "split-horizontal",
        "split-down" => "split-vertical",
        "pane-prev" => "arrow-left",
        "pane-next" => "arrow-right",
        "close-pane" => "close",
        "tab-new" => "add",
        "tab-prev" => "chevron-left",
        "tab-next" => "chevron-right",
        "workspace-new" => "new-folder",
        "workspace-prev" => "arrow-circle-left",
        "workspace-next" => "arrow-circle-right",
        _ => "",
    }, fallback, size);

    public static BitmapImage Group(string group, PluginImageSize size) => Icon(group switch
    {
        "Layout" => "layout",
        "Tabs" => "multiple-windows",
        "Workspaces" => "folder",
        _ => "",
    }, group, size);

    public static BitmapImage Icon(string name, string fallback, PluginImageSize size)
    {
        var image = ReadIcon(name);
        if (image is null) return Glyph(fallback, size);
        using var b = new BitmapBuilder(size);
        b.Clear(BitmapColor.Black);
        var side = (int)(Math.Min(b.Width, b.Height) * 0.5f);
        b.DrawImage(image, (b.Width - side) / 2, (int)(b.Height * 0.55f) - side / 2, side, side, BitmapRotation.None);
        return b.ToImage();
    }

    private static BitmapImage? ReadIcon(string name)
    {
        lock (Icons)
        {
            if (Icons.TryGetValue(name, out var cached)) return cached;
            BitmapImage? image = null;
            using var stream = typeof(KeyFaces).Assembly.GetManifestResourceStream($"Icons.{name}.svg");
            if (stream is not null)
            {
                using var reader = new StreamReader(stream);
                var svg = reader.ReadToEnd()
                    .Replace("currentColor", "#ffffff")
                    .Replace("width=\"16\" height=\"16\"", "width=\"128\" height=\"128\"")
                    .Replace("</svg>", "<rect width=\"0.01\" height=\"0.01\" fill=\"#000001\"/></svg>");
                image = BitmapImage.FromSvg(svg);
            }
            Icons[name] = image;
            return image;
        }
    }

    public static BitmapImage Confirm(PluginImageSize size)
    {
        using var b = new BitmapBuilder(size);
        b.Clear(new BitmapColor(200, 30, 30));
        b.DrawText("?", 0, (int)(b.Height * 0.15), b.Width, (int)(b.Height * 0.8), BitmapColor.White, (int)(b.Height * 0.4), 0, 0, null);
        return b.ToImage();
    }

    public static BitmapImage State(string status, PluginImageSize size, bool remote = false)
    {
        var rgb = AgentPalette.For(status);
        var color = new BitmapColor(rgb.Red, rgb.Green, rgb.Blue);
        using var b = new BitmapBuilder(size);
        b.Clear(BitmapColor.Black);
        var radius = Math.Min(b.Width, b.Height) * 0.27f;
        b.FillCircle(b.Width / 2f, b.Height * 0.55f, radius, color);
        if (status is "none" or "offline")
            b.FillCircle(b.Width / 2f, b.Height * 0.55f, radius * 0.7f, BitmapColor.Black);
        else if (ReadIcon("herdr") is { } logo)
        {
            var side = (int)(Math.Min(b.Width, b.Height) * 0.38f);
            b.DrawImage(logo, (b.Width - side) / 2, (int)(b.Height * 0.55f) - side / 2, side, side, BitmapRotation.None);
        }
        if (remote)
        {
            var unit = Math.Min(b.Width, b.Height);
            var cx = b.Width / 2f + unit * 0.23f;
            var cy = b.Height * 0.55f - unit * 0.23f;
            var badgeRadius = unit * 0.115f;
            b.FillCircle(cx, cy, badgeRadius + unit * 0.025f, BitmapColor.Black);
            b.FillCircle(cx, cy, badgeRadius, BitmapColor.White);
            b.DrawText("R", (int)(cx - badgeRadius), (int)(cy - badgeRadius + unit * 0.065f) - 2,
                (int)(badgeRadius * 2), (int)(badgeRadius * 2), BitmapColor.Black,
                (int)(unit * 0.15f), 0, 0, null);
        }
        return b.ToImage();
    }

    public static BitmapImage Glyph(string text, PluginImageSize size)
    {
        using var b = new BitmapBuilder(size);
        b.Clear(BitmapColor.Black);
        b.DrawText(text, 0, (int)(b.Height * 0.15), b.Width, (int)(b.Height * 0.8), BitmapColor.White, (int)(b.Height * 0.17), 0, 0, null);
        return b.ToImage();
    }
}
