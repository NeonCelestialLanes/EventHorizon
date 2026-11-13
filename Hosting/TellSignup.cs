using EventHorizon.Models;
using EventHorizon.Persistence;

namespace EventHorizon.Hosting;

internal static class TellSignup
{
    public static string Word(GameInstance game)
    {
        var written = (game.TellWord ?? "").Trim();
        if (written.Length > 0)
            return written;

        return UnoTournament.IsUno(game) ? "uno" : "";
    }

    public static bool Matches(GameInstance game, string? message)
    {
        var word = Word(game);
        var text = (message ?? "").Trim();
        return word.Length > 0 && text.Equals(word, StringComparison.OrdinalIgnoreCase);
    }

    public static (EventDocument Event, GameInstance Game)? Find(HorizonStore store, string? message, bool registrationOpen)
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
                if (!Matches(game, message))
                    continue;

                var open = HostController.PeekGame(state, game.InstanceId).RegistrationOpen;
                if (open == registrationOpen)
                    return (ev, game);
            }
        }

        return null;
    }
}
