namespace QuickNoteApp.Models;

public class ReminderItem
{
    public int Id { get; set; }
    public int? NoteId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTime ReminderAt { get; set; }
    public bool IsShown { get; set; }
    public DateTime CreatedAt { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string SourceKey { get; set; } = string.Empty;
}
