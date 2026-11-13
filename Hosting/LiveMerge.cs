using EventHorizon.Models;
using EventHorizon.Persistence;

namespace EventHorizon.Hosting;

internal static class LiveMerge
{
    public static LiveEventState Apply(LiveEventState local, LiveEventState remote, bool remoteNewer)
    {
        local.Normalize();
        remote.Normalize();
        var result = JsonIo.Clone(remoteNewer ? remote : local);
        var extra = remoteNewer ? local : remote;
        result.Normalize();
        extra.Normalize();
        if (remoteNewer)
            KeepLocalPlayers(result, extra);
        return result;
    }

    public static bool Differs(LiveEventState merged, LiveEventState remote) =>
        Signature(merged) != Signature(remote);

    private static void KeepLocalPlayers(LiveEventState result, LiveEventState local)
    {
        foreach (var pair in local.Games)
        {
            pair.Value.Normalize();
            if (!result.Games.TryGetValue(pair.Key, out var game))
            {
                result.Games[pair.Key] = JsonIo.Clone(pair.Value);
                continue;
            }

            game.Normalize();
            foreach (var player in pair.Value.Players)
            {
                if (game.Players.Any(item => Same(item, player)))
                    continue;
                game.Players.Add(JsonIo.Clone(player));
            }
        }
    }

    private static bool Same(PlayerRecord left, PlayerRecord right)
    {
        if (!string.IsNullOrWhiteSpace(left.PlayerId) && string.Equals(left.PlayerId, right.PlayerId, StringComparison.Ordinal))
            return true;
        if (string.IsNullOrWhiteSpace(left.Name) || string.IsNullOrWhiteSpace(right.Name))
            return false;
        if (!left.Name.Trim().Equals(right.Name.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        var leftWorld = (left.World ?? "").Trim();
        var rightWorld = (right.World ?? "").Trim();
        return leftWorld.Length == 0 || rightWorld.Length == 0 || leftWorld.Equals(rightWorld, StringComparison.OrdinalIgnoreCase);
    }

    private static string Signature(LiveEventState state)
    {
        state.Normalize();
        var text = new System.Text.StringBuilder();
        foreach (var pair in state.Games.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var game = pair.Value;
            text.Append(pair.Key).Append('|').Append(game.RegistrationOpen).Append('|');
            text.Append(string.Join(",", game.Winners)).Append('|');
            foreach (var player in game.Players.OrderBy(item => item.PlayerId, StringComparer.Ordinal).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
                text.Append(player.PlayerId).Append('~').Append(player.Name).Append('~').Append(player.World).Append('~').Append(player.Score).Append('~').Append(player.Eliminated).Append(';');
            text.Append('\n');
        }

        return text.ToString();
    }
}
