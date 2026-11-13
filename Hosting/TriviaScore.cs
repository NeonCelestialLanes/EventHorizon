using System.Globalization;
using EventHorizon.Models;
using EventHorizon.Persistence;

namespace EventHorizon.Hosting;

internal readonly record struct TriviaQuestion(string Id, string Title, string Prompt, string? Answer, bool Extra);

internal readonly record struct TriviaRow(int Rank, string PlayerId, string Label, int Points, int Correct);

internal readonly record struct TriviaClock(string Label, bool Final, bool Done);

internal static class TriviaScore
{
    public const int YellLimit = 500;

    public const int QuestionSeconds = 60;

    public const int FinalCountdownSeconds = 5;

    public static bool IsTrivia(GameInstance game)
    {
        if (UnoTournament.IsUno(game))
            return false;

        return HasTrivia(game.Name) || HasTrivia(game.TemplateId) || HasTrivia(game.Category);
    }

    public static TriviaLive Desk(GameProgress progress)
    {
        progress.Trivia ??= new TriviaLive();
        progress.Trivia.Normalize();
        return progress.Trivia;
    }

    public static List<TriviaQuestion> Questions(GameInstance game, TriviaLive? desk)
    {
        var list = new List<TriviaQuestion>();
        foreach (var round in game.Rounds)
            list.Add(new TriviaQuestion(round.RoundId, round.Title ?? "", round.Prompt ?? "", round.CorrectAnswer, false));
        if (desk is null)
            return list;

        foreach (var extra in desk.Extras)
            list.Add(new TriviaQuestion(extra.QuestionId, "Tiebreaker", extra.Prompt, extra.Answer, true));
        return list;
    }

    public static TriviaQuestion? Current(GameInstance game, GameProgress progress)
    {
        var questions = Questions(game, progress.Trivia);
        if (questions.Count == 0)
            return null;

        var index = Math.Clamp(progress.Trivia?.ViewIndex ?? progress.RoundIndex, 0, questions.Count - 1);
        return questions[index];
    }

    public static bool Arm(GameProgress progress, string questionId, DateTimeOffset now, bool restart)
    {
        var desk = Desk(progress);
        if (!restart && desk.TimerQuestionId == questionId && desk.TimerStarted is not null)
            return false;

        desk.TimerQuestionId = questionId;
        desk.TimerStarted = now;
        return true;
    }

    public static TriviaClock ReadClock(GameProgress progress, string questionId, DateTimeOffset now)
    {
        if (progress.Trivia is not { TimerQuestionId: var timed, TimerStarted: { } started } || timed != questionId)
            return new TriviaClock(FormatClock(QuestionSeconds), false, false);

        var remaining = QuestionSeconds - (now - started).TotalSeconds;
        if (remaining <= 0)
            return new TriviaClock("Time", false, true);

        var seconds = (int)Math.Ceiling(remaining);
        if (seconds <= FinalCountdownSeconds)
            return new TriviaClock(seconds.ToString(), true, false);

        return new TriviaClock(FormatClock(seconds), false, false);
    }

    public static void Move(GameInstance game, GameProgress progress, int delta)
    {
        var desk = Desk(progress);
        var count = Questions(game, desk).Count;
        if (count == 0)
            return;

        desk.ViewIndex = Math.Clamp(desk.ViewIndex + delta, 0, count - 1);
        if (desk.ViewIndex < game.Rounds.Count)
            progress.RoundIndex = desk.ViewIndex;
    }

    public static PlayerRecord Ensure(GameProgress progress, string name, string? world)
    {
        var person = name.Trim();
        var home = string.IsNullOrWhiteSpace(world) ? null : world.Trim();
        var existing = progress.Players.FirstOrDefault(player => Same(player, person, home));
        if (existing is not null)
            return existing;

        var added = HostController.AddPlayer(progress, person);
        added.World = home;
        return added;
    }

    public static void Hear(GameProgress progress, string name, string? world, string message, string? questionId)
    {
        var text = message.Trim();
        if (text.Length == 0 || text.StartsWith('/'))
            return;
        if (text.Length > 240)
            text = text[..240];

        var player = Ensure(progress, name, world);
        var desk = Desk(progress);
        var order = desk.Heard.Count == 0 ? 1 : desk.Heard.Max(item => item.Order) + 1;
        desk.Heard.Add(new TriviaHeard
        {
            Order = order,
            Name = player.Name,
            World = player.World,
            Message = text,
            QuestionId = questionId,
        });
        if (desk.Heard.Count > 40)
            desk.Heard.RemoveAt(0);
        Sync(progress);
    }

    public static int Points(TriviaLive? desk, string playerId)
    {
        if (desk is null)
            return 0;

        var scored = desk.Results.Count(result => !result.Skipped && result.PlayerId == playerId);
        var adjusted = desk.Adjustments.Where(item => !item.Undone && item.PlayerId == playerId).Sum(item => item.Delta);
        return Math.Max(0, scored + adjusted);
    }

    public static int Correct(TriviaLive? desk, string playerId) =>
        desk?.Results.Count(result => !result.Skipped && result.PlayerId == playerId) ?? 0;

    public static List<TriviaRow> Board(GameProgress progress)
    {
        var ranked = progress.Players
            .Select(player => new TriviaRow(0, player.PlayerId, UnoTournament.Label(player.Name, player.World), Points(progress.Trivia, player.PlayerId), Correct(progress.Trivia, player.PlayerId)))
            .OrderByDescending(row => row.Points)
            .ThenBy(row => row.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return ranked.Select(row => row with { Rank = 1 + ranked.Count(other => other.Points > row.Points) }).ToList();
    }

    public static (int Done, int Left) Progress(GameInstance game, GameProgress progress)
    {
        var total = Questions(game, progress.Trivia).Count;
        var done = progress.Trivia?.Results.Count(result => result.Skipped || !string.IsNullOrWhiteSpace(result.PlayerId)) ?? 0;
        return (done, Math.Max(0, total - done));
    }

    public static string? Award(GameProgress progress, string questionId, string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
            return "Choose the player who answered first.";

        var desk = Desk(progress);
        var existing = Result(desk, questionId);
        if (existing is { Skipped: true })
            return "This question was skipped. Restore it before awarding a point.";
        if (existing is { PlayerId: not null })
            return "This question already has a point.";

        desk.Results.Add(new TriviaResult { QuestionId = questionId, PlayerId = playerId });
        desk.Actions.Add(new TriviaAction { Op = "Award", QuestionId = questionId, PlayerId = playerId });
        Sync(progress);
        return null;
    }

    public static string? Skip(GameProgress progress, string questionId)
    {
        var desk = Desk(progress);
        var existing = Result(desk, questionId);
        if (existing is { PlayerId: not null, Skipped: false })
            return "This question already has a point. Undo it before skipping.";
        if (existing is { Skipped: true })
            return null;

        desk.Results.Add(new TriviaResult { QuestionId = questionId, Skipped = true });
        desk.Actions.Add(new TriviaAction { Op = "Skip", QuestionId = questionId });
        Sync(progress);
        return null;
    }

    public static string? Restore(GameProgress progress, string questionId)
    {
        var desk = Desk(progress);
        var existing = Result(desk, questionId);
        if (existing is not { Skipped: true })
            return "This question is not skipped.";

        desk.Results.Remove(existing);
        desk.Actions.Add(new TriviaAction { Op = "Clear", QuestionId = questionId, PreviousSkipped = true });
        Sync(progress);
        return null;
    }

    public static string? Reassign(GameProgress progress, string questionId, string playerId)
    {
        var desk = Desk(progress);
        var existing = Result(desk, questionId);
        if (existing is not { Skipped: false, PlayerId: not null })
            return "This question does not have a point to move.";
        if (existing.PlayerId == playerId)
            return null;

        var previous = existing.PlayerId;
        existing.PlayerId = playerId;
        desk.Actions.Add(new TriviaAction { Op = "Reassign", QuestionId = questionId, PlayerId = playerId, PreviousPlayerId = previous });
        Sync(progress);
        return null;
    }

    public static string? RemoveLatest(GameProgress progress, string playerId)
    {
        var desk = Desk(progress);
        var existing = desk.Results.LastOrDefault(result => !result.Skipped && result.PlayerId == playerId);
        if (existing is null)
            return "That player has no question point to remove.";

        desk.Results.Remove(existing);
        desk.Actions.Add(new TriviaAction
        {
            Op = "Clear",
            QuestionId = existing.QuestionId,
            PreviousPlayerId = playerId,
        });
        Sync(progress);
        return null;
    }

    public static string? Adjust(GameProgress progress, string playerId, int delta)
    {
        if (delta == 0)
            return "Enter a point change.";

        var desk = Desk(progress);
        var id = Ids.Unique("adj-", desk.Adjustments.Select(item => item.Id));
        desk.Adjustments.Add(new TriviaAdjust { Id = id, PlayerId = playerId, Delta = delta });
        desk.Actions.Add(new TriviaAction { Op = "Adjust", PlayerId = playerId, AdjustId = id, Delta = delta });
        Sync(progress);
        return null;
    }

    public static string? Undo(GameProgress progress)
    {
        var desk = progress.Trivia;
        var action = desk?.Actions.LastOrDefault(item => !item.Undone);
        if (desk is null || action is null)
            return "Nothing to undo.";

        switch (action.Op)
        {
            case "Award":
            case "Skip":
                desk.Results.RemoveAll(result => result.QuestionId == action.QuestionId);
                break;
            case "Reassign":
                var moved = Result(desk, action.QuestionId);
                if (moved is not null)
                    moved.PlayerId = action.PreviousPlayerId;
                break;
            case "Clear":
                if (desk.Results.All(result => result.QuestionId != action.QuestionId))
                {
                    desk.Results.Add(new TriviaResult
                    {
                        QuestionId = action.QuestionId ?? "",
                        PlayerId = action.PreviousPlayerId,
                        Skipped = action.PreviousSkipped,
                    });
                }

                break;
            case "Adjust":
                var adjustment = desk.Adjustments.FirstOrDefault(item => item.Id == action.AdjustId);
                if (adjustment is not null)
                    adjustment.Undone = true;
                break;
            default:
                return "That score change could not be undone.";
        }

        action.Undone = true;
        Sync(progress);
        return null;
    }

    public static string AddTiebreak(GameInstance game, GameProgress progress, string prompt, string? answer)
    {
        var desk = Desk(progress);
        var id = Ids.Unique("tb-", desk.Extras.Select(item => item.QuestionId));
        desk.Extras.Add(new TriviaExtra
        {
            QuestionId = id,
            Prompt = prompt.Trim(),
            Answer = string.IsNullOrWhiteSpace(answer) ? null : answer.Trim(),
        });
        desk.Ended = false;
        desk.ViewIndex = Questions(game, desk).Count - 1;
        return id;
    }

    public static string? Finish(EventDocument ev, GameInstance game, GameProgress progress, LiveEventState state)
    {
        var leaders = Board(progress).Where(row => row.Rank == 1).ToList();
        if (leaders.Count == 0)
            return "Add players before ending trivia.";
        if (leaders.Count > 1)
            return "It's a tie. Add a tiebreaker before crowning a winner.";

        var desk = Desk(progress);
        desk.Ended = true;
        desk.Listening = false;
        progress.Winners.Clear();
        progress.Winners.Add(leaders[0].Label);
        LinkPrize(ev, game, progress, state);
        return null;
    }

    public static void LinkPrize(EventDocument ev, GameInstance game, GameProgress progress, LiveEventState state)
    {
        var prize = PrizeFor(ev, game);
        if (prize is null)
            return;

        var award = HostController.EditPrize(state, prize);
        if (award.Status == AwardStatus.Awarded)
            return;

        award.Winners.Clear();
        var leader = Board(progress).Where(row => row.Rank == 1).ToList();
        if (progress.Trivia is not { Ended: true } || leader.Count != 1)
        {
            award.Status = AwardStatus.NotAwarded;
            return;
        }

        award.Winners.Add(leader[0].Label);
        award.Status = AwardStatus.Pending;
    }

    public static PrizeEntry? PrizeFor(EventDocument ev, GameInstance game)
    {
        if (!string.IsNullOrWhiteSpace(game.PrizeId))
        {
            var linked = ev.Prizes.FirstOrDefault(prize => prize.PrizeId == game.PrizeId);
            if (linked is not null)
                return linked;
        }

        var byGame = ev.Prizes.FirstOrDefault(prize => string.Equals(prize.GameInstanceId, game.InstanceId, StringComparison.Ordinal));
        if (byGame is not null)
            return byGame;

        return ev.Prizes.FirstOrDefault(prize => HasTrivia(prize.RewardName) || HasTrivia(prize.Notes));
    }

    public static string PrizeLabel(EventDocument ev, GameInstance game)
    {
        var prize = PrizeFor(ev, game);
        if (prize is { RewardType: RewardType.AetherCoins, Amount: decimal amount })
            return amount.ToString("#,0", CultureInfo.GetCultureInfo("en-US")) + " Aether Coins";
        if (prize is not null)
            return Labels.Reward(prize);

        return "1,250 Aether Coins";
    }

    public static string ChampionYell(string winner, int points, string prize) =>
        "/yell And we have our trivia champion! Congratulations to " + winner.Trim() + ", who finished with " + points + " " + PointWord(points) + "! You've won " + prize.Trim() + "! Everybody give our winner a round of applause!";

    public static IReadOnlyList<string> QuestionYell(GameInstance game, GameProgress progress, TriviaQuestion question)
    {
        var prompt = (question.Prompt ?? "").Trim();
        if (prompt.Length == 0)
            prompt = (question.Title ?? "").Trim();
        if (prompt.Length == 0)
            return [];

        var round = game.Rounds.FirstOrDefault(item => item.RoundId == question.Id);
        var choices = (round?.Choices ?? []).Where(choice => !string.IsNullOrWhiteSpace(choice)).Select(choice => choice.Trim()).ToList();
        if (choices.Count > 0)
            prompt = prompt.TrimEnd() + " " + string.Join(" / ", choices);

        var questions = Questions(game, progress.Trivia);
        var index = questions.FindIndex(item => item.Id == question.Id);
        var lead = question.Extra ? "Tiebreaker: " : index < 0 ? "" : "Question " + (index + 1) + ": ";
        return ScriptSpeech.Fit("/yell " + lead + prompt);
    }

    public static IReadOnlyList<string> PointYell(GameInstance game, GameProgress progress, TriviaQuestion question)
    {
        var result = progress.Trivia?.Results.FirstOrDefault(item => item.QuestionId == question.Id);
        if (result is not { Skipped: false, PlayerId: { } playerId })
            return [];

        var player = progress.Players.FirstOrDefault(item => item.PlayerId == playerId);
        if (player is null || string.IsNullOrWhiteSpace(player.Name))
            return [];

        var label = UnoTournament.Label(player.Name, player.World);
        var points = Points(progress.Trivia, playerId);
        var questions = Questions(game, progress.Trivia);
        var index = questions.FindIndex(item => item.Id == question.Id);
        var which = question.Extra ? "The tiebreaker" : index < 0 ? "That question" : "Question " + (index + 1);
        return ScriptSpeech.Fit("/yell " + which + " goes to " + label + "! That's " + points + " " + PointWord(points) + ".");
    }

    public static IReadOnlyList<string> TopThree(IReadOnlyList<TriviaRow> rows, int done, int left)
    {
        var shown = rows.Where(row => row.Rank <= 3).ToList();
        var entries = shown.Select(row => Place(row.Rank) + ": " + row.Label + " (" + row.Points + " " + PointWord(row.Points) + ")").ToList();
        var header = "/yell TRIVIA LEADERBOARD! After " + Count(done, "question") + ", here's where we stand! ";
        var tail = left > 0
            ? " We've still got " + Count(left, "question") + " to go, so it's anyone's game!"
            : " That's every question.";
        return Pack(header, entries, ", ", tail);
    }

    public static IReadOnlyList<string> AllScores(IReadOnlyList<TriviaRow> rows)
    {
        var entries = rows.Select(row => Place(row.Rank) + " - " + row.Label + ": " + row.Points + (row.Points == 1 ? " pt" : " pts")).ToList();
        return Pack("/yell TRIVIA SCORES: ", entries, " | ", null);
    }

    public static IReadOnlyList<string> Standings(IReadOnlyList<TriviaRow> rows, int left)
    {
        if (rows.Count == 0)
            return [];

        var groups = rows.GroupBy(row => row.Rank).OrderBy(group => group.Key).Take(3).ToList();
        var first = groups[0].ToList();
        var clause = first.Count == 1
            ? first[0].Label + " is currently in first place with " + first[0].Points + " " + PointWord(first[0].Points)
            : JoinNames(first.Select(row => row.Label)) + " are tied for first with " + first[0].Points + " " + PointWord(first[0].Points);
        if (groups.Count > 1)
        {
            var rest = groups.Skip(1).Select(group =>
            {
                var people = group.ToList();
                return JoinNames(people.Select(row => row.Label)) + " with " + people[0].Points;
            });
            clause += ", followed by " + string.Join(" and ", rest);
        }

        var tail = left > 0
            ? " We've still got plenty of questions left, so keep those answers coming!"
            : " That's the last of the questions.";
        var line = "/yell All right, everybody! Let's take a look at our trivia standings! " + clause + "." + tail;
        return line.Length <= YellLimit ? [line] : Pack("/yell Trivia standings: ", groups.SelectMany(group => group).Select(row => Place(row.Rank) + " " + row.Label + " (" + row.Points + ")").ToList(), ", ", null);
    }

    public static IReadOnlyList<string> FinalResults(IReadOnlyList<TriviaRow> rows, string prize)
    {
        var leaders = rows.Where(row => row.Rank == 1).ToList();
        if (leaders.Count == 1)
            return [ChampionYell(leaders[0].Label, leaders[0].Points, prize)];
        if (leaders.Count > 1)
            return ["/yell Trivia is tied! " + JoinNames(leaders.Select(row => row.Label)) + " are tied with " + leaders[0].Points + " " + PointWord(leaders[0].Points) + ". We need a tiebreaker."];
        return [];
    }

    public static List<string> Pack(string header, IReadOnlyList<string> entries, string separator, string? closing)
    {
        var yells = new List<string>();
        var batch = new List<string>();
        void Flush(bool last)
        {
            if (batch.Count == 0)
                return;

            var body = header + string.Join(separator, batch);
            if (last && closing is not null && body.Length + closing.Length <= YellLimit)
                body += closing;
            else if (last && closing is not null && closing.Trim().Length > 0)
            {
                yells.Add(body);
                var tail = "/yell " + closing.Trim();
                yells.Add(tail);
                batch.Clear();
                return;
            }

            yells.Add(body);
            batch.Clear();
        }

        foreach (var entry in entries)
        {
            var trial = header + string.Join(separator, batch.Concat([entry]));
            if (batch.Count > 0 && trial.Length > YellLimit)
                Flush(false);
            batch.Add(entry);
            if (batch.Count == 1 && (header + entry).Length > YellLimit)
                Flush(false);
        }

        Flush(true);
        return yells;
    }

    public static (EventDocument Event, GameInstance Game, string? QuestionId)? Listening(HorizonStore store)
    {
        var events = store.Events.Values.ToList();
        var selected = store.SelectedEvent();
        if (selected is not null)
        {
            events.Remove(selected);
            events.Insert(0, selected);
        }

        foreach (var ev in events)
        {
            var state = store.StateFor(ev.EventId);
            foreach (var game in ev.Games)
            {
                if (!IsTrivia(game))
                    continue;

                var progress = HostController.PeekGame(state, game.InstanceId);
                if (progress.Trivia is not { Listening: true })
                    continue;

                return (ev, game, Current(game, progress)?.Id);
            }
        }

        return null;
    }

    private static void Sync(GameProgress progress)
    {
        var desk = progress.Trivia;
        foreach (var player in progress.Players)
            player.Score = Points(desk, player.PlayerId);
        if (desk is { Ended: true })
        {
            var leaders = Board(progress).Where(row => row.Rank == 1).ToList();
            if (leaders.Count != 1)
            {
                desk.Ended = false;
                progress.Winners.Clear();
            }
            else
            {
                progress.Winners.Clear();
                progress.Winners.Add(leaders[0].Label);
            }
        }
    }

    private static TriviaResult? Result(TriviaLive desk, string? questionId) =>
        string.IsNullOrWhiteSpace(questionId) ? null : desk.Results.FirstOrDefault(result => result.QuestionId == questionId);

    private static string Place(int rank)
    {
        var teen = rank % 100;
        if (teen is >= 11 and <= 13)
            return rank + "th";

        return (rank % 10) switch
        {
            1 => rank + "st",
            2 => rank + "nd",
            3 => rank + "rd",
            _ => rank + "th",
        };
    }

    private static string PointWord(int points) => points == 1 ? "point" : "points";

    private static string Count(int number, string noun) => number + " " + noun + (number == 1 ? "" : "s");

    private static string FormatClock(int seconds)
    {
        var whole = Math.Max(0, seconds);
        return (whole / 60) + ":" + (whole % 60).ToString("00");
    }

    private static string JoinNames(IEnumerable<string> names)
    {
        var list = names.ToList();
        if (list.Count <= 1)
            return list.FirstOrDefault() ?? "";
        if (list.Count == 2)
            return list[0] + " and " + list[1];
        return string.Join(", ", list.Take(list.Count - 1)) + ", and " + list[^1];
    }

    private static bool Same(PlayerRecord player, string name, string? world) =>
        string.Equals(player.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)
        && string.Equals((player.World ?? "").Trim(), world ?? "", StringComparison.OrdinalIgnoreCase);

    private static bool HasTrivia(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Contains("trivia", StringComparison.OrdinalIgnoreCase);
}
