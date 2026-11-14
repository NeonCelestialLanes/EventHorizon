using System.Globalization;
using EventHorizon.Models;
using EventHorizon.Persistence;

namespace EventHorizon.Hosting;

internal static class SendOrDelete
{
    public const string QuestionYell = "/yell Would they send or delete this chirp?";

    public static bool Is(GameInstance game)
    {
        var name = game.Name ?? "";
        return name.Contains("send", StringComparison.OrdinalIgnoreCase)
            && name.Contains("delete", StringComparison.OrdinalIgnoreCase);
    }

    public static string Yell(EventDocument ev, GameInstance game, GameProgress progress)
    {
        var winner = Winner(progress);
        if (winner.Length == 0)
            return "";

        return "/yell And we've got our last player standing! Congratulations to " + winner + ", who wins " + Prize(ev, game) + "! Let's give our winner a round of applause!";
    }

    public static string Chirp(GameRound round)
    {
        if (round.Extras is null)
            return "";

        foreach (var pair in round.Extras)
        {
            if (pair.Key.Equals("chirp", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(pair.Value))
                return pair.Value.Trim();
        }

        return "";
    }

    public static string ChirpYell(GameRound round)
    {
        var chirp = Chirp(round);
        return chirp.Length == 0 ? "" : "/yell " + chirp;
    }

    public static string StaffName(GameRound round)
    {
        var name = (round.FeaturedStaff ?? "").Trim();
        if (name.Length == 0 || name.Contains("TBD", StringComparison.OrdinalIgnoreCase) || name.Contains("assign", StringComparison.OrdinalIgnoreCase))
            return "";

        return name;
    }

    public static string ChoseYell(GameRound round)
    {
        var answer = (round.CorrectAnswer ?? "").Trim();
        if (answer.Length == 0)
            return "";

        var staff = StaffName(round);
        return "/yell " + (staff.Length == 0 ? "They" : staff) + " chose... " + answer + "!";
    }

    private static string Winner(GameProgress progress)
    {
        var standing = progress.Players.Where(player => !player.Eliminated && !string.IsNullOrWhiteSpace(player.Name)).ToList();
        if (standing.Count == 1)
            return UnoTournament.Label(standing[0].Name, standing[0].World);
        if (progress.Winners.Count == 1 && !string.IsNullOrWhiteSpace(progress.Winners[0]))
            return progress.Winners[0].Trim();
        return "";
    }

    private static string Prize(EventDocument ev, GameInstance game)
    {
        PrizeEntry? prize = null;
        if (!string.IsNullOrWhiteSpace(game.PrizeId))
            prize = ev.Prizes.FirstOrDefault(item => item.PrizeId == game.PrizeId);
        prize ??= ev.Prizes.FirstOrDefault(item => string.Equals(item.GameInstanceId, game.InstanceId, StringComparison.Ordinal));
        if (prize is { RewardType: RewardType.AetherCoins, Amount: decimal amount })
            return amount.ToString("#,0", CultureInfo.GetCultureInfo("en-US")) + " Aether Coins";
        if (prize is not null)
            return Labels.Reward(prize);
        return "1,250 Aether Coins";
    }
}
