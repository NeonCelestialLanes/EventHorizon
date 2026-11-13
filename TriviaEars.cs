using System.Collections.Concurrent;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using EventHorizon.Hosting;

namespace EventHorizon;

internal sealed class TriviaEars : IDisposable
{
    private static readonly HashSet<XivChatType> Channels =
    [
        XivChatType.Say,
        XivChatType.Shout,
        XivChatType.Party,
        XivChatType.Alliance,
    ];

    private readonly HorizonApp app;
    private readonly IChatGui chat;
    private readonly IObjectTable players;
    private readonly ConcurrentQueue<(string Name, string? World, string Message)> waiting = new();

    public TriviaEars(HorizonApp app, IChatGui chat, IObjectTable players)
    {
        this.app = app;
        this.chat = chat;
        this.players = players;
        this.chat.ChatMessage += this.OnChat;
    }

    public void Flush()
    {
        while (this.waiting.TryDequeue(out var heard))
            this.Apply(heard.Name, heard.World, heard.Message);
    }

    public void Dispose() => this.chat.ChatMessage -= this.OnChat;

    private void OnChat(IChatMessage message)
    {
        try
        {
            if (!Channels.Contains(message.LogKind))
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
            if (name.Length == 0 || this.IsLocal(name))
                return;

            this.waiting.Enqueue((name, world, text));
        }
        catch (Exception ex)
        {
            this.app.Log.Error(ex, "Could not read a trivia answer.");
        }
    }

    private void Apply(string name, string? world, string message)
    {
        var desk = TriviaScore.Listening(this.app.Store);
        if (desk is null)
            return;

        var ev = desk.Value.Event;
        var game = desk.Value.Game;
        var progress = HostController.EditGame(this.app.Store.StateFor(ev.EventId), game.InstanceId);
        TriviaScore.Hear(progress, name, world, message, desk.Value.QuestionId);
        this.app.Store.MarkState(ev.EventId);
    }

    private bool IsLocal(string name)
    {
        var local = this.players.LocalPlayer?.Name.TextValue;
        return !string.IsNullOrWhiteSpace(local) && string.Equals(local.Trim(), name, StringComparison.OrdinalIgnoreCase);
    }
}
