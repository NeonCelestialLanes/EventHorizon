using EventHorizon.Models;

namespace EventHorizon.Hosting;

internal sealed class SpeechParagraph
{
    public string Spoken { get; init; } = "";

    public string Yell { get; init; } = "";

    public IReadOnlyList<string> Parts { get; init; } = [];

    public bool Split => this.Parts.Count > 1 || this.Yell.Length > ChatLines.YellLimit;
}

internal static class ScriptSpeech
{
    public static bool IsFollowOn(IReadOnlyList<ScriptDocument> scripts, string? scriptId)
    {
        if (string.IsNullOrWhiteSpace(scriptId))
            return false;

        return ChatLines.Follow(scriptId, out var root, out _) && scripts.Any(script => script.ScriptId == root);
    }

    public static string Root(IReadOnlyList<ScriptDocument> scripts, string? scriptId) =>
        string.IsNullOrWhiteSpace(scriptId) ? "" : ChatLines.SectionRoot(scripts, scriptId);

    public static List<ScriptDocument> Members(IReadOnlyList<ScriptDocument> scripts, string? scriptId)
    {
        if (string.IsNullOrWhiteSpace(scriptId))
            return [];

        var root = Root(scripts, scriptId);
        var members = new List<(int Order, ScriptDocument Script)>();
        foreach (var script in scripts)
        {
            if (script.ScriptId == root)
            {
                members.Add((0, script));
                continue;
            }

            if (ChatLines.Follow(script.ScriptId, out var parent, out var number) && parent == root)
                members.Add((number, script));
        }

        return members.OrderBy(item => item.Order).Select(item => item.Script).ToList();
    }

    public static List<SpeechParagraph> Paragraphs(IReadOnlyList<ScriptDocument> scripts, string? scriptId)
    {
        var members = Members(scripts, scriptId);
        if (members.Count == 0)
            return [];

        var yells = new List<string>();
        foreach (var member in members)
            yells.AddRange(ChatLines.Yells(member.Announcement));

        var spoken = SpokenParts(members, yells.Count);
        if (yells.Count == 0)
        {
            foreach (var part in spoken)
                yells.Add(WithYell(part));
        }

        var count = Math.Max(spoken.Count, yells.Count);
        var paragraphs = new List<SpeechParagraph>(count);
        for (var index = 0; index < count; index++)
        {
            var say = index < spoken.Count ? spoken[index] : StripYell(yells[Math.Min(index, yells.Count - 1)]);
            var yell = index < yells.Count ? WithYell(yells[index]) : WithYell(say);
            paragraphs.Add(new SpeechParagraph
            {
                Spoken = say,
                Yell = yell,
                Parts = Fit(yell),
            });
        }

        return paragraphs;
    }

    public static string SpokenScript(IReadOnlyList<SpeechParagraph> paragraphs) =>
        string.Join("\n\n", paragraphs.Select(paragraph => paragraph.Spoken).Where(text => text.Length > 0));

    public static IReadOnlyList<string> Fit(string yell)
    {
        var line = WithYell(yell);
        if (line.Length <= ChatLines.YellLimit)
            return [line];

        var body = line["/yell ".Length..];
        var sentences = Sentences(body);
        var parts = new List<string>();
        var batch = "";
        foreach (var sentence in sentences)
        {
            var trial = batch.Length == 0 ? sentence : batch + " " + sentence;
            if (("/yell " + trial).Length <= ChatLines.YellLimit)
            {
                batch = trial;
                continue;
            }

            if (batch.Length > 0)
                parts.Add("/yell " + batch);
            if (("/yell " + sentence).Length <= ChatLines.YellLimit)
            {
                batch = sentence;
                continue;
            }

            parts.AddRange(Words(sentence));
            batch = "";
        }

        if (batch.Length > 0)
            parts.Add("/yell " + batch);
        return parts.Count == 0 ? [line] : parts;
    }

    public static ScheduleActivity? Head(EventDocument ev, ScheduleActivity activity)
    {
        var root = Root(ev.Scripts, activity.ScriptId);
        if (root.Length == 0 || root == activity.ScriptId)
            return activity;

        return ev.Schedule.FirstOrDefault(item => item.ScriptId == root) ?? activity;
    }

    public static ScheduleActivity? NextActivity(EventDocument ev, LiveEventState state)
    {
        var ordered = ScheduleOrder.Sort(ev);
        var index = ordered.FindIndex(activity => activity.ActivityId == state.CurrentActivityId);
        var root = index < 0 ? "" : Root(ev.Scripts, ordered[index].ScriptId);
        for (var cursor = Math.Max(0, index + 1); cursor < ordered.Count; cursor++)
        {
            var activity = ordered[cursor];
            if (root.Length > 0 && Root(ev.Scripts, activity.ScriptId) == root)
                continue;
            if (IsFollowOn(ev.Scripts, activity.ScriptId))
                continue;
            if (ScheduleOrder.StatusOf(activity, state) is ActivityProgressStatus.Completed or ActivityProgressStatus.Skipped)
                continue;
            return activity;
        }

        return null;
    }

    public static ScheduleActivity? PreviousActivity(EventDocument ev, LiveEventState state)
    {
        var ordered = ScheduleOrder.Sort(ev);
        var index = ordered.FindIndex(activity => activity.ActivityId == state.CurrentActivityId);
        if (index <= 0)
            return null;

        var root = Root(ev.Scripts, ordered[index].ScriptId);
        for (var cursor = index - 1; cursor >= 0; cursor--)
        {
            var activity = ordered[cursor];
            if (root.Length > 0 && Root(ev.Scripts, activity.ScriptId) == root)
                continue;
            return Head(ev, activity);
        }

        return null;
    }

    private static List<string> SpokenParts(List<ScriptDocument> members, int yellCount)
    {
        var root = (members[0].SpokenText ?? "").Replace("\r\n", "\n").Trim();
        var later = members.Skip(1)
            .Select(script => (script.SpokenText ?? "").Trim())
            .Where(text => text.Length > 0)
            .ToList();
        var fromRoot = SplitBlank(root);
        if (later.Count > 0 && later.All(part => root.Contains(part, StringComparison.Ordinal)) && fromRoot.Count >= later.Count)
            return fromRoot.Count > 0 ? fromRoot : later;

        if (members.Count == 1)
            return fromRoot.Count > 0 ? fromRoot : [];

        var each = new List<string>();
        if (fromRoot.Count > 0)
            each.Add(fromRoot[0]);
        each.AddRange(later);
        if (each.Count == 0 && yellCount == 0)
            return fromRoot;
        return each;
    }

    private static List<string> SplitBlank(string text)
    {
        if (text.Length == 0)
            return [];

        return text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => part.Length > 0)
            .Select(part => part.Replace('\n', ' ').Trim())
            .ToList();
    }

    private static List<string> Sentences(string body)
    {
        var sentences = new List<string>();
        var start = 0;
        for (var index = 0; index < body.Length; index++)
        {
            if (body[index] is not ('.' or '!' or '?'))
                continue;
            var end = index + 1;
            while (end < body.Length && char.IsWhiteSpace(body[end]))
                end++;
            var sentence = body[start..end].Trim();
            if (sentence.Length > 0)
                sentences.Add(sentence);
            start = end;
            index = end - 1;
        }

        var tail = body[start..].Trim();
        if (tail.Length > 0)
            sentences.Add(tail);
        return sentences.Count == 0 ? [body.Trim()] : sentences;
    }

    private static List<string> Words(string sentence)
    {
        var parts = new List<string>();
        var words = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var batch = "";
        foreach (var word in words)
        {
            var trial = batch.Length == 0 ? word : batch + " " + word;
            if (("/yell " + trial).Length <= ChatLines.YellLimit)
            {
                batch = trial;
                continue;
            }

            if (batch.Length > 0)
                parts.Add("/yell " + batch);
            batch = word;
        }

        if (batch.Length > 0)
            parts.Add("/yell " + batch);
        return parts;
    }

    private static string WithYell(string text)
    {
        var body = (text ?? "").Trim();
        if (body.StartsWith("/yell", StringComparison.OrdinalIgnoreCase))
            return body;
        return body.Length == 0 ? "" : "/yell " + body;
    }

    private static string StripYell(string text)
    {
        var body = (text ?? "").Trim();
        return body.StartsWith("/yell", StringComparison.OrdinalIgnoreCase) ? body["/yell".Length..].Trim() : body;
    }
}
