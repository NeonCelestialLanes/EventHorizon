using System.Diagnostics;
using EventHorizon.Hosting;
using EventHorizon.Models;
using EventHorizon.Persistence;
using EventHorizon.Theme;
using EventHorizon.Ui;

namespace EventHorizon;

internal enum UiRequest
{
    None,
    ToggleMain,
    OpenMain,
    OpenHost,
}

internal readonly record struct CommandResult(UiRequest Request, string? Message);

internal sealed class PendingConfirm
{
    public string Body { get; init; } = "";

    public Action? Run { get; init; }
}

internal sealed class PendingImport
{
    public string Kind { get; set; } = "";

    public string Path { get; set; } = "";

    public string? Id { get; set; }

    public List<string> Errors { get; set; } = [];

    public List<string> Warnings { get; set; } = [];

    public bool Choosing { get; set; }

    public bool ReportOnly { get; set; }
}

internal sealed class UiSession
{
    public PendingConfirm? Confirm { get; set; }

    public PendingImport? Import { get; set; }

    public string GameSearch { get; set; } = "";

    public string ScriptSearch { get; set; } = "";

    public string GameCategory { get; set; } = "All";

    public string ScriptCategory { get; set; } = "All";

    public bool ShowArchived { get; set; }

    public string PlayerName { get; set; } = "";

    public string UnoWorld { get; set; } = "";

    public string TriviaName { get; set; } = "";

    public string TriviaWorld { get; set; } = "";

    public string TriviaSearch { get; set; } = "";

    public string? TriviaPlayerId { get; set; }

    public string TiePrompt { get; set; } = "";

    public string TieAnswer { get; set; } = "";

    public bool TriviaAllAnswers { get; set; }

    public string QuickNote { get; set; } = "";

    public string ExtraKey { get; set; } = "";

    public string WinnerName { get; set; } = "";

    public bool RequestHost { get; set; }

    public bool RequestCompact { get; set; }

    public string? LibraryGameId { get; set; }

    public string? LibraryScriptId { get; set; }

    public string? InstanceId { get; set; }

    public string? EventScriptId { get; set; }

    public bool EditingGame { get; set; } = true;

    public string? Status { get; set; }

    public string? Error { get; set; }

    public int NotesMode { get; set; }

    public Dictionary<string, string> Drafts { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, int> CopyNext { get; } = new(StringComparer.Ordinal);

    public string? FocusScriptId { get; set; }

    public string? EditorId { get; set; }

    public string? DueCueId { get; set; }

    public string? DueCueTitle { get; set; }

    public string? DueCueKind { get; set; }

    public string? QuietCueId { get; set; }

    public DateTimeOffset NextChime { get; set; }
}

internal sealed class HorizonApp
{
    private readonly string? assemblyDirectory;

    public HorizonApp(string root, ILog log, string? assemblyDirectory)
    {
        this.Log = log;
        this.assemblyDirectory = assemblyDirectory;
        this.Store = new HorizonStore(root, log);
        this.Theme = new ThemeService();
    }

    public HorizonStore Store { get; }

    public ThemeService Theme { get; }

    public UiSession Session { get; } = new();

    public ScriptFace? ScriptFace { get; set; }

    public VenueTeleport? Travel { get; set; }

    public PlannerSync? Sync { get; set; }

    public ILog Log { get; }

    public void Start()
    {
        this.Store.LoadAll();
        var examples = string.IsNullOrWhiteSpace(this.assemblyDirectory) ? null : Path.Combine(this.assemblyDirectory, "Examples");
        this.Store.SeedExamples(examples);
        this.Log.Info("Event Horizon is ready.");
    }

    public void Flush() => this.Store.FlushIfIdle();

    public void Save() => this.Store.SaveDirty();

    public CommandResult Execute(string args)
    {
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var sub = parts.Length == 0 ? "" : parts[0].ToLowerInvariant();
        switch (sub)
        {
            case "":
                return new CommandResult(UiRequest.ToggleMain, null);
            case "host":
                if (this.Store.SelectedEvent() is null)
                    return new CommandResult(UiRequest.OpenMain, "Select an event first. Choose one from the dashboard, then use /eh host.");
                this.StartHosting(openWindow: true);
                return new CommandResult(UiRequest.OpenHost, "Live Host is open.");
            case "next":
                if (this.Store.SelectedEvent() is null)
                    return new CommandResult(UiRequest.None, "Select an event first.");
                this.Advance();
                return new CommandResult(UiRequest.OpenHost, null);
            case "reload":
                return new CommandResult(UiRequest.OpenMain, this.ReloadSelected());
            case "games":
                this.Store.Config.Section = "Games";
                this.Store.MarkConfig();
                this.Store.SaveDirty();
                return new CommandResult(UiRequest.OpenMain, null);
            case "schedule":
                if (this.Store.SelectedEvent() is null)
                    return new CommandResult(UiRequest.OpenMain, "Select an event first.");
                this.Store.Config.Section = "Events";
                this.Store.Config.ShowWorkspace = true;
                this.Store.Config.WorkspaceTab = "Schedule";
                this.Store.MarkConfig();
                this.Store.SaveDirty();
                return new CommandResult(UiRequest.OpenMain, null);
            default:
                return new CommandResult(UiRequest.None, "Try /eh, /eh host, /eh next, /eh reload, /eh games, or /eh schedule.");
        }
    }

    public void Select(string eventId)
    {
        this.Store.Select(eventId);
        this.Store.Config.Section = "Events";
        this.Store.Config.ShowWorkspace = true;
        this.Store.SaveDirty();
    }

    public void StartHosting(bool openWindow)
    {
        var ev = this.Store.SelectedEvent();
        if (ev is null)
        {
            this.Fail("Select an event first.");
            return;
        }

        if (this.Store.StateIsCorrupt(ev.EventId))
        {
            this.Fail("Live progress for this event could not be read. Reset it from the banner before hosting.");
            return;
        }

        var state = this.Store.StateFor(ev.EventId);
        HostController.Start(ev, state);
        this.Store.MarkEvent(ev.EventId);
        this.Store.MarkState(ev.EventId);
        this.Store.SaveDirty();
        this.Store.Config.Section = "Live";
        this.Store.MarkConfig();
        this.Store.SaveDirty();
        if (openWindow)
            this.Session.RequestHost = true;
        this.Note("Hosting " + ev.EventName + ".");
    }

    public void WatchCue(DateTimeOffset now)
    {
        var ev = this.Store.SelectedEvent();
        if (ev is null)
        {
            this.Session.DueCueId = null;
            return;
        }

        var due = ScheduleOrder.DueCue(ev, this.Store.StateFor(ev.EventId), now);
        if (due is null || due.ActivityId == this.Session.QuietCueId)
        {
            this.Session.DueCueId = null;
            return;
        }

        var fresh = due.ActivityId != this.Session.DueCueId;
        this.Session.DueCueId = due.ActivityId;
        this.Session.DueCueTitle = string.IsNullOrWhiteSpace(due.Title) ? "Untitled" : due.Title;
        this.Session.DueCueKind = due.Category == ActivityCategory.Game ? "party game" : "announcement";
        if (fresh || now >= this.Session.NextChime)
        {
            CueChime.Play();
            this.Session.NextChime = now.AddSeconds(20);
        }
    }

    public void OpenDueCue()
    {
        var ev = this.Store.SelectedEvent();
        var id = this.Session.DueCueId;
        if (ev is null || string.IsNullOrWhiteSpace(id))
            return;

        if (ev.Status is EventStatus.Draft or EventStatus.Planned or EventStatus.Ready)
            ev.Status = EventStatus.Live;

        HostController.StartActivity(ev, this.Store.StateFor(ev.EventId), id);
        this.Store.MarkEvent(ev.EventId);
        this.Store.MarkState(ev.EventId);
        this.Store.Config.Section = "Live";
        this.Store.MarkConfig();
        this.Store.SaveDirty();
        this.Session.DueCueId = null;
        this.Session.QuietCueId = id;
    }

    public void QuietCue()
    {
        this.Session.QuietCueId = this.Session.DueCueId;
        this.Session.DueCueId = null;
    }

    public void Advance()
    {
        var ev = this.Store.SelectedEvent();
        if (ev is null)
        {
            this.Fail("Select an event first.");
            return;
        }

        var state = this.Store.StateFor(ev.EventId);
        if (!HostController.Advance(ev, state))
        {
            this.Fail("This event has no activities to advance.");
            return;
        }

        this.Store.MarkState(ev.EventId);
        this.Note("Moved to the next activity.");
    }

    public void Previous()
    {
        var ev = this.Store.SelectedEvent();
        if (ev is null)
            return;

        var state = this.Store.StateFor(ev.EventId);
        if (!HostController.Previous(ev, state))
        {
            this.Fail("Already at the first activity.");
            return;
        }

        this.Store.MarkState(ev.EventId);
    }

    public string ReloadSelected()
    {
        var ev = this.Store.SelectedEvent();
        if (ev is null)
            return "Select an event first.";

        var dirty = this.Store.HasUnsavedContent;
        try
        {
            this.Store.ReloadEvent(ev.EventId);
        }
        catch (Exception ex)
        {
            this.Log.Error(ex, "Could not reload " + ev.EventId);
            this.Fail(ex.Message);
            return "Could not reload that event. " + ex.Message;
        }

        var warning = this.Store.StateWarnings.TryGetValue(ev.EventId, out var issues) && issues.Count > 0
            ? " Live progress references something the file no longer contains."
            : "";
        var discarded = dirty ? " Unsaved edits were discarded." : "";
        this.Note("Reloaded " + ev.EventName + "." + discarded + warning);
        return "Reloaded " + ev.EventName + "." + discarded + warning;
    }

    public void BeginImport(string kind, string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            if (kind == "validate")
            {
                this.Session.Import = this.Inspect(json, path);
                return;
            }

            var outcome = this.RunImport(kind, json, ImportChoice.Ask);
            this.ApplyOutcome(kind, path, outcome);
        }
        catch (Exception ex)
        {
            this.Log.Error(ex, "Could not import " + path);
            this.Session.Import = new PendingImport
            {
                Kind = kind,
                Path = path,
                ReportOnly = true,
                Errors = ["This file could not be read. " + ex.Message],
            };
        }
    }

    public void FinishImport(ImportChoice choice)
    {
        var pending = this.Session.Import;
        if (pending is null || pending.ReportOnly)
            return;

        try
        {
            var json = File.ReadAllText(pending.Path);
            var outcome = this.RunImport(pending.Kind, json, choice);
            this.ApplyOutcome(pending.Kind, pending.Path, outcome);
        }
        catch (Exception ex)
        {
            this.Log.Error(ex, "Could not finish import.");
            this.Fail(ex.Message);
            this.Session.Import = null;
        }
    }

    public void OpenPath(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = "\"" + path + "\"",
            UseShellExecute = true,
        });
    }

    public void Note(string message)
    {
        this.Session.Error = null;
        this.Session.Status = message;
    }

    public void Fail(string message)
    {
        this.Session.Status = null;
        this.Session.Error = message;
    }

    public void Ask(string body, Action run) => this.Session.Confirm = new PendingConfirm { Body = body, Run = run };

    private ImportOutcome RunImport(string kind, string json, ImportChoice choice) => kind switch
    {
        "event" => this.Store.ImportEvent(json, choice),
        "game" => this.Store.ImportGame(json, choice),
        "library" => this.Store.ImportLibrary(json, choice),
        "script" => this.Store.ImportScript(json, choice),
        _ => throw new InvalidDataException("Choose an event, game, game library, or script file."),
    };

    private void ApplyOutcome(string kind, string path, ImportOutcome outcome)
    {
        if (outcome.NeedsChoice)
        {
            this.Session.Import = new PendingImport
            {
                Kind = kind,
                Path = path,
                Id = outcome.Id,
                Choosing = true,
                Warnings = outcome.Report.Warnings,
            };
            return;
        }

        if (outcome.Success && outcome.Report.Warnings.Count == 0)
        {
            if (kind == "event" && outcome.Id is not null)
                this.Select(outcome.Id);
            this.Session.Import = null;
            this.Note("Imported " + (outcome.Id ?? kind) + ".");
            return;
        }

        this.Session.Import = new PendingImport
        {
            Kind = kind,
            Path = path,
            Id = outcome.Id,
            ReportOnly = true,
            Errors = outcome.Report.Errors,
            Warnings = outcome.Report.Warnings,
        };
        if (outcome.Success)
        {
            if (kind == "event" && outcome.Id is not null)
                this.Select(outcome.Id);
            this.Note("Imported " + (outcome.Id ?? kind) + ".");
        }
    }

    private PendingImport Inspect(string json, string path)
    {
        var kind = Validation.DetectKind(json);
        var report = new Report();
        try
        {
            report = kind switch
            {
                "event" => Validation.Event(JsonIo.ReadText<EventDocument>(json) ?? new EventDocument(), true),
                "game" => Validation.Game(JsonIo.ReadText<GameTemplate>(json) ?? new GameTemplate()),
                "script" => Validation.Script(JsonIo.ReadText<ScriptDocument>(json) ?? new ScriptDocument(), true),
                "library" => Validation.Library(this.ReadLibrary(json)),
                _ => FailReport("This file does not look like an Event Horizon event, game, or script."),
            };
        }
        catch (Exception ex)
        {
            report.Error(ex.Message);
        }

        return new PendingImport
        {
            Kind = kind,
            Path = path,
            ReportOnly = true,
            Errors = report.Errors,
            Warnings = report.Warnings,
        };
    }

    private GameLibraryBundle ReadLibrary(string json)
    {
        if (json.TrimStart().StartsWith('['))
        {
            return new GameLibraryBundle
            {
                SchemaVersion = Validation.SchemaVersion,
                Games = JsonIo.ReadText<List<GameTemplate>>(json) ?? [],
            };
        }

        return JsonIo.ReadText<GameLibraryBundle>(json) ?? new GameLibraryBundle();
    }

    private static Report FailReport(string message)
    {
        var report = new Report();
        report.Error(message);
        return report;
    }
}
