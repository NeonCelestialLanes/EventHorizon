using EventHorizon;
using EventHorizon.Hosting;
using EventHorizon.Models;
using EventHorizon.Persistence;
using EventHorizon.Ui;

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

Check("whats new", () =>
{
    Require(ReleaseNotes.Releases.Length > 0, "notes");
    Require(ReleaseNotes.Releases[0].Sections.Length > 0, "sections");
    var manifest = File.ReadAllText(Path.Combine(root, "EventHorizon.json"));
    var marker = "\"AssemblyVersion\": \"" + ReleaseNotes.Releases[0].Version + ".";
    Require(manifest.Contains(marker, StringComparison.Ordinal), "manifest version");
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

Check("uno registration", () =>
{
    Require(UnoTournament.IsSignup("Uno") && UnoTournament.IsSignup(" uno ") && UnoTournament.IsSignup("UNO"), "capitalization");
    Require(!UnoTournament.IsSignup("uno please") && !UnoTournament.IsSignup("hello"), "unrelated tells");
    var triviaWord = new GameInstance { Name = "Hall trivia", Category = "Trivia", TellWord = "trivia" };
    Require(TellSignup.Matches(triviaWord, " Trivia ") && !TellSignup.Matches(triviaWord, "trivia please"), "trivia tell");
    triviaWord.TellWord = "triva";
    Require(TellSignup.Matches(triviaWord, "TRIVA") && !TellSignup.Matches(triviaWord, "trivia"), "custom tell word");
    var unoWord = new GameInstance { Name = "Uno table", Category = "Tabletop", Formats = [GameFormat.Tournament], Tournament = new TournamentBoard() };
    Require(TellSignup.Word(unoWord) == "uno", "blank uno word");
    unoWord.TellWord = "cards";
    Require(TellSignup.Matches(unoWord, "cards") && !TellSignup.Matches(unoWord, "uno"), "replaced uno word");
    Require(UnoTournament.WorldAfter("Nova", "Nova Sephirot") == "Sephirot", "world");

    var progress = new GameProgress();
    Require(UnoTournament.TryRegister(progress, "Nova", "Sephirot", fromTell: true, bracketExists: false) == UnoSignup.Closed, "closed");
    progress.RegistrationOpen = true;
    Require(UnoTournament.TryRegister(progress, "Nova", "Sephirot", fromTell: true, bracketExists: false) == UnoSignup.Added, "first tell");
    Require(UnoTournament.TryRegister(progress, "nova", "sephirot", fromTell: true, bracketExists: false) == UnoSignup.Duplicate, "duplicate");
    Require(UnoTournament.TryRegister(progress, "Nova", "Zalera", fromTell: true, bracketExists: false) == UnoSignup.Added, "same name, other world");
    Require(progress.Players.Count == 2, "two players");

    var json = System.Text.Json.JsonSerializer.Serialize(progress, JsonIo.Options);
    var loaded = System.Text.Json.JsonSerializer.Deserialize<GameProgress>(json, JsonIo.Options);
    Require(loaded is { RegistrationOpen: true } && loaded.Players[0].World == "Sephirot", "reload keeps the roster");
    var older = System.Text.Json.JsonSerializer.Deserialize<GameProgress>("{\"players\":[{\"playerId\":\"ply-1\",\"name\":\"Rio\"}]}", JsonIo.Options);
    Require(older is { RegistrationOpen: false, Players.Count: 1 }, "older progress");
    var party = Read<EventDocument>(Path.Combine(examples, "party.json"));
    Require(Validation.Event(party, true).Ok, "older event file");

    var game = new GameInstance
    {
        InstanceId = "gme-uno",
        Name = "Uno table",
        Formats = [GameFormat.Tournament],
        Tournament = new TournamentBoard(),
    };
    var bracket = new GameProgress();
    foreach (var name in new[] { "Ana", "Bea", "Cal", "Dee", "Eve" })
        HostController.AddPlayer(bracket, name);
    var built = HostController.BuildDuelBracket(game, bracket, new Random(1));
    Require(built is null, built ?? "bracket");
    Require(!bracket.RegistrationOpen, "generating closes registration");
    var board = game.Tournament!;
    Require(board.Matches.Count(match => match.Round == 1) == 4, "first round");
    Require(board.Matches.Count(match => match.Notes == "Bye") == 3, "byes");
    Require(board.Matches.Where(match => match.Notes == "Bye").All(match => HostController.WinnerOf(bracket, match) == match.Players[0]), "bye advances");
    var final = board.Matches.Single(match => match.Round == board.Matches.Max(item => item.Round));
    Require(final.Round == 3, "final");

    var playing = board.Matches.Single(match => match.Round == 1 && match.Notes != "Bye");
    var winner = playing.Players[0];
    var other = playing.Players[1];
    HostController.RecordWinner(bracket, board, playing, winner);
    var next = board.Matches.Single(match => match.MatchId == playing.NextMatchId);
    Require(HostController.SeatName(board, next, playing.NextSeat!.Value, bracket) == winner, "advance");
    HostController.RecordWinner(bracket, board, playing, null);
    Require(HostController.SeatName(board, next, playing.NextSeat.Value, bracket) is null, "undo");
    HostController.RecordWinner(bracket, board, playing, other);
    Require(HostController.SeatName(board, next, playing.NextSeat.Value, bracket) == other, "correction");

    var ev = new EventDocument
    {
        EventId = "evt-uno",
        Games = [game],
        Prizes =
        [
            new PrizeEntry
            {
                PrizeId = "prz-uno",
                GameInstanceId = game.InstanceId,
                RewardType = RewardType.AetherCoins,
                Amount = 2000,
            },
        ],
    };
    bracket.Winners.Clear();
    bracket.Winners.Add(other);
    var state = new LiveEventState { EventId = ev.EventId };
    UnoTournament.LinkChampion(ev, game, bracket, state);
    Require(state.Prizes["prz-uno"].Status == AwardStatus.Pending && state.Prizes["prz-uno"].Winners[0] == other, "prize is pending");
    var line = UnoTournament.Announcement(other, UnoTournament.PrizeLabel(ev, game));
    Require(line.StartsWith("/yell ", StringComparison.Ordinal) && line.Contains(other, StringComparison.Ordinal) && line.Contains("2,000 Aether Coins", StringComparison.Ordinal), line);
});

Check("trivia scores", () =>
{
    var game = new GameInstance
    {
        InstanceId = "gme-trivia",
        Name = "Hall trivia",
        Category = "Trivia",
        Formats = [GameFormat.QuestionAnswer],
        Rounds =
        [
            new GameRound { RoundId = "q1", Prompt = "One", CorrectAnswer = "A" },
            new GameRound { RoundId = "q2", Prompt = "Two", CorrectAnswer = "B" },
            new GameRound { RoundId = "q3", Prompt = "Three", CorrectAnswer = "C" },
        ],
    };
    var progress = new GameProgress();
    var nova = TriviaScore.Ensure(progress, "Nova", "Sephirot");
    var otherNova = TriviaScore.Ensure(progress, "Nova", "Zalera");
    Require(nova.PlayerId != otherNova.PlayerId, "same name, different world");
    Require(TriviaScore.Ensure(progress, "nova", "sephirot").PlayerId == nova.PlayerId, "duplicate player");
    Require(TriviaScore.Award(progress, "q1", nova.PlayerId) is null, "first point");
    Require(TriviaScore.Award(progress, "q1", otherNova.PlayerId) is not null, "second answer gets no point");
    Require(TriviaScore.Award(progress, "q2", nova.PlayerId) is null, "another question");
    Require(TriviaScore.Points(progress.Trivia, nova.PlayerId) == 2, "two points");
    Require(TriviaScore.Skip(progress, "q3") is null, "skip");
    Require(TriviaScore.Points(progress.Trivia, nova.PlayerId) == 2, "skip adds nothing");
    Require(TriviaScore.Restore(progress, "q3") is null, "restore");
    Require(TriviaScore.Award(progress, "q3", otherNova.PlayerId) is null, "restored question");
    var rows = TriviaScore.Board(progress);
    Require(rows.Single(row => row.PlayerId == nova.PlayerId).Rank == 1, "leader");
    Require(rows.Single(row => row.PlayerId == otherNova.PlayerId).Rank == 2, "second");

    Require(TriviaScore.Reassign(progress, "q3", nova.PlayerId) is null, "reassign");
    Require(TriviaScore.Points(progress.Trivia, otherNova.PlayerId) == 0, "old player lost the point");
    Require(TriviaScore.Undo(progress) is null, "undo reassign");
    Require(TriviaScore.Points(progress.Trivia, otherNova.PlayerId) == 1, "point restored");

    var tied = TriviaScore.Ensure(progress, "Bea", "Sephirot");
    Require(TriviaScore.Award(progress, "q2", tied.PlayerId) is not null, "q2 already scored");
    progress.Trivia!.Results.RemoveAll(result => result.QuestionId == "q2");
    Require(TriviaScore.Award(progress, "q2", tied.PlayerId) is null, "bea scores q2");
    var tiedRows = TriviaScore.Board(progress);
    var novaRank = tiedRows.Single(row => row.PlayerId == nova.PlayerId).Rank;
    var beaRank = tiedRows.Single(row => row.PlayerId == tied.PlayerId).Rank;
    Require(novaRank == beaRank, "equal scores share a rank");

    var ev = new EventDocument
    {
        EventId = "evt-trivia",
        Games = [game],
        Prizes = [new PrizeEntry { PrizeId = "prz-trivia", GameInstanceId = game.InstanceId, RewardType = RewardType.AetherCoins, Amount = 1250 }],
    };
    var state = new LiveEventState { EventId = ev.EventId };
    Require(TriviaScore.Finish(ev, game, progress, state) is not null, "tie needs a tiebreaker");
    TriviaScore.AddTiebreak(game, progress, "One more", "Yes");
    var extra = TriviaScore.Current(game, progress);
    if (extra is not { Extra: true } tiebreak)
        throw new InvalidOperationException("tiebreaker is current");
    Require(TriviaScore.Award(progress, tiebreak.Id, nova.PlayerId) is null, "tiebreaker point");
    Require(TriviaScore.Finish(ev, game, progress, state) is null, "single winner");
    Require(state.Prizes["prz-trivia"].Status == AwardStatus.Pending, "prize pending");
    Require(progress.Winners[0] == "Nova (Sephirot)", progress.Winners[0]);
    var champion = TriviaScore.ChampionYell(progress.Winners[0], TriviaScore.Points(progress.Trivia, nova.PlayerId), TriviaScore.PrizeLabel(ev, game));
    Require(champion.StartsWith("/yell And we have our trivia champion!", StringComparison.Ordinal), champion);
    Require(champion.Contains("Nova (Sephirot)", StringComparison.Ordinal) && champion.Contains("1,250 Aether Coins", StringComparison.Ordinal), champion);

    var top = TriviaScore.TopThree(TriviaScore.Board(progress), 4, 0);
    Require(top.Count > 0 && top[0].StartsWith("/yell ", StringComparison.Ordinal), "top three yell");
    Require(top.All(line => line.Contains("Nova (Sephirot)", StringComparison.Ordinal) || top.Any(item => item.Contains("Nova (Sephirot)", StringComparison.Ordinal))), "names kept");
    var many = new GameProgress();
    for (var index = 0; index < 30; index++)
    {
        var player = TriviaScore.Ensure(many, "Player Name Number " + index.ToString("00"), "Sephirot");
        if (index % 2 == 0)
            TriviaScore.Adjust(many, player.PlayerId, index);
    }

    var packed = TriviaScore.AllScores(TriviaScore.Board(many));
    Require(packed.Count > 1, "long board splits");
    Require(packed.All(line => line.StartsWith("/yell ", StringComparison.Ordinal)), "each part is a yell");
    Require(packed.All(line => line.Length <= TriviaScore.YellLimit), "each yell fits");
    var combined = string.Join("\n", packed);
    Require(combined.Contains("Player Name Number 00", StringComparison.Ordinal) && combined.Contains("Player Name Number 29", StringComparison.Ordinal), "no name dropped");

    var json = System.Text.Json.JsonSerializer.Serialize(progress, JsonIo.Options);
    var loaded = System.Text.Json.JsonSerializer.Deserialize<GameProgress>(json, JsonIo.Options);
    Require(loaded?.Trivia is { Ended: true } && TriviaScore.Points(loaded.Trivia, nova.PlayerId) == TriviaScore.Points(progress.Trivia, nova.PlayerId), "reload keeps scores");
    var older = System.Text.Json.JsonSerializer.Deserialize<GameProgress>("{}", JsonIo.Options);
    Require(older?.Trivia is null, "older progress");
    var party = Read<EventDocument>(Path.Combine(examples, "party.json"));
    Require(Validation.Event(party, true).Ok, "older event");
    TriviaScore.Move(game, progress, -1);
    TriviaScore.Move(game, progress, 1);
    Require(TriviaScore.Current(game, progress) is not null, "navigation");

    var start = new DateTimeOffset(2026, 11, 13, 18, 0, 0, TimeSpan.Zero);
    var timed = new GameProgress();
    Require(TriviaScore.Arm(timed, "q1", start, false), "timer starts");
    Require(!TriviaScore.Arm(timed, "q1", start.AddSeconds(10), false), "timer keeps running");
    var running = TriviaScore.ReadClock(timed, "q1", start.AddSeconds(10));
    Require(running.Label == "0:50" && !running.Final && !running.Done, running.Label);
    var ending = TriviaScore.ReadClock(timed, "q1", start.AddSeconds(55));
    Require(ending.Final && ending.Label == "5", ending.Label);
    var lastSecond = TriviaScore.ReadClock(timed, "q1", start.AddSeconds(59.1));
    Require(lastSecond.Final && lastSecond.Label == "1", lastSecond.Label);
    var finished = TriviaScore.ReadClock(timed, "q1", start.AddSeconds(60));
    Require(finished.Done && finished.Label == "Time", finished.Label);
    Require(TriviaScore.Arm(timed, "q2", start.AddMinutes(1), false), "next question");
    Require(TriviaScore.ReadClock(timed, "q2", start.AddMinutes(1)).Label == "1:00", "fresh minute");
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
