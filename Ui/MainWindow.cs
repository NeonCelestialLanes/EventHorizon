using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using EventHorizon.Models;
using EventHorizon.Persistence;
using EventHorizon.Theme;

namespace EventHorizon.Ui;

internal sealed class MainWindow : Window
{
    private readonly HorizonApp app;

    public MainWindow(HorizonApp app)
        : base("Event Horizon")
    {
        this.app = app;
        this.Size = new Vector2(1180, 760);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(880, 560),
            MaximumSize = new Vector2(2400, 1600),
        };
    }

    public FileDialogManager Dialogs { get; } = new();

    public override void Draw()
    {
        ImGui.SetWindowFontScale(this.app.Store.Config.UiScale);
        DueBanner.Draw(this.app);
        this.Header();
        var sidebar = this.app.Store.Config.CompactMode ? 188f : 210f;
        ImGui.PushStyleColor(ImGuiCol.ChildBg, ThemeHolder.Panel);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 16f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8f, 10f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8f, 6f));
        ImGui.BeginChild("eh-nav", new Vector2(sidebar, 0), false);
        this.Nav("Dashboard", "Dashboard");
        this.Nav("My Events", "Events");
        this.Nav("Live Host", "Live");
        this.Nav("Game Library", "Games");
        this.Nav("Scripts", "Scripts");
        this.Nav("Prizes & Winners", "Prizes");
        this.Nav("Notes", "Notes");
        var settingsY = ImGui.GetCursorPosY();
        var bottom = ImGui.GetContentRegionAvail().Y + settingsY - 46f;
        if (bottom > settingsY)
            ImGui.SetCursorPosY(bottom);
        this.Nav("Settings", "Settings");
        ImGui.EndChild();
        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.BeginChild("eh-page", new Vector2(0, 0), false);
        switch (this.app.Store.Config.Section)
        {
            case "Events":
                EventsPanel.Draw(this.app, this);
                break;
            case "Live":
                LiveHostPanel.Draw(this.app, poppedOut: false);
                break;
            case "Games":
                LibraryPanel.DrawGames(this.app, this);
                break;
            case "Scripts":
                LibraryPanel.DrawScripts(this.app, this);
                break;
            case "Prizes":
                PrizePanel.Draw(this.app, this.app.Store.SelectedEvent());
                break;
            case "Notes":
                NotesPanel.Draw(this.app, this.app.Store.SelectedEvent(), generalToggle: true);
                break;
            case "Settings":
                SettingsPanel.Draw(this.app, this);
                break;
            default:
                DashboardPanel.Draw(this.app);
                break;
        }

        this.StatusLine();
        ImGui.EndChild();
        this.Popups();
        ImGui.SetWindowFontScale(1f);
    }

    public void Pick(string title, Action<string> chosen)
    {
        this.Dialogs.OpenFileDialog(title, "JSON{.json}", (ok, path) =>
        {
            if (ok && !string.IsNullOrWhiteSpace(path))
                chosen(path);
        });
    }

    public void SaveAs(string title, string name, Action<string> chosen)
    {
        this.Dialogs.SaveFileDialog(title, "JSON{.json}", name, "json", (ok, path) =>
        {
            if (ok && !string.IsNullOrWhiteSpace(path))
                chosen(path);
        });
    }

    private void Header()
    {
        ImGui.TextColored(ThemeHolder.Accent, "EVENT HORIZON");
        ImGui.SameLine();
        ImGui.TextDisabled("Live event desk");
        var selected = this.app.Store.SelectedEvent();
        if (selected is not null)
        {
            ImGui.SameLine();
            ImGui.TextUnformatted(selected.EventName);
            ImGui.SameLine();
            Chrome.Badge(Labels.Spaced(selected.Status.ToString()), ThemeService.Status(this.app.Theme.Current, selected.Status));
        }

        ImGui.Spacing();
    }

    private void Nav(string label, string section)
    {
        if (!Chrome.Nav(label, this.app.Store.Config.Section == section, section))
            return;

        this.app.Store.Config.Section = section;
        this.app.Store.MarkConfig();
        this.app.Store.SaveDirty();
    }

    private void StatusLine()
    {
        if (!string.IsNullOrWhiteSpace(this.app.Session.Error))
        {
            ImGui.Spacing();
            ImGui.TextColored(ThemeHolder.Red, this.app.Session.Error);
        }
        else if (!string.IsNullOrWhiteSpace(this.app.Session.Status))
        {
            ImGui.Spacing();
            ImGui.TextColored(ThemeHolder.Mint, this.app.Session.Status);
        }
    }

    private void Popups()
    {
        var confirm = this.app.Session.Confirm;
        if (confirm is not null)
            ImGui.OpenPopup("Confirm##eh");

        if (ImGui.BeginPopupModal("Confirm##eh", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.PushTextWrapPos(420f);
            ImGui.TextUnformatted(confirm?.Body ?? "");
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            if (Chrome.Accent("Confirm"))
            {
                confirm?.Run?.Invoke();
                this.app.Session.Confirm = null;
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (Chrome.Button("Cancel"))
            {
                this.app.Session.Confirm = null;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        var import = this.app.Session.Import;
        if (import is not null)
            ImGui.OpenPopup("Import##eh");

        if (!ImGui.BeginPopupModal("Import##eh", ImGuiWindowFlags.AlwaysAutoResize))
            return;

        ImGui.TextUnformatted(import?.ReportOnly == false && import.Choosing ? "An item with this ID already exists." : "JSON check");
        if (import?.Id is not null)
            ImGui.TextDisabled(import.Id);
        foreach (var error in import?.Errors ?? [])
            ImGui.TextColored(ThemeHolder.Red, error);
        foreach (var warning in import?.Warnings ?? [])
            ImGui.TextColored(ThemeHolder.Gold, warning);
        if (import is { ReportOnly: false, Choosing: true, Errors.Count: 0 })
        {
            if (Chrome.Accent("Replace"))
            {
                this.app.FinishImport(Persistence.ImportChoice.Replace);
                if (this.app.Session.Import is { Choosing: false })
                    ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (Chrome.Button("Import as copy"))
            {
                this.app.FinishImport(Persistence.ImportChoice.Copy);
                if (this.app.Session.Import is { Choosing: false })
                    ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
        }

        if (Chrome.Button("Close"))
        {
            this.app.Session.Import = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }
}

internal static class Banners
{
    public static void Draw(HorizonApp app, EventDocument ev)
    {
        if (app.Store.StateIsCorrupt(ev.EventId))
        {
            ImGui.TextColored(ThemeHolder.Red, "Live progress could not be read, so it was not overwritten.");
            if (Chrome.Tone("Reset live progress", ThemeHolder.Red))
            {
                app.Ask("Replace the unreadable progress file with a fresh one? A backup is kept.", () =>
                {
                    app.Store.ResetCorruptState(ev.EventId);
                    app.Note("Live progress was reset.");
                });
            }

            ImGui.Spacing();
        }

        if (!app.Store.StateWarnings.TryGetValue(ev.EventId, out var issues) || issues.Count == 0)
            return;

        Chrome.Section("Progress needs a decision");
        ImGui.TextWrapped("Live progress mentions IDs that are not in the current event file. Nothing was overwritten.");
        foreach (var issue in issues.Take(8))
            ImGui.TextWrapped(issue);
        if (Chrome.Tone("Drop missing references", ThemeHolder.Gold))
        {
            app.Store.DropStaleProgress(ev.EventId);
            app.Note("Missing progress references were dropped. Matching progress was kept.");
        }

        ImGui.Spacing();
    }
}
