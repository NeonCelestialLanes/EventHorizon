using EventHorizon.Models;
using EventHorizon.Persistence;

namespace EventHorizon.Hosting;

internal static class CollectTell
{
    private static readonly char[] Punctuation = [',', '.', '!', '?', ';', ':', '"', '\'', '(', ')', '[', ']'];

    public static bool SameWords(IEnumerable<string?> expected, string? message)
    {
        var wanted = Tokens(expected);
        var heard = Tokens([message]);
        if (wanted.Count == 0 || wanted.Count != heard.Count)
            return false;

        var left = Counts(wanted);
        var right = Counts(heard);
        if (left.Count != right.Count)
            return false;

        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var count) || count != pair.Value)
                return false;
        }

        return true;
    }

    public static string? Hear(HorizonApp app, string name, string? world, string? message)
    {
        var events = app.Store.Events.Values.ToList();
        var selected = app.Store.SelectedEvent();
        if (selected is not null)
        {
            events.Remove(selected);
            events.Insert(0, selected);
        }

        foreach (var ev in events)
        {
            var contest = ev.Contests.FirstOrDefault(item => IsCollection(item) && SameWords(item.Clues.Select(clue => clue.Content), message));
            if (contest is not null)
                return ClaimContest(app, ev, contest, name, world);

            var game = ev.Games.FirstOrDefault(item => item.Formats.Contains(GameFormat.Collection) && SameWords(item.Rounds.Select(round => string.IsNullOrWhiteSpace(round.Prompt) ? round.Title : round.Prompt), message));
            if (game is not null)
                return ClaimGame(app, ev, game, name, world);
        }

        return null;
    }

    private static bool IsCollection(ContestEntry contest) =>
        (contest.ContestType ?? "").Contains("collect", StringComparison.OrdinalIgnoreCase);

    private static string ClaimContest(HorizonApp app, EventDocument ev, ContestEntry contest, string name, string? world)
    {
        var label = UnoTournament.Label(name, world);
        var title = string.IsNullOrWhiteSpace(contest.Title) ? "Collect the tags" : contest.Title.Trim();
        var state = app.Store.StateFor(ev.EventId);
        var current = HostController.ContestWinner(state, contest) ?? "";
        if (Listed(current, label))
            return label + " already has every word for " + title + ".";

        var prize = ev.Prizes.FirstOrDefault(item => item.ContestId == contest.ContestId || item.PrizeId == contest.PrizeId);
        if (!string.IsNullOrWhiteSpace(current) && prize is not { AllowMultipleWinners: true })
            return label + " has every word for " + title + ". " + current.Trim() + " is already the winner.";

        state.ContestWinners[contest.ContestId] = string.IsNullOrWhiteSpace(current) ? label : current.Trim() + ", " + label;
        app.Store.MarkState(ev.EventId);
        return label + " has every word for " + title + ".";
    }

    private static string ClaimGame(HorizonApp app, EventDocument ev, GameInstance game, string name, string? world)
    {
        var label = UnoTournament.Label(name, world);
        var title = string.IsNullOrWhiteSpace(game.Name) ? "Collect the tags" : game.Name.Trim();
        var progress = HostController.EditGame(app.Store.StateFor(ev.EventId), game.InstanceId);
        if (progress.Winners.Any(winner => winner.Equals(label, StringComparison.OrdinalIgnoreCase)))
            return label + " already has every word for " + title + ".";

        progress.Winners.Add(label);
        app.Store.MarkState(ev.EventId);
        return label + " has every word for " + title + ".";
    }

    private static bool Listed(string current, string label) =>
        current.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(name => name.Equals(label, StringComparison.OrdinalIgnoreCase));

    private static List<string> Tokens(IEnumerable<string?> parts)
    {
        var tokens = new List<string>();
        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part))
                continue;

            foreach (var raw in part.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                var token = raw.Trim().Trim(Punctuation);
                if (token.Length > 0)
                    tokens.Add(token);
            }
        }

        return tokens;
    }

    private static Dictionary<string, int> Counts(List<string> tokens)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in tokens)
            counts[token] = counts.TryGetValue(token, out var count) ? count + 1 : 1;
        return counts;
    }
}
