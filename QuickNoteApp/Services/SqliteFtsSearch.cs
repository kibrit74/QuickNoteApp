using System.Text;
using System.Text.RegularExpressions;

namespace QuickNoteApp.Services;

public static class SqliteFtsSearch
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "acaba", "ara", "atmis", "atmış", "bugun", "bugün", "icin", "için", "kac", "kaç",
        "maili", "mails", "mesaj", "mi", "mı", "mu", "mü", "ne", "nedir", "olan",
        "olarak", "var", "ve", "ya", "yazmis", "yazmış"
    };

    public static string BuildMatchQuery(string query)
    {
        var tokens = ExtractTokens(query);
        return tokens.Count == 0 ? string.Empty : string.Join(" AND ", tokens.Select(ToPrefixToken));
    }

    public static string BuildAnyMatchQuery(string query)
    {
        var tokens = ExtractTokens(query);
        return tokens.Count == 0 ? string.Empty : string.Join(" OR ", tokens.Select(ToPrefixToken));
    }

    private static List<string> ExtractTokens(string query)
    {
        var tokens = Regex.Matches(Normalize(query), @"[\p{L}\p{N}]{2,}")
            .Select(match => CleanToken(match.Value))
            .Where(token => token.Length >= 2 && !StopWords.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        return tokens;
    }

    public static string BuildSearchText(params string?[] values)
    {
        return Normalize(string.Join(" ", values.Where(value => !string.IsNullOrWhiteSpace(value))));
    }

    private static string Normalize(string? value)
    {
        var text = RepairMojibake(value ?? string.Empty);
        return text
            .Trim()
            .ToLowerInvariant()
            .Replace("ã–", "o")
            .Replace("ã¶", "o")
            .Replace("ã¼", "u")
            .Replace("ã§", "c")
            .Replace("äŸ", "g")
            .Replace("äÿ", "g")
            .Replace("ä±", "i")
            .Replace("åŸ", "s")
            .Replace("åÿ", "s")
            .Replace('\u0131', 'i')
            .Replace('\u011f', 'g')
            .Replace('\u00fc', 'u')
            .Replace('\u015f', 's')
            .Replace('\u00f6', 'o')
            .Replace('\u00e7', 'c')
            .Replace('\u0130', 'i')
            .Replace('\u011e', 'g')
            .Replace('\u00dc', 'u')
            .Replace('\u015e', 's')
            .Replace('\u00d6', 'o')
            .Replace('\u00c7', 'c');
    }

    private static string RepairMojibake(string value)
    {
        if (!value.Contains('\u00c3') && !value.Contains('\u00c4') && !value.Contains('\u00c5'))
            return value;

        try
        {
            return Encoding.UTF8.GetString(Encoding.GetEncoding(1252).GetBytes(value));
        }
        catch
        {
            return value;
        }
    }

    private static string CleanToken(string token)
    {
        var cleaned = token.Trim();
        foreach (var suffix in new[] { "larda", "lerde", "ndan", "nden", "dan", "den", "tan", "ten", "da", "de", "ta", "te" })
        {
            if (cleaned.Length > suffix.Length + 3 && cleaned.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return cleaned[..^suffix.Length];
        }

        return cleaned;
    }

    private static string ToPrefixToken(string token)
    {
        return "\"" + token.Replace("\"", "\"\"") + "\"*";
    }
}



