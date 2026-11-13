using EventHorizon.Models;

namespace EventHorizon.Hosting;

internal static class VenueAddress
{
    private static readonly (string Token, string Name)[] Districts =
    [
        ("lavender", "lavender beds"),
        ("goblet", "the goblet"),
        ("lavender", "lavender"),
        ("goblet", "goblet"),
        ("shirogane", "shirogane"),
        ("empyreum", "empyreum"),
        ("mist", "mist"),
    ];

    public static bool TryCommand(VenueInfo venue, out string command, out string where, out string error)
    {
        command = "";
        where = "";
        if (!TryDistrict(venue, out var district, out error))
            return false;
        if (!Number(venue.Ward, out var ward) || !Number(venue.Plot, out var plot))
        {
            error = "This venue needs a ward and a plot before it can teleport.";
            return false;
        }

        var world = (venue.World ?? "").Trim();
        command = string.IsNullOrWhiteSpace(world)
            ? district + " " + ward + " " + plot
            : world + " " + district + " " + ward + " " + plot;
        where = string.IsNullOrWhiteSpace(world)
            ? district + ", ward " + ward + ", plot " + plot
            : world + ", " + district + ", ward " + ward + ", plot " + plot;
        error = "";
        return true;
    }

    private static bool TryDistrict(VenueInfo venue, out string district, out string error)
    {
        district = "";
        error = "";
        if (!string.IsNullOrWhiteSpace(venue.District))
        {
            var written = venue.District.Trim();
            district = Match(written) ?? (written.Contains(' ') ? "" : written);
            if (district.Length > 0)
                return true;
        }

        var found = Match((venue.Notes ?? "") + "\n" + (venue.Name ?? ""));
        if (found is not null)
        {
            district = found;
            return true;
        }

        error = "Add the housing district on the venue. Mist, Lavender Beds, the Goblet, Shirogane, or Empyreum.";
        return false;
    }

    private static string? Match(string text)
    {
        var lower = text.ToLowerInvariant();
        string? token = null;
        var atBest = int.MaxValue;
        var lengthBest = -1;
        foreach (var (nameToken, name) in Districts)
        {
            var at = WordAt(lower, name);
            if (at < 0)
                continue;
            if (at < atBest || (at == atBest && name.Length > lengthBest))
            {
                atBest = at;
                lengthBest = name.Length;
                token = nameToken;
            }
        }

        return token;
    }

    private static int WordAt(string text, string word)
    {
        var at = 0;
        while (at < text.Length)
        {
            var found = text.IndexOf(word, at, StringComparison.Ordinal);
            if (found < 0)
                return -1;
            var before = found == 0 || !char.IsLetter(text[found - 1]);
            var after = found + word.Length >= text.Length || !char.IsLetter(text[found + word.Length]);
            if (before && after)
                return found;
            at = found + word.Length;
        }

        return -1;
    }

    private static bool Number(string? raw, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var start = 0;
        while (start < raw.Length && !char.IsDigit(raw[start]))
            start++;
        var end = start;
        while (end < raw.Length && char.IsDigit(raw[end]))
            end++;
        return end > start && int.TryParse(raw[start..end], out value) && value > 0;
    }
}
