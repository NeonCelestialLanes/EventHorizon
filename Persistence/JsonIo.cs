using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace EventHorizon.Persistence;

internal static class JsonIo
{
    public static readonly JsonSerializerOptions Options = Create();

    public static T? Read<T>(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json, Options);
    }

    public static T? ReadText<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    public static void Write<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(value, Options);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }

    public static T Clone<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, Options);
        return JsonSerializer.Deserialize<T>(json, Options)!;
    }

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        options.Converters.Add(new LooseEnumFactory());
        return options;
    }
}

internal sealed class LooseEnumFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var converter = typeof(LooseEnumConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter)Activator.CreateInstance(converter)!;
    }
}

internal sealed class LooseEnumConverter<T> : JsonConverter<T>
    where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number) && Enum.IsDefined(typeof(T), number))
            return (T)Enum.ToObject(typeof(T), number);

        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException($"Expected a {typeof(T).Name} string.");

        var raw = reader.GetString() ?? "";
        var key = Normalize(raw);
        foreach (var name in Enum.GetNames<T>())
        {
            if (Normalize(name) == key)
                return Enum.Parse<T>(name);
        }

        throw new JsonException($"'{raw}' is not a valid {typeof(T).Name}. Use one of: {string.Join(", ", Enum.GetNames<T>())}.");
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());

    private static string Normalize(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        var count = 0;
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
                buffer[count++] = char.ToLowerInvariant(character);
        }

        return new string(buffer[..count]);
    }
}

internal static partial class Ids
{
    public static bool IsValid(string? id) => !string.IsNullOrWhiteSpace(id) && Pattern().IsMatch(id);

    public static string New(string prefix)
    {
        var id = prefix + Guid.NewGuid().ToString("N")[..8];
        return id.Length <= 64 ? id : id[..64];
    }

    public static string Unique(string prefix, IEnumerable<string?> existing)
    {
        var taken = existing.Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.Ordinal);
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var id = New(prefix);
            if (!taken.Contains(id))
                return id;
        }

        return New(prefix);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$")]
    private static partial Regex Pattern();
}

internal static partial class Labels
{
    public static string Spaced(string name)
    {
        var spaced = Space().Replace(name, "$1 $2").Replace("Dj", "DJ");
        return spaced;
    }

    public static string Reward(Models.PrizeEntry prize)
    {
        var amount = prize.Amount?.ToString("0.##", CultureInfo.InvariantCulture);
        var label = string.IsNullOrWhiteSpace(prize.RewardName) ? Spaced(prize.RewardType.ToString()) : prize.RewardName.Trim();
        return prize.RewardType switch
        {
            Models.RewardType.Gil => string.IsNullOrWhiteSpace(amount) ? "Gil" : amount + " gil",
            Models.RewardType.AetherCoins => string.IsNullOrWhiteSpace(amount) ? "Aether coins" : amount + " aether coins",
            _ => string.IsNullOrWhiteSpace(amount) ? label : amount + " " + label,
        };
    }

    public static string Location(Models.EventDocument ev)
    {
        var parts = new List<string>();
        Add(parts, ev.Venue.Name);
        Add(parts, ev.Venue.World);
        var housing = string.Join(" ", new[] { ev.Venue.Ward, ev.Venue.Plot }.Where(part => !string.IsNullOrWhiteSpace(part)));
        Add(parts, housing);
        if (parts.Count == 0)
            Add(parts, ev.Venue.DataCenter);
        return parts.Count == 0 ? "Location not set" : string.Join(" · ", parts);
    }

    private static void Add(List<string> parts, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            parts.Add(value.Trim());
    }

    [GeneratedRegex("([a-z])([A-Z])")]
    private static partial Regex Space();
}
