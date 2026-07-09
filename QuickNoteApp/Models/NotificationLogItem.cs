namespace QuickNoteApp.Models;

public class NotificationLogItem
{
    public int Id { get; set; }

    /// <summary>Bildirimi gönderen uygulama (ör. "WhatsApp", "Outlook").</summary>
    public string AppName { get; set; } = string.Empty;

    /// <summary>Bildirimin başlığı - genelde gönderen kişinin adı (ör. "Ahmet Yılmaz").</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Bildirim gövde metni (mesaj/mail özeti).</summary>
    public string Body { get; set; } = string.Empty;

    public DateTime ReceivedAt { get; set; }
}
