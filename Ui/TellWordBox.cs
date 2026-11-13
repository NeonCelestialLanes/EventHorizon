namespace EventHorizon.Ui;

internal static class TellWordBox
{
    public static void Draw(string? word, Action<string?> set, Action saved)
    {
        if (Chrome.Field("Tell word", word, value => set(string.IsNullOrWhiteSpace(value) ? null : value.Trim()), 32))
            saved();

        var shown = (word ?? "").Trim();
        Chrome.Muted(shown.Length == 0
            ? "The word a player /tells you to register. Capitalization does not matter. Leave this blank on Uno to keep uno."
            : "Players register with a tell that says " + shown + ". Capitalization does not matter.");
    }
}
