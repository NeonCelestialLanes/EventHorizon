using System.Numerics;
using Dalamud.Bindings.ImGui;
using EventHorizon.Persistence;

namespace EventHorizon.Ui;

internal static class SettingsPanel
{
    private static readonly (string Id, string Label)[] Presets =
    [
        ("MidnightNeon", "Midnight Neon"),
        ("Afterword", "Afterword"),
        ("NclArcade", "NCL Arcade"),
    ];

    public static void Draw(HorizonApp app, MainWindow window)
    {
        var config = app.Store.Config;
        Chrome.Section("Theme");
        Chrome.Muted("Midnight Neon is the default. Changes apply immediately.");
        foreach (var (id, label) in Presets)
        {
            if (!ImGui.RadioButton(label, config.ThemePreset == id))
                continue;

            config.ThemePreset = id;
            app.Store.MarkConfig();
            app.Store.SaveDirty();
        }

        var accent = config.UseAccentOverride;
        if (ImGui.Checkbox("Custom accent color", ref accent))
        {
            config.UseAccentOverride = accent;
            app.Store.MarkConfig();
            app.Store.SaveDirty();
        }

        var color = new Vector4(config.AccentR, config.AccentG, config.AccentB, 1f);
        if (ImGui.ColorEdit4("Accent", ref color, ImGuiColorEditFlags.NoAlpha))
        {
            config.AccentR = color.X;
            config.AccentG = color.Y;
            config.AccentB = color.Z;
            config.UseAccentOverride = true;
            app.Store.MarkConfig();
            app.Store.SaveDirty();
        }

        var scale = config.UiScale;
        if (ImGui.SliderFloat("UI scale", ref scale, 0.8f, 1.6f, "%.2f"))
        {
            config.UiScale = scale;
            app.Store.MarkConfig();
            app.Store.SaveDirty();
        }

        var script = config.ScriptFontScale;
        if (ImGui.SliderFloat("Script text", ref script, 1f, 2.2f, "%.2f"))
        {
            config.ScriptFontScale = script;
            app.Store.MarkConfig();
            app.Store.SaveDirty();
        }

        var compact = config.CompactMode;
        if (ImGui.Checkbox("Compact spacing", ref compact))
        {
            config.CompactMode = compact;
            app.Store.MarkConfig();
            app.Store.SaveDirty();
        }

        var motion = config.Animations;
        if (ImGui.Checkbox("Subtle motion", ref motion))
        {
            config.Animations = motion;
            app.Store.MarkConfig();
            app.Store.SaveDirty();
        }

        var localTime = config.ShowComputerLocalTime;
        if (ImGui.Checkbox("Also show this computer's local time", ref localTime))
        {
            config.ShowComputerLocalTime = localTime;
            app.Store.MarkConfig();
            app.Store.SaveDirty();
        }

        var backups = config.BackupCount;
        if (ImGui.SliderInt("Backups to keep", ref backups, 1, 50))
        {
            config.BackupCount = backups;
            app.Store.MarkConfig();
            app.Store.SaveDirty();
        }

        Chrome.Section("Files");
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

        ImGui.Spacing();
        if (Chrome.Button("Import event"))
            window.Pick("Import event", path => app.BeginImport("event", path));
        ImGui.SameLine();
        if (Chrome.Button("Import game"))
            window.Pick("Import game", path => app.BeginImport("game", path));
        ImGui.SameLine();
        if (Chrome.Button("Import script"))
            window.Pick("Import script", path => app.BeginImport("script", path));

        SyncPanel.Draw(app);
        Chrome.Section("About");
        Chrome.Muted("Event Horizon " + ReleaseNotes.CurrentVersion());
        if (Chrome.Button("What's new"))
            window.ShowNews();
        ImGui.Spacing();
        Chrome.Muted("Event Horizon keeps events, games, and scripts in JSON. A new party game does not need a new plugin build when it fits the schema. Chat is never posted for you, and links are never opened.");
    }
}
