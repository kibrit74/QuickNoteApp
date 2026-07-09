using QuickNoteApp.Models;

namespace QuickNoteApp.Services;

public sealed class CalendarPollingService : IDisposable
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan TimerInterval = TimeSpan.FromMinutes(1);

    private readonly GoogleAuthService _googleAuth;
    private readonly CalendarPollingState _state;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private System.Threading.Timer? _timer;

    public CalendarPollingService(GoogleAuthService googleAuth)
        : this(googleAuth, CalendarPollingState.Create(DefaultInterval))
    {
    }

    internal CalendarPollingService(GoogleAuthService googleAuth, CalendarPollingState state)
    {
        _googleAuth = googleAuth;
        _state = state;
    }

    public IReadOnlyList<CalendarEvent> TodayEvents { get; private set; } = new List<CalendarEvent>();
    public DateTimeOffset? LastRefreshAt { get; private set; }
    public event Action? StatusChanged;

    private string _statusText = "Takvim henüz kontrol edilmedi.";
    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText != value)
            {
                _statusText = value;
                StatusChanged?.Invoke();
            }
        }
    }

    public void Start()
    {
        _timer ??= new System.Threading.Timer(async _ => await RefreshIfDueAsync(), null, TimeSpan.FromSeconds(5), TimerInterval);
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetTodayEventsAsync(bool forceRefresh = false)
    {
        if (forceRefresh || _state.ShouldRefresh(DateTimeOffset.Now))
            await RefreshAsync();

        return TodayEvents;
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsForDateAsync(DateTime date, bool forceRefresh = false)
    {
        if (date.Date == DateTime.Today)
        {
            return await GetTodayEventsAsync(forceRefresh);
        }

        try
        {
            var authState = _googleAuth.GetState();
            if (!authState.IsSignedIn || authState.IsExpired)
            {
                return new List<CalendarEvent>();
            }

            var events = await _googleAuth.GetCalendarEventsAsync(date);
            return events ?? new List<CalendarEvent>();
        }
        catch
        {
            return new List<CalendarEvent>();
        }
    }

    private async Task RefreshIfDueAsync()
    {
        if (_state.ShouldRefresh(DateTimeOffset.Now))
            await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (!await _refreshLock.WaitAsync(0))
            return;

        try
        {
            var authState = _googleAuth.GetState();
            if (!authState.IsSignedIn || authState.IsExpired)
            {
                StatusText = "Google Takvim için giriş gerekli.";
                _state.MarkRefreshed(DateTimeOffset.Now);
                return;
            }

            var events = await _googleAuth.GetTodayCalendarEventsAsync();
            TodayEvents = events ?? new List<CalendarEvent>();
            LastRefreshAt = DateTimeOffset.Now;
            StatusText = $"Takvim güncellendi: {TodayEvents.Count} etkinlik.";
            _state.MarkRefreshed(LastRefreshAt.Value);
        }
        catch (Exception ex)
        {
            StatusText = "Takvim kontrol edilemedi: " + ex.Message;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _refreshLock.Dispose();
    }
}
