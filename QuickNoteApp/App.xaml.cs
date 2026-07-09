using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Controls;
using Microsoft.Data.Sqlite;
using QuickNoteApp.Services;
using QuickNoteApp.Models;
using QuickNoteApp.Windows;

namespace QuickNoteApp;

public partial class App : System.Windows.Application
{
    private DatabaseService _db = null!;
    private HotkeyManager _hotkeys = null!;
    private NotificationListenerService _notificationListener = null!;
    private TrayIconService _tray = null!;
    private ReminderService _reminders = null!;
    private GoogleAuthService _googleAuth = null!;
    private CalendarPollingService _calendarPolling = null!;
    private CalendarReminderSyncService _calendarReminderSync = null!;

    private QuickNoteWindow? _quickNoteWindow;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        _db = new DatabaseService();
        _db.Initialize();
        _googleAuth = new GoogleAuthService();
        _calendarPolling = new CalendarPollingService(_googleAuth);
        _calendarReminderSync = new CalendarReminderSyncService(_googleAuth, _db);

        _hotkeys = new HotkeyManager();
        RegisterHotkeys();
        _hotkeys.HotkeyPressed += OnHotkeyPressed;

        _tray = new TrayIconService();
        _tray.OpenNoteRequested += (_, _) => ShowQuickNoteWindow();
        _tray.OpenReviewRequested += (_, _) => ShowReviewWindow();
        _tray.ExitRequested += (_, _) => Shutdown();
        StartupLog("Tray hazır");
        
        _notificationListener = new NotificationListenerService(_db);
        StartupLog("Pencere açma öncesi");
        ShowQuickNoteWindow();
        StartupLog("Pencere açma sonrası");

        // Defer starting background services until UI thread is fully idle (after window shows up)
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _ = _notificationListener.StartAsync();
            _calendarPolling.Start();
            _calendarReminderSync.Start();

            _reminders = new ReminderService(_db, ShowReminderAlert);
            _reminders.Start();
            StartupLog("Arka plan servisleri başlatıldı.");
        }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }



    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        System.Windows.MessageBox.Show(
            BuildUserFriendlyErrorMessage(e.Exception),
            "QuickNoteApp hata yakaladı",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        e.Handled = true;
    }

    private static string BuildUserFriendlyErrorMessage(Exception exception)
    {
        if (IsDiskFullSqliteError(exception))
        {
            return "C diskinde boş alan kalmadığı için not veritabanına yazılamıyor.\n\n" +
                   "Lütfen biraz disk alanı açıp QuickNoteApp'i tekrar deneyin.";
        }

        return $"Beklenmeyen hata: {exception.Message}";
    }

    private static bool IsDiskFullSqliteError(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is SqliteException sqliteException && sqliteException.SqliteErrorCode == 13)
                return true;

            if (current.Message.Contains("database or disk is full", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static void StartupLog(string message)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[QuickNote] " + message);
        }
        catch
        {
            // Log yazilamazsa uygulama acilisini etkilemesin.
        }
    }

    private void ShowReminderAlert(ReminderItem reminder)
    {
        var notificationText = $"{reminder.Title}\n{reminder.Text}".Trim();
        try
        {
            _tray.ShowReminder($"Hatırlatma - {reminder.ReminderAt:dd.MM.yyyy HH:mm}", notificationText);
        }
        catch (Exception ex)
        {
            StartupLog($"Tray reminder failed: {ex.Message}");
        }

        var window = new Window
        {
            Title = "Hatırlatma",
            Width = 440,
            Height = 300,
            MinWidth = 380,
            MinHeight = 260,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.CanResize,
            Topmost = true,
            Background = System.Windows.Media.Brushes.White
        };

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(reminder.Title) ? "Hatırlatma" : reminder.Title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };

        var body = new System.Windows.Controls.TextBox
        {
            Text = $"{reminder.ReminderAt:dd.MM.yyyy HH:mm}\n\n{reminder.Text}".Trim(),
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10)
        };

        var close = new System.Windows.Controls.Button
        {
            Content = "Tamam",
            MinWidth = 96,
            Height = 34,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        close.Click += (_, _) => window.Close();

        Grid.SetRow(title, 0);
        Grid.SetRow(body, 1);
        Grid.SetRow(close, 2);
        root.Children.Add(title);
        root.Children.Add(body);
        root.Children.Add(close);

        window.Content = root;
        window.Show();
        window.Activate();
    }
    private void RegisterHotkeys()
    {
        if (!TryRegisterHotkey(HotkeyManager.NOTE_HOTKEY_ID, ModifierKeys.Control | ModifierKeys.Shift, System.Windows.Forms.Keys.N))
            TryRegisterHotkey(HotkeyManager.NOTE_HOTKEY_ID, ModifierKeys.Control | ModifierKeys.Alt, System.Windows.Forms.Keys.N);

        if (!TryRegisterHotkey(HotkeyManager.REVIEW_HOTKEY_ID, ModifierKeys.Control | ModifierKeys.Shift, System.Windows.Forms.Keys.R))
            TryRegisterHotkey(HotkeyManager.REVIEW_HOTKEY_ID, ModifierKeys.Control | ModifierKeys.Alt, System.Windows.Forms.Keys.R);
    }

    private bool TryRegisterHotkey(int id, ModifierKeys modifiers, System.Windows.Forms.Keys key)
    {
        try
        {
            _hotkeys.Register(id, modifiers, key);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void OnHotkeyPressed(object? sender, int hotkeyId)
    {
        if (hotkeyId == HotkeyManager.NOTE_HOTKEY_ID)
            ShowQuickNoteWindow();
        else if (hotkeyId == HotkeyManager.REVIEW_HOTKEY_ID)
            ShowReviewWindow();
    }

    private void ShowQuickNoteWindow(DateTime? date = null)
    {
        if (_quickNoteWindow == null || !_quickNoteWindow.IsLoaded)
        {
            _quickNoteWindow = new QuickNoteWindow(_db, _googleAuth, _calendarPolling, _notificationListener);
        }

        _quickNoteWindow.Show();
        _quickNoteWindow.Activate();

        if (date.HasValue)
            _quickNoteWindow.ShowDate(date.Value);
        else
            _quickNoteWindow.FocusInput();
    }

    private void ShowReviewWindow(DateTime? date = null)
    {
        var targetDate = date?.Date ?? _quickNoteWindow?.SelectedNoteDate?.Date ?? DateTime.Today;
        ShowQuickNoteWindow(targetDate);
        _quickNoteWindow?.ShowDailySummary(targetDate);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeys.Dispose();
        _tray.Dispose();
        _notificationListener.Dispose();
        _reminders?.Dispose();
        _calendarPolling.Dispose();
        _calendarReminderSync.Dispose();
        base.OnExit(e);
    }
}

