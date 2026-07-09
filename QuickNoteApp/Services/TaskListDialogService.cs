using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using QuickNoteApp.Models;

namespace QuickNoteApp.Services;

public static class TaskListDialogService
{
    public static void ShowTaskListDialog(Window owner, string taskListText, DateTime date, DatabaseService db)
    {
        var theme = DialogTheme.Current();
        var bgDeepBrush = theme.BgDeepBrush;
        var bgPanelBrush = theme.BgPanelBrush;
        var borderSoftBrush = theme.BorderSoftBrush;
        var textPrimaryBrush = theme.TextPrimaryBrush;
        var textSecondaryBrush = theme.TextSecondaryBrush;
        var accentPurple = theme.AccentPurple;
        var accentCyan = theme.AccentCyan;

        // Window
        var window = new Window
        {
            Owner = owner,
            Width = 520,
            Height = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            ShowInTaskbar = false
        };

        // Shadow and Main Border
        var shadowEffect = new DropShadowEffect
        {
            BlurRadius = 24,
            ShadowDepth = 0,
            Opacity = 0.5,
            Color = Colors.Black
        };

        var mainBorder = new Border
        {
            Background = bgDeepBrush,
            BorderBrush = borderSoftBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Effect = shadowEffect,
            Margin = new Thickness(10)
        };

        // Layout Grid
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3) }); // Accent strip
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });    // Title bar
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });    // Buttons

        // 1. Accent Strip
        var accentGradient = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint = new System.Windows.Point(1, 0)
        };
        accentGradient.GradientStops.Add(new GradientStop(accentPurple, 0));
        accentGradient.GradientStops.Add(new GradientStop(accentCyan, 1));

        var accentBorder = new Border
        {
            Background = accentGradient,
            CornerRadius = new CornerRadius(12, 12, 0, 0),
            Height = 3
        };
        Grid.SetRow(accentBorder, 0);
        grid.Children.Add(accentBorder);

        // 2. Title Bar
        var titleBar = new Grid
        {
            Background = System.Windows.Media.Brushes.Transparent
        };
        titleBar.MouseLeftButtonDown += (s, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                window.DragMove();
        };

        var titleText = new TextBlock
        {
            Text = $"📅 Günlük Görev Planı ({date:dd.MM.yyyy})",
            Foreground = textPrimaryBrush,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(16, 12, 16, 6)
        };
        titleBar.Children.Add(titleText);
        Grid.SetRow(titleBar, 1);
        grid.Children.Add(titleBar);

        // 3. Content Panel (Scrollable Tasks List)
        var contentBorder = new Border
        {
            Background = bgPanelBrush,
            BorderBrush = borderSoftBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(16, 8, 16, 8),
            Padding = new Thickness(12)
        };

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        var taskListDisplay = new TextBlock
        {
            Text = taskListText,
            Foreground = textPrimaryBrush,
            FontSize = 13.5,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 22,
            FontFamily = new System.Windows.Media.FontFamily("Consolas, Courier New, Segoe UI")
        };

        scrollViewer.Content = taskListDisplay;
        contentBorder.Child = scrollViewer;
        Grid.SetRow(contentBorder, 2);
        grid.Children.Add(contentBorder);

        // 4. Buttons Panel
        var bottomGrid = new Grid
        {
            Margin = new Thickness(16, 4, 16, 16)
        };
        bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var statusText = new TextBlock
        {
            Foreground = theme.AccentCyanBrush,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Width = 180,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left
        };
        bottomGrid.Children.Add(statusText);
        Grid.SetColumn(statusText, 0);

        var buttonsStack = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };

        var copyBtn = new System.Windows.Controls.Button
        {
            Content = "📋 Panoya Kopyala",
            MinWidth = 120,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0),
            Background = bgPanelBrush,
            Foreground = textPrimaryBrush,
            BorderBrush = borderSoftBrush
        };

        var saveBtn = new System.Windows.Controls.Button
        {
            Content = "💾 Not Olarak Kaydet",
            MinWidth = 130,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0),
            Background = bgPanelBrush,
            Foreground = textPrimaryBrush,
            BorderBrush = borderSoftBrush
        };

        var closeBtn = new System.Windows.Controls.Button
        {
            Content = "Kapat",
            MinWidth = 70,
            Height = 32,
            Background = theme.AccentPurpleBrush,
            Foreground = System.Windows.Media.Brushes.White,
            BorderBrush = theme.AccentPurpleBrush,
            FontWeight = FontWeights.SemiBold
        };

        buttonsStack.Children.Add(copyBtn);
        buttonsStack.Children.Add(saveBtn);
        buttonsStack.Children.Add(closeBtn);
        bottomGrid.Children.Add(buttonsStack);
        Grid.SetColumn(buttonsStack, 1);

        Grid.SetRow(bottomGrid, 3);
        grid.Children.Add(bottomGrid);

        mainBorder.Child = grid;
        window.Content = mainBorder;

        // Button Click Handlers
        copyBtn.Click += (s, e) =>
        {
            try
            {
                System.Windows.Clipboard.SetText(taskListText);
                statusText.Text = "Görev listesi panoya kopyalandı.";
            }
            catch (Exception ex)
            {
                statusText.Text = "Kopyalama hatası: " + ex.Message;
            }
        };

        saveBtn.Click += (s, e) =>
        {
            try
            {
                var title = $"Görev Planı - {date:dd.MM.yyyy}";
                db.AddNote(title, taskListText);
                statusText.Text = "Görev planı not olarak kaydedildi.";
                saveBtn.IsEnabled = false; // Prevent double saving
            }
            catch (Exception ex)
            {
                statusText.Text = "Kaydetme hatası: " + ex.Message;
            }
        };

        closeBtn.Click += (s, e) => window.Close();

        window.ShowDialog();
    }
}
