using QuickNoteApp.Models;

namespace QuickNoteApp.Services;

public static class DailySummarySearch
{
    public static List<NoteItem> FilterNotes(IEnumerable<NoteItem> notes, string query)
    {
        var words = SplitQuery(query);
        if (words.Count == 0)
            return notes.ToList();

        return notes.Where(note => words.All(word => NoteMatches(note, word))).ToList();
    }

    public static List<NotificationLogItem> FilterNotifications(IEnumerable<NotificationLogItem> notifications, string query)
    {
        var words = SplitQuery(query);
        if (words.Count == 0)
            return notifications.ToList();

        return notifications.Where(notification => words.All(word => NotificationMatches(notification, word))).ToList();
    }

    private static List<string> SplitQuery(string query)
    {
        return query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(word => word.TrimStart('#'))
            .Where(word => !string.IsNullOrWhiteSpace(word))
            .ToList();
    }

    private static bool NoteMatches(NoteItem note, string query)
    {
        return Contains(note.Title, query)
            || Contains(note.Text, query)
            || Contains(note.Tags, query)
            || Contains(note.CreatedAt.ToString("dd.MM.yyyy HH:mm"), query)
            || Contains(note.CreatedAt.ToString("yyyy-MM-dd"), query);
    }

    private static bool NotificationMatches(NotificationLogItem notification, string query)
    {
        return Contains(notification.AppName, query)
            || Contains(notification.Title, query)
            || Contains(notification.Body, query)
            || Contains(notification.ReceivedAt.ToString("dd.MM.yyyy HH:mm"), query)
            || Contains(notification.ReceivedAt.ToString("yyyy-MM-dd"), query);
    }

    private static bool Contains(string? text, string query)
    {
        return !string.IsNullOrWhiteSpace(text)
            && text.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
