namespace QuickNoteApp.Services;

public static class GeminiSearchIntent
{
    private static readonly string[] DatabaseKeywords =
    [
        "ara",
        "bul",
        "kaç",
        "kac",
        "kim",
        "ne zaman",
        "hangi",
        "toplam",
        "tutar",
        "mail",
        "mesaj",
        "bildirim",
        "whatsapp",
        "outlook",
        "notlarımda",
        "notlarimda",
        "notlarda",
        "veritabanı",
        "veritabani"
    ];

    private static readonly string[] ExplicitDatabaseKeywords =
    [
        "notlarımda",
        "notlarimda",
        "notlarda",
        "veritabanı",
        "veritabani",
        "bildirim",
        "whatsapp",
        "outlook",
        "mail",
        "mesaj"
    ];

    private static readonly string[] CalendarKeywords =
    [
        "takvim",
        "etkinlik",
        "program",
        "ajanda",
        "calendar",
        "toplantı",
        "toplanti",
        "randevu",
        "günümü planla",
        "gunumu planla",
        "bugünümü planla",
        "bugunumu planla",
        "yarınımı planla",
        "yarinimi planla",
        "görev listesi",
        "gorev listesi",
        "görevler",
        "gorevler",
        "bugünü özetle",
        "bugunu ozetle",
        "günlük plan",
        "gunluk plan",
        "planı",
        "plani"
    ];

    public static bool ShouldUseDatabase(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return false;

        var lower = prompt.ToLowerInvariant();
        if (ExplicitDatabaseKeywords.Any(keyword => prompt.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (lower.Contains("düz") || lower.Contains("duz") || lower.Contains("yaz") || lower.Contains("oluşt") || lower.Contains("olust") ||
            lower.Contains("hazır") || lower.Contains("hazir") || lower.Contains("özet") || lower.Contains("ozet") ||
            lower.Contains("çevir") || lower.Contains("cevir"))
        {
            return false;
        }

        return DatabaseKeywords.Any(keyword => prompt.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    public static bool ShouldUseCalendar(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return false;

        var lower = prompt.ToLowerInvariant();
        if (lower.Contains("çevir") || lower.Contains("cevir"))
            return false;

        return CalendarKeywords.Any(keyword => prompt.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}



