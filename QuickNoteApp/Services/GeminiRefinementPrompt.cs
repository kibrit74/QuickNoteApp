namespace QuickNoteApp.Services;

public static class GeminiRefinementPrompt
{
    public const string ModelName = "gemini-2.5-flash";

    public static string Build(string rawText)
    {
        return $"""
        Aşağıdaki Türkçe sesli dikte (konuşmadan yazıya çevrilmiş) metnini oku.
        Bu metnin anlamını ve özellikle (varsa) hukuki terimlerini kesinlikle bozmadan:
        1. Türkçe imla, yazım ve harf hatalarını düzelt.
        2. Eksik noktalama işaretlerini (nokta, virgül, soru işareti vb.) anlam akışına uygun olarak koy.
        3. Metni mantıklı paragraflara böl.
        4. Sadece düzeltilmiş temiz Türkçe metni döndür. Açıklama, başlık, markdown biçimlendirmesi veya yorum ekleme.

        Dikte Metni:
        {rawText}
        """;
    }
}
