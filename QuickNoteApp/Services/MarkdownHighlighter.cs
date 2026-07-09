using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Collections.Generic;
using System.Linq;

namespace QuickNoteApp.Services;

public static class MarkdownHighlighter
{
    private static readonly Regex BoldRegex = new(@"\*\*(.*?)\*\*", RegexOptions.Compiled);
    private static readonly Regex ItalicRegex = new(@"\*(.*?)\*", RegexOptions.Compiled);
    private static readonly Regex CodeRegex = new(@"`(.*?)`", RegexOptions.Compiled);
    private static readonly Regex NoteLinkRegex = new(@"\[\[(.*?)\]\]", RegexOptions.Compiled);
    private static readonly Regex HeaderRegex = new(@"^(#{1,6})\s+(.*)$", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex BulletRegex = new(@"^\s*[-*+]\s+(.*)$", RegexOptions.Compiled | RegexOptions.Multiline);

    private static bool _isHighlighting = false;

    public static void Highlight(System.Windows.Controls.RichTextBox rtb)
    {
        if (_isHighlighting) return;
        _isHighlighting = true;

        try
        {
            var doc = rtb.Document;
            
            // Save caret position
            TextPointer caret = rtb.CaretPosition;
            int caretOffset = GetCharOffset(doc.ContentStart, caret);

            // Clear formatting
            var fullRange = new TextRange(doc.ContentStart, doc.ContentEnd);
            fullRange.ClearAllProperties();
            
            // Apply theme-consistent default foreground
            fullRange.ApplyPropertyValue(TextElement.ForegroundProperty, new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#CBD5E1"))); // Slate 300
            fullRange.ApplyPropertyValue(TextElement.FontSizeProperty, 13.0);
            fullRange.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Normal);
            fullRange.ApplyPropertyValue(TextElement.FontStyleProperty, FontStyles.Normal);

            // Highlight regex matches
            HighlightRegex(doc, NoteLinkRegex, range =>
            {
                range.ApplyPropertyValue(TextElement.ForegroundProperty, new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#00D4FF")));
                range.ApplyPropertyValue(System.Windows.Documents.Inline.TextDecorationsProperty, System.Windows.TextDecorations.Underline);
                range.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.SemiBold);
            });

            HighlightRegex(doc, BoldRegex, range =>
            {
                range.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Bold);
                range.ApplyPropertyValue(TextElement.ForegroundProperty, new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFFFFF")));
            });

            HighlightRegex(doc, ItalicRegex, range =>
            {
                range.ApplyPropertyValue(TextElement.FontStyleProperty, FontStyles.Italic);
            });

            HighlightRegex(doc, CodeRegex, range =>
            {
                range.ApplyPropertyValue(TextElement.FontFamilyProperty, new System.Windows.Media.FontFamily("Consolas"));
                range.ApplyPropertyValue(TextElement.BackgroundProperty, new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E1B4B"))); // Indigo 950
                range.ApplyPropertyValue(TextElement.ForegroundProperty, new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F43F5E"))); // Rose 500
            });

            HighlightHeadersAndBullets(doc);

            // Restore caret
            var newCaret = GetPointerAtCharOffset(doc.ContentStart, caretOffset);
            if (newCaret != null)
            {
                rtb.CaretPosition = newCaret;
            }
        }
        finally
        {
            _isHighlighting = false;
        }
    }

    private static void HighlightRegex(FlowDocument doc, Regex regex, Action<TextRange> applyStyle)
    {
        string text = new TextRange(doc.ContentStart, doc.ContentEnd).Text;
        foreach (Match match in regex.Matches(text))
        {
            var startPointer = GetPointerAtCharOffset(doc.ContentStart, match.Index);
            var endPointer = GetPointerAtCharOffset(doc.ContentStart, match.Index + match.Length);
            if (startPointer != null && endPointer != null)
            {
                var range = new TextRange(startPointer, endPointer);
                applyStyle(range);
            }
        }
    }

    private static void HighlightHeadersAndBullets(FlowDocument doc)
    {
        foreach (var block in doc.Blocks)
        {
            if (block is Paragraph p)
            {
                var range = new TextRange(p.ContentStart, p.ContentEnd);
                var text = range.Text;
                
                var headerMatch = HeaderRegex.Match(text);
                if (headerMatch.Success)
                {
                    int level = headerMatch.Groups[1].Length;
                    double size = 13.0 + (7 - level) * 2; // H1=25, H2=23, etc.
                    range.ApplyPropertyValue(TextElement.FontSizeProperty, size);
                    range.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Bold);
                    range.ApplyPropertyValue(TextElement.ForegroundProperty, new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#C084FC"))); // Purple 400
                }
                
                var bulletMatch = BulletRegex.Match(text);
                if (bulletMatch.Success)
                {
                    range.ApplyPropertyValue(TextElement.ForegroundProperty, new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#34D399"))); // Emerald 400
                }
            }
        }
    }

    private static int GetCharOffset(TextPointer start, TextPointer current)
    {
        return new TextRange(start, current).Text.Length;
    }

    private static TextPointer? GetPointerAtCharOffset(TextPointer start, int charOffset)
    {
        TextPointer p = start;
        int currentChars = 0;
        
        while (p != null)
        {
            if (p.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                string textRun = p.GetTextInRun(LogicalDirection.Forward);
                int runLen = textRun.Length;
                
                if (currentChars + runLen >= charOffset)
                {
                    return p.GetPositionAtOffset(charOffset - currentChars, LogicalDirection.Forward);
                }
                
                currentChars += runLen;
            }
            
            var next = p.GetNextContextPosition(LogicalDirection.Forward);
            if (next == null) break;
            p = next;
        }
        return p;
    }
}
