namespace EventHorizon.Ui;

internal sealed class ReleaseNote
{
    public required string Version { get; init; }

    public required string Title { get; init; }

    public ReleaseSection[] Sections { get; init; } = [];
}

internal sealed class ReleaseSection
{
    public required string Heading { get; init; }

    public string[] Lines { get; init; } = [];
}

internal static class ReleaseNotes
{
    public static ReleaseNote[] Releases { get; } =
    [
        new ReleaseNote
        {
            Version = "1.0.6",
            Title = "Trivia and Two Truths host from the file.",
            Sections =
            [
                new ReleaseSection
                {
                    Heading = "New",
                    Lines =
                    [
                        "Trivia listens for say, yell, party, and alliance. The timer starts when the question is yelled.",
                        "The countdown yells last call at 15 seconds, then 3 at 10, 2 at 6, and 1 at 2.",
                        "The tiebreaker is the extra question already in the file.",
                        "Two Truths copies the round as a yell, and Copy answer changes the line each time.",
                        "Two Truths has the same timer, listens for answers, and copies a leaderboard.",
                        "Two Truths scores points. That roster no longer has Eliminated.",
                        "The Uno winner yell names the champion and asks for a round of applause.",
                        "Now playing follows the activity you are on.",
                    ],
                },
            ],
        },
        new ReleaseNote
        {
            Version = "1.0.5",
            Title = "Points, a winner yell, and live sync.",
            Sections =
            [
                new ReleaseSection
                {
                    Heading = "New",
                    Lines =
                    [
                        "Two Truths and a Chirp scores points.",
                        "Send or Delete has Announce the winner. It yells the last player standing and the prize.",
                        "Live sync keeps registrations, eliminations, and points from both planners.",
                        "The edit boxes wrap long lines.",
                    ],
                },
            ],
        },
        new ReleaseNote
        {
            Version = "1.0.4",
            Title = "Open a game in its own window.",
            Sections =
            [
                new ReleaseSection
                {
                    Heading = "New",
                    Lines =
                    [
                        "Open game opens that game in its own window.",
                        "Send or Delete and Two Truths and a Chirp can open registration. The tell word ignores capitalization.",
                        "Trivia can copy the question. Announce the point yells who earned it.",
                        "In Two Truths, click A, B, or C to copy that chirp. Copy cue pastes the round as one chat line.",
                        "Mark a player eliminated. The last one still standing stays at the top.",
                        "A Collect the Tags tell matches when it has every clue word, in any order.",
                        "The Discord voice script wraps inside the box.",
                    ],
                },
            ],
        },
        new ReleaseNote
        {
            Version = "1.0.3",
            Title = "The logo sits above Settings.",
            Sections =
            [
                new ReleaseSection
                {
                    Heading = "New",
                    Lines =
                    [
                        "The EH logo is in the sidebar, under Notes and above Settings. The background is transparent.",
                    ],
                },
            ],
        },
        new ReleaseNote
        {
            Version = "1.0.2",
            Title = "The EH logo is in the plugin list.",
            Sections =
            [
                new ReleaseSection
                {
                    Heading = "New",
                    Lines =
                    [
                        "Dalamud shows the Event Horizon logo in the plugin search.",
                    ],
                },
            ],
        },
        new ReleaseNote
        {
            Version = "1.0.1",
            Title = "What's new opens after an update.",
            Sections =
            [
                new ReleaseSection
                {
                    Heading = "New",
                    Lines =
                    [
                        "What's new opens when an update is installed. Close it, and it stays closed until the next one.",
                        "Settings has What's new.",
                    ],
                },
            ],
        },
        new ReleaseNote
        {
            Version = "1.0.0",
            Title = "Plan a party, then host it live.",
            Sections =
            [
                new ReleaseSection
                {
                    Heading = "New",
                    Lines =
                    [
                        "Import a party file, or start from the sample party.",
                        "Live Host reads the script. Copy announcement walks each /yell. Next moves on when you say so.",
                        "Teleport goes to the venue when Lifestream is on.",
                        "A sync code shares the selected event with other planners.",
                        "Click a name on a bracket to record that table's winner.",
                        "Midnight Neon, Afterword, and NCL Arcade are included.",
                    ],
                },
            ],
        },
    ];

    public static string CurrentVersion()
    {
        var version = typeof(Plugin).Assembly.GetName().Version;
        if (version == null)
            return Releases[0].Version;

        return version.Revision > 0
            ? version.ToString()
            : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
