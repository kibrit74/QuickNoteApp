namespace QuickNoteApp.Services;

public sealed record GoogleCalendarPushSetupPlan(bool CanEnablePush, string? CallbackUrl, string Message)
{
    public static GoogleCalendarPushSetupPlan Create(string? callbackUrl)
    {
        if (string.IsNullOrWhiteSpace(callbackUrl))
        {
            return new GoogleCalendarPushSetupPlan(
                false,
                null,
                "Google Calendar Push için public HTTPS webhook gerekir. Masaüstü uygulama tek başına bu bildirimi alamaz.");
        }

        if (!Uri.TryCreate(callbackUrl.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            return new GoogleCalendarPushSetupPlan(
                false,
                callbackUrl.Trim(),
                "Google Calendar Push için callback adresi public HTTPS webhook olmalıdır.");
        }

        return new GoogleCalendarPushSetupPlan(
            true,
            uri.ToString(),
            "Google Calendar Push kurulabilir. Takvim değişince Google bu webhook adresine bildirim gönderir.");
    }
}
