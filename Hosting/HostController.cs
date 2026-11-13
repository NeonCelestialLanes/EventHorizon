using EventHorizon.Models;

namespace EventHorizon.Hosting;

internal static class ScheduleOrder
{
    public static List<ScheduleActivity> Sort(EventDocument ev) =>
        ev.Schedule
            .Select((activity, index) => (activity, index))
            .OrderBy(item => EventClock.SortKey(ev, item.activity))
            .ThenBy(item => item.index)
            .Select(item => item.activity)
            .ToList();

    public static ActivityProgressStatus StatusOf(ScheduleActivity activity, LiveEventState state) =>
        state.Activities.TryGetValue(activity.ActivityId, out var progress)
            ? progress.Status
            : activity.Status ?? ActivityProgressStatus.Scheduled;

    public static ScheduleActivity? Current(EventDocument ev, LiveEventState state) =>
        ev.Schedule.FirstOrDefault(activity => activity.ActivityId == state.CurrentActivityId);

    public static ScheduleActivity? NextAfterCurrent(EventDocument ev, LiveEventState state)
    {
        var ordered = Sort(ev);
        var index = ordered.FindIndex(activity => activity.ActivityId == state.CurrentActivityId);
        if (index < 0)
            return ordered.FirstOrDefault(activity => StatusOf(activity, state) is not (ActivityProgressStatus.Completed or ActivityProgressStatus.Skipped));

        var root = ScriptSpeech.Root(ev.Scripts, ordered[index].ScriptId);
        return ordered.Skip(index + 1).FirstOrDefault(activity =>
            StatusOf(activity, state) is not (ActivityProgressStatus.Completed or ActivityProgressStatus.Skipped)
            && ScriptSpeech.Root(ev.Scripts, activity.ScriptId) != root
            && !ScriptSpeech.IsFollowOn(ev.Scripts, activity.ScriptId));
    }

    public static ScheduleActivity? Suggested(EventDocument ev, LiveEventState state, DateTimeOffset now)
    {
        ScheduleActivity? best = null;
        var bestStart = DateTimeOffset.MinValue;
        foreach (var activity in ev.Schedule)
        {
            if (StatusOf(activity, state) is ActivityProgressStatus.Completed or ActivityProgressStatus.Skipped)
                continue;
            if (ScriptSpeech.IsFollowOn(ev.Scripts, activity.ScriptId))
                continue;

            var start = EventClock.EffectiveStart(ev, activity, state);
            var end = EventClock.Read(ev.Date, activity.EndTime, ev.Timezone);
            if (start is null)
                continue;

            var finishes = end.Ok ? end.Instant : start.Value.AddMinutes(15);
            if (now < start || now > finishes || start < bestStart)
                continue;

            best = activity;
            bestStart = start.Value;
        }

        return best;
    }

    public static int CompletedCount(EventDocument ev, LiveEventState state) =>
        ev.Schedule.Count(activity => StatusOf(activity, state) is ActivityProgressStatus.Completed or ActivityProgressStatus.Skipped);

    public static ScheduleActivity? DueCue(EventDocument ev, LiveEventState state, DateTimeOffset now)
    {
        foreach (var activity in Sort(ev))
        {
            if (activity.Category is not (ActivityCategory.Announcement or ActivityCategory.Game))
                continue;
            if (StatusOf(activity, state) is ActivityProgressStatus.Completed or ActivityProgressStatus.Skipped)
                continue;
            if (activity.ActivityId == state.CurrentActivityId || ScriptSpeech.IsFollowOn(ev.Scripts, activity.ScriptId))
                continue;

            var start = EventClock.EffectiveStart(ev, activity, state);
            if (start is null || now < start.Value)
                return null;

            return activity;
        }

        return null;
    }
}

internal static class HostController
{
    public static void Start(EventDocument ev, LiveEventState state)
    {
        state.Hosting = true;
        if (ev.Status is EventStatus.Draft or EventStatus.Planned or EventStatus.Ready)
            ev.Status = EventStatus.Live;

        var current = ScheduleOrder.Current(ev, state);
        if (current is null)
        {
            var ordered = ScheduleOrder.Sort(ev);
            current = ordered.FirstOrDefault(activity => ScheduleOrder.StatusOf(activity, state) is not (ActivityProgressStatus.Completed or ActivityProgressStatus.Skipped))
                ?? ordered.FirstOrDefault();
        }

        if (current is not null)
            Focus(ev, state, current);
    }

    public static bool Advance(EventDocument ev, LiveEventState state)
    {
        var ordered = ScheduleOrder.Sort(ev);
        if (ordered.Count == 0)
            return false;

        state.Hosting = true;
        var index = ordered.FindIndex(activity => activity.ActivityId == state.CurrentActivityId);
        if (index < 0)
        {
            Focus(ev, state, ordered[0]);
            return true;
        }

        FinishSpeech(ev, state, ordered[index]);
        var next = ScriptSpeech.NextActivity(ev, state);
        if (next is null)
            return true;

        Focus(ev, state, next);
        return true;
    }

    public static bool Previous(EventDocument ev, LiveEventState state)
    {
        var previous = ScriptSpeech.PreviousActivity(ev, state);
        if (previous is null)
            return false;

        Focus(ev, state, previous);
        return true;
    }

    public static void StartActivity(EventDocument ev, LiveEventState state, string activityId)
    {
        foreach (var pair in state.Activities)
        {
            if (pair.Key != activityId && pair.Value.Status == ActivityProgressStatus.Live)
                pair.Value.Status = ActivityProgressStatus.Paused;
        }

        var activity = ev.Schedule.FirstOrDefault(item => item.ActivityId == activityId);
        if (activity is null)
            return;

        state.Hosting = true;
        Focus(ev, state, activity);
    }

    public static void Complete(LiveEventState state, string activityId) => Set(state, activityId, ActivityProgressStatus.Completed);

    public static void Skip(EventDocument ev, LiveEventState state, string activityId)
    {
        Set(state, activityId, ActivityProgressStatus.Skipped);
        if (state.CurrentActivityId == activityId)
            Advance(ev, state);
    }

    public static void Delay(LiveEventState state, string activityId)
    {
        var progress = Edit(state, activityId);
        progress.Status = ActivityProgressStatus.Delayed;
        progress.DelayMinutes += 5;
    }

    public static void Resume(EventDocument ev, LiveEventState state, string activityId)
    {
        var activity = ev.Schedule.FirstOrDefault(item => item.ActivityId == activityId);
        if (activity is null)
            return;

        Focus(ev, state, activity);
    }

    public static GameProgress EditGame(LiveEventState state, string instanceId)
    {
        if (!state.Games.TryGetValue(instanceId, out var progress))
        {
            progress = new GameProgress();
            state.Games[instanceId] = progress;
        }

        progress.Normalize();
        return progress;
    }

    public static GameProgress PeekGame(LiveEventState state, string instanceId)
    {
        if (!state.Games.TryGetValue(instanceId, out var progress))
            return new GameProgress();

        progress.Normalize();
        return progress;
    }

    public static void NextRound(GameInstance game, GameProgress progress)
    {
        if (game.Rounds.Count == 0)
            return;

        progress.RoundIndex = Math.Clamp(progress.RoundIndex + 1, 0, game.Rounds.Count - 1);
    }

    public static void PreviousRound(GameProgress progress)
    {
        progress.RoundIndex = Math.Max(0, progress.RoundIndex - 1);
    }

    public static void Reveal(GameProgress progress, string roundId)
    {
        if (!progress.RevealedRoundIds.Contains(roundId))
            progress.RevealedRoundIds.Add(roundId);
    }

    public static void Hide(GameProgress progress, string roundId) => progress.RevealedRoundIds.Remove(roundId);

    public static bool IsRevealed(GameProgress progress, string roundId) => progress.RevealedRoundIds.Contains(roundId);

    public static PlayerRecord AddPlayer(GameProgress progress, string name)
    {
        var player = new PlayerRecord
        {
            PlayerId = Persistence.Ids.Unique("ply-", progress.Players.Select(item => item.PlayerId)),
            Name = name.Trim(),
        };
        progress.Players.Add(player);
        return player;
    }

    public static string? BuildBracket(GameInstance game, GameProgress progress)
    {
        game.Tournament ??= new TournamentBoard();
        var board = game.Tournament;
        board.Normalize();
        var seats = board.Seats;
        var names = progress.Players.Where(player => !player.Eliminated && !string.IsNullOrWhiteSpace(player.Name)).Select(player => player.Name.Trim()).ToList();
        if (names.Count < 2)
            return "Add at least two players before making the bracket.";

        progress.MatchWinners.Clear();
        progress.Winners.Clear();
        board.Matches.Clear();

        var opening = new List<Entrant>();
        foreach (var group in GroupsOf(names, seats))
        {
            if (group.Count == 1)
                opening.Add(new Entrant(null, group[0]));
            else
                opening.Add(new Entrant(NewMatch(board, 1, group), null));
        }

        var current = opening;
        var guard = 0;
        while (current.Count > 1 && guard++ < 16)
        {
            var next = new List<Entrant>();
            foreach (var group in GroupsOf(current, seats))
            {
                if (group.Count == 1)
                {
                    next.Add(group[0]);
                    continue;
                }

                var round = group.Select(item => item.Source?.Round ?? 0).DefaultIfEmpty(0).Max() + 1;
                var players = group.Select(item => item.Name ?? "").ToList();
                var created = NewMatch(board, round, players);
                for (var seat = 0; seat < group.Count; seat++)
                {
                    var source = group[seat].Source;
                    if (source is null)
                        continue;
                    source.NextMatchId = created.MatchId;
                    source.NextSeat = seat;
                }

                next.Add(new Entrant(created, null));
            }

            if (next.Count >= current.Count)
                break;
            current = next;
        }

        return null;
    }

    public static string? BuildDuelBracket(GameInstance game, GameProgress progress, Random? random = null)
    {
        var labels = progress.Players
            .Where(player => !string.IsNullOrWhiteSpace(player.Name))
            .Select(player => UnoTournament.Label(player.Name, player.World))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (labels.Count < 2)
            return "Add at least two players before making the bracket.";

        var rng = random ?? Random.Shared;
        for (var index = labels.Count - 1; index > 0; index--)
        {
            var swap = rng.Next(index + 1);
            (labels[index], labels[swap]) = (labels[swap], labels[index]);
        }

        game.Tournament ??= new TournamentBoard();
        var board = game.Tournament;
        board.Format = "SingleElimination";
        board.Seats = 2;
        board.Matches.Clear();
        progress.MatchWinners.Clear();
        progress.Winners.Clear();
        progress.RegistrationOpen = false;

        var size = 1;
        while (size < labels.Count)
            size *= 2;

        var byes = size - labels.Count;
        var cursor = 0;
        var round = new List<TournamentMatch>();
        for (var bye = 0; bye < byes; bye++)
        {
            var match = NewMatch(board, 1, [labels[cursor]]);
            match.Notes = "Bye";
            cursor++;
            round.Add(match);
        }

        while (cursor + 1 < labels.Count)
        {
            round.Add(NewMatch(board, 1, [labels[cursor], labels[cursor + 1]]));
            cursor += 2;
        }

        if (cursor < labels.Count)
        {
            var leftover = NewMatch(board, 1, [labels[cursor]]);
            leftover.Notes = "Bye";
            round.Add(leftover);
        }

        var guard = 0;
        while (round.Count > 1 && guard++ < 16)
        {
            var next = new List<TournamentMatch>();
            var number = round[0].Round + 1;
            for (var index = 0; index < round.Count; index += 2)
            {
                var created = NewMatch(board, number, []);
                round[index].NextMatchId = created.MatchId;
                round[index].NextSeat = 0;
                if (index + 1 < round.Count)
                {
                    round[index + 1].NextMatchId = created.MatchId;
                    round[index + 1].NextSeat = 1;
                }

                next.Add(created);
            }

            round = next;
        }

        foreach (var match in board.Matches)
        {
            if (!string.Equals(match.Notes, "Bye", StringComparison.Ordinal))
                continue;

            var winner = match.Players.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
            if (!string.IsNullOrWhiteSpace(winner))
                SetMatchWinner(progress, match, winner);
        }

        return null;
    }

    public static string MatchPhase(TournamentBoard board, TournamentMatch match, GameProgress progress)
    {
        if (!string.IsNullOrWhiteSpace(WinnerOf(progress, match)))
            return "Done";

        var known = 0;
        for (var seat = 0; seat < Math.Max(match.Players.Count, 2); seat++)
        {
            if (!string.IsNullOrWhiteSpace(SeatName(board, match, seat, progress)))
                known++;
        }

        return known >= 2 ? "Playing" : "Waiting";
    }

    public static string? SeatName(TournamentBoard board, TournamentMatch match, int seat, GameProgress progress)
    {
        if (seat < match.Players.Count && !string.IsNullOrWhiteSpace(match.Players[seat]))
            return match.Players[seat].Trim();

        var feeder = board.Matches.FirstOrDefault(item => item.NextMatchId == match.MatchId && item.NextSeat == seat);
        return feeder is null ? null : WinnerOf(progress, feeder);
    }

    public static string SeatHint(TournamentBoard board, TournamentMatch match, int seat)
    {
        var feeder = board.Matches.FirstOrDefault(item => item.NextMatchId == match.MatchId && item.NextSeat == seat);
        if (feeder is null)
            return "Open seat";

        var index = board.Matches.Where(item => item.Round == feeder.Round).ToList().IndexOf(feeder) + 1;
        return "Winner of " + (board.Seats > 2 ? "table " : "match ") + index;
    }

    public static string? AdvanceBracket(GameInstance game, GameProgress progress)
    {
        var board = game.Tournament;
        if (board is null || board.Matches.Count == 0)
            return "Add matches before advancing the bracket.";

        var round = board.Matches.Max(match => match.Round);
        var current = board.Matches.Where(match => match.Round == round).ToList();
        var winners = new List<string>();
        foreach (var match in current)
        {
            var winner = WinnerOf(progress, match);
            if (string.IsNullOrWhiteSpace(winner))
                return "Every match in the current round needs a winner first.";
            winners.Add(winner);
        }

        if (winners.Count == 1)
        {
            progress.Winners.Clear();
            progress.Winners.Add(winners[0]);
            return null;
        }

        var next = round + 1;
        for (var index = 0; index < winners.Count; index += 2)
        {
            board.Matches.Add(new TournamentMatch
            {
                MatchId = Persistence.Ids.Unique("mch-", board.Matches.Select(match => match.MatchId)),
                Round = next,
                PlayerA = winners[index],
                PlayerB = index + 1 < winners.Count ? winners[index + 1] : null,
            });
        }

        return null;
    }

    public static string? WinnerOf(GameProgress progress, TournamentMatch match)
    {
        if (progress.MatchWinners.TryGetValue(match.MatchId, out var stored) && !string.IsNullOrWhiteSpace(stored))
            return stored;
        return string.IsNullOrWhiteSpace(match.Winner) ? null : match.Winner;
    }

    public static void SetMatchWinner(GameProgress progress, TournamentMatch match, string? winner)
    {
        if (string.IsNullOrWhiteSpace(winner))
            progress.MatchWinners.Remove(match.MatchId);
        else
            progress.MatchWinners[match.MatchId] = winner.Trim();
    }

    public static void RecordWinner(GameProgress progress, TournamentBoard board, TournamentMatch match, string? winner)
    {
        SetMatchWinner(progress, match, winner);
        ClearAfter(board, match, progress, []);
        var max = board.Matches.Count == 0 ? 0 : board.Matches.Max(item => item.Round);
        var final = match.Round == max && board.Matches.Count(item => item.Round == max) == 1;
        progress.Winners.Clear();
        if (final && !string.IsNullOrWhiteSpace(winner))
            progress.Winners.Add(winner.Trim());
    }

    private static void ClearAfter(TournamentBoard board, TournamentMatch match, GameProgress progress, HashSet<string> seen)
    {
        if (string.IsNullOrWhiteSpace(match.NextMatchId) || !seen.Add(match.MatchId))
            return;

        var next = board.Matches.FirstOrDefault(item => item.MatchId == match.NextMatchId);
        if (next is null)
            return;

        progress.MatchWinners.Remove(next.MatchId);
        ClearAfter(board, next, progress, seen);
    }

    private static TournamentMatch NewMatch(TournamentBoard board, int round, IReadOnlyList<string> players)
    {
        var match = new TournamentMatch
        {
            MatchId = Persistence.Ids.Unique("mch-", board.Matches.Select(item => item.MatchId)),
            Round = round,
            Players = players.ToList(),
        };
        match.Normalize();
        board.Matches.Add(match);
        return match;
    }

    private static List<List<T>> GroupsOf<T>(List<T> items, int seats)
    {
        var groups = new List<List<T>>();
        for (var index = 0; index < items.Count; index += seats)
            groups.Add(items.GetRange(index, Math.Min(seats, items.Count - index)));

        if (groups.Count > 1 && groups[^1].Count == 1 && groups[^2].Count > 2)
        {
            var donor = groups[^2];
            groups[^1].Insert(0, donor[^1]);
            donor.RemoveAt(donor.Count - 1);
        }

        return groups;
    }

    private readonly record struct Entrant(TournamentMatch? Source, string? Name);

    public static AwardProgress Prize(LiveEventState state, PrizeEntry prize)
    {
        if (!state.Prizes.TryGetValue(prize.PrizeId, out var progress))
        {
            progress = new AwardProgress
            {
                Status = prize.Status ?? AwardStatus.NotAwarded,
                Winners = prize.Winners.ToList(),
            };
        }

        progress.Normalize();
        return progress;
    }

    public static AwardProgress EditPrize(LiveEventState state, PrizeEntry prize)
    {
        if (!state.Prizes.TryGetValue(prize.PrizeId, out var progress))
        {
            progress = new AwardProgress
            {
                Status = prize.Status ?? AwardStatus.NotAwarded,
                Winners = prize.Winners.ToList(),
            };
            state.Prizes[prize.PrizeId] = progress;
        }

        progress.Normalize();
        return progress;
    }

    public static bool ChecklistDone(LiveEventState state, ChecklistItem item) =>
        state.ChecklistDone.TryGetValue(item.ItemId, out var done) ? done : item.Done;

    public static void SetChecklist(LiveEventState state, ChecklistItem item, bool done) =>
        state.ChecklistDone[item.ItemId] = done;

    public static string? ContestWinner(LiveEventState state, ContestEntry contest) =>
        state.ContestWinners.TryGetValue(contest.ContestId, out var winner) ? winner : contest.Winner;

    public static void RevealClue(LiveEventState state, string clueId)
    {
        if (!state.RevealedClueIds.Contains(clueId))
            state.RevealedClueIds.Add(clueId);
    }

    public static void HideClue(LiveEventState state, string clueId) => state.RevealedClueIds.Remove(clueId);

    public static GameRound? CurrentRound(GameInstance game, GameProgress progress)
    {
        if (game.Rounds.Count == 0)
            return null;

        var index = Math.Clamp(progress.RoundIndex, 0, game.Rounds.Count - 1);
        return game.Rounds[index];
    }

    public static DjSlot? ScheduledDj(EventDocument ev, DateTimeOffset now)
    {
        DjSlot? best = null;
        var bestStart = DateTimeOffset.MinValue;
        foreach (var dj in ev.Djs)
        {
            var start = EventClock.Read(ev.Date, dj.StartTime, ev.Timezone);
            var end = EventClock.Read(ev.Date, dj.EndTime, ev.Timezone);
            if (!start.Ok)
                continue;

            var finishes = end.Ok ? end.Instant : start.Instant.AddHours(1);
            if (now < start.Instant || now > finishes || start.Instant < bestStart)
                continue;

            best = dj;
            bestStart = start.Instant;
        }

        return best;
    }

    public static DjSlot? NextDj(EventDocument ev, DateTimeOffset now)
    {
        DjSlot? best = null;
        var bestStart = DateTimeOffset.MaxValue;
        foreach (var dj in ev.Djs)
        {
            var start = EventClock.Read(ev.Date, dj.StartTime, ev.Timezone);
            if (!start.Ok || start.Instant <= now || start.Instant >= bestStart)
                continue;

            best = dj;
            bestStart = start.Instant;
        }

        return best;
    }

    public static DjSlot? DjForActivity(EventDocument ev, ScheduleActivity? activity)
    {
        if (activity?.DjId is null)
            return null;

        return ev.Djs.FirstOrDefault(dj => dj.DjId == activity.DjId);
    }

    private static void FinishSpeech(EventDocument ev, LiveEventState state, ScheduleActivity current)
    {
        var root = ScriptSpeech.Root(ev.Scripts, current.ScriptId);
        foreach (var activity in ev.Schedule)
        {
            var same = activity.ActivityId == current.ActivityId
                || (root.Length > 0 && ScriptSpeech.Root(ev.Scripts, activity.ScriptId) == root);
            if (!same)
                continue;
            if (ScheduleOrder.StatusOf(activity, state) is ActivityProgressStatus.Completed or ActivityProgressStatus.Skipped)
                continue;
            Set(state, activity.ActivityId, ActivityProgressStatus.Completed);
        }
    }

    private static void Focus(EventDocument ev, LiveEventState state, ScheduleActivity activity)
    {
        state.CurrentActivityId = activity.ActivityId;
        Set(state, activity.ActivityId, ActivityProgressStatus.Live);
        if (!string.IsNullOrWhiteSpace(activity.GameInstanceId))
            state.OpenGameInstanceId = activity.GameInstanceId;
    }

    private static void Set(LiveEventState state, string activityId, ActivityProgressStatus status) =>
        Edit(state, activityId).Status = status;

    private static ActivityProgress Edit(LiveEventState state, string activityId)
    {
        if (!state.Activities.TryGetValue(activityId, out var progress))
        {
            progress = new ActivityProgress();
            state.Activities[activityId] = progress;
        }

        return progress;
    }
}
