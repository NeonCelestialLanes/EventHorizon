using System.Numerics;
using Dalamud.Bindings.ImGui;
using EventHorizon.Persistence;
using EventHorizon.Theme;

namespace EventHorizon.Ui;

internal static class SettingsPanel
{
    private static readonly (string Id, string Label, Vector4 A, Vector4 B, Vector4 C)[] Presets =
    [
        ("MidnightNeon", "Midnight Neon", Palette.Hex("0B0D1A"), Palette.Hex("8B5CF6"), Palette.Hex("38E8FF")),
        ("Afterword", "Afterword", Palette.Afterword().Bg, Palette.Afterword().Accent, Palette.Afterword().Cyan),
        ("NclArcade", "NCL Arcade", Palette.Arcade().Bg, Palette.Arcade().Pink, Palette.Arcade().Accent),
    ];

    private static readonly (string Name, Vector4 Color)[] Accents =
    [
        ("Cyan", Palette.Hex("38E8FF")),
        ("Violet", Palette.Hex("8B5CF6")),
        ("Magenta", Palette.Hex("FF4FD8")),
        ("Mint", Palette.Hex("3DFFA8")),
    ];

    public static void Draw(HorizonApp app, MainWindow window)
    {
        var config = app.Store.Config;
        ImGui.SetWindowFontScale(config.UiScale * 1.6f);
        ImGui.TextUnformatted("Settings");
        ImGui.SetWindowFontScale(config.UiScale);

        var room = ImGui.GetContentRegionAvail();
        var side = Math.Min(340f, Math.Max(220f, room.X * 0.34f));
        var body = Math.Max(160f, room.Y - 8f);
        ImGui.BeginChild("eh-settings-main", new Vector2(Math.Max(80f, room.X - side - 12f), body), false);
        ThemeCard(app, config);
        ImGui.Spacing();
        DisplayCard(app, config);
        ImGui.Spacing();
        var line = ImGui.GetTextLineHeightWithSpacing();
        ImGui.BeginChild("eh-files-card", new Vector2(0, (line * 3.4f) + (Chrome.ButtonHeight() * 2f) + 56f), true);
        Chrome.Title("Data and files");
        Files(app, window);
        ImGui.EndChild();
        ImGui.EndChild();
        ImGui.SameLine();
        ImGui.BeginChild("eh-settings-sync", new Vector2(0, body), true);
        Chrome.Title("Live sync");
        Chrome.Muted("Share a code with other planners. Edits go both ways. Chat is never posted.");
        ImGui.Spacing();
        SyncPanel.Draw(app);
        var spare = ImGui.GetContentRegionAvail().Y - (line * 3.4f);
        if (spare > 0f)
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + spare);
        Chrome.Muted("Short codes work on the same network. If Windows asks, allow Event Horizon on private networks.");
        ImGui.EndChild();
    }

    private static void ThemeCard(HorizonApp app, HorizonConfig config)
    {
        ImGui.BeginChild("eh-theme-card", new Vector2(0, 210), true);
        Chrome.Title("Theme");
        var width = Math.Max(72f, (ImGui.GetContentRegionAvail().X - 16f) / Presets.Length);
        foreach (var preset in Presets)
        {
            ImGui.PushID(preset.Id);
            var selected = config.ThemePreset == preset.Id;
            if (ImGui.InvisibleButton("tile", new Vector2(width, 78f)))
            {
                config.ThemePreset = preset.Id;
                if (selected)
                    config.UseAccentOverride = false;
                Save(app);
            }

            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            var draw = ImGui.GetWindowDrawList();
            draw.AddRectFilled(min, max, ImGui.GetColorU32(ThemeHolder.Button), 12f);
            draw.AddRect(min, max, ImGui.GetColorU32(selected ? ThemeHolder.Accent : ThemeHolder.BorderStrong), 12f, ImDrawFlags.None, selected ? 2f : 1f);
            var swatch = min + new Vector2(10f, 12f);
            draw.AddRectFilled(swatch, swatch + new Vector2(18f, 12f), ImGui.GetColorU32(preset.A), 3f);
            draw.AddRectFilled(swatch + new Vector2(22f, 0f), swatch + new Vector2(40f, 12f), ImGui.GetColorU32(preset.B), 3f);
            draw.AddRectFilled(swatch + new Vector2(44f, 0f), swatch + new Vector2(62f, 12f), ImGui.GetColorU32(preset.C), 3f);
            draw.AddText(min + new Vector2(10f, 48f), ImGui.GetColorU32(ThemeHolder.Text), preset.Label);
            ImGui.SameLine();
            ImGui.PopID();
        }

        ImGui.NewLine();
        ImGui.TextDisabled("Accent color");
        foreach (var accent in Accents)
        {
            ImGui.PushID(accent.Name);
            if (ImGui.InvisibleButton("swatch", new Vector2(36f, 36f)))
            {
                config.AccentR = accent.Color.X;
                config.AccentG = accent.Color.Y;
                config.AccentB = accent.Color.Z;
                config.UseAccentOverride = true;
                Save(app);
            }

            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            var center = (min + max) * 0.5f;
            var draw = ImGui.GetWindowDrawList();
            var chosen = config.UseAccentOverride && Near(config, accent.Color);
            draw.AddCircleFilled(center, 14f, ImGui.GetColorU32(accent.Color));
            if (chosen)
                draw.AddCircle(center, 17f, ImGui.GetColorU32(ThemeHolder.Text), 24, 2f);
            ImGui.SameLine();
            ImGui.PopID();
        }

        ImGui.EndChild();
    }

    private static void DisplayCard(HorizonApp app, HorizonConfig config)
    {
        var line = ImGui.GetTextLineHeightWithSpacing();
        var frame = ImGui.GetFrameHeightWithSpacing();
        var height = (line * 1.4f) + (frame * 3f) + (3f * ((line * 2f) + 10f)) + 40f;
        ImGui.BeginChild("eh-display-card", new Vector2(0, height), true);
        Chrome.Title("Display");
        var scale = config.UiScale;
        if (LabeledSlider("UI scale", ref scale, 0.8f, 1.6f))
        {
            config.UiScale = scale;
            Save(app);
        }

        var script = config.ScriptFontScale;
        if (LabeledSlider("Script text size", ref script, 1f, 2.2f))
        {
            config.ScriptFontScale = script;
            Save(app);
        }

        var compact = config.CompactMode;
        if (Switch("Compact spacing", "Tighter rows and padding", ref compact))
        {
            config.CompactMode = compact;
            Save(app);
        }

        var motion = config.Animations;
        if (Switch("Motion", "Banner flash and progress animation", ref motion))
        {
            config.Animations = motion;
            Save(app);
        }

        var localTime = config.ShowComputerLocalTime;
        if (Switch("Local time", "Also show this computer's clock", ref localTime))
        {
            config.ShowComputerLocalTime = localTime;
            Save(app);
        }

        var backups = config.BackupCount;
        if (ImGui.SliderInt("Backups to keep", ref backups, 1, 50))
        {
            config.BackupCount = backups;
            Save(app);
        }

        ImGui.EndChild();
    }

    private static void Files(HorizonApp app, MainWindow window)
    {
        Chrome.Muted(app.Store.Root);
        if (Chrome.Button("Open data folder"))
            app.OpenPath(app.Store.Root);
        ImGui.SameLine();
        if (Chrome.Button("Validate JSON"))
            window.Pick("Validate JSON", path => app.BeginImport("validate", path));
        ImGui.SameLine();
        if (Chrome.Button("Reload all"))
        {
            app.Ask("Reload every event, game, and script from disk? Unsaved edits will be discarded. Live progress files stay unless a reload finds stale IDs.", () =>
            {
                app.Store.LoadAll();
                app.Note("Reloaded data from disk.");
            });
        }

        if (Chrome.Button("Import event"))
            window.Pick("Import event", path => app.BeginImport("event", path));
        ImGui.SameLine();
        if (Chrome.Button("Import game"))
            window.Pick("Import game", path => app.BeginImport("game", path));
        ImGui.SameLine();
        if (Chrome.Button("Import script"))
            window.Pick("Import script", path => app.BeginImport("script", path));
        ImGui.SameLine();
        if (Chrome.Button("What's new"))
            window.ShowNews();
    }

    private static bool LabeledSlider(string label, ref float value, float min, float max)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        ImGui.SameLine(170f);
        ImGui.SetNextItemWidth(Math.Max(120f, ImGui.GetContentRegionAvail().X - 8f));
        return ImGui.SliderFloat("##" + label, ref value, min, max, "%.2f");
    }

    private static bool Switch(string label, string hint, ref bool value)
    {
        var start = ImGui.GetCursorPos();
        var width = ImGui.GetContentRegionAvail().X;
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, 2f));
        ImGui.TextUnformatted(label);
        ImGui.TextDisabled(hint);
        ImGui.PopStyleVar();
        var after = ImGui.GetCursorPos();
        ImGui.SetCursorPos(new Vector2(start.X + Math.Max(60f, width - 48f), start.Y + 2f));
        var pressed = ImGui.InvisibleButton(label + "##switch", new Vector2(44f, 24f));
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var draw = ImGui.GetWindowDrawList();
        var track = value ? ThemeHolder.Accent : ThemeHolder.Button;
        draw.AddRectFilled(min, max, ImGui.GetColorU32(track), 12f);
        var knob = value ? max.X - 14f : min.X + 14f;
        draw.AddCircleFilled(new Vector2(knob, (min.Y + max.Y) * 0.5f), 8f, ImGui.GetColorU32(value ? Chrome.Contrast(ThemeHolder.Accent) : ThemeHolder.Muted));
        ImGui.SetCursorPos(after);
        ImGui.Dummy(new Vector2(1f, 6f));
        if (pressed)
            value = !value;
        return pressed;
    }

    private static bool Near(HorizonConfig config, Vector4 color) =>
        Math.Abs(config.AccentR - color.X) < 0.02f
        && Math.Abs(config.AccentG - color.Y) < 0.02f
        && Math.Abs(config.AccentB - color.Z) < 0.02f;

    private static void Save(HorizonApp app)
    {
        app.Store.MarkConfig();
        app.Store.SaveDirty();
    }
}
