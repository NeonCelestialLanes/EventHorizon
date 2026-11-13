using Dalamud.Bindings.ImGui;
using EventHorizon.Hosting;
using EventHorizon.Models;
using EventHorizon.Theme;

namespace EventHorizon.Ui;

internal static class TriviaPanel
{
    public static void Draw(HorizonApp app, EventDocument ev, GameInstance game, GameProgress progress)
    {
        var questions = TriviaScore.Questions(game, progress.Trivia);
        var current = TriviaScore.Current(game, progress);
        var rows = TriviaScore.Board(progress);
        var counts = TriviaScore.Progress(game, progress);
        Chrome.Section("Trivia scoreboard");
        ImGui.TextUnformatted(questions.Count == 0
            ? "No questions yet."
            : "Question " + ((progress.Trivia?.ViewIndex ?? 0) + 1) + " of " + questions.Count);
        ImGui.SameLine();
        ImGui.TextDisabled(counts.Done + " done  ·  " + counts.Left + " left");
        LeaderLine(rows);

        if (current is { } question)
            Question(app, ev, game, progress, question);
        Copies(app, ev, game, rows, counts.Done, counts.Left);
        People(app, ev, game, progress, current?.Id);
        Heard(app, ev, game, progress, current?.Id);
        Board(app, ev, game, progress, rows);
    }

    private static void Question(HorizonApp app, EventDocument ev, GameInstance game, GameProgress progress, TriviaQuestion question)
    {
        Clock(app, ev, game, question.Id, app.Store.Config.UiScale);
        if (!string.IsNullOrWhiteSpace(question.Title))
            ImGui.TextColored(ThemeHolder.Accent, question.Title);
        if (!string.IsNullOrWhiteSpace(question.Prompt))
            ImGui.TextWrapped(question.Prompt);
        var round = game.Rounds.FirstOrDefault(item => item.RoundId == question.Id);
        if (round?.Choices.Count > 0)
            ImGui.TextDisabled(string.Join("   ", round.Choices));

        var revealed = HostController.IsRevealed(progress, question.Id);
        if (!revealed)
        {
            ImGui.TextColored(ThemeHolder.Gold, "Answer hidden. Only this window can reveal it.");
            if (Chrome.Accent("Reveal answer") && !string.IsNullOrWhiteSpace(question.Answer))
                Edit(app, ev, game, edit => HostController.Reveal(edit, question.Id));
        }
        else
        {
            ImGui.TextDisabled("Correct answer");
            ImGui.SameLine();
            ImGui.TextColored(ThemeHolder.Mint, string.IsNullOrWhiteSpace(question.Answer) ? "No answer was written down." : question.Answer);
            if (Chrome.Button("Hide answer"))
                Edit(app, ev, game, edit => HostController.Hide(edit, question.Id));
        }

        var result = progress.Trivia?.Results.FirstOrDefault(item => item.QuestionId == question.Id);
        if (result is { Skipped: true })
            ImGui.TextDisabled("Skipped. No point.");
        else if (result?.PlayerId is not null)
        {
            var holder = progress.Players.FirstOrDefault(player => player.PlayerId == result.PlayerId);
            ImGui.TextColored(ThemeHolder.Mint, "Point: " + (holder is null ? "Unknown" : UnoTournament.Label(holder.Name, holder.World)));
        }
        else
            ImGui.TextDisabled("No point yet.");

        if (Chrome.Button("Previous question"))
            Edit(app, ev, game, edit => TriviaScore.Move(game, edit, -1));
        ImGui.SameLine();
        if (Chrome.Button("Next question"))
            Edit(app, ev, game, edit => TriviaScore.Move(game, edit, 1));
        ImGui.SameLine();
        if (Chrome.Accent("Award 1 point"))
            Give(app, ev, game, question.Id, app.Session.TriviaPlayerId);
        ImGui.SameLine();
        if (Chrome.Button("Skip question"))
            Try(app, ev, game, edit => TriviaScore.Skip(edit, question.Id));
        if (result is { Skipped: true } && Chrome.Button("Restore question"))
            Try(app, ev, game, edit => TriviaScore.Restore(edit, question.Id));
        if (result is { Skipped: false, PlayerId: not null } && !string.IsNullOrWhiteSpace(app.Session.TriviaPlayerId) && app.Session.TriviaPlayerId != result.PlayerId)
        {
            ImGui.SameLine();
            if (Chrome.Button("Reassign point"))
                GiveMove(app, ev, game, question.Id, app.Session.TriviaPlayerId);
        }

        ImGui.SameLine();
        if (Chrome.Button("End trivia"))
        {
            var state = app.Store.StateFor(ev.EventId);
            var live = HostController.EditGame(state, game.InstanceId);
            var message = TriviaScore.Finish(ev, game, live, state);
            if (message is null)
            {
                var ready = TriviaScore.FinalResults(TriviaScore.Board(live), TriviaScore.PrizeLabel(ev, game));
                if (ready.Count > 0)
                    ImGui.SetClipboardText(ready[0]);
                app.Store.MarkState(ev.EventId);
                app.Note(ready.Count > 0
                    ? "Trivia is complete. The winner announcement is copied."
                    : "Trivia is complete.");
            }
            else
            {
                app.Fail(message);
            }
        }
    }

    private static void People(HorizonApp app, EventDocument ev, GameInstance game, GameProgress progress, string? questionId)
    {
        Chrome.Section("Players");
        TellWordBox.Draw(game.TellWord, value => game.TellWord = value, () => app.Store.MarkEvent(ev.EventId));
        var word = TellSignup.Word(game);
        Chrome.Badge(progress.RegistrationOpen ? "Registration open" : "Registration closed", progress.RegistrationOpen ? ThemeHolder.Mint : ThemeHolder.Gold);
        ImGui.SameLine();
        if (!progress.RegistrationOpen && Chrome.Accent("Open registration"))
        {
            if (word.Length == 0)
                app.Fail("Enter a tell word first.");
            else
            {
                Edit(app, ev, game, edit => edit.RegistrationOpen = true);
                app.Note("Registration is open. A tell that says " + word + " will add that character.");
            }
        }
        else if (progress.RegistrationOpen && Chrome.Button("Close registration"))
        {
            Edit(app, ev, game, edit => edit.RegistrationOpen = false);
            app.Note("Registration is closed.");
        }

        var listening = progress.Trivia is { Listening: true };
        if (Chrome.Button(listening ? "Stop listening" : "Listen for answers"))
        {
            Edit(app, ev, game, edit => TriviaScore.Desk(edit).Listening = !listening);
            app.Note(listening ? "Trivia is no longer listening to chat." : "Say, yell, party, and alliance answers will be listed. Points stay with you.");
        }

        ImGui.SameLine();
        Chrome.Muted(listening ? "Listening." : "Not listening.");
        var search = app.Session.TriviaSearch;
        if (Chrome.Field("Find a player", search, value => app.Session.TriviaSearch = value ?? ""))
            _ = search;
        var name = app.Session.TriviaName;
        if (Chrome.Field("Character name", name, value => app.Session.TriviaName = value ?? ""))
            _ = name;
        var world = app.Session.TriviaWorld;
        if (Chrome.Field("World", world, value => app.Session.TriviaWorld = value ?? ""))
            _ = world;
        if (Chrome.Button("Add player") && !string.IsNullOrWhiteSpace(app.Session.TriviaName))
        {
            var person = app.Session.TriviaName;
            var home = app.Session.TriviaWorld;
            Edit(app, ev, game, edit => TriviaScore.Ensure(edit, person, home));
            app.Session.TriviaName = "";
            app.Session.TriviaWorld = "";
        }

        var needle = app.Session.TriviaSearch.Trim();
        foreach (var row in TriviaScore.Board(progress))
        {
            if (needle.Length > 0 && !row.Label.Contains(needle, StringComparison.OrdinalIgnoreCase))
                continue;

            ImGui.PushID(row.PlayerId);
            if (ImGui.Selectable(row.Label + "##pick", app.Session.TriviaPlayerId == row.PlayerId))
                app.Session.TriviaPlayerId = row.PlayerId;
            ImGui.PopID();
        }

        if (!string.IsNullOrWhiteSpace(questionId) && Chrome.Button("Remove a point") && app.Session.TriviaPlayerId is not null)
            Try(app, ev, game, edit => TriviaScore.RemoveLatest(edit, app.Session.TriviaPlayerId));
        ImGui.SameLine();
        if (Chrome.Button("Undo last point"))
            Try(app, ev, game, edit => TriviaScore.Undo(edit));
        ImGui.SameLine();
        if (Chrome.Button("Add 1 manual point") && app.Session.TriviaPlayerId is not null)
        {
            var playerId = app.Session.TriviaPlayerId;
            app.Ask("Add 1 point that is not tied to a question?", () =>
                Try(app, ev, game, edit => TriviaScore.Adjust(edit, playerId, 1)));
        }
    }

    private static void Heard(HorizonApp app, EventDocument ev, GameInstance game, GameProgress progress, string? questionId)
    {
        Chrome.Section("Recent answers");
        var all = app.Session.TriviaAllAnswers;
        if (ImGui.Checkbox("Show every question", ref all))
            app.Session.TriviaAllAnswers = all;
        var heard = progress.Trivia?.Heard ?? [];
        var shown = heard.Where(item => all || item.QuestionId == questionId).OrderBy(item => item.Order).ToList();
        if (shown.Count == 0)
        {
            Chrome.Muted("No answers yet. The first line is the first one received.");
            return;
        }

        ImGui.BeginChild("trivia-heard", new System.Numerics.Vector2(0, 140), true);
        foreach (var item in shown)
        {
            ImGui.PushID(item.Order);
            ImGui.TextDisabled(item.Order.ToString());
            ImGui.SameLine();
            ImGui.TextUnformatted(UnoTournament.Label(item.Name, item.World));
            ImGui.SameLine();
            if (Chrome.Button("Award"))
            {
                var target = item.QuestionId ?? questionId;
                if (target is null)
                    app.Fail("Open a question before awarding a point.");
                else
                {
                    string? playerId = null;
                    Edit(app, ev, game, edit => playerId = TriviaScore.Ensure(edit, item.Name, item.World).PlayerId);
                    if (playerId is not null)
                    {
                        app.Session.TriviaPlayerId = playerId;
                        Give(app, ev, game, target, playerId);
                    }
                }
            }

            ImGui.TextWrapped(item.Message);
            ImGui.PopID();
        }

        ImGui.EndChild();
    }

    private static void Board(HorizonApp app, EventDocument ev, GameInstance game, GameProgress progress, IReadOnlyList<TriviaRow> rows)
    {
        Chrome.Section("Leaderboard");
        if (rows.Count == 0)
        {
            Chrome.Muted("No players yet.");
            return;
        }

        if (ImGui.BeginTable("trivia-board", 4))
        {
            ImGui.TableSetupColumn("Rank");
            ImGui.TableSetupColumn("Player");
            ImGui.TableSetupColumn("Points");
            ImGui.TableSetupColumn("Correct");
            ImGui.TableHeadersRow();
            foreach (var row in rows)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                var color = row.Rank == 1 ? ThemeHolder.Gold : ThemeHolder.Text;
                ImGui.TextColored(color, row.Rank.ToString());
                ImGui.TableNextColumn();
                ImGui.TextColored(color, row.Label);
                ImGui.TableNextColumn();
                ImGui.TextColored(color, row.Points.ToString());
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(row.Correct.ToString());
            }

            ImGui.EndTable();
        }

        var leaders = rows.Where(row => row.Rank == 1).ToList();
        if (leaders.Count > 1)
        {
            ImGui.TextColored(ThemeHolder.Gold, "Tiebreaker needed.");
            var prompt = app.Session.TiePrompt;
            if (Chrome.Field("Tiebreaker question", prompt, value => app.Session.TiePrompt = value ?? ""))
                _ = prompt;
            var answer = app.Session.TieAnswer;
            if (Chrome.Field("Tiebreaker answer", answer, value => app.Session.TieAnswer = value ?? ""))
                _ = answer;
            if (Chrome.Accent("Add tiebreaker") && !string.IsNullOrWhiteSpace(app.Session.TiePrompt))
            {
                var text = app.Session.TiePrompt;
                var hidden = app.Session.TieAnswer;
                Edit(app, ev, game, edit => TriviaScore.AddTiebreak(game, edit, text, hidden));
                app.Session.TiePrompt = "";
                app.Session.TieAnswer = "";
                app.Note("Tiebreaker added. Award one point when someone gets it.");
            }
        }

        if (progress.Trivia is { Ended: true } && leaders.Count == 1)
        {
            ImGui.Spacing();
            ImGui.TextColored(ThemeHolder.Gold, "Champion");
            ImGui.TextUnformatted(leaders[0].Label + "  ·  " + leaders[0].Points + " points");
            ImGui.TextDisabled(TriviaScore.PrizeLabel(ev, game));
        }
    }

    private static void Copies(HorizonApp app, EventDocument ev, GameInstance game, IReadOnlyList<TriviaRow> rows, int done, int left)
    {
        Chrome.Muted("Copy scores to chat, then paste them in. Nothing is sent from here.");
        var prize = TriviaScore.PrizeLabel(ev, game);
        Copy(app, "trivia-top-" + game.InstanceId, "Copy top 3", TriviaScore.TopThree(rows, done, left));
        ImGui.SameLine();
        Copy(app, "trivia-all-" + game.InstanceId, "Copy all scores", TriviaScore.AllScores(rows));
        ImGui.SameLine();
        Copy(app, "trivia-now-" + game.InstanceId, "Copy current standings", TriviaScore.Standings(rows, left));
        Copy(app, "trivia-final-" + game.InstanceId, "Copy final results", TriviaScore.FinalResults(rows, prize));
    }

    private static void Copy(HorizonApp app, string key, string label, IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
        {
            if (Chrome.Button(label + "##" + key))
                app.Fail("There are no scores to copy yet.");
            return;
        }

        if (lines.Any(line => line.Length > TriviaScore.YellLimit))
            ImGui.TextDisabled("One yell is longer than chat allows. The names were not shortened.");
        ChatCopy.Button(app, key, lines, label, "There are no scores to copy yet.");
    }

    public static void Clock(HorizonApp app, EventDocument ev, GameInstance game, string questionId, float fontScale)
    {
        var state = app.Store.StateFor(ev.EventId);
        var live = HostController.EditGame(state, game.InstanceId);
        if (live.Trivia is { Ended: true })
            return;

        var now = DateTimeOffset.Now;
        if (TriviaScore.Arm(live, questionId, now, false))
            app.Store.MarkState(ev.EventId);

        var clock = TriviaScore.ReadClock(live, questionId, now);
        var boost = clock.Final ? 2.8f : clock.Done ? 2.2f : 1.8f;
        ImGui.SetWindowFontScale(Math.Max(0.8f, fontScale) * boost);
        var color = clock.Final || clock.Done ? ThemeHolder.Pink : ThemeHolder.Gold;
        ImGui.TextColored(color, clock.Label);
        ImGui.SetWindowFontScale(fontScale);
        ImGui.TextDisabled(clock.Final ? "Last five seconds" : clock.Done ? "Time is up" : "1:00 for this question");
        if (Chrome.Button("Restart timer##" + questionId))
        {
            TriviaScore.Arm(live, questionId, DateTimeOffset.Now, true);
            app.Store.MarkState(ev.EventId);
        }
    }

    private static void LeaderLine(IReadOnlyList<TriviaRow> rows)
    {
        var leaders = rows.Where(row => row.Rank == 1).ToList();
        if (leaders.Count == 0)
            ImGui.TextDisabled("No leader yet.");
        else if (leaders.Count == 1)
            ImGui.TextColored(ThemeHolder.Gold, "Leader: " + leaders[0].Label + " (" + leaders[0].Points + ")");
        else
            ImGui.TextColored(ThemeHolder.Gold, "Tied: " + string.Join(", ", leaders.Select(row => row.Label)) + " (" + leaders[0].Points + ")");
    }

    private static void Give(HorizonApp app, EventDocument ev, GameInstance game, string questionId, string? playerId)
    {
        if (playerId is null)
        {
            app.Fail("Choose the player who answered first.");
            return;
        }

        Try(app, ev, game, edit => TriviaScore.Award(edit, questionId, playerId));
    }

    private static void GiveMove(HorizonApp app, EventDocument ev, GameInstance game, string questionId, string playerId) =>
        Try(app, ev, game, edit => TriviaScore.Reassign(edit, questionId, playerId));

    private static void Try(HorizonApp app, EventDocument ev, GameInstance game, Func<GameProgress, string?> change)
    {
        string? message = null;
        var state = app.Store.StateFor(ev.EventId);
        var progress = HostController.EditGame(state, game.InstanceId);
        message = change(progress);
        TriviaScore.LinkPrize(ev, game, progress, state);
        app.Store.MarkState(ev.EventId);
        if (message is not null)
            app.Fail(message);
    }

    private static void Edit(HorizonApp app, EventDocument ev, GameInstance game, Action<GameProgress> change)
    {
        var state = app.Store.StateFor(ev.EventId);
        var progress = HostController.EditGame(state, game.InstanceId);
        change(progress);
        app.Store.MarkState(ev.EventId);
    }
}
