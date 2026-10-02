namespace QuickNoteApp.Services;

public static class GeminiCalendarSearchPrompt
{
    public static string Build(string question, string calendarContext, string databaseContext)
    {
        return $"""
Kullanıcı Gemini'ye şu soruyu sordu:
{question.Trim()}

Cevap vermek için aşağıdaki Google Takvim verilerini ve yerel veritabanı kayıtlarını kullan.

Google Takvim Verileri:
{calendarContext.Trim()}

Yerel Veritabanı Kayıtları (Notlar ve Bildirimler):
{databaseContext.Trim()}

Talimatlar:
1. Kullanıcı takvim, etkinlik, toplantı veya plan soruyorsa ÖNCELİKLİ olarak "Google Takvim Verileri" kısmını kontrol et.
2. Eğer Google Takvim'de o tarihte hiçbir etkinlik bulunmuyorsa, cevabında açıkça "Google Takviminizde bu tarihte herhangi bir etkinlik bulunmuyor." ifadesini belirt.
3. Eğer yerel veritabanı kayıtlarında (notlar veya WhatsApp/Outlook bildirimlerinde) takvimle ilişkili olabilecek (örn. toplantı saati veya randevu bilgisi içeren) kayıtlar varsa, bunları "Ayrıca yerel notlarda/bildirimlerde şu kayıtlar bulundu:" diyerek ikincil bilgi olarak sun.
4. Cevabı net, kısa ve kullanıcının soruyu sorduğu dilde yaz (soru İngilizce ise İngilizce, Türkçe ise Türkçe vb. yaz). / Write the response clearly, shortly, and in the same language as the user's question.
""";
    }
}
