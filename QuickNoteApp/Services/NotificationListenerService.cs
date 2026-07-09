using System.Runtime.InteropServices;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace QuickNoteApp.Services;

/// <summary>
/// Windows toast bildirimlerini okur. Paket kimligi olmayan normal exe calisamiyorsa,
/// Outlook masaustu uygulamasindan yedek akisa gecer.
/// </summary>
public class NotificationListenerService : IDisposable
{
    private readonly DatabaseService _db;
    private readonly OutlookDesktopNotificationBridge _outlookFallback;
    private readonly HashSet<uint> _seenNotificationIds = new();
    private readonly object _sync = new();
    private UserNotificationListener? _listener;
    private System.Threading.Timer? _pollTimer;
    private bool _isActive;
    private volatile bool _isPolling;

    private string _statusText = "Bildirim servisi baslatilmadi.";
    public string StatusText
    {
        get
        {
            if (_outlookFallback != null && _outlookFallback.IsActive)
                return _outlookFallback.StatusText;
            return _statusText;
        }
        private set
        {
            if (_statusText != value)
            {
                _statusText = value;
                StatusChanged?.Invoke();
            }
        }
    }

    public event Action? StatusChanged;

    public NotificationListenerService(DatabaseService db)
    {
        _db = db;
        _outlookFallback = new OutlookDesktopNotificationBridge(db);
        _outlookFallback.StatusChanged += () => StatusChanged?.Invoke();
    }

    public async Task EnsureAccessAndStartAsync()
    {
        await StartAsync();
    }

    public async Task StartAsync()
    {
        if (_isActive)
            return;
        try
        {
            _listener = UserNotificationListener.Current;
            StatusText = "Windows bildirim izni isteniyor...";
            var accessStatus = await _listener.RequestAccessAsync();

            if (accessStatus != UserNotificationListenerAccessStatus.Allowed)
            {
                _isActive = false;
                if (TryStartOutlookFallback())
                    return;

                StatusText = "Windows bildirim erişimi kapalı. Ayarlar bölümünden tekrar izin isteyebilirsiniz.";
                return;
            }

            _listener.NotificationChanged += OnNotificationChanged;
            _isActive = true;
            StatusText = "Bildirim dinleme aktif.";

            await CaptureCurrentNotificationsAsync();
            _pollTimer = new System.Threading.Timer(async _ => await PollNotificationsAsync(), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30));
        }
        catch (COMException)
        {
            _isActive = false;
            if (TryStartOutlookFallback())
                return;

            StatusText = IsRunningAsPackaged()
                ? "Windows toast bildirimi okunamadı. Sistem ayarlarından bildirim erişim iznini açın."
                : "Windows toast bildirimi bu sürümde okunamadı. MSIX paketli sürüm gerekiyor.";
        }
        catch (UnauthorizedAccessException)
        {
            _isActive = false;
            if (TryStartOutlookFallback())
                return;

            StatusText = IsRunningAsPackaged()
                ? "Windows bildirim erişim izni yok. Sistem ayarlarından bildirim iznini verin."
                : "Windows bildirim erişimi yok. MSIX paketli sürüm ve bildirim izni gerekiyor.";
        }
        catch (Exception ex)
        {
            _isActive = false;
            StatusText = $"Bildirim servisi başlatılamadı: {ex.GetType().Name}.";
        }
    }

    private bool TryStartOutlookFallback()
    {
        if (!_outlookFallback.Start())
            return false;

        StatusText = "Windows toast dinleyicisi kullanilamadi; Outlook masaustu yedegi aktif.";
        return true;
    }

    private async void OnNotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
    {
        if (args.ChangeKind != UserNotificationChangedKind.Added)
            return;

        await CaptureNotificationByIdAsync(sender, args.UserNotificationId);
    }

    private async Task PollNotificationsAsync()
    {
        if (_isPolling || !_isActive || _listener == null)
            return;

        _isPolling = true;
        try
        {
            await CaptureCurrentNotificationsAsync();
        }
        finally
        {
            _isPolling = false;
        }
    }

    private async Task CaptureCurrentNotificationsAsync()
    {
        if (_listener == null)
            return;

        try
        {
            var notifications = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            var savedCount = 0;

            foreach (var notification in notifications)
            {
                if (SaveNotificationIfNew(notification))
                    savedCount++;
            }

            StatusText = savedCount > 0
                ? $"Bildirimler alindi: {savedCount} yeni bildirim."
                : "Bildirim dinleme aktif.";
        }
        catch (COMException)
        {
            _isActive = false;
            if (!TryStartOutlookFallback())
            {
                StatusText = IsRunningAsPackaged()
                    ? "Windows bildirimleri okunamadı. Sistem ayarlarından bildirim erişim iznini açın."
                    : "Windows bildirimleri okunamadı. MSIX paketli sürüm gerekiyor.";
            }
        }
        catch (UnauthorizedAccessException)
        {
            _isActive = false;
            if (!TryStartOutlookFallback())
            {
                StatusText = IsRunningAsPackaged()
                    ? "Windows bildirimleri okunamadı. Sistem ayarlarından bildirim iznini verin."
                    : "Windows bildirimleri okunamadı. MSIX paketli sürüm ve bildirim izni gerekiyor.";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Bildirim okunamadı: {ex.GetType().Name}.";
        }
    }

    private Task CaptureNotificationByIdAsync(UserNotificationListener listener, uint notificationId)
    {
        try
        {
            var notification = listener.GetNotification(notificationId);
            if (notification != null && SaveNotificationIfNew(notification))
                StatusText = "Yeni bildirim kaydedildi.";
        }
        catch (COMException)
        {
            _isActive = false;
            if (!TryStartOutlookFallback())
            {
                StatusText = IsRunningAsPackaged()
                    ? "Windows tekil bildirimi veremedi. Sistem ayarlarından bildirim erişim iznini açın."
                    : "Windows tekil bildirimi veremedi. MSIX paketli sürüm gerekiyor.";
            }
        }
        catch (UnauthorizedAccessException)
        {
            _isActive = false;
            if (!TryStartOutlookFallback())
            {
                StatusText = IsRunningAsPackaged()
                    ? "Windows tekil bildirimi veremedi. Sistem ayarlarından bildirim iznini verin."
                    : "Windows tekil bildirimi veremedi. MSIX paketli sürüm ve bildirim izni gerekiyor.";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Tek bildirim okunamadı: {ex.GetType().Name}.";
        }

        return Task.CompletedTask;
    }

    private bool SaveNotificationIfNew(UserNotification notification)
    {
        try
        {
            var appName = GetSafeAppName(notification);
            var (title, body) = GetSafeNotificationText(notification);

            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body))
            {
                MarkAsSeen(notification.Id);
                return false;
            }

            if (NotificationFilter.IsBlockedSystemNotification(appName, title, body))
            {
                MarkAsSeen(notification.Id);
                StatusText = "Windows sistem bildirimi atlandi.";
                return false;
            }

            if (!NotificationFilter.ShouldCapture(appName, title, body))
            {
                MarkAsSeen(notification.Id);
                return false;
            }

            lock (_sync)
            {
                if (_seenNotificationIds.Count > 1000)
                    _seenNotificationIds.Clear();

                if (!_seenNotificationIds.Add(notification.Id))
                    return false;

                _db.AddNotification(appName, title, body);
            }

            return true;
        }
        catch (Exception ex)
        {
            StatusText = $"Bildirim kaydedilemedi: {ex.GetType().Name}.";
            return false;
        }
    }

    private void MarkAsSeen(uint notificationId)
    {
        lock (_sync)
        {
            if (_seenNotificationIds.Count > 1000)
                _seenNotificationIds.Clear();

            _seenNotificationIds.Add(notificationId);
        }
    }

    private static string GetSafeAppName(UserNotification notification)
    {
        try
        {
            var displayName = notification.AppInfo?.DisplayInfo?.DisplayName;
            if (!string.IsNullOrWhiteSpace(displayName))
                return displayName;
        }
        catch (NotImplementedException)
        {
        }
        catch (COMException)
        {
        }

        try
        {
            var packageName = notification.AppInfo?.PackageFamilyName;
            if (!string.IsNullOrWhiteSpace(packageName))
                return packageName;
        }
        catch (NotImplementedException)
        {
        }
        catch (COMException)
        {
        }

        return "Bilinmeyen uygulama";
    }

    private static (string title, string body) GetSafeNotificationText(UserNotification notification)
    {
        try
        {
            var binding = notification.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
            var textElements = binding?.GetTextElements();

            if (textElements == null || textElements.Count == 0)
                return (string.Empty, string.Empty);

            var title = SafeText(textElements[0].Text);
            var bodyParts = textElements
                .Skip(1)
                .Select(element => SafeText(element.Text))
                .Where(text => !string.IsNullOrWhiteSpace(text));

            return (title, string.Join(" ", bodyParts));
        }
        catch
        {
            return (string.Empty, string.Empty);
        }
    }

    private static string SafeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    public static bool IsRunningAsPackaged()
    {
        try
        {
            var desktopApp = global::Windows.ApplicationModel.Package.Current;
            return desktopApp != null && desktopApp.Id != null;
        }
        catch (InvalidOperationException) // E_NO_SIGNATURE
        {
            return false;
        }
    }

    public void Dispose()
    {
        _pollTimer?.Dispose();
        _outlookFallback.Dispose();

        if (_isActive && _listener != null)
            _listener.NotificationChanged -= OnNotificationChanged;
    }
}
