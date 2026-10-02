using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickNoteApp.Services;

public static class NotificationFilter
{
    public static HashSet<string> EnabledAppNames { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "Outlook",
        "WhatsApp"
    };

    private static readonly NotificationSource[] SupportedSources =
    [
        new("Outlook", ["outlook", "outlookforwindows", "microsoft.office", "office.outlook", "olk", "hxoutlook"]),
        new("WhatsApp", ["whatsapp"]),
        new("Teams", ["teams", "microsoft teams", "msteams"]),
        new("Slack", ["slack"]),
        new("Telegram", ["telegram"]),
        new("Gmail", ["gmail", "googlemail"])
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

    public static void Initialize(DatabaseService db)
    {
        try
        {
            var enabledStr = db.GetMeta("EnabledNotificationApps");
            if (string.IsNullOrWhiteSpace(enabledStr))
            {
                EnabledAppNames.Clear();
                EnabledAppNames.Add("Outlook");
                EnabledAppNames.Add("WhatsApp");
                return;
            }

            var enabledNames = enabledStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            EnabledAppNames.Clear();
            foreach (var name in enabledNames)
            {
                EnabledAppNames.Add(name);
            }
        }
        catch
        {
            // Fallback to default in case of DB initialization issues on first startup
            EnabledAppNames.Clear();
            EnabledAppNames.Add("Outlook");
            EnabledAppNames.Add("WhatsApp");
        }
    }

    public static bool ShouldCapture(string appName, string title, string body)
    {
        if (string.IsNullOrWhiteSpace(appName))
            return false;

        if (!HasReadableContent(appName, title, body))
            return false;

        if (IsBlockedSystemNotification(appName, title, body))
            return false;

        if (IsPlaceholderNotification(title, body))
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

        return SupportedSources.Any(source =>
            string.Equals(source.Name, normalizedTitle, StringComparison.OrdinalIgnoreCase)
            || source.Keywords.Any(keyword => string.Equals(keyword, normalizedTitle, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsAllowedDirectApp(string appName)
    {
        return SupportedSources
            .Where(source => EnabledAppNames.Contains(source.Name))
            .Any(source => ContainsAny(appName, source.Keywords));
    }

    private static bool IsAllowedBrowserNotification(string appName, string title, string body)
    {
        if (!ContainsAny(appName, BrowserNames))
            return false;

        var text = $"{title} {body}";
        return SupportedSources
            .Where(source => EnabledAppNames.Contains(source.Name))
            .Any(source => ContainsAny(text, source.Keywords));
    }

    public static string GetCanonicalAppName(string appName)
    {
        if (string.IsNullOrWhiteSpace(appName))
            return "Bilinmeyen uygulama";

        foreach (var source in SupportedSources)
        {
            if (ContainsAny(appName, source.Keywords))
            {
                return source.Name;
            }
        }

        return appName;
    }

    private static bool ContainsAny(string text, IEnumerable<string> keywords)
    {
        return keywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static readonly string[] PlaceholderNotificationTexts =
    [
        "You may have new messages",
        "Yeni mesajlarınız olabilir",
        "Checking for new messages",
        "Yeni mesajlar kontrol ediliyor",
        "Yeni mesajlar için kontrol ediliyor"
    ];

    private static bool IsPlaceholderNotification(string title, string body)
    {
        var trimmedBody = body?.Trim();
        var trimmedTitle = title?.Trim();

        if (!string.IsNullOrWhiteSpace(trimmedBody) && PlaceholderNotificationTexts.Any(placeholder => 
            string.Equals(trimmedBody, placeholder, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(trimmedTitle) && PlaceholderNotificationTexts.Any(placeholder => 
            string.Equals(trimmedTitle, placeholder, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    private sealed record NotificationSource(string Name, string[] Keywords);
}
