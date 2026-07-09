using QuickNoteApp.Models;

namespace QuickNoteApp.Services;

public static class ImportantCalendarEventPolicy
{
    private static readonly string[] CriticalKeywords =
    [
        "durusma",
        "duruşma",
        "mahkeme",
        "hearing",
        "icra",
        "deadline",
        "son teslim",
        "son gun",
        "son gün"
    ];

    private static readonly string[] ImportantKeywords =
    [
        "toplanti",
        "toplantı",
        "musteri",
        "müşteri",
        "randevu",
        "gorusme",
        "görüşme",
        "sunum"
    ];

    public static bool IsImportant(CalendarEvent calendarEvent)
    {
        if (calendarEvent == null)
            return false;

        var text = BuildText(calendarEvent);
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return ContainsAny(text, CriticalKeywords) || ContainsAny(text, ImportantKeywords);
    }

    public static DateTime? GetReminderTime(CalendarEvent calendarEvent)
    {
        if (!IsImportant(calendarEvent))
            return null;

        if (calendarEvent.IsAllDay)
            return calendarEvent.StartTime.Date.AddHours(9);

        var text = BuildText(calendarEvent);
        if (ContainsAny(text, CriticalKeywords))
            return calendarEvent.StartTime.AddHours(-2);

        return calendarEvent.StartTime.AddMinutes(-30);
    }

    private static string BuildText(CalendarEvent calendarEvent)
    {
        return string.Join(' ', new[]
        {
            calendarEvent.Summary,
            calendarEvent.Description,
            calendarEvent.Location
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static bool ContainsAny(string text, IEnumerable<string> keywords)
    {
        return keywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}
