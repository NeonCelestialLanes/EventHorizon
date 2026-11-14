using System.Numerics;
using Dalamud.Bindings.ImGui;
using EventHorizon.Hosting;
using EventHorizon.Models;
using EventHorizon.Persistence;
using EventHorizon.Theme;

namespace EventHorizon.Ui;

internal static class DjPanel
{
    public static void Draw(HorizonApp app, EventDocument ev)
    {
        var now = DateTimeOffset.Now;
        var currentActivity = ScheduleOrder.Current(ev, app.Store.StateFor(ev.EventId));
        var stage = HostController.OnStage(ev, currentActivity, now);
        var nowPlaying = stage.Current;
        var upNext = stage.Upcoming;
        ImGui.TextDisabled("Now playing");
        ImGui.SameLine();
        ImGui.TextUnformatted(nowPlaying?.Name ?? "—");
        ImGui.SameLine();
        ImGui.TextDisabled("Up next");
        ImGui.SameLine();
        ImGui.TextUnformatted(upNext?.Name ?? "—");
        Chrome.Muted("Now playing follows the activity you are on. Before the show starts, it follows the clock. Links are never opened.");
        if (Chrome.Button("Add DJ"))
        {
            ev.Djs.Add(new DjSlot
            {
                DjId = Ids.Unique("dj-", ev.Djs.Select(dj => dj.DjId)),
                Name = "New DJ",
                StartTime = ev.StartTime,
            });
            app.Store.MarkEvent(ev.EventId);
        }

        string? remove = null;
        foreach (var dj in ev.Djs)
        {
            ImGui.PushID(dj.DjId);
            ImGui.BeginChild("dj", new Vector2(0, 320), true);
            ImGui.TextDisabled(dj.DjId);
            if (Chrome.Required("Name", dj.Name, value => dj.Name = value))
                app.Store.MarkEvent(ev.EventId);
            if (Chrome.Field("Start", dj.StartTime, value => dj.StartTime = value))
                app.Store.MarkEvent(ev.EventId);
            if (Chrome.Field("End", dj.EndTime, value => dj.EndTime = value))
                app.Store.MarkEvent(ev.EventId);
            if (Chrome.Field("Stream URL", dj.StreamUrl, value => dj.StreamUrl = value, 400))
                app.Store.MarkEvent(ev.EventId);
            if (!string.IsNullOrWhiteSpace(dj.StreamUrl) && Chrome.Button("Copy URL"))
                ImGui.SetClipboardText(dj.StreamUrl);
            if (Chrome.Area("Introduction", dj.IntroText, value => dj.IntroText = value, 70))
                app.Store.MarkEvent(ev.EventId);
            if (!string.IsNullOrWhiteSpace(dj.IntroText) && Chrome.Button("Copy introduction"))
                ImGui.SetClipboardText(dj.IntroText);
            if (Chrome.Area("Transition", dj.TransitionText, value => dj.TransitionText = value, 70))
                app.Store.MarkEvent(ev.EventId);
            if (!string.IsNullOrWhiteSpace(dj.TransitionText) && Chrome.Button("Copy transition"))
                ImGui.SetClipboardText(dj.TransitionText);
            if (Chrome.Field("Intro script id", dj.IntroScriptId, value => dj.IntroScriptId = value))
                app.Store.MarkEvent(ev.EventId);
            if (Chrome.Field("Transition script id", dj.TransitionScriptId, value => dj.TransitionScriptId = value))
                app.Store.MarkEvent(ev.EventId);
            OpenScript(app, ev, dj.IntroScriptId, "Open introduction script");
            ImGui.SameLine();
            OpenScript(app, ev, dj.TransitionScriptId, "Open transition script");
            if (Chrome.Area("Notes", dj.Notes, value => dj.Notes = value, 50))
                app.Store.MarkEvent(ev.EventId);
            if (Chrome.Tone("Remove DJ", ThemeHolder.Red))
                remove = dj.DjId;
            ImGui.EndChild();
            ImGui.PopID();
        }

        if (remove is not null)
        {
            ev.Djs.RemoveAll(dj => dj.DjId == remove);
            app.Store.MarkEvent(ev.EventId);
        }
    }

    private static void OpenScript(HorizonApp app, EventDocument ev, string? scriptId, string label)
    {
        if (string.IsNullOrWhiteSpace(scriptId) || Chrome.Button(label) == false)
            return;

        app.Session.EventScriptId = scriptId;
        app.Store.Config.WorkspaceTab = "Scripts";
        app.Store.MarkConfig();
        if (ev.Scripts.All(script => script.ScriptId != scriptId))
            app.Fail("That script id is not in this event.");
    }
}

internal static class ContestPanel
{
    public static void Draw(HorizonApp app, EventDocument ev)
    {
        var state = app.Store.StateFor(ev.EventId);
        Chrome.Muted("Contests can run all night without a schedule block. Clues stay hidden until you reveal them.");
        if (Chrome.Button("Add contest"))
        {
            ev.Contests.Add(new ContestEntry
            {
                ContestId = Ids.Unique("cst-", ev.Contests.Select(contest => contest.ContestId)),
                Title = "New contest",
                ContestType = "Custom",
                WinnerMethod = "Manual",
            });
            app.Store.MarkEvent(ev.EventId);
        }

        string? remove = null;
        foreach (var contest in ev.Contests)
        {
            ImGui.PushID(contest.ContestId);
            ImGui.BeginChild("contest", new Vector2(0, 360), true);
            if (Chrome.Required("Title", contest.Title, value => contest.Title = value))
                app.Store.MarkEvent(ev.EventId);
            if (Chrome.Field("Type", contest.ContestType, value => contest.ContestType = value))
                app.Store.MarkEvent(ev.EventId);
            ImGui.TextDisabled("Screenshot, GPose, Collection, Scavenger, Raffle, Creative, or anything else.");
            if ((contest.ContestType ?? "").Contains("collect", StringComparison.OrdinalIgnoreCase))
                Chrome.Muted("A tell with every clue word counts. Capitalization and word order do not matter. Nothing is sent back.");
            if (Chrome.Area("Rules", contest.Rules, value => contest.Rules = value, 60))
                app.Store.MarkEvent(ev.EventId);
            if (Chrome.Field("Starts", contest.StartTime, value => contest.StartTime = value))
                app.Store.MarkEvent(ev.EventId);
            if (Chrome.Field("Deadline", contest.Deadline, value => contest.Deadline = value))
                app.Store.MarkEvent(ev.EventId);
            if (Chrome.Field("Prize id", contest.PrizeId, value => contest.PrizeId = value))
                app.Store.MarkEvent(ev.EventId);
            if (Chrome.Field("How to enter", contest.SubmissionMethod, value => contest.SubmissionMethod = value))
                app.Store.MarkEvent(ev.EventId);
            if (Chrome.Field("How a winner is chosen", contest.WinnerMethod, value => contest.WinnerMethod = value))
                app.Store.MarkEvent(ev.EventId);
            var winner = HostController.ContestWinner(state, contest) ?? "";
            if (Chrome.Required("Winner", winner, value =>
                {
                    state.ContestWinners[contest.ContestId] = value;
                    app.Store.QueueState(ev.EventId);
                }))
            {
                /* saved above */
            }

            if (Chrome.Area("Notes", contest.Notes, value => contest.Notes = value, 50))
                app.Store.MarkEvent(ev.EventId);
            Chrome.Section("Clues");
            string? removeClue = null;
            foreach (var clue in contest.Clues)
            {
                ImGui.PushID(clue.ClueId);
                var revealed = state.RevealedClueIds.Contains(clue.ClueId);
                ImGui.TextUnformatted(revealed ? clue.Content : "Hidden until you reveal it");
                if (!string.IsNullOrWhiteSpace(clue.RevealTime))
                    ImGui.TextDisabled("Suggested reveal " + EventClock.FormatClock(ev.Date, clue.RevealTime, ev.Timezone));
                if (revealed && Chrome.Area("Clue", clue.Content, value => clue.Content = value ?? "", 40))
                    app.Store.MarkEvent(ev.EventId);
                if (Chrome.Button(revealed ? "Hide clue" : "Reveal clue"))
                {
                    if (revealed)
                        HostController.HideClue(state, clue.ClueId);
                    else
                        HostController.RevealClue(state, clue.ClueId);
                    app.Store.MarkState(ev.EventId);
                }

                ImGui.SameLine();
                if (Chrome.Button("Remove clue"))
                    removeClue = clue.ClueId;
                ImGui.PopID();
            }

            if (removeClue is not null)
            {
                contest.Clues.RemoveAll(clue => clue.ClueId == removeClue);
                app.Store.MarkEvent(ev.EventId);
            }

            if (Chrome.Button("Add clue"))
            {
                contest.Clues.Add(new ContestClue
                {
                    ClueId = Ids.Unique("clu-", contest.Clues.Select(clue => clue.ClueId)),
                    Content = "New clue",
                });
                app.Store.MarkEvent(ev.EventId);
            }

            if (Chrome.Tone("Remove contest", ThemeHolder.Red))
                remove = contest.ContestId;
            ImGui.EndChild();
            ImGui.PopID();
        }

        if (remove is not null)
        {
            ev.Contests.RemoveAll(contest => contest.ContestId == remove);
            app.Store.MarkEvent(ev.EventId);
        }
    }
}

internal static class PrizePanel
{
    public static void Draw(HorizonApp app, EventDocument? ev)
    {
        if (ev is null)
        {
            Chrome.Muted("Select an event to track prizes.");
            return;
        }

        var state = app.Store.StateFor(ev.EventId);
        var rows = ev.Prizes.Select(prize => HostController.Prize(state, prize)).ToList();
        var awarded = rows.Count(row => row.Status == AwardStatus.Awarded);
        var pending = rows.Count(row => row.Status == AwardStatus.Pending);
        ImGui.TextUnformatted(ev.Prizes.Count + " prizes");
        ImGui.SameLine();
        Chrome.Badge(awarded + " awarded", ThemeHolder.Mint);
        ImGui.SameLine();
        Chrome.Badge(pending + " pending", ThemeHolder.Gold);
        ImGui.SameLine();
        Chrome.Badge((ev.Prizes.Count - awarded) + " outstanding", ThemeHolder.Pink);
        Chrome.Muted("Winners are saved with live progress, separate from the reusable event file. Nothing is traded or mailed in game.");
        if (Chrome.Button("Add prize"))
        {
            ev.Prizes.Add(new PrizeEntry
            {
                PrizeId = Ids.Unique("prz-", ev.Prizes.Select(prize => prize.PrizeId)),
                RewardType = RewardType.Custom,
                RewardName = "Prize",
            });
            app.Store.MarkEvent(ev.EventId);
        }

        string? remove = null;
        foreach (var prize in ev.Prizes)
        {
            ImGui.PushID(prize.PrizeId);
            var progress = HostController.Prize(state, prize);
            ImGui.BeginChild("prize", new Vector2(0, 280), true);
            ImGui.TextDisabled(prize.PrizeId);
            ImGui.TextUnformatted(Labels.Reward(prize));
            var type = prize.RewardType;
            if (Chrome.EnumBox("Reward", ref type))
            {
                prize.RewardType = type;
                app.Store.MarkEvent(ev.EventId);
            }

            if (Chrome.Field("Name", prize.RewardName, value => prize.RewardName = value))
                app.Store.MarkEvent(ev.EventId);
            var amount = prize.Amount?.ToString("0.##") ?? "";
            if (Chrome.Required("Amount", amount, value =>
                {
                    if (string.IsNullOrWhiteSpace(value))
                        prize.Amount = null;
                    else if (decimal.TryParse(value, out var number))
                        prize.Amount = number;
                }))
                app.Store.MarkEvent(ev.EventId);
            var multiple = prize.AllowMultipleWinners;
            if (ImGui.Checkbox("Allow multiple winners", ref multiple))
            {
                prize.AllowMultipleWinners = multiple;
                app.Store.MarkEvent(ev.EventId);
            }
            if (Chrome.Field("Game id", prize.GameInstanceId, value => prize.GameInstanceId = value))
                app.Store.MarkEvent(ev.EventId);
            if (Chrome.Field("Contest id", prize.ContestId, value => prize.ContestId = value))
                app.Store.MarkEvent(ev.EventId);
            var status = progress.Status;
            if (Chrome.EnumBox("Award status", ref status))
            {
                var edit = HostController.EditPrize(state, prize);
                edit.Status = status;
                app.Store.MarkState(ev.EventId);
            }

            ImGui.TextDisabled("Winners");
            string? drop = null;
            foreach (var winner in progress.Winners)
            {
                ImGui.BulletText(winner);
                ImGui.SameLine();
                if (ImGui.SmallButton("Remove##" + winner))
                    drop = winner;
            }

            if (drop is not null)
            {
                var edit = HostController.EditPrize(state, prize);
                edit.Winners.Remove(drop);
                app.Store.MarkState(ev.EventId);
            }

            app.Session.Drafts.TryGetValue(prize.PrizeId, out var winnerDraft);
            winnerDraft ??= "";
            if (Chrome.Required("Add winner##name", winnerDraft, value => app.Session.Drafts[prize.PrizeId] = value, 80))
                _ = winnerDraft;
            ImGui.SameLine();
            if (Chrome.Button("Add") && app.Session.Drafts.TryGetValue(prize.PrizeId, out var typed) && !string.IsNullOrWhiteSpace(typed))
            {
                var edit = HostController.EditPrize(state, prize);
                if (!prize.AllowMultipleWinners)
                    edit.Winners.Clear();
                edit.Winners.Add(typed.Trim());
                if (edit.Status == AwardStatus.NotAwarded)
                    edit.Status = AwardStatus.Pending;
                app.Session.Drafts[prize.PrizeId] = "";
                app.Store.MarkState(ev.EventId);
            }

            if (Chrome.Area("Notes", prize.Notes, value => prize.Notes = value, 40))
                app.Store.MarkEvent(ev.EventId);
            if (Chrome.Tone("Remove prize", ThemeHolder.Red))
                remove = prize.PrizeId;
            ImGui.EndChild();
            ImGui.PopID();
        }

        if (remove is not null)
        {
            ev.Prizes.RemoveAll(prize => prize.PrizeId == remove);
            app.Store.MarkEvent(ev.EventId);
        }
    }
}

internal static class NotesPanel
{
    public static void Draw(HorizonApp app, EventDocument? ev, bool generalToggle)
    {
        if (generalToggle)
        {
            if (ImGui.RadioButton("This event", app.Session.NotesMode == 0))
                app.Session.NotesMode = ev is null ? 1 : 0;
            ImGui.SameLine();
            if (ImGui.RadioButton("General", app.Session.NotesMode == 1))
                app.Session.NotesMode = 1;
        }

        var general = generalToggle && (app.Session.NotesMode == 1 || ev is null);
        if (general)
        {
            DrawLists(app.Store.Notes.Notes, app.Store.Notes.Checklists, null, app, () => app.Store.MarkNotes());
            return;
        }

        if (ev is null)
        {
            Chrome.Muted("Select an event, or switch to general notes.");
            return;
        }

        var state = app.Store.StateFor(ev.EventId);
        Chrome.Section("Notes");
        NoteList(ev.Notes, () => app.Store.MarkEvent(ev.EventId));
        if (Chrome.Button("Add note"))
        {
            ev.Notes.Add(new NoteEntry { NoteId = Ids.Unique("nte-", ev.Notes.Select(note => note.NoteId)), Title = "Note", Body = "" });
            app.Store.MarkEvent(ev.EventId);
        }

        Chrome.Section("Checklist");
        string? remove = null;
        foreach (var item in ev.Checklists)
        {
            ImGui.PushID(item.ItemId);
            var done = HostController.ChecklistDone(state, item);
            if (ImGui.Checkbox("##done", ref done))
            {
                item.Done = done;
                HostController.SetChecklist(state, item, done);
                app.Store.MarkEvent(ev.EventId);
                app.Store.MarkState(ev.EventId);
            }

            ImGui.SameLine();
            if (Chrome.Required("##text", item.Text, value => item.Text = value))
                app.Store.MarkEvent(ev.EventId);
            ImGui.SameLine();
            if (Chrome.Button("Remove"))
                remove = item.ItemId;
            ImGui.PopID();
        }

        if (remove is not null)
        {
            ev.Checklists.RemoveAll(item => item.ItemId == remove);
            app.Store.MarkEvent(ev.EventId);
        }

        if (Chrome.Button("Add checklist item"))
        {
            ev.Checklists.Add(new ChecklistItem { ItemId = Ids.Unique("chk-", ev.Checklists.Select(item => item.ItemId)), Text = "To do" });
            app.Store.MarkEvent(ev.EventId);
        }
    }

    private static void DrawLists(List<NoteEntry> notes, List<ChecklistItem> checks, LiveEventState? state, HorizonApp app, Action changed)
    {
        NoteList(notes, changed);
        if (Chrome.Button("Add note"))
        {
            notes.Add(new NoteEntry { NoteId = Ids.Unique("nte-", notes.Select(note => note.NoteId)), Title = "Note", Body = "" });
            changed();
        }

        Chrome.Section("Checklist");
        string? remove = null;
        foreach (var item in checks)
        {
            ImGui.PushID(item.ItemId);
            var done = item.Done;
            if (ImGui.Checkbox("##done", ref done))
            {
                item.Done = done;
                changed();
            }

            ImGui.SameLine();
            if (Chrome.Required("##text", item.Text, value => item.Text = value))
                changed();
            ImGui.SameLine();
            if (Chrome.Button("Remove"))
                remove = item.ItemId;
            ImGui.PopID();
        }

        if (remove is not null)
        {
            checks.RemoveAll(item => item.ItemId == remove);
            changed();
        }

        if (Chrome.Button("Add checklist item"))
        {
            checks.Add(new ChecklistItem { ItemId = Ids.Unique("chk-", checks.Select(item => item.ItemId)), Text = "To do" });
            changed();
        }

        _ = state;
        _ = app;
    }

    private static void NoteList(List<NoteEntry> notes, Action changed)
    {
        string? remove = null;
        foreach (var note in notes)
        {
            ImGui.PushID(note.NoteId);
            if (Chrome.Field("Title", note.Title, value => note.Title = value))
                changed();
            if (Chrome.Field("Category", note.Category, value => note.Category = value))
                changed();
            if (Chrome.RequiredArea("##body", note.Body, value => note.Body = value, 70))
                changed();
            if (Chrome.Button("Remove note"))
                remove = note.NoteId;
            ImGui.Separator();
            ImGui.PopID();
        }

        if (remove is not null)
        {
            notes.RemoveAll(note => note.NoteId == remove);
            changed();
        }
    }
}
