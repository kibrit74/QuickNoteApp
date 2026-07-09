namespace QuickNoteApp.Services;

public static class NotificationFilter
{
    private static readonly NotificationSource[] AllowedSources =
    [
        new("Outlook", ["outlook", "outlookforwindows", "microsoft.office", "office.outlook", "olk", "hxoutlook"]),
        new("WhatsApp", ["whatsapp"])
    ];

    private static readonly string[] BrowserNames =
    [
        "chrome",
        "edge",
        "firefox",
        "opera",
        "brave",
        "browser"
    ];

    private static readonly string[] BlockedSystemNotificationWords =
    [
        "Windows Guvenligi",
        "Windows Güvenliği",
        "Windows Security",
        "Windows Defender",
        "Microsoft Defender",
        "Guvenlik Duvari",
        "Güvenlik Duvarı",
        "SecHealthUI",
        "SecurityHealth",
        "Tehditler bulundu",
        "Virusten Koruma",
        "Virüsten Koruma",
        "Windows Update",
        "Microsoft-Windows",
        "USB",
        "Bluetooth",
        "Settings",
        "Ayarlar",
        "Pil",
        "Battery",
        "Power",
        "Güç"
    ];

    public static bool ShouldCapture(string appName, string title, string body)
    {
        if (string.IsNullOrWhiteSpace(appName))
            return false;

        if (!HasReadableContent(appName, title, body))
            return false;

        if (IsBlockedSystemNotification(appName, title, body))
            return false;

        return IsAllowedDirectApp(appName)
            || IsAllowedBrowserNotification(appName, title, body);
    }

    public static bool IsBlockedSystemNotification(string appName, string title, string body)
    {
        return ContainsAny($"{appName} {title} {body}", BlockedSystemNotificationWords);
    }

    private static bool HasReadableContent(string appName, string title, string body)
    {
        if (!string.IsNullOrWhiteSpace(body))
            return true;

        if (string.IsNullOrWhiteSpace(title))
            return false;

        return !IsOnlySourceLabel(appName, title);
    }

    private static bool IsOnlySourceLabel(string appName, string title)
    {
        var normalizedTitle = title.Trim();

        if (string.Equals(appName.Trim(), normalizedTitle, StringComparison.OrdinalIgnoreCase))
            return true;

        return AllowedSources.Any(source =>
            string.Equals(source.Name, normalizedTitle, StringComparison.OrdinalIgnoreCase)
            || source.Keywords.Any(keyword => string.Equals(keyword, normalizedTitle, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsAllowedDirectApp(string appName)
    {
        return AllowedSources.Any(source => ContainsAny(appName, source.Keywords));
    }

    private static bool IsAllowedBrowserNotification(string appName, string title, string body)
    {
        if (!ContainsAny(appName, BrowserNames))
            return false;

        var text = $"{title} {body}";
        return AllowedSources.Any(source => ContainsAny(text, source.Keywords));
    }

    private static bool ContainsAny(string text, IEnumerable<string> keywords)
    {
        return keywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record NotificationSource(string Name, string[] Keywords);
}


