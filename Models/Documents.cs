namespace EventHorizon.Models;

public enum EventStatus
{
    Draft,
    Planned,
    Ready,
    Live,
    Completed,
    Archived,
}

public enum ActivityCategory
{
    Welcome,
    Announcement,
    DjSet,
    Game,
    Contest,
    Giveaway,
    Break,
    Intermission,
    Closing,
    Custom,
}

public enum ActivityProgressStatus
{
    Scheduled,
    Live,
    Completed,
    Skipped,
    Delayed,
    Paused,
}

public enum GameFormat
{
    MultipleChoice,
    QuestionAnswer,
    Elimination,
    Points,
    Tournament,
    Voting,
    Collection,
    Custom,
}

public enum ScriptCategory
{
    Welcome,
    DjIntroduction,
    DjTransition,
    GameIntroduction,
    GameInstructions,
    GameCommentary,
    WinnerAnnouncement,
    Giveaway,
    GeneralAnnouncement,
    Closing,
    Custom,
}

public enum RewardType
{
    Gil,
    AetherCoins,
    Item,
    Custom,
}

public enum AwardStatus
{
    NotAwarded,
    Pending,
    Awarded,
}

public sealed class EventDocument
{
    public int? SchemaVersion { get; set; }

    public string EventId { get; set; } = "";

    public string EventName { get; set; } = "";

    public string? Description { get; set; }

    public string Date { get; set; } = "";

    public string Timezone { get; set; } = EventClockName.Default;

    public string? StartTime { get; set; }

    public string? EndTime { get; set; }

    public EventStatus Status { get; set; } = EventStatus.Draft;

    public VenueInfo Venue { get; set; } = new();

    public string? Organizer { get; set; }

    public List<StaffMember> Staff { get; set; } = [];

    public List<ScheduleActivity> Schedule { get; set; } = [];

    public List<DjSlot> Djs { get; set; } = [];

    public List<GameInstance> Games { get; set; } = [];

    public List<ScriptDocument> Scripts { get; set; } = [];

    public List<ContestEntry> Contests { get; set; } = [];

    public List<PrizeEntry> Prizes { get; set; } = [];

    public List<NoteEntry> Notes { get; set; } = [];

    public List<ChecklistItem> Checklists { get; set; } = [];

    public void Normalize()
    {
        this.Venue ??= new VenueInfo();
        this.Staff ??= [];
        this.Schedule ??= [];
        this.Djs ??= [];
        this.Games ??= [];
        this.Scripts ??= [];
        this.Contests ??= [];
        this.Prizes ??= [];
        this.Notes ??= [];
        this.Checklists ??= [];
        if (string.IsNullOrWhiteSpace(this.Timezone))
            this.Timezone = EventClockName.Default;

        foreach (var game in this.Games)
            game.Normalize();
        foreach (var script in this.Scripts)
            script.Normalize();
        foreach (var contest in this.Contests)
            contest.Normalize();
        foreach (var prize in this.Prizes)
            prize.Normalize();
    }
}

public static class EventClockName
{
    public const string Default = "America/New_York";
}

public sealed class VenueInfo
{
    public string? Name { get; set; }

    public string? DataCenter { get; set; }

    public string? World { get; set; }

    public string? District { get; set; }

    public string? Ward { get; set; }

    public string? Plot { get; set; }

    public string? Notes { get; set; }
}

public sealed class StaffMember
{
    public string StaffId { get; set; } = "";

    public string Name { get; set; } = "";

    public string? Role { get; set; }

    public string? Notes { get; set; }
}

public sealed class ScheduleActivity
{
    public string ActivityId { get; set; } = "";

    public string? StartTime { get; set; }

    public string? EndTime { get; set; }

    public string Title { get; set; } = "";

    public string? Description { get; set; }

    public ActivityCategory Category { get; set; } = ActivityCategory.Custom;

    public string? DjId { get; set; }

    public string? GameInstanceId { get; set; }

    public string? ScriptId { get; set; }

    public string? PrizeId { get; set; }

    public string? HostNotes { get; set; }

    /// <summary>Optional starting status from the event file. Live progress overrides it.</summary>
    public ActivityProgressStatus? Status { get; set; }
}

public sealed class DjSlot
{
    public string DjId { get; set; } = "";

    public string Name { get; set; } = "";

    public string? StartTime { get; set; }

    public string? EndTime { get; set; }

    public string? StreamUrl { get; set; }

    public string? IntroScriptId { get; set; }

    public string? TransitionScriptId { get; set; }

    public string? IntroText { get; set; }

    public string? TransitionText { get; set; }

    public string? Notes { get; set; }
}

public sealed class GameTemplate
{
    public int? SchemaVersion { get; set; }

    public string TemplateId { get; set; } = "";

    public string Name { get; set; } = "";

    public string? Description { get; set; }

    public string? Category { get; set; }

    public List<GameFormat> Formats { get; set; } = [];

    public string? Rules { get; set; }

    public string? IntroScript { get; set; }

    public string? ClosingScript { get; set; }

    public string? DefaultPrize { get; set; }

    public string? ScoringMethod { get; set; }

    public string? ParticipationMethod { get; set; }

    public string? TellWord { get; set; }

    public string? HostNotes { get; set; }

    public List<GameRound> Rounds { get; set; } = [];

    public TournamentBoard? Tournament { get; set; }

    public void Normalize()
    {
        this.Formats ??= [];
        this.Rounds ??= [];
        foreach (var round in this.Rounds)
            round.Normalize();
        this.Tournament?.Normalize();
        var tell = (this.TellWord ?? "").Trim();
        this.TellWord = tell.Length == 0 ? null : tell;
    }
}

public sealed class GameInstance
{
    public string InstanceId { get; set; } = "";

    public string? TemplateId { get; set; }

    public string Name { get; set; } = "";

    public string? Description { get; set; }

    public string? Category { get; set; }

    public List<GameFormat> Formats { get; set; } = [];

    public string? Rules { get; set; }

    public string? IntroScript { get; set; }

    public string? ClosingScript { get; set; }

    public string? PrizeId { get; set; }

    public string? ScoringMethod { get; set; }

    public string? ParticipationMethod { get; set; }

    public string? TellWord { get; set; }

    public string? HostNotes { get; set; }

    public List<GameRound> Rounds { get; set; } = [];

    public TournamentBoard? Tournament { get; set; }

    public void Normalize()
    {
        this.Formats ??= [];
        this.Rounds ??= [];
        foreach (var round in this.Rounds)
            round.Normalize();
        this.Tournament?.Normalize();
        var tell = (this.TellWord ?? "").Trim();
        this.TellWord = tell.Length == 0 ? null : tell;
    }
}

public sealed class GameRound
{
    public string RoundId { get; set; } = "";

    public string? Title { get; set; }

    public string? Prompt { get; set; }

    public string? Description { get; set; }

    public List<string> Choices { get; set; } = [];

    public string? CorrectAnswer { get; set; }

    public string? Explanation { get; set; }

    public string? SourceReference { get; set; }

    public string? FeaturedStaff { get; set; }

    public string? ImagePath { get; set; }

    public string? HostNotes { get; set; }

    public string? Announcement { get; set; }

    public Dictionary<string, string>? Extras { get; set; }

    public void Normalize()
    {
        this.Choices ??= [];
        if (this.Extras is { Count: 0 })
            this.Extras = null;
    }
}

public sealed class TournamentBoard
{
    public string Format { get; set; } = "SingleElimination";

    public int Seats { get; set; } = 2;

    public List<TournamentMatch> Matches { get; set; } = [];

    public void Normalize()
    {
        if (this.Seats < 2)
            this.Seats = 2;
        if (this.Seats > 8)
            this.Seats = 8;
        this.Matches ??= [];
        foreach (var match in this.Matches)
            match.Normalize();
    }
}

public sealed class TournamentMatch
{
    public string MatchId { get; set; } = "";

    public int Round { get; set; } = 1;

    public string? PlayerA { get; set; }

    public string? PlayerB { get; set; }

    public List<string> Players { get; set; } = [];

    public string? NextMatchId { get; set; }

    public int? NextSeat { get; set; }

    public string? Winner { get; set; }

    public string? Notes { get; set; }

    public void Normalize()
    {
        this.Players ??= [];
        if (this.Players.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(this.PlayerA))
                this.Players.Add(this.PlayerA);
            if (!string.IsNullOrWhiteSpace(this.PlayerB))
                this.Players.Add(this.PlayerB);
        }

        this.PlayerA = this.Players.Count > 0 && !string.IsNullOrWhiteSpace(this.Players[0]) ? this.Players[0].Trim() : null;
        this.PlayerB = this.Players.Count > 1 && !string.IsNullOrWhiteSpace(this.Players[1]) ? this.Players[1].Trim() : null;
    }
}

public sealed class ScriptDocument
{
    public int? SchemaVersion { get; set; }

    public string ScriptId { get; set; } = "";

    public string Title { get; set; } = "";

    public ScriptCategory Category { get; set; } = ScriptCategory.Custom;

    public string SpokenText { get; set; } = "";

    public string? StageDirections { get; set; }

    public string? Announcement { get; set; }

    public string? EventId { get; set; }

    public string? ActivityId { get; set; }

    public string? SourceScriptId { get; set; }

    public string? HostNotes { get; set; }

    public void Normalize() => this.SpokenText ??= "";
}

public sealed class ContestEntry
{
    public string ContestId { get; set; } = "";

    public string Title { get; set; } = "";

    public string? ContestType { get; set; }

    public string? Rules { get; set; }

    public string? StartTime { get; set; }

    public string? Deadline { get; set; }

    public string? PrizeId { get; set; }

    public string? SubmissionMethod { get; set; }

    public string? WinnerMethod { get; set; }

    public string? Winner { get; set; }

    public string? Notes { get; set; }

    public List<ContestClue> Clues { get; set; } = [];

    public void Normalize() => this.Clues ??= [];
}

public sealed class ContestClue
{
    public string ClueId { get; set; } = "";

    public string Content { get; set; } = "";

    public string? RevealTime { get; set; }

    public string? HostNotes { get; set; }
}

public sealed class PrizeEntry
{
    public string PrizeId { get; set; } = "";

    public string? GameInstanceId { get; set; }

    public string? ContestId { get; set; }

    public string? ActivityId { get; set; }

    public RewardType RewardType { get; set; } = RewardType.Custom;

    public string? RewardName { get; set; }

    public decimal? Amount { get; set; }

    public bool AllowMultipleWinners { get; set; }

    public AwardStatus? Status { get; set; }

    public List<string> Winners { get; set; } = [];

    public string? Notes { get; set; }

    public void Normalize() => this.Winners ??= [];
}

public sealed class NoteEntry
{
    public string NoteId { get; set; } = "";

    public string? Title { get; set; }

    public string? Category { get; set; }

    public string Body { get; set; } = "";
}

public sealed class ChecklistItem
{
    public string ItemId { get; set; } = "";

    public string Text { get; set; } = "";

    public bool Done { get; set; }
}

public sealed class GeneralNotes
{
    public int? SchemaVersion { get; set; } = 1;

    public List<NoteEntry> Notes { get; set; } = [];

    public List<ChecklistItem> Checklists { get; set; } = [];

    public void Normalize()
    {
        this.Notes ??= [];
        this.Checklists ??= [];
    }
}

public sealed class GameLibraryBundle
{
    public int? SchemaVersion { get; set; }

    public List<GameTemplate> Games { get; set; } = [];
}

public sealed class LiveEventState
{
    public int? SchemaVersion { get; set; } = 1;

    public string EventId { get; set; } = "";

    public bool Hosting { get; set; }

    public string? CurrentActivityId { get; set; }

    public string? OpenGameInstanceId { get; set; }

    public Dictionary<string, ActivityProgress> Activities { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, GameProgress> Games { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, AwardProgress> Prizes { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> ContestWinners { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, bool> ChecklistDone { get; set; } = new(StringComparer.Ordinal);

    public List<string> ReadScriptIds { get; set; } = [];

    public List<string> RevealedClueIds { get; set; } = [];

    public List<string> QuickNotes { get; set; } = [];

    public Dictionary<string, SpeechCursor> Speeches { get; set; } = new(StringComparer.Ordinal);

    public void Normalize()
    {
        this.Activities ??= new(StringComparer.Ordinal);
        this.Games ??= new(StringComparer.Ordinal);
        this.Prizes ??= new(StringComparer.Ordinal);
        this.ContestWinners ??= new(StringComparer.Ordinal);
        this.ChecklistDone ??= new(StringComparer.Ordinal);
        this.ReadScriptIds ??= [];
        this.RevealedClueIds ??= [];
        this.QuickNotes ??= [];
        this.Speeches ??= new(StringComparer.Ordinal);
        foreach (var speech in this.Speeches.Values)
            speech.Normalize();
        foreach (var game in this.Games.Values)
            game.Normalize();
        foreach (var prize in this.Prizes.Values)
            prize.Normalize();
    }
}

public sealed class SpeechCursor
{
    public int Index { get; set; }

    public List<int> Copied { get; set; } = [];

    public List<int> Delivered { get; set; } = [];

    public void Normalize()
    {
        this.Copied ??= [];
        this.Delivered ??= [];
        if (this.Index < 0)
            this.Index = 0;
    }
}

public sealed class ActivityProgress
{
    public ActivityProgressStatus Status { get; set; } = ActivityProgressStatus.Scheduled;

    public int DelayMinutes { get; set; }
}

public sealed class GameProgress
{
    public int RoundIndex { get; set; }

    public bool RegistrationOpen { get; set; }

    public List<string> RevealedRoundIds { get; set; } = [];

    public List<PlayerRecord> Players { get; set; } = [];

    public List<string> Winners { get; set; } = [];

    public Dictionary<string, int> Votes { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> MatchWinners { get; set; } = new(StringComparer.Ordinal);

    public TriviaLive? Trivia { get; set; }

    public void Normalize()
    {
        this.RevealedRoundIds ??= [];
        this.Players ??= [];
        this.Winners ??= [];
        this.Votes ??= new(StringComparer.Ordinal);
        this.MatchWinners ??= new(StringComparer.Ordinal);
        this.Trivia?.Normalize();
    }
}

public sealed class TriviaLive
{
    public bool Listening { get; set; }

    public bool Ended { get; set; }

    public int ViewIndex { get; set; }

    public string? TimerQuestionId { get; set; }

    public DateTimeOffset? TimerStarted { get; set; }

    public List<TriviaResult> Results { get; set; } = [];

    public List<TriviaAdjust> Adjustments { get; set; } = [];

    public List<TriviaHeard> Heard { get; set; } = [];

    public List<TriviaExtra> Extras { get; set; } = [];

    public List<TriviaAction> Actions { get; set; } = [];

    public void Normalize()
    {
        this.Results ??= [];
        this.Adjustments ??= [];
        this.Heard ??= [];
        this.Extras ??= [];
        this.Actions ??= [];
        if (this.ViewIndex < 0)
            this.ViewIndex = 0;
    }
}

public sealed class TriviaResult
{
    public string QuestionId { get; set; } = "";

    public string? PlayerId { get; set; }

    public bool Skipped { get; set; }
}

public sealed class TriviaAdjust
{
    public string Id { get; set; } = "";

    public string PlayerId { get; set; } = "";

    public int Delta { get; set; }

    public bool Undone { get; set; }
}

public sealed class TriviaHeard
{
    public int Order { get; set; }

    public string Name { get; set; } = "";

    public string? World { get; set; }

    public string Message { get; set; } = "";

    public string? QuestionId { get; set; }
}

public sealed class TriviaExtra
{
    public string QuestionId { get; set; } = "";

    public string Prompt { get; set; } = "";

    public string? Answer { get; set; }
}

public sealed class TriviaAction
{
    public string Op { get; set; } = "";

    public string? QuestionId { get; set; }

    public string? PlayerId { get; set; }

    public string? PreviousPlayerId { get; set; }

    public bool PreviousSkipped { get; set; }

    public string? AdjustId { get; set; }

    public int Delta { get; set; }

    public bool Undone { get; set; }
}

public sealed class PlayerRecord
{
    public string PlayerId { get; set; } = "";

    public string Name { get; set; } = "";

    public string? World { get; set; }

    public int Score { get; set; }

    public bool Eliminated { get; set; }

    public string? Notes { get; set; }
}

public sealed class AwardProgress
{
    public AwardStatus Status { get; set; } = AwardStatus.NotAwarded;

    public List<string> Winners { get; set; } = [];

    public void Normalize() => this.Winners ??= [];
}
