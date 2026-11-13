using Dalamud.Bindings.ImGui;
using EventHorizon.Hosting;
using EventHorizon.Models;
using EventHorizon.Theme;

namespace EventHorizon.Ui;

internal static class UnoPanel
{
    public static void Draw(HorizonApp app, EventDocument ev, GameInstance game, GameProgress progress)
    {
        game.Tournament ??= new TournamentBoard { Format = "SingleElimination", Seats = 2 };
        var board = game.Tournament;
        board.Normalize();
        Chrome.Section("Uno tournament");
        Chrome.Muted("Players register by sending you a tell that says " + TellSignup.Word(game) + ". Capitalization does not matter. Nothing is sent back, and the prize is not handed out from here.");

        if (progress.Winners.Count == 1 && !string.IsNullOrWhiteSpace(progress.Winners[0]))
            Champion(app, ev, game, progress.Winners[0]);

        if (board.Matches.Count == 0)
            Registration(app, ev, game, progress);
        else
            Bracket(app, ev, game, board, progress);
    }

    private static void Champion(HorizonApp app, EventDocument ev, GameInstance game, string champion)
    {
        var prize = UnoTournament.PrizeLabel(ev, game);
        ImGui.TextColored(ThemeHolder.Gold, "Champion");
        ImGui.TextUnformatted(champion);
        ImGui.TextDisabled("Prize");
        ImGui.SameLine();
        ImGui.TextUnformatted(prize);
        var line = UnoTournament.Announcement(champion, prize);
        ImGui.TextWrapped(line);
        ChatCopy.Button(app, "uno-win-" + game.InstanceId, line, "Copy winner announcement", "The announcement is empty.");
        ImGui.Spacing();
    }

    private static void Registration(HorizonApp app, EventDocument ev, GameInstance game, GameProgress progress)
    {
        TellWordBox.Draw(game.TellWord, value => game.TellWord = value, () => app.Store.MarkEvent(ev.EventId));
        Chrome.Badge(progress.RegistrationOpen ? "Registration open" : "Registration closed", progress.RegistrationOpen ? ThemeHolder.Mint : ThemeHolder.Gold);
        ImGui.SameLine();
        ImGui.TextDisabled(progress.Players.Count + (progress.Players.Count == 1 ? " player" : " players"));

        if (!progress.RegistrationOpen && Chrome.Accent("Open registration"))
        {
            Edit(app, ev, game, edit => edit.RegistrationOpen = true);
            app.Note("Registration is open. A tell that says " + TellSignup.Word(game) + " will add that character. Capitalization does not matter.");
        }
        else if (progress.RegistrationOpen && Chrome.Button("Close registration"))
        {
            Edit(app, ev, game, edit => edit.RegistrationOpen = false);
            app.Note("Uno registration is closed.");
        }

        var name = app.Session.PlayerName;
        if (Chrome.Field("Character name", name, value => app.Session.PlayerName = value ?? ""))
            _ = name;
        var world = app.Session.UnoWorld;
        if (Chrome.Field("World", world, value => app.Session.UnoWorld = value ?? ""))
            _ = world;
        if (Chrome.Button("Add player") && !string.IsNullOrWhiteSpace(app.Session.PlayerName))
        {
            var person = app.Session.PlayerName;
            var home = app.Session.UnoWorld;
            var signup = UnoSignup.NeedsName;
            Edit(app, ev, game, edit => signup = UnoTournament.TryRegister(edit, person, home, fromTell: false, bracketExists: false));
            if (signup == UnoSignup.Added)
            {
                app.Session.PlayerName = "";
                app.Session.UnoWorld = "";
                app.Note(UnoTournament.Label(person, home) + " was added.");
            }
            else
            {
                app.Fail(UnoTournament.Explain(signup, game.Name));
            }
        }

        if (progress.Players.Count == 0)
            Chrome.Muted("No one is registered yet.");

        string? remove = null;
        foreach (var player in progress.Players)
        {
            ImGui.PushID(player.PlayerId);
            ImGui.TextUnformatted(UnoTournament.Label(player.Name, player.World));
            ImGui.SameLine();
            if (Chrome.Button("Remove"))
                remove = player.PlayerId;
            ImGui.PopID();
        }

        if (remove is not null)
            Edit(app, ev, game, edit => edit.Players.RemoveAll(player => player.PlayerId == remove));

        ImGui.Spacing();
        if (Chrome.Accent("Generate bracket"))
        {
            var message = HostController.BuildDuelBracket(game, HostController.EditGame(app.Store.StateFor(ev.EventId), game.InstanceId));
            if (message is null)
            {
                app.Store.MarkEvent(ev.EventId);
                app.Store.MarkState(ev.EventId);
                app.Note("The Uno bracket is ready. Registration is closed.");
            }
            else
            {
                app.Fail(message);
            }
        }

        ImGui.SameLine();
        ResetButton(app, ev, game);
    }

    private static void Bracket(HorizonApp app, EventDocument ev, GameInstance game, TournamentBoard board, GameProgress progress)
    {
        Chrome.Muted("Registration is closed. Click a name to record the winner. Click that name again to undo it. A bye moves that player ahead.");
        Chrome.Section("Standings");
        foreach (var row in UnoTournament.Standings(board, progress))
        {
            var color = row.Place == "Champion" ? ThemeHolder.Gold : row.Place == "Eliminated" ? ThemeHolder.Muted : ThemeHolder.Text;
            ImGui.TextColored(color, row.Place);
            ImGui.SameLine();
            ImGui.TextUnformatted(row.Name);
        }

        ImGui.Spacing();
        GameBoard.DrawBracket(app, ev, game, board, progress);
        ImGui.Spacing();
        ResetButton(app, ev, game);
    }

    private static void ResetButton(HorizonApp app, EventDocument ev, GameInstance game)
    {
        if (!Chrome.Tone("Reset tournament", ThemeHolder.Red))
            return;

        app.Ask("Reset this Uno tournament? The roster, the bracket, and the recorded results are cleared.", () =>
        {
            var state = app.Store.StateFor(ev.EventId);
            var progress = HostController.EditGame(state, game.InstanceId);
            UnoTournament.Reset(game, progress);
            UnoTournament.LinkChampion(ev, game, progress, state);
            app.Store.MarkEvent(ev.EventId);
            app.Store.MarkState(ev.EventId);
            app.Note("The Uno tournament was reset.");
        });
    }

    private static void Edit(HorizonApp app, EventDocument ev, GameInstance game, Action<GameProgress> change)
    {
        var progress = HostController.EditGame(app.Store.StateFor(ev.EventId), game.InstanceId);
        change(progress);
        app.Store.MarkState(ev.EventId);
    }
}
