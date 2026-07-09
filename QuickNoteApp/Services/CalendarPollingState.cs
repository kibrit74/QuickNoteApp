namespace QuickNoteApp.Services;

public sealed class CalendarPollingState
{
    private readonly TimeSpan _interval;
    private DateTimeOffset? _lastRefreshAt;

    private CalendarPollingState(TimeSpan interval)
    {
        _interval = interval;
    }

    public static CalendarPollingState Create(TimeSpan interval)
    {
        return new CalendarPollingState(interval <= TimeSpan.Zero ? TimeSpan.FromMinutes(15) : interval);
    }

    public bool ShouldRefresh(DateTimeOffset now)
    {
        return _lastRefreshAt == null || now - _lastRefreshAt.Value >= _interval;
    }

    public void MarkRefreshed(DateTimeOffset now)
    {
        _lastRefreshAt = now;
    }
}
