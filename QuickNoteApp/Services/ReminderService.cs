using QuickNoteApp.Models;
using System.Windows.Threading;

namespace QuickNoteApp.Services;

public sealed class ReminderService : IDisposable
{
    private readonly DatabaseService _db;
    private readonly Action<ReminderItem> _onReminderDue;
    private readonly DispatcherTimer _timer;
    private bool _isChecking;

    public ReminderService(DatabaseService db, Action<ReminderItem> onReminderDue)
    {
        _db = db;
        _onReminderDue = onReminderDue;
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(15)
        };
        _timer.Tick += (_, _) => CheckDueReminders();
    }

    public void Start()
    {
        _timer.Start();
        CheckDueReminders();
    }

    private void CheckDueReminders()
    {
        if (_isChecking)
            return;

        _isChecking = true;
        try
        {
            var dueReminders = _db.GetDueReminders(DateTime.Now);
            foreach (var reminder in dueReminders)
            {
                _onReminderDue(reminder);
                _db.MarkReminderShown(reminder.Id);
            }
        }
        finally
        {
            _isChecking = false;
        }
    }

    public void Dispose()
    {
        _timer.Stop();
    }
}