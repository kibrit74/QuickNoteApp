using System.Text;
using QuickNoteApp.Models;

namespace QuickNoteApp.Services;

public static class SearchContextFormatter
{
    public static string Format(IEnumerable<NoteItem> notes, IEnumerable<NotificationLogItem> notifications)
    {
        var builder = new StringBuilder();

        builder.AppendLine("Notlar:");
        foreach (var note in notes)
        {
            builder.AppendLine($"- [{note.Id}] {note.CreatedAt:yyyy-MM-dd HH:mm} | {note.Title}");
            if (!string.IsNullOrWhiteSpace(note.Tags))
                builder.AppendLine($"  Etiketler: {note.Tags}");
            builder.AppendLine($"  Icerik: {SingleLine(note.Text)}");
        }

        builder.AppendLine();
        builder.AppendLine("Bildirimler:");
        foreach (var notification in notifications)
        {
            builder.AppendLine($"- [{notification.Id}] {notification.ReceivedAt:yyyy-MM-dd HH:mm} | {notification.AppName} | {notification.Title}");
            builder.AppendLine($"  Icerik: {SingleLine(notification.Body)}");
        }

        return builder.ToString().Trim();
    }

    private static string SingleLine(string value)
    {
        return value
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Trim();
    }
}
