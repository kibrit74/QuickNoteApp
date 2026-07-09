using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using QuickNoteApp.Models;

namespace QuickNoteApp.Services;

public static class CalendarEventTimeDialogService
{
    public static bool TryAskCalendarTime(Window owner, NoteItem note, out DateTime start, out DateTime end)
    {
        var selectedStart = RoundToNextHalfHour(DateTime.Now.AddHours(1));
        var selectedEnd = selectedStart.AddMinutes(30);
        start = selectedStart;
        end = selectedEnd;
        var theme = DialogTheme.Current();

        var window = new Window
        {
            Title = "Takvime ekle",
            Owner = owner,
            Width = 390,
            Height = 440,
            MinWidth = 360,
            MinHeight = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            Background = theme.BgDeepBrush
        };

        var datePicker = new DatePicker
        {
            SelectedDate = selectedStart.Date,
            Margin = new Thickness(0, 4, 0, 10),
            Background = theme.BgInputBrush,
            Foreground = theme.TextPrimaryBrush,
            BorderBrush = theme.BorderSoftBrush
        };

        var timeBox = CreateInput(selectedStart.ToString("HH:mm"), theme, new Thickness(0, 4, 0, 12));
        var durationBox = CreateInput("30", theme, new Thickness(0, 4, 0, 12));

        var errorText = new TextBlock
        {
            Foreground = theme.ErrorBrush,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 24
        };

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock
        {
            Text = note.Title,
            Foreground = theme.TextPrimaryBrush,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 58,
            TextTrimming = System.Windows.TextTrimming.CharacterEllipsis
        });
        panel.Children.Add(CreateLabel("Tarih", theme, new Thickness(0, 14, 0, 0)));
        panel.Children.Add(datePicker);
        panel.Children.Add(CreateLabel("Saat (?r. 14:30)", theme));
        panel.Children.Add(timeBox);
        panel.Children.Add(CreateLabel("S?re (dakika)", theme));
        panel.Children.Add(durationBox);
        panel.Children.Add(errorText);

        var buttons = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var cancelButton = new System.Windows.Controls.Button { Content = "Vazge?", MinWidth = 86, Margin = new Thickness(0, 0, 8, 0) };
        var okButton = new System.Windows.Controls.Button { Content = "Takvime Ekle", MinWidth = 110 };
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(okButton);
        panel.Children.Add(buttons);

        window.Content = new Border
        {
            Background = theme.BgPanelBrush,
            BorderBrush = theme.BorderSoftBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(2),
            Child = panel
        };

        cancelButton.Click += (_, _) => window.DialogResult = false;
        okButton.Click += (_, _) =>
        {
            if (datePicker.SelectedDate == null || !TimeSpan.TryParse(timeBox.Text.Trim(), out var time))
            {
                errorText.Text = "Tarih ve saati kontrol et.";
                return;
            }

            if (!int.TryParse(durationBox.Text.Trim(), out var durationMinutes) || durationMinutes < 5 || durationMinutes > 1440)
            {
                errorText.Text = "S?re 5 ile 1440 dakika aras?nda olmal?.";
                return;
            }

            var chosenStart = datePicker.SelectedDate.Value.Date.Add(time);
            selectedStart = chosenStart;
            selectedEnd = chosenStart.AddMinutes(durationMinutes);
            window.DialogResult = true;
        };

        var accepted = window.ShowDialog() == true;
        if (accepted)
        {
            start = selectedStart;
            end = selectedEnd;
        }

        return accepted;
    }

    private static TextBlock CreateLabel(string text, DialogTheme theme, Thickness? margin = null)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = theme.TextSecondaryBrush,
            Margin = margin ?? new Thickness(0),
            FontSize = 12.5
        };
    }

    private static System.Windows.Controls.TextBox CreateInput(string text, DialogTheme theme, Thickness margin)
    {
        return new System.Windows.Controls.TextBox
        {
            Text = text,
            Height = 34,
            Margin = margin,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = theme.BgInputBrush,
            Foreground = theme.TextPrimaryBrush,
            BorderBrush = theme.BorderSoftBrush,
            CaretBrush = theme.AccentCyanBrush
        };
    }

    private static DateTime RoundToNextHalfHour(DateTime value)
    {
        var minute = value.Minute <= 30 ? 30 : 60;
        var rounded = new DateTime(value.Year, value.Month, value.Day, value.Hour, 0, 0);
        return rounded.AddMinutes(minute);
    }
}
