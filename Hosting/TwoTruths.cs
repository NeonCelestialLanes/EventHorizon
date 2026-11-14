using EventHorizon.Models;
using EventHorizon.Persistence;

namespace EventHorizon.Hosting;

internal readonly record struct TruthRow(int Rank, string Label, int Points);

internal static class TwoTruths
{
    public static bool Is(GameInstance game)
    {
        var name = game.Name ?? "";
        return name.Contains("two truth", StringComparison.OrdinalIgnoreCase);
    }

    public static string Letter(string choice)
    {
        var text = choice.Trim();
        return text.Length > 0 && char.IsLetter(text[0]) && (text.Length == 1 || text[1] is '.' or ')' or ':')
            ? char.ToUpperInvariant(text[0]).ToString()
            : "";
    }

    public static string Body(string choice)
    {
        var text = choice.Trim();
        if (text.Length >= 2 && char.IsLetter(text[0]) && text[1] is '.' or ')' or ':')
        {
            var body = text[2..].Trim();
            if (body.Length > 0)
                return body;
        }

        return text;
    }

    public static string Answer(GameRound round)
    {
        var fake = Extra(round, "fictionalChirp");
        if (fake.Length > 0)
        {
            foreach (var choice in round.Choices)
            {
                if (!Body(choice).Equals(fake, StringComparison.OrdinalIgnoreCase))
                    continue;
                var letter = Letter(choice);
                if (letter.Length == 1)
                    return letter;
            }
        }

        var draft = Draft(round);
        if (draft.Length == 1)
            return draft;

        return Picked(round);
    }

    public static string Picked(GameRound round)
    {
        var answer = (round.CorrectAnswer ?? "").Trim();
        if (answer.Length == 0)
            return "";

        var letter = Letter(answer);
        if (letter.Length == 1)
            return letter;

        return char.IsLetter(answer[0]) ? char.ToUpperInvariant(answer[0]).ToString() : "";
    }

    public static string Draft(GameRound round)
    {
        if (round.Extras is null)
            return "";

        foreach (var pair in round.Extras)
        {
            if (!pair.Key.Equals("fakePositionDraft", StringComparison.OrdinalIgnoreCase))
                continue;

            var letter = Letter((pair.Value ?? "").Trim());
            if (letter.Length == 1)
                return letter;
            var raw = (pair.Value ?? "").Trim();
            if (raw.Length > 0 && char.IsLetter(raw[0]))
                return char.ToUpperInvariant(raw[0]).ToString();
        }

        return "";
    }

    private static string Extra(GameRound round, string key)
    {
        if (round.Extras is null)
            return "";

        foreach (var pair in round.Extras)
        {
            if (pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(pair.Value))
                return pair.Value.Trim();
        }

        return "";
    }

    public static IReadOnlyList<string> RoundYell(IReadOnlyList<ScriptDocument> scripts, GameRound round)
    {
        var paragraph = RoundCue.Paste(scripts, round);
        return paragraph.Length == 0 ? [] : ScriptSpeech.Fit("/yell " + paragraph);
    }

    public static IReadOnlyList<string> AnswerYell(IReadOnlyList<ScriptDocument> scripts, GameRound round)
    {
        var letter = Answer(round);
        if (letter.Length != 1)
            return [];

        var lead = "/yell And the fake Chirp was... " + letter + "!";
        var lines = new List<string>();
        foreach (var tail in Tails(Others(round, letter)))
            lines.Add(tail.Length == 0 ? lead : lead + " " + tail);

        var filed = AnswerTemplate(scripts);
        if (filed.Length > 0)
        {
            var legacy = filed.Replace("[ANSWER]", letter, StringComparison.OrdinalIgnoreCase);
            if (!legacy.StartsWith("/yell ", StringComparison.OrdinalIgnoreCase))
                legacy = "/yell " + legacy.Trim();
            if (!lines.Contains(legacy, StringComparer.OrdinalIgnoreCase))
                lines.Add(legacy);
        }

        return lines;
    }

    public static string NextAnswer(GameRound round, ref int cursor)
    {
        var lines = AnswerYell([], round).Where(line => !line.Contains("[ANSWER]", StringComparison.OrdinalIgnoreCase)).ToList();
        var rotating = lines.Where(line => !IsFiledAside(line)).ToList();
        if (rotating.Count == 0)
            rotating = lines;
        if (rotating.Count == 0)
            return "";

        if (cursor < 0)
            cursor = 0;
        var line = rotating[cursor % rotating.Count];
        cursor++;
        return line;
    }

    public static bool IsRound(IReadOnlyList<ScriptDocument> scripts, GameRound round, string message)
    {
        var heard = Spoken(message);
        if (heard.Length < 16)
            return false;

        foreach (var piece in RoundYell(scripts, round))
        {
            var line = Spoken(piece);
            if (line.Length == 0)
                continue;
            if (heard.Contains(line, StringComparison.OrdinalIgnoreCase))
                return true;
            if (line.Contains(heard, StringComparison.OrdinalIgnoreCase) && heard.Length >= Math.Min(line.Length, 40))
                return true;
            if (line.Length >= 24)
            {
                var slice = line.Length <= 48 ? line : line[..48];
                if (heard.Contains(slice, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    public static bool IsAnswer(IReadOnlyList<ScriptDocument> scripts, GameRound round, string message)
    {
        var heard = Spoken(message);
        if (heard.Length < 16)
            return false;

        foreach (var piece in AnswerYell(scripts, round).Concat(AnswerYell([], round)))
        {
            var line = Spoken(piece);
            if (line.Length == 0)
                continue;
            if (heard.Equals(line, StringComparison.OrdinalIgnoreCase) || heard.Contains(line, StringComparison.OrdinalIgnoreCase))
                return true;
            if (line.Contains(heard, StringComparison.OrdinalIgnoreCase) && heard.Length >= Math.Min(line.Length, 40))
                return true;
        }

        return false;
    }

    public static (EventDocument Event, GameInstance Game, string? RoundId)? Listening(HorizonStore store)
    {
        var ev = store.SelectedEvent();
        if (ev is null || string.IsNullOrWhiteSpace(store.StateFor(ev.EventId).OpenGameInstanceId))
            return null;

        var state = store.StateFor(ev.EventId);
        if (!state.Hosting)
            return null;

        var game = ev.Games.FirstOrDefault(item => item.InstanceId == state.OpenGameInstanceId);
        if (game is null || !Is(game))
            return null;

        var progress = HostController.PeekGame(state, game.InstanceId);
        return (ev, game, HostController.CurrentRound(game, progress)?.RoundId);
    }

    public static void Hear(GameProgress progress, string name, string? world, string message, string? roundId)
    {
        var text = message.Trim();
        if (text.Length == 0 || text.StartsWith('/'))
            return;
        if (text.Length > 240)
            text = text[..240];

        var player = TriviaScore.Ensure(progress, name, world);
        var desk = TriviaScore.Desk(progress);
        var order = desk.Heard.Count == 0 ? 1 : desk.Heard.Max(item => item.Order) + 1;
        desk.Heard.Add(new TriviaHeard
        {
            Order = order,
            Name = player.Name,
            World = player.World,
            Message = text,
            QuestionId = roundId,
        });
        if (desk.Heard.Count > 40)
            desk.Heard.RemoveAt(0);
    }

    public static List<TruthRow> Board(GameProgress progress)
    {
        var named = progress.Players
            .Where(player => !string.IsNullOrWhiteSpace(player.Name))
            .OrderByDescending(player => player.Score)
            .ThenBy(player => player.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var rows = new List<TruthRow>(named.Count);
        var place = 0;
        var seen = 0;
        int? previous = null;
        foreach (var player in named)
        {
            seen++;
            if (previous != player.Score)
            {
                place = seen;
                previous = player.Score;
            }

            rows.Add(new TruthRow(place, UnoTournament.Label(player.Name, player.World), player.Score));
        }

        return rows;
    }

    public static IReadOnlyList<string> TopThree(IReadOnlyList<TruthRow> rows, int done, int left)
    {
        if (rows.All(row => row.Points <= 0))
            return [];

        var shown = rows.Where(row => row.Rank <= 3).ToList();
        var entries = shown.Select(row => Place(row.Rank) + ": " + row.Label + " (" + row.Points + " " + PointWord(row.Points) + ")").ToList();
        var header = "/yell TWO TRUTHS LEADERBOARD! After " + Count(done, "round") + ", here's where we stand! ";
        var tail = left > 0
            ? " We've still got " + Count(left, "round") + " to go, so it's anyone's game!"
            : " That's every round.";
        return TriviaScore.Pack(header, entries, ", ", tail);
    }

    public static IReadOnlyList<string> AllScores(IReadOnlyList<TruthRow> rows)
    {
        if (rows.Count == 0 || rows.All(row => row.Points <= 0))
            return [];

        var entries = rows.Select(row => Place(row.Rank) + " - " + row.Label + ": " + row.Points + (row.Points == 1 ? " pt" : " pts")).ToList();
        return TriviaScore.Pack("/yell TWO TRUTHS SCORES: ", entries, " | ", null);
    }

    public static IReadOnlyList<string> Standings(IReadOnlyList<TruthRow> rows, int left)
    {
        var groups = rows.Where(row => row.Points > 0).GroupBy(row => row.Rank).OrderBy(group => group.Key).Take(3).ToList();
        if (groups.Count == 0)
            return [];

        var first = groups[0].ToList();
        var clause = first.Count == 1
            ? first[0].Label + " is currently in first place with " + first[0].Points + " " + PointWord(first[0].Points)
            : JoinNames(first.Select(row => row.Label)) + " are tied for first with " + first[0].Points + " " + PointWord(first[0].Points);
        if (groups.Count > 1)
        {
            var rest = groups.Skip(1).Select(group =>
            {
                var people = group.ToList();
                return JoinNames(people.Select(row => row.Label)) + " with " + people[0].Points;
            });
            clause += ", followed by " + string.Join(" and ", rest);
        }

        var tail = left > 0
            ? " We've still got plenty of rounds left, so keep those guesses coming!"
            : " That's the last of the rounds.";
        var line = "/yell All right, everybody! Let's take a look at our Two Truths standings! " + clause + "." + tail;
        return line.Length <= ChatLines.YellLimit
            ? [line]
            : TriviaScore.Pack("/yell Two Truths standings: ", groups.SelectMany(group => group).Select(row => Place(row.Rank) + " " + row.Label + " (" + row.Points + ")").ToList(), ", ", null);
    }

    public static IReadOnlyList<string> WinnerYell(IReadOnlyList<ScriptDocument> scripts, IReadOnlyList<TruthRow> rows)
    {
        var leaders = rows.Where(row => row.Rank == 1 && row.Points > 0).ToList();
        if (leaders.Count == 0)
            return [];
        if (leaders.Count > 1)
            return ScriptSpeech.Fit("/yell Two Truths is tied! " + JoinNames(leaders.Select(row => row.Label)) + " are tied with " + leaders[0].Points + " " + PointWord(leaders[0].Points) + ".");

        var template = WinnerTemplate(scripts);
        var line = template.Length == 0
            ? "/yell Congratulations to " + leaders[0].Label + "! You've just won yourself 1,250 Aether Coins! Apparently, spending too much time on Chirper really does pay off!"
            : template.Replace("[WINNER]", leaders[0].Label, StringComparison.OrdinalIgnoreCase);
        if (!line.StartsWith("/yell ", StringComparison.OrdinalIgnoreCase))
            line = "/yell " + line.Trim();
        return ScriptSpeech.Fit(line);
    }

    private static string WinnerTemplate(IReadOnlyList<ScriptDocument> scripts)
    {
        foreach (var script in scripts)
        {
            var announcement = (script.Announcement ?? "").Trim();
            var title = (script.Title ?? "") + " " + (script.ScriptId ?? "");
            if (announcement.Contains("[WINNER]", StringComparison.OrdinalIgnoreCase) && title.Contains("truth", StringComparison.OrdinalIgnoreCase))
                return announcement;
        }

        return "";
    }

    private static string Place(int rank)
    {
        var teen = rank % 100;
        if (teen is >= 11 and <= 13)
            return rank + "th";

        return (rank % 10) switch
        {
            1 => rank + "st",
            2 => rank + "nd",
            3 => rank + "rd",
            _ => rank + "th",
        };
    }

    private static string PointWord(int points) => points == 1 ? "point" : "points";

    private static string Count(int number, string noun) => number + " " + noun + (number == 1 ? "" : "s");

    private static string JoinNames(IEnumerable<string> names)
    {
        var list = names.ToList();
        if (list.Count <= 1)
            return list.FirstOrDefault() ?? "";
        if (list.Count == 2)
            return list[0] + " and " + list[1];
        return string.Join(", ", list.Take(list.Count - 1)) + ", and " + list[^1];
    }

    private static bool IsFiledAside(string line) =>
        line.Contains("people's heads", StringComparison.OrdinalIgnoreCase)
        || line.Contains("actually real. Sometimes", StringComparison.OrdinalIgnoreCase);

    private static string Others(GameRound round, string fake)
    {
        var letters = round.Choices
            .Select(Letter)
            .Where(letter => letter.Length == 1 && !letter.Equals(fake, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (letters.Count >= 2)
            return letters[0] + " and " + letters[1];
        return "";
    }

    private static IReadOnlyList<string> Tails(string others)
    {
        if (others.Length == 0)
        {
            return
            [
                "",
                "The other two were real.",
                "Yeah, the other two actually got posted.",
                "",
                "The other two are the genuine ones.",
                "Not gonna lie, the other two happened.",
                "Those other two were actually posted.",
                "So yeah, the other two are real.",
                "The other two? Those were real.",
                "I checked. The other two were real.",
            ];
        }

        return
        [
            "",
            others + " were real.",
            "Yeah, " + others + " actually got posted.",
            "",
            others + " are the genuine ones.",
            "Not gonna lie, " + others + " happened.",
            "Those other two were actually posted.",
            "So yeah, " + others + " are real.",
            others + " got posted for real.",
            "The other two? Those were real.",
            "I checked. " + others + " were real.",
        ];
    }

    private static string AnswerTemplate(IReadOnlyList<ScriptDocument> scripts)
    {
        foreach (var script in scripts)
        {
            var announcement = (script.Announcement ?? "").Trim();
            if (announcement.Contains("[ANSWER]", StringComparison.OrdinalIgnoreCase)
                && announcement.Contains("fake", StringComparison.OrdinalIgnoreCase)
                && !announcement.Contains("[WINNER]", StringComparison.OrdinalIgnoreCase))
                return announcement;
        }

        return "";
    }

    private static string Spoken(string message)
    {
        var heard = message.Trim();
        if (heard.StartsWith("/yell ", StringComparison.OrdinalIgnoreCase))
            heard = heard[6..].Trim();

        var builder = new System.Text.StringBuilder();
        var space = false;
        foreach (var character in heard)
        {
            if (char.IsWhiteSpace(character))
            {
                space = builder.Length > 0;
                continue;
            }

            if (space)
                builder.Append(' ');
            builder.Append(character);
            space = false;
        }

        return builder.ToString();
    }
}
