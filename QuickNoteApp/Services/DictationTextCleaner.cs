namespace QuickNoteApp.Services;

public static class DictationTextCleaner
{
    private static readonly string[] PrefixesToRemove =
    [
        "Deşifre şudur:",
        "Deşifre şudur:",
        "Deşifre:",
        "Transkripsiyon:",
        "Yazıya döküm:",
        "Metin:",
        "Konuşulan metin:",
        "İşte transkripsiyon:",
        "Ses dosyasındaki konuşma:"
    ];

    public static string Clean(string text)
    {
        var cleaned = text.Trim();
        cleaned = RemoveGeminiCliThought(cleaned);
        cleaned = ExtractQuotedTranscription(cleaned);

        foreach (var prefix in PrefixesToRemove)
        {
            if (cleaned.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return cleaned[prefix.Length..].Trim();
        }

        return cleaned;
    }

    private static string ExtractQuotedTranscription(string text)
    {
        var firstQuote = text.IndexOf('"');
        if (firstQuote < 0)
            return text;

        var secondQuote = text.IndexOf('"', firstQuote + 1);
        if (secondQuote <= firstQuote)
            return text;

        var quoted = text[(firstQuote + 1)..secondQuote].Trim();
        return string.IsNullOrWhiteSpace(quoted) ? text : quoted;
    }

    private static string RemoveGeminiCliThought(string text)
    {
        var cleaned = text.Trim();
        if (!cleaned.StartsWith("thought", StringComparison.OrdinalIgnoreCase))
            return cleaned;

        var markers = new[]
        {
            "I will now provide the transcription based on the audio content.",
            "I will now provide the transcription.",
            "Here is the transcription:",
            "Transcription:"
        };

        foreach (var marker in markers)
        {
            var markerIndex = cleaned.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex >= 0)
                return cleaned[(markerIndex + marker.Length)..].Trim();
        }

        var lines = cleaned
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Select(line => line.Trim())
            .Where(line => !IsGeminiExplanationLine(line))
            .ToList();

        return string.Join(Environment.NewLine, lines).Trim();
    }

    private static bool IsGeminiExplanationLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        return line.Equals("thought", StringComparison.OrdinalIgnoreCase)
            || line.Contains("read_file", StringComparison.OrdinalIgnoreCase)
            || line.Contains("binary content", StringComparison.OrdinalIgnoreCase)
            || line.Contains("audio content", StringComparison.OrdinalIgnoreCase)
            || line.Contains("transcribe the audio", StringComparison.OrdinalIgnoreCase)
            || line.Contains("processed the WAV file", StringComparison.OrdinalIgnoreCase);
    }
}

