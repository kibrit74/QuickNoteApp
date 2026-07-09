using QuickNoteApp.Models;

namespace QuickNoteApp.Services;

public sealed class CalendarReminderSyncService : IDisposable
{
    private static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan TimerInterval = TimeSpan.FromMinutes(1);
    private const int SyncDayCount = 4;

    private readonly GoogleAuthService _googleAuth;
    private readonly DatabaseService _db;
    private readonly CalendarPollingState _state;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private System.Threading.Timer? _timer;

    public CalendarReminderSyncService(GoogleAuthService googleAuth, DatabaseService db)
        : this(googleAuth, db, CalendarPollingState.Create(SyncInterval))
    {
    }

    internal CalendarReminderSyncService(GoogleAuthService googleAuth, DatabaseService db, CalendarPollingState state)
    {
        _googleAuth = googleAuth;
        _db = db;
        _state = state;
    }

    public string StatusText { get; private set; } = "Takvim hatirlatma senkronu hazir.";

    public void Start()
    {
        _timer ??= new System.Threading.Timer(async _ => await SyncIfDueAsync(), null, TimeSpan.FromSeconds(10), TimerInterval);
    }

    public async Task SyncNowAsync()
    {
        await SyncAsync();
    }

    private async Task SyncIfDueAsync()
    {
        if (_state.ShouldRefresh(DateTimeOffset.Now))
            await SyncAsync();
    }

    private async Task SyncAsync()
    {
        if (!await _syncLock.WaitAsync(0))
            return;

        try
        {
            var authState = _googleAuth.GetState();
            if (!authState.IsSignedIn || authState.IsExpired)
            {
                StatusText = "Google Takvim bagli degil.";
                return;
            }

            var upcomingEvents = new List<CalendarEvent>();
            for (var dayOffset = 0; dayOffset < SyncDayCount; dayOffset++)
            {
                var date = DateTime.Today.AddDays(dayOffset);
                var events = await _googleAuth.GetCalendarEventsAsync(date);
                if (events != null)
                    upcomingEvents.AddRange(events);
            }

            var syncedCount = 0;
            foreach (var calendarEvent in upcomingEvents.Where(ImportantCalendarEventPolicy.IsImportant))
            {
                if (calendarEvent.EndTime <= DateTime.Now)
                    continue;

                var reminderAt = ImportantCalendarEventPolicy.GetReminderTime(calendarEvent);
                if (!reminderAt.HasValue)
                    continue;

                _db.UpsertCalendarReminder(calendarEvent, reminderAt.Value);
                syncedCount++;
            }

            _state.MarkRefreshed(DateTimeOffset.Now);
            StatusText = syncedCount > 0
                ? $"Takvim hatirlatmalari guncellendi: {syncedCount} onemli etkinlik."
                : "Takvimde yeni onemli etkinlik bulunmadi.";
        }
        catch (Exception ex)
        {
            StatusText = "Takvim hatirlatmalari guncellenemedi: " + ex.Message;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _syncLock.Dispose();
    }
}
