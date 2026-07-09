using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace QuickNoteApp.Services;

public static class EmailDialogService
{
    public static bool TryShowEmailDialog(Window owner, string defaultSubject, string body, out string recipient, out string subject, out bool useGmail)
    {
        recipient = string.Empty;
        subject = defaultSubject;
        useGmail = true;

        var selectedRecipient = "";
        var selectedSubject = defaultSubject;
        var selectedUseGmail = true;

        var theme = DialogTheme.Current();
        var bgDeepBrush = theme.BgDeepBrush;
        var bgPanelBrush = theme.BgPanelBrush;
        var bgInputBrush = theme.BgInputBrush;
        var borderSoftBrush = theme.BorderSoftBrush;
        var textPrimaryBrush = theme.TextPrimaryBrush;
        var textSecondaryBrush = theme.TextSecondaryBrush;
        var accentPurple = theme.AccentPurple;
        var accentCyan = theme.AccentCyan;

        // Window
        var window = new Window
        {
            Owner = owner,
            Width = 460,
            Height = 440,
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
            Text = "📧 E-posta Gönder",
            Foreground = textPrimaryBrush,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(16, 12, 16, 6)
        };
        titleBar.Children.Add(titleText);
        Grid.SetRow(titleBar, 1);
        grid.Children.Add(titleBar);

        // 3. Content Panel
        var contentPanel = new StackPanel
        {
            Margin = new Thickness(16, 8, 16, 8)
        };

        // Recipient
        contentPanel.Children.Add(new TextBlock
        {
            Text = "Alıcı E-posta",
            Foreground = textSecondaryBrush,
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 4)
        });

        var recipientInput = new System.Windows.Controls.TextBox
        {
            Background = bgInputBrush,
            Foreground = textPrimaryBrush,
            BorderBrush = borderSoftBrush,
            BorderThickness = new Thickness(1),
            Height = 38,
            Padding = new Thickness(6, 2, 6, 2),
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 13
        };
        contentPanel.Children.Add(recipientInput);

        // Subject
        contentPanel.Children.Add(new TextBlock
        {
            Text = "Konu",
            Foreground = textSecondaryBrush,
            FontSize = 12,
            Margin = new Thickness(0, 8, 0, 4)
        });

        var subjectInput = new System.Windows.Controls.TextBox
        {
            Text = defaultSubject,
            Background = bgInputBrush,
            Foreground = textPrimaryBrush,
            BorderBrush = borderSoftBrush,
            BorderThickness = new Thickness(1),
            Height = 38,
            Padding = new Thickness(6, 2, 6, 2),
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 13
        };
        contentPanel.Children.Add(subjectInput);

        // Body Preview
        contentPanel.Children.Add(new TextBlock
        {
            Text = "İçerik Önizleme",
            Foreground = textSecondaryBrush,
            FontSize = 12,
            Margin = new Thickness(0, 8, 0, 4)
        });

        var bodyBorder = new Border
        {
            Background = bgPanelBrush,
            BorderBrush = borderSoftBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Height = 85,
            Padding = new Thickness(8)
        };

        var bodyViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        var bodyText = new TextBlock
        {
            Text = body,
            Foreground = textSecondaryBrush,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };

        bodyViewer.Content = bodyText;
        bodyBorder.Child = bodyViewer;
        contentPanel.Children.Add(bodyBorder);

        // Service Selectors
        contentPanel.Children.Add(new TextBlock
        {
            Text = "Gönderim Yöntemi",
            Foreground = textSecondaryBrush,
            FontSize = 12,
            Margin = new Thickness(0, 10, 0, 4)
        });

        var radioPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 6) };
        
        var gmailRadio = new System.Windows.Controls.RadioButton
        {
            GroupName = "EmailProvider",
            Content = "Gmail (Web Tarayıcı)",
            IsChecked = true,
            Foreground = textPrimaryBrush,
            Margin = new Thickness(0, 0, 24, 0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        
        var outlookRadio = new System.Windows.Controls.RadioButton
        {
            GroupName = "EmailProvider",
            Content = "Outlook (Masaüstü)",
            IsChecked = false,
            Foreground = textPrimaryBrush,
            Cursor = System.Windows.Input.Cursors.Hand
        };

        radioPanel.Children.Add(gmailRadio);
        radioPanel.Children.Add(outlookRadio);
        contentPanel.Children.Add(radioPanel);

        Grid.SetRow(contentPanel, 2);
        grid.Children.Add(contentPanel);

        // 4. Buttons Panel
        var bottomGrid = new Grid();
        bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var errorText = new TextBlock
        {
            Foreground = theme.ErrorBrush,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 16, 16),
            TextWrapping = TextWrapping.Wrap,
            Width = 200,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left
        };

        var buttonsPanel = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(16, 4, 16, 16)
        };

        var cancelBtn = new System.Windows.Controls.Button
        {
            Content = "Vazgeç",
            MinWidth = 80,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0),
            Background = bgInputBrush,
            Foreground = textPrimaryBrush,
            BorderBrush = borderSoftBrush
        };

        var okBtn = new System.Windows.Controls.Button
        {
            Content = "E-posta Aç",
            MinWidth = 100,
            Height = 32,
            Background = theme.AccentPurpleBrush,
            Foreground = System.Windows.Media.Brushes.White,
            BorderBrush = theme.AccentPurpleBrush,
            FontWeight = FontWeights.SemiBold
        };

        buttonsPanel.Children.Add(cancelBtn);
        buttonsPanel.Children.Add(okBtn);

        bottomGrid.Children.Add(errorText);
        Grid.SetColumn(errorText, 0);
        bottomGrid.Children.Add(buttonsPanel);
        Grid.SetColumn(buttonsPanel, 1);

        Grid.SetRow(bottomGrid, 3);
        grid.Children.Add(bottomGrid);

        mainBorder.Child = grid;
        window.Content = mainBorder;

        // Button clicks
        cancelBtn.Click += (s, e) => window.DialogResult = false;
        okBtn.Click += (s, e) =>
        {
            var emailTo = recipientInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(emailTo))
            {
                errorText.Text = "Lütfen bir alıcı e-posta adresi yazın.";
                return;
            }

            // Simple validation
            if (!emailTo.Contains("@") || !emailTo.Contains("."))
            {
                errorText.Text = "Geçersiz e-posta formatı.";
                return;
            }

            selectedRecipient = emailTo;
            selectedSubject = subjectInput.Text.Trim();
            selectedUseGmail = gmailRadio.IsChecked == true;

            window.DialogResult = true;
        };

        var accepted = window.ShowDialog() == true;
        if (accepted)
        {
            recipient = selectedRecipient;
            subject = selectedSubject;
            useGmail = selectedUseGmail;
        }

        return accepted;
    }
}
