namespace QuickNoteApp.Services;

public static class GeminiTranscriptionPrompt
{
    public const string ModelName = "gemini-2.5-flash";

    public static string Build()
    {
        return """
        Verilen WAV ses dosyasını read_file aracıyla oku ve konuşmayı Türkçe yazıya çevir.
        Sadece konuşulan metni döndür.
        Kodlama uyumu: Türkçe konuşulan metni.
        Türkçe karakterleri doğru kullan: ç, ğ, ı, İ, ö, ş, ü.
        Konuşma Türkçe ise Türkçe yaz; İngilizceye çevirme.
        Duyduğun kelimeleri tahmin ederek başka dile çevirme.
        Açıklama, başlık, madde işareti, markdown veya yorum ekleme.
        Ses yoksa hiçbir şey yazma.
        """;
    }
}

