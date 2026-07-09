using System;
using System.Collections.Generic;
using System.Text;
using QuickNoteApp.Models;

namespace QuickNoteApp.Services;

public static class GeminiTaskListPrompt
{
    public static string Build(List<CalendarEvent> events, List<NoteItem> notes, List<NotificationLogItem> notifications, DateTime date)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"{date:dd.MM.yyyy} icin Takvim verilerine gore uygulanabilir bir gun plani hazirla.");
        builder.AppendLine("Cevabi kisa, saat sirali ve Turkce yaz. Takvimdeki sabit etkinlikleri degistirme; bosluklara odaklanma bloklari ve kucuk gorevler yerlestir.");
        builder.AppendLine();
        builder.AppendLine("Format kurallari:");
        builder.AppendLine("- Görevleri önem sırasına göre [YÜKSEK ÖNCELİK], [ORTA ÖNCELİK], [DÜÅÜK ÖNCELİK] başlıkları altında grupla.");
        builder.AppendLine("- Her görev maddesinin başına varsa ilgili saat aralığını (örn. [10:00 - 11:30]) yerleştir.");
        builder.AppendLine("- Görevin ne olduğunu net, profesyonel ve kısa bir cümleyle açıkla.");
        builder.AppendLine("- Yanıtında giriş cümlesi, açıklama, markdown kod blokları veya yorum ekleme, doğrudan görev gruplarını ve maddelerini listele.");
        builder.AppendLine("- Cevabın tamamını Türkçe yaz.");
        builder.AppendLine();

        builder.AppendLine($"=== ğŸ“… {date:dd.MM.yyyy} TAKVİM ETKİNLİKLERİ ===");
        if (events.Count == 0)
        {
            builder.AppendLine("(Takvimde etkinlik bulunmuyor)");
        }
        else
        {
            foreach (var ev in events)
            {
                builder.AppendLine(ev.FormatForReport());
            }
        }
        builder.AppendLine();

        builder.AppendLine($"=== ğŸ“ {date:dd.MM.yyyy} NOTLARI ===");
        if (notes.Count == 0)
        {
            builder.AppendLine("(Kaydedilmiş not bulunmuyor)");
        }
        else
        {
            foreach (var note in notes)
            {
                var titlePrefix = string.IsNullOrWhiteSpace(note.Title) ? "" : $"[{note.Title}] ";
                builder.AppendLine($"- [{note.CreatedAt:HH:mm}] {titlePrefix}{note.Text}");
            }
        }
        builder.AppendLine();

        builder.AppendLine($"=== ğŸ”” {date:dd.MM.yyyy} BİLDİRİMLERİ VE MESAJLAR ===");
        if (notifications.Count == 0)
        {
            builder.AppendLine("(Kaydedilmiş bildirim bulunmuyor)");
        }
        else
        {
            foreach (var notif in notifications)
            {
                builder.AppendLine($"- [{notif.ReceivedAt:HH:mm}] {notif.AppName} - {notif.Title}: {notif.Body}");
            }
        }

        return builder.ToString();
    }
}

