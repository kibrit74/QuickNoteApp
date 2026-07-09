using System;

namespace QuickNoteApp.Models;

public class CalendarEvent
{
    public string ExternalId { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string Location { get; set; } = string.Empty;
    public bool IsAllDay { get; set; }

    public string FormatForReport()
    {
        var timeStr = IsAllDay ? "Tum gun" : StartTime.ToString("HH:mm") + " - " + EndTime.ToString("HH:mm");
        var details = string.IsNullOrWhiteSpace(Summary) ? "(Konu Yok)" : Summary;
        var locationStr = string.IsNullOrWhiteSpace(Location) ? "" : $" [Konum: {Location}]";
        var descStr = string.IsNullOrWhiteSpace(Description) ? "" : $" ({Description})";
        
        return $"- [{timeStr}] {details}{locationStr}{descStr}";
    }
}


