namespace Herdr;

// A comma separates sequential strokes, e.g. ctrl+b,g. Validate before raising
// the terminal so malformed settings never produce partial keyboard input.
internal static class KeyboardShortcut
{
    public static string Script(string application, string sequence)
    {
        var strokes = sequence.Split(',').Select(stroke =>
        {
            var parts = stroke.Trim().ToLowerInvariant().Split('+');
            var key = parts[^1];
            if (key.Length != 1 || !char.IsAsciiLetterOrDigit(key[0]))
                throw new HerdrException("Shortcut keys must be single ASCII letters or digits");
            var modifiers = parts[..^1].Select(modifier => modifier switch
            {
                "ctrl" => "control down", "alt" => "option down",
                "shift" => "shift down", "cmd" => "command down",
                _ => throw new HerdrException($"Unknown shortcut modifier: {modifier}"),
            }).ToArray();
            return $"keystroke \"{key}\"" + (modifiers.Length == 0 ? "" : " using {" + string.Join(", ", modifiers) + "}");
        }).ToArray();
        var quoted = "\"" + application.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        var commands = string.Join("\ndelay 0.1\n", strokes.Select(stroke =>
            "set foregroundBundle to bundle identifier of (first application process whose frontmost is true)\n" +
            "if foregroundBundle is not targetBundle then error \"Terminal lost focus\"\n" + stroke));
        return $"""
            tell application {quoted} to activate
            set targetBundle to id of application {quoted}
            tell application "System Events"
                repeat 30 times
                    set foregroundBundle to bundle identifier of (first application process whose frontmost is true)
                    if foregroundBundle is targetBundle then exit repeat
                    delay 0.05
                end repeat
                {commands}
            end tell
            """;
    }
}
