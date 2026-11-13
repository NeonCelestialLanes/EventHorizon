using System.Globalization;
using EventHorizon.Models;

namespace EventHorizon.Hosting;

internal readonly record struct ClockReading(bool Ok, DateTimeOffset Instant, string? Note);

internal static class EventClock
{
    public static bool TryGetZone(string? id, out TimeZoneInfo zone)
    {
        var key = string.IsNullOrWhiteSpace(id) ? EventClockName.Default : id.Trim();
        if (TimeZoneInfo.TryFindSystemTimeZoneById(key, out var found))
        {
            zone = found;
            return true;
        }

        zone = TimeZoneInfo.Utc;
        return false;
    }

    public static ClockReading Read(string? date, string? time, string? timezone)
    {
        if (string.IsNullOrWhiteSpace(time))
            return new ClockReading(false, default, "Time is empty.");

        var trimmed = time.Trim();
        if (LooksAbsolute(trimmed))
            return ReadAbsolute(trimmed, timezone);

        if (!TimeOnly.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var clock))
            return new ClockReading(false, default, $"Could not read time '{time}'.");

        if (!DateOnly.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return new ClockReading(false, default, $"Could not read date '{date}'.");

        if (!TryGetZone(timezone, out var zone))
            return new ClockReading(false, default, $"Unknown timezone '{timezone}'.");

        var local = new DateTime(day.Year, day.Month, day.Day, clock.Hour, clock.Minute, clock.Second, DateTimeKind.Unspecified);
        return ToZone(local, zone);
    }

    public static DateTimeOffset SortKey(EventDocument ev, ScheduleActivity activity)
    {
        var reading = Read(ev.Date, activity.StartTime, ev.Timezone);
        return reading.Ok ? reading.Instant : DateTimeOffset.MaxValue;
    }

    public static string FormatDate(string? date)
    {
        if (string.IsNullOrWhiteSpace(date))
            return "Date not set";

        if (!DateOnly.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return date;

        return day.ToDateTime(TimeOnly.MinValue).ToString("dddd, MMMM d, yyyy", CultureInfo.CurrentCulture);
    }

    public static string FormatTime(DateTimeOffset instant, string? timezone)
    {
        if (!TryGetZone(timezone, out var zone))
            return instant.LocalDateTime.ToString("h:mm tt", CultureInfo.CurrentCulture);

        return TimeZoneInfo.ConvertTime(instant, zone).ToString("h:mm tt", CultureInfo.CurrentCulture);
    }

    public static string FormatClock(string? date, string? time, string? timezone)
    {
        var reading = Read(date, time, timezone);
        return reading.Ok ? FormatTime(reading.Instant, timezone) : (string.IsNullOrWhiteSpace(time) ? "—" : time);
    }

    public static string FormatComputerLocal(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.Local).ToString("h:mm tt", CultureInfo.CurrentCulture);

    public static string Countdown(DateTimeOffset target, DateTimeOffset now)
    {
        var span = target - now;
        if (span <= TimeSpan.Zero)
            return "Due now";
        if (span.TotalDays >= 1)
            return $"{(int)span.TotalDays}d {span.Hours}h";
        if (span.TotalHours >= 1)
            return $"{(int)span.TotalHours}h {span.Minutes}m";
        if (span.TotalMinutes >= 1)
            return $"{tMinutes(span)}m {span.Seconds}s";
        return $"{Math.Max(0, span.Seconds)}s";
    }

    public static string EventCountdown(EventDocument ev, DateTimeOffset now)
    {
        var start = Read(ev.Date, ev.StartTime, ev.Timezone);
        var end = Read(ev.Date, ev.EndTime, ev.Timezone);
        if (!start.Ok)
            return "Time not set";
        if (now < start.Instant)
            return "Starts in " + Countdown(start.Instant, now);
        if (!end.Ok || now <= end.Instant)
            return "Underway";
        return "Ended";
    }

    public static DateTimeOffset? EffectiveStart(EventDocument ev, ScheduleActivity activity, LiveEventState state)
    {
        var reading = Read(ev.Date, activity.StartTime, ev.Timezone);
        if (!reading.Ok)
            return null;

        var delay = state.Activities.TryGetValue(activity.ActivityId, out var progress) ? progress.DelayMinutes : 0;
        return reading.Instant.AddMinutes(delay);
    }

    private static int tMinutes(TimeSpan span) => (int)span.TotalMinutes;

    private static ClockReading ReadAbsolute(string text, string? timezone)
    {
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            return new ClockReading(false, default, $"Could not read time '{text}'.");

        if (HasExplicitOffset(text))
            return new ClockReading(true, parsed, null);

        if (!TryGetZone(timezone, out var zone))
            return new ClockReading(false, default, $"Unknown timezone '{timezone}'.");

        var local = DateTime.SpecifyKind(parsed.DateTime, DateTimeKind.Unspecified);
        return ToZone(local, zone);
    }

    private static ClockReading ToZone(DateTime local, TimeZoneInfo zone)
    {
        string? note = null;
        if (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
            note = "That local time falls in a daylight-saving gap, so it was moved forward one hour.";
        }

        if (zone.IsAmbiguousTime(local))
        {
            var offsets = zone.GetAmbiguousTimeOffsets(local);
            return new ClockReading(true, new DateTimeOffset(local, offsets[0]), "That local time happens twice because of daylight saving. The earlier offset was used.");
        }

        return new ClockReading(true, new DateTimeOffset(local, zone.GetUtcOffset(local)), note);
    }

    private static bool LooksAbsolute(string text) => text.Contains('T') || text.EndsWith('Z') || HasExplicitOffset(text);

    private static bool HasExplicitOffset(string text)
    {
        if (text.EndsWith('Z') || text.EndsWith('z'))
            return true;

        var sign = text.LastIndexOfAny(['+', '-']);
        return sign > 8 && text.Length - sign is 6 or 3;
    }
}
