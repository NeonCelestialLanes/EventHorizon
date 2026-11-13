using System.Collections.Concurrent;
using Dalamud.Game.Chat;
using Dalamud.Game.Gui.Toast;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using EventHorizon.Hosting;

namespace EventHorizon;

internal sealed class UnoTells : IDisposable
{
    private readonly HorizonApp app;
    private readonly IChatGui chat;
    private readonly IToastGui toast;
    private readonly ConcurrentQueue<(string Name, string? World, string Message)> waiting = new();

    public UnoTells(HorizonApp app, IChatGui chat, IToastGui toast)
    {
        this.app = app;
        this.chat = chat;
        this.toast = toast;
        this.chat.ChatMessage += this.OnChat;
    }

    public void Flush()
    {
        while (this.waiting.TryDequeue(out var tell))
            this.Apply(tell.Name, tell.World, tell.Message);
    }

    public void Dispose() => this.chat.ChatMessage -= this.OnChat;

    private void OnChat(IChatMessage message)
    {
        try
        {
            if (message.LogKind != XivChatType.TellIncoming)
                return;

            var text = message.OriginalMessage.ExtractText();
            if (string.IsNullOrWhiteSpace(text))
                text = message.Message.TextValue;
            text = (text ?? "").Trim();
            if (text.Length == 0 || text.StartsWith('/'))
                return;

            var name = "";
            string? world = null;
            var sender = message.OriginalSender.ToDalamudString();
            foreach (var payload in sender.Payloads)
            {
                if (payload is not PlayerPayload player || string.IsNullOrWhiteSpace(player.PlayerName))
                    continue;

                name = player.PlayerName.Trim();
                world = UnoTournament.WorldAfter(name, player.DisplayedName);
                break;
            }

            if (name.Length == 0)
                name = (sender.TextValue ?? "").Trim();
            if (name.Length == 0)
                return;

            this.waiting.Enqueue((name, world, text));
        }
        catch (Exception ex)
        {
            this.app.Log.Error(ex, "Could not read an Uno tell.");
        }
    }

    private void Apply(string name, string? world, string message)
    {
        var collected = CollectTell.Hear(this.app, name, world, message);
        if (collected is not null)
        {
            this.app.Note(collected);
            this.Show(collected);
            return;
        }

        var label = UnoTournament.Label(name, world);
        var desk = TellSignup.Find(this.app.Store, message, registrationOpen: true);
        if (desk is null)
        {
            if (TellSignup.Find(this.app.Store, message, registrationOpen: false) is not null)
                this.Show(label + " sent " + message.Trim() + ", but registration is closed.");
            return;
        }

        var ev = desk.Value.Event;
        var game = desk.Value.Game;
        var state = this.app.Store.StateFor(ev.EventId);
        var progress = HostController.EditGame(state, game.InstanceId);
        var bracket = UnoTournament.IsUno(game) && game.Tournament is { Matches.Count: > 0 };
        var signup = UnoTournament.TryRegister(progress, name, world, fromTell: true, bracket);
        if (signup == UnoSignup.Added)
        {
            this.app.Store.MarkState(ev.EventId);
            this.app.Note(label + " registered for " + game.Name + ".");
            this.Show(label + " registered for " + game.Name + ".");
            return;
        }

        this.Show(label + ": " + UnoTournament.Explain(signup, game.Name));
    }

    private void Show(string text)
    {
        try
        {
            this.toast.ShowNormal(text, new ToastOptions());
        }
        catch (Exception ex)
        {
            this.app.Log.Error(ex, "Could not show the Uno registration notice.");
        }
    }
}
