using EventHorizon.Models;

namespace EventHorizon.Hosting;

internal static class ChatLines
{
    public const int YellLimit = 500;

    public static IReadOnlyList<string> Yells(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        var text = raw.Replace("\r\n", "\n").Replace('\r', '\n');
        var starts = new List<int>();
        var index = 0;
        while (index < text.Length)
        {
            var found = text.IndexOf("/yell", index, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
                break;
            if (IsYellCommand(text, found))
                starts.Add(found);
            index = found + 5;
        }

        if (starts.Count == 0)
        {
            var plain = Flatten(text);
            return plain.Length == 0 ? [] : [plain];
        }

        var pieces = new List<string>(starts.Count);
        for (var piece = 0; piece < starts.Count; piece++)
        {
            var end = piece + 1 < starts.Count ? starts[piece + 1] : text.Length;
            var body = Flatten(text[starts[piece]..end]);
            if (body.Length > 0)
                pieces.Add(body);
        }

        return pieces;
    }

    public static string SectionRoot(IReadOnlyList<ScriptDocument> scripts, string scriptId)
    {
        if (Follow(scriptId, out var root, out _) && scripts.Any(script => script.ScriptId == root))
            return root;
        return scriptId;
    }

    public static IReadOnlyList<string> Section(IReadOnlyList<ScriptDocument> scripts, string? scriptId)
    {
        if (string.IsNullOrWhiteSpace(scriptId))
            return [];

        var root = SectionRoot(scripts, scriptId);
        var members = new List<(int Order, string? Announcement)>();
        foreach (var script in scripts)
        {
            if (script.ScriptId == root)
            {
                members.Add((0, script.Announcement));
                continue;
            }

            if (Follow(script.ScriptId, out var parent, out var number) && parent == root)
                members.Add((number, script.Announcement));
        }

        var prompts = new List<string>();
        foreach (var member in members.OrderBy(item => item.Order))
            prompts.AddRange(Yells(member.Announcement));
        return prompts;
    }

    private static bool IsYellCommand(string text, int index)
    {
        if (index > 0 && !char.IsWhiteSpace(text[index - 1]))
            return false;

        var after = index + 5;
        return after >= text.Length || !char.IsLetterOrDigit(text[after]);
    }

    public static bool Follow(string scriptId, out string root, out int number)
    {
        root = "";
        number = 0;
        const string mark = "-chat-";
        var at = scriptId.LastIndexOf(mark, StringComparison.Ordinal);
        if (at <= 0 || at + mark.Length >= scriptId.Length)
            return false;
        if (!int.TryParse(scriptId[(at + mark.Length)..], out number) || number < 1)
            return false;

        root = scriptId[..at];
        return true;
    }

    private static string Flatten(string raw)
    {
        var builder = new System.Text.StringBuilder();
        var space = false;
        foreach (var character in raw)
        {
            if (char.IsWhiteSpace(character))
            {
                space = builder.Length > 0;
                continue;
            }

            if (space)
                builder.Append(' ');
            builder.Append(character);
            space = false;
        }

        return builder.ToString();
    }
}
