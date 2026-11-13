using System.Globalization;
using System.Text.Json;
using EventHorizon.Models;
using EventHorizon.Persistence;

namespace EventHorizon.Hosting;

internal sealed class Report
{
    public List<string> Errors { get; } = [];

    public List<string> Warnings { get; } = [];

    public bool Ok => this.Errors.Count == 0;

    public void Error(string message) => this.Errors.Add(message);

    public void Warn(string message) => this.Warnings.Add(message);
}

internal static class Validation
{
    public const int SchemaVersion = 1;

    public static Report Event(EventDocument ev, bool requireSchema)
    {
        var report = new Report();
        ev.Normalize();
        if (requireSchema)
            RequireSchema(report, ev.SchemaVersion, "event");

        RequireId(report, ev.EventId, "eventId");
        if (string.IsNullOrWhiteSpace(ev.EventName))
            report.Error("eventName is required.");
        if (string.IsNullOrWhiteSpace(ev.Date))
            report.Error("date is required.");
        else if (!DateOnly.TryParse(ev.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            report.Error("date must be yyyy-MM-dd.");

        CheckZone(report, ev.Timezone);
        CheckClock(report, ev.Date, ev.StartTime, ev.Timezone, "startTime");
        CheckClock(report, ev.Date, ev.EndTime, ev.Timezone, "endTime");
        Unique(report, ev.Staff.Select(staff => staff.StaffId), "staff");
        Unique(report, ev.Schedule.Select(activity => activity.ActivityId), "schedule");
        Unique(report, ev.Djs.Select(dj => dj.DjId), "djs");
        Unique(report, ev.Games.Select(game => game.InstanceId), "games");
        Unique(report, ev.Scripts.Select(script => script.ScriptId), "scripts");
        Unique(report, ev.Contests.Select(contest => contest.ContestId), "contests");
        Unique(report, ev.Prizes.Select(prize => prize.PrizeId), "prizes");
        Unique(report, ev.Notes.Select(note => note.NoteId), "notes");
        Unique(report, ev.Checklists.Select(item => item.ItemId), "checklists");

        var games = ev.Games.Select(game => game.InstanceId).ToHashSet(StringComparer.Ordinal);
        var scripts = ev.Scripts.Select(script => script.ScriptId).ToHashSet(StringComparer.Ordinal);
        var prizes = ev.Prizes.Select(prize => prize.PrizeId).ToHashSet(StringComparer.Ordinal);
        var djs = ev.Djs.Select(dj => dj.DjId).ToHashSet(StringComparer.Ordinal);

        foreach (var activity in ev.Schedule)
        {
            if (string.IsNullOrWhiteSpace(activity.Title))
                report.Warn($"Activity {activity.ActivityId} has no title.");
            CheckClock(report, ev.Date, activity.StartTime, ev.Timezone, $"activity {activity.ActivityId} startTime");
            CheckClock(report, ev.Date, activity.EndTime, ev.Timezone, $"activity {activity.ActivityId} endTime");
            Missing(report, activity.DjId, djs, "DJ");
            Missing(report, activity.GameInstanceId, games, "game");
            Missing(report, activity.ScriptId, scripts, "script");
            Missing(report, activity.PrizeId, prizes, "prize");
        }

        foreach (var game in ev.Games)
            GameContent(report, game.InstanceId, game.Formats, game.Rounds, game.Tournament, game.Name);

        return report;
    }

    public static Report Game(GameTemplate game)
    {
        var report = new Report();
        game.Normalize();
        RequireSchema(report, game.SchemaVersion, "game");
        RequireId(report, game.TemplateId, "templateId");
        if (string.IsNullOrWhiteSpace(game.Name))
            report.Error("name is required.");
        GameContent(report, game.TemplateId, game.Formats, game.Rounds, game.Tournament, game.Name);
        return report;
    }

    public static Report Script(ScriptDocument script, bool requireSchema)
    {
        var report = new Report();
        script.Normalize();
        if (requireSchema)
            RequireSchema(report, script.SchemaVersion, "script");
        RequireId(report, script.ScriptId, "scriptId");
        if (string.IsNullOrWhiteSpace(script.Title))
            report.Error("title is required.");
        return report;
    }

    public static Report Library(GameLibraryBundle bundle)
    {
        var report = new Report();
        if (bundle.SchemaVersion != SchemaVersion)
            report.Error($"schemaVersion must be {SchemaVersion}.");
        bundle.Games ??= [];
        Unique(report, bundle.Games.Select(game => game.TemplateId), "games");
        foreach (var game in bundle.Games)
        {
            var gameReport = Game(game);
            report.Errors.AddRange(gameReport.Errors);
            report.Warnings.AddRange(gameReport.Warnings);
        }

        return report;
    }

    public static List<string> StaleProgress(EventDocument ev, LiveEventState state)
    {
        var issues = new List<string>();
        var activities = ev.Schedule.Select(activity => activity.ActivityId).ToHashSet(StringComparer.Ordinal);
        var games = ev.Games.Select(game => game.InstanceId).ToHashSet(StringComparer.Ordinal);
        var prizes = ev.Prizes.Select(prize => prize.PrizeId).ToHashSet(StringComparer.Ordinal);
        var scripts = ev.Scripts.Select(script => script.ScriptId).ToHashSet(StringComparer.Ordinal);
        var clues = ev.Contests.SelectMany(contest => contest.Clues).Select(clue => clue.ClueId).ToHashSet(StringComparer.Ordinal);
        var checks = ev.Checklists.Select(item => item.ItemId).ToHashSet(StringComparer.Ordinal);
        var contests = ev.Contests.Select(contest => contest.ContestId).ToHashSet(StringComparer.Ordinal);

        if (state.CurrentActivityId is not null && !activities.Contains(state.CurrentActivityId))
            issues.Add($"Current activity {state.CurrentActivityId} is no longer in the event.");
        if (state.OpenGameInstanceId is not null && !games.Contains(state.OpenGameInstanceId))
            issues.Add($"Open game {state.OpenGameInstanceId} is no longer in the event.");

        MissingKeys(issues, state.Activities.Keys, activities, "Activity progress");
        MissingKeys(issues, state.Games.Keys, games, "Game progress");
        MissingKeys(issues, state.Prizes.Keys, prizes, "Prize progress");
        MissingKeys(issues, state.ContestWinners.Keys, contests, "Contest winner");
        MissingKeys(issues, state.ChecklistDone.Keys, checks, "Checklist progress");
        foreach (var scriptId in state.ReadScriptIds.Where(id => !scripts.Contains(id)))
            issues.Add($"Read script {scriptId} is no longer in the event.");
        foreach (var clueId in state.RevealedClueIds.Where(id => !clues.Contains(id)))
            issues.Add($"Revealed clue {clueId} is no longer in the event.");

        return issues;
    }

    public static void DropStale(EventDocument ev, LiveEventState state)
    {
        var activities = ev.Schedule.Select(activity => activity.ActivityId).ToHashSet(StringComparer.Ordinal);
        var games = ev.Games.Select(game => game.InstanceId).ToHashSet(StringComparer.Ordinal);
        var prizes = ev.Prizes.Select(prize => prize.PrizeId).ToHashSet(StringComparer.Ordinal);
        var scripts = ev.Scripts.Select(script => script.ScriptId).ToHashSet(StringComparer.Ordinal);
        var clues = ev.Contests.SelectMany(contest => contest.Clues).Select(clue => clue.ClueId).ToHashSet(StringComparer.Ordinal);
        var checks = ev.Checklists.Select(item => item.ItemId).ToHashSet(StringComparer.Ordinal);
        var contests = ev.Contests.Select(contest => contest.ContestId).ToHashSet(StringComparer.Ordinal);

        if (state.CurrentActivityId is not null && !activities.Contains(state.CurrentActivityId))
            state.CurrentActivityId = null;
        if (state.OpenGameInstanceId is not null && !games.Contains(state.OpenGameInstanceId))
            state.OpenGameInstanceId = null;

        RemoveMissing(state.Activities, activities);
        RemoveMissing(state.Games, games);
        RemoveMissing(state.Prizes, prizes);
        RemoveMissing(state.ContestWinners, contests);
        RemoveMissing(state.ChecklistDone, checks);
        state.ReadScriptIds.RemoveAll(id => !scripts.Contains(id));
        state.RevealedClueIds.RemoveAll(id => !clues.Contains(id));
    }

    public static string DetectKind(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
            return "library";
        if (root.ValueKind != JsonValueKind.Object)
            return "unknown";

        var hasGames = Has(root, "games");
        var hasEvent = Has(root, "eventName");
        var hasTemplate = Has(root, "templateId") || Has(root, "formats");
        var hasScript = Has(root, "spokenText") || (Has(root, "scriptId") && !hasEvent);
        if (hasEvent)
            return "event";
        if (hasGames && !hasTemplate)
            return "library";
        if (hasTemplate)
            return "game";
        if (hasScript)
            return "script";
        return "unknown";
    }

    private static void GameContent(Report report, string owner, List<GameFormat> formats, List<GameRound> rounds, TournamentBoard? tournament, string? name)
    {
        if (formats.Count == 0)
            report.Warn($"{Label(name, owner)} has no game format. It will be treated as custom.");
        Unique(report, rounds.Select(round => round.RoundId), $"rounds in {Label(name, owner)}");
        if (tournament is not null)
            Unique(report, tournament.Matches.Select(match => match.MatchId), $"matches in {Label(name, owner)}");
    }

    private static string Label(string? name, string owner) => string.IsNullOrWhiteSpace(name) ? owner : name;

    private static void RequireSchema(Report report, int? version, string kind)
    {
        if (version is null)
            report.Error("schemaVersion is required.");
        else if (version != SchemaVersion)
            report.Error($"schemaVersion {version} is not supported. This plugin reads version {SchemaVersion}.");
    }

    private static void RequireId(Report report, string? id, string field)
    {
        if (string.IsNullOrWhiteSpace(id))
            report.Error($"{field} is required.");
        else if (!Ids.IsValid(id))
            report.Error($"{field} '{id}' must start with a letter or number and use only letters, numbers, hyphens, and underscores.");
    }

    private static void Unique(Report report, IEnumerable<string?> ids, string collection)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                report.Error($"{collection} contains an item with no id.");
                continue;
            }

            if (!Ids.IsValid(id))
                report.Error($"{collection} id '{id}' uses unsupported characters.");
            else if (!seen.Add(id))
                report.Error($"{collection} id '{id}' is duplicated.");
        }
    }

    private static void CheckZone(Report report, string? timezone)
    {
        if (!EventClock.TryGetZone(timezone, out _))
            report.Warn($"Timezone '{timezone}' was not found on this computer. Times cannot be converted until it is corrected.");
    }

    private static void CheckClock(Report report, string? date, string? time, string? timezone, string field)
    {
        if (string.IsNullOrWhiteSpace(time))
            return;

        var reading = EventClock.Read(date, time, timezone);
        if (!reading.Ok)
            report.Error($"{field}: {reading.Note}");
        else if (!string.IsNullOrWhiteSpace(reading.Note))
            report.Warn($"{field}: {reading.Note}");
    }

    private static void Missing(Report report, string? id, HashSet<string> known, string label)
    {
        if (!string.IsNullOrWhiteSpace(id) && !known.Contains(id))
            report.Warn($"{label} '{id}' is referenced but not included in this event.");
    }

    private static void MissingKeys(List<string> issues, IEnumerable<string> keys, HashSet<string> known, string label)
    {
        foreach (var key in keys)
        {
            if (!known.Contains(key))
                issues.Add($"{label} {key} does not match the current event file.");
        }
    }

    private static void RemoveMissing<T>(Dictionary<string, T> values, HashSet<string> known)
    {
        foreach (var key in values.Keys.Where(key => !known.Contains(key)).ToList())
            values.Remove(key);
    }

    private static bool Has(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
