using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Text.RegularExpressions;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuickNoteApp.Models;
using QuickNoteApp.Services;

namespace QuickNoteApp.Windows;

public partial class QuickNoteWindow : Window
{
    private readonly DatabaseService _db;
    private readonly GeminiCliService _gemini = new();
    private readonly GoogleAuthService _googleAuth;
    private readonly CalendarPollingService _calendarPolling;
    private readonly NotificationListenerService _notificationListener;
    private const string DefaultGeminiInstruction = "Notu özetle. Görsel varsa görseldeki metinleri çıkar. Varsa yapılacak işleri madde madde yaz.";
    private string? _selectedImagePath;
    private readonly List<string> _selectedFilePaths = new();
    private string? _lastGeminiResponse;
    private int? _editingNoteId;
    private DateTime? _selectedNoteDate;
    private bool _isChangingDateFilter;
    private bool _isDailySummaryMode;
    private NoteItem? _modalNote;
    private FloatingRestoreWindow? _floatingRestoreWindow;
    private readonly AudioRecorderService _audioRecorder = new();
    private bool _isDictating;
    private bool _isLoadingNote;
    private bool _isSidebarCollapsed;
    private readonly List<string> _editingNoteTags = new();
    private string? _selectedTagFilter;
    
    private readonly System.Windows.Threading.DispatcherTimer _autoSaveTimer;
    private string _originalTitle = string.Empty;
    private string _originalText = string.Empty;
    private string? _originalImagePath;
    private readonly List<string> _originalTags = new();

    private string NoteInputText
    {
        get { return FlowDocumentToMarkdown(NoteInput.Document); }
        set { SetNoteInputText(value); }
    }

    private string NoteInputSelectedText
    {
        get { return new System.Windows.Documents.TextRange(NoteInput.Selection.Start, NoteInput.Selection.End).Text; }
    }

    public DateTime? SelectedNoteDate => _selectedNoteDate;

    public QuickNoteWindow(DatabaseService db, GoogleAuthService googleAuth, CalendarPollingService calendarPolling, NotificationListenerService notificationListener)
    {
        InitializeComponent();
        _db = db;
        _googleAuth = googleAuth;
        _calendarPolling = calendarPolling;
        _notificationListener = notificationListener;

        _autoSaveTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _autoSaveTimer.Tick += AutoSaveTimer_Tick;

        RefreshGoogleStatus();
        RefreshTagsInEditor();
        RefreshTagFilterPanel();
        RefreshNotes();
        NoteInput.PreviewMouseLeftButtonDown += NoteInput_PreviewMouseLeftButtonDown;
        TitleInput.TextChanged += TitleInput_TextChanged;
        LoadDashboardData();
        UpdateGeminiCliStatusUI();

        _notificationListener.StatusChanged += () => Dispatcher.Invoke(UpdateNotificationStatusUI);
        UpdateNotificationStatusUI();

        _calendarPolling.StatusChanged += () => Dispatcher.Invoke(UpdateCalendarStatusUI);
        UpdateCalendarStatusUI();

        Loaded += (s, e) => GeminiOnboardingWindow.ShowFirstRunIfNeeded(this);
        System.Windows.DataObject.AddPastingHandler(NoteInput, NoteInput_Pasting);
    }

    private void NoteInput_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetDataPresent(System.Windows.DataFormats.UnicodeText))
        {
            try
            {
                var text = (string)e.DataObject.GetData(System.Windows.DataFormats.UnicodeText);
                var dataObject = new System.Windows.DataObject();
                dataObject.SetText(text, System.Windows.TextDataFormat.UnicodeText);
                e.DataObject = dataObject;
            }
            catch
            {
            }
        }
        else if (e.DataObject.GetDataPresent(System.Windows.DataFormats.Text))
        {
            try
            {
                var text = (string)e.DataObject.GetData(System.Windows.DataFormats.Text);
                var dataObject = new System.Windows.DataObject();
                dataObject.SetText(text, System.Windows.TextDataFormat.Text);
                e.DataObject = dataObject;
            }
            catch
            {
            }
        }
        else
        {
            e.CancelCommand();
        }
    }

    private void OpenGeminiOnboardingButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new GeminiOnboardingWindow { Owner = this };
        window.ShowDialog();
        UpdateGeminiCliStatusUI();
    }

    private void RefreshGeminiStatusButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateGeminiCliStatusUI();
    }

    private void UpdateGeminiCliStatusUI()
    {
        if (GeminiCliStatusText == null || GeminiCliStatusDot == null)
            return;

        if (GeminiCliService.IsGeminiCliAvailable())
        {
            GeminiCliStatusText.Text = "Gemini CLI bulundu. Google oturumu açıldıysa kullanıma hazır.";
            GeminiCliStatusDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94));
            return;
        }

        GeminiCliStatusText.Text = "Gemini CLI bulunamadı. Rehberi açıp kurulumu tamamlayın.";
        GeminiCliStatusDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));
    }
    public void FocusInput()
    {
        TitleInput.Focus();
        RefreshNotes();
    }

    public void ShowDate(DateTime date)
    {
        _isDailySummaryMode = false;
        SetDateFilter(date);
        FocusInput();
    }

    public void ShowDailySummary(DateTime? date = null)
    {
        _isDailySummaryMode = true;
        SetDateFilter(date?.Date ?? _selectedNoteDate?.Date ?? DateTime.Today);
        Show();
        Activate();
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.V && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && ClipboardHasImage(showError: false))
        {
            e.Handled = true;
            PasteImageFromClipboard(showMessageWhenEmpty: false);
            return;
        }

        if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && ShouldCopySelectedNote(e.OriginalSource))
        {
            e.Handled = true;
            CopySelectedNoteToClipboard();
            return;
        }

        // Ctrl+S: Notu Kaydet
        if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            SaveCurrentNote();
            return;
        }

        // Ctrl+N: Yeni Not
        if (e.Key == Key.N && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            NewButton_Click(null!, null!);
            return;
        }

        // Ctrl+F: Aramaya Odaklan
        if (e.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            SearchInput.Focus();
            SearchInput.SelectAll();
            return;
        }

        // Ctrl+D: Notu Sabitle/Sabitlemeyi Kaldır
        if (e.Key == Key.D && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (NotesList.SelectedItem is NoteItem note)
            {
                e.Handled = true;
                _db.ToggleNotePin(note.Id);
                RefreshNotes();
                return;
            }
        }

        // Delete: Seçili Notu Sil (NotesList odaklıyken)
        if (e.Key == Key.Delete && NotesList.IsKeyboardFocusWithin)
        {
            if (NotesList.SelectedItem is NoteItem note)
            {
                e.Handled = true;
                DeleteNote(note);
                return;
            }
        }
    }

    private void NoteInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SaveCurrentNote();
            e.Handled = true;
        }
        else if (e.Key == Key.B && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            System.Windows.Documents.EditingCommands.ToggleBold.Execute(null, NoteInput);
            e.Handled = true;
        }
        else if (e.Key == Key.I && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            System.Windows.Documents.EditingCommands.ToggleItalic.Execute(null, NoteInput);
            e.Handled = true;
        }
    }

    private void ChooseImageButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Resim seç",
            Filter = "Resim dosyaları|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Tüm dosyalar|*.*"
        };

        if (dialog.ShowDialog(this) == true)
            SetSelectedImage(CopyImageToAttachments(dialog.FileName));
    }

    private void PasteImageButton_Click(object sender, RoutedEventArgs e)
    {
        PasteImageFromClipboard(showMessageWhenEmpty: true);
    }

    private void ChooseFileButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Gemini için dosya seç",
            Filter = "Desteklenen dosyalar|*.pdf;*.xlsx;*.csv;*.txt|PDF|*.pdf|Excel|*.xlsx|CSV|*.csv|Metin|*.txt|Tüm dosyalar|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog(this) != true)
            return;

        foreach (var fileName in dialog.FileNames)
        {
            if (!_selectedFilePaths.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                _selectedFilePaths.Add(fileName);
        }

        RefreshAttachmentText();
    }

    private void ClearFilesButton_Click(object sender, RoutedEventArgs e)
    {
        _selectedFilePaths.Clear();
        RefreshAttachmentText();
    }

    private void ExportGeminiCsvButton_Click(object sender, RoutedEventArgs e)
    {
        var content = _lastGeminiResponse;
        if (!string.IsNullOrWhiteSpace(content) && IsGeminiFileCapabilityRefusal(content))
            content = null;
        if (string.IsNullOrWhiteSpace(content))
            content = NoteInputSelectedText;
        if (string.IsNullOrWhiteSpace(content))
            content = NoteInputText;

        if (string.IsNullOrWhiteSpace(content))
        {
            CopilotStatusText.Text = "Excel'e aktarılacak metin yok.";
            return;
        }

        ExportTextToCsv(content);
    }
    private void PasteImageFromClipboard(bool showMessageWhenEmpty)
    {
        try
        {
            if (!ClipboardHasImage(showError: showMessageWhenEmpty))
            {
                if (showMessageWhenEmpty)
                    System.Windows.MessageBox.Show(this, "Panoda resim yok. Önce ekran görüntüsü alıp Ctrl+C yap veya ekran görüntüsü aracından kopyala.", "Resim yok", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var image = System.Windows.Clipboard.GetImage();
            if (image == null)
                return;

            image.Freeze();

            var targetPath = CreateAttachmentPath(".png");
            SaveClipboardImage(image, targetPath);

            SetSelectedImage(targetPath);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"Resim yapıştırılamadı: {ex.Message}", "Resim yapıştırma hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private bool ClipboardHasImage(bool showError)
    {
        try
        {
            return System.Windows.Clipboard.ContainsImage();
        }
        catch (Exception ex)
        {
            if (showError)
                System.Windows.MessageBox.Show(this, $"Panoya erişilemedi: {ex.Message}", "Pano hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private void ClearImageButton_Click(object sender, RoutedEventArgs e)
    {
        SetSelectedImage(null);
    }

    private async void GeminiButton_Click(object sender, RoutedEventArgs e)
    {
        await SendGeminiPromptAsync();
    }

    private async void GeminiPromptInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            return;

        e.Handled = true;
        await SendGeminiPromptAsync();
    }

    private async Task SendGeminiPromptAsync()
    {
        var question = GeminiPromptInput.Text.Trim();

        if (GeminiSearchIntent.ShouldUseCalendar(question))
        {
            await SendCalendarQuestionToGeminiAsync(question);
            return;
        }

        if (GeminiSearchIntent.ShouldUseDatabase(question))
        {
            await SendDatabaseQuestionToGeminiAsync(question);
            return;
        }

        await SendCurrentNoteToGeminiAsync(BuildGeminiHeading());
    }

    private async Task SendCalendarQuestionToGeminiAsync(string question)
    {
        try
        {
            var fallbackDate = _selectedNoteDate ?? DateTime.Today;
            var targetDate = CalendarQueryDateResolver.Resolve(question, fallbackDate);
            CopilotStatusText.Text = $"Gemini {targetDate:dd.MM.yyyy} takvimini kontrol ediyor...";

            var selectedEvents = await _calendarPolling.GetEventsForDateAsync(targetDate, targetDate.Date == DateTime.Today);
            var upcomingImportantEvents = await LoadUpcomingImportantCalendarEventsAsync(targetDate);
            var calendarSummary = CalendarSummaryBuilder.Build(targetDate, selectedEvents, upcomingImportantEvents);

            var databaseContext = _db.BuildGeminiDatabaseContext(question, 100);
            
            var targetDateNotes = _db.GetNotesForDate(targetDate);
            if (targetDateNotes != null && targetDateNotes.Count > 0)
            {
                var targetNotesText = string.Join(Environment.NewLine + "---" + Environment.NewLine, 
                    targetDateNotes.Select(n => $"Başlık: {n.Title}\nİçerik: {n.Text}"));
                databaseContext = $"Hedef Tarih Notları ({targetDate:dd.MM.yyyy}):" + Environment.NewLine + targetNotesText + Environment.NewLine + Environment.NewLine + databaseContext;
            }

            var instruction = GeminiCalendarSearchPrompt.Build(question, calendarSummary, databaseContext);
            var response = await _gemini.SummarizeAsync(instruction, "Takvim ve veritabanı arama sonucunu hazırla.");

            if (string.IsNullOrWhiteSpace(response) || IsGeminiStatus(response))
            {
                CopilotStatusText.Text = string.IsNullOrWhiteSpace(response) ? "Gemini takvim cevabi vermedi." : response;
                return;
            }

            _lastGeminiResponse = response;
            NoteInputText = AppendSection(NoteInputText, $"Gemini: {question}", response);
            CopilotStatusText.Text = $"Gemini {targetDate:dd.MM.yyyy} takvim sonucunu nota ekledi.";
        }
        catch (Exception ex)
        {
            CopilotStatusText.Text = $"Gemini takvim sorgulama hatasi: {ex.Message}";
        }
    }

    private async Task<List<CalendarEvent>> LoadUpcomingImportantCalendarEventsAsync(DateTime startDate)
    {
        var importantEvents = new List<CalendarEvent>();

        for (var dayOffset = 0; dayOffset < 4; dayOffset++)
        {
            var date = startDate.Date.AddDays(dayOffset);
            var events = await _calendarPolling.GetEventsForDateAsync(date, date.Date == DateTime.Today);
            importantEvents.AddRange(events.Where(ImportantCalendarEventPolicy.IsImportant));
        }

        return importantEvents
            .GroupBy(calendarEvent => string.IsNullOrWhiteSpace(calendarEvent.ExternalId)
                ? $"{calendarEvent.Summary}|{calendarEvent.StartTime:O}|{calendarEvent.EndTime:O}"
                : calendarEvent.ExternalId)
            .Select(group => group.First())
            .OrderBy(calendarEvent => calendarEvent.StartTime)
            .ToList();
    }

    private async Task SendDatabaseQuestionToGeminiAsync(string question)
    {
        try
        {
            CopilotStatusText.Text = "Gemini veritabanında arıyor...";

            var databaseContext = _db.BuildGeminiDatabaseContext(question, 150);
            var instruction = GeminiDatabaseSearchPrompt.Build(question, databaseContext);
            var response = await _gemini.SummarizeAsync(instruction, "Veritabanı arama sonucunu hazırla.");

            if (string.IsNullOrWhiteSpace(response) || IsGeminiStatus(response))
            {
                CopilotStatusText.Text = string.IsNullOrWhiteSpace(response) ? "Gemini veritabanı cevabı vermedi." : response;
                return;
            }

            _lastGeminiResponse = response;
            NoteInputText = AppendSection(NoteInputText, $"Gemini: {question}", response);
            CopilotStatusText.Text = "Gemini veritabanı sonucunu nota ekledi.";
        }
        catch (Exception ex)
        {
            CopilotStatusText.Text = $"Gemini veritabanı arama hatası: {ex.Message}";
        }
    }

    private async Task SendCurrentNoteToGeminiAsync(string heading)
    {
        var prompt = BuildCopilotPrompt();
        var hasFiles = _selectedFilePaths.Any(File.Exists);
        if (string.IsNullOrWhiteSpace(prompt) && string.IsNullOrWhiteSpace(_selectedImagePath) && !hasFiles)
        {
            CopilotStatusText.Text = "Gemini'ye gönderilecek not, resim veya dosya yok.";
            return;
        }

        try
        {
            CopilotStatusText.Text = "Gemini CLI çalışıyor...";
            var response = await _gemini.SummarizeAsync(BuildGeminiInstruction(), prompt, _selectedImagePath, _selectedFilePaths);

            if (IsGeminiStatus(response))
            {
                CopilotStatusText.Text = response;
                return;
            }

            var shouldExport = ShouldAutoExportGeminiToCsv();
            if (shouldExport && IsGeminiFileCapabilityRefusal(response))
            {
                _lastGeminiResponse = prompt;
                ExportTextToCsv(prompt);
                CopilotStatusText.Text = "Gemini tablo döndürmedi; not içeriği CSV olarak oluşturuldu.";
                return;
            }

            _lastGeminiResponse = response;
            NoteInputText = AppendSection(NoteInputText, heading, response);
            if (shouldExport)
                ExportTextToCsv(response);
            CopilotStatusText.Text = "Gemini cevabı nota eklendi.";
        }
        catch (Exception ex)
        {
            CopilotStatusText.Text = $"Gemini hatası: {ex.Message}";
        }
    }
    private static bool IsGeminiStatus(string response)
    {
        return response.StartsWith("Gemini CLI", StringComparison.OrdinalIgnoreCase)
            || response.StartsWith("Gemini'ye", StringComparison.OrdinalIgnoreCase);
    }

    private string BuildGeminiInstruction()
    {
        var instruction = GeminiPromptInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(instruction))
            instruction = DefaultGeminiInstruction;

        return ShouldAutoExportGeminiToCsv()
            ? BuildExcelExportInstruction(instruction)
            : instruction;
    }

    private static string BuildExcelExportInstruction(string instruction)
    {
        return instruction.Trim() + Environment.NewLine + Environment.NewLine
            + "ÖNEMLİ EXCEL/CSV KURALI:" + Environment.NewLine
            + "- Dosyayı sen oluşturma, kaydetme, write_file veya kabuk komutu kullanmaya çalışma." + Environment.NewLine
            + "- Excel/CSV dosyasını bu Windows uygulaması oluşturacak." + Environment.NewLine
            + "- Sen sadece tablo verisini döndür." + Environment.NewLine
            + "- Cevabı CSV gibi ver: ilk satır başlıklar, sonraki satırlar kayıtlar." + Environment.NewLine
            + "- Türkçe Excel için sütunları noktalı virgül (;) ile ayır." + Environment.NewLine
            + "- Açıklama, özür, markdown kod bloğu veya dosya oluşturma cümlesi ekleme.";
    }

    private string BuildGeminiHeading()
    {
        var instruction = GeminiPromptInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(instruction))
            return "Gemini özeti";

        return instruction.Length <= 60 ? "Gemini: " + instruction : "Gemini: " + instruction[..60] + "...";
    }
    private string BuildCopilotPrompt()
    {
        var title = TitleInput.Text.Trim();
        var body = NoteInputText.Trim();

        if (string.IsNullOrWhiteSpace(title))
            return body;

        return $"Başlık: {title}{Environment.NewLine}{Environment.NewLine}{body}".Trim();
    }

    private static string AppendSection(string currentText, string heading, string content)
    {
        var prefix = string.IsNullOrWhiteSpace(currentText) ? string.Empty : currentText.TrimEnd() + Environment.NewLine + Environment.NewLine;
        return prefix + $"--- {heading} ---" + Environment.NewLine + content.Trim();
    }

    private static string CleanSqlString(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var cleaned = input.Trim();
        if (cleaned.StartsWith("```", StringComparison.OrdinalIgnoreCase))
        {
            var lines = cleaned.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var sqlLines = lines.Where(l => !l.StartsWith("```")).ToArray();
            cleaned = string.Join(" ", sqlLines);
        }

        // Noktalı virgülü temizle
        cleaned = cleaned.Trim();
        if (cleaned.EndsWith(";"))
        {
            cleaned = cleaned.Substring(0, cleaned.Length - 1);
        }

        return cleaned.Trim();
    }

    private void NewButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsDirty())
        {
            SaveCurrentNoteSilent();
        }
        ResetEditor();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentNote();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        ShowFloatingRestoreIcon();
    }

    private void ShowFloatingRestoreIcon()
    {
        _floatingRestoreWindow ??= new FloatingRestoreWindow("Q", RestoreFromFloatingIcon);
        Hide();
        _floatingRestoreWindow.ShowIcon();
    }

    private void RestoreFromFloatingIcon()
    {
        WindowState = WindowState.Normal;
        Show();
        Activate();
        FocusInput();
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (MaximizeButton != null)
        {
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "\u25A1" : "\u25A2";
        }
    }

    private void OpenReviewButton_Click(object sender, RoutedEventArgs e)
    {
        ShowDailySummary(_selectedNoteDate ?? DateTime.Today);
    }

    private void EmailButton_Click(object sender, RoutedEventArgs e)
    {
        var subject = string.IsNullOrWhiteSpace(TitleInput.Text) ? "Hızlı Not" : TitleInput.Text.Trim();
        var body = NoteInputSelectedText;
        if (string.IsNullOrWhiteSpace(body))
            body = _lastGeminiResponse;
        if (string.IsNullOrWhiteSpace(body) || IsGeminiStatus(body))
            body = NoteInputText;

        if (string.IsNullOrWhiteSpace(body))
        {
            CopilotStatusText.Text = "E-posta ile gönderilecek içerik bulunamadı.";
            return;
        }

        if (EmailDialogService.TryShowEmailDialog(this, subject, body, out var recipient, out var finalSubject, out var useGmail))
        {
            try
            {
                if (useGmail)
                    EmailService.OpenInGmailWeb(recipient, finalSubject, body);
                else
                    EmailService.OpenInOutlookDesktop(recipient, finalSubject, body);
                
                CopilotStatusText.Text = "E-posta compose ekranı açıldı.";
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, "E-posta gönderme ekranı açılamadı:\n" + ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void EmailNoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetNoteFromCommandSender(sender, out var note))
            return;

        var subject = string.IsNullOrWhiteSpace(note.Title) ? "Hızlı Not" : note.Title;
        var body = FormatNoteForSharing(note);

        if (EmailDialogService.TryShowEmailDialog(this, subject, body, out var recipient, out var finalSubject, out var useGmail))
        {
            try
            {
                if (useGmail)
                    EmailService.OpenInGmailWeb(recipient, finalSubject, body);
                else
                    EmailService.OpenInOutlookDesktop(recipient, finalSubject, body);

                CopilotStatusText.Text = "E-posta compose ekranı açıldı.";
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, "E-posta gönderme ekranı açılamadı:\n" + ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void ModalEmailButton_Click(object sender, RoutedEventArgs e)
    {
        if (_modalNote == null)
            return;

        var note = _modalNote;
        var subject = string.IsNullOrWhiteSpace(note.Title) ? "Hızlı Not" : note.Title;
        var body = FormatNoteForSharing(note);

        if (EmailDialogService.TryShowEmailDialog(this, subject, body, out var recipient, out var finalSubject, out var useGmail))
        {
            try
            {
                if (useGmail)
                    EmailService.OpenInGmailWeb(recipient, finalSubject, body);
                else
                    EmailService.OpenInOutlookDesktop(recipient, finalSubject, body);

                CopilotStatusText.Text = "E-posta compose ekranı açıldı.";
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, "E-posta gönderme ekranı açılamadı:\n" + ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void GenerateTaskListButton_Click(object sender, RoutedEventArgs e)
    {
        CopilotStatusText.Text = "Günlük görev planı hazırlanıyor...";

        // 1. Check Google Login State
        var authState = _googleAuth.GetState();
        if (!authState.IsSignedIn || authState.IsExpired)
        {
            var loginResult = System.Windows.MessageBox.Show(
                this, 
                "Google Takvim etkinliklerini çekebilmek için Google Giriş yapmanız gerekmektedir. Åžimdi giriş yapmak istiyor musunuz?", 
                "Google Girişi Gerekli", 
                MessageBoxButton.YesNo, 
                MessageBoxImage.Question);

            if (loginResult == MessageBoxResult.Yes)
            {
                GoogleSignInButton_Click(sender, e);
            }
            else
            {
                CopilotStatusText.Text = "Google takvim etkinlikleri olmadan görev listesi oluşturuluyor...";
            }
        }

        try
        {
            var targetDate = _selectedNoteDate ?? DateTime.Today;
            // 2. Fetch Google Calendar Events for target date
            var events = (await _calendarPolling.GetEventsForDateAsync(targetDate)).ToList();

            // 3. Fetch target date's Notes
            var notes = _db.GetNotesForDate(targetDate);

            // 4. Fetch target date's Notifications (filtered)
            var notifications = _db.GetNotificationsForDate(targetDate)
                .Where(n => NotificationFilter.ShouldCapture(n.AppName, n.Title, n.Body))
                .ToList();

            // 5. Build Gemini Prompt
            var prompt = GeminiTaskListPrompt.Build(events, notes, notifications, targetDate);

            // 6. Call Gemini
            CopilotStatusText.Text = "Görev planı Gemini tarafından analiz ediliyor...";
            var taskListText = await _gemini.GenerateTaskListAsync(prompt);

            if (string.IsNullOrWhiteSpace(taskListText) || IsGeminiStatus(taskListText))
            {
                CopilotStatusText.Text = "Görev planı Gemini tarafından üretilemedi.";
                System.Windows.MessageBox.Show(this, "Gemini görev planı üretemedi veya hata verdi.", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            CopilotStatusText.Text = "Görev planı başarıyla üretildi.";
            
            // 7. Show Task List Dialog
            TaskListDialogService.ShowTaskListDialog(this, taskListText, targetDate, _db);
            
            // 8. Refresh notes to display the saved note if they saved it
            RefreshNotes();
        }
        catch (Exception ex)
        {
            CopilotStatusText.Text = "Görev planı hatası: " + ex.Message;
            System.Windows.MessageBox.Show(this, "Görev planı hazırlanırken bir hata oluştu:\n" + ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void GoogleSignInButton_Click(object sender, RoutedEventArgs e)
    {
        GoogleSignInButton.IsEnabled = false;
        CopilotStatusText.Text = "Google girisi icin tarayici aciliyor...";

        try
        {
            var result = await _googleAuth.SignInAsync();
            if (result.Success)
            {
                RefreshGoogleStatus();
                await _calendarPolling.GetTodayEventsAsync(forceRefresh: true);
                UpdateCalendarStatusUI();
                CopilotStatusText.Text = string.IsNullOrWhiteSpace(result.Email)
                    ? "Google hesabi baglandi."
                    : $"Google hesabi baglandi: {result.Email}";
                return;
            }

            CopilotStatusText.Text = "Google girisi tamamlanamadi: " + result.Error;
        }
        finally
        {
            GoogleSignInButton.IsEnabled = true;
            RefreshGoogleStatus();
            UpdateCalendarStatusUI();
        }
    }

    private void RefreshGoogleStatus()
    {
        if (GoogleSignInButton == null)
            return;

        var state = _googleAuth.GetState();
        GoogleSignInButton.Content = state.IsSignedIn && !state.IsExpired ? "Google Bagli" : "Google Giris";
        GoogleSignInButton.ToolTip = state.DisplayText;

        string displayName = "Obuzhukuk";
        if (state.IsSignedIn && !state.IsExpired)
        {
            if (!string.IsNullOrWhiteSpace(state.Name))
            {
                displayName = state.Name;
            }
            else if (!string.IsNullOrWhiteSpace(state.Email))
            {
                displayName = GoogleAuthService.GetNameFromEmail(state.Email);
            }
        }

        if (ProfileNameText != null)
        {
            ProfileNameText.Text = displayName;
        }

        if (ProfileAvatarText != null)
        {
            ProfileAvatarText.Text = string.IsNullOrWhiteSpace(displayName) ? "O" : displayName[0].ToString().ToUpper();
        }

        if (DashboardGreetingText != null)
        {
            DashboardGreetingText.Text = $"Merhaba, {displayName}";
        }
    }

    private void SaveCurrentNote()
    {
        var title = TitleInput.Text.Trim();
        var text = NoteInputText.Trim();
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(_selectedImagePath))
            return;

        var wasTitleEmpty = string.IsNullOrWhiteSpace(title);
        if (wasTitleEmpty)
        {
            title = CreateFallbackTitle(text, _selectedImagePath);
            TitleInput.Text = title;
        }

        int savedId;
        if (_editingNoteId.HasValue)
        {
            savedId = _editingNoteId.Value;
            _db.UpdateNote(savedId, title, text, _selectedImagePath, _editingNoteTags);
        }
        else
        {
            savedId = _db.AddNote(title, text, _selectedImagePath, _editingNoteTags);
            _editingNoteId = savedId;
            SaveButton.Content = "Güncelle";
            EditModeText.Text = $"Düzenleniyor: {DateTime.Now:dd.MM.yyyy HH:mm}";
        }

        // Reload from DB to capture auto-generated tags/title
        var savedNote = _db.GetNoteById(savedId);
        if (savedNote != null)
        {
            if (wasTitleEmpty)
            {
                TitleInput.Text = savedNote.Title;
                title = savedNote.Title;
            }
            _editingNoteTags.Clear();
            _editingNoteTags.AddRange(savedNote.TagList.Select(t => t.Name));
            RefreshTagsInEditor();
        }

        _originalTitle = title;
        _originalText = text;
        _originalImagePath = _selectedImagePath;
        _originalTags.Clear();
        _originalTags.AddRange(_editingNoteTags);

        RefreshNotes();
        RefreshTagFilterPanel();
        TitleInput.Focus();

        CopilotStatusText.Text = "Not kaydedildi.";
    }

    private void SaveCurrentNoteSilent()
    {
        var title = TitleInput.Text.Trim();
        var text = NoteInputText.Trim();
        
        if (!_editingNoteId.HasValue && string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(_selectedImagePath))
            return;

        var wasTitleEmpty = string.IsNullOrWhiteSpace(title);
        if (wasTitleEmpty)
        {
            title = CreateFallbackTitle(text, _selectedImagePath);
            Dispatcher.Invoke(() => TitleInput.Text = title);
        }

        int savedId;
        if (_editingNoteId.HasValue)
        {
            savedId = _editingNoteId.Value;
            _db.UpdateNote(savedId, title, text, _selectedImagePath, _editingNoteTags);
        }
        else
        {
            savedId = _db.AddNote(title, text, _selectedImagePath, _editingNoteTags);
            _editingNoteId = savedId;
            Dispatcher.Invoke(() =>
            {
                SaveButton.Content = "Güncelle";
                EditModeText.Text = $"Düzenleniyor: {DateTime.Now:dd.MM.yyyy HH:mm}";
            });
        }

        // Reload from DB to capture auto-generated tags/title
        var savedNote = _db.GetNoteById(savedId);
        if (savedNote != null)
        {
            if (wasTitleEmpty)
            {
                Dispatcher.Invoke(() => { TitleInput.Text = savedNote.Title; });
                title = savedNote.Title;
            }
            _editingNoteTags.Clear();
            _editingNoteTags.AddRange(savedNote.TagList.Select(t => t.Name));
            Dispatcher.Invoke(() => RefreshTagsInEditor());
        }

        _originalTitle = title;
        _originalText = text;
        _originalImagePath = _selectedImagePath;
        _originalTags.Clear();
        _originalTags.AddRange(_editingNoteTags);

        Dispatcher.Invoke(() =>
        {
            RefreshNotes();
            RefreshTagFilterPanel();
        });
    }

    private void RefreshNotes()
    {
        if (_isDailySummaryMode)
        {
            RefreshEmbeddedDailySummary(_selectedNoteDate ?? DateTime.Today);
            return;
        }

        NotesHeaderText.Text = "Notlar";
        NotesList.Visibility = Visibility.Visible;
        DailySummaryPanel.Visibility = Visibility.Collapsed;
        SearchInput.IsEnabled = true;
        BackToNotesButton.Visibility = Visibility.Collapsed;

        var query = SearchInput.Text.Trim();
        var notes = _selectedNoteDate.HasValue
            ? _db.GetNotesForDate(_selectedNoteDate.Value)
            : (_selectedTagFilter != null ? _db.GetNotesByTag(_selectedTagFilter) : _db.GetRecentNotes());

        if (_selectedTagFilter != null && _selectedNoteDate.HasValue)
        {
            notes = notes.Where(n => n.TagList.Any(t => t.Name.Equals(_selectedTagFilter, System.StringComparison.OrdinalIgnoreCase))).ToList();
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            notes = notes.Where(note => words.All(word => NoteMatches(note, word.TrimStart('#')))).ToList();
        }

        NotesList.ItemsSource = notes;
        NoteFilterText.Text = _selectedNoteDate.HasValue
            ? $"{_selectedNoteDate.Value:dd.MM.yyyy} - {notes.Count} not"
            : $"Son notlar - {notes.Count} not";
    }

    private void RefreshEmbeddedDailySummary(DateTime date)
    {
        var selectedDate = date.Date;
        _selectedNoteDate = selectedDate;

        NotesHeaderText.Text = "Gün Özeti";
        NoteFilterText.Text = $"{selectedDate:dd.MM.yyyy}";
        SearchInput.IsEnabled = true;
        NotesList.Visibility = Visibility.Collapsed;
        DailySummaryPanel.Visibility = Visibility.Visible;
        BackToNotesButton.Visibility = Visibility.Visible;

        var notes = _db.GetNotesForDate(selectedDate);
        var notifications = _db.GetNotificationsForDate(selectedDate)
            .Where(n => NotificationFilter.ShouldCapture(n.AppName, n.Title, n.Body))
            .OrderByDescending(n => n.ReceivedAt)
            .ToList();

        // Arama kutusundaki değere göre Gün Özeti içindeki notları ve bildirimleri filtrele
        var query = SearchInput.Text.Trim();
        if (!string.IsNullOrWhiteSpace(query))
        {
            notes = DailySummarySearch.FilterNotes(notes, query);
            notifications = DailySummarySearch.FilterNotifications(notifications, query);
        }

        DailySummaryDateText.Text = selectedDate.ToString("dd MMMM yyyy, dddd");
        DailySummaryNotesCountText.Text = $"{notes.Count} not";
        DailySummaryNotificationsCountText.Text = $"{notifications.Count} bildirim";
        DailySummaryNotesList.ItemsSource = notes;
        DailySummaryNotificationsList.ItemsSource = notifications;
    }

    private void SearchInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        RefreshNotes();
    }

    private void BackToNotesButton_Click(object sender, RoutedEventArgs e)
    {
        _isDailySummaryMode = false;
        RefreshNotes();
    }

    private async void TakvimButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            PanelimRadio.IsChecked = true;
            var targetDate = _selectedNoteDate ?? DateTime.Today;
            DashboardCalendar.SelectedDate = targetDate;
            DashboardCalendar.DisplayDate = targetDate;
            CopilotStatusText.Text = "Takvim aciliyor...";
            await LoadCalendarEventsForDateAsync(targetDate);
            CopilotStatusText.Text = $"Takvim gosteriliyor: {targetDate:dd.MM.yyyy}";
        }
        catch (Exception ex)
        {
            CopilotStatusText.Text = "Takvim acilamadi: " + ex.Message;
        }
    }
    private void CalendarTodayButton_Click(object sender, RoutedEventArgs e)
    {
        SetDateFilter(DateTime.Today);
    }

    private void DateFilterPicker_SelectedDateChanged(object? sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isChangingDateFilter)
            return;

        _selectedNoteDate = DateFilterPicker.SelectedDate?.Date;
        SearchInput.Text = string.Empty;
        RefreshNotes();
    }

    private void ClearDateFilterButton_Click(object sender, RoutedEventArgs e)
    {
        SetDateFilter(null);
    }

    private void SetDateFilter(DateTime? date)
    {
        _selectedNoteDate = date?.Date;
        _isChangingDateFilter = true;
        DateFilterPicker.SelectedDate = _selectedNoteDate;
        SearchInput.Text = string.Empty;
        _isChangingDateFilter = false;
        RefreshNotes();
    }

    private static bool NoteMatches(NoteItem note, string query)
    {
        return note.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || note.Text.Contains(query, StringComparison.OrdinalIgnoreCase)
            || note.Tags.Contains(query, StringComparison.OrdinalIgnoreCase)
            || note.CreatedAt.ToString("dd.MM.yyyy HH:mm").Contains(query, StringComparison.OrdinalIgnoreCase)
            || note.CreatedAt.ToString("yyyy-MM-dd").Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void CopyNoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetNoteFromCommandSender(sender, out var note))
            CopyNoteToClipboard(note);
    }

    private void ExportNoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetNoteFromCommandSender(sender, out var note))
            ExportNote(note);
    }

    private async void CalendarNoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetNoteFromCommandSender(sender, out var note))
            return;

        if (!CalendarEventTimeDialogService.TryAskCalendarTime(this, note, out var start, out var end))
            return;

        await AddNoteToGoogleCalendarAsync(note, start, end);
    }

    private void ReminderNoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetNoteFromCommandSender(sender, out var note))
            return;

        if (!TryAskReminderTime(note, out var reminderAt))
            return;

        _db.AddReminder(note, reminderAt);
        CopilotStatusText.Text = $"Hatırlatma eklendi: {reminderAt:dd.MM.yyyy HH:mm}";
        System.Windows.MessageBox.Show(this, $"Hatırlatma eklendi: {reminderAt:dd.MM.yyyy HH:mm}", "Hatırlatma", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static bool TryGetNoteFromCommandSender(object sender, out NoteItem note)
    {
        if (sender is FrameworkElement { DataContext: NoteItem directNote })
        {
            note = directNote;
            return true;
        }

        if (sender is MenuItem menuItem
            && menuItem.Parent is ContextMenu contextMenu
            && contextMenu.PlacementTarget is FrameworkElement { DataContext: NoteItem menuNote })
        {
            note = menuNote;
            return true;
        }

        note = null!;
        return false;
    }


    private async void OpenNoteInGoogleCalendar(NoteItem note)
    {
        if (!CalendarEventTimeDialogService.TryAskCalendarTime(this, note, out var start, out var end))
            return;

        await AddNoteToGoogleCalendarAsync(note, start, end);
    }

    private void AddReminderForNote(NoteItem note)
    {
        if (!TryAskReminderTime(note, out var reminderAt))
            return;

        _db.AddReminder(note, reminderAt);
        CopilotStatusText.Text = $"Hatırlatma eklendi: {reminderAt:dd.MM.yyyy HH:mm}";
        System.Windows.MessageBox.Show(this, $"Hatırlatma eklendi: {reminderAt:dd.MM.yyyy HH:mm}", "Hatırlatma", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private void CopySelectedNoteToClipboard()
    {
        if (NotesList.SelectedItem is NoteItem note)
            CopyNoteToClipboard(note);
    }

    private void CopyNoteToClipboard(NoteItem note)
    {
        System.Windows.Clipboard.SetText(FormatNoteForSharing(note));
        CopilotStatusText.Text = "Not panoya kopyalandı.";
    }

    private void ExportNote(NoteItem note)
    {
        var safeTitle = MakeSafeFileName(note.Title);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Notu dışa aktar",
            FileName = $"{safeTitle}-{note.CreatedAt:yyyyMMdd-HHmm}.txt",
            Filter = "Metin dosyası|*.txt|Tüm dosyalar|*.*"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        File.WriteAllText(dialog.FileName, FormatNoteForSharing(note), System.Text.Encoding.UTF8);
        CopilotStatusText.Text = "Not dışa aktarıldı.";
    }

    private static string FormatNoteForSharing(NoteItem note)
    {
        var lines = new List<string>
        {
            $"Başlık: {note.Title}",
            $"Tarih: {note.CreatedAt:dd.MM.yyyy HH:mm}",
            $"Etiketler: {note.Tags}",
            string.Empty,
            note.Text
        };

        if (!string.IsNullOrWhiteSpace(note.ImagePath))
        {
            lines.Add(string.Empty);
            lines.Add($"Resim: {note.ImagePath}");
        }

        return string.Join(Environment.NewLine, lines).TrimEnd();
    }


    private async Task AddNoteToGoogleCalendarAsync(NoteItem note, DateTime start, DateTime end)
    {
        CopilotStatusText.Text = "Google Takvim'e ekleniyor...";

        var result = await _googleAuth.CreateCalendarEventAsync(
            note.Title,
            FormatNoteForSharing(note),
            start,
            end);

        CopilotStatusText.Text = result.Success
            ? "Not Google Takvim'e eklendi."
            : result.Error ?? "Google Takvim'e eklenemedi.";
    }

    private bool TryAskReminderTime(NoteItem note, out DateTime reminderAt)
    {
        var selectedReminderAt = DateTime.Now.AddHours(1);
        reminderAt = selectedReminderAt;

        var theme = QuickNoteApp.Services.DialogTheme.Current();
        var window = new Window
        {
            Title = "Hat?rlatma ekle",
            Owner = this,
            Width = 360,
            Height = 390,
            MinWidth = 340,
            MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            Background = theme.BgDeepBrush
        };

        var datePicker = new DatePicker
        {
            SelectedDate = selectedReminderAt.Date,
            Margin = new Thickness(0, 4, 0, 10),
            Background = theme.BgInputBrush,
            Foreground = theme.TextPrimaryBrush,
            BorderBrush = theme.BorderSoftBrush
        };
        var timeBox = new System.Windows.Controls.TextBox
        {
            Text = selectedReminderAt.ToString("HH:mm"),
            Height = 34,
            Margin = new Thickness(0, 4, 0, 14),
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = theme.BgInputBrush,
            Foreground = theme.TextPrimaryBrush,
            BorderBrush = theme.BorderSoftBrush,
            CaretBrush = theme.AccentCyanBrush
        };
        var errorText = new TextBlock { Foreground = theme.ErrorBrush, TextWrapping = TextWrapping.Wrap, MinHeight = 20 };

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = note.Title, Foreground = theme.TextPrimaryBrush, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, MaxHeight = 56, TextTrimming = TextTrimming.CharacterEllipsis });
        panel.Children.Add(new TextBlock { Text = "Tarih", Foreground = theme.TextSecondaryBrush, Margin = new Thickness(0, 12, 0, 0) });
        panel.Children.Add(datePicker);
        panel.Children.Add(new TextBlock { Text = "Saat (?r. 14:30)", Foreground = theme.TextSecondaryBrush });
        panel.Children.Add(timeBox);
        panel.Children.Add(errorText);

        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var cancelButton = new System.Windows.Controls.Button { Content = "Vazgeç", MinWidth = 86, Margin = new Thickness(0, 0, 8, 0) };
        var okButton = new System.Windows.Controls.Button { Content = "Ekle", MinWidth = 86 };
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(okButton);
        panel.Children.Add(buttons);
        window.Content = new Border
        {
            Background = theme.BgPanelBrush,
            BorderBrush = theme.BorderSoftBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
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

            var selected = datePicker.SelectedDate.Value.Date.Add(time);
            if (selected <= DateTime.Now)
            {
                errorText.Text = "Hatırlatma zamanı ileri bir zaman olmalı.";
                return;
            }

            selectedReminderAt = selected;
            window.DialogResult = true;
        };

        var accepted = window.ShowDialog() == true;
        if (accepted)
            reminderAt = selectedReminderAt;

        return accepted;
    }
    private static bool ShouldCopySelectedNote(object originalSource)
    {
        return !IsInsideEditableTextBox(originalSource);
    }

    private static bool IsInsideEditableTextBox(object source)
    {
        if (source is not DependencyObject current)
            return false;

        while (current is not null)
        {
            if (current is System.Windows.Controls.TextBox || current is System.Windows.Controls.PasswordBox)
                return true;

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private static string MakeSafeFileName(string title)
    {
        var cleaned = string.IsNullOrWhiteSpace(title) ? "not" : title.Trim();
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
            cleaned = cleaned.Replace(invalidChar, '-');

        return cleaned.Length <= 50 ? cleaned : cleaned[..50];
    }

    private void EditNoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetNoteFromCommandSender(sender, out var note))
            LoadNoteForEditing(note);
    }

    private void DeleteNoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetNoteFromCommandSender(sender, out var note))
            return;

        var result = System.Windows.MessageBox.Show(
            this,
            $"'{note.Title}' notu silinsin mi?",
            "Notu sil",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        _db.DeleteNote(note.Id);

        if (_editingNoteId == note.Id)
            ResetEditor();

        RefreshNotes();
    }

    private void TogglePinNoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetNoteFromCommandSender(sender, out var note))
            return;

        _db.ToggleNotePin(note.Id);
        RefreshNotes();
    }


    private bool DeleteNote(NoteItem note)
    {
        var result = System.Windows.MessageBox.Show(
            this,
            $"'{note.Title}' notu silinsin mi?",
            "Notu sil",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return false;

        _db.DeleteNote(note.Id);

        if (_editingNoteId == note.Id)
            ResetEditor();

        RefreshNotes();
        return true;
    }
    private void NotesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (NotesList.SelectedItem is NoteItem note)
        {
            ShowNoteDetailsModal(note);
        }
    }

    private void ShowNoteDetailsModal(NoteItem note)
    {
        _modalNote = note;
        ModalTitle.Text = note.Title;
        ModalBody.Text = note.Text;

        if (!string.IsNullOrWhiteSpace(note.ImagePath) && File.Exists(note.ImagePath))
        {
            try
            {
                ModalImage.Source = new BitmapImage(new Uri(note.ImagePath));
                ModalImage.Visibility = Visibility.Visible;
            }
            catch
            {
                ModalImage.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            ModalImage.Visibility = Visibility.Collapsed;
        }

        NoteDetailsModal.Visibility = Visibility.Visible;
    }

    private void CloseModalButton_Click(object sender, RoutedEventArgs e)
    {
        NoteDetailsModal.Visibility = Visibility.Collapsed;
        _modalNote = null;
    }

    private void ModalCopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_modalNote is not null)
            CopyNoteToClipboard(_modalNote);
    }

    private void ModalExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_modalNote is not null)
            ExportNote(_modalNote);
    }

    private void ModalCalendarButton_Click(object sender, RoutedEventArgs e)
    {
        if (_modalNote is not null)
            OpenNoteInGoogleCalendar(_modalNote);
    }

    private void ModalReminderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_modalNote is not null)
            AddReminderForNote(_modalNote);
    }

    private void ModalEditButton_Click(object sender, RoutedEventArgs e)
    {
        if (_modalNote is null)
            return;

        var note = _modalNote;
        NoteDetailsModal.Visibility = Visibility.Collapsed;
        LoadNoteForEditing(note);
    }

    private void ModalDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_modalNote is null)
            return;

        var note = _modalNote;
        NoteDetailsModal.Visibility = Visibility.Collapsed;
        DeleteNote(note);
        _modalNote = null;
    }
    private void NoteInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (NoteInput == null) return;
        CheckNoteLinkSuggestions();
        TriggerAutoSaveTimer();
    }

    private void TitleInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        TriggerAutoSaveTimer();
    }

    private void TriggerAutoSaveTimer()
    {
        if (_isLoadingNote) return;
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    private void AutoSaveTimer_Tick(object? sender, EventArgs e)
    {
        _autoSaveTimer.Stop();
        if (IsDirty())
        {
            SaveCurrentNoteSilent();
        }
    }

    private bool IsDirty()
    {
        var title = TitleInput.Text.Trim();
        var text = NoteInputText.Trim();
        var imagePath = _selectedImagePath;

        if (!_editingNoteId.HasValue && string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(imagePath))
            return false;

        if (title != _originalTitle) return true;
        if (text != _originalText) return true;
        if (imagePath != _originalImagePath) return true;

        if (_editingNoteId.HasValue)
        {
            if (_editingNoteTags.Count != _originalTags.Count || !_editingNoteTags.All(_originalTags.Contains))
                return true;
        }
        else
        {
            if (_editingNoteTags.Count > 0)
                return true;
        }

        return false;
    }

    private void NoteResizeGrip_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        if (NoteInput == null) return;
        double newHeight = NoteInput.Height + e.VerticalChange;
        if (newHeight >= 90 && newHeight <= 300)
        {
            NoteInput.Height = newHeight;
        }
    }

    private void LoadNoteForEditing(NoteItem note)
    {
        if (_isLoadingNote)
            return;

        _isLoadingNote = true;
        try
        {
            _editingNoteId = note.Id;
            TitleInput.Text = note.Title;
            NoteInputText = note.Text;
            SetSelectedImage(note.ImagePath);
            _editingNoteTags.Clear();
            if (note.TagList != null)
            {
                _editingNoteTags.AddRange(note.TagList.Select(t => t.Name));
            }
            RefreshTagsInEditor();
            RefreshBacklinks(note);
            EditModeText.Text = $"Düzenleniyor: {note.CreatedAt:dd.MM.yyyy HH:mm}";
            SaveButton.Content = "Güncelle";
            TitleInput.Focus();

            _originalTitle = note.Title;
            _originalText = note.Text;
            _originalImagePath = note.ImagePath;
            _originalTags.Clear();
            _originalTags.AddRange(_editingNoteTags);
        }
        catch (Exception ex)
        {
            LogUiError("Not yüklenemedi", ex);
            System.Windows.MessageBox.Show(this, "Not açılırken bir hata oluştu. Uygulama kapanmadı; hata kaydı oluşturuldu.", "Not açılamadı", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _isLoadingNote = false;
        }
    }

    private void ResetEditor()
    {
        _editingNoteId = null;
        TitleInput.Text = string.Empty;
        NoteInputText = string.Empty;
        SetSelectedImage(null);
        _editingNoteTags.Clear();
        RefreshTagsInEditor();
        ClearBacklinksPanel();
        EditModeText.Text = "Yeni not";
        SaveButton.Content = "Kaydet";

        _originalTitle = string.Empty;
        _originalText = string.Empty;
        _originalImagePath = null;
        _originalTags.Clear();
    }

    private void RefreshAttachmentText()
    {
        if (SelectedImageText == null)
            return;

        var imageText = string.IsNullOrWhiteSpace(_selectedImagePath) ? "Resim yok" : Path.GetFileName(_selectedImagePath);
        var fileText = _selectedFilePaths.Count == 0
            ? "Dosya yok"
            : string.Join(", ", _selectedFilePaths.Select(Path.GetFileName));

        SelectedImageText.Text = imageText + " | " + fileText;
    }

    private bool ShouldAutoExportGeminiToCsv()
    {
        var instruction = GeminiPromptInput.Text.Trim();
        return instruction.Contains("excel", StringComparison.OrdinalIgnoreCase)
            || instruction.Contains("excele", StringComparison.OrdinalIgnoreCase)
            || instruction.Contains("csv", StringComparison.OrdinalIgnoreCase)
            || instruction.Contains("aktar", StringComparison.OrdinalIgnoreCase);
    }

    private void ExportTextToCsv(string content)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Excel için CSV olarak aktar",
            FileName = $"gemini-cevabi-{DateTime.Now:yyyyMMdd-HHmm}.csv",
            Filter = "CSV dosyası|*.csv|Tüm dosyalar|*.*"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        File.WriteAllText(dialog.FileName, ConvertTextToCsv(content), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        CopilotStatusText.Text = "CSV oluşturuldu. Excel ile açabilirsin.";
    }

    private static string ConvertTextToCsv(string content)
    {
        var cleanedContent = ExtractCsvReadyContent(content);
        var lines = cleanedContent.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var builder = new System.Text.StringBuilder();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || IsMarkdownTableSeparator(line))
                continue;

            var cells = SplitCsvLikeLine(line);
            builder.AppendLine(string.Join(";", cells.Select(EscapeCsv)));
        }

        if (builder.Length == 0 && !string.IsNullOrWhiteSpace(content))
            builder.AppendLine(EscapeCsv(content.Trim()));

        return builder.ToString();
    }

    private static string ExtractCsvReadyContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;

        var text = content.Trim();
        var fenceStart = text.IndexOf("```", StringComparison.Ordinal);
        if (fenceStart >= 0)
        {
            var contentStart = text.IndexOf('\n', fenceStart);
            var fenceEnd = contentStart >= 0 ? text.IndexOf("```", contentStart + 1, StringComparison.Ordinal) : -1;
            if (contentStart >= 0 && fenceEnd > contentStart)
                text = text[(contentStart + 1)..fenceEnd].Trim();
        }

        var lines = text.Replace("\r\n", "\n").Split('\n');
        var usefulLines = lines
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Where(line => !IsGeminiFileCapabilityRefusal(line))
            .ToList();

        return usefulLines.Count == 0 ? text : string.Join(Environment.NewLine, usefulLines);
    }

    private static string[] SplitCsvLikeLine(string line)
    {
        if (line.Contains('\t'))
            return line.Split('\t').Select(part => part.Trim()).ToArray();

        if (line.Contains('|'))
            return line.Trim('|').Split('|').Select(part => part.Trim()).Where(part => part.Length > 0).ToArray();

        if (line.Contains(';'))
            return line.Split(';').Select(part => part.Trim()).ToArray();

        if (line.Count(character => character == ',') >= 2)
            return line.Split(',').Select(part => part.Trim()).ToArray();

        return new[] { line };
    }

    private static bool IsMarkdownTableSeparator(string line)
    {
        var compact = line.Replace("|", string.Empty).Replace(":", string.Empty).Replace("-", string.Empty).Trim();
        return compact.Length == 0 && line.Contains('-');
    }

    private static bool IsGeminiFileCapabilityRefusal(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return ContainsAny(text,
            "write_file",
            "kabuk komutu",
            "dosya oluşturma veya yazma yeteneğim",
            "dosya yazma yeteneğim",
            "Excel'i doğrudan oluşturamam",
            "CSV dosyası oluşturabilirim",
            "output.csv",
            "Üzgünüm, dosya oluşturma",
            "kaydedemiyorum");
    }

    private static string EscapeCsv(string value)
    {
        var cleaned = value.Trim();
        return "\"" + cleaned.Replace("\"", "\"\"") + "\"";
    }

    private static bool ContainsAny(string text, params string[] needles)
    {
        return needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }
    private void SetSelectedImage(string? imagePath)
    {
        _selectedImagePath = imagePath;

        if (string.IsNullOrWhiteSpace(imagePath))
        {
            ImagePreview.Source = null;
            ImagePreviewPanel.Visibility = Visibility.Collapsed;
            RefreshAttachmentText();
            return;
        }

        ImagePreview.Source = LoadImage(imagePath);
        ImagePreviewPanel.Visibility = Visibility.Visible;
        RefreshAttachmentText();
    }

    private string CopyImageToAttachments(string sourcePath)
    {
        var extension = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(extension))
            extension = ".png";

        var targetPath = CreateAttachmentPath(extension);
        File.Copy(sourcePath, targetPath, overwrite: false);
        return targetPath;
    }

    private string CreateAttachmentPath(string extension)
    {
        var safeExtension = string.IsNullOrWhiteSpace(extension) ? ".png" : extension;
        return Path.Combine(_db.AttachmentsDirectory, $"note-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}{safeExtension}");
    }

    private static void SaveClipboardImage(BitmapSource image, string targetPath)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));

        using var fileStream = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        encoder.Save(fileStream);
    }

    private static BitmapImage LoadImage(string imagePath)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(imagePath);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static string CreateFallbackTitle(string text, string? imagePath)
    {
        var firstLine = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        if (!string.IsNullOrWhiteSpace(firstLine))
            return firstLine.Length <= 60 ? firstLine : firstLine[..60] + "...";

        return string.IsNullOrWhiteSpace(imagePath) ? "Başlıksız not" : "Resim notu";
    }

    private void NoteCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.CheckBox checkBox && checkBox.DataContext is NoteItem note)
            _db.SetNoteDone(note.Id, checkBox.IsChecked == true);
    }

    private void ToggleSidebarButton_Click(object sender, RoutedEventArgs e)
    {
        _isSidebarCollapsed = !_isSidebarCollapsed;

        SidebarColumn.Width = _isSidebarCollapsed ? new GridLength(0) : new GridLength(190);
        SidebarSeparatorColumn.Width = _isSidebarCollapsed ? new GridLength(0) : new GridLength(1);
        SidebarPanel.Visibility = _isSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        SidebarSeparator.Visibility = _isSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        MainContentPanel.Margin = _isSidebarCollapsed ? new Thickness(0) : new Thickness(14, 0, 0, 0);
        SidebarToggleButton.Content = "\uE700";
        SidebarToggleButton.ToolTip = _isSidebarCollapsed ? "Menüyü aç" : "Menüyü gizle";
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }
        DragMove();
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (NoteDetailsModal.Visibility == Visibility.Visible)
            {
                NoteDetailsModal.Visibility = Visibility.Collapsed;
            }
            else
            {
                Hide();
            }
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        if (_isDictating)
        {
            try { _audioRecorder.Stop(); } catch { }
            _audioRecorder.DiscardCurrentRecording();
            _isDictating = false;
        }

        DictateButton.Content = "Dikte";
        Hide();
    }

    public new void Hide()
    {
        _autoSaveTimer.Stop();
        if (IsDirty())
        {
            SaveCurrentNoteSilent();
        }
        base.Hide();
    }

    private async void DictateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isDictating)
        {
            string? audioPath = null;
            try
            {
                audioPath = _audioRecorder.Stop();
                _isDictating = false;
                DictateButton.IsEnabled = false;
                DictateButton.Content = "Gemini yazıyor...";
                CopilotStatusText.Text = "Ses Gemini Flash ile yazıya çevriliyor...";

                if (string.IsNullOrWhiteSpace(audioPath) || !File.Exists(audioPath))
                {
                    CopilotStatusText.Text = "Ses kaydı oluşturulamadı.";
                    return;
                }

                var transcription = await _gemini.TranscribeAudioAsync(audioPath);
                if (string.IsNullOrWhiteSpace(transcription) || IsGeminiStatus(transcription))
                {
                    CopilotStatusText.Text = string.IsNullOrWhiteSpace(transcription)
                        ? "Gemini sesten metin döndürmedi."
                        : transcription;
                    return;
                }

                AppendDictationText(DictationTextCleaner.Clean(transcription));
                CopilotStatusText.Text = "Ses yazıya çevrildi ve nota eklendi.";
            }
            catch (Exception ex)
            {
                CopilotStatusText.Text = $"Dikte hatası: {ex.Message}";
                System.Windows.MessageBox.Show(
                    $"Ses yazıya çevrilemedi:\n{ex.Message}",
                    "Dikte Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                _audioRecorder.DiscardCurrentRecording();
                DictateButton.IsEnabled = true;
                DictateButton.Content = "Dikte";
            }

            return;
        }

        try
        {
            _audioRecorder.Start();
            _isDictating = true;
            DictateButton.Content = "\u25CF Durdur";
            CopilotStatusText.Text = "Ses kaydediliyor...";
        }
        catch (Exception ex)
        {
            _isDictating = false;
            DictateButton.Content = "Dikte";
            CopilotStatusText.Text = $"Mikrofon başlatılamadı: {ex.Message}";
            System.Windows.MessageBox.Show(
                $"Mikrofon başlatılamadı:\n{ex.Message}\n\nWindows Ayarları > Gizlilik > Mikrofon bölümünden uygulamanın mikrofon erişimine izin verin.",
                "Mikrofon Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AppendDictationText(string text)
    {
        var cleanText = DictationTextCleaner.Clean(text);
        if (string.IsNullOrWhiteSpace(cleanText))
            return;

        NoteInput.TextChanged -= NoteInput_TextChanged;
        try
        {
            var currentText = NoteInputText;
            var separator = string.IsNullOrWhiteSpace(currentText) ? string.Empty : Environment.NewLine;
            NoteInputText = currentText + separator + cleanText;
            NoteInput.CaretPosition = NoteInput.Document.ContentEnd;
        }
        finally
        {
            NoteInput.TextChanged += NoteInput_TextChanged;
        }

        QuickNoteApp.Services.MarkdownHighlighter.Highlight(NoteInput);
        CheckNoteLinkSuggestions();
    }

    private const int WM_NCHITTEST = 0x0084;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;
    private const int resizeBorderWidth = 8;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwndSource = PresentationSource.FromVisual(this) as System.Windows.Interop.HwndSource;
        if (hwndSource != null)
        {
            hwndSource.AddHook(ResizeHook);
        }
    }

    private IntPtr ResizeHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NCHITTEST)
        {
            var x = lParam.ToInt32() & 0xffff;
            var y = lParam.ToInt32() >> 16;
            var clientPoint = PointFromScreen(new System.Windows.Point(x, y));

            if (clientPoint.Y <= resizeBorderWidth)
            {
                if (clientPoint.X <= resizeBorderWidth) { handled = true; return new IntPtr(HTTOPLEFT); }
                if (clientPoint.X >= ActualWidth - resizeBorderWidth) { handled = true; return new IntPtr(HTTOPRIGHT); }
                handled = true; return new IntPtr(HTTOP);
            }
            if (clientPoint.Y >= ActualHeight - resizeBorderWidth)
            {
                if (clientPoint.X <= resizeBorderWidth) { handled = true; return new IntPtr(HTBOTTOMLEFT); }
                if (clientPoint.X >= ActualWidth - resizeBorderWidth) { handled = true; return new IntPtr(HTBOTTOMRIGHT); }
                handled = true; return new IntPtr(HTBOTTOM);
            }
            if (clientPoint.X <= resizeBorderWidth) { handled = true; return new IntPtr(HTLEFT); }
            if (clientPoint.X >= ActualWidth - resizeBorderWidth) { handled = true; return new IntPtr(HTRIGHT); }
        }
        
        return IntPtr.Zero;
    }

    // =========================================================================
    // TAG MANAGEMENT CODE-BEHIND METHODS
    // =========================================================================

    private void RefreshTagsInEditor()
    {
        var tb = TagInputTextBox;
        if (SelectedTagsPanel == null || tb == null) return;
        SelectedTagsPanel.Children.Clear();
        
        foreach (var tag in _editingNoteTags)
        {
            var border = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#6C63FF")),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 6, 4),
                VerticalAlignment = VerticalAlignment.Center
            };
            
            var dock = new DockPanel();
            
            var textBlock = new TextBlock
            {
                Text = "#" + tag,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            
            var removeButton = new System.Windows.Controls.Button
            {
                Content = "Ã—",
                Foreground = System.Windows.Media.Brushes.LightGray,
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Margin = new Thickness(4, -2, 0, 0),
                Width = 12,
                Height = 16,
                Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Sil"
            };
            
            var btnStyle = new Style(typeof(System.Windows.Controls.Button));
            btnStyle.Setters.Add(new Setter(System.Windows.Controls.Button.BackgroundProperty, System.Windows.Media.Brushes.Transparent));
            btnStyle.Setters.Add(new Setter(System.Windows.Controls.Button.BorderThicknessProperty, new Thickness(0)));
            btnStyle.Setters.Add(new Setter(System.Windows.Controls.Button.PaddingProperty, new Thickness(0)));
            btnStyle.Setters.Add(new Setter(System.Windows.Controls.Button.MarginProperty, new Thickness(4, -2, 0, 0)));
            btnStyle.Setters.Add(new Setter(System.Windows.Controls.Button.MinWidthProperty, 0.0));
            btnStyle.Setters.Add(new Setter(System.Windows.Controls.Button.HeightProperty, 16.0));
            removeButton.Style = btnStyle;
            
            string currentTag = tag;
            removeButton.Click += (s, e) =>
            {
                _editingNoteTags.Remove(currentTag);
                RefreshTagsInEditor();
            };
            
            DockPanel.SetDock(removeButton, Dock.Right);
            dock.Children.Add(removeButton);
            dock.Children.Add(textBlock);
            border.Child = dock;
            
            SelectedTagsPanel.Children.Add(border);
        }
        
        SelectedTagsPanel.Children.Add(tb);
    }

    private void RefreshTagFilterPanel()
    {
        if (TagFilterPanel == null || ClearTagFilterButton == null) return;
        var clearBtn = ClearTagFilterButton;
        TagFilterPanel.Children.Clear();
        TagFilterPanel.Children.Add(clearBtn);

        try
        {
            var tags = _db.GetAllTags();
            foreach (var tag in tags)
            {
                var btn = new System.Windows.Controls.Button
                {
                    Content = "#" + tag.Name,
                    Margin = new Thickness(0, 0, 6, 0),
                    Padding = new Thickness(10, 3, 10, 3),
                    Height = 26,
                    FontSize = 11,
                    Tag = tag.Name,
                    Style = (Style)FindResource("TagFilterButtonStyle")
                };

                if (_selectedTagFilter == tag.Name)
                {
                    btn.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8B5CF6"));
                    btn.Foreground = System.Windows.Media.Brushes.White;
                }

                string currentTagName = tag.Name;
                btn.Click += (s, e) =>
                {
                    if (_selectedTagFilter == currentTagName)
                        _selectedTagFilter = null;
                    else
                        _selectedTagFilter = currentTagName;

                    RefreshTagFilterPanel();
                    RefreshNotes();
                };

                TagFilterPanel.Children.Add(btn);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Etiketler yüklenirken hata oluştu: {ex.Message}");
        }
    }

    private void TagInputTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var text = TagInputTextBox.Text;
        if (text.Contains(" ") || text.Contains(",") || text.Contains(";"))
        {
            var parts = text.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var clean = part.Trim().TrimStart('#');
                if (!string.IsNullOrWhiteSpace(clean) && !_editingNoteTags.Contains(clean))
                {
                    _editingNoteTags.Add(clean);
                }
            }
            TagInputTextBox.Text = string.Empty;
            RefreshTagsInEditor();
            TagInputTextBox.Focus();
            return;
        }

        // Show suggestions
        if (text.Length > 0)
        {
            var cleanText = text.TrimStart('#');
            var allTags = _db.GetAllTags();
            var matched = allTags
                .Where(t => t.Name.Contains(cleanText, StringComparison.OrdinalIgnoreCase) && !_editingNoteTags.Contains(t.Name))
                .ToList();

            if (matched.Count > 0)
            {
                TagSuggestionList.ItemsSource = matched;
                TagSuggestionPopup.IsOpen = true;
            }
            else
            {
                TagSuggestionPopup.IsOpen = false;
            }
        }
        else
        {
            TagSuggestionPopup.IsOpen = false;
        }
    }

    private void TagInputTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            var text = TagInputTextBox.Text.Trim().TrimStart('#');
            if (!string.IsNullOrWhiteSpace(text) && !_editingNoteTags.Contains(text))
            {
                _editingNoteTags.Add(text);
                TagInputTextBox.Text = string.Empty;
                RefreshTagsInEditor();
                TagInputTextBox.Focus();
                e.Handled = true;
            }
        }
        else if (e.Key == System.Windows.Input.Key.Back && string.IsNullOrEmpty(TagInputTextBox.Text) && _editingNoteTags.Count > 0)
        {
            _editingNoteTags.RemoveAt(_editingNoteTags.Count - 1);
            RefreshTagsInEditor();
            TagInputTextBox.Focus();
            e.Handled = true;
        }
    }

    private void TagSuggestionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TagSuggestionList.SelectedItem is NoteTag selectedTag)
        {
            if (!_editingNoteTags.Contains(selectedTag.Name))
            {
                _editingNoteTags.Add(selectedTag.Name);
                TagInputTextBox.Text = string.Empty;
                RefreshTagsInEditor();
                TagInputTextBox.Focus();
            }
            TagSuggestionPopup.IsOpen = false;
            TagSuggestionList.SelectedItem = null;
        }
    }

    private void ClearTagFilterButton_Click(object sender, RoutedEventArgs e)
    {
        _selectedTagFilter = null;
        RefreshTagFilterPanel();
        RefreshNotes();
    }
    // =========================================================================
    // MARKDOWN FORMATTING TOOLBAR CLICK HANDLERS
    // =========================================================================

    private void BoldButton_Click(object sender, RoutedEventArgs e)
    {
        if (NoteInput == null) return;
        System.Windows.Documents.EditingCommands.ToggleBold.Execute(null, NoteInput);
        NoteInput.Focus();
    }

    private void ItalicButton_Click(object sender, RoutedEventArgs e)
    {
        if (NoteInput == null) return;
        System.Windows.Documents.EditingCommands.ToggleItalic.Execute(null, NoteInput);
        NoteInput.Focus();
    }

    private void CodeButton_Click(object sender, RoutedEventArgs e)
    {
        if (NoteInput == null) return;
        var selection = NoteInput.Selection;
        if (!selection.IsEmpty)
        {
            var currentFamily = selection.GetPropertyValue(TextElement.FontFamilyProperty);
            if (currentFamily is System.Windows.Media.FontFamily f && f.Source == "Consolas")
            {
                selection.ApplyPropertyValue(TextElement.FontFamilyProperty, DependencyProperty.UnsetValue);
                selection.ApplyPropertyValue(TextElement.BackgroundProperty, DependencyProperty.UnsetValue);
                selection.ApplyPropertyValue(TextElement.ForegroundProperty, DependencyProperty.UnsetValue);
            }
            else
            {
                selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new System.Windows.Media.FontFamily("Consolas"));
                selection.ApplyPropertyValue(TextElement.BackgroundProperty, new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E1B4B")));
                selection.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F43F5E")));
            }
        }
        NoteInput.Focus();
    }

    private void HeaderButton_Click(object sender, RoutedEventArgs e)
    {
        if (NoteInput == null) return;
        var caret = NoteInput.CaretPosition;
        var p = caret.Paragraph;
        if (p != null)
        {
            var range = new System.Windows.Documents.TextRange(p.ContentStart, p.ContentStart);
            range.Text = "# ";
        }
        NoteInput.Focus();
    }

    private void ListButton_Click(object sender, RoutedEventArgs e)
    {
        if (NoteInput == null) return;
        var caret = NoteInput.CaretPosition;
        var p = caret.Paragraph;
        if (p != null)
        {
            var range = new System.Windows.Documents.TextRange(p.ContentStart, p.ContentStart);
            range.Text = "- ";
        }
        NoteInput.Focus();
    }

    
    // =========================================================================
    // BACKLINKS / NOTE LINKING METHODS
    // =========================================================================

    private void RefreshBacklinks(NoteItem note)
    {
        if (BacklinksList == null || NoBacklinksText == null) return;
        try
        {
            var backlinks = _db.GetBacklinksForNote(note.Title);
            BacklinksList.ItemsSource = backlinks;
            if (backlinks.Count == 0)
            {
                NoBacklinksText.Visibility = Visibility.Visible;
                BacklinksList.Visibility = Visibility.Collapsed;
            }
            else
            {
                NoBacklinksText.Visibility = Visibility.Collapsed;
                BacklinksList.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Geri bağlantılar yüklenirken hata: {ex.Message}");
        }
    }

    private void ClearBacklinksPanel()
    {
        if (BacklinksList == null || NoBacklinksText == null) return;
        BacklinksList.ItemsSource = null;
        NoBacklinksText.Visibility = Visibility.Collapsed;
        BacklinksList.Visibility = Visibility.Collapsed;
    }

    private void BacklinksList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BacklinksList == null) return;
        if (BacklinksList.SelectedItem is NoteItem selectedNote)
        {
            LoadNoteForEditing(selectedNote);
            BacklinksList.SelectedItem = null;
        }
    }

    private void NoteInput_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (NoteInput == null) return;
        var position = NoteInput.GetPositionFromPoint(e.GetPosition(NoteInput), true);
        if (position == null) return;

        var p = position.Paragraph;
        if (p == null) return;

        var paragraphRange = new System.Windows.Documents.TextRange(p.ContentStart, p.ContentEnd);
        string text = paragraphRange.Text;
        int clickOffset = new System.Windows.Documents.TextRange(p.ContentStart, position).Text.Length;

        var match = Regex.Matches(text, @"\[\[(.*?)\]\]")
            .Cast<Match>()
            .FirstOrDefault(m => clickOffset >= m.Index && clickOffset <= m.Index + m.Length);

        if (match != null)
        {
            var noteTitle = match.Groups[1].Value.Trim();
            var targetNote = _db.GetNoteByTitle(noteTitle);
            if (targetNote != null)
            {
                LoadNoteForEditing(targetNote);
                e.Handled = true;
            }
            else
            {
                // Create new linked note
                TitleInput.Text = noteTitle;
                NoteInputText = string.Empty;
                _editingNoteId = null;
                _editingNoteTags.Clear();
                RefreshTagsInEditor();
                ClearBacklinksPanel();
                EditModeText.Text = "Yeni bağlantılı not";
                SaveButton.Content = "Kaydet";
                NoteInput.Focus();
                e.Handled = true;
            }
        }
    }

    private void CheckNoteLinkSuggestions()
    {
        if (NoteInput == null || NoteLinkSuggestionPopup == null) return;
        var caret = NoteInput.CaretPosition;
        var p = caret.Paragraph;
        if (p != null)
        {
            var range = new System.Windows.Documents.TextRange(p.ContentStart, caret);
            string textBeforeCaret = range.Text;
            var match = Regex.Match(textBeforeCaret, @"\[\[([^\]]*)$");
            if (match.Success)
            {
                var query = match.Groups[1].Value.Trim();
                ShowNoteLinkSuggestions(query);
                return;
            }
        }
        NoteLinkSuggestionPopup.IsOpen = false;
    }

    private void ShowNoteLinkSuggestions(string query)
    {
        try
        {
            var notes = _db.GetRecentNotes();
            var matched = notes
                .Where(n => n.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matched.Count > 0)
            {
                NoteLinkSuggestionList.ItemsSource = matched;
                NoteLinkSuggestionPopup.IsOpen = true;
            }
            else
            {
                NoteLinkSuggestionPopup.IsOpen = false;
            }
        }
        catch
        {
            NoteLinkSuggestionPopup.IsOpen = false;
        }
    }

    private void NoteLinkSuggestionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NoteLinkSuggestionList == null || NoteInput == null) return;
        if (NoteLinkSuggestionList.SelectedItem is NoteItem selectedNote)
        {
            var caret = NoteInput.CaretPosition;
            var p = caret.Paragraph;
            if (p != null)
            {
                var range = new System.Windows.Documents.TextRange(p.ContentStart, caret);
                string textBeforeCaret = range.Text;
                var match = Regex.Match(textBeforeCaret, @"\[\[([^\]]*)$");
                if (match.Success)
                {
                    int matchCharIndex = match.Index;
                    var startPointer = GetPointerAtOffsetWithinParagraph(p, matchCharIndex);
                    if (startPointer != null)
                    {
                        var replaceRange = new System.Windows.Documents.TextRange(startPointer, caret);
                        replaceRange.Text = "[[" + selectedNote.Title + "]] ";
                        NoteInput.CaretPosition = replaceRange.End;
                    }
                }
            }
            NoteLinkSuggestionPopup.IsOpen = false;
            NoteLinkSuggestionList.SelectedItem = null;
            NoteInput.Focus();
        }
    }

    private TextPointer? GetPointerAtOffsetWithinParagraph(Paragraph p, int offset)
    {
        TextPointer start = p.ContentStart;
        int remaining = offset;
        while (start != null && remaining > 0)
        {
            if (start.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                string textRun = start.GetTextInRun(LogicalDirection.Forward);
                if (textRun.Length >= remaining)
                {
                    return start.GetPositionAtOffset(remaining, LogicalDirection.Forward);
                }
                remaining -= textRun.Length;
            }
            var next = start.GetNextContextPosition(LogicalDirection.Forward);
            if (next == null || next.CompareTo(p.ContentEnd) > 0) break;
            start = next;
        }
        return start;
    }
    // =========================================================================
    // WYSIWYG MARKDOWN CONVERTERS
    // =========================================================================

    private string FlowDocumentToMarkdown(FlowDocument doc)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var block in doc.Blocks)
        {
            if (block is Paragraph p)
            {
                var lineSb = new System.Text.StringBuilder();
                bool isHeader = p.FontSize >= 16.0;
                if (isHeader)
                {
                    lineSb.Append("## ");
                }

                foreach (var inline in p.Inlines)
                {
                    if (inline is Run run)
                    {
                        var text = run.Text;
                        if (string.IsNullOrEmpty(text)) continue;

                        bool isBold = run.FontWeight == FontWeights.Bold;
                        bool isItalic = run.FontStyle == FontStyles.Italic;
                        bool isCode = run.FontFamily.Source == "Consolas";

                        if (text.StartsWith("[[") && text.EndsWith("]]"))
                        {
                            lineSb.Append(text);
                        }
                        else
                        {
                            if (isBold && isItalic)
                                lineSb.Append("***" + text + "***");
                            else if (isBold)
                                lineSb.Append("**" + text + "**");
                            else if (isItalic)
                                lineSb.Append("*" + text + "*");
                            else if (isCode)
                                lineSb.Append("`" + text + "`");
                            else
                                lineSb.Append(text);
                        }
                    }
                    else if (inline is LineBreak)
                    {
                        lineSb.AppendLine();
                    }
                }
                
                string lineText = lineSb.ToString();
                if (!string.IsNullOrEmpty(lineText))
                {
                    sb.AppendLine(lineText);
                }
            }
            else if (block is List list)
            {
                foreach (var item in list.ListItems)
                {
                    var lineSb = new System.Text.StringBuilder();
                    lineSb.Append("- ");
                    
                    foreach (var blockInItem in item.Blocks)
                    {
                        if (blockInItem is Paragraph pInItem)
                        {
                            foreach (var inline in pInItem.Inlines)
                            {
                                if (inline is Run run)
                                {
                                    var text = run.Text;
                                    if (string.IsNullOrEmpty(text)) continue;

                                    bool isBold = run.FontWeight == FontWeights.Bold;
                                    bool isItalic = run.FontStyle == FontStyles.Italic;
                                    bool isCode = run.FontFamily.Source == "Consolas";

                                    if (text.StartsWith("[[") && text.EndsWith("]]"))
                                    {
                                        lineSb.Append(text);
                                    }
                                    else
                                    {
                                        if (isBold && isItalic)
                                            lineSb.Append("***" + text + "***");
                                        else if (isBold)
                                            lineSb.Append("**" + text + "**");
                                        else if (isItalic)
                                            lineSb.Append("*" + text + "*");
                                        else if (isCode)
                                            lineSb.Append("`" + text + "`");
                                        else
                                            lineSb.Append(text);
                                    }
                                }
                                else if (inline is LineBreak)
                                {
                                    lineSb.AppendLine();
                                }
                            }
                        }
                    }
                    sb.AppendLine(lineSb.ToString());
                }
            }
        }
        return sb.ToString().TrimEnd('\r', '\n');
    }

    private FlowDocument MarkdownToFlowDocument(string markdown)
    {
        var doc = new FlowDocument();
        if (string.IsNullOrEmpty(markdown))
        {
            var p = new Paragraph(new Run());
            doc.Blocks.Add(p);
            return doc;
        }

        var lines = markdown.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        foreach (var line in lines)
        {
            var p = new Paragraph();
            string text = line;
            
            if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                var list = new List { MarkerStyle = TextMarkerStyle.Disc };
                var item = new ListItem();
                var pListItem = new Paragraph();
                text = line.Substring(2);
                ParseLineToParagraph(text, pListItem);
                item.Blocks.Add(pListItem);
                list.ListItems.Add(item);
                doc.Blocks.Add(list);
                continue;
            }

            if (line.StartsWith("## "))
            {
                p.FontSize = 18.0;
                p.FontWeight = FontWeights.Bold;
                p.Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#C084FC"));
                text = line.Substring(3);
            }
            else if (line.StartsWith("# "))
            {
                p.FontSize = 20.0;
                p.FontWeight = FontWeights.Bold;
                p.Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#C084FC"));
                text = line.Substring(2);
            }

            ParseLineToParagraph(text, p);
            doc.Blocks.Add(p);
        }
        return doc;
    }

    private void ParseLineToParagraph(string line, Paragraph p)
    {
        var pattern = @"(\[\[.*?\]\]|\*\*\*.*?\*\*\*|\*\*.*?\*\*|\*.*?\*|`.*?`)";
        var parts = Regex.Split(line, pattern);

        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part)) continue;

            if (part.StartsWith("[[") && part.EndsWith("]]"))
            {
                var run = new Run(part)
                {
                    Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#00D4FF")),
                    FontWeight = FontWeights.SemiBold
                };
                run.TextDecorations = TextDecorations.Underline;
                p.Inlines.Add(run);
            }
            else if (part.StartsWith("***") && part.EndsWith("***"))
            {
                var run = new Run(part.Substring(3, part.Length - 6))
                {
                    FontWeight = FontWeights.Bold,
                    FontStyle = FontStyles.Italic
                };
                p.Inlines.Add(run);
            }
            else if (part.StartsWith("**") && part.EndsWith("**"))
            {
                var run = new Run(part.Substring(2, part.Length - 4))
                {
                    FontWeight = FontWeights.Bold
                };
                p.Inlines.Add(run);
            }
            else if (part.StartsWith("*") && part.EndsWith("*"))
            {
                var run = new Run(part.Substring(1, part.Length - 2))
                {
                    FontStyle = FontStyles.Italic
                };
                p.Inlines.Add(run);
            }
            else if (part.StartsWith("`") && part.EndsWith("`"))
            {
                var run = new Run(part.Substring(1, part.Length - 2))
                {
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E1B4B")),
                    Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F43F5E"))
                };
                p.Inlines.Add(run);
            }
            else
            {
                p.Inlines.Add(new Run(part));
            }
        }
    }

    // Sidebar view switching
    private void SidebarButton_Checked(object sender, RoutedEventArgs e)
    {
        if (DashboardView == null || NotesView == null || SettingsView == null) return;
        if (PanelimRadio.IsChecked == true)
            SwitchView("Dashboard");
        else if (TumNotlarRadio.IsChecked == true)
            SwitchView("Notes");
        else if (AyarlarRadio.IsChecked == true)
            SwitchView("Settings");
    }

    private void SwitchView(string viewName)
    {
        DashboardView.Visibility = (viewName == "Dashboard") ? Visibility.Visible : Visibility.Collapsed;
        NotesView.Visibility = (viewName == "Notes") ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = (viewName == "Settings") ? Visibility.Visible : Visibility.Collapsed;

        if (viewName == "Dashboard")
        {
            LoadDashboardData();
        UpdateGeminiCliStatusUI();
        }
        else if (viewName == "Settings")
        {
            UpdateNotificationStatusUI();
            UpdateCalendarStatusUI();
        }
    }

    // Dashboard data population
    private async void LoadDashboardData()
    {
        try
        {
            // 1. Recent Notes
            var notes = _db.GetRecentNotes();
            DashboardRecentNotes.ItemsSource = notes.Take(2).ToList();

            // 2. Today's Focus (Pending Notes/Tasks)
            DashboardFocusTasksPanel.Children.Clear();
            var activeNotes = notes.Where(n => !n.IsDone).Take(3).ToList();
            if (activeNotes.Count == 0)
            {
                DashboardFocusTasksPanel.Children.Add(new TextBlock
                {
                    Text = "Bugün için odaklanacak bekleyen görev veya not bulunmuyor.",
                    Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextSecondaryBrush"],
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 8, 0, 0),
                    FontSize = 12.5
                });
            }
            else
            {
                foreach (var note in activeNotes)
                {
                    var sp = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
                    var cb = new System.Windows.Controls.CheckBox { IsChecked = false, Margin = new Thickness(0, 0, 8, 0), Tag = note };
                    cb.Checked += (s, ev) =>
                    {
                        var n = (NoteItem)((System.Windows.Controls.CheckBox)s).Tag;
                        n.IsDone = true;
                        _db.UpdateNote(n.Id, n.Title, n.Text, n.ImagePath, n.TagList.Select(t => t.Name).ToList());
                        LoadDashboardData();
        UpdateGeminiCliStatusUI();
                        RefreshNotes();
                    };
                    var tb = new TextBlock
                    {
                        Text = note.Title,
                        Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextPrimaryBrush"],
                        FontSize = 13,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    sp.Children.Add(cb);
                    sp.Children.Add(tb);
                    DashboardFocusTasksPanel.Children.Add(sp);
                }
            }

            // 3. Mini Calendar and Events
            var selectedDate = DashboardCalendar.SelectedDate ?? DateTime.Today;
            await LoadCalendarEventsForDateAsync(selectedDate);

            // 4. Upcoming Reminders
            DashboardRemindersPanel.Children.Clear();
            var upcomingReminders = _db.GetUpcomingReminders(4);
            if (upcomingReminders.Count == 0)
            {
                DashboardRemindersPanel.Children.Add(new TextBlock
                {
                    Text = "Yaklaşan hatırlatıcı bulunmuyor.",
                    Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextSecondaryBrush"],
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 8, 0, 0),
                    FontSize = 12
                });
            }
            else
            {
                foreach (var rem in upcomingReminders)
                {
                    var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
                    var titleTb = new TextBlock
                    {
                        Text = rem.Title,
                        Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextPrimaryBrush"],
                        FontSize = 12.5,
                        FontWeight = FontWeights.SemiBold
                    };
                    var dateTb = new TextBlock
                    {
                        Text = rem.ReminderAt.ToString("dd MMMM yyyy, HH:mm"),
                        Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextMutedBrush"],
                        FontSize = 10.5,
                        Margin = new Thickness(0, 2, 0, 0)
                    };
                    sp.Children.Add(titleTb);
                    sp.Children.Add(dateTb);
                    DashboardRemindersPanel.Children.Add(sp);
                }
            }
        }
        catch (Exception)
        {
            // Silently swallow dashboard population exceptions
        }
    }

    private async Task LoadCalendarEventsForDateAsync(DateTime date)
    {
        try
        {
            DashboardCalendarEventsPanel.Children.Clear();
            var events = await _calendarPolling.GetEventsForDateAsync(date);
            if (events == null || events.Count == 0)
            {
                DashboardCalendarEventsPanel.Children.Add(new TextBlock
                {
                    Text = date.Date == DateTime.Today ? "Bugün için takvim etkinliği bulunmuyor." : $"{date:dd.MM.yyyy} için takvim etkinliği bulunmuyor.",
                    Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextSecondaryBrush"],
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 8, 0, 0),
                    FontSize = 12
                });
            }
            else
            {
                foreach (var ev in events)
                {
                    var border = new Border
                    {
                        Background = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["BgCardBrush"],
                        BorderBrush = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["AccentPurpleBrush"],
                        BorderThickness = new Thickness(3, 0, 0, 0),
                        Padding = new Thickness(8, 6, 8, 6),
                        Margin = new Thickness(0, 0, 0, 8),
                        CornerRadius = new CornerRadius(0, 4, 4, 0)
                    };
                    var sp = new StackPanel();
                    var timeTb = new TextBlock
                    {
                        Text = ev.IsAllDay ? "Tum gun" : ev.StartTime.ToString("HH:mm") + " - " + ev.EndTime.ToString("HH:mm"),
                        Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextSecondaryBrush"],
                        FontSize = 11,
                        FontWeight = FontWeights.Bold
                    };
                    var descTb = new TextBlock
                    {
                        Text = ev.Summary,
                        Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextPrimaryBrush"],
                        FontSize = 12,
                        FontWeight = FontWeights.Medium,
                        TextWrapping = TextWrapping.Wrap
                    };
                    sp.Children.Add(timeTb);
                    sp.Children.Add(descTb);

                    if (!string.IsNullOrWhiteSpace(ev.Location))
                    {
                        var locTb = new TextBlock
                        {
                            Text = "Konum: " + ev.Location.Trim(),
                            Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextMutedBrush"],
                            FontSize = 11,
                            Margin = new Thickness(0, 2, 0, 0),
                            TextWrapping = TextWrapping.Wrap
                        };
                        sp.Children.Add(locTb);
                    }

                    if (!string.IsNullOrWhiteSpace(ev.Description))
                    {
                        var infoTb = new TextBlock
                        {
                            Text = ev.Description.Trim(),
                            Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextSecondaryBrush"],
                            FontSize = 11,
                            Margin = new Thickness(0, 4, 0, 0),
                            FontStyle = FontStyles.Italic,
                            TextWrapping = TextWrapping.Wrap
                        };
                        sp.Children.Add(infoTb);
                    }

                    border.Child = sp;
                    DashboardCalendarEventsPanel.Children.Add(border);
                }
            }
        }
        catch (Exception ex)
        {
            DashboardCalendarEventsPanel.Children.Add(new TextBlock
            {
                Text = "Takvim yüklenemedi: " + ex.Message,
                Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextSecondaryBrush"],
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 8, 0, 0),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });
        }
    }

    // Dashboard Actions
    private void DashboardNewNoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsDirty())
        {
            SaveCurrentNoteSilent();
        }
        ResetEditor();
        TumNotlarRadio.IsChecked = true;
    }

    private void DashboardSeeAllNotes_Click(object sender, RoutedEventArgs e)
    {
        TumNotlarRadio.IsChecked = true;
    }

    private void DashboardRecentNoteCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is NoteItem note)
        {
            LoadNoteForEditing(note);
            TumNotlarRadio.IsChecked = true;
        }
    }

    private void DashboardTodayCalendar_Click(object sender, RoutedEventArgs e)
    {
        DashboardCalendar.SelectedDate = DateTime.Today;
        DashboardCalendar.DisplayDate = DateTime.Today;
    }

    private async void DashboardCalendar_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DashboardCalendar.SelectedDate.HasValue)
        {
            var date = DashboardCalendar.SelectedDate.Value;
            _selectedNoteDate = date;
            _isChangingDateFilter = true;
            DateFilterPicker.SelectedDate = date;
            _isChangingDateFilter = false;
            RefreshNotes();

            await LoadCalendarEventsForDateAsync(date);
        }
    }

    private void DashboardSuggest_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn)
        {
            var prompt = btn.Content?.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(prompt))
                return;

            DashboardGeminiInput.Text = prompt;
            ExecuteDashboardGemini(prompt);
        }
    }

    private void DashboardGeminiInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ExecuteDashboardGemini(DashboardGeminiInput.Text);
        }
    }

    private void DashboardGeminiSend_Click(object sender, RoutedEventArgs e)
    {
        ExecuteDashboardGemini(DashboardGeminiInput.Text);
    }

    private async void ExecuteDashboardGemini(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return;
        
        // Show status loading
        DashboardGeminiInput.IsEnabled = false;
        
        try
        {
            // Switch to notes view to display results/interact
            TumNotlarRadio.IsChecked = true;
            GeminiPromptInput.Text = prompt;
            await SendGeminiPromptAsync();
        }
        finally
        {
            DashboardGeminiInput.IsEnabled = true;
            DashboardGeminiInput.Text = "";
        }
    }

    // Settings Theme switching
    private void ThemeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (DarkThemeRadio == null || LightThemeRadio == null) return;
        
        bool isLight = LightThemeRadio.IsChecked == true;
        ApplyTheme(isLight);
    }

    private void LightThemeButton_Click(object sender, RoutedEventArgs e)
    {
        if (LightThemeRadio != null)
            LightThemeRadio.IsChecked = true;

        ApplyTheme(true);
    }

    private void DarkThemeButton_Click(object sender, RoutedEventArgs e)
    {
        if (DarkThemeRadio != null)
            DarkThemeRadio.IsChecked = true;

        ApplyTheme(false);
    }

    private async void RequestNotificationAccessButton_Click(object sender, RoutedEventArgs e)
    {
        NotificationStatusText.Text = "Windows bildirim izni isteniyor...";
        await _notificationListener.EnsureAccessAndStartAsync();
        UpdateNotificationStatusUI();
    }
    private void UpdateNotificationStatusUI()
    {
        if (_notificationListener == null) return;

        var status = _notificationListener.StatusText;
        if (NotificationStatusText != null)
            NotificationStatusText.Text = status;

        if (NotificationStatusDot != null)
        {
            if (status.Contains("aktif", StringComparison.OrdinalIgnoreCase) && !status.Contains("yedek", StringComparison.OrdinalIgnoreCase))
            {
                NotificationStatusDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94));
            }
            else if (status.Contains("yedek", StringComparison.OrdinalIgnoreCase))
            {
                NotificationStatusDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(234, 179, 8));
            }
            else
            {
                NotificationStatusDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));
            }
        }

        try
        {
            var today = DateTime.Today;
            var todayNotifs = _db.GetNotificationsForDate(today)
                .Where(n => NotificationFilter.ShouldCapture(n.AppName, n.Title, n.Body))
                .ToList();

            if (CapturedTodayCountText != null)
                CapturedTodayCountText.Text = $"{todayNotifs.Count} adet";

            var recentNotifs = _db.GetRecentNotifications(1)
                .Where(n => NotificationFilter.ShouldCapture(n.AppName, n.Title, n.Body))
                .ToList();

            if (LastNotificationTimeText != null)
            {
                if (recentNotifs.Count > 0)
                {
                    LastNotificationTimeText.Text = recentNotifs[0].ReceivedAt.ToString("dd.MM.yyyy HH:mm") + $" ({recentNotifs[0].AppName})";
                }
                else
                {
                    LastNotificationTimeText.Text = "Henüz bildirim yok";
                }
            }
        }
        catch
        {
            if (CapturedTodayCountText != null)
                CapturedTodayCountText.Text = "Bilinmiyor";
            if (LastNotificationTimeText != null)
                LastNotificationTimeText.Text = "Bilinmiyor";
        }
    }

    private void UpdateCalendarStatusUI()
    {
        if (_calendarPolling == null) return;

        var status = _calendarPolling.StatusText;
        if (CalendarStatusText != null)
            CalendarStatusText.Text = status;

        if (CalendarStatusDot != null)
        {
            var authState = _googleAuth.GetState();
            if (authState.IsSignedIn && !authState.IsExpired)
            {
                CalendarStatusDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94));
            }
            else if (authState.IsSignedIn && authState.IsExpired)
            {
                CalendarStatusDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(234, 179, 8));
            }
            else
            {
                CalendarStatusDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));
            }
        }

        if (LastCalendarSyncTimeText != null)
        {
            var lastRefresh = _calendarPolling.LastRefreshAt;
            LastCalendarSyncTimeText.Text = lastRefresh.HasValue
                ? lastRefresh.Value.ToString("dd.MM.yyyy HH:mm")
                : "Henüz senkronize edilmedi";
        }

        if (CalendarEventsCountText != null)
        {
            var eventsCount = _calendarPolling.TodayEvents?.Count ?? 0;
            CalendarEventsCountText.Text = $"{eventsCount} adet";
        }
    }

    private async void SyncCalendarButton_Click(object sender, RoutedEventArgs e)
    {
        if (_calendarPolling == null) return;
        CopilotStatusText.Text = "Google Takvim senkronize ediliyor...";
        try
        {
            await _calendarPolling.GetTodayEventsAsync(forceRefresh: true);
            UpdateCalendarStatusUI();
            CopilotStatusText.Text = "Google Takvim senkronizasyonu tamamlandı.";
        }
        catch (Exception ex)
        {
            CopilotStatusText.Text = "Google Takvim senkronizasyon hatası: " + ex.Message;
        }
    }

    private void CleanOldNotificationsButton_Click(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            this,
            "Veritabanındaki filtrelere uymayan tüm eski bildirimler kalıcı olarak silinecektir. Devam etmek istiyor musunuz?",
            "Eski Bildirimleri Temizle",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                var deletedCount = _db.CleanUnallowedNotifications();
                System.Windows.MessageBox.Show(
                    this,
                    $"Temizleme tamamlandı. Toplam {deletedCount} adet gereksiz/filtre dışı bildirim veritabanından temizlendi.",
                    "Başarılı",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                
                UpdateNotificationStatusUI();
                RefreshNotes();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, "Temizleme sırasında bir hata oluştu:\n" + ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public static void ApplyTheme(bool isLight)
    {
        ApplyThemeToResources(System.Windows.Application.Current.Resources, isLight);
    }

    public static void ApplyThemeToResources(ResourceDictionary resources, bool isLight)
    {
        try
        {
            if (isLight)
            {
                SetThemeColor(resources, "BgDeep", "#F4F7FB");
                SetThemeColor(resources, "BgPanel", "#FFFFFF");
                SetThemeColor(resources, "BgCard", "#F8FAFC");
                SetThemeColor(resources, "BgInput", "#FFFFFF");
                SetThemeColor(resources, "BorderSoft", "#DDE5F0");
                SetThemeColor(resources, "BorderHover", "#B9C5D6");
                SetThemeColor(resources, "TextPrimary", "#172033");
                SetThemeColor(resources, "TextSecondary", "#46556A");
                SetThemeColor(resources, "TextMuted", "#748198");
                SetThemeColor(resources, "AccentPurple", "#6D5CFF");
                SetThemeColor(resources, "AccentCyan", "#0EA5E9");

                SetThemeBrush(resources, "BgDeepBrush", "#F4F7FB");
                SetThemeBrush(resources, "BgPanelBrush", "#FFFFFF");
                SetThemeBrush(resources, "BgCardBrush", "#F8FAFC");
                SetThemeBrush(resources, "BgInputBrush", "#FFFFFF");
                SetThemeBrush(resources, "BorderSoftBrush", "#DDE5F0");
                SetThemeBrush(resources, "BorderHoverBrush", "#B9C5D6");
                SetThemeBrush(resources, "TextPrimaryBrush", "#172033");
                SetThemeBrush(resources, "TextSecondaryBrush", "#46556A");
                SetThemeBrush(resources, "TextMutedBrush", "#748198");
                SetThemeBrush(resources, "AccentPurpleBrush", "#6D5CFF");
                SetThemeBrush(resources, "AccentCyanBrush", "#0EA5E9");
                SetThemeBrush(resources, "ButtonBrush", "#FFFFFF");
                SetThemeBrush(resources, "ButtonHoverBrush", "#EEF4FF");
                SetThemeBrush(resources, "ButtonPressedBrush", "#E1EAFA");
                SetThemeBrush(resources, "NavHoverBrush", "#EEF4FF");
                SetThemeBrush(resources, "NavSelectedBrush", "#E7EEFF");
                SetThemeBrush(resources, "NoteCardBrush", "#FFFFFF");
                SetThemeBrush(resources, "EditorSurfaceBrush", "#FFFFFF");
                SetThemeBrush(resources, "EditorToolbarBrush", "#F6F8FC");
                SetThemeBrush(resources, "CommandCenterBrush", "#F7FAFF");
                SetThemeBrush(resources, "SubtleSurfaceBrush", "#FFFFFF");
                SetThemeBrush(resources, "DividerBrush", "#E2E8F0");
                SetThemeBrush(resources, "WarningPanelBrush", "#FFF7E6");
                SetThemeBrush(resources, "WarningBorderBrush", "#F5D08A");
                SetThemeBrush(resources, "TimeBadgeBrush", "#EEF4FF");
            }
            else
            {
                SetThemeColor(resources, "BgDeep", "#080B10");
                SetThemeColor(resources, "BgPanel", "#0F141D");
                SetThemeColor(resources, "BgCard", "#151B27");
                SetThemeColor(resources, "BgInput", "#0D121B");
                SetThemeColor(resources, "BorderSoft", "#242B38");
                SetThemeColor(resources, "BorderHover", "#3A4354");
                SetThemeColor(resources, "TextPrimary", "#F5F7FB");
                SetThemeColor(resources, "TextSecondary", "#AAB4C5");
                SetThemeColor(resources, "TextMuted", "#778196");
                SetThemeColor(resources, "AccentPurple", "#8B6CFF");
                SetThemeColor(resources, "AccentCyan", "#3ABDF6");

                SetThemeBrush(resources, "BgDeepBrush", "#080B10");
                SetThemeBrush(resources, "BgPanelBrush", "#0F141D");
                SetThemeBrush(resources, "BgCardBrush", "#151B27");
                SetThemeBrush(resources, "BgInputBrush", "#0D121B");
                SetThemeBrush(resources, "BorderSoftBrush", "#242B38");
                SetThemeBrush(resources, "BorderHoverBrush", "#3A4354");
                SetThemeBrush(resources, "TextPrimaryBrush", "#F5F7FB");
                SetThemeBrush(resources, "TextSecondaryBrush", "#AAB4C5");
                SetThemeBrush(resources, "TextMutedBrush", "#778196");
                SetThemeBrush(resources, "AccentPurpleBrush", "#8B6CFF");
                SetThemeBrush(resources, "AccentCyanBrush", "#3ABDF6");
                SetThemeBrush(resources, "ButtonBrush", "#151B28");
                SetThemeBrush(resources, "ButtonHoverBrush", "#202737");
                SetThemeBrush(resources, "ButtonPressedBrush", "#2B3348");
                SetThemeBrush(resources, "NavHoverBrush", "#161C2B");
                SetThemeBrush(resources, "NavSelectedBrush", "#1A2232");
                SetThemeBrush(resources, "NoteCardBrush", "#101722");
                SetThemeBrush(resources, "EditorSurfaceBrush", "#0F141E");
                SetThemeBrush(resources, "EditorToolbarBrush", "#111722");
                SetThemeBrush(resources, "CommandCenterBrush", "#0B1018");
                SetThemeBrush(resources, "SubtleSurfaceBrush", "#0F1621");
                SetThemeBrush(resources, "DividerBrush", "#202838");
                SetThemeBrush(resources, "WarningPanelBrush", "#1C1A14");
                SetThemeBrush(resources, "WarningBorderBrush", "#3D3520");
                SetThemeBrush(resources, "TimeBadgeBrush", "#1A1D28");
            }
        }
        catch (Exception ex)
        {
            LogUiError("Tema de?i?tirilemedi", ex);
        }
    }

    private static void SetThemeColor(ResourceDictionary resources, string key, string colorHex)
    {
        var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex);
        if (resources.Contains(key))
        {
            resources[key] = color;
            return;
        }

        resources.Add(key, color);
    }

    private static void SetThemeBrush(ResourceDictionary resources, string key, string colorHex)
    {
        var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex);
        resources[key] = new SolidColorBrush(color);
    }

    private void NotesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingNote)
            return;

        if (IsDirty())
        {
            SaveCurrentNoteSilent();
        }

        if (NotesList.SelectedItem is NoteItem note)
        {
            LoadNoteForEditing(note);
        }
    }

    private void SetNoteInputText(string? value)
    {
        if (NoteInput == null)
            return;

        NoteInput.TextChanged -= NoteInput_TextChanged;
        try
        {
            NoteInput.Document = MarkdownToFlowDocument(value ?? string.Empty);
        }
        finally
        {
            NoteInput.TextChanged += NoteInput_TextChanged;
        }
    }

    private static void LogUiError(string title, Exception ex)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "QuickNoteApp");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "ui-errors.log");
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {title}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Hata kaydı yazılamazsa uygulama akışını bozmayalım.
        }
    }

    private void CloseDailySummaryButton_Click(object sender, RoutedEventArgs e)
    {
        _isDailySummaryMode = false;
        DailySummaryPanel.Visibility = Visibility.Collapsed;
        RefreshNotes();
    }
}





