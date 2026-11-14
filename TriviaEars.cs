using System.Collections.Concurrent;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using EventHorizon.Hosting;
using EventHorizon.Models;

namespace EventHorizon;

internal sealed class TriviaEars : IDisposable
{
    private static readonly HashSet<XivChatType> Channels =
    [
        XivChatType.Say,
        XivChatType.Shout,
        XivChatType.Yell,
        XivChatType.Party,
        XivChatType.Alliance,
    ];

    private readonly HorizonApp app;
    private readonly IChatGui chat;
    private readonly IObjectTable players;
    private readonly ConcurrentQueue<(string Name, string? World, string Message, bool Local)> waiting = new();

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
            this.Apply(heard.Name, heard.World, heard.Message, heard.Local);
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
            if (text.StartsWith("/yell", StringComparison.OrdinalIgnoreCase))
                text = text[5..].Trim();
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
            this.waiting.Enqueue((name, world, text, name.Length == 0 || this.IsLocal(name)));
        }
        catch (Exception ex)
        {
            this.app.Log.Error(ex, "Could not read a trivia answer.");
        }
    }

    private void Apply(string name, string? world, string message, bool local)
    {
        var truths = TwoTruths.Listening(this.app.Store);
        if (truths is not null)
        {
            this.ApplyTruths(truths.Value.Event, truths.Value.Game, name, world, message, local);
            return;
        }

        var desk = TriviaScore.Listening(this.app.Store);
        if (desk is null)
            return;

        var ev = desk.Value.Event;
        var game = desk.Value.Game;
        var progress = HostController.EditGame(this.app.Store.StateFor(ev.EventId), game.InstanceId);
        if (TriviaScore.Current(game, progress) is { } question && TriviaScore.IsQuestion(game, progress, question, message))
        {
            this.StartClock(ev, progress, question.Id);
            return;
        }

        if (TriviaScore.IsCountdown(message))
            return;

        if (name.Length == 0 && local)
            name = this.players.LocalPlayer?.Name.TextValue?.Trim() ?? "";
        if (name.Length == 0)
            name = "Someone";

        TriviaScore.Hear(progress, name, world, message, desk.Value.QuestionId);
        this.app.Store.MarkState(ev.EventId);
    }

    private void ApplyTruths(EventDocument ev, GameInstance game, string name, string? world, string message, bool local)
    {
        var progress = HostController.EditGame(this.app.Store.StateFor(ev.EventId), game.InstanceId);
        var round = HostController.CurrentRound(game, progress);
        if (round is not null && TwoTruths.IsRound(ev.Scripts, round, message))
        {
            this.StartClock(ev, progress, round.RoundId);
            return;
        }

        if (TriviaScore.IsCountdown(message) || (round is not null && TwoTruths.IsAnswer(ev.Scripts, round, message)))
            return;

        if (name.Length == 0 && local)
            name = this.players.LocalPlayer?.Name.TextValue?.Trim() ?? "";
        if (name.Length == 0)
            name = "Someone";

        TwoTruths.Hear(progress, name, world, message, round?.RoundId);
        this.app.Store.MarkState(ev.EventId);
    }

    private void StartClock(EventDocument ev, GameProgress progress, string questionId)
    {
        var now = DateTimeOffset.Now;
        var remaining = TriviaScore.Remaining(progress, questionId, now);
        var running = progress.Trivia?.TimerStarted;
        var already = running is null ? null : questionId + "\u001f" + running.Value.UtcTicks;
        if (remaining is > 0 && already is not null && this.app.Session.TriviaYells.ContainsKey(already))
            return;

        if (remaining is not > 0 && !TriviaScore.Begin(progress, questionId, now))
            return;

        var started = progress.Trivia?.TimerStarted;
        if (started is null)
            return;

        var key = questionId + "\u001f" + started.Value.UtcTicks;
        this.app.Session.TriviaYellKey = key;
        this.app.Session.TriviaYells[key] = -1;
        this.app.Store.MarkState(ev.EventId);
        this.app.Note("Timer started.");
    }

    private bool IsLocal(string name)
    {
        var local = this.players.LocalPlayer?.Name.TextValue;
        return !string.IsNullOrWhiteSpace(local) && string.Equals(local.Trim(), name, StringComparison.OrdinalIgnoreCase);
    }
}
