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
        draw.AddRectFilled(pos, pos + size, ImGui.GetColorU32(ThemeService.With(color, 0.18f)), 8f);
        draw.AddRect(pos, pos + size, ImGui.GetColorU32(ThemeService.With(color, 0.9f)), 8f, ImDrawFlags.None, 1f);
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
        var padY = Math.Max(14f, ImGui.GetStyle().FramePadding.Y * 2f);
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
        DrawPill(min, max, face);
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
        var width = Math.Max(1f, ImGui.GetContentRegionAvail().X);
        const float height = 40f;
        var pressed = ImGui.InvisibleButton(label, new Vector2(width, height));
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var draw = ImGui.GetWindowDrawList();
        Vector4 ink;
        if (selected)
        {
            draw.AddRectFilled(min, max, ImGui.GetColorU32(ThemeHolder.Accent), height * 0.5f);
            ink = Contrast(ThemeHolder.Accent);
        }
        else if (ImGui.IsItemHovered())
        {
            draw.AddRectFilled(min, max, ImGui.GetColorU32(ThemeHolder.ButtonHover), height * 0.5f);
            ink = ThemeHolder.Text;
        }
        else
        {
            ink = ThemeHolder.Muted;
        }

        const float markSize = 18f;
        var markMin = min + new Vector2(10f, (height - markSize) * 0.5f);
        var markMax = markMin + new Vector2(markSize, markSize);
        var tile = selected ? new Vector4(0f, 0f, 0f, 0.18f) : ThemeHolder.Button;
        draw.AddRectFilled(markMin, markMax, ImGui.GetColorU32(tile), 6f);
        DrawMark(draw, mark, markMin, markMax, ink);
        var textSize = ImGui.CalcTextSize(label);
        draw.AddText(new Vector2(markMax.X + 10f, min.Y + ((height - textSize.Y) * 0.5f)), ImGui.GetColorU32(ink), label);
        return pressed;
    }

    private static void DrawMark(ImDrawListPtr draw, string mark, Vector2 min, Vector2 max, Vector4 ink)
    {
        var color = ImGui.GetColorU32(ink);
        var center = (min + max) * 0.5f;
        switch (mark)
        {
            case "Dashboard":
                draw.AddRectFilled(center + new Vector2(-6f, -6f), center + new Vector2(-2f, -2f), color, 1f);
                draw.AddRectFilled(center + new Vector2(2f, -6f), center + new Vector2(6f, -2f), color, 1f);
                draw.AddRectFilled(center + new Vector2(-6f, 2f), center + new Vector2(-2f, 6f), color, 1f);
                draw.AddRectFilled(center + new Vector2(2f, 2f), center + new Vector2(6f, 6f), color, 1f);
                break;
            case "Events":
                draw.AddRect(center + new Vector2(-5.5f, -5f), center + new Vector2(5.5f, 6f), color, 2f, ImDrawFlags.None, 1.4f);
                draw.AddLine(center + new Vector2(-5.5f, -1.5f), center + new Vector2(5.5f, -1.5f), color, 1.4f);
                break;
            case "Live":
                draw.AddCircle(center, 6f, color, 16, 1.4f);
                draw.AddCircleFilled(center, 2.6f, color);
                break;
            case "Games":
                draw.AddTriangleFilled(center + new Vector2(0f, -6f), center + new Vector2(6f, 0f), center + new Vector2(0f, 6f), color);
                draw.AddTriangleFilled(center + new Vector2(0f, -6f), center + new Vector2(-6f, 0f), center + new Vector2(0f, 6f), color);
                break;
            case "Scripts":
                draw.AddLine(center + new Vector2(-5.5f, -4f), center + new Vector2(5.5f, -4f), color, 1.5f);
                draw.AddLine(center + new Vector2(-5.5f, 0f), center + new Vector2(3f, 0f), color, 1.5f);
                draw.AddLine(center + new Vector2(-5.5f, 4f), center + new Vector2(5.5f, 4f), color, 1.5f);
                break;
            case "Prizes":
                draw.AddRectFilled(center + new Vector2(-5.5f, -4.5f), center + new Vector2(5.5f, -2f), color, 1f);
                draw.AddRectFilled(center + new Vector2(-5f, -2f), center + new Vector2(5f, 5.5f), color, 1.5f);
                draw.AddLine(center + new Vector2(0f, -4.5f), center + new Vector2(0f, 5.5f), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.35f)), 1.2f);
                break;
            case "Notes":
                draw.AddRect(center + new Vector2(-5f, -6f), center + new Vector2(5f, 6f), color, 1.5f, ImDrawFlags.None, 1.4f);
                draw.AddLine(center + new Vector2(-2.5f, -2f), center + new Vector2(2.5f, -2f), color, 1.2f);
                draw.AddLine(center + new Vector2(-2.5f, 1.5f), center + new Vector2(2.5f, 1.5f), color, 1.2f);
                break;
            default:
                draw.AddCircle(center, 5f, color, 12, 1.4f);
                draw.AddCircleFilled(center, 1.6f, color);
                break;
        }
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
        if (!ImGui.InputTextMultiline(InputId(label), ref buffer, max, new Vector2(-1, height)))
            return false;

        set(buffer.Length == 0 ? null : buffer);
        return true;
    }

    public static bool RequiredArea(string label, string value, Action<string> set, float height, int max = 8000)
    {
        Caption(label);
        var buffer = value ?? "";
        if (!ImGui.InputTextMultiline(InputId(label), ref buffer, max, new Vector2(-1, height)))
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

    internal static void DrawPill(Vector2 min, Vector2 max, Vector4 face)
    {
        var draw = ImGui.GetWindowDrawList();
        var radius = Math.Max(8f, (max.Y - min.Y) * 0.5f);
        draw.AddRectFilled(
            min + new Vector2(0f, 3f),
            max + new Vector2(0f, 5f),
            ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.28f)),
            radius);
        draw.AddRectFilled(min, max, ImGui.GetColorU32(face), radius);
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
