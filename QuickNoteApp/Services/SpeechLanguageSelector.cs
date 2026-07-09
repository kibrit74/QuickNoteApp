namespace QuickNoteApp.Services;

public static class SpeechLanguageSelector
{
    public static string? SelectTurkishLanguageTag(IEnumerable<string> languageTags)
    {
        return languageTags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .OrderByDescending(tag => tag.Equals("tr-TR", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(tag => tag.StartsWith("tr", StringComparison.OrdinalIgnoreCase));
    }

    public static string FormatLanguageTags(IEnumerable<string> languageTags)
    {
        var tags = languageTags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag)
            .ToList();

        return tags.Count == 0 ? "Yok" : string.Join(", ", tags);
    }
}
