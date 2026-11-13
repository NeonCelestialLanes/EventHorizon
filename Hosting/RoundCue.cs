using EventHorizon.Models;

namespace EventHorizon.Hosting;

internal static class RoundCue
{
    public static string Paste(IReadOnlyList<ScriptDocument> scripts, GameRound round)
    {
        var script = scripts.FirstOrDefault(item =>
            !ScriptSpeech.IsFollowOn(scripts, item.ScriptId)
            && (item.SpokenText ?? "").Contains("[READ CHIRP A]", StringComparison.OrdinalIgnoreCase));
        if (script is not null)
        {
            var filled = Fill(script.SpokenText ?? "", round);
            if (filled.Length > 0)
                return filled;
        }

        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(round.Prompt))
            lines.Add(round.Prompt.Trim());
        foreach (var choice in round.Choices.Where(choice => !string.IsNullOrWhiteSpace(choice)))
        {
            var label = Letter(choice);
            var body = Body(choice);
            lines.Add(label.Length == 1 ? label + ": " + body : body);
        }

        return Chat(lines);
    }

    public static string Fill(string spoken, GameRound round)
    {
        var text = spoken.Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (var choice in round.Choices)
        {
            var label = Letter(choice);
            if (label.Length != 1)
                continue;
            text = text.Replace("[READ CHIRP " + label + "]", Body(choice), StringComparison.OrdinalIgnoreCase);
        }

        var keep = new List<string>();
        foreach (var paragraph in text.Split("\n\n"))
        {
            if (paragraph.Contains("[ANSWER]", StringComparison.OrdinalIgnoreCase) || paragraph.Contains("[WINNER]", StringComparison.OrdinalIgnoreCase))
                break;
            if (paragraph.Trim().Length > 0)
                keep.Add(paragraph.Trim());
        }

        return Chat(keep);
    }

    private static string Chat(IReadOnlyList<string> parts) =>
        string.Join(" ", parts).Replace('\n', ' ').Replace('\r', ' ');

    private static string Letter(string choice)
    {
        var text = choice.Trim();
        return text.Length > 0 && char.IsLetter(text[0]) && (text.Length == 1 || text[1] is '.' or ')' or ':')
            ? text[0].ToString()
            : "";
    }

    private static string Body(string choice)
    {
        var text = choice.Trim();
        if (text.Length >= 2 && char.IsLetter(text[0]) && text[1] is '.' or ')' or ':')
        {
            var body = text[2..].Trim();
            if (body.Length > 0)
                return body;
        }

        return text;
    }
}
