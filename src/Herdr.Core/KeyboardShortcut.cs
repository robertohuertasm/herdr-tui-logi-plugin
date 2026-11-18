using System.Globalization;

namespace Herdr;

// A comma separates sequential strokes, e.g. ctrl+b,g. Validate before raising
// the terminal so malformed settings never produce partial keyboard input.
internal static class KeyboardShortcut
{
    public static string Script(string application, string sequence) => Wrap(application, Strokes(sequence));

    public static string TypeGoToScript(string application, string query, int inputDelayMs)
    {
        GoToAgentFocus.ValidateQuery(query);
        return Wrap(application, [
            $"delay {Seconds(inputDelayMs)}", "keystroke \"/\"", "keystroke \"u\" using {control down}",
            $"keystroke \"{query}\"",
        ], activate: false, delayMs: inputDelayMs);
    }

    public static string ConfirmGoToScript(string application, int inputDelayMs) =>
        Wrap(application, [$"delay {Seconds(inputDelayMs)}", "key code 36", "delay 0.4"], activate: false, delayMs: inputDelayMs);

    private static string Seconds(int milliseconds)
    {
        if (milliseconds is < 0 or > 5000) throw new HerdrException("goToInputDelayMs must be between 0 and 5000");
        return (milliseconds / 1000m).ToString(CultureInfo.InvariantCulture);
    }

    private static IEnumerable<string> Strokes(string sequence)
    {
        return sequence.Split(',').Select(stroke =>
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
    }

    private static string Wrap(string application, IEnumerable<string> strokes, bool activate = true, int delayMs = 100)
    {
        var quoted = "\"" + application.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        var commands = string.Join($"\ndelay {Seconds(delayMs)}\n", strokes.Select(stroke =>
            "set foregroundBundle to bundle identifier of (first application process whose frontmost is true)\n" +
            "if foregroundBundle is not targetBundle then error \"Terminal lost focus\"\n" + stroke));
        return $"""
            {(activate ? $"tell application {quoted} to activate" : "")}
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
