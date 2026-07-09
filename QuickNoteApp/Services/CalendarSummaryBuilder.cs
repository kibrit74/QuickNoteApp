using System.Text;
using QuickNoteApp.Models;

namespace QuickNoteApp.Services;

public static class CalendarSummaryBuilder
{
    public static string Build(
        DateTime selectedDate,
        IEnumerable<CalendarEvent>? selectedEvents,
        IEnumerable<CalendarEvent>? upcomingImportantEvents)
    {
        var builder = new StringBuilder();
        var dayEvents = (selectedEvents ?? Enumerable.Empty<CalendarEvent>())
            .OrderBy(calendarEvent => calendarEvent.StartTime)
            .ToList();
        var importantEvents = (upcomingImportantEvents ?? Enumerable.Empty<CalendarEvent>())
            .Where(calendarEvent => calendarEvent.StartTime.Date >= selectedDate.Date)
            .OrderBy(calendarEvent => calendarEvent.StartTime)
            .ToList();

        builder.AppendLine($"Takvim ozeti - {selectedDate:dd.MM.yyyy}");
        builder.AppendLine();
        builder.AppendLine($"{selectedDate:dd.MM.yyyy} tarihindeki etkinlikler:");

        if (dayEvents.Count == 0)
        {
            builder.AppendLine("- Bu gun icin etkinlik bulunmuyor.");
        }
        else
        {
            foreach (var calendarEvent in dayEvents)
                builder.AppendLine(calendarEvent.FormatForReport());
        }

        if (importantEvents.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Yaklasan onemli etkinlikler:");

            foreach (var calendarEvent in importantEvents)
            {
                var prefix = calendarEvent.IsAllDay
                    ? calendarEvent.StartTime.ToString("dd.MM.yyyy")
                    : calendarEvent.StartTime.ToString("dd.MM.yyyy HH:mm");
                builder.AppendLine($"- [{prefix}] {calendarEvent.Summary}");
            }
        }

        return builder.ToString().Trim();
    }
}
