using EventHorizon.Hosting;
using EventHorizon.Models;

namespace EventHorizon.Persistence;

internal interface ILog
{
    void Info(string message);

    void Error(Exception exception, string message);
}

internal sealed class HorizonConfig
{
    public int SchemaVersion { get; set; } = 1;

    public string ThemePreset { get; set; } = "MidnightNeon";

    public bool UseAccentOverride { get; set; }

    public float AccentR { get; set; } = 0.651f;

    public float AccentG { get; set; } = 0.424f;

    public float AccentB { get; set; } = 1f;

    public float UiScale { get; set; } = 1f;

    public bool CompactMode { get; set; }

    public float ScriptFontScale { get; set; } = 1.35f;

    public bool Animations { get; set; } = true;

    public bool ShowComputerLocalTime { get; set; }

    public string? SelectedEventId { get; set; }

    public int BackupCount { get; set; } = 12;

    public bool SeededSamples { get; set; }

    public string Section { get; set; } = "Dashboard";

    public string WorkspaceTab { get; set; } = "Overview";

    public bool ShowWorkspace { get; set; }

    public void Clamp()
    {
        this.UiScale = Math.Clamp(this.UiScale, 0.8f, 1.6f);
        this.ScriptFontScale = Math.Clamp(this.ScriptFontScale, 1f, 2.2f);
        this.BackupCount = Math.Clamp(this.BackupCount, 1, 50);
        if (string.IsNullOrWhiteSpace(this.ThemePreset))
            this.ThemePreset = "MidnightNeon";
        if (string.IsNullOrWhiteSpace(this.Section))
            this.Section = "Dashboard";
        if (string.IsNullOrWhiteSpace(this.WorkspaceTab))
            this.WorkspaceTab = "Overview";
    }
}

internal sealed class HorizonStore
{
    private readonly ILog log;
    private readonly Dictionary<string, LiveEventState> states = new(StringComparer.Ordinal);
    private readonly HashSet<string> corruptStates = new(StringComparer.Ordinal);
    private readonly HashSet<string> dirtyEvents = new(StringComparer.Ordinal);
    private readonly HashSet<string> dirtyGames = new(StringComparer.Ordinal);
    private readonly HashSet<string> dirtyScripts = new(StringComparer.Ordinal);
    private readonly HashSet<string> dirtyStates = new(StringComparer.Ordinal);
    private bool dirtyNotes;
    private bool dirtyConfig;
    private DateTime lastTouchUtc = DateTime.MinValue;

    public HorizonStore(string root, ILog log)
    {
        this.Root = root;
        this.log = log;
        Directory.CreateDirectory(this.EventsDir);
        Directory.CreateDirectory(this.GamesDir);
        Directory.CreateDirectory(this.ScriptsDir);
        Directory.CreateDirectory(this.StateDir);
        Directory.CreateDirectory(this.BackupDir);
        Directory.CreateDirectory(Path.Combine(root, "Notes"));
        this.Config = this.LoadConfig();
        this.Notes = new GeneralNotes();
    }

    public string Root { get; }

    public string EventsDir => Path.Combine(this.Root, "Events");

    public string GamesDir => Path.Combine(this.Root, "GameLibrary");

    public string ScriptsDir => Path.Combine(this.Root, "ScriptLibrary");

    public string StateDir => Path.Combine(this.Root, "State");

    public string BackupDir => Path.Combine(this.Root, "Backups");

    public HorizonConfig Config { get; private set; }

    public GeneralNotes Notes { get; private set; }

    public Dictionary<string, EventDocument> Events { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, GameTemplate> Games { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, ScriptDocument> Scripts { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> FileErrors { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, List<string>> StateWarnings { get; } = new(StringComparer.Ordinal);

    public bool HasUnsavedContent => this.dirtyEvents.Count > 0 || this.dirtyGames.Count > 0 || this.dirtyScripts.Count > 0 || this.dirtyNotes;

    public void LoadAll()
    {
        this.Events.Clear();
        this.Games.Clear();
        this.Scripts.Clear();
        this.states.Clear();
        this.FileErrors.Clear();
        this.LoadDirectory(this.EventsDir, this.ReadEvent);
        this.LoadDirectory(this.GamesDir, this.ReadGame);
        this.LoadDirectory(this.ScriptsDir, this.ReadScript);
        this.LoadNotes();
        this.RefreshWarnings(this.Config.SelectedEventId);
    }

    public void SeedExamples(string? examplesDirectory)
    {
        if (this.Config.SeededSamples)
            return;

        this.Config.SeededSamples = true;
        this.dirtyConfig = true;
        if (string.IsNullOrWhiteSpace(examplesDirectory) || !Directory.Exists(examplesDirectory))
        {
            this.log.Info("Event Horizon has no bundled examples to install.");
            this.SaveConfig();
            return;
        }

        foreach (var file in Directory.GetFiles(examplesDirectory, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var kind = Validation.DetectKind(json);
                var outcome = kind switch
                {
                    "event" => this.ImportEvent(json, ImportChoice.Copy),
                    "game" => this.ImportGame(json, ImportChoice.Copy),
                    "script" => this.ImportScript(json, ImportChoice.Copy),
                    _ => null,
                };
                if (outcome is { Success: false })
                    this.log.Info("Skipped example " + Path.GetFileName(file) + ": " + string.Join(" ", outcome.Report.Errors));
            }
            catch (Exception ex)
            {
                this.log.Error(ex, "Could not install example " + Path.GetFileName(file));
            }
        }

        if (this.Config.SelectedEventId is null && this.Events.Count > 0)
            this.Config.SelectedEventId = this.Events.Keys.OrderBy(id => id).First();

        this.SaveAll();
    }

    public void Touch()
    {
        this.lastTouchUtc = DateTime.UtcNow;
    }

    public void MarkEvent(string eventId)
    {
        this.dirtyEvents.Add(eventId);
        this.Touch();
    }

    public void MarkGame(string templateId)
    {
        this.dirtyGames.Add(templateId);
        this.Touch();
    }

    public void MarkScript(string scriptId)
    {
        this.dirtyScripts.Add(scriptId);
        this.Touch();
    }

    public void MarkNotes()
    {
        this.dirtyNotes = true;
        this.Touch();
    }

    public void MarkConfig() => this.dirtyConfig = true;

    public void MarkState(string eventId)
    {
        this.dirtyStates.Add(eventId);
        this.SaveState(eventId);
    }

    public void QueueState(string eventId)
    {
        this.dirtyStates.Add(eventId);
        this.Touch();
    }

    public void FlushIfIdle()
    {
        if (DateTime.UtcNow - this.lastTouchUtc < TimeSpan.FromMilliseconds(700))
            return;

        this.SaveDirty();
    }

    public void SaveAll()
    {
        foreach (var id in this.Events.Keys)
            this.dirtyEvents.Add(id);
        foreach (var id in this.Games.Keys)
            this.dirtyGames.Add(id);
        foreach (var id in this.Scripts.Keys)
            this.dirtyScripts.Add(id);
        this.dirtyNotes = true;
        this.dirtyConfig = true;
        foreach (var id in this.states.Keys)
            this.dirtyStates.Add(id);
        this.SaveDirty();
    }

    public void SaveDirty()
    {
        foreach (var id in this.dirtyEvents.ToList())
        {
            if (this.Events.TryGetValue(id, out var ev))
                this.WriteEvent(ev);
            this.dirtyEvents.Remove(id);
        }

        foreach (var id in this.dirtyGames.ToList())
        {
            if (this.Games.TryGetValue(id, out var game))
                this.WriteGame(game);
            this.dirtyGames.Remove(id);
        }

        foreach (var id in this.dirtyScripts.ToList())
        {
            if (this.Scripts.TryGetValue(id, out var script))
                this.WriteScript(script);
            this.dirtyScripts.Remove(id);
        }

        foreach (var id in this.dirtyStates.ToList())
            this.SaveState(id);

        if (this.dirtyNotes)
        {
            this.Notes.SchemaVersion = Validation.SchemaVersion;
            JsonIo.Write(this.NotesPath, this.Notes);
            this.dirtyNotes = false;
        }

        if (this.dirtyConfig)
            this.SaveConfig();
    }

    public event Action<string>? Changed;

    public void ApplySync(EventDocument ev, LiveEventState state)
    {
        ev.Normalize();
        state.Normalize();
        if (string.IsNullOrWhiteSpace(ev.EventId))
            return;

        state.EventId = ev.EventId;
        state.SchemaVersion = Validation.SchemaVersion;
        ev.SchemaVersion = Validation.SchemaVersion;
        Backups.Keep(this.BackupDir, this.EventPath(ev.EventId), this.Config.BackupCount);
        Backups.Keep(this.BackupDir, this.StatePath(ev.EventId), this.Config.BackupCount);
        this.corruptStates.Remove(ev.EventId);
        this.Events[ev.EventId] = ev;
        this.states[ev.EventId] = state;
        this.Config.SelectedEventId = ev.EventId;
        this.WriteEvent(ev);
        this.SaveState(ev.EventId);
        this.SaveConfig();
        this.RefreshWarnings(ev.EventId);
    }

    public EventDocument? SelectedEvent() =>
        this.Config.SelectedEventId is not null && this.Events.TryGetValue(this.Config.SelectedEventId, out var ev) ? ev : null;

    public LiveEventState StateFor(string eventId)
    {
        if (this.states.TryGetValue(eventId, out var cached))
            return cached;

        var path = this.StatePath(eventId);
        LiveEventState state;
        if (!File.Exists(path))
        {
            state = new LiveEventState { SchemaVersion = Validation.SchemaVersion, EventId = eventId };
        }
        else
        {
            try
            {
                state = JsonIo.Read<LiveEventState>(path) ?? new LiveEventState();
                state.Normalize();
                if (string.IsNullOrWhiteSpace(state.EventId))
                    state.EventId = eventId;
            }
            catch (Exception ex)
            {
                this.log.Error(ex, "Could not read live progress for " + eventId);
                this.corruptStates.Add(eventId);
                this.FileErrors[path] = "Live progress could not be read and will not be overwritten: " + ex.Message;
                state = new LiveEventState { SchemaVersion = Validation.SchemaVersion, EventId = eventId };
            }
        }

        state.Normalize();
        this.states[eventId] = state;
        return state;
    }

    public bool StateIsCorrupt(string eventId) => this.corruptStates.Contains(eventId);

    public void ResetCorruptState(string eventId)
    {
        var path = this.StatePath(eventId);
        if (File.Exists(path))
            Backups.Keep(this.BackupDir, path, this.Config.BackupCount);
        this.corruptStates.Remove(eventId);
        this.states[eventId] = new LiveEventState { SchemaVersion = Validation.SchemaVersion, EventId = eventId };
        this.SaveState(eventId);
    }

    public void ResetParty(string eventId)
    {
        if (!this.Events.TryGetValue(eventId, out var ev))
            return;

        var state = this.StateFor(eventId);
        foreach (var item in ev.Checklists)
        {
            if (state.ChecklistDone.ContainsKey(item.ItemId))
                item.Done = false;
        }

        foreach (var game in ev.Games)
        {
            if (game.Tournament is null)
                continue;

            foreach (var match in game.Tournament.Matches)
                match.Winner = null;
        }

        if (ev.Status is EventStatus.Live or EventStatus.Completed)
            ev.Status = EventStatus.Ready;

        this.corruptStates.Remove(eventId);
        this.StateWarnings.Remove(eventId);
        var path = this.StatePath(eventId);
        if (File.Exists(path))
            Backups.Keep(this.BackupDir, path, this.Config.BackupCount);

        this.states[eventId] = new LiveEventState { SchemaVersion = Validation.SchemaVersion, EventId = eventId };
        this.SaveState(eventId);
        this.WriteEvent(ev);
        this.dirtyEvents.Remove(eventId);
    }

    public void Select(string? eventId)
    {
        this.Config.SelectedEventId = eventId;
        this.Config.ShowWorkspace = eventId is not null;
        this.dirtyConfig = true;
        this.SaveConfig();
        this.RefreshWarnings(eventId);
    }

    public EventDocument CreateEvent()
    {
        var id = Ids.Unique("evt-", this.Events.Keys);
        var activityId = Ids.New("act-");
        var ev = new EventDocument
        {
            SchemaVersion = Validation.SchemaVersion,
            EventId = id,
            EventName = "New event",
            Date = DateTime.Now.ToString("yyyy-MM-dd"),
            Timezone = EventClockName.Default,
            StartTime = "20:00",
            EndTime = "23:00",
            Status = EventStatus.Draft,
            Schedule =
            [
                new ScheduleActivity
                {
                    ActivityId = activityId,
                    StartTime = "20:00",
                    EndTime = "20:15",
                    Title = "Welcome",
                    Category = ActivityCategory.Welcome,
                },
            ],
        };
        this.Events[id] = ev;
        this.WriteEvent(ev);
        this.Select(id);
        this.Config.WorkspaceTab = "Overview";
        this.SaveConfig();
        return ev;
    }

    public EventDocument Duplicate(EventDocument source)
    {
        var copy = JsonIo.Clone(source);
        copy.EventId = Ids.Unique("evt-", this.Events.Keys);
        copy.EventName = source.EventName + " copy";
        copy.Status = EventStatus.Draft;
        copy.SchemaVersion = Validation.SchemaVersion;
        this.Events[copy.EventId] = copy;
        this.WriteEvent(copy);
        this.Select(copy.EventId);
        return copy;
    }

    public void DeleteEvent(string eventId)
    {
        var path = this.EventPath(eventId);
        if (File.Exists(path))
            Backups.Keep(this.BackupDir, path, this.Config.BackupCount);
        var statePath = this.StatePath(eventId);
        if (File.Exists(statePath))
            Backups.Keep(this.BackupDir, statePath, this.Config.BackupCount);

        if (File.Exists(path))
            File.Delete(path);
        if (File.Exists(statePath))
            File.Delete(statePath);

        this.Events.Remove(eventId);
        this.states.Remove(eventId);
        this.dirtyEvents.Remove(eventId);
        this.StateWarnings.Remove(eventId);
        if (this.Config.SelectedEventId == eventId)
            this.Select(null);
    }

    public void Archive(string eventId)
    {
        if (!this.Events.TryGetValue(eventId, out var ev))
            return;

        ev.Status = EventStatus.Archived;
        this.WriteEvent(ev);
    }

    public GameTemplate CreateGame()
    {
        var game = new GameTemplate
        {
            SchemaVersion = Validation.SchemaVersion,
            TemplateId = Ids.Unique("tpl-", this.Games.Keys),
            Name = "New game",
            Formats = [GameFormat.Custom],
            Rounds =
            [
                new GameRound { RoundId = Ids.New("rnd-"), Title = "Round 1", Prompt = "" },
            ],
        };
        this.Games[game.TemplateId] = game;
        this.WriteGame(game);
        return game;
    }

    public GameTemplate DuplicateGame(GameTemplate source)
    {
        var copy = JsonIo.Clone(source);
        copy.TemplateId = Ids.Unique("tpl-", this.Games.Keys);
        copy.Name = source.Name + " copy";
        copy.SchemaVersion = Validation.SchemaVersion;
        this.Games[copy.TemplateId] = copy;
        this.WriteGame(copy);
        return copy;
    }

    public void DeleteGame(string templateId)
    {
        var path = this.GamePath(templateId);
        if (File.Exists(path))
        {
            Backups.Keep(this.BackupDir, path, this.Config.BackupCount);
            File.Delete(path);
        }

        this.Games.Remove(templateId);
        this.dirtyGames.Remove(templateId);
    }

    public GameInstance AddGameToEvent(EventDocument ev, GameTemplate template)
    {
        var instance = new GameInstance
        {
            InstanceId = Ids.Unique("gme-", ev.Games.Select(game => game.InstanceId)),
            TemplateId = template.TemplateId,
            Name = template.Name,
            Description = template.Description,
            Category = template.Category,
            Formats = template.Formats.ToList(),
            Rules = template.Rules,
            IntroScript = template.IntroScript,
            ClosingScript = template.ClosingScript,
            ScoringMethod = template.ScoringMethod,
            ParticipationMethod = template.ParticipationMethod,
            HostNotes = template.HostNotes,
            Rounds = JsonIo.Clone(template.Rounds),
            Tournament = template.Tournament is null ? null : JsonIo.Clone(template.Tournament),
        };
        ev.Games.Add(instance);
        this.MarkEvent(ev.EventId);
        return instance;
    }

    public ScriptDocument CreateScript()
    {
        var script = new ScriptDocument
        {
            SchemaVersion = Validation.SchemaVersion,
            ScriptId = Ids.Unique("scr-", this.Scripts.Keys),
            Title = "New script",
            Category = ScriptCategory.Custom,
            SpokenText = "",
        };
        this.Scripts[script.ScriptId] = script;
        this.WriteScript(script);
        return script;
    }

    public ScriptDocument DuplicateScript(ScriptDocument source)
    {
        var copy = JsonIo.Clone(source);
        copy.ScriptId = Ids.Unique("scr-", this.Scripts.Keys);
        copy.Title = source.Title + " copy";
        copy.SchemaVersion = Validation.SchemaVersion;
        copy.EventId = null;
        copy.ActivityId = null;
        this.Scripts[copy.ScriptId] = copy;
        this.WriteScript(copy);
        return copy;
    }

    public void DeleteScript(string scriptId)
    {
        var path = this.ScriptPath(scriptId);
        if (File.Exists(path))
        {
            Backups.Keep(this.BackupDir, path, this.Config.BackupCount);
            File.Delete(path);
        }

        this.Scripts.Remove(scriptId);
        this.dirtyScripts.Remove(scriptId);
    }

    public ScriptDocument CopyScriptToEvent(EventDocument ev, ScriptDocument source)
    {
        var copy = JsonIo.Clone(source);
        copy.ScriptId = Ids.Unique("scr-", ev.Scripts.Select(script => script.ScriptId));
        copy.SourceScriptId = source.ScriptId;
        copy.EventId = ev.EventId;
        copy.SchemaVersion = null;
        ev.Scripts.Add(copy);
        this.MarkEvent(ev.EventId);
        return copy;
    }

    public ImportOutcome ImportEvent(string json, ImportChoice choice)
    {
        var ev = JsonIo.ReadText<EventDocument>(json) ?? throw new InvalidDataException("The event file was empty.");
        ev.Normalize();
        var report = Validation.Event(ev, requireSchema: true);
        if (!report.Ok)
            return ImportOutcome.Blocked(report);

        var conflict = this.Events.ContainsKey(ev.EventId);
        if (conflict && choice == ImportChoice.Ask)
            return ImportOutcome.Conflict(ev.EventId, report);
        if (conflict && choice == ImportChoice.Replace)
        {
            var existing = this.EventPath(ev.EventId);
            if (File.Exists(existing))
                Backups.Keep(this.BackupDir, existing, this.Config.BackupCount);
        }

        if (conflict && choice == ImportChoice.Copy)
            ev.EventId = Ids.Unique("evt-", this.Events.Keys);

        ev.SchemaVersion = Validation.SchemaVersion;
        this.Events[ev.EventId] = ev;
        this.WriteEvent(ev);
        this.RefreshWarnings(ev.EventId);
        return ImportOutcome.Done(ev.EventId, report);
    }

    public ImportOutcome ImportGame(string json, ImportChoice choice)
    {
        var game = JsonIo.ReadText<GameTemplate>(json) ?? throw new InvalidDataException("The game file was empty.");
        var report = Validation.Game(game);
        if (!report.Ok)
            return ImportOutcome.Blocked(report);

        var conflict = this.Games.ContainsKey(game.TemplateId);
        if (conflict && choice == ImportChoice.Ask)
            return ImportOutcome.Conflict(game.TemplateId, report);
        if (conflict && choice == ImportChoice.Replace)
        {
            var existing = this.GamePath(game.TemplateId);
            if (File.Exists(existing))
                Backups.Keep(this.BackupDir, existing, this.Config.BackupCount);
        }

        if (conflict && choice == ImportChoice.Copy)
            game.TemplateId = Ids.Unique("tpl-", this.Games.Keys);

        game.SchemaVersion = Validation.SchemaVersion;
        this.Games[game.TemplateId] = game;
        this.WriteGame(game);
        return ImportOutcome.Done(game.TemplateId, report);
    }

    public ImportOutcome ImportLibrary(string json, ImportChoice choice)
    {
        GameLibraryBundle bundle;
        if (json.TrimStart().StartsWith('['))
        {
            var games = JsonIo.ReadText<List<GameTemplate>>(json) ?? [];
            bundle = new GameLibraryBundle { SchemaVersion = Validation.SchemaVersion, Games = games };
        }
        else
        {
            bundle = JsonIo.ReadText<GameLibraryBundle>(json) ?? throw new InvalidDataException("The library file was empty.");
        }

        var report = Validation.Library(bundle);
        if (!report.Ok)
            return ImportOutcome.Blocked(report);

        var conflicts = bundle.Games.Where(game => this.Games.ContainsKey(game.TemplateId)).Select(game => game.TemplateId).ToList();
        if (conflicts.Count > 0 && choice == ImportChoice.Ask)
            return ImportOutcome.Conflict(string.Join(", ", conflicts), report);

        foreach (var game in bundle.Games)
        {
            if (this.Games.ContainsKey(game.TemplateId) && choice == ImportChoice.Copy)
                game.TemplateId = Ids.Unique("tpl-", this.Games.Keys.Concat(bundle.Games.Select(item => item.TemplateId)));
            if (this.Games.ContainsKey(game.TemplateId) && choice == ImportChoice.Replace)
            {
                var existing = this.GamePath(game.TemplateId);
                if (File.Exists(existing))
                    Backups.Keep(this.BackupDir, existing, this.Config.BackupCount);
            }

            game.SchemaVersion = Validation.SchemaVersion;
            this.Games[game.TemplateId] = game;
            this.WriteGame(game);
        }

        return ImportOutcome.Done(bundle.Games.Count + " games", report);
    }

    public ImportOutcome ImportScript(string json, ImportChoice choice)
    {
        var script = JsonIo.ReadText<ScriptDocument>(json) ?? throw new InvalidDataException("The script file was empty.");
        var report = Validation.Script(script, requireSchema: true);
        if (!report.Ok)
            return ImportOutcome.Blocked(report);

        var conflict = this.Scripts.ContainsKey(script.ScriptId);
        if (conflict && choice == ImportChoice.Ask)
            return ImportOutcome.Conflict(script.ScriptId, report);
        if (conflict && choice == ImportChoice.Replace)
        {
            var existing = this.ScriptPath(script.ScriptId);
            if (File.Exists(existing))
                Backups.Keep(this.BackupDir, existing, this.Config.BackupCount);
        }

        if (conflict && choice == ImportChoice.Copy)
            script.ScriptId = Ids.Unique("scr-", this.Scripts.Keys);

        script.SchemaVersion = Validation.SchemaVersion;
        this.Scripts[script.ScriptId] = script;
        this.WriteScript(script);
        return ImportOutcome.Done(script.ScriptId, report);
    }

    public string ExportEvent(string eventId, string path)
    {
        var ev = this.Events[eventId];
        ev.SchemaVersion = Validation.SchemaVersion;
        JsonIo.Write(path, ev);
        return path;
    }

    public string ExportGame(string templateId, string path)
    {
        var game = this.Games[templateId];
        game.SchemaVersion = Validation.SchemaVersion;
        JsonIo.Write(path, game);
        return path;
    }

    public string ExportLibrary(string path)
    {
        var bundle = new GameLibraryBundle
        {
            SchemaVersion = Validation.SchemaVersion,
            Games = this.Games.Values.OrderBy(game => game.Name).ToList(),
        };
        JsonIo.Write(path, bundle);
        return path;
    }

    public string ExportScript(string scriptId, string path)
    {
        var script = this.Scripts[scriptId];
        script.SchemaVersion = Validation.SchemaVersion;
        JsonIo.Write(path, script);
        return path;
    }

    public void ReloadEvent(string eventId)
    {
        var path = this.EventPath(eventId);
        if (!File.Exists(path))
            throw new FileNotFoundException("That event file is no longer on disk.", path);

        this.ReadEvent(path);
        this.dirtyEvents.Remove(eventId);
        this.RefreshWarnings(eventId);
    }

    public void DropStaleProgress(string eventId)
    {
        if (!this.Events.TryGetValue(eventId, out var ev))
            return;

        var state = this.StateFor(eventId);
        Validation.DropStale(ev, state);
        this.StateWarnings.Remove(eventId);
        this.SaveState(eventId);
    }

    public void RefreshWarnings(string? eventId)
    {
        if (eventId is null || !this.Events.TryGetValue(eventId, out var ev))
            return;

        var issues = Validation.StaleProgress(ev, this.StateFor(eventId));
        if (issues.Count == 0)
            this.StateWarnings.Remove(eventId);
        else
            this.StateWarnings[eventId] = issues;
    }

    public string EventPath(string eventId) => Path.Combine(this.EventsDir, eventId + ".json");

    public string GamePath(string templateId) => Path.Combine(this.GamesDir, templateId + ".json");

    public string ScriptPath(string scriptId) => Path.Combine(this.ScriptsDir, scriptId + ".json");

    public string StatePath(string eventId) => Path.Combine(this.StateDir, eventId + ".state.json");

    private string NotesPath => Path.Combine(this.Root, "Notes", "general.json");

    private string ConfigPath => Path.Combine(this.Root, "config.json");

    private void SaveState(string eventId)
    {
        this.dirtyStates.Remove(eventId);
        if (this.corruptStates.Contains(eventId))
        {
            this.FileErrors[this.StatePath(eventId)] = "Live progress was not overwritten because the existing file could not be read.";
            return;
        }

        if (!this.states.TryGetValue(eventId, out var state))
            return;

        state.SchemaVersion = Validation.SchemaVersion;
        state.EventId = eventId;
        JsonIo.Write(this.StatePath(eventId), state);
        this.Changed?.Invoke(eventId);
    }

    private void WriteEvent(EventDocument ev)
    {
        ev.SchemaVersion = Validation.SchemaVersion;
        JsonIo.Write(this.EventPath(ev.EventId), ev);
        this.Changed?.Invoke(ev.EventId);
    }

    private void WriteGame(GameTemplate game)
    {
        game.SchemaVersion = Validation.SchemaVersion;
        JsonIo.Write(this.GamePath(game.TemplateId), game);
    }

    private void WriteScript(ScriptDocument script)
    {
        script.SchemaVersion = Validation.SchemaVersion;
        JsonIo.Write(this.ScriptPath(script.ScriptId), script);
    }

    private HorizonConfig LoadConfig()
    {
        if (!File.Exists(this.ConfigPath))
            return new HorizonConfig();

        try
        {
            var config = JsonIo.Read<HorizonConfig>(this.ConfigPath) ?? new HorizonConfig();
            config.Clamp();
            return config;
        }
        catch (Exception ex)
        {
            this.log.Error(ex, "Could not read config.json. A fresh configuration will be used.");
            var broken = this.ConfigPath + ".unreadable";
            try
            {
                File.Copy(this.ConfigPath, broken, overwrite: true);
            }
            catch (Exception copyError)
            {
                this.log.Error(copyError, "Could not keep the unreadable config.");
            }

            return new HorizonConfig();
        }
    }

    private void SaveConfig()
    {
        this.Config.Clamp();
        this.Config.SchemaVersion = Validation.SchemaVersion;
        JsonIo.Write(this.ConfigPath, this.Config);
        this.dirtyConfig = false;
    }

    private void LoadNotes()
    {
        if (!File.Exists(this.NotesPath))
        {
            this.Notes = new GeneralNotes { SchemaVersion = Validation.SchemaVersion };
            return;
        }

        try
        {
            this.Notes = JsonIo.Read<GeneralNotes>(this.NotesPath) ?? new GeneralNotes();
            this.Notes.Normalize();
        }
        catch (Exception ex)
        {
            this.log.Error(ex, "Could not read general notes.");
            this.FileErrors[this.NotesPath] = ex.Message;
            this.Notes = new GeneralNotes { SchemaVersion = Validation.SchemaVersion };
        }
    }

    private void LoadDirectory(string directory, Action<string> read)
    {
        foreach (var file in Directory.GetFiles(directory, "*.json"))
        {
            try
            {
                read(file);
            }
            catch (Exception ex)
            {
                this.log.Error(ex, "Could not read " + file);
                this.FileErrors[file] = ex.Message;
            }
        }
    }

    private void ReadEvent(string path)
    {
        var ev = JsonIo.Read<EventDocument>(path) ?? throw new InvalidDataException("The event file was empty.");
        ev.Normalize();
        if (string.IsNullOrWhiteSpace(ev.EventId))
            throw new InvalidDataException("eventId is missing.");
        this.Events[ev.EventId] = ev;
    }

    private void ReadGame(string path)
    {
        var game = JsonIo.Read<GameTemplate>(path) ?? throw new InvalidDataException("The game file was empty.");
        game.Normalize();
        if (string.IsNullOrWhiteSpace(game.TemplateId))
            throw new InvalidDataException("templateId is missing.");
        this.Games[game.TemplateId] = game;
    }

    private void ReadScript(string path)
    {
        var script = JsonIo.Read<ScriptDocument>(path) ?? throw new InvalidDataException("The script file was empty.");
        script.Normalize();
        if (string.IsNullOrWhiteSpace(script.ScriptId))
            throw new InvalidDataException("scriptId is missing.");
        this.Scripts[script.ScriptId] = script;
    }
}

internal enum ImportChoice
{
    Ask,
    Replace,
    Copy,
}

internal sealed class ImportOutcome
{
    public bool Success { get; init; }

    public bool NeedsChoice { get; init; }

    public string? Id { get; init; }

    public Report Report { get; init; } = new();

    public static ImportOutcome Blocked(Report report) => new() { Report = report };

    public static ImportOutcome Conflict(string id, Report report) => new() { NeedsChoice = true, Id = id, Report = report };

    public static ImportOutcome Done(string id, Report report) => new() { Success = true, Id = id, Report = report };
}

internal static class Backups
{
    public static void Keep(string backupDir, string sourcePath, int max)
    {
        if (!File.Exists(sourcePath))
            return;

        Directory.CreateDirectory(backupDir);
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var dest = Path.Combine(backupDir, name + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".json");
        File.Copy(sourcePath, dest, overwrite: false);
        var extras = new DirectoryInfo(backupDir)
            .GetFiles(name + "-*.json")
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Skip(Math.Max(1, max));
        foreach (var extra in extras)
            extra.Delete();
    }
}
