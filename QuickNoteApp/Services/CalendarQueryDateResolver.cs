using System.Globalization;
using System.Text.RegularExpressions;

namespace QuickNoteApp.Services;

public static class CalendarQueryDateResolver
{
    private static readonly Dictionary<string, int> MonthNumbers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ocak"] = 1,
        ["subat"] = 2,
        ["şubat"] = 2,
        ["mart"] = 3,
        ["nisan"] = 4,
        ["mayis"] = 5,
        ["mayıs"] = 5,
        ["haziran"] = 6,
        ["temmuz"] = 7,
        ["agustos"] = 8,
        ["ağustos"] = 8,
        ["eylul"] = 9,
        ["eylül"] = 9,
        ["ekim"] = 10,
        ["kasim"] = 11,
        ["kasım"] = 11,
        ["aralik"] = 12,
        ["aralık"] = 12
    };

    public static DateTime Resolve(string question, DateTime fallbackDate)
    {
        if (string.IsNullOrWhiteSpace(question))
            return fallbackDate.Date;

        var lower = question.ToLowerInvariant();
        if (lower.Contains("yarin") || lower.Contains("yarın"))
            return DateTime.Today.AddDays(1);

        if (lower.Contains("bugun") || lower.Contains("bugün"))
            return DateTime.Today;

        if (lower.Contains("dun") || lower.Contains("dün"))
            return DateTime.Today.AddDays(-1);

        var exactDateMatch = Regex.Match(question, @"\b(?<day>\d{1,2})[./-](?<month>\d{1,2})[./-](?<year>\d{4})\b");
        if (exactDateMatch.Success)
        {
            var candidate = $"{exactDateMatch.Groups["day"].Value}.{exactDateMatch.Groups["month"].Value}.{exactDateMatch.Groups["year"].Value}";
            if (DateTime.TryParseExact(candidate, "d.M.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var exactDate))
                return exactDate.Date;
        }

        var verbalDateMatch = Regex.Match(lower, @"\b(?<day>\d{1,2})\s+(?<month>ocak|subat|şubat|mart|nisan|mayis|mayıs|haziran|temmuz|agustos|ağustos|eylul|eylül|ekim|kasim|kasım|aralik|aralık)\b");
        if (verbalDateMatch.Success)
        {
            var day = int.Parse(verbalDateMatch.Groups["day"].Value, CultureInfo.InvariantCulture);
            var monthName = verbalDateMatch.Groups["month"].Value;
            if (MonthNumbers.TryGetValue(monthName, out var month))
            {
                return new DateTime(DateTime.Today.Year, month, day);
            }
        }

        return fallbackDate.Date;
    }
}
