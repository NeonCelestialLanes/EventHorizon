using System.Globalization;
using EventHorizon.Models;
using EventHorizon.Persistence;

namespace EventHorizon.Hosting;

internal enum UnoSignup
{
    Added,
    Duplicate,
    Closed,
    NeedsName,
    BracketStarted,
}

internal readonly record struct UnoStanding(string Place, string Name);

internal static class UnoTournament
{
    public static bool IsUno(GameInstance game)
    {
        if (!game.Formats.Contains(GameFormat.Tournament) && game.Tournament is null)
            return false;

        return HasUno(game.Name) || HasUno(game.TemplateId) || HasUno(game.Category);
    }

    public static bool IsSignup(string? message)
    {
        var text = (message ?? "").Trim();
        return text.Equals("uno", StringComparison.OrdinalIgnoreCase);
    }

    public static string Label(string name, string? world)
    {
        var person = name.Trim();
        if (string.IsNullOrWhiteSpace(world))
            return person;

        return person + " (" + world.Trim() + ")";
    }

    public static string? WorldAfter(string name, string? displayed)
    {
        var person = name.Trim();
        var shown = (displayed ?? "").Trim();
        if (person.Length == 0 || shown.Length <= person.Length || !shown.StartsWith(person, StringComparison.Ordinal))
            return null;

        var rest = shown[person.Length..].Trim();
        var start = 0;
        while (start < rest.Length && !char.IsLetter(rest[start]))
            start++;

        var world = rest[start..].Trim();
        return world.Length == 0 ? null : world;
    }

    public static (EventDocument Event, GameInstance Game)? OpenDesk(HorizonStore store)
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
            var progress = store.StateFor(ev.EventId);
            foreach (var game in ev.Games)
            {
                if (!IsUno(game))
                    continue;

                if (HostController.PeekGame(progress, game.InstanceId).RegistrationOpen)
                    return (ev, game);
            }
        }

        return null;
    }

    public static UnoSignup TryRegister(GameProgress progress, string? name, string? world, bool fromTell, bool bracketExists)
    {
        var person = (name ?? "").Trim();
        var home = string.IsNullOrWhiteSpace(world) ? null : world.Trim();
        if (person.Length == 0)
            return UnoSignup.NeedsName;
        if (person.Length > 64)
            person = person[..64];
        if (home is { Length: > 32 })
            home = home[..32];

        if (bracketExists)
            return UnoSignup.BracketStarted;
        if (fromTell && !progress.RegistrationOpen)
            return UnoSignup.Closed;
        if (progress.Players.Any(player => Same(player, person, home)))
            return UnoSignup.Duplicate;

        var added = HostController.AddPlayer(progress, person);
        added.World = home;
        return UnoSignup.Added;
    }

    public static string Explain(UnoSignup signup, string? gameName = null)
    {
        var name = string.IsNullOrWhiteSpace(gameName) ? "this game" : gameName.Trim();
        return signup switch
        {
            UnoSignup.Duplicate => "That player is already registered.",
            UnoSignup.Closed => name + " registration is closed.",
            UnoSignup.NeedsName => "Enter the character name.",
            UnoSignup.BracketStarted => "Reset the tournament before changing the roster.",
            _ => "Registered for " + name + ".",
        };
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

        return ev.Prizes.FirstOrDefault(prize => HasUno(prize.RewardName) || HasUno(prize.Notes));
    }

    public static string PrizeLabel(EventDocument ev, GameInstance game)
    {
        var prize = PrizeFor(ev, game);
        if (prize is { RewardType: RewardType.AetherCoins, Amount: decimal amount })
            return amount.ToString("#,0", CultureInfo.GetCultureInfo("en-US")) + " Aether Coins";
        if (prize is not null)
            return Labels.Reward(prize);

        return "2,000 Aether Coins";
    }

    public static string Announcement(string champion, string prize) =>
        "/yell And there we have it! " + champion.Trim() + " is our Uno champion! They ran the whole bracket! Congratulations! You've just won " + prize.Trim() + "! Everybody get on your feet and give our winner a huge round of applause!";

    public static void LinkChampion(EventDocument ev, GameInstance game, GameProgress progress, LiveEventState state)
    {
        var prize = PrizeFor(ev, game);
        if (prize is null)
            return;

        var award = HostController.EditPrize(state, prize);
        if (award.Status == AwardStatus.Awarded)
            return;

        award.Winners.Clear();
        var champion = progress.Winners.Count == 1 ? progress.Winners[0] : null;
        if (string.IsNullOrWhiteSpace(champion))
        {
            award.Status = AwardStatus.NotAwarded;
            return;
        }

        award.Winners.Add(champion.Trim());
        award.Status = AwardStatus.Pending;
    }

    public static void Reset(GameInstance game, GameProgress progress)
    {
        progress.Players.Clear();
        progress.Winners.Clear();
        progress.MatchWinners.Clear();
        progress.RegistrationOpen = false;
        if (game.Tournament is not null)
        {
            game.Tournament.Matches.Clear();
            game.Tournament.Seats = 2;
        }
    }

    public static bool Eliminated(TournamentBoard board, GameProgress progress, string label)
    {
        foreach (var match in board.Matches)
        {
            var winner = HostController.WinnerOf(progress, match);
            if (string.IsNullOrWhiteSpace(winner) || string.Equals(winner, label, StringComparison.Ordinal))
                continue;

            for (var seat = 0; seat < Math.Max(match.Players.Count, 2); seat++)
            {
                var name = HostController.SeatName(board, match, seat, progress);
                if (string.Equals(name, label, StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    public static List<UnoStanding> Standings(TournamentBoard board, GameProgress progress)
    {
        var rows = new List<UnoStanding>();
        if (progress.Winners.Count == 1 && !string.IsNullOrWhiteSpace(progress.Winners[0]))
            rows.Add(new UnoStanding("Champion", progress.Winners[0]));

        foreach (var player in progress.Players)
        {
            var label = Label(player.Name, player.World);
            if (progress.Winners.Any(winner => string.Equals(winner, label, StringComparison.Ordinal)))
                continue;

            rows.Add(new UnoStanding(Eliminated(board, progress, label) ? "Eliminated" : "Still in", label));
        }

        return rows;
    }

    private static bool Same(PlayerRecord player, string name, string? world) =>
        string.Equals(player.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)
        && string.Equals((player.World ?? "").Trim(), world ?? "", StringComparison.OrdinalIgnoreCase);

    private static bool HasUno(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Contains("uno", StringComparison.OrdinalIgnoreCase);
}
