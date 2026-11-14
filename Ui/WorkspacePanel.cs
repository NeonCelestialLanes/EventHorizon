using System.Numerics;
using Dalamud.Bindings.ImGui;
using EventHorizon.Hosting;
using EventHorizon.Models;
using EventHorizon.Persistence;
using EventHorizon.Theme;

namespace EventHorizon.Ui;

internal static class WorkspacePanel
{
    private static readonly string[] Tabs = ["Overview", "Schedule", "DJs", "Games", "Scripts", "Contests", "Prizes", "Notes"];

    public static void Draw(HorizonApp app, MainWindow window, EventDocument ev)
    {
        Banners.Draw(app, ev);
        foreach (var tab in Tabs)
        {
            var width = Math.Max(78f, ImGui.CalcTextSize(tab).X + 18f);
            if (ImGui.Selectable(tab, app.Store.Config.WorkspaceTab == tab, ImGuiSelectableFlags.None, new Vector2(width, 26)))
            {
                app.Store.Config.WorkspaceTab = tab;
                app.Store.MarkConfig();
            }

            ImGui.SameLine();
        }

        ImGui.NewLine();
        ImGui.Spacing();
        switch (app.Store.Config.WorkspaceTab)
        {
            case "Schedule":
                SchedulePanel.Draw(app, ev);
                break;
            case "DJs":
                DjPanel.Draw(app, ev);
                break;
            case "Games":
                EventGamePanel.Draw(app, ev);
                break;
            case "Scripts":
                EventScriptPanel.Draw(app, window, ev);
                break;
            case "Contests":
                ContestPanel.Draw(app, ev);
                break;
            case "Prizes":
                PrizePanel.Draw(app, ev);
                break;
            case "Notes":
                NotesPanel.Draw(app, ev, generalToggle: false);
                break;
            default:
                Overview(app, window, ev);
                break;
        }
    }

    private static void Overview(HorizonApp app, MainWindow window, EventDocument ev)
    {
        Chrome.Section("Event");
        if (Chrome.Required("Name", ev.EventName, value => ev.EventName = value))
            app.Store.MarkEvent(ev.EventId);
        if (Chrome.Area("Description", ev.Description, value => ev.Description = value, 70))
            app.Store.MarkEvent(ev.EventId);
        if (Chrome.Required("Date", ev.Date, value => ev.Date = value))
            app.Store.MarkEvent(ev.EventId);
        ImGui.TextDisabled("Use yyyy-MM-dd. Times below are in the event timezone.");
        if (Chrome.Required("Timezone", ev.Timezone, value => ev.Timezone = value))
            app.Store.MarkEvent(ev.EventId);
        if (!EventClock.TryGetZone(ev.Timezone, out _))
            ImGui.TextColored(ThemeHolder.Gold, "That timezone was not found on this computer.");
        if (Chrome.Field("Start", ev.StartTime, value => ev.StartTime = value))
            app.Store.MarkEvent(ev.EventId);
        if (Chrome.Field("End", ev.EndTime, value => ev.EndTime = value))
            app.Store.MarkEvent(ev.EventId);
        var status = ev.Status;
        if (Chrome.EnumBox("Status", ref status))
        {
            ev.Status = status;
            app.Store.MarkEvent(ev.EventId);
        }

        if (Chrome.Field("Organizer", ev.Organizer, value => ev.Organizer = value))
            app.Store.MarkEvent(ev.EventId);
        Chrome.Section("Venue");
        if (Chrome.Field("Venue", ev.Venue.Name, value => ev.Venue.Name = value))
            app.Store.MarkEvent(ev.EventId);
        if (Chrome.Field("Data center", ev.Venue.DataCenter, value => ev.Venue.DataCenter = value))
            app.Store.MarkEvent(ev.EventId);
        if (Chrome.Field("World", ev.Venue.World, value => ev.Venue.World = value))
            app.Store.MarkEvent(ev.EventId);
        if (Chrome.Field("District", ev.Venue.District, value => ev.Venue.District = value))
            app.Store.MarkEvent(ev.EventId);
        if (Chrome.Field("Ward", ev.Venue.Ward, value => ev.Venue.Ward = value))
            app.Store.MarkEvent(ev.EventId);
        if (Chrome.Field("Plot", ev.Venue.Plot, value => ev.Venue.Plot = value))
            app.Store.MarkEvent(ev.EventId);
        if (Chrome.Area("Venue notes", ev.Venue.Notes, value => ev.Venue.Notes = value, 50))
            app.Store.MarkEvent(ev.EventId);
        Chrome.Muted("A house plot is optional. Leave it blank for a venue, cafe, or nightclub. District is Mist, Lavender Beds, the Goblet, Shirogane, or Empyreum.");
        EventActions.TeleportButton(app, ev, "venue");

        Chrome.Section("Staff");
        string? remove = null;
        foreach (var staff in ev.Staff)
        {
            ImGui.PushID(staff.StaffId);
            if (Chrome.Required("Name##staff", staff.Name, value => staff.Name = value))
                app.Store.MarkEvent(ev.EventId);
            if (Chrome.Field("Role", staff.Role, value => staff.Role = value))
                app.Store.MarkEvent(ev.EventId);
            if (Chrome.Button("Remove staff"))
                remove = staff.StaffId;
            ImGui.PopID();
        }

        if (remove is not null)
        {
            ev.Staff.RemoveAll(staff => staff.StaffId == remove);
            app.Store.MarkEvent(ev.EventId);
        }

        if (Chrome.Button("Add staff"))
        {
            ev.Staff.Add(new StaffMember { StaffId = Ids.Unique("stf-", ev.Staff.Select(staff => staff.StaffId)), Name = "New staff" });
            app.Store.MarkEvent(ev.EventId);
        }

        ImGui.Spacing();
        if (Chrome.Button("Open event folder"))
            app.OpenPath(app.Store.EventsDir);
        ImGui.SameLine();
        if (Chrome.Button("Reload from disk"))
            app.ReloadSelected();
        ImGui.SameLine();
        if (Chrome.Button("Export"))
        {
            var id = ev.EventId;
            window.SaveAs("Export event", id + ".json", path =>
            {
                app.Store.ExportEvent(id, path);
                app.Note("Exported the event.");
            });
        }

        ImGui.SameLine();
        EventActions.ResetButton(app, ev);
        ImGui.SameLine();
        EventActions.DeleteButton(app, ev);
    }
}

internal static class EventActions
{
    public static void DeleteButton(HorizonApp app, EventDocument ev)
    {
        if (!Chrome.Tone("Delete event", ThemeHolder.Red))
            return;

        var id = ev.EventId;
        var name = ev.EventName;
        app.Ask("Delete " + name + "? The event file and its live progress are removed. A backup is kept.", () =>
        {
            app.Store.DeleteEvent(id);
            app.Note("Deleted " + name + ".");
        });
    }

    public static void TeleportButton(HorizonApp app, EventDocument ev, string scope)
    {
        if (!Chrome.Button("Teleport to venue##" + scope))
            return;

        if (app.Travel is null)
        {
            app.Fail("Teleport isn't available.");
            return;
        }

        app.Travel.Go(app, ev.Venue);
    }

    public static void ResetButton(HorizonApp app, EventDocument ev)
    {
        if (!Chrome.Tone("Reset party", ThemeHolder.Gold))
            return;

        var id = ev.EventId;
        var name = ev.EventName;
        app.Ask("Start " + name + " over? The current activity, completed blocks, revealed answers, scores, winners, and clues go back to the beginning. The schedule, scripts, and games stay. A backup of this run is kept.", () =>
        {
            app.Store.ResetParty(id);
            app.Note(name + " is ready to host from the start.");
        });
    }
}

internal static class SchedulePanel
{
    public static void Draw(HorizonApp app, EventDocument ev)
    {
        var state = app.Store.StateFor(ev.EventId);
        var now = DateTimeOffset.Now;
        var suggested = ScheduleOrder.Suggested(ev, state, now);
        if (suggested is not null && suggested.ActivityId != state.CurrentActivityId)
            Chrome.Muted("Clock suggestion: " + suggested.Title + " looks current. Nothing is marked complete automatically.");

        if (Chrome.Button("Add activity"))
        {
            ev.Schedule.Add(new ScheduleActivity
            {
                ActivityId = Ids.Unique("act-", ev.Schedule.Select(activity => activity.ActivityId)),
                Title = "New activity",
                Category = ActivityCategory.Custom,
                StartTime = ev.StartTime,
            });
            app.Store.MarkEvent(ev.EventId);
        }

        var ordered = ScheduleOrder.Spoken(ev);
        for (var index = 0; index < ordered.Count; index++)
            Row(app, ev, state, ordered, index, now);
    }

    private static void Row(HorizonApp app, EventDocument ev, LiveEventState state, List<ScheduleActivity> ordered, int index, DateTimeOffset now)
    {
        var activity = ordered[index];
        var status = ScheduleOrder.StatusOf(activity, state);
        var color = ThemeService.Category(app.Theme.Current, activity.Category);
        ImGui.PushID(activity.ActivityId);
        var pulse = status == ActivityProgressStatus.Live && app.Store.Config.Animations
            ? 0.55f + (0.4f * (0.5f + (0.5f * MathF.Sin((float)ImGui.GetTime() * 2.4f))))
            : 0.85f;
        ImGui.PushStyleColor(ImGuiCol.Border, ThemeService.With(status == ActivityProgressStatus.Live ? ThemeHolder.Pink : color, pulse));
        var style = ImGui.GetStyle();
        var line = ImGui.GetTextLineHeightWithSpacing();
        var button = ImGui.GetTextLineHeight() + Math.Max(14f, style.FramePadding.Y * 2f);
        var notesHeight = string.IsNullOrWhiteSpace(activity.HostNotes)
            ? 0f
            : ImGui.CalcTextSize(activity.HostNotes, false, Math.Max(40f, ImGui.GetContentRegionAvail().X - 28f)).Y + style.ItemSpacing.Y;
        var height = (style.WindowPadding.Y * 2f) + (line * 2f) + notesHeight + button + 18f;
        if (ImGui.GetContentRegionAvail().X < 860f)
            height += button + style.ItemSpacing.Y;
        ImGui.BeginChild("act", new Vector2(0, height), true);
        var min = ImGui.GetWindowPos();
        ImGui.GetWindowDrawList().AddRectFilled(min, min + new Vector2(4, ImGui.GetWindowSize().Y), ImGui.GetColorU32(color));
        ImGui.Indent(10);
        ImGui.TextUnformatted(activity.Title);
        ImGui.SameLine();
        Chrome.Badge(Labels.Spaced(status.ToString()), ThemeService.Progress(app.Theme.Current, status));
        ImGui.SameLine();
        Chrome.Badge(Labels.Spaced(activity.Category.ToString()), color);
        var when = EventClock.FormatClock(ev.Date, activity.StartTime, ev.Timezone) + " – " + EventClock.FormatClock(ev.Date, activity.EndTime, ev.Timezone);
        if (state.Activities.TryGetValue(activity.ActivityId, out var progress) && progress.DelayMinutes > 0)
            when += "  ·  delayed " + progress.DelayMinutes + "m";
        ImGui.TextDisabled(when);
        if (!string.IsNullOrWhiteSpace(activity.HostNotes))
            Chrome.Muted(activity.HostNotes);

        if (Chrome.Tone("Start", ThemeHolder.Pink))
            Run(app, ev, () => HostController.StartActivity(ev, state, activity.ActivityId));
        ImGui.SameLine();
        if (Chrome.Button("Complete"))
            Run(app, ev, () => HostController.Complete(state, activity.ActivityId));
        ImGui.SameLine();
        if (Chrome.Button("Skip"))
            Run(app, ev, () => HostController.Skip(ev, state, activity.ActivityId));
        ImGui.SameLine();
        if (Chrome.Button("Delay 5m"))
            Run(app, ev, () => HostController.Delay(state, activity.ActivityId));
        ImGui.SameLine();
        if (Chrome.Button("Resume"))
            Run(app, ev, () => HostController.Resume(ev, state, activity.ActivityId));
        ImGui.SameLine();
        if (Chrome.Button(activity.ActivityId == app.Session.EditorId ? "Close editor" : "Edit"))
            app.Session.EditorId = app.Session.EditorId == activity.ActivityId ? null : activity.ActivityId;
        ImGui.Unindent(10);
        ImGui.EndChild();
        ImGui.PopStyleColor();

        if (app.Session.EditorId == activity.ActivityId)
            Editor(app, ev, activity, index, ordered.Count);
        ImGui.PopID();
    }

    private static void Editor(HorizonApp app, EventDocument ev, ScheduleActivity activity, int index, int count)
    {
        if (Chrome.Required("Title", activity.Title, value => activity.Title = value))
            app.Store.MarkEvent(ev.EventId);
        if (Chrome.Field("Start", activity.StartTime, value => activity.StartTime = value))
            app.Store.MarkEvent(ev.EventId);
        if (Chrome.Field("End", activity.EndTime, value => activity.EndTime = value))
            app.Store.MarkEvent(ev.EventId);
        var category = activity.Category;
        if (Chrome.EnumBox("Category", ref category))
        {
            activity.Category = category;
            app.Store.MarkEvent(ev.EventId);
        }

        if (Chrome.Area("Description", activity.Description, value => activity.Description = value, 50))
            app.Store.MarkEvent(ev.EventId);
        if (Chrome.Area("Host notes", activity.HostNotes, value => activity.HostNotes = value, 50))
            app.Store.MarkEvent(ev.EventId);
        if (Chrome.Field("DJ id", activity.DjId, value => activity.DjId = value))
            app.Store.MarkEvent(ev.EventId);
        if (Chrome.Field("Game id", activity.GameInstanceId, value => activity.GameInstanceId = value))
            app.Store.MarkEvent(ev.EventId);
        if (Chrome.Field("Script id", activity.ScriptId, value => activity.ScriptId = value))
            app.Store.MarkEvent(ev.EventId);
        if (Chrome.Field("Prize id", activity.PrizeId, value => activity.PrizeId = value))
            app.Store.MarkEvent(ev.EventId);
        if (index > 0 && Chrome.Button("Move earlier"))
            Swap(app, ev, activity, -1);
        ImGui.SameLine();
        if (index < count - 1 && Chrome.Button("Move later"))
            Swap(app, ev, activity, 1);
        ImGui.SameLine();
        if (Chrome.Tone("Remove", ThemeHolder.Red))
        {
            var id = activity.ActivityId;
            app.Ask("Remove this activity from the schedule?", () =>
            {
                ev.Schedule.RemoveAll(item => item.ActivityId == id);
                app.Store.MarkEvent(ev.EventId);
            });
        }
    }

    private static void Swap(HorizonApp app, EventDocument ev, ScheduleActivity activity, int direction)
    {
        var ordered = ScheduleOrder.Spoken(ev);
        var index = ordered.FindIndex(item => item.ActivityId == activity.ActivityId);
        var other = ordered[index + direction];
        (activity.StartTime, other.StartTime) = (other.StartTime, activity.StartTime);
        (activity.EndTime, other.EndTime) = (other.EndTime, activity.EndTime);
        app.Store.MarkEvent(ev.EventId);
    }

    private static void Run(HorizonApp app, EventDocument ev, Action action)
    {
        action();
        app.Store.MarkState(ev.EventId);
    }
}
