using System.Numerics;
using Dalamud.Bindings.ImGui;
using EventHorizon.Models;
using EventHorizon.Theme;

namespace EventHorizon.Ui;

internal static class Chrome
{
    public static void Section(string title)
    {
        ImGui.TextColored(ThemeHolder.Accent, title);
        ImGui.PushStyleColor(ImGuiCol.Separator, ThemeService.With(ThemeHolder.Accent, 0.4f));
        ImGui.Separator();
        ImGui.PopStyleColor();
    }

    public static void Muted(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ThemeHolder.Muted);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    public static void Badge(string text, Vector4 color)
    {
        var pad = new Vector2(8f, 2f);
        var size = ImGui.CalcTextSize(text) + (pad * 2f);
        var pos = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var round = size.Y * 0.5f;
        draw.AddRectFilled(pos, pos + size, ImGui.GetColorU32(ThemeService.With(color, 0.18f)), round);
        draw.AddRect(pos, pos + size, ImGui.GetColorU32(ThemeService.With(color, 0.9f)), round, ImDrawFlags.None, 1f);
        draw.AddText(pos + pad, ImGui.GetColorU32(color), text);
        ImGui.Dummy(size);
    }

    public static bool Button(string label, Vector2 size = default, Vector4? fill = null, Vector4? ink = null, Vector4? hover = null)
    {
        var visible = label;
        var marker = visible.IndexOf("##", StringComparison.Ordinal);
        if (marker >= 0)
            visible = visible[..marker];

        var textSize = ImGui.CalcTextSize(visible);
        var padX = Math.Max(28f, ImGui.GetStyle().FramePadding.X * 2f);
        var padY = Math.Max(ThemeHolder.Compact ? 18f : 28f, ImGui.GetStyle().FramePadding.Y * 2f);
        if (size.Y < textSize.Y + padY)
            size.Y = textSize.Y + padY;
        if (size.X < 0f)
            size.X = Math.Max(textSize.X + padX, ImGui.GetContentRegionAvail().X);
        else if (size.X < textSize.X + padX)
            size.X = textSize.X + padX;

        var pressed = ImGui.InvisibleButton(label, size);
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var resting = fill ?? ThemeHolder.Button;
        var over = hover ?? (fill is Vector4 custom ? Lighten(custom, 0.06f) : ThemeHolder.ButtonHover);
        var held = ImGui.IsItemActive();
        var face = held ? Darken(resting) : ImGui.IsItemHovered() ? over : resting;
        DrawPill(min, max, face, fill is null ? ThemeHolder.BorderStrong : null);
        var textPos = min + ((max - min - textSize) * 0.5f);
        if (held)
            textPos.Y += 1f;
        ImGui.GetWindowDrawList().AddText(textPos, ImGui.GetColorU32(ink ?? ThemeHolder.Text), visible);
        return pressed;
    }

    public static bool Accent(string label, Vector2 size = default)
    {
        var fill = ThemeHolder.Accent;
        return Button(label, size, fill, Contrast(fill), Lighten(fill, 0.08f));
    }

    public static bool Tone(string label, Vector4 color)
    {
        return Button(label, default, color, Contrast(color), Lighten(color, 0.06f));
    }

    public static bool Nav(string label, bool selected, string mark)
    {
        _ = mark;
        var width = Math.Max(1f, ImGui.GetContentRegionAvail().X);
        const float height = 44f;
        var pressed = ImGui.InvisibleButton(label, new Vector2(width, height));
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var draw = ImGui.GetWindowDrawList();
        var ink = ThemeHolder.Muted;
        if (selected)
        {
            var left = ImGui.GetColorU32(ThemeService.With(ThemeHolder.Violet, 0.28f));
            var right = ImGui.GetColorU32(ThemeService.With(ThemeHolder.Cyan, 0.10f));
            draw.AddRectFilledMultiColor(min, max, left, right, right, left);
            draw.AddRect(min, max, ImGui.GetColorU32(ThemeHolder.BorderStrong), 10f, ImDrawFlags.None, 1f);
            ink = ThemeHolder.Text;
        }
        else if (ImGui.IsItemHovered())
        {
            draw.AddRectFilled(min, max, ImGui.GetColorU32(ThemeHolder.ButtonHover), 10f);
            ink = ThemeHolder.Text;
        }

        var textSize = ImGui.CalcTextSize(label);
        draw.AddText(new Vector2(min.X + 12f, min.Y + ((height - textSize.Y) * 0.5f)), ImGui.GetColorU32(ink), label);
        return pressed;
    }

    public static bool Field(string label, string? value, Action<string?> set, int max = 240)
    {
        Caption(label);
        var buffer = value ?? "";
        ImGui.SetNextItemWidth(BoxWidth());
        if (!ImGui.InputText(InputId(label), ref buffer, max))
            return false;

        set(string.IsNullOrWhiteSpace(buffer) ? null : buffer);
        return true;
    }

    public static bool Required(string label, string value, Action<string> set, int max = 240)
    {
        Caption(label);
        var buffer = value ?? "";
        ImGui.SetNextItemWidth(BoxWidth());
        if (!ImGui.InputText(InputId(label), ref buffer, max))
            return false;

        set(buffer);
        return true;
    }

    public static bool Area(string label, string? value, Action<string?> set, float height, int max = 8000)
    {
        Caption(label);
        var buffer = value ?? "";
        if (!WrappedBox.Edit(InputId(label), ref buffer, max, height))
            return false;

        set(buffer.Length == 0 ? null : buffer);
        return true;
    }

    public static bool RequiredArea(string label, string value, Action<string> set, float height, int max = 8000)
    {
        Caption(label);
        var buffer = value ?? "";
        if (!WrappedBox.Edit(InputId(label), ref buffer, max, height))
            return false;

        set(buffer);
        return true;
    }

    public static bool EnumBox<T>(string label, ref T value)
        where T : struct, Enum
    {
        Caption(label);
        var changed = false;
        ImGui.SetNextItemWidth(BoxWidth());
        if (!ImGui.BeginCombo(InputId(label), Persistence.Labels.Spaced(value.ToString() ?? "")))
            return false;

        foreach (var option in Enum.GetValues<T>())
        {
            if (ImGui.Selectable(Persistence.Labels.Spaced(option.ToString() ?? ""), option.Equals(value)))
            {
                value = option;
                changed = true;
            }
        }

        ImGui.EndCombo();
        return changed;
    }

    private static void Caption(string label)
    {
        var visible = label;
        var cut = visible.IndexOf("##", StringComparison.Ordinal);
        if (cut >= 0)
            visible = visible[..cut];
        if (visible.Length == 0)
            return;

        ImGui.TextDisabled(visible);
    }

    private static string InputId(string label) =>
        label.Contains("##", StringComparison.Ordinal) ? label : "##" + label;

    private static float BoxWidth() => Math.Max(160f, ImGui.GetContentRegionAvail().X);

    public static Vector4 Contrast(Vector4 color)
    {
        var luminance = (0.2126f * color.X) + (0.7152f * color.Y) + (0.0722f * color.Z);
        return luminance > 0.62f ? new Vector4(0.08f, 0.07f, 0.12f, 1f) : new Vector4(0.97f, 0.97f, 0.99f, 1f);
    }

    internal static void DrawPill(Vector2 min, Vector2 max, Vector4 face, Vector4? edge = null)
    {
        var draw = ImGui.GetWindowDrawList();
        var radius = Math.Min(12f, Math.Max(8f, (max.Y - min.Y) * 0.5f));
        draw.AddRectFilled(min, max, ImGui.GetColorU32(face), radius);
        if (edge is Vector4 line)
            draw.AddRect(min, max, ImGui.GetColorU32(line), radius, ImDrawFlags.None, 1f);
    }

    public static float ButtonHeight() => ImGui.GetTextLineHeight() + (ThemeHolder.Compact ? 18f : 28f);

    public static float ButtonWidth(string label)
    {
        var visible = label;
        var marker = visible.IndexOf("##", StringComparison.Ordinal);
        if (marker >= 0)
            visible = visible[..marker];
        return ImGui.CalcTextSize(visible).X + Math.Max(28f, ImGui.GetStyle().FramePadding.X * 2f);
    }

    public static void Dashes(Vector2 min, Vector2 max, Vector4 color)
    {
        var draw = ImGui.GetWindowDrawList();
        var ink = ImGui.GetColorU32(color);
        const float dash = 6f;
        const float gap = 4f;
        for (var x = min.X; x < max.X; x += dash + gap)
        {
            var end = Math.Min(x + dash, max.X);
            draw.AddLine(new Vector2(x, min.Y), new Vector2(end, min.Y), ink, 1f);
            draw.AddLine(new Vector2(x, max.Y), new Vector2(end, max.Y), ink, 1f);
        }

        for (var y = min.Y; y < max.Y; y += dash + gap)
        {
            var end = Math.Min(y + dash, max.Y);
            draw.AddLine(new Vector2(min.X, y), new Vector2(min.X, end), ink, 1f);
            draw.AddLine(new Vector2(max.X, y), new Vector2(max.X, end), ink, 1f);
        }
    }

    public static void Title(string text)
    {
        ImGui.SetWindowFontScale(1.2f);
        ImGui.TextUnformatted(text);
        ImGui.SetWindowFontScale(1f);
    }

    public static void CodeWell(string text, Vector4? ink = null)
    {
        var width = Math.Max(1f, ImGui.GetContentRegionAvail().X);
        var height = ImGui.GetTextLineHeight() * 2.6f;
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(width, height);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(min, max, ImGui.GetColorU32(ThemeHolder.BgAlt), 12f);
        Dashes(min, max, ThemeHolder.BorderStrong);
        var size = ImGui.CalcTextSize(text);
        draw.AddText(min + new Vector2((width - size.X) * 0.5f, (height - size.Y) * 0.5f), ImGui.GetColorU32(ink ?? ThemeHolder.Cyan), text);
        ImGui.Dummy(new Vector2(width, height));
    }

    private static Vector4 Lighten(Vector4 color, float amount) =>
        new(
            Math.Clamp(color.X + amount, 0f, 1f),
            Math.Clamp(color.Y + amount, 0f, 1f),
            Math.Clamp(color.Z + amount, 0f, 1f),
            color.W);

    private static Vector4 Darken(Vector4 color) => Mix(color, new Vector4(0f, 0f, 0f, color.W), 0.22f);

    internal static Vector4 Mix(Vector4 a, Vector4 b, float t) =>
        new(
            a.X + ((b.X - a.X) * t),
            a.Y + ((b.Y - a.Y) * t),
            a.Z + ((b.Z - a.Z) * t),
            1f);
}
