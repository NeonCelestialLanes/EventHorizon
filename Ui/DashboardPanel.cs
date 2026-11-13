using System.Numerics;
using Dalamud.Bindings.ImGui;
using EventHorizon.Hosting;
using EventHorizon.Models;
using EventHorizon.Persistence;
using EventHorizon.Theme;

namespace EventHorizon.Ui;

internal static class DashboardPanel
{
    public static void Draw(HorizonApp app)
    {
        var selected = app.Store.SelectedEvent();
        if (selected is not null)
            Active(app, selected);

        SyncPanel.Draw(app);
        Chrome.Section("Upcoming");
        var showArchived = app.Session.ShowArchived;
        if (ImGui.Checkbox("Show archived", ref showArchived))
            app.Session.ShowArchived = showArchived;

        var events = app.Store.Events.Values
            .Where(ev => app.Session.ShowArchived || ev.Status != EventStatus.Archived)
            .OrderBy(ev => ev.Date, StringComparer.Ordinal)
            .ThenBy(ev => ev.StartTime, StringComparer.Ordinal)
            .ToList();
        if (events.Count == 0)
        {
            Chrome.Muted("No events yet. Create one in My Events, or import a JSON file.");
            return;
        }

        Errors(app);
        var avail = StableWidth();
        var gap = 12f;
        var columns = Math.Max(1, (int)((avail + gap) / 312f));
        var width = (avail - (gap * (columns - 1))) / columns;
        for (var index = 0; index < events.Count; index++)
        {
            if (index % columns != 0)
                ImGui.SameLine(0f, gap);
            Card(app, events[index], width);
        }
    }

    private static void Active(HorizonApp app, EventDocument ev)
    {
        Chrome.Section("Selected event");
        Banners.Draw(app, ev);
        var state = app.Store.StateFor(ev.EventId);
        var now = DateTimeOffset.Now;
        var current = ScheduleOrder.Current(ev, state);
        var next = ScheduleOrder.NextAfterCurrent(ev, state);
        var style = ImGui.GetStyle();
        var line = ImGui.GetTextLineHeightWithSpacing();
        var button = ImGui.GetTextLineHeight() + Math.Max(14f, style.FramePadding.Y * 2f);
        var inner = Math.Max(40f, StableWidth() - (style.WindowPadding.X * 2f));
        var meta = EventClock.FormatDate(ev.Date) + "  ·  " + EventClock.FormatClock(ev.Date, ev.StartTime, ev.Timezone) + "  ·  " + Labels.Location(ev);
        var metaHeight = ImGui.CalcTextSize(meta, false, inner).Y;
        var lines = next is null ? 3 : 4;
        var height = (style.WindowPadding.Y * 2f) + (line * lines) + metaHeight + 14f + button + 36f;
        var buttonRow = PillWidth(style, "Enter Live Host Mode") + PillWidth(style, "Teleport") + PillWidth(style, "Reset party") + (style.ItemSpacing.X * 2f);
        if (buttonRow > inner)
            height += button + style.ItemSpacing.Y;
        ImGui.BeginChild("eh-active", new Vector2(0, height), true);
        ImGui.TextUnformatted(ev.EventName);
        ImGui.SameLine();
        Chrome.Badge(state.Hosting ? "Hosting" : Labels.Spaced(ev.Status.ToString()), state.Hosting ? ThemeHolder.Pink : ThemeService.Status(app.Theme.Current, ev.Status));
        Chrome.Muted(EventClock.FormatDate(ev.Date) + "  ·  " + EventClock.FormatClock(ev.Date, ev.StartTime, ev.Timezone) + "  ·  " + Labels.Location(ev));
        var done = ScheduleOrder.SpokenCompleted(ev, state);
        var total = ScheduleOrder.Spoken(ev).Count;
        ImGui.ProgressBar(total == 0 ? 0f : done / (float)total, new Vector2(-1, 14), done + " / " + total + " activities");
        ImGui.TextDisabled("Now");
        ImGui.SameLine();
        ImGui.TextUnformatted(current?.Title ?? "Not started");
        ImGui.SameLine();
        ImGui.TextDisabled("Next");
        ImGui.SameLine();
        ImGui.TextUnformatted(next is null ? "—" : next.Title);
        var dj = HostController.DjForActivity(ev, current) ?? HostController.ScheduledDj(ev, now);
        var upcoming = HostController.NextDj(ev, now);
        ImGui.TextDisabled("DJ");
        ImGui.SameLine();
        ImGui.TextUnformatted(dj?.Name ?? "—");
        ImGui.SameLine();
        ImGui.TextDisabled("Up next");
        ImGui.SameLine();
        ImGui.TextUnformatted(upcoming?.Name ?? "—");
        if (next is not null)
        {
            var start = EventClock.EffectiveStart(ev, next, state);
            ImGui.TextDisabled(start is null ? "Next activity has no start time." : "Time until next activity: " + EventClock.Countdown(start.Value, now));
        }

        if (Chrome.Accent("Enter Live Host Mode"))
            app.StartHosting(openWindow: true);
        ImGui.SameLine();
        EventActions.TeleportButton(app, ev, "dash");
        ImGui.SameLine();
        EventActions.ResetButton(app, ev);
        ImGui.EndChild();
        ImGui.Spacing();
    }

    private static void Card(HorizonApp app, EventDocument ev, float width)
    {
        var style = ImGui.GetStyle();
        var line = ImGui.GetTextLineHeightWithSpacing();
        var button = ImGui.GetTextLineHeight() + Math.Max(14f, style.FramePadding.Y * 2f);
        var inner = Math.Max(40f, width - (style.WindowPadding.X * 2f));
        var location = Labels.Location(ev);
        var locationHeight = ImGui.CalcTextSize(string.IsNullOrWhiteSpace(location) ? " " : location, false, inner).Y;
        var height = (style.WindowPadding.Y * 2f) + (line * 5f) + locationHeight + button + 28f;
        var buttonRow = PillWidth(style, "Open") + PillWidth(style, "Teleport") + PillWidth(style, "Delete event") + (style.ItemSpacing.X * 2f);
        if (buttonRow > inner)
            height += button + style.ItemSpacing.Y;
        ImGui.BeginChild("card-" + ev.EventId, new Vector2(width, height), true);
        ImGui.TextUnformatted(ev.EventName);
        Chrome.Badge(Labels.Spaced(ev.Status.ToString()), ThemeService.Status(app.Theme.Current, ev.Status));
        ImGui.TextUnformatted(EventClock.FormatDate(ev.Date));
        var time = EventClock.FormatClock(ev.Date, ev.StartTime, ev.Timezone);
        if (app.Store.Config.ShowComputerLocalTime)
        {
            var reading = EventClock.Read(ev.Date, ev.StartTime, ev.Timezone);
            if (reading.Ok)
                time += "  ·  your clock " + EventClock.FormatComputerLocal(reading.Instant);
        }

        ImGui.TextUnformatted(time);
        Chrome.Muted(Labels.Location(ev));
        ImGui.TextDisabled(EventClock.EventCountdown(ev, DateTimeOffset.Now) + "  ·  " + ScheduleOrder.Spoken(ev).Count + " activities");
        if (Chrome.Accent("Open"))
            app.Select(ev.EventId);
        ImGui.SameLine();
        EventActions.TeleportButton(app, ev, "card");
        ImGui.SameLine();
        EventActions.DeleteButton(app, ev);
        ImGui.EndChild();
    }

    private static float StableWidth()
    {
        var width = ImGui.GetContentRegionAvail().X;
        if (ImGui.GetScrollMaxY() <= 0f)
            width -= ImGui.GetStyle().ScrollbarSize;
        return Math.Max(40f, width);
    }

    private static float PillWidth(ImGuiStylePtr style, string label)
    {
        var padX = Math.Max(28f, style.FramePadding.X * 2f);
        return ImGui.CalcTextSize(label).X + padX;
    }

    private static void Errors(HorizonApp app)
    {
        foreach (var error in app.Store.FileErrors.Take(4))
            ImGui.TextColored(ThemeHolder.Red, Path.GetFileName(error.Key) + ": " + error.Value);
    }
}

internal static class EventsPanel
{
    public static void Draw(HorizonApp app, MainWindow window)
    {
        var selected = app.Store.SelectedEvent();
        if (app.Store.Config.ShowWorkspace && selected is not null)
        {
            if (Chrome.Button("All events"))
            {
                app.Store.Config.ShowWorkspace = false;
                app.Store.MarkConfig();
            }

            ImGui.SameLine();
            ImGui.TextUnformatted(selected.EventName);
            ImGui.SameLine();
            EventActions.TeleportButton(app, selected, "header");
            ImGui.SameLine();
            EventActions.ResetButton(app, selected);
            ImGui.SameLine();
            EventActions.DeleteButton(app, selected);
            ImGui.Spacing();
            WorkspacePanel.Draw(app, window, selected);
            return;
        }

        if (Chrome.Accent("Create event"))
        {
            app.Store.CreateEvent();
            app.Note("Created a draft event.");
        }

        ImGui.SameLine();
        if (Chrome.Button("Import event"))
            window.Pick("Import event", path => app.BeginImport("event", path));
        ImGui.SameLine();
        var showArchived = app.Session.ShowArchived;
        if (ImGui.Checkbox("Show archived", ref showArchived))
            app.Session.ShowArchived = showArchived;

        ImGui.Spacing();
        foreach (var ev in app.Store.Events.Values.OrderBy(ev => ev.Date, StringComparer.Ordinal).ThenBy(ev => ev.EventName, StringComparer.Ordinal))
        {
            if (!app.Session.ShowArchived && ev.Status == EventStatus.Archived)
                continue;

            ImGui.PushID(ev.EventId);
            var style = ImGui.GetStyle();
            var line = ImGui.GetTextLineHeightWithSpacing();
            var button = ImGui.GetTextLineHeight() + Math.Max(14f, style.FramePadding.Y * 2f);
            var height = (style.WindowPadding.Y * 2f) + (line * 2f) + button + 16f;
            if (ImGui.GetContentRegionAvail().X < 720f)
                height += button + style.ItemSpacing.Y;
            ImGui.BeginChild("row", new Vector2(0, height), true);
            ImGui.TextUnformatted(ev.EventName);
            ImGui.SameLine();
            Chrome.Badge(Labels.Spaced(ev.Status.ToString()), ThemeService.Status(app.Theme.Current, ev.Status));
            ImGui.TextDisabled(EventClock.FormatDate(ev.Date) + "  ·  " + Labels.Location(ev));
            if (Chrome.Button("Open"))
                app.Select(ev.EventId);
            ImGui.SameLine();
            if (Chrome.Button("Duplicate"))
            {
                app.Store.Duplicate(ev);
                app.Note("Duplicated the event. Live progress was not copied.");
            }

            ImGui.SameLine();
            if (Chrome.Button("Export"))
            {
                var id = ev.EventId;
                window.SaveAs("Export event", id + ".json", path =>
                {
                    app.Store.ExportEvent(id, path);
                    app.Note("Exported " + path);
                });
            }

            ImGui.SameLine();
            if (ev.Status == EventStatus.Archived)
            {
                if (Chrome.Button("Unarchive"))
                {
                    ev.Status = EventStatus.Draft;
                    app.Store.MarkEvent(ev.EventId);
                    app.Store.SaveDirty();
                }
            }
            else if (Chrome.Button("Archive"))
            {
                app.Store.Archive(ev.EventId);
                app.Note("Archived " + ev.EventName + ".");
            }

            ImGui.SameLine();
            EventActions.DeleteButton(app, ev);

            ImGui.EndChild();
            ImGui.PopID();
        }
    }
}
