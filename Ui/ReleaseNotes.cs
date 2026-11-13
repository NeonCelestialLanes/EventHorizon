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
