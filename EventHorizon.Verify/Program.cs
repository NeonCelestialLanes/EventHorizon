using EventHorizon;
using EventHorizon.Hosting;
using EventHorizon.Models;
using EventHorizon.Persistence;

var failed = 0;
var root = FindRepo();
var examples = Path.Combine(root, "Examples");

Check("sample event", () =>
{
    var ev = Read<EventDocument>(Path.Combine(examples, "event-starlight.json"));
    var report = Validation.Event(ev, true);
    Require(report.Ok, string.Join(" ", report.Errors));
    Require(ev.EventId == "evt-starlight", "event id");
    Require(ev.Games.Count == 4, "games");
    Require(ev.Schedule.Count == 8, "schedule");
});

Check("party template", () =>
{
    var ev = Read<EventDocument>(Path.Combine(examples, "party.json"));
    var report = Validation.Event(ev, true);
    Require(report.Ok, string.Join(" ", report.Errors));
    Require(ev.EventId == "evt-my-party", "event id");
    Require(ChatLines.Yells(ev.Scripts[0].Announcement).Count == 2, "welcome yells");
});

foreach (var file in Directory.GetFiles(examples, "game-*.json"))
{
    Check(Path.GetFileName(file), () =>
    {
        var game = Read<GameTemplate>(file);
        var report = Validation.Game(game);
        Require(report.Ok, string.Join(" ", report.Errors));
    });
}

Check("uno bracket", () =>
{
    var game = new GameInstance
    {
        InstanceId = "gme-uno",
        Formats = [GameFormat.Tournament],
        Tournament = new TournamentBoard { Seats = 4 },
    };
    var progress = new GameProgress();
    foreach (var name in new[] { "Nova", "Rio", "Lake", "Moss", "Rowan" })
        HostController.AddPlayer(progress, name);

    var message = HostController.BuildBracket(game, progress);
    Require(message is null, message ?? "bracket");
    var board = game.Tournament!;
    Require(board.Matches.Count(match => match.Round == 1) == 2, "two opening tables");
    var final = board.Matches.Single(match => match.Round == 2);
    var first = board.Matches.First(match => match.Round == 1);
    HostController.RecordWinner(progress, board, first, first.Players[0]);
    Require(HostController.SeatName(board, final, first.NextSeat!.Value, progress) == first.Players[0], "winner fills the next table");
    Require(progress.Winners.Count == 0, "a table win is not the tournament yet");

    var other = board.Matches.Last(match => match.Round == 1);
    HostController.RecordWinner(progress, board, other, other.Players[0]);
    HostController.RecordWinner(progress, board, final, first.Players[0]);
    Require(progress.Winners.Count == 1 && progress.Winners[0] == first.Players[0], "final names the winner");
});

Check("due cue", () =>
{
    var ev = new EventDocument
    {
        EventId = "evt-cue",
        EventName = "Cue",
        Date = "2026-11-12",
        Timezone = "UTC",
        Schedule =
        [
            new ScheduleActivity { ActivityId = "act-dj", StartTime = "20:00", EndTime = "20:10", Category = ActivityCategory.DjSet, Title = "Set" },
            new ScheduleActivity { ActivityId = "act-game", StartTime = "20:10", EndTime = "20:20", Category = ActivityCategory.Game, Title = "Uno" },
        ],
    };
    var state = new LiveEventState { CurrentActivityId = "act-dj" };
    var before = new DateTimeOffset(2026, 11, 12, 20, 9, 0, TimeSpan.Zero);
    var after = new DateTimeOffset(2026, 11, 12, 20, 10, 0, TimeSpan.Zero);
    Require(ScheduleOrder.DueCue(ev, state, before) is null, "the game is not due yet");
    Require(ScheduleOrder.DueCue(ev, state, after)?.ActivityId == "act-game", "the party game is due");
    state.CurrentActivityId = "act-game";
    Require(ScheduleOrder.DueCue(ev, state, after) is null, "the current game should stay quiet");
});

Check("yell prompts", () =>
{
    var one = ChatLines.Yells("/yell Welcome in, everyone.");
    Require(one.Count == 1 && one[0] == "/yell Welcome in, everyone.", "one yell stays whole");
    Require(!one[0].EndsWith('1'), "the copy has no number");

    var gathered = ChatLines.Yells("/yell We /gather/ as a community.");
    Require(gathered.Count == 1 && gathered[0].Contains("/gather/", StringComparison.Ordinal), "/gather/ stays inside the yell");

    var three = ChatLines.Yells("/yell First line.\n/yell Second line.\n/yell Third line.");
    Require(three.Count == 3 && three[0] == "/yell First line." && three[2] == "/yell Third line.", "each /yell is its own prompt");

    var scripts = new List<ScriptDocument>
    {
        new() { ScriptId = "scr-send", Announcement = "/yell First." },
        new() { ScriptId = "scr-send-chat-2", Announcement = "/yell Second." },
        new() { ScriptId = "scr-send-chat-10", Announcement = "/yell Tenth." },
        new() { ScriptId = "scr-send-cues", Announcement = "/yell Cue." },
        new() { ScriptId = "scr-send-cues-chat-2", Announcement = "/yell Cue two." },
    };
    var section = ChatLines.Section(scripts, "scr-send");
    Require(section.Count == 3 && section[0] == "/yell First." && section[1] == "/yell Second." && section[2] == "/yell Tenth.", "the section follows its own yells");
    var cues = ChatLines.Section(scripts, "scr-send-cues-chat-2");
    Require(cues.Count == 2 && cues[0] == "/yell Cue." && cues[1] == "/yell Cue two.", "a later yell still belongs to its section");
});

Check("venue teleport", () =>
{
    var house = new VenueInfo { World = "Sephirot", Ward = "24", Plot = "32", Notes = "Mist | Ward 24 | Plot 32." };
    Require(VenueAddress.TryCommand(house, out var command, out _, out var error), error);
    Require(command == "Sephirot mist 24 32", command);

    var beds = new VenueInfo { World = "Jenova", District = "Lavender Beds", Ward = "Ward 3", Plot = "plot 10" };
    Require(VenueAddress.TryCommand(beds, out var bedsCommand, out _, out _), "lavender beds");
    Require(bedsCommand == "Jenova lavender 3 10", bedsCommand);

    var hall = new VenueInfo { World = "Jenova", Name = "The Sample Hall", Notes = "A made-up room." };
    Require(!VenueAddress.TryCommand(hall, out _, out _, out var missing), "a room without a plot should not teleport");
    Require(!string.IsNullOrWhiteSpace(missing), "the reason is explained");
});

Check("sync code", () =>
{
    Require(PlannerSync.TryReadCode("ab3-k7m", out var code, out var host, out var port, out _), "a short code");
    Require(code == "AB3K7M" && host is null && port == 0, code);
    Require(PlannerSync.TryReadCode("AB3K7M@10.0.0.8:47821", out var remote, out var remoteHost, out var remotePort, out _), "an address code");
    Require(remote == "AB3K7M" && remoteHost == "10.0.0.8" && remotePort == 47821, remoteHost + ":" + remotePort);
    Require(!PlannerSync.TryReadCode("nope", out _, out _, out _, out var error), "a bad code is refused");
    Require(error.Length > 0, "the reason is explained");
});

Check("sample script", () =>
{
    var script = Read<ScriptDocument>(Path.Combine(examples, "script-welcome.json"));
    var report = Validation.Script(script, true);
    Require(report.Ok, string.Join(" ", report.Errors));
});

Check("daylight saving", () =>
{
    var summer = EventClock.Read("2026-07-15", "12:00", "America/New_York");
    Require(summer.Ok && summer.Instant.Offset == TimeSpan.FromHours(-4), "July offset " + summer.Instant.Offset);
    var winter = EventClock.Read("2026-12-05", "20:00", "America/New_York");
    Require(winter.Ok && winter.Instant.Offset == TimeSpan.FromHours(-5), "December offset " + winter.Instant.Offset);
    var gap = EventClock.Read("2026-03-08", "02:30", "America/New_York");
    Require(gap.Ok && !string.IsNullOrWhiteSpace(gap.Note), "spring gap should be reported");
});

Check("loose enums", () =>
{
    var ev = JsonIo.ReadText<EventDocument>("""
        { "schemaVersion": 1, "eventId": "evt-enum", "eventName": "Enum", "date": "2026-01-02",
          "schedule": [ { "activityId": "act-1", "title": "Set", "category": "DJ Set" } ] }
        """);
    Require(ev!.Schedule[0].Category == ActivityCategory.DjSet, "DJ Set");
});

Check("answers stay hidden", () =>
{
    var progress = new GameProgress();
    Require(!HostController.IsRevealed(progress, "rnd-1"), "starts hidden");
    HostController.Reveal(progress, "rnd-1");
    Require(HostController.IsRevealed(progress, "rnd-1"), "reveal");
    HostController.Hide(progress, "rnd-1");
    Require(!HostController.IsRevealed(progress, "rnd-1"), "hide");
});

Check("manual progression", () =>
{
    var ev = Read<EventDocument>(Path.Combine(examples, "event-starlight.json"));
    var state = new LiveEventState { EventId = ev.EventId };
    var before = state.CurrentActivityId;
    _ = ScheduleOrder.Suggested(ev, state, EventClock.Read(ev.Date, "20:30", ev.Timezone).Instant);
    Require(state.CurrentActivityId == before, "the clock must not move the host");
    HostController.Start(ev, state);
    Require(state.CurrentActivityId == "act-welcome", "start at welcome");
    HostController.Advance(ev, state);
    Require(ScheduleOrder.StatusOf(ev.Schedule[0], state) == ActivityProgressStatus.Completed, "welcome completed");
    Require(state.CurrentActivityId == "act-nova", "next is the DJ");
});

var temp = Path.Combine(Path.GetTempPath(), "event-horizon-verify-" + Guid.NewGuid().ToString("N"));
failed += StoreTests(temp);

if (failed > 0)
{
    Console.Error.WriteLine(failed + " check(s) failed.");
    return 1;
}

Console.WriteLine("Event Horizon checks passed.");
return 0;

int StoreTests(string directory)
{
    var local = 0;
    var store = new HorizonStore(directory, new QuietLog());
    store.LoadAll();
    try
    {
        var threw = false;
        try
        {
            store.ImportEvent("{", ImportChoice.Ask);
        }
        catch (System.Text.Json.JsonException)
        {
            threw = true;
        }

        if (!threw)
        {
            Console.Error.WriteLine("FAIL malformed json was accepted");
            local++;
        }

        var created = store.CreateEvent();
        var rejected = store.ImportEvent("""
            { "schemaVersion": 2, "eventId": "evt-future", "eventName": "Future", "date": "2026-12-05" }
            """, ImportChoice.Ask);
        if (rejected.Success || rejected.Report.Errors.Count == 0)
        {
            Console.Error.WriteLine("FAIL schema 2 was accepted");
            local++;
        }

        if (store.Events.ContainsKey("evt-future"))
        {
            Console.Error.WriteLine("FAIL schema 2 was written");
            local++;
        }

        var same = JsonIo.Clone(created);
        same.SchemaVersion = 1;
        var ask = store.ImportEvent(System.Text.Json.JsonSerializer.Serialize(same, JsonIo.Options), ImportChoice.Ask);
        if (!ask.NeedsChoice || store.Events[created.EventId].EventName != "New event")
        {
            Console.Error.WriteLine("FAIL conflict replaced without a choice");
            local++;
        }

        same.EventName = "Replaced";
        var replaced = store.ImportEvent(System.Text.Json.JsonSerializer.Serialize(same, JsonIo.Options), ImportChoice.Replace);
        if (!replaced.Success || store.Events[created.EventId].EventName != "Replaced")
        {
            Console.Error.WriteLine("FAIL replace did not keep the id");
            local++;
        }

        if (Directory.GetFiles(store.BackupDir, "*.json").Length == 0)
        {
            Console.Error.WriteLine("FAIL replace did not keep a backup");
            local++;
        }

        var copy = store.Duplicate(store.Events[created.EventId]);
        if (copy.EventId == created.EventId || File.Exists(store.StatePath(copy.EventId)))
        {
            Console.Error.WriteLine("FAIL duplicate did not get independent progress");
            local++;
        }

        var template = store.CreateGame();
        template.Name = "Library original";
        var instance = store.AddGameToEvent(copy, template);
        instance.Name = "Event copy";
        if (template.Name != "Library original" || copy.Games.Count != 1)
        {
            Console.Error.WriteLine("FAIL event game edited the library");
            local++;
        }

        store.DeleteGame(template.TemplateId);
        if (!store.Games.ContainsKey(template.TemplateId) == false || copy.Games.Count != 1)
        {
            Console.Error.WriteLine("FAIL deleting a template removed the event copy");
            local++;
        }

        var state = store.StateFor(copy.EventId);
        state.Activities["act-missing"] = new ActivityProgress { Status = ActivityProgressStatus.Live };
        state.CurrentActivityId = "act-missing";
        store.MarkState(copy.EventId);
        store.RefreshWarnings(copy.EventId);
        if (!store.StateWarnings.TryGetValue(copy.EventId, out var warnings) || warnings.Count == 0)
        {
            Console.Error.WriteLine("FAIL stale progress was silent");
            local++;
        }

        if (!File.ReadAllText(store.StatePath(copy.EventId)).Contains("act-missing", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("FAIL progress file was overwritten early");
            local++;
        }

        store.DropStaleProgress(copy.EventId);
        if (store.StateWarnings.ContainsKey(copy.EventId) || state.Activities.ContainsKey("act-missing"))
        {
            Console.Error.WriteLine("FAIL drop did not clear only the missing ids");
            local++;
        }

        var sample = File.ReadAllText(Path.Combine(examples, "event-starlight.json"));
        var imported = store.ImportEvent(sample, ImportChoice.Ask);
        if (!imported.Success)
        {
            Console.Error.WriteLine("FAIL sample import " + string.Join(" ", imported.Report.Errors));
            local++;
        }

        var send = File.ReadAllText(Path.Combine(examples, "game-send-or-delete.json"));
        var gameImport = store.ImportGame(send, ImportChoice.Ask);
        if (!gameImport.Success)
        {
            Console.Error.WriteLine("FAIL game import " + string.Join(" ", gameImport.Report.Errors));
            local++;
        }

        store.DeleteGame("tpl-send-or-delete");
        var starlight = store.Events["evt-starlight"];
        if (starlight.Games.All(game => game.InstanceId != "gme-send"))
        {
            Console.Error.WriteLine("FAIL sample event lost its game when the template was deleted");
            local++;
        }

        var party = store.Events["evt-starlight"];
        var partyState = store.StateFor(party.EventId);
        HostController.Start(party, partyState);
        HostController.Advance(party, partyState);
        store.MarkState(party.EventId);
        store.ResetParty(party.EventId);
        var fresh = store.StateFor(party.EventId);
        if (fresh.Hosting || fresh.CurrentActivityId is not null || fresh.Activities.Count > 0 || party.Schedule.Count != 8 || party.Status != EventStatus.Ready)
        {
            Console.Error.WriteLine("FAIL reset party did not return the event to the start");
            local++;
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("FAIL store tests threw " + ex.GetType().Name + ": " + ex.Message);
        local++;
    }
    finally
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // The temp folder can be inspected if a test left it locked.
        }
    }

    return local;
}

static T Read<T>(string path)
{
    var value = JsonIo.Read<T>(path) ?? throw new InvalidDataException(path + " was empty.");
    return value;
}

static string FindRepo()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "Examples", "event-starlight.json")))
            return dir.FullName;
        dir = dir.Parent;
    }

    throw new DirectoryNotFoundException("Could not find the Examples folder.");
}

void Check(string name, Action body)
{
    try
    {
        body();
        Console.WriteLine("ok  " + name);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("FAIL " + name + ": " + ex.Message);
        Interlocked.Increment(ref failed);
    }
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

file sealed class QuietLog : ILog
{
    public void Info(string message)
    {
    }

    public void Error(Exception exception, string message)
    {
    }
}
