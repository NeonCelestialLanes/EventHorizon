using System.Numerics;
using Dalamud.Bindings.ImGui;
using EventHorizon.Models;

namespace EventHorizon.Theme;

internal readonly record struct Palette(
    Vector4 Bg,
    Vector4 BgAlt,
    Vector4 Panel,
    Vector4 Text,
    Vector4 Muted,
    Vector4 Accent,
    Vector4 Cyan,
    Vector4 Pink,
    Vector4 Gold,
    Vector4 Mint,
    Vector4 Red,
    Vector4 Border,
    Vector4 Button,
    Vector4 ButtonHover,
    Vector4 Input,
    Vector4 Violet,
    Vector4 BorderStrong)
{
    public static Palette For(Persistence.HorizonConfig config)
    {
        var palette = config.ThemePreset switch
        {
            "Afterword" => Afterword(),
            "NclArcade" => Arcade(),
            _ => Midnight(),
        };

        if (!config.UseAccentOverride)
            return palette;

        return palette with { Accent = new Vector4(config.AccentR, config.AccentG, config.AccentB, 1f) };
    }

    public static Palette Midnight() => new(
        Bg: Hex("0B0D1A"),
        BgAlt: Hex("0F1226"),
        Panel: Hex("141831"),
        Text: Hex("E8EAFF"),
        Muted: Hex("9AA0C8"),
        Accent: Hex("38E8FF"),
        Cyan: Hex("38E8FF"),
        Pink: Hex("FF4FD8"),
        Gold: Hex("FFC76A"),
        Mint: Hex("3DFFA8"),
        Red: Hex("FF7086"),
        Border: Hex("262B4D"),
        Button: Hex("1B2050"),
        ButtonHover: Hex("262C66"),
        Input: Hex("0F1226"),
        Violet: Hex("8B5CF6"),
        BorderStrong: Hex("3B3F78"));

    public static Palette Afterword() => new(
        Bg: Hex("0D141E"),
        BgAlt: Hex("141C2A"),
        Panel: Hex("1C2636"),
        Text: Hex("F3EFE6"),
        Muted: Hex("9AA6B8"),
        Accent: Hex("C6A56A"),
        Cyan: Hex("7F9BB5"),
        Pink: Hex("C4899A"),
        Gold: Hex("C6A56A"),
        Mint: Hex("8EAE96"),
        Red: Hex("D4746A"),
        Border: Hex("3A4658", 0.85f),
        Button: Hex("243044"),
        ButtonHover: Hex("2E3D55"),
        Input: Hex("121924"),
        Violet: Hex("C6A56A"),
        BorderStrong: Hex("3A4658"));

    public static Palette Arcade() => new(
        Bg: Hex("140818"),
        BgAlt: Hex("1E1028"),
        Panel: Hex("2A1640"),
        Text: Hex("F4F7FF"),
        Muted: Hex("C7B6E4"),
        Accent: Hex("2EE6DC"),
        Cyan: Hex("2EE6DC"),
        Pink: Hex("FF4EBE"),
        Gold: Hex("FFE14A"),
        Mint: Hex("67F0B5"),
        Red: Hex("FF6B8A"),
        Border: Hex("5A3480", 0.9f),
        Button: Hex("3A1C62"),
        ButtonHover: Hex("4C2780"),
        Input: Hex("1A0C28"),
        Violet: Hex("FF4EBE"),
        BorderStrong: Hex("5A3480"));

    public static Vector4 Hex(string hex, float alpha = 1f)
    {
        var value = hex.TrimStart('#');
        var r = Convert.ToInt32(value[..2], 16) / 255f;
        var g = Convert.ToInt32(value[2..4], 16) / 255f;
        var b = Convert.ToInt32(value[4..6], 16) / 255f;
        return new Vector4(r, g, b, alpha);
    }
}

internal static class ThemeHolder
{
    public static Vector4 Accent { get; set; } = Palette.Midnight().Accent;

    public static Vector4 Muted { get; set; } = Palette.Midnight().Muted;

    public static Vector4 Text { get; set; } = Palette.Midnight().Text;

    public static Vector4 Pink { get; set; } = Palette.Midnight().Pink;

    public static Vector4 Cyan { get; set; } = Palette.Midnight().Cyan;

    public static Vector4 Gold { get; set; } = Palette.Midnight().Gold;

    public static Vector4 Mint { get; set; } = Palette.Midnight().Mint;

    public static Vector4 Red { get; set; } = Palette.Midnight().Red;

    public static Vector4 Panel { get; set; } = Palette.Midnight().Panel;

    public static Vector4 Button { get; set; } = Palette.Midnight().Button;

    public static Vector4 ButtonHover { get; set; } = Palette.Midnight().ButtonHover;

    public static Vector4 Violet { get; set; } = Palette.Midnight().Violet;

    public static Vector4 Border { get; set; } = Palette.Midnight().Border;

    public static Vector4 BorderStrong { get; set; } = Palette.Midnight().BorderStrong;

    public static Vector4 Bg { get; set; } = Palette.Midnight().Bg;

    public static Vector4 BgAlt { get; set; } = Palette.Midnight().BgAlt;

    public static bool Compact { get; set; }

    public static void Apply(Palette palette)
    {
        Bg = palette.Bg;
        BgAlt = palette.BgAlt;
        Accent = palette.Accent;
        Muted = palette.Muted;
        Text = palette.Text;
        Pink = palette.Pink;
        Cyan = palette.Cyan;
        Gold = palette.Gold;
        Mint = palette.Mint;
        Red = palette.Red;
        Panel = palette.Panel;
        Button = palette.Button;
        ButtonHover = palette.ButtonHover;
        Violet = palette.Violet;
        Border = palette.Border;
        BorderStrong = palette.BorderStrong;
    }
}

internal sealed class ThemeService
{
    private int colors;
    private int vars;

    public Palette Current { get; private set; } = Palette.Midnight();

    public void Push(Persistence.HorizonConfig config)
    {
        this.Current = Palette.For(config);
        ThemeHolder.Apply(this.Current);
        ThemeHolder.Compact = config.CompactMode;
        var palette = this.Current;
        var compact = config.CompactMode;
        this.colors = 0;
        this.vars = 0;

        this.Var(ImGuiStyleVar.WindowRounding, 8f);
        this.Var(ImGuiStyleVar.ChildRounding, 16f);
        this.Var(ImGuiStyleVar.FrameRounding, 6f);
        this.Var(ImGuiStyleVar.PopupRounding, 8f);
        this.Var(ImGuiStyleVar.GrabRounding, 6f);
        this.Var(ImGuiStyleVar.TabRounding, 6f);
        this.Var(ImGuiStyleVar.WindowBorderSize, 1f);
        this.Var(ImGuiStyleVar.ChildBorderSize, 1f);
        this.Var(ImGuiStyleVar.FrameBorderSize, 1f);
        this.Var(ImGuiStyleVar.WindowPadding, compact ? new Vector2(12, 10) : new Vector2(16, 14));
        this.Var(ImGuiStyleVar.FramePadding, compact ? new Vector2(8, 4) : new Vector2(10, 6));
        this.Var(ImGuiStyleVar.ItemSpacing, compact ? new Vector2(8, 6) : new Vector2(10, 8));
        this.Var(ImGuiStyleVar.ScrollbarRounding, 8f);
        this.Var(ImGuiStyleVar.ScrollbarSize, 12f);

        this.Col(ImGuiCol.Text, palette.Text);
        this.Col(ImGuiCol.TextDisabled, palette.Muted);
        this.Col(ImGuiCol.WindowBg, palette.Bg);
        this.Col(ImGuiCol.ChildBg, palette.Panel);
        this.Col(ImGuiCol.PopupBg, palette.Panel);
        this.Col(ImGuiCol.Border, palette.Border);
        this.Col(ImGuiCol.FrameBg, palette.Input);
        this.Col(ImGuiCol.FrameBgHovered, palette.ButtonHover);
        this.Col(ImGuiCol.FrameBgActive, palette.Button);
        this.Col(ImGuiCol.TitleBg, palette.BgAlt);
        this.Col(ImGuiCol.TitleBgActive, palette.Panel);
        this.Col(ImGuiCol.TitleBgCollapsed, palette.BgAlt);
        this.Col(ImGuiCol.ScrollbarBg, palette.Bg);
        this.Col(ImGuiCol.ScrollbarGrab, palette.ButtonHover);
        this.Col(ImGuiCol.ScrollbarGrabHovered, palette.Accent);
        this.Col(ImGuiCol.ScrollbarGrabActive, palette.Accent);
        this.Col(ImGuiCol.CheckMark, palette.Accent);
        this.Col(ImGuiCol.SliderGrab, palette.Accent);
        this.Col(ImGuiCol.SliderGrabActive, palette.Pink);
        this.Col(ImGuiCol.Button, palette.Button);
        this.Col(ImGuiCol.ButtonHovered, palette.ButtonHover);
        this.Col(ImGuiCol.ButtonActive, With(palette.Accent, 0.55f));
        this.Col(ImGuiCol.Header, With(palette.Accent, 0.28f));
        this.Col(ImGuiCol.HeaderHovered, With(palette.Accent, 0.4f));
        this.Col(ImGuiCol.HeaderActive, With(palette.Accent, 0.55f));
        this.Col(ImGuiCol.Separator, palette.Border);
        this.Col(ImGuiCol.Tab, palette.BgAlt);
        this.Col(ImGuiCol.TabHovered, palette.ButtonHover);
        this.Col(ImGuiCol.TabActive, palette.Panel);
        this.Col(ImGuiCol.TabUnfocused, palette.BgAlt);
        this.Col(ImGuiCol.TabUnfocusedActive, palette.Panel);
        this.Col(ImGuiCol.PlotHistogram, palette.Accent);
        this.Col(ImGuiCol.ResizeGrip, With(palette.Accent, 0.35f));
        this.Col(ImGuiCol.ResizeGripHovered, palette.Accent);
        this.Col(ImGuiCol.ResizeGripActive, palette.Pink);
    }

    public void Pop()
    {
        if (this.colors > 0)
            ImGui.PopStyleColor(this.colors);
        if (this.vars > 0)
            ImGui.PopStyleVar(this.vars);
        this.colors = 0;
        this.vars = 0;
    }

    public static Vector4 With(Vector4 color, float alpha) => new(color.X, color.Y, color.Z, alpha);

    public static Vector4 Status(Palette palette, EventStatus status) => status switch
    {
        EventStatus.Planned => palette.Cyan,
        EventStatus.Ready => palette.Mint,
        EventStatus.Live => palette.Pink,
        EventStatus.Completed => palette.Gold,
        EventStatus.Archived => palette.Muted,
        _ => palette.Muted,
    };

    public static Vector4 Progress(Palette palette, ActivityProgressStatus status) => status switch
    {
        ActivityProgressStatus.Live => palette.Pink,
        ActivityProgressStatus.Completed => palette.Mint,
        ActivityProgressStatus.Skipped => palette.Muted,
        ActivityProgressStatus.Delayed => palette.Gold,
        ActivityProgressStatus.Paused => palette.Cyan,
        _ => palette.Muted,
    };

    public static Vector4 Category(Palette palette, ActivityCategory category) => category switch
    {
        ActivityCategory.Welcome => palette.Gold,
        ActivityCategory.Announcement => palette.Cyan,
        ActivityCategory.DjSet => palette.Accent,
        ActivityCategory.Game => palette.Pink,
        ActivityCategory.Contest => palette.Mint,
        ActivityCategory.Giveaway => palette.Gold,
        ActivityCategory.Break => palette.Muted,
        ActivityCategory.Intermission => palette.Cyan,
        ActivityCategory.Closing => palette.Red,
        _ => palette.Accent,
    };

    private void Var(ImGuiStyleVar style, float value)
    {
        ImGui.PushStyleVar(style, value);
        this.vars++;
    }

    private void Var(ImGuiStyleVar style, Vector2 value)
    {
        ImGui.PushStyleVar(style, value);
        this.vars++;
    }

    private void Col(ImGuiCol color, Vector4 value)
    {
        ImGui.PushStyleColor(color, value);
        this.colors++;
    }
}
