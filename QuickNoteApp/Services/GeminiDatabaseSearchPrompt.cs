namespace QuickNoteApp.Services;

public static class GeminiDatabaseSearchPrompt
{
    public static string Build(string question, string databaseContext)
    {
        return $"""
Kullanıcı Gemini'ye şu soruyu sordu:
{question.Trim()}

Aşağıdaki yerel veritabanı kayıtlarını kullanarak cevap ver.
Eski kodlama uyumu: yerel veritabanı.
Kayıtlar notlardan ve kaydedilmiş Outlook/WhatsApp bildirimlerinden gelir.
Soru sayı, tarih, kişi veya uygulama içeriyorsa kayıtları buna göre bul ve sonucu net söyle.
Emin olmadığın eşleşmeleri ayrı belirt.
Cevabı kısa ve kullanıcının sorusuyla aynı dilde yaz (örneğin soru İngilizce ise İngilizce, Türkçe ise Türkçe vb. yaz). / Write the response shortly and in the same language as the user's question.

Yerel veritabanı kayıtları:
{databaseContext.Trim()}
""";
    }
}

