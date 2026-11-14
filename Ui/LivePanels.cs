using System.Numerics;
using Dalamud.Bindings.ImGui;
using EventHorizon.Hosting;
using EventHorizon.Models;
using EventHorizon.Theme;

namespace EventHorizon.Ui;

internal static class GameBoard
{
    public static void Draw(HorizonApp app, EventDocument ev, GameInstance game)
    {
        var state = app.Store.StateFor(ev.EventId);
        var progress = HostController.PeekGame(state, game.InstanceId);
        if (game.Rounds.Count > 0)
            progress.RoundIndex = Math.Clamp(progress.RoundIndex, 0, game.Rounds.Count - 1);

        if (SendOrDelete.Is(game))
        {
            SendShow(app, ev, game, progress);
            return;
        }

        if (TwoTruths.Is(game))
        {
            TruthShow(app, ev, game, progress);
            return;
        }

        ImGui.TextColored(ThemeHolder.Accent, game.Name);
        if (game.Formats.Count > 0)
            ImGui.TextDisabled(string.Join("  ·  ", game.Formats.Select(format => Persistence.Labels.Spaced(format.ToString()))));
        if (!string.IsNullOrWhiteSpace(game.Rules))
            Chrome.Muted(game.Rules);

        if (TriviaScore.IsTrivia(game))
        {
            TriviaPanel.Draw(app, ev, game, progress);
            return;
        }

        if (!UnoTournament.IsUno(game) && UsesRoster(game))
            Registration(app, ev, game, progress);

        var round = HostController.CurrentRound(game, progress);
        if (round is not null)
        {
            ImGui.TextDisabled("Round " + (Math.Clamp(progress.RoundIndex, 0, game.Rounds.Count - 1) + 1) + " / " + game.Rounds.Count);
            if (!string.IsNullOrWhiteSpace(round.Title))
                ImGui.TextUnformatted(round.Title);
            Prompt(app, round);
            if (game.Formats.Contains(GameFormat.MultipleChoice) || game.Formats.Contains(GameFormat.Voting) || round.Choices.Count > 0)
                Choices(app, ev, game, progress, round);
            Answer(app, ev, game, progress, round);
            if (Chrome.Button("Previous round"))
                Mutate(app, ev, game, edit => HostController.PreviousRound(edit));
            ImGui.SameLine();
            if (Chrome.Button("Next round"))
                Mutate(app, ev, game, edit => HostController.NextRound(game, edit));
        }
        else if (!game.Formats.Contains(GameFormat.Tournament))
        {
            Chrome.Muted("This game has no rounds. Use it for instructions or manual scoring.");
        }

        if (UnoTournament.IsUno(game))
        {
            UnoPanel.Draw(app, ev, game, progress);
            return;
        }

        if (game.Formats.Contains(GameFormat.Collection))
            Collection(app, ev, game, progress);
        if (UsesRoster(game))
            Players(app, ev, game, progress);
        if (game.Formats.Contains(GameFormat.Tournament))
            Bracket(app, ev, game, progress);

        if (KeepsScore(game) && progress.Winners.Count > 0)
            ImGui.TextColored(ThemeHolder.Gold, "Winner: " + string.Join(", ", progress.Winners));
    }

    public static void Compact(HorizonApp app, EventDocument ev, GameInstance game)
    {
        var state = app.Store.StateFor(ev.EventId);
        var progress = HostController.PeekGame(state, game.InstanceId);
        if (TriviaScore.IsTrivia(game))
        {
            var asked = TriviaScore.Current(game, progress);
            ImGui.TextDisabled(TriviaScore.Heading(game, progress));
            if (asked is { } question)
            {
                TriviaPanel.Clock(app, ev, game, question.Id, Math.Max(1f, app.Store.Config.UiScale));
                if (!string.IsNullOrWhiteSpace(question.Prompt))
                    ImGui.TextWrapped(question.Prompt);
                ChatCopy.Button(app, "trivia-question-" + game.InstanceId + "-" + question.Id, TriviaScore.QuestionYell(game, progress, question), "Copy question", "This question is empty.");
                ImGui.SameLine();
                if (Chrome.Button("Announce the point##compact-" + question.Id))
                    TriviaPanel.AnnouncePoint(app, game, progress, question);
                var answerHidden = !string.IsNullOrWhiteSpace(question.Answer) && !HostController.IsRevealed(progress, question.Id);
                if (answerHidden && Chrome.Accent("Reveal"))
                    Mutate(app, ev, game, edit => HostController.Reveal(edit, question.Id));
                if (!answerHidden && !string.IsNullOrWhiteSpace(question.Answer))
                    ImGui.TextColored(ThemeHolder.Mint, question.Answer);
            }

            var leaders = TriviaScore.Board(progress).Where(row => row.Rank == 1).ToList();
            ImGui.TextColored(ThemeHolder.Gold, leaders.Count == 0 ? "No trivia leader yet." : "Leader: " + string.Join(", ", leaders.Select(row => row.Label + " " + row.Points)));
            if (Chrome.Button("Next"))
                Mutate(app, ev, game, edit => TriviaScore.Move(game, edit, 1));
            return;
        }

        if (SendOrDelete.Is(game))
        {
            SendCompact(app, ev, game, progress);
            return;
        }

        if (TwoTruths.Is(game))
        {
            TruthCompact(app, ev, game, progress);
            return;
        }

        var round = HostController.CurrentRound(game, progress);
        ImGui.TextDisabled("Round " + (game.Rounds.Count == 0 ? 0 : Math.Clamp(progress.RoundIndex, 0, game.Rounds.Count - 1) + 1) + " / " + game.Rounds.Count);
        if (round is null)
            return;

        if (!string.IsNullOrWhiteSpace(round.FeaturedStaff))
        {
            ImGui.TextDisabled("Featured staff");
            ImGui.TextUnformatted(round.FeaturedStaff);
        }

        Prompt(app, round);
        var hidden = !string.IsNullOrWhiteSpace(round.CorrectAnswer) && !HostController.IsRevealed(progress, round.RoundId);
        if (hidden && Chrome.Accent("Reveal"))
            Mutate(app, ev, game, edit => HostController.Reveal(edit, round.RoundId));
        if (!hidden && !string.IsNullOrWhiteSpace(round.CorrectAnswer))
            ImGui.TextColored(ThemeHolder.Mint, round.CorrectAnswer);
        ImGui.SameLine();
        if (Chrome.Button("Next"))
            Mutate(app, ev, game, edit => HostController.NextRound(game, edit));
    }

    private static void SendShow(HorizonApp app, EventDocument ev, GameInstance game, GameProgress progress)
    {
        ImGui.TextColored(ThemeHolder.Accent, game.Name);
        Chrome.Muted("Copy the chirp, then the question. Pick SEND or DELETE once the room has answered.");
        Registration(app, ev, game, progress);
        var round = HostController.CurrentRound(game, progress);
        if (round is null)
        {
            Chrome.Muted("This game has no chirps yet.");
        }
        else
        {
            SendRound(app, ev, game, progress, round, compact: false);
            if (Chrome.Button("Previous round"))
                Mutate(app, ev, game, edit => HostController.PreviousRound(edit));
            ImGui.SameLine();
            if (Chrome.Button("Next round"))
                Mutate(app, ev, game, edit => HostController.NextRound(game, edit));
            ImGui.SameLine();
            if (Chrome.Button("Announce the winner##send"))
                AnnounceWinner(app, ev, game, progress);
        }

        Players(app, ev, game, progress);
    }

    private static void SendCompact(HorizonApp app, EventDocument ev, GameInstance game, GameProgress progress)
    {
        var round = HostController.CurrentRound(game, progress);
        if (round is null)
        {
            Chrome.Muted("This game has no chirps yet.");
            return;
        }

        SendRound(app, ev, game, progress, round, compact: true);
        if (Chrome.Button("Next"))
            Mutate(app, ev, game, edit => HostController.NextRound(game, edit));
        ImGui.SameLine();
        if (Chrome.Button("Announce the winner##send-compact"))
            AnnounceWinner(app, ev, game, progress);
    }

    private static void SendRound(HorizonApp app, EventDocument ev, GameInstance game, GameProgress progress, GameRound round, bool compact)
    {
        var index = Math.Clamp(progress.RoundIndex, 0, Math.Max(0, game.Rounds.Count - 1));
        ImGui.TextDisabled("Chirp " + (index + 1) + " / " + game.Rounds.Count);
        var staff = round.FeaturedStaff;
        if (Chrome.Field("Staff", staff, value => round.FeaturedStaff = value))
            app.Store.MarkEvent(ev.EventId);

        var chirp = SendOrDelete.Chirp(round);
        if (chirp.Length == 0)
            Chrome.Muted("This chirp is empty.");
        else
            ImGui.TextWrapped(chirp);

        ChatCopy.Button(app, "send-chirp-" + round.RoundId, SendOrDelete.ChirpYell(round), "Copy chirp", "This chirp is empty.");
        ImGui.SameLine();
        ChatCopy.Button(app, "send-question-" + round.RoundId, SendOrDelete.QuestionYell, "Copy the question", "This question is empty.");

        var answer = (round.CorrectAnswer ?? "").Trim();
        var sent = answer.Equals("SEND", StringComparison.OrdinalIgnoreCase);
        var deleted = answer.Equals("DELETE", StringComparison.OrdinalIgnoreCase);
        if (sent ? Chrome.Accent("SEND##" + round.RoundId) : Chrome.Button("SEND##" + round.RoundId))
            Choose(app, ev, game, round, "SEND");
        ImGui.SameLine();
        if (deleted ? Chrome.Accent("DELETE##" + round.RoundId) : Chrome.Button("DELETE##" + round.RoundId))
            Choose(app, ev, game, round, "DELETE");
        if (sent || deleted)
        {
            ImGui.SameLine();
            ChatCopy.Button(app, "send-chose-" + round.RoundId, SendOrDelete.ChoseYell(round), "Copy what they chose", "Pick SEND or DELETE first.");
            if (!compact)
                ImGui.TextColored(ThemeHolder.Mint, SendOrDelete.StaffName(round) is { Length: > 0 } name ? name + " chose " + answer + "." : "They chose " + answer + ".");
        }
    }

    private static void Choose(HorizonApp app, EventDocument ev, GameInstance game, GameRound round, string answer)
    {
        round.CorrectAnswer = answer;
        app.Store.MarkEvent(ev.EventId);
        Mutate(app, ev, game, edit => HostController.Reveal(edit, round.RoundId));
        var yell = SendOrDelete.ChoseYell(round);
        if (yell.Length > 0)
            ImGui.SetClipboardText(yell);
        var who = SendOrDelete.StaffName(round);
        app.Note((who.Length == 0 ? "They" : who) + " chose " + answer + ". The line is copied.");
    }

    private static void TruthShow(HorizonApp app, EventDocument ev, GameInstance game, GameProgress progress)
    {
        Chrome.Section("Two Truths");
        var round = HostController.CurrentRound(game, progress);
        var count = game.Rounds.Count;
        var index = count == 0 ? 0 : Math.Clamp(progress.RoundIndex, 0, count - 1) + 1;
        ImGui.TextUnformatted("Round " + index + " of " + count);
        TruthLeader(progress);
        if (round is null)
            Chrome.Muted("This game has no rounds yet.");
        else
            TruthRound(app, ev, game, progress, round, compact: false);

        var ears = TwoTruths.Listening(app.Store);
        Chrome.Muted(ears?.Game.InstanceId == game.InstanceId
            ? "Listening for say, yell, party, and alliance. Points stay with you."
            : "Two Truths listens while this game is open and hosting is on.");
        TruthHeard(app, ev, game, progress, round?.RoundId);
        Registration(app, ev, game, progress);
        Players(app, ev, game, progress);
        TruthBoard(app, ev, game, progress);
    }

    private static void TruthCompact(HorizonApp app, EventDocument ev, GameInstance game, GameProgress progress)
    {
        var round = HostController.CurrentRound(game, progress);
        var count = game.Rounds.Count;
        var index = count == 0 ? 0 : Math.Clamp(progress.RoundIndex, 0, count - 1) + 1;
        ImGui.TextDisabled("Round " + index + " of " + count);
        if (round is null)
        {
            Chrome.Muted("This game has no rounds yet.");
            return;
        }

        TruthRound(app, ev, game, progress, round, compact: true);
        TruthLeader(progress);
    }

    private static void TruthRound(HorizonApp app, EventDocument ev, GameInstance game, GameProgress progress, GameRound round, bool compact)
    {
        if (!compact)
            TriviaPanel.Clock(app, ev, game, round.RoundId, app.Store.Config.UiScale, "round");
        else
            TriviaPanel.Clock(app, ev, game, round.RoundId, Math.Max(1f, app.Store.Config.UiScale), "round");

        var paragraph = RoundCue.Paste(ev.Scripts, round);
        if (paragraph.Length == 0)
            Chrome.Muted("This round has no chirps yet.");
        else
            ImGui.TextWrapped(paragraph);
        ChatCopy.Button(app, "truth-round-" + round.RoundId, TwoTruths.RoundYell(ev.Scripts, round), "Copy round", "This round is empty.");

        var answer = TwoTruths.Answer(round);
        var revealed = HostController.IsRevealed(progress, round.RoundId);
        if (answer.Length != 1)
        {
            if (!compact)
                Chrome.Muted("This round has no answer in the file yet.");
        }
        else if (!revealed)
        {
            ImGui.TextColored(ThemeHolder.Gold, "Answer hidden. Only this window can reveal it.");
            if (Chrome.Accent("Reveal answer"))
                Mutate(app, ev, game, edit => HostController.Reveal(edit, round.RoundId));
        }
        else
        {
            ImGui.TextDisabled("The fake");
            ImGui.SameLine();
            ImGui.TextColored(ThemeHolder.Mint, answer);
            if (Chrome.Button("Hide answer"))
                Mutate(app, ev, game, edit => HostController.Hide(edit, round.RoundId));
        }

        if (answer.Length == 1)
        {
            ImGui.SameLine();
            if (Chrome.Button("Copy answer##truth-answer-" + round.RoundId))
            {
                var cursor = app.Session.TruthAnswer;
                var line = TwoTruths.NextAnswer(round, ref cursor);
                app.Session.TruthAnswer = cursor;
                if (line.Length == 0)
                    app.Fail("This round has no answer in the file yet.");
                else
                {
                    ImGui.SetClipboardText(line);
                    app.Note("Copied.");
                }
            }
        }

        if (Chrome.Button(compact ? "Previous" : "Previous round"))
            Mutate(app, ev, game, edit => HostController.PreviousRound(edit));
        ImGui.SameLine();
        if (Chrome.Button(compact ? "Next" : "Next round"))
            Mutate(app, ev, game, edit => HostController.NextRound(game, edit));
    }

    private static void TruthHeard(HorizonApp app, EventDocument ev, GameInstance game, GameProgress progress, string? roundId)
    {
        Chrome.Section("Recent answers");
        var all = app.Session.TriviaAllAnswers;
        if (ImGui.Checkbox("Show every round", ref all))
            app.Session.TriviaAllAnswers = all;
        var heard = progress.Trivia?.Heard ?? [];
        var shown = heard.Where(item => all || item.QuestionId == roundId).OrderBy(item => item.Order).ToList();
        if (shown.Count == 0)
        {
            Chrome.Muted("No answers yet. The first line is the first one received.");
            return;
        }

        ImGui.BeginChild("truth-heard", new Vector2(0, 140), true);
        foreach (var item in shown)
        {
            ImGui.PushID(item.Order);
            ImGui.TextDisabled(item.Order.ToString());
            ImGui.SameLine();
            ImGui.TextUnformatted(UnoTournament.Label(item.Name, item.World));
            ImGui.SameLine();
            if (Chrome.Button("Award"))
            {
                var person = item.Name;
                var home = item.World;
                Mutate(app, ev, game, edit =>
                {
                    var player = TriviaScore.Ensure(edit, person, home);
                    player.Score += 1;
                });
            }

            ImGui.TextWrapped(item.Message);
            ImGui.PopID();
        }

        ImGui.EndChild();
    }

    private static void TruthLeader(GameProgress progress)
    {
        var leaders = TwoTruths.Board(progress).Where(row => row.Rank == 1 && row.Points > 0).ToList();
        if (leaders.Count == 0)
            ImGui.TextDisabled("No leader yet.");
        else if (leaders.Count == 1)
            ImGui.TextColored(ThemeHolder.Gold, "Leader: " + leaders[0].Label + " (" + leaders[0].Points + ")");
        else
            ImGui.TextColored(ThemeHolder.Gold, "Tied: " + string.Join(", ", leaders.Select(row => row.Label)) + " (" + leaders[0].Points + ")");
    }

    private static void TruthBoard(HorizonApp app, EventDocument ev, GameInstance game, GameProgress progress)
    {
        Chrome.Section("Leaderboard");
        var rows = TwoTruths.Board(progress);
        var total = game.Rounds.Count;
        var done = total == 0 ? 0 : Math.Clamp(progress.RoundIndex, 0, total - 1) + 1;
        var left = Math.Max(0, total - done);
        Chrome.Muted("Copy the leaderboard to chat, then paste it in. Nothing is sent from here.");
        ChatCopy.Button(app, "truth-top-" + game.InstanceId, TwoTruths.TopThree(rows, done, left), "Copy top 3", "There are no scores to copy yet.");
        ImGui.SameLine();
        ChatCopy.Button(app, "truth-all-" + game.InstanceId, TwoTruths.AllScores(rows), "Copy all scores", "There are no scores to copy yet.");
        ImGui.SameLine();
        ChatCopy.Button(app, "truth-now-" + game.InstanceId, TwoTruths.Standings(rows, left), "Copy current standings", "There are no scores to copy yet.");
        ChatCopy.Button(app, "truth-final-" + game.InstanceId, TwoTruths.WinnerYell(ev.Scripts, rows), "Copy final results", "There are no scores to copy yet.");
        if (rows.Count == 0)
        {
            Chrome.Muted("No players yet.");
            return;
        }

        if (ImGui.BeginTable("truth-board", 3))
        {
            ImGui.TableSetupColumn("Rank");
            ImGui.TableSetupColumn("Player");
            ImGui.TableSetupColumn("Points");
            ImGui.TableHeadersRow();
            foreach (var row in rows)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                var color = row.Rank == 1 && row.Points > 0 ? ThemeHolder.Gold : ThemeHolder.Text;
                ImGui.TextColored(color, row.Rank.ToString());
                ImGui.TableNextColumn();
                ImGui.TextColored(color, row.Label);
                ImGui.TableNextColumn();
                ImGui.TextColored(color, row.Points.ToString());
            }

            ImGui.EndTable();
        }
    }

    private static void Prompt(HorizonApp app, GameRound round)
    {
        var shown = new HashSet<string>(StringComparer.Ordinal);
        if (round.Extras is not null)
        {
            foreach (var pair in round.Extras)
            {
                if (string.IsNullOrWhiteSpace(pair.Value) || !shown.Add(pair.Value))
                    continue;
                var chirp = pair.Key.Equals("chirp", StringComparison.OrdinalIgnoreCase);
                ImGui.TextDisabled(Persistence.Labels.Spaced(pair.Key));
                if (chirp)
                {
                    ImGui.SameLine();
                    ChatCopy.Button(app, "chirp-" + round.RoundId, CrowdChirp(round, pair.Value), "Copy chirp", "This chirp is empty.");
                }

                ImGui.TextWrapped(pair.Value);
            }
        }

        if (!string.IsNullOrWhiteSpace(round.Prompt) && shown.Add(round.Prompt))
            ImGui.TextWrapped(round.Prompt);
        if (!string.IsNullOrWhiteSpace(round.FeaturedStaff) && round.Extras is null)
            ImGui.TextDisabled("Featured staff  ·  " + round.FeaturedStaff);
        if (!string.IsNullOrWhiteSpace(round.ImagePath))
            ImGui.TextDisabled(round.ImagePath);
    }

    private static string ChirpLabel(string choice)
    {
        var text = choice.Trim();
        return text.Length > 0 && char.IsLetter(text[0]) && (text.Length == 1 || text[1] is '.' or ')' or ':')
            ? text[0].ToString()
            : "the chirp";
    }

    private static string ChirpText(string choice)
    {
        var text = choice.Trim();
        if (text.Length >= 2 && char.IsLetter(text[0]) && text[1] is '.' or ')' or ':')
        {
            var body = text[2..].Trim();
            if (body.Length > 0)
                return body;
        }

        return text;
    }

    private static string CrowdChirp(GameRound round, string chirp)
    {
        var choices = round.Choices.Where(choice => !string.IsNullOrWhiteSpace(choice)).ToList();
        var ask = choices.Count >= 2
            ? "Would they " + string.Join(" or ", choices) + "?"
            : "Would they SEND or DELETE?";
        return chirp.Trim() + "\n" + ask;
    }

    private static void Choices(HorizonApp app, EventDocument ev, GameInstance game, GameProgress progress, GameRound round)
    {
        var lettered = round.Choices.Any(choice => ChirpLabel(choice).Length == 1);
        if (lettered && Chrome.Button("Copy cue"))
        {
            var cue = RoundCue.Paste(ev.Scripts, round);
            if (string.IsNullOrWhiteSpace(cue))
                app.Fail("This round has no cue to copy.");
            else
            {
                ImGui.SetClipboardText(cue);
                app.Note("Copied the round cue.");
            }
        }

        foreach (var choice in round.Choices.Where(choice => !string.IsNullOrWhiteSpace(choice)))
        {
            var fill = Chrome.Mix(ThemeHolder.Button, ThemeHolder.Accent, 0.28f);
            var hover = Chrome.Mix(ThemeHolder.ButtonHover, ThemeHolder.Accent, 0.4f);
            var label = ChirpLabel(choice);
            if (Chrome.Button(choice, new Vector2(Math.Max(120, ImGui.CalcTextSize(choice).X + 24), 0), fill, ThemeHolder.Text, hover) && (label.Length == 1 || !KeepsScore(game)))
            {
                var chirp = ChirpText(choice);
                ImGui.SetClipboardText(chirp);
                app.Note(string.IsNullOrWhiteSpace(chirp) ? "This chirp is empty." : "Copied " + label + ".");
            }

            if (!game.Formats.Contains(GameFormat.Voting))
                continue;

            var key = round.RoundId + "\n" + choice;
            progress.Votes.TryGetValue(key, out var count);
            ImGui.SameLine();
            if (Chrome.Button("-##" + choice))
                Mutate(app, ev, game, edit => edit.Votes[key] = Math.Max(0, count - 1));
            ImGui.SameLine();
            ImGui.TextUnformatted(count.ToString());
            ImGui.SameLine();
            if (Chrome.Button("+##" + choice))
                Mutate(app, ev, game, edit => edit.Votes[key] = count + 1);
        }

        if (round.Choices.Count > 0 && KeepsScore(game))
            Chrome.Muted("Choosing an option does not reveal the answer or change a score.");
    }

    private static void Answer(HorizonApp app, EventDocument ev, GameInstance game, GameProgress progress, GameRound round)
    {
        if (string.IsNullOrWhiteSpace(round.CorrectAnswer) && string.IsNullOrWhiteSpace(round.Explanation))
            return;

        var revealed = HostController.IsRevealed(progress, round.RoundId);
        if (!revealed)
        {
            ImGui.TextColored(ThemeHolder.Gold, "Answer hidden");
            if (Chrome.Accent("Reveal answer"))
                Mutate(app, ev, game, edit => HostController.Reveal(edit, round.RoundId));
            return;
        }

        ImGui.TextColored(ThemeHolder.Mint, round.CorrectAnswer ?? "");
        if (!string.IsNullOrWhiteSpace(round.Explanation))
            ImGui.TextWrapped(round.Explanation);
        if (!string.IsNullOrWhiteSpace(round.SourceReference))
            ImGui.TextDisabled(round.SourceReference);
        if (Chrome.Button("Hide answer"))
            Mutate(app, ev, game, edit => HostController.Hide(edit, round.RoundId));
    }

    private static void Collection(HorizonApp app, EventDocument ev, GameInstance game, GameProgress progress)
    {
        Chrome.Section("Hidden items");
        Chrome.Muted("A tell with every word counts. Capitalization and word order do not matter. Nothing is sent back.");
        if (progress.Winners.Count > 0)
            ImGui.TextColored(ThemeHolder.Gold, "Winner: " + string.Join(", ", progress.Winners));
        foreach (var round in game.Rounds)
        {
            var revealed = HostController.IsRevealed(progress, round.RoundId);
            ImGui.TextUnformatted(revealed ? (round.Prompt ?? round.Title ?? round.RoundId) : "Hidden");
            ImGui.SameLine();
            if (Chrome.Button((revealed ? "Hide " : "Reveal ") + round.RoundId))
            {
                Mutate(app, ev, game, edit =>
                {
                    if (revealed)
                        HostController.Hide(edit, round.RoundId);
                    else
                        HostController.Reveal(edit, round.RoundId);
                });
            }
        }
    }

    private static bool UsesRoster(GameInstance game) =>
        game.Formats.Count == 0
        || game.Formats.Contains(GameFormat.MultipleChoice)
        || game.Formats.Contains(GameFormat.Voting)
        || game.Formats.Contains(GameFormat.Points)
        || game.Formats.Contains(GameFormat.Elimination)
        || game.Formats.Contains(GameFormat.Custom)
        || game.Formats.Contains(GameFormat.Tournament)
        || game.Formats.Contains(GameFormat.QuestionAnswer);

    private static void Registration(HorizonApp app, EventDocument ev, GameInstance game, GameProgress progress)
    {
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
                Mutate(app, ev, game, edit => edit.RegistrationOpen = true);
                app.Note("Registration is open. A tell that says " + word + " will add that character. Capitalization does not matter.");
            }
        }
        else if (progress.RegistrationOpen && Chrome.Button("Close registration"))
        {
            Mutate(app, ev, game, edit => edit.RegistrationOpen = false);
            app.Note("Registration is closed.");
        }
    }

    private static bool TracksPoints(GameInstance game) => TwoTruths.Is(game);

    private static void AnnounceWinner(HorizonApp app, EventDocument ev, GameInstance game, GameProgress progress)
    {
        var yell = SendOrDelete.Yell(ev, game, progress);
        if (yell.Length == 0)
        {
            app.Fail("Leave one player standing before announcing the winner.");
            return;
        }

        try
        {
            foreach (var line in ScriptSpeech.Fit(yell))
                CrowdYell.Send(line);
            app.Note("Announced the winner.");
        }
        catch (Exception ex)
        {
            app.Log.Error(ex, "Could not yell the Send or Delete winner.");
            app.Fail(ex.Message);
        }
    }

    private static bool KeepsScore(GameInstance game)
    {
        if (game.Formats.Contains(GameFormat.Points) || game.Formats.Contains(GameFormat.QuestionAnswer))
            return true;

        var method = game.ScoringMethod ?? "";
        return !method.Contains("no point", StringComparison.OrdinalIgnoreCase)
            && method.Contains("point", StringComparison.OrdinalIgnoreCase);
    }

    private static void Players(HorizonApp app, EventDocument ev, GameInstance game, GameProgress progress)
    {
        var pointsGame = TracksPoints(game);
        var scored = KeepsScore(game) || pointsGame;
        Chrome.Section("Players");
        if (!scored)
            Chrome.Muted("Check Eliminated when they sit down. The last player still in is the winner.");
        var playerName = app.Session.PlayerName;
        ImGui.TextDisabled("Player name");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(180);
        if (ImGui.InputText("##player", ref playerName, 80))
            app.Session.PlayerName = playerName;
        ImGui.SameLine();
        if (Chrome.Button("Add player") && !string.IsNullOrWhiteSpace(app.Session.PlayerName))
        {
            var name = app.Session.PlayerName;
            Mutate(app, ev, game, edit =>
            {
                HostController.AddPlayer(edit, name);
                if (!scored)
                    HostController.SeatWinner(edit);
            });
            app.Session.PlayerName = "";
        }

        if (!scored)
        {
            var standing = progress.Players.Where(player => !player.Eliminated && !string.IsNullOrWhiteSpace(player.Name)).Select(player => player.Name.Trim()).ToList();
            if (progress.Winners.Count == 1)
                ImGui.TextColored(ThemeHolder.Gold, "Winner: " + progress.Winners[0]);
            else if (standing.Count > 1)
                ImGui.TextUnformatted("Still in: " + string.Join(", ", standing));
            else if (progress.Players.Any(player => !string.IsNullOrWhiteSpace(player.Name)))
                Chrome.Muted("Everyone is sitting down.");
            if (SendOrDelete.Is(game) && Chrome.Button("Announce the winner"))
                AnnounceWinner(app, ev, game, progress);
        }

        string? remove = null;
        var roster = scored ? progress.Players : progress.Players.OrderBy(player => player.Eliminated).ToList();
        foreach (var player in roster)
        {
            ImGui.PushID(player.PlayerId);
            var name = player.Name;
            ImGui.TextDisabled("Name");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(160);
            if (ImGui.InputText("##pname", ref name, 80))
            {
                var written = name;
                Mutate(app, ev, game, edit =>
                {
                    edit.Players.First(item => item.PlayerId == player.PlayerId).Name = written;
                    if (!scored)
                        HostController.SeatWinner(edit);
                });
            }

            ImGui.SameLine();
            if (scored)
            {
                ImGui.TextDisabled(pointsGame ? "Points" : "Score");
                ImGui.SameLine();
                var score = player.Score;
                ImGui.SetNextItemWidth(80);
                if (ImGui.InputInt("##score", ref score))
                    Mutate(app, ev, game, edit => edit.Players.First(item => item.PlayerId == player.PlayerId).Score = score);
                if (pointsGame)
                {
                    ImGui.SameLine();
                    if (Chrome.Button("+1"))
                    {
                        var next = player.Score + 1;
                        Mutate(app, ev, game, edit => edit.Players.First(item => item.PlayerId == player.PlayerId).Score = next);
                    }
                }
                if (!pointsGame)
                {
                    ImGui.SameLine();
                    var eliminated = player.Eliminated;
                    if (ImGui.Checkbox("Out", ref eliminated))
                        Mutate(app, ev, game, edit => edit.Players.First(item => item.PlayerId == player.PlayerId).Eliminated = eliminated);
                    ImGui.SameLine();
                    if (Chrome.Button("Winner"))
                    {
                        Mutate(app, ev, game, edit =>
                        {
                            if (!edit.Winners.Contains(player.Name))
                                edit.Winners.Add(player.Name);
                        });
                    }
                }
            }
            else
            {
                var eliminated = player.Eliminated;
                if (ImGui.Checkbox("Eliminated", ref eliminated))
                {
                    var sitting = eliminated;
                    Mutate(app, ev, game, edit =>
                    {
                        edit.Players.First(item => item.PlayerId == player.PlayerId).Eliminated = sitting;
                        HostController.SeatWinner(edit);
                    });
                }
            }

            ImGui.SameLine();
            if (Chrome.Button("Remove"))
                remove = player.PlayerId;
            ImGui.PopID();
        }

        if (remove is not null)
            Mutate(app, ev, game, edit =>
            {
                edit.Players.RemoveAll(player => player.PlayerId == remove);
                if (!scored)
                    HostController.SeatWinner(edit);
            });
    }

    private static void Bracket(HorizonApp app, EventDocument ev, GameInstance game, GameProgress progress)
    {
        Chrome.Section("Bracket");
        Chrome.Muted("Single elimination. Add the players, set how many sit at each table, then make the bracket. Click a name when that table finishes. This does not deal cards or play the game.");
        game.Tournament ??= new TournamentBoard();
        var board = game.Tournament;
        board.Normalize();
        var seats = board.Seats;
        ImGui.SetNextItemWidth(160);
        if (ImGui.SliderInt("Players per table", ref seats, 2, 8))
        {
            board.Seats = seats;
            app.Store.MarkEvent(ev.EventId);
        }

        if (Chrome.Accent("Make bracket"))
        {
            void Make()
            {
                var message = HostController.BuildBracket(game, HostController.EditGame(app.Store.StateFor(ev.EventId), game.InstanceId));
                if (message is null)
                {
                    app.Store.MarkEvent(ev.EventId);
                    app.Store.MarkState(ev.EventId);
                    app.Note("Bracket is ready.");
                }
                else
                {
                    app.Fail(message);
                }
            }

            if (board.Matches.Count > 0)
                app.Ask("Replace this bracket? The current pairing and recorded winners are cleared. The player list stays.", Make);
            else
                Make();
        }

        if (board.Matches.Count > 0)
        {
            ImGui.SameLine();
            if (Chrome.Button("Clear bracket"))
            {
                app.Ask("Clear this bracket? The player list stays.", () =>
                {
                    board.Matches.Clear();
                    var edit = HostController.EditGame(app.Store.StateFor(ev.EventId), game.InstanceId);
                    edit.MatchWinners.Clear();
                    edit.Winners.Clear();
                    app.Store.MarkEvent(ev.EventId);
                    app.Store.MarkState(ev.EventId);
                });
            }
        }

        if (board.Matches.Count == 0)
        {
            Chrome.Muted("The bracket is empty.");
            return;
        }

        DrawBracket(app, ev, game, board, progress);
    }

    internal static void DrawBracket(HorizonApp app, EventDocument ev, GameInstance game, TournamentBoard board, GameProgress progress)
    {
        const float cardWidth = 196f;
        const float rowHeight = 28f;
        const float header = 28f;
        const float gapX = 52f;
        const float gapY = 16f;
        const float top = 26f;

        var rounds = board.Matches.Select(match => match.Round).Distinct().OrderBy(round => round).ToList();
        var pos = new Dictionary<string, Vector2>(StringComparer.Ordinal);
        var height = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var round in rounds)
        {
            var column = rounds.IndexOf(round);
            var stack = top;
            foreach (var match in board.Matches.Where(match => match.Round == round))
            {
                var rows = SeatCount(board, match);
                var card = header + (rows * rowHeight) + 8f;
                var y = stack;
                var feeders = board.Matches.Where(item => item.NextMatchId == match.MatchId).ToList();
                if (feeders.Count > 0 && feeders.All(item => pos.ContainsKey(item.MatchId)))
                {
                    var feederTop = feeders.Min(item => pos[item.MatchId].Y);
                    var feederBottom = feeders.Max(item => pos[item.MatchId].Y + height[item.MatchId]);
                    y = ((feederTop + feederBottom) / 2f) - (card / 2f);
                }

                pos[match.MatchId] = new Vector2(column * (cardWidth + gapX), y);
                height[match.MatchId] = card;
                stack = y + card + gapY;
            }
        }

        var totalW = Math.Max(cardWidth, rounds.Count * (cardWidth + gapX) - gapX);
        var totalH = pos.Count == 0 ? 40f : pos.Max(pair => pair.Value.Y + height[pair.Key]) + 8f;
        var childH = Math.Clamp(totalH + 8f, 140f, 420f);
        ImGui.BeginChild("bracket-view", new Vector2(0, childH), true, ImGuiWindowFlags.HorizontalScrollbar);
        var origin = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(totalW, totalH));
        var draw = ImGui.GetWindowDrawList();
        foreach (var round in rounds)
        {
            var column = rounds.IndexOf(round);
            var title = RoundTitle(board, round, rounds[^1]);
            draw.AddText(origin + new Vector2(column * (cardWidth + gapX), 2f), ImGui.GetColorU32(ThemeHolder.Muted), title);
        }

        foreach (var match in board.Matches)
        {
            if (!pos.TryGetValue(match.MatchId, out var at))
                continue;

            var card = height[match.MatchId];
            var min = origin + at;
            var max = min + new Vector2(cardWidth, card);
            draw.AddRectFilled(min, max, ImGui.GetColorU32(ThemeService.With(ThemeHolder.Panel, 0.95f)), 8f);
            draw.AddRect(min, max, ImGui.GetColorU32(ThemeService.With(ThemeHolder.Accent, 0.55f)), 8f);
            var index = board.Matches.Where(item => item.Round == match.Round).ToList().IndexOf(match) + 1;
            var label = (board.Seats > 2 ? "Table " : "Match ") + index + "  ·  " + HostController.MatchPhase(board, match, progress);
            draw.AddText(min + new Vector2(10f, 6f), ImGui.GetColorU32(ThemeHolder.Accent), label);

            if (!string.IsNullOrWhiteSpace(match.NextMatchId) && pos.TryGetValue(match.NextMatchId, out var nextAt))
            {
                var from = min + new Vector2(cardWidth, card / 2f);
                var seat = match.NextSeat ?? 0;
                var to = origin + nextAt + new Vector2(0f, header + (seat * rowHeight) + (rowHeight / 2f));
                var mid = (from.X + to.X) / 2f;
                var line = ImGui.GetColorU32(ThemeService.With(ThemeHolder.Accent, 0.8f));
                draw.AddLine(from, new Vector2(mid, from.Y), line, 1.5f);
                draw.AddLine(new Vector2(mid, from.Y), new Vector2(mid, to.Y), line, 1.5f);
                draw.AddLine(new Vector2(mid, to.Y), to, line, 1.5f);
            }

            var winner = HostController.WinnerOf(progress, match);
            var seats = SeatCount(board, match);
            for (var seat = 0; seat < seats; seat++)
            {
                var row = min + new Vector2(8f, header + (seat * rowHeight));
                var rowMax = new Vector2(max.X - 8f, row.Y + rowHeight - 4f);
                var name = HostController.SeatName(board, match, seat, progress);
                var bye = string.Equals(match.Notes, "Bye", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(name);
                var won = !string.IsNullOrWhiteSpace(name) && string.Equals(name, winner, StringComparison.Ordinal);
                var eliminated = !bye && !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(winner) && !won;
                if (won)
                    draw.AddRectFilled(row, rowMax, ImGui.GetColorU32(ThemeService.With(ThemeHolder.Mint, 0.22f)), 4f);
                else if (!bye && !string.IsNullOrWhiteSpace(name) && ImGui.IsMouseHoveringRect(row, rowMax))
                    draw.AddRectFilled(row, rowMax, ImGui.GetColorU32(ThemeService.With(ThemeHolder.Accent, 0.16f)), 4f);

                var text = bye ? "Bye" : string.IsNullOrWhiteSpace(name) ? HostController.SeatHint(board, match, seat) : name;
                var color = bye || string.IsNullOrWhiteSpace(name) || eliminated ? ThemeHolder.Muted : won ? ThemeHolder.Mint : ThemeHolder.Text;
                draw.AddText(row + new Vector2(6f, 4f), ImGui.GetColorU32(color), text);
                if (bye || string.IsNullOrWhiteSpace(name))
                    continue;

                ImGui.SetCursorScreenPos(row);
                if (!ImGui.InvisibleButton(match.MatchId + "-" + seat, rowMax - row))
                    continue;

                var chosen = won ? null : name;
                Mutate(app, ev, game, edit =>
                {
                    HostController.RecordWinner(edit, board, match, chosen);
                    if (UnoTournament.IsUno(game))
                        UnoTournament.LinkChampion(ev, game, edit, app.Store.StateFor(ev.EventId));
                });
            }
        }

        ImGui.EndChild();
    }

    private static int SeatCount(TournamentBoard board, TournamentMatch match)
    {
        var feeders = board.Matches.Count(item => item.NextMatchId == match.MatchId);
        var count = Math.Max(match.Players.Count, feeders);
        if (string.Equals(match.Notes, "Bye", StringComparison.OrdinalIgnoreCase))
            count = Math.Max(count, 2);
        return Math.Max(1, count);
    }

    private static string RoundTitle(TournamentBoard board, int round, int last)
    {
        if (round == last && last > 1)
            return "Final";
        if (board.Seats == 2 && round == last - 1 && last >= 3)
            return "Semifinal";
        return "Round " + round;
    }

    private static void Mutate(HorizonApp app, EventDocument ev, GameInstance game, Action<GameProgress> edit)
    {
        var progress = HostController.EditGame(app.Store.StateFor(ev.EventId), game.InstanceId);
        edit(progress);
        app.Store.MarkState(ev.EventId);
    }
}

internal static class LiveHostPanel
{
    public static void Draw(HorizonApp app, bool poppedOut)
    {
        var ev = app.Store.SelectedEvent();
        if (ev is null)
        {
            Chrome.Muted("Select an event first. /eh host also explains this when nothing is selected.");
            return;
        }

        if (poppedOut)
            DueBanner.Draw(app);
        Banners.Draw(app, ev);
        var state = app.Store.StateFor(ev.EventId);
        var now = DateTimeOffset.Now;
        var current = ScheduleOrder.Current(ev, state);
        var next = ScheduleOrder.NextAfterCurrent(ev, state);
        ImGui.TextColored(ThemeHolder.Accent, ev.EventName);
        ImGui.SameLine();
        if (!poppedOut && Chrome.Button("Pop out"))
            app.Session.RequestHost = true;
        ImGui.SameLine();
        if (Chrome.Button("Compact presenter"))
            app.Session.RequestCompact = true;
        if (!state.Hosting && Chrome.Accent("Start hosting"))
            app.StartHosting(openWindow: !poppedOut);
        ImGui.SameLine();
        EventActions.TeleportButton(app, ev, "live");
        ImGui.SameLine();
        EventActions.ResetButton(app, ev);

        var done = ScheduleOrder.SpokenCompleted(ev, state);
        var slots = ScheduleOrder.Spoken(ev).Count;
        ImGui.ProgressBar(slots == 0 ? 0f : done / (float)slots, new Vector2(-1, 16), done + " / " + slots);
        var suggested = ScheduleOrder.Suggested(ev, state, now);
        if (suggested is not null && suggested.ActivityId != state.CurrentActivityId)
            Chrome.Muted("Clock suggestion: " + suggested.Title + ". The schedule will not advance by itself.");

        ImGui.BeginChild("now", new Vector2(ImGui.GetContentRegionAvail().X * 0.48f, 110), true);
        ImGui.TextDisabled("Current activity");
        ImGui.TextUnformatted(current?.Title ?? "Not started");
        if (current is not null)
            ImGui.TextDisabled(EventClock.FormatClock(ev.Date, current.StartTime, ev.Timezone) + " – " + EventClock.FormatClock(ev.Date, current.EndTime, ev.Timezone));
        ImGui.EndChild();
        ImGui.SameLine();
        ImGui.BeginChild("next", new Vector2(0, 110), true);
        ImGui.TextDisabled("Next activity");
        ImGui.TextUnformatted(next?.Title ?? "—");
        if (next is not null)
        {
            var start = EventClock.EffectiveStart(ev, next, state);
            ImGui.TextDisabled(start is null ? EventClock.FormatClock(ev.Date, next.StartTime, ev.Timezone) : EventClock.Countdown(start.Value, now));
        }

        ImGui.EndChild();

        var stage = HostController.OnStage(ev, current, now);
        var dj = stage.Current;
        var upcoming = stage.Upcoming;
        ImGui.TextDisabled("Current DJ");
        ImGui.SameLine();
        ImGui.TextUnformatted(dj?.Name ?? "—");
        ImGui.SameLine();
        ImGui.TextDisabled("Upcoming DJ");
        ImGui.SameLine();
        ImGui.TextUnformatted(upcoming?.Name ?? "—");

        LiveScriptView.Draw(app, ev, state, current);

        if (current is not null && Chrome.Button("Mark complete"))
        {
            HostController.Complete(state, current.ActivityId);
            app.Store.MarkState(ev.EventId);
        }

        var openGame = OpenGame(ev, state, current);
        var openRound = openGame is null ? null : HostController.CurrentRound(openGame, HostController.PeekGame(state, openGame.InstanceId));
        if (!string.IsNullOrWhiteSpace(openRound?.Announcement))
        {
            ImGui.SameLine();
            ChatCopy.Button(app, "live-round-" + openRound.RoundId, ChatLines.Yells(openRound.Announcement), "Copy round", "There is no chat announcement to copy.");
        }

        ImGui.SameLine();
        if (current?.GameInstanceId is not null && Chrome.Button("Open game"))
            OpenGameWindow(app, ev, state, current.GameInstanceId);

        ImGui.SameLine();
        if (dj is not null && Chrome.Button("Copy DJ intro"))
        {
            if (!string.IsNullOrWhiteSpace(dj.IntroText))
                ImGui.SetClipboardText(dj.IntroText);
            else
                app.Note(dj.Name + " has no introduction text yet. Add it on the DJ tab.");
        }

        if (ev.Games.Count > 0 && ImGui.BeginCombo("Open a game", "Choose"))
        {
            foreach (var game in ev.Games)
            {
                if (!ImGui.Selectable(game.Name))
                    continue;
                OpenGameWindow(app, ev, state, game.InstanceId);
            }

            ImGui.EndCombo();
        }

        foreach (var contest in ev.Contests.Where(contest => contest.Clues.Count > 0))
            ContestClues(app, ev, state, contest);

        Chrome.Section("Quick note");
        if (Chrome.Required("##quick", app.Session.QuickNote, value => app.Session.QuickNote = value, 240))
            _ = app.Session.QuickNote;
        ImGui.SameLine();
        if (Chrome.Button("Add note") && !string.IsNullOrWhiteSpace(app.Session.QuickNote))
        {
            state.QuickNotes.Add(DateTime.Now.ToString("h:mm tt") + "  " + app.Session.QuickNote.Trim());
            app.Session.QuickNote = "";
            app.Store.MarkState(ev.EventId);
        }

        foreach (var note in state.QuickNotes.AsEnumerable().Reverse().Take(4))
            ImGui.BulletText(note);
    }

    private static void ContestClues(HorizonApp app, EventDocument ev, LiveEventState state, ContestEntry contest)
    {
        var collect = (contest.Title ?? "").Contains("collect", StringComparison.OrdinalIgnoreCase)
            || (contest.ContestType ?? "").Contains("collect", StringComparison.OrdinalIgnoreCase);
        Chrome.Section(string.IsNullOrWhiteSpace(contest.Title) ? "Contest" : contest.Title);
        var winner = HostController.ContestWinner(state, contest);
        if (!string.IsNullOrWhiteSpace(winner))
            ImGui.TextColored(ThemeHolder.Gold, "Winner: " + winner.Trim());

        var number = 0;
        foreach (var clue in contest.Clues)
        {
            number++;
            var word = (clue.Content ?? "").Trim();
            var revealed = state.RevealedClueIds.Contains(clue.ClueId);
            ImGui.PushID(clue.ClueId);
            if (collect)
            {
                var when = string.IsNullOrWhiteSpace(clue.RevealTime)
                    ? ""
                    : "  ·  " + EventClock.FormatClock(ev.Date, clue.RevealTime, ev.Timezone);
                ImGui.TextDisabled("Word " + number + " / " + contest.Clues.Count + when);
                ImGui.TextColored(revealed ? ThemeHolder.Mint : ThemeHolder.Accent, word.Length == 0 ? "This word is empty." : word);
                var line = TagLine(ev, word);
                ChatCopy.Button(app, "tag-" + clue.ClueId, line, "Copy word " + number, "This word has no line to copy.");
                ImGui.SameLine();
                if (Chrome.Button(revealed ? "Read" : "Mark read"))
                {
                    if (revealed)
                        HostController.HideClue(state, clue.ClueId);
                    else
                        HostController.RevealClue(state, clue.ClueId);
                    app.Store.MarkState(ev.EventId);
                }
            }
            else
            {
                ImGui.TextUnformatted(revealed ? word : "Hidden");
                ImGui.SameLine();
                if (Chrome.Button(revealed ? "Hide" : "Reveal"))
                {
                    if (revealed)
                        HostController.HideClue(state, clue.ClueId);
                    else
                        HostController.RevealClue(state, clue.ClueId);
                    app.Store.MarkState(ev.EventId);
                }
            }

            ImGui.PopID();
        }
    }

    private static string TagLine(EventDocument ev, string word)
    {
        if (word.Length == 0)
            return "";

        var slash = "/" + word + "/";
        var script = ev.Scripts.FirstOrDefault(item =>
            ((item.Announcement ?? "") + "\n" + (item.SpokenText ?? "")).Contains(slash, StringComparison.OrdinalIgnoreCase));
        if (script is null)
            return "";

        var line = (script.Announcement ?? "").Trim();
        if (line.Length > 0)
            return line;

        var spoken = (script.SpokenText ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim();
        return spoken.Length == 0 ? "" : "/yell " + spoken;
    }

    private static GameInstance? OpenGame(EventDocument ev, LiveEventState state, ScheduleActivity? current)
    {
        if (!string.IsNullOrWhiteSpace(current?.GameInstanceId))
            return ev.Games.FirstOrDefault(game => game.InstanceId == current.GameInstanceId);

        if (string.IsNullOrWhiteSpace(state.OpenGameInstanceId))
            return null;

        return ev.Games.FirstOrDefault(game => game.InstanceId == state.OpenGameInstanceId);
    }

    private static void OpenGameWindow(HorizonApp app, EventDocument ev, LiveEventState state, string? instanceId)
    {
        var game = ev.Games.FirstOrDefault(item => item.InstanceId == instanceId);
        if (game is null)
        {
            app.Fail("This activity has no game to open.");
            return;
        }

        state.OpenGameInstanceId = game.InstanceId;
        app.Store.MarkState(ev.EventId);
        app.Session.RequestGame = true;
    }
}

internal sealed class LiveHostWindow : Dalamud.Interface.Windowing.Window
{
    private readonly HorizonApp app;

    public LiveHostWindow(HorizonApp app)
        : base("Event Horizon Live Host")
    {
        this.app = app;
        this.Size = new Vector2(920, 720);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new Dalamud.Interface.Windowing.WindowSizeConstraints
        {
            MinimumSize = new Vector2(640, 480),
            MaximumSize = new Vector2(2200, 1600),
        };
    }

    public override void Draw()
    {
        ImGui.SetWindowFontScale(this.app.Store.Config.UiScale);
        LiveHostPanel.Draw(this.app, poppedOut: true);
        ImGui.SetWindowFontScale(1f);
    }
}

internal sealed class GameWindow : Dalamud.Interface.Windowing.Window
{
    private readonly HorizonApp app;

    public GameWindow(HorizonApp app)
        : base("Event Horizon Game")
    {
        this.app = app;
        this.Size = new Vector2(760, 680);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new Dalamud.Interface.Windowing.WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 320),
            MaximumSize = new Vector2(1800, 1400),
        };
    }

    public override void Draw()
    {
        ImGui.SetWindowFontScale(this.app.Store.Config.UiScale);
        var ev = this.app.Store.SelectedEvent();
        if (ev is null)
        {
            Chrome.Muted("Select an event first.");
            ImGui.SetWindowFontScale(1f);
            return;
        }

        var state = this.app.Store.StateFor(ev.EventId);
        var game = string.IsNullOrWhiteSpace(state.OpenGameInstanceId)
            ? null
            : ev.Games.FirstOrDefault(item => item.InstanceId == state.OpenGameInstanceId);
        if (game is null)
        {
            Chrome.Muted("Choose a game from Live Host.");
            ImGui.SetWindowFontScale(1f);
            return;
        }

        this.WindowName = game.Name;
        GameBoard.Draw(this.app, ev, game);
        ImGui.SetWindowFontScale(1f);
    }
}

internal sealed class CompactHostWindow : Dalamud.Interface.Windowing.Window
{
    private readonly HorizonApp app;

    public CompactHostWindow(HorizonApp app)
        : base("Event Horizon Presenter")
    {
        this.app = app;
        this.Size = new Vector2(420, 360);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new Dalamud.Interface.Windowing.WindowSizeConstraints
        {
            MinimumSize = new Vector2(300, 220),
            MaximumSize = new Vector2(800, 900),
        };
    }

    public override void Draw()
    {
        ImGui.SetWindowFontScale(Math.Max(1f, this.app.Store.Config.UiScale));
        DueBanner.Draw(this.app);
        var ev = this.app.Store.SelectedEvent();
        ImGui.TextColored(ThemeHolder.Accent, "EVENT HORIZON");
        if (ev is null)
        {
            Chrome.Muted("Select an event first.");
            ImGui.SetWindowFontScale(1f);
            return;
        }

        var state = this.app.Store.StateFor(ev.EventId);
        var current = ScheduleOrder.Current(ev, state);
        var next = ScheduleOrder.NextAfterCurrent(ev, state);
        ImGui.TextDisabled("Now");
        ImGui.TextUnformatted(current?.Title ?? "Not started");
        var game = current?.GameInstanceId is null ? null : ev.Games.FirstOrDefault(item => item.InstanceId == current.GameInstanceId);
        if (game is not null)
            GameBoard.Compact(this.app, ev, game);
        ImGui.Spacing();
        ImGui.TextDisabled("Up next");
        if (next is null)
            ImGui.TextUnformatted("—");
        else
        {
            var start = EventClock.EffectiveStart(ev, next, state);
            ImGui.TextWrapped(next.Title + (start is null ? "" : "  ·  " + EventClock.FormatTime(start.Value, ev.Timezone)));
        }

        if (Chrome.Button("Next activity"))
            this.app.Advance();
        ImGui.SetWindowFontScale(1f);
    }
}
