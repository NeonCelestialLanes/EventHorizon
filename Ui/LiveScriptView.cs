using Dalamud.Bindings.ImGui;
using EventHorizon.Hosting;
using EventHorizon.Models;
using EventHorizon.Theme;

namespace EventHorizon.Ui;

internal readonly record struct SpeechFocus(string Title, string Spoken, int Number, int Count);

internal static class LiveScriptView
{
    public static SpeechFocus Focus(EventDocument ev, LiveEventState state, ScheduleActivity? activity)
    {
        var head = activity is null ? null : ScriptSpeech.Head(ev, activity);
        var scriptId = head?.ScriptId ?? activity?.ScriptId;
        var title = head?.Title ?? activity?.Title ?? "This block";
        if (string.IsNullOrWhiteSpace(scriptId))
            return new SpeechFocus(title, "", 0, 0);

        var paragraphs = ScriptSpeech.Paragraphs(ev.Scripts, scriptId);
        if (paragraphs.Count == 0)
            return new SpeechFocus(title, "", 0, 0);

        var current = Math.Clamp(Cursor(state, scriptId).Index, 0, paragraphs.Count - 1);
        return new SpeechFocus(title, paragraphs[current].Spoken, current + 1, paragraphs.Count);
    }

    public static string Spoken(EventDocument ev, ScheduleActivity? activity)
    {
        var head = activity is null ? null : ScriptSpeech.Head(ev, activity);
        var scriptId = head?.ScriptId ?? activity?.ScriptId;
        return ScriptSpeech.SpokenScript(ScriptSpeech.Paragraphs(ev.Scripts, scriptId));
    }

    public static void CopyAnnouncement(HorizonApp app, EventDocument ev, LiveEventState state, ScheduleActivity? activity)
    {
        var head = activity is null ? null : ScriptSpeech.Head(ev, activity);
        var scriptId = head?.ScriptId ?? activity?.ScriptId;
        if (string.IsNullOrWhiteSpace(scriptId))
            return;

        var paragraphs = ScriptSpeech.Paragraphs(ev.Scripts, scriptId);
        if (paragraphs.Count == 0)
            return;

        var current = Math.Clamp(Cursor(state, scriptId).Index, 0, paragraphs.Count - 1);
        Copy(app, ev, state, scriptId, paragraphs, current, advance: true, wrap: paragraphs.Count > 1);
    }

    public static void Draw(HorizonApp app, EventDocument ev, LiveEventState state, ScheduleActivity? activity)
    {
        var head = activity is null ? null : ScriptSpeech.Head(ev, activity);
        var scriptId = head?.ScriptId ?? activity?.ScriptId;
        var paragraphs = ScriptSpeech.Paragraphs(ev.Scripts, scriptId);
        var title = head?.Title ?? activity?.Title ?? "Script";
        var when = head is null ? "" : EventClock.FormatClock(ev.Date, head.StartTime, ev.Timezone);
        Chrome.Section(when.Length == 0 ? title : when + " — " + title);
        var directions = ev.Scripts.FirstOrDefault(script => script.ScriptId == scriptId)?.StageDirections;
        if (!string.IsNullOrWhiteSpace(directions) && !directions.StartsWith("Read this continued", StringComparison.Ordinal))
            ImGui.TextColored(ThemeHolder.Gold, directions);

        LiveHostPanel.GameMenu(app, ev, state);

        if (Chrome.Button("Previous activity"))
        {
            var previous = ScriptSpeech.PreviousActivity(ev, state);
            if (previous is null)
                app.Note("This is the first activity.");
            else
            {
                HostController.StartActivity(ev, state, previous.ActivityId);
                app.Store.MarkState(ev.EventId);
            }
        }

        ImGui.SameLine();
        if (Chrome.Button("Next activity"))
            app.Advance();

        if (paragraphs.Count == 0)
        {
            Chrome.Muted("This activity has no script.");
            return;
        }

        var cursor = Cursor(state, scriptId!);
        var current = Math.Clamp(cursor.Index, 0, paragraphs.Count - 1);
        ImGui.TextDisabled("Paragraph " + (current + 1) + " of " + paragraphs.Count + ". Copied is not the same as sent.");

        if (Chrome.Accent("Copy next"))
            Copy(app, ev, state, scriptId!, paragraphs, current, advance: true);
        ImGui.SameLine();
        if (Chrome.Button("Copy previous"))
            Copy(app, ev, state, scriptId!, paragraphs, Math.Max(0, current - 1), advance: false, stick: true);
        ImGui.SameLine();
        if (Chrome.Button("Copy current"))
            Copy(app, ev, state, scriptId!, paragraphs, current, advance: false);
        ImGui.SameLine();
        if (Chrome.Button("Copy all"))
        {
            ImGui.SetClipboardText(string.Join("\n\n", paragraphs.SelectMany(paragraph => paragraph.Parts)));
            app.Note("Copied all " + paragraphs.Count + " paragraphs.");
        }

        if (Chrome.Button("Mark paragraph as delivered"))
        {
            if (!cursor.Delivered.Contains(current))
                cursor.Delivered.Add(current);
            app.Store.MarkState(ev.EventId);
            app.Note("Marked paragraph " + (current + 1) + " as delivered. Event Horizon did not send it.");
        }

        ImGui.SameLine();
        if (Chrome.Button("Reset progress"))
        {
            cursor.Index = 0;
            cursor.Copied.Clear();
            cursor.Delivered.Clear();
            app.Store.MarkState(ev.EventId);
            app.Note("Script progress reset.");
        }

        Chrome.Section("FFXIV chat announcements");
        for (var index = 0; index < paragraphs.Count; index++)
        {
            var paragraph = paragraphs[index];
            var done = cursor.Copied.Contains(index) || cursor.Delivered.Contains(index);
            var color = index == current ? ThemeHolder.Accent : done ? ThemeHolder.Muted : ThemeHolder.Text;
            var label = "Paragraph " + (index + 1) + " of " + paragraphs.Count;
            if (ImGui.Selectable(label + "##speech-" + index, index == current))
            {
                cursor.Index = index;
                app.Store.MarkState(ev.EventId);
            }

            ImGui.PushStyleColor(ImGuiCol.Text, color);
            ImGui.TextWrapped(paragraph.Spoken);
            ImGui.PopStyleColor();
            if (paragraph.Split)
                Chrome.Muted("This paragraph is longer than one yell (" + ChatLines.YellLimit + " characters, including /yell). Copy it in " + paragraph.Parts.Count + " parts. The spoken script is unchanged.");

            for (var part = 0; part < paragraph.Parts.Count; part++)
            {
                var caption = paragraph.Parts.Count == 1 ? "Copy" : "Copy part " + (part + 1);
                if (!Chrome.Button(caption + "##yell-" + index + "-" + part))
                    continue;

                ImGui.SetClipboardText(paragraph.Parts[part]);
                Remember(cursor, index, advance: false);
                app.Store.MarkState(ev.EventId);
                app.Note("Copied paragraph " + (index + 1) + (paragraph.Parts.Count == 1 ? "." : ", part " + (part + 1) + "."));
            }
        }
    }

    private static void Copy(HorizonApp app, EventDocument ev, LiveEventState state, string scriptId, List<SpeechParagraph> paragraphs, int index, bool advance, bool stick = false, bool wrap = false)
    {
        if (paragraphs.Count == 0)
            return;

        index = Math.Clamp(index, 0, paragraphs.Count - 1);
        var paragraph = paragraphs[index];
        var last = index + 1 >= paragraphs.Count;
        ImGui.SetClipboardText(paragraph.Parts.Count == 1 ? paragraph.Parts[0] : string.Join("\n", paragraph.Parts));
        var cursor = Cursor(state, scriptId);
        Remember(cursor, index, advance && !last);
        if (stick)
            cursor.Index = index;
        else if (wrap && last)
            cursor.Index = 0;
        app.Store.MarkState(ev.EventId);
        app.Note(wrap && last
            ? "Copied paragraph " + paragraphs.Count + ". Next copy is the first announcement."
            : last && advance
                ? "Copied paragraph " + paragraphs.Count + ". That was the last one."
                : "Copied paragraph " + (index + 1) + " of " + paragraphs.Count + ".");
    }

    private static void Remember(SpeechCursor cursor, int index, bool advance)
    {
        if (!cursor.Copied.Contains(index))
            cursor.Copied.Add(index);
        cursor.Index = advance ? index + 1 : index;
    }

    private static SpeechCursor Cursor(LiveEventState state, string scriptId)
    {
        state.Normalize();
        if (!state.Speeches.TryGetValue(scriptId, out var cursor))
        {
            cursor = new SpeechCursor();
            state.Speeches[scriptId] = cursor;
        }

        cursor.Normalize();
        return cursor;
    }
}
