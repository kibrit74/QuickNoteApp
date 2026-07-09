using System.Windows.Forms;
using Application = System.Windows.Forms.Application;

namespace QuickNoteApp.Services;

/// <summary>
/// Uygulama tamamen tepside yaşıyor - görev çubuğunda pencere yok, sadece hotkey'ler ve
/// bu tepsi ikonu üzerinden erişilebiliyor.
/// </summary>
public class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;

    public event EventHandler? OpenNoteRequested;
    public event EventHandler? OpenReviewRequested;
    public event EventHandler? ExitRequested;

    public TrayIconService()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Hızlı not (Ctrl+Shift+N)", null, (_, _) => OpenNoteRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Gün sonu özeti (Ctrl+Shift+R)", null, (_, _) => OpenReviewRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Çıkış", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _notifyIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application, // Resources/app.ico ile değiştir
            Text = "Hızlı Not & Bildirim Takibi",
            Visible = true,
            ContextMenuStrip = menu
        };

        _notifyIcon.DoubleClick += (_, _) => OpenNoteRequested?.Invoke(this, EventArgs.Empty);
    }
    public void ShowReminder(string title, string text)
    {
        _notifyIcon.BalloonTipTitle = string.IsNullOrWhiteSpace(title) ? "Hatırlatma" : title;
        _notifyIcon.BalloonTipText = string.IsNullOrWhiteSpace(text) ? "Hatırlatma zamanı geldi." : text;
        _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(10000);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
