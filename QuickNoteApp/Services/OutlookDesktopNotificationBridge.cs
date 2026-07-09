using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace QuickNoteApp.Services;

public sealed class OutlookDesktopNotificationBridge : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(45);
    private readonly DatabaseService _db;
    private readonly SemaphoreSlim _pollLock = new(1, 1);
    private System.Threading.Timer? _timer;

    public OutlookDesktopNotificationBridge(DatabaseService db)
    {
        _db = db;
    }

    public bool IsActive { get; private set; }

    private string _statusText = "Outlook masaustu yedegi hazir degil.";
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

    public event Action? StatusChanged;

    public bool Start()
    {
        if (_timer != null)
            return IsActive;

        if (Type.GetTypeFromProgID("Outlook.Application") == null)
        {
            StatusText = "Outlook masaustu uygulamasi bulunamadi.";
            return false;
        }

        IsActive = true;
        _timer = new System.Threading.Timer(async _ => await PollAsync(), null, TimeSpan.Zero, PollInterval);
        StatusText = "Outlook masaustu yedek dinleme aktif.";
        return true;
    }

    private async Task PollAsync()
    {
        if (!IsActive || !await _pollLock.WaitAsync(0))
            return;

        try
        {
            var importedCount = ImportRecentInboxItems();
            StatusText = importedCount > 0
                ? $"Outlook masaustu yedegi aktif: {importedCount} yeni posta eslesti."
                : "Outlook masaustu yedegi aktif.";
        }
        catch (Exception ex)
        {
            StatusText = "Outlook masaustu okunamadi: " + ex.Message;
        }
        finally
        {
            _pollLock.Release();
        }
    }

    private int ImportRecentInboxItems()
    {
        var outlookType = Type.GetTypeFromProgID("Outlook.Application");
        if (outlookType == null)
        {
            IsActive = false;
            return 0;
        }

        object? outlook = null;
        object? session = null;
        object? inbox = null;
        object? items = null;

        try
        {
            outlook = Activator.CreateInstance(outlookType)
                ?? throw new InvalidOperationException("Outlook baslatilamadi.");

            dynamic outlookApp = outlook;
            session = outlookApp.GetNamespace("MAPI");
            dynamic mapi = session;

            try
            {
                mapi.Logon(Type.Missing, Type.Missing, false, false);
            }
            catch
            {
            }

            inbox = mapi.GetDefaultFolder(6);
            dynamic inboxFolder = inbox;
            items = inboxFolder.Items;
            dynamic inboxItems = items;
            inboxItems.Sort("[ReceivedTime]", true);

            var importedCount = 0;
            var totalCount = Convert.ToInt32(inboxItems.Count);
            var limit = Math.Min(totalCount, 20);

            for (var index = 1; index <= limit; index++)
            {
                object? currentItem = null;
                try
                {
                    currentItem = inboxItems[index];
                    if (!TryReadMailSnapshot(currentItem, out var snapshot))
                        continue;

                    if (snapshot.ReceivedAt < DateTime.Now.AddDays(-1))
                        continue;

                    _db.AddNotification("Outlook", snapshot.Title, snapshot.Body, snapshot.ReceivedAt);
                    importedCount++;
                }
                finally
                {
                    ReleaseComObject(currentItem);
                }
            }

            return importedCount;
        }
        finally
        {
            ReleaseComObject(items);
            ReleaseComObject(inbox);
            ReleaseComObject(session);
            ReleaseComObject(outlook);
        }
    }

    private static bool TryReadMailSnapshot(object value, out OutlookMailSnapshot snapshot)
    {
        snapshot = default;

        try
        {
            dynamic item = value;
            var messageClass = (item.MessageClass as string) ?? string.Empty;
            if (!messageClass.StartsWith("IPM.Note", StringComparison.OrdinalIgnoreCase))
                return false;

            var receivedAt = item.ReceivedTime is DateTime dateTime
                ? dateTime
                : DateTime.MinValue;
            if (receivedAt == DateTime.MinValue)
                return false;

            var senderName = SafeTrim(item.SenderName as string);
            var subject = SafeTrim(item.Subject as string);
            var body = CreatePreview(item.Body as string);

            var title = string.IsNullOrWhiteSpace(senderName) ? subject : senderName;
            if (string.IsNullOrWhiteSpace(title))
                title = "Outlook";

            var content = string.IsNullOrWhiteSpace(subject)
                ? body
                : string.IsNullOrWhiteSpace(body)
                    ? subject
                    : subject + " - " + body;

            snapshot = new OutlookMailSnapshot(title, content, receivedAt);
            return !string.IsNullOrWhiteSpace(snapshot.Body);
        }
        catch
        {
            return false;
        }
    }

    private static string CreatePreview(string? body)
    {
        var normalized = Regex.Replace(body ?? string.Empty, "\\s+", " ").Trim();
        if (normalized.Length <= 180)
            return normalized;

        return normalized[..180].TrimEnd() + "...";
    }

    private static string SafeTrim(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static void ReleaseComObject(object? value)
    {
        if (value == null)
            return;

        try
        {
            if (Marshal.IsComObject(value))
                Marshal.FinalReleaseComObject(value);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        IsActive = false;
        _timer?.Dispose();
        _pollLock.Dispose();
    }

    private readonly record struct OutlookMailSnapshot(string Title, string Body, DateTime ReceivedAt);
}
