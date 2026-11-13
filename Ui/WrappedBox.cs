using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EventHorizon.Ui;

internal readonly struct TextLine
{
    public int Start { get; init; }

    public int End { get; init; }

    public int Next { get; init; }

    public bool Hard { get; init; }
}

internal static class WrappedBox
{
    private static readonly Dictionary<uint, BoxState> Boxes = [];

    public static bool Edit(string id, ref string text, int max, float height)
    {
        var key = ImGui.GetID(id);
        if (!Boxes.TryGetValue(key, out var state))
            Boxes[key] = state = new BoxState { Text = text };

        if (!string.Equals(state.Text, text, StringComparison.Ordinal))
        {
            state.Text = text;
            state.Cursor = Math.Clamp(state.Cursor, 0, text.Length);
            state.Anchor = -1;
            state.PreferredX = -1f;
        }

        ImGui.PushStyleColor(ImGuiCol.ChildBg, ImGui.GetColorU32(ImGuiCol.FrameBg));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, ImGui.GetStyle().FramePadding);
        ImGui.BeginChild(id, new Vector2(-1, height), true);
        var focused = ImGui.IsWindowFocused();
        if (focused)
        {
            ImGui.GetIO().WantTextInput = true;
            ImGuiP.SetActiveID(ImGui.GetID("##eh-caret"), ImGuiP.GetCurrentWindow());
            ImGuiP.SetActiveIdUsingNavAndKeys();
        }

        var lineHeight = Math.Max(1f, ImGui.GetTextLineHeight());
        var viewH = Math.Max(lineHeight, ImGui.GetContentRegionAvail().Y);
        var full = Math.Max(8f, ImGui.GetContentRegionAvail().X);
        var lines = Layout(text, full, Measure);
        if (lines.Count * lineHeight > viewH)
            lines = Layout(text, Math.Max(8f, full - ImGui.GetStyle().ScrollbarSize), Measure);

        var origin = ImGui.GetCursorScreenPos();
        var changed = focused && ApplyInput(state, ref text, lines, max, origin, lineHeight);
        if (changed)
        {
            state.Text = text;
            lines = Layout(text, full, Measure);
            if (lines.Count * lineHeight > viewH)
                lines = Layout(text, Math.Max(8f, full - ImGui.GetStyle().ScrollbarSize), Measure);
        }

        var total = Math.Max(lineHeight, lines.Count * lineHeight);
        ImGui.Dummy(new Vector2(1f, total));
        Draw(text, lines, state, origin, lineHeight, focused);
        if (state.Reveal)
        {
            var index = LineOf(lines, state.Cursor, text.Length);
            var y = index * lineHeight;
            var scroll = ImGui.GetScrollY();
            if (y < scroll)
                ImGui.SetScrollY(y);
            else if (y + lineHeight > scroll + viewH)
                ImGui.SetScrollY(y + lineHeight - viewH);
            state.Reveal = false;
        }

        ImGui.EndChild();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
        return changed;
    }

    internal static List<TextLine> Layout(string text, float width, Func<string, float> widthOf)
    {
        var lines = new List<TextLine>();
        var index = 0;
        while (index < text.Length)
        {
            var newline = text.IndexOf('\n', index);
            var hard = newline >= 0;
            var hardEnd = hard ? newline : text.Length;
            if (index == hardEnd)
            {
                lines.Add(new TextLine { Start = index, End = index, Next = hard ? index + 1 : index, Hard = hard });
            }
            else
            {
                var start = index;
                while (start < hardEnd)
                {
                    var end = Fit(text, start, hardEnd, width, widthOf);
                    var skipped = end < hardEnd && text[end] == ' ';
                    var next = skipped ? end + 1 : end;
                    var last = next >= hardEnd;
                    lines.Add(new TextLine
                    {
                        Start = start,
                        End = end,
                        Next = last && hard ? hardEnd + 1 : last ? hardEnd : next,
                        Hard = last && hard,
                    });
                    if (last)
                        break;
                    start = next;
                }
            }

            if (!hard)
                break;
            index = newline + 1;
        }

        if (lines.Count == 0 || text.EndsWith('\n'))
            lines.Add(new TextLine { Start = text.Length, End = text.Length, Next = text.Length, Hard = false });

        return lines;
    }

    private static int Fit(string text, int start, int end, float width, Func<string, float> widthOf)
    {
        if (start >= end)
            return start;
        if (widthOf(text[start..end]) <= width)
            return end;

        var last = start;
        var index = start;
        while (index < end)
        {
            int next;
            if (text[index] == ' ')
            {
                next = index + 1;
            }
            else
            {
                next = index;
                while (next < end && text[next] != ' ')
                    next++;
            }

            if (widthOf(text[start..next]) <= width)
            {
                last = next;
                index = next;
                continue;
            }

            if (last > start)
                return last;

            var take = start + 1;
            var lo = take;
            var hi = next;
            while (lo <= hi)
            {
                var mid = (lo + hi) / 2;
                if (mid > start && widthOf(text[start..mid]) <= width)
                {
                    take = mid;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return Math.Clamp(take, start + 1, end);
        }

        return end;
    }

    private static bool ApplyInput(BoxState state, ref string text, List<TextLine> lines, int max, Vector2 origin, float lineHeight)
    {
        var changed = false;
        var io = ImGui.GetIO();
        if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            var index = IndexAt(text, lines, origin, lineHeight, ImGui.GetMousePos());
            if (!io.KeyShift)
                state.Anchor = index;
            else if (state.Anchor < 0)
                state.Anchor = state.Cursor;
            state.Cursor = index;
            state.PreferredX = -1f;
            state.Dragging = true;
            state.Blink = ImGui.GetTime();
        }
        else if (state.Dragging && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            state.Cursor = IndexAt(text, lines, origin, lineHeight, ImGui.GetMousePos());
            state.Reveal = true;
        }

        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            state.Dragging = false;
        if (!state.Dragging && state.Anchor == state.Cursor)
            state.Anchor = -1;

        if (io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.A))
        {
            state.Anchor = 0;
            state.Cursor = text.Length;
            state.PreferredX = -1f;
        }
        else if (io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.C))
        {
            Copy(state, text);
        }
        else if (io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.X))
        {
            changed |= Cut(state, ref text);
        }
        else if (io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.V))
        {
            changed |= Paste(state, ref text, max);
        }
        else if (!io.KeyCtrl && (ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter)))
        {
            changed |= Insert(state, ref text, "\n", max);
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.Backspace))
        {
            changed |= Backspace(state, ref text);
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.Delete))
        {
            changed |= Delete(state, ref text);
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.LeftArrow))
        {
            var next = io.KeyCtrl ? WordLeft(text, state.Cursor) : Math.Max(0, state.Cursor - 1);
            if (!io.KeyShift && !io.KeyCtrl && Selected(state))
                next = Math.Min(state.Cursor, state.Anchor);
            Move(state, text, next, io.KeyShift, false);
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.RightArrow))
        {
            var next = io.KeyCtrl ? WordRight(text, state.Cursor) : Math.Min(text.Length, state.Cursor + 1);
            if (!io.KeyShift && !io.KeyCtrl && Selected(state))
                next = Math.Max(state.Cursor, state.Anchor);
            Move(state, text, next, io.KeyShift, false);
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.UpArrow))
        {
            Move(state, text, Vertical(text, lines, state, -1), io.KeyShift, true);
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.DownArrow))
        {
            Move(state, text, Vertical(text, lines, state, 1), io.KeyShift, true);
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.Home))
        {
            var line = lines[LineOf(lines, state.Cursor, text.Length)];
            Move(state, text, io.KeyCtrl ? 0 : line.Start, io.KeyShift, false);
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.End))
        {
            var line = lines[LineOf(lines, state.Cursor, text.Length)];
            Move(state, text, io.KeyCtrl ? text.Length : line.End, io.KeyShift, false);
        }
        else if (!io.KeyCtrl)
        {
            var queue = io.InputQueueCharacters;
            for (var n = 0; n < queue.Size; n++)
            {
                var ch = (char)queue[n];
                if (ch < ' ' || char.IsSurrogate(ch))
                    continue;
                changed |= Insert(state, ref text, ch.ToString(), max);
            }
        }

        state.Cursor = Math.Clamp(state.Cursor, 0, text.Length);
        if (state.Anchor > text.Length)
            state.Anchor = text.Length;
        return changed;
    }

    private static void Move(BoxState state, string text, int next, bool shift, bool keepColumn)
    {
        if (shift)
        {
            if (state.Anchor < 0)
                state.Anchor = state.Cursor;
        }
        else
        {
            state.Anchor = -1;
        }

        state.Cursor = Math.Clamp(next, 0, text.Length);
        if (!keepColumn)
            state.PreferredX = -1f;
        state.Reveal = true;
        state.Blink = ImGui.GetTime();
    }

    private static int Vertical(string text, List<TextLine> lines, BoxState state, int direction)
    {
        var index = LineOf(lines, state.Cursor, text.Length);
        var line = lines[index];
        if (state.PreferredX < 0f)
            state.PreferredX = CursorPixel(text, line, state.Cursor);
        var next = index + direction;
        if (next < 0)
            return 0;
        if (next >= lines.Count)
            return text.Length;
        return Place(text, lines[next], state.PreferredX);
    }

    private static bool Backspace(BoxState state, ref string text)
    {
        if (Selected(state))
            return RemoveSelection(state, ref text);
        if (state.Cursor <= 0)
            return false;

        text = text.Remove(state.Cursor - 1, 1);
        state.Cursor--;
        state.Blink = ImGui.GetTime();
        state.Reveal = true;
        return true;
    }

    private static bool Delete(BoxState state, ref string text)
    {
        if (Selected(state))
            return RemoveSelection(state, ref text);
        if (state.Cursor >= text.Length)
            return false;

        text = text.Remove(state.Cursor, 1);
        state.Blink = ImGui.GetTime();
        return true;
    }

    private static bool Insert(BoxState state, ref string text, string add, int max)
    {
        if (add.Length == 0)
            return false;
        if (Selected(state))
            RemoveSelection(state, ref text);
        var room = max - text.Length;
        if (room <= 0)
            return false;
        if (add.Length > room)
            add = add[..room];
        text = text.Insert(state.Cursor, add);
        state.Cursor += add.Length;
        state.PreferredX = -1f;
        state.Reveal = true;
        state.Blink = ImGui.GetTime();
        return true;
    }

    private static bool RemoveSelection(BoxState state, ref string text)
    {
        var start = Math.Min(state.Cursor, state.Anchor);
        var end = Math.Max(state.Cursor, state.Anchor);
        text = text.Remove(start, end - start);
        state.Cursor = start;
        state.Anchor = -1;
        state.Blink = ImGui.GetTime();
        state.Reveal = true;
        return true;
    }

    private static void Copy(BoxState state, string text)
    {
        if (!Selected(state))
            return;
        var start = Math.Min(state.Cursor, state.Anchor);
        var end = Math.Max(state.Cursor, state.Anchor);
        ImGui.SetClipboardText(text[start..end]);
    }

    private static bool Cut(BoxState state, ref string text)
    {
        if (!Selected(state))
            return false;
        Copy(state, text);
        return RemoveSelection(state, ref text);
    }

    private static bool Paste(BoxState state, ref string text, int max)
    {
        var clip = ImGui.GetClipboardText() ?? "";
        clip = clip.Replace("\r\n", "\n").Replace('\r', '\n');
        var kept = new System.Text.StringBuilder(clip.Length);
        foreach (var ch in clip)
        {
            if (ch == '\n' || ch >= ' ')
                kept.Append(ch);
        }

        return Insert(state, ref text, kept.ToString(), max);
    }

    private static void Draw(string text, List<TextLine> lines, BoxState state, Vector2 origin, float lineHeight, bool focused)
    {
        var draw = ImGui.GetWindowDrawList();
        var ink = ImGui.GetColorU32(ImGuiCol.Text);
        var select = ImGui.GetColorU32(ImGuiCol.TextSelectedBg);
        var selected = Selected(state);
        var from = selected ? Math.Min(state.Cursor, state.Anchor) : 0;
        var to = selected ? Math.Max(state.Cursor, state.Anchor) : 0;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var y = origin.Y + (i * lineHeight);
            if (selected)
            {
                var markFrom = Math.Max(from, line.Start);
                var markTo = Math.Min(to, Math.Max(line.End, line.Start));
                if (markFrom < markTo || (to > line.End && from <= line.End && line.End > line.Start))
                {
                    var left = markFrom <= line.Start ? origin.X : origin.X + Measure(text[line.Start..markFrom]);
                    var right = origin.X + Measure(text[line.Start..Math.Min(markTo, line.End)]);
                    if (right < left)
                        right = left;
                    draw.AddRectFilled(new Vector2(left, y), new Vector2(Math.Max(right, left + 2f), y + lineHeight), select);
                }
            }

            if (line.End > line.Start)
                draw.AddText(new Vector2(origin.X, y), ink, text[line.Start..line.End]);
        }

        if (!focused || selected || ((ImGui.GetTime() - state.Blink) % 1.0) >= 0.5)
            return;

        var cursorLine = lines[LineOf(lines, state.Cursor, text.Length)];
        var cursorX = origin.X + CursorPixel(text, cursorLine, state.Cursor);
        var cursorY = origin.Y + (LineOf(lines, state.Cursor, text.Length) * lineHeight);
        draw.AddRectFilled(new Vector2(cursorX, cursorY), new Vector2(cursorX + 1f, cursorY + lineHeight), ink);
    }

    private static int IndexAt(string text, List<TextLine> lines, Vector2 origin, float lineHeight, Vector2 mouse)
    {
        if (lines.Count == 0)
            return 0;
        var index = (int)Math.Floor((mouse.Y - origin.Y) / lineHeight);
        if (index < 0)
            return 0;
        if (index >= lines.Count)
            return text.Length;
        return Place(text, lines[index], mouse.X - origin.X);
    }

    private static int Place(string text, TextLine line, float x)
    {
        var best = line.Start;
        var bestDist = float.MaxValue;
        for (var i = line.Start; i <= line.End; i++)
        {
            var width = i == line.Start ? 0f : Measure(text[line.Start..i]);
            var dist = Math.Abs(width - x);
            if (dist < bestDist)
            {
                best = i;
                bestDist = dist;
            }
        }

        return best;
    }

    private static float CursorPixel(string text, TextLine line, int cursor)
    {
        var at = Math.Clamp(cursor, line.Start, line.End);
        return at <= line.Start ? 0f : Measure(text[line.Start..at]);
    }

    private static int LineOf(IReadOnlyList<TextLine> lines, int cursor, int length)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (cursor < lines[i].Next || (i == lines.Count - 1 && cursor <= length))
                return i;
        }

        return Math.Max(0, lines.Count - 1);
    }

    private static int WordLeft(string text, int cursor)
    {
        var index = Math.Max(0, cursor - 1);
        while (index > 0 && char.IsWhiteSpace(text[index]))
            index--;
        while (index > 0 && !char.IsWhiteSpace(text[index - 1]))
            index--;
        return index;
    }

    private static int WordRight(string text, int cursor)
    {
        var index = cursor;
        while (index < text.Length && char.IsWhiteSpace(text[index]))
            index++;
        while (index < text.Length && !char.IsWhiteSpace(text[index]))
            index++;
        return index;
    }

    private static bool Selected(BoxState state) => state.Anchor >= 0 && state.Anchor != state.Cursor;

    private static float Measure(string text) => text.Length == 0 ? 0f : ImGui.CalcTextSize(text, false, 0f).X;

    private sealed class BoxState
    {
        public string Text = "";

        public int Cursor;

        public int Anchor = -1;

        public float PreferredX = -1f;

        public bool Dragging;

        public bool Reveal;

        public double Blink;
    }
}
