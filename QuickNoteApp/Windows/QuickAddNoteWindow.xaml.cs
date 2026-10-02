using System;
using System.Windows;
using System.Windows.Input;
using QuickNoteApp.Services;

namespace QuickNoteApp.Windows
{
    public partial class QuickAddNoteWindow : Window
    {
        private readonly DatabaseService _db;
        private readonly Action _onNoteSaved;

        public QuickAddNoteWindow(DatabaseService db, Action onNoteSaved)
        {
            InitializeComponent();
            _db = db;
            _onNoteSaved = onNoteSaved;

            Loaded += (s, e) =>
            {
                TitleInput.Focus();
                // Set default text hints / placeholders
                TitleInput.Text = "";
                ContentInput.Text = "";
            };
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveNote();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
            else if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true;
                SaveNote();
            }
            else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true;
                SaveNote();
            }
        }

        private void SaveNote()
        {
            string title = TitleInput.Text.Trim();
            string text = ContentInput.Text.Trim();

            if (string.IsNullOrWhiteSpace(text))
            {
                System.Windows.MessageBox.Show(this, "Not içeriği boş olamaz.", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
                ContentInput.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                // Auto generate title from first line of text
                var firstLine = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                title = firstLine.Length > 30 ? firstLine[..27] + "..." : firstLine;
            }

            try
            {
                _db.AddNote(title, text);
                _onNoteSaved?.Invoke();
                Close();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, $"Not kaydedilirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
