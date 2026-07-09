using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace QuickNoteApp.Models;

public class NoteItem
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;
    public string? ImagePath { get; set; }
    public bool IsDone { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsPinned { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public List<NoteTag> TagList { get; set; } = new();

    public string TextPreview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Text)) return string.Empty;
            // Clean markdown syntax characters
            var clean = Regex.Replace(Text, @"[\*_`#]", "");
            // Clean list bullet prefix
            clean = Regex.Replace(clean, @"^\s*[-*+]\s+", "", RegexOptions.Multiline);
            return clean.Length > 150 ? clean.Substring(0, 150) + "..." : clean;
        }
    }
}