using QuickNoteApp.Models;
using QuickNoteApp.Services;
using QuickNoteApp.Windows;
using System.Windows;
using System.Windows.Media;

var tests = new (string Name, Action Test)[]
{
    ("Outlook bildirimi kabul edilir", () =>
    {
        AssertTrue(NotificationFilter.ShouldCapture("Outlook", "Konu", "Mesaj"));
    }),
    ("Yeni Outlook paket bildirimi kabul edilir", () =>
    {
        AssertTrue(NotificationFilter.ShouldCapture("Microsoft.OutlookForWindows_8wekyb3d8bbwe!Microsoft.OutlookforWindows", "Konu", "Mesaj"));
    }),
    ("WhatsApp bildirimi kabul edilir", () =>
    {
        AssertTrue(NotificationFilter.ShouldCapture("WhatsApp", "Ali", "Merhaba"));
    }),
    ("Chrome uzerinden gelen WhatsApp kabul edilir", () =>
    {
        AssertTrue(NotificationFilter.ShouldCapture("Google Chrome", "WhatsApp", "Ali: Merhaba"));
    }),
    ("Chrome uzerinden gelen bos WhatsApp basligi kabul edilmez", () =>
    {
        AssertFalse(NotificationFilter.ShouldCapture("Google Chrome", "WhatsApp", ""));
    }),
    ("Gmail bildirimi kabul edilmez", () =>
    {
        AssertFalse(NotificationFilter.ShouldCapture("Gmail", "Konu", "Mesaj"));
    }),
    ("Thunderbird bildirimi kabul edilmez", () =>
    {
        AssertFalse(NotificationFilter.ShouldCapture("Thunderbird", "Konu", "Mesaj"));
    }),
    ("Windows guvenlik bildirimi kabul edilmez", () =>
    {
        AssertFalse(NotificationFilter.ShouldCapture("Windows Security", "Tehditler bulundu", "Microsoft Defender"));
    }),
    ("Bilinmeyen uygulama bildirimi kabul edilmez", () =>
    {
        AssertFalse(NotificationFilter.ShouldCapture("Bilinmeyen uygulama", "", ""));
    }),
    ("Bos WhatsApp bildirimi kabul edilmez", () =>
    {
        AssertFalse(NotificationFilter.ShouldCapture("WhatsApp", "", ""));
    }),
    ("Sadece uygulama adi olan WhatsApp bildirimi kabul edilmez", () =>
    {
        AssertFalse(NotificationFilter.ShouldCapture("WhatsApp", "WhatsApp", ""));
    }),
    ("Gemini arama baglami not ve bildirimleri listeler", () =>
    {
        var notes = new[]
        {
            new NoteItem
            {
                Id = 7,
                Title = "Teklif",
                Text = "Ahmet ile fiyat konusuldu.",
                Tags = "#teklif",
                CreatedAt = new DateTime(2026, 7, 4, 10, 30, 0)
            }
        };

        var notifications = new[]
        {
            new NotificationLogItem
            {
                Id = 3,
                AppName = "WhatsApp",
                Title = "Ahmet",
                Body = "Bugun donus yapacagim.",
                ReceivedAt = new DateTime(2026, 7, 4, 11, 0, 0)
            }
        };

        var context = SearchContextFormatter.Format(notes, notifications);

        AssertContains(context, "Notlar");
        AssertContains(context, "Bildirimler");
        AssertContains(context, "Teklif");
        AssertContains(context, "WhatsApp");
    }),
    ("Gemini veritabani arama talimati soru ve kayitlari icerir", () =>
    {
        var instruction = GeminiDatabaseSearchPrompt.Build("Omer 3 Temmuzda kac mail atmis?", "Bildirimler:\n- Outlook | Omer");

        AssertContains(instruction, "Omer 3 Temmuzda kac mail atmis?");
        AssertContains(instruction, "Outlook");
        AssertContains(instruction, "yerel veritabanı");
    }),
    ("Gemini takvim arama talimati Google Takvim ve yerel verileri icerir", () =>
    {
        var instruction = GeminiCalendarSearchPrompt.Build(
            "Bugun ne toplantim var?",
            "Takvim ozeti:\n- [09:00] Haftalik Toplanti",
            "Bildirimler:\n- WhatsApp | Ahmet");

        AssertContains(instruction, "Bugun ne toplantim var?");
        AssertContains(instruction, "Google Takvim Verileri");
        AssertContains(instruction, "Yerel Veritabanı Kayıtları");
        AssertContains(instruction, "Haftalik Toplanti");
        AssertContains(instruction, "Ahmet");
    }),
    ("Gemini mail sayisi sorusunu veritabani aramasi sayar", () =>
    {
        AssertTrue(GeminiSearchIntent.ShouldUseDatabase("Omer Tamdogan 3 Temmuzda kac mail atmis?"));
    }),
    ("Gemini toplam tutar sorusunu veritabani aramasi sayar", () =>
    {
        AssertTrue(GeminiSearchIntent.ShouldUseDatabase("toplam tutar ne kadar?"));
    }),
    ("Gemini normal ozet istegini veritabani aramasi saymaz", () =>
    {
        AssertFalse(GeminiSearchIntent.ShouldUseDatabase("Bu metni kisaca ozetle"));
    }),
    ("Gemini veritabani ozet sorusunu not ozeti saymaz", () =>
    {
        AssertTrue(GeminiSearchIntent.ShouldUseDatabase("Veritabanindaki kayitlari ozetle"));
        AssertTrue(GeminiSearchIntent.ShouldUseDatabase("Notlarimda Omer ile ilgili mail kayitlarini ozetle"));
    }),
    ("Gun ozeti aramasi bildirim basligi ve govdesinde arar", () =>
    {
        var notifications = new[]
        {
            new NotificationLogItem
            {
                Id = 10,
                AppName = "Outlook",
                Title = "Omer Tamdogan",
                Body = "3 Temmuz toplantisi icin mail atti.",
                ReceivedAt = new DateTime(2026, 7, 3, 9, 15, 0)
            },
            new NotificationLogItem
            {
                Id = 11,
                AppName = "WhatsApp",
                Title = "Ayse",
                Body = "Baska mesaj.",
                ReceivedAt = new DateTime(2026, 7, 3, 10, 0, 0)
            }
        };

        var result = DailySummarySearch.FilterNotifications(notifications, "Omer mail");

        AssertEqual(1, result.Count);
        AssertEqual(10, result[0].Id);
    }),
    ("FTS arama sorgusu Turkce karakterleri normalize eder", () =>
    {
        var query = SqliteFtsSearch.BuildMatchQuery("Ömer Tamdoğan mail");

        AssertEqual("\"omer\"* AND \"tamdogan\"* AND \"mail\"*", query);
    }),
    ("FTS arama sorgusu dogal dil soru kelimelerini ayiklar", () =>
    {
        var query = SqliteFtsSearch.BuildMatchQuery("Ömer Tamdoğan 3 Temmuzda kaç mail atmış?");

        AssertEqual("\"omer\"* AND \"tamdogan\"* AND \"temmuz\"* AND \"mail\"*", query);
    }),
    ("FTS arama sorgusu bos metinde bos doner", () =>
    {
        var query = SqliteFtsSearch.BuildMatchQuery("   ");

        AssertEqual(string.Empty, query);
    }),
    ("Google Calendar Push public webhook olmadan kurulamaz", () =>
    {
        var plan = GoogleCalendarPushSetupPlan.Create(null);

        AssertFalse(plan.CanEnablePush);
        AssertContains(plan.Message, "public HTTPS webhook");
    }),
    ("Google Calendar Push webhook adresiyle kurulabilir gorunur", () =>
    {
        var plan = GoogleCalendarPushSetupPlan.Create("https://quicknote.example.com/google/calendar/push");

        AssertTrue(plan.CanEnablePush);
        AssertContains(plan.CallbackUrl!, "https://");
    }),
    ("Takvim yoklama ilk acilista hemen calismali", () =>
    {
        var state = CalendarPollingState.Create(TimeSpan.FromMinutes(15));

        AssertTrue(state.ShouldRefresh(DateTimeOffset.Now));
    }),
    ("Takvim yoklama aralik dolmadan tekrar calismamali", () =>
    {
        var now = DateTimeOffset.Now;
        var state = CalendarPollingState.Create(TimeSpan.FromMinutes(15));
        state.MarkRefreshed(now);

        AssertFalse(state.ShouldRefresh(now.AddMinutes(10)));
    }),
    ("Takvim yoklama aralik dolunca tekrar calismali", () =>
    {
        var now = DateTimeOffset.Now;
        var state = CalendarPollingState.Create(TimeSpan.FromMinutes(15));
        state.MarkRefreshed(now);

        AssertTrue(state.ShouldRefresh(now.AddMinutes(16)));
    }),
    ("Takvim niyeti ozetleme ifadesinde de calismali", () =>
    {
        AssertTrue(GeminiSearchIntent.ShouldUseCalendar("Takvimimi ozetle"));
        AssertTrue(GeminiSearchIntent.ShouldUseCalendar("Yarinki toplantilarimi ozetle"));
    }),
    ("Gemini gun planlama istegini takvim aramasi sayar", () =>
    {
        AssertTrue(GeminiSearchIntent.ShouldUseCalendar("gunumu planla"));
        AssertTrue(GeminiSearchIntent.ShouldUseCalendar("bugunumu takvimime gore planla"));
        AssertTrue(GeminiSearchIntent.ShouldUseCalendar("yarinimi planla"));
    }),
    ("Onemli takvim etkinligi icin reminder zamani uretilmeli", () =>
    {
        var calendarEvent = new CalendarEvent
        {
            Summary = "Mahkeme durusmasi",
            StartTime = new DateTime(2026, 7, 8, 14, 0, 0),
            EndTime = new DateTime(2026, 7, 8, 15, 0, 0)
        };

        var reminderAt = ImportantCalendarEventPolicy.GetReminderTime(calendarEvent);

        AssertTrue(reminderAt.HasValue);
        AssertEqual(new DateTime(2026, 7, 8, 12, 0, 0), reminderAt!.Value);
    }),
    ("Gemini gorev planlama promptu takvim verisine gore gun plani ister", () =>
    {
        var events = new List<CalendarEvent>
        {
            new CalendarEvent
            {
                Summary = "Musteri toplantisi",
                StartTime = new DateTime(2026, 7, 9, 10, 0, 0),
                EndTime = new DateTime(2026, 7, 9, 11, 0, 0)
            }
        };

        var prompt = GeminiTaskListPrompt.Build(events, new List<NoteItem>(), new List<NotificationLogItem>(), new DateTime(2026, 7, 9));

        AssertContains(prompt, "Takvim");
        AssertContains(prompt, "Musteri toplantisi");
        AssertContains(prompt, "gun plani");
    }),
    ("Takvim ozeti secili gun ve yaklasan onemli etkinlikleri icermeli", () =>
    {
        var selectedDate = new DateTime(2026, 7, 8);
        var selectedEvents = new[]
        {
            new CalendarEvent
            {
                Summary = "Musteri toplantisi",
                StartTime = new DateTime(2026, 7, 8, 10, 0, 0),
                EndTime = new DateTime(2026, 7, 8, 11, 0, 0)
            }
        };
        var upcomingImportantEvents = new[]
        {
            new CalendarEvent
            {
                Summary = "Mahkeme durusmasi",
                StartTime = new DateTime(2026, 7, 9, 9, 0, 0),
                EndTime = new DateTime(2026, 7, 9, 10, 0, 0)
            }
        };

        var summary = CalendarSummaryBuilder.Build(selectedDate, selectedEvents, upcomingImportantEvents);

        AssertContains(summary, "08.07.2026");
        AssertContains(summary, "Musteri toplantisi");
        AssertContains(summary, "Mahkeme durusmasi");
    }),
    ("Takvim reminder sync ayni etkinligi tek kayit tutmali", () =>
    {
        var dbPath = CreateTempDatabasePath();
        var db = new DatabaseService(dbPath);
        db.Initialize();

        var calendarEvent = new CalendarEvent
        {
            ExternalId = "evt-1",
            Summary = "Mahkeme durusmasi",
            StartTime = new DateTime(2026, 7, 10, 9, 0, 0),
            EndTime = new DateTime(2026, 7, 10, 10, 0, 0)
        };

        db.UpsertCalendarReminder(calendarEvent, new DateTime(2026, 7, 10, 7, 0, 0));
        db.UpsertCalendarReminder(calendarEvent, new DateTime(2026, 7, 10, 8, 0, 0));

        var reminders = db.GetUpcomingReminders(10);

        AssertEqual(1, reminders.Count);
        AssertEqual(new DateTime(2026, 7, 10, 8, 0, 0), reminders[0].ReminderAt);
    }),
    ("Ayni Outlook bildirimi ayni zaman damgasi ile tekrar kaydolmamali", () =>
    {
        var dbPath = CreateTempDatabasePath();
        var db = new DatabaseService(dbPath);
        db.Initialize();

        var receivedAt = new DateTime(2026, 7, 8, 9, 35, 0);
        db.AddNotification("Outlook", "Cansu", "RE: Son 1 yillik tespit edilemeyen ode", receivedAt);
        db.AddNotification("Outlook", "Cansu", "RE: Son 1 yillik tespit edilemeyen ode", receivedAt);

        var notifications = db.GetRecentNotifications(10);

        AssertEqual(1, notifications.Count);
    }),
    ("Veritabani FTS not aramasi dogal dil sorusunda kaydi bulur", () =>
    {
        var dbPath = CreateTempDatabasePath();
        var db = new DatabaseService(dbPath);
        db.Initialize();
        db.AddNote("Mail takibi", "Ömer Tamdoğan 3 Temmuz tarihinde mail attı.");

        var result = db.SearchNotes("Ömer Tamdoğan 3 Temmuzda kaç mail atmış?", 10);

        AssertEqual(1, result.Count);
        AssertContains(result[0].Text, "Ömer");
    }),
    ("Veritabani not eklemede bos etiket verilirse otomatik etiket üretir", () =>
    {
        var dbPath = CreateTempDatabasePath();
        var db = new DatabaseService(dbPath);
        db.Initialize();
        
        var id = db.AddNote("Başlık", "veritabanı toplantı rapor", null, new List<string>());
        var note = db.GetNoteById(id);
        
        AssertTrue(note != null);
        AssertContains(note!.Tags, "veritabani");
        AssertContains(note.Tags, "toplanti");
        AssertTrue(note.TagList.Count > 0);
    }),
    ("Veritabani FTS bildirim aramasi dogal dil sorusunda kaydi bulur", () =>
    {
        var dbPath = CreateTempDatabasePath();
        var db = new DatabaseService(dbPath);
        db.Initialize();
        db.AddNotification("Outlook", "Ömer Tamdoğan", "3 Temmuz tarihinde mail attı.");

        var result = db.SearchNotifications("Ömer Tamdoğan 3 Temmuzda kaç mail atmış?", 10);

        AssertEqual(1, result.Count);
        AssertContains(result[0].Title, "Ömer");
    }),
    ("Veritabani FTS indeksi ikinci acilista yeniden kurulmaz", () =>
    {
        var dbPath = CreateTempDatabasePath();
        var db = new DatabaseService(dbPath);
        db.Initialize();
        db.AddNote("Acilis testi", "FTS indeksi tekrar kurulmamali.");
        db.AddNotification("Outlook", "Acilis testi", "Bildirim FTS indeksi tekrar kurulmamali.");

        var firstMeta = db.ExecuteReadOnlyQuery("SELECT Key, Value FROM AppMeta WHERE Key IN ('FtsIndexVersion', 'FtsIndexBuiltAt') ORDER BY Key");
        AssertContains(firstMeta, "FtsIndexVersion");
        AssertContains(firstMeta, "FtsIndexBuiltAt");

        Thread.Sleep(20);
        db.Initialize();

        var secondMeta = db.ExecuteReadOnlyQuery("SELECT Key, Value FROM AppMeta WHERE Key IN ('FtsIndexVersion', 'FtsIndexBuiltAt') ORDER BY Key");
        AssertEqual(firstMeta, secondMeta);
    }),
    ("Veritabani toplam tutar sorusunda son Gemini analiz notunu fallback olarak bulur", () =>
    {
        var dbPath = CreateTempDatabasePath();
        var db = new DatabaseService(dbPath);
        db.Initialize();
        db.AddNote("--- Gemini: DOSYAYI ANALIZ EDIP OZETLE ---", "Yasal Vekalet Ucreti 249.283,61 TL ve Sirket Vekalet Ucreti 49.856,72 TL.");

        var result = db.SearchNotes("toplam tutar ne kadar?", 10);

        AssertEqual(1, result.Count);
        AssertContains(result[0].Text, "249.283,61");
    }),
    ("Veritabani semantik arama tum kelimeler tutmasa da ilgili adayi bulur", () =>
    {
        var dbPath = CreateTempDatabasePath();
        var db = new DatabaseService(dbPath);
        db.Initialize();
        db.AddNote("Ibraname analizi", "Avukat Yavuz Obuz vekalet ucretlerini tahsil ettigini beyan eder.");

        var result = db.SearchNotes("ibraname belgesinde hangi avukat geciyor?", 10);

        AssertEqual(1, result.Count);
        AssertContains(result[0].Text, "Yavuz");
    }),
    ("Gemini DB baglami toplam tutar sorusunda analiz notunu icerir", () =>
    {
        var dbPath = CreateTempDatabasePath();
        var db = new DatabaseService(dbPath);
        db.Initialize();
        db.AddNote("Alakasiz not", "Bugun toplantida dosya teslim alindi.");
        db.AddNote("--- Gemini: DOSYAYI ANALIZ EDIP OZETLE ---", "Tahsil edilen tutarlar 249.283,61 TL ve 49.856,72 TL.");

        var context = db.BuildGeminiDatabaseContext("toplam tutar ne kadar?", 10);

        AssertContains(context, "249.283,61");
        AssertContains(context, "Yerel veritabani");
    }),
    ("Konusma gizlilik hatasi taninir", () =>
    {
        AssertTrue(SpeechRecognitionErrorHelper.IsSpeechPrivacyNotAccepted(
            "The speech privacy policy was not accepted prior to attempting a speech recognition."));
    }),
    ("Dikte dili Turkce yoksa Ingilizceye dusmez", () =>
    {
        var selected = SpeechLanguageSelector.SelectTurkishLanguageTag(["en-US", "de-DE"]);

        AssertEqual<string?>(null, selected);
    }),
    ("Dikte dili Turkce varsa tr-TR secer", () =>
    {
        var selected = SpeechLanguageSelector.SelectTurkishLanguageTag(["en-US", "tr-TR", "tr"]);

        AssertEqual("tr-TR", selected);
    }),
    ("Dikte dili listesi okunabilir yazilir", () =>
    {
        var text = SpeechLanguageSelector.FormatLanguageTags(["en-US", "tr-TR", "en-US"]);

        AssertContains(text, "en-US");
        AssertContains(text, "tr-TR");
    }),
    ("Gemini dikte icin Flash modelini kullanir", () =>
    {
        AssertEqual("gemini-2.5-flash", GeminiTranscriptionPrompt.ModelName);
    }),
    ("Gemini dikte talimati sadece Turkce metin ister", () =>
    {
        var prompt = GeminiTranscriptionPrompt.Build();

        AssertContains(prompt, "Türkçe");
        AssertContains(prompt, "Sadece");
        AssertContains(prompt, "konuşulan metni");
    }),
    ("Gemini CLI komutu istenen modeli kullanir", () =>
    {
        var command = GeminiCliService.BuildCommand("gemini", GeminiTranscriptionPrompt.ModelName);

        AssertContains(command, "-m gemini-2.5-flash");
        AssertContains(command, "gemini");
    }),
    ("Gemini ses payloadu ses dosyasini referans verir", () =>
    {
        var audioPath = @"C:\Temp\audio.wav";
        var payload = GeminiCliService.BuildAudioPayload(audioPath);

        AssertContains(payload, "/Temp/audio.wav");
        AssertContains(payload, "Ses dosyası");
        AssertContains(payload, "Türkçe");
    }),
    ("Ses kaydedici baslangicta kayit yapmaz", () =>
    {
        using var recorder = new AudioRecorderService();

        AssertFalse(recorder.IsRecording);
    }),
    ("Ses kaydedici gecici wav yolu uretir", () =>
    {
        var path = AudioRecorderService.CreateRecordingPath();

        AssertContains(path, "QuickNoteApp");
        AssertTrue(path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase));
    }),
    ("Outlook secimi mailto veya tarayici hedefi kullanmaz", () =>
    {
        var plan = EmailService.CreateOutlookDesktopPlan("ali@example.com", "Konu", "Gövde");

        AssertEqual(EmailOpenMethod.OutlookCom, plan.Method);
        AssertFalse(plan.Target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase));
        AssertFalse(plan.Target.StartsWith("http", StringComparison.OrdinalIgnoreCase));
    }),
    ("Dikte sonucu aciklama cumlesinden temizlenir", () =>
    {
        var cleaned = DictationTextCleaner.Clean("Deşifre şudur: merhaba nasılsın");

        AssertEqual("merhaba nasılsın", cleaned);
    }),
    ("Dikte sonucu thought ve read_file aciklamasindan temizlenir", () =>
    {
        var raw = "thought\nThe `read_file` tool successfully processed the WAV file. Now I need to transcribe the audio content into Turkish text, following all the specified rules. The output from `read_file` indicates that it processed binary content of type `audio/wave`. I will now provide the transcription based on the audio content.Kabul eyle hacılar. Kul der Allah.";
        var cleaned = DictationTextCleaner.Clean(raw);

        AssertEqual("Kabul eyle hacılar. Kul der Allah.", cleaned);
    }),
    ("Tema kaynaklari aydinlik ve koyu moda gecis yapar", () =>
    {
        var resources = CreateThemeResources();

        QuickNoteWindow.ApplyThemeToResources(resources, true);
        AssertEqual("#F4F7FB", ColorToHex((Color)resources["BgDeep"]));
        AssertEqual("#FFFFFF", BrushToHex((SolidColorBrush)resources["ButtonBrush"]));
        AssertEqual("#172033", BrushToHex((SolidColorBrush)resources["TextPrimaryBrush"]));

        QuickNoteWindow.ApplyThemeToResources(resources, false);
        AssertEqual("#080B10", ColorToHex((Color)resources["BgDeep"]));
        AssertEqual("#151B28", BrushToHex((SolidColorBrush)resources["ButtonBrush"]));
        AssertEqual("#F5F7FB", BrushToHex((SolidColorBrush)resources["TextPrimaryBrush"]));
    }),
    ("Tema gorsel kaynaklari DynamicResource kullanir", () =>
    {
        foreach (var file in new[]
        {
            FindRepoFile("QuickNoteApp", "App.xaml"),
            FindRepoFile("QuickNoteApp", "Windows", "QuickNoteWindow.xaml"),
            FindRepoFile("QuickNoteApp", "Windows", "DailyReviewWindow.xaml")
        })
        {
            var text = File.ReadAllText(file);
            AssertFalse(text.Contains("{StaticResource TextPrimaryBrush}", StringComparison.Ordinal));
            AssertFalse(text.Contains("{StaticResource BgPanelBrush}", StringComparison.Ordinal));
            AssertFalse(text.Contains("{StaticResource BorderSoftBrush}", StringComparison.Ordinal));
        }
    }),

    ("Gun ozeti bildirimleri izin filtresini kullanir", () =>
    {
        var text = File.ReadAllText(FindRepoFile("QuickNoteApp", "Windows", "QuickNoteWindow.xaml.cs"));
        var start = text.IndexOf("private void RefreshEmbeddedDailySummary", StringComparison.Ordinal);
        var end = text.IndexOf("private void SearchInput_TextChanged", StringComparison.Ordinal);
        var methodText = text.Substring(start, end - start);

        AssertContains(methodText, "NotificationFilter.ShouldCapture");
    }),
    ("Gun ozeti eski ozel Windows filtresini tutmaz", () =>
    {
        var text = File.ReadAllText(FindRepoFile("QuickNoteApp", "Windows", "QuickNoteWindow.xaml.cs"));

        AssertFalse(text.Contains("private static bool IsBlockedWindowsNotification", StringComparison.Ordinal));
    }),
    ("Gun sonu inceleme bildirimleri izin filtresini kullanir", () =>
    {
        var text = File.ReadAllText(FindRepoFile("QuickNoteApp", "Windows", "DailyReviewWindow.xaml.cs"));
        var start = text.IndexOf("public void Refresh(DateTime date)", StringComparison.Ordinal);
        var end = text.IndexOf("private void ReviewDatePicker_SelectedDateChanged", StringComparison.Ordinal);
        var methodText = text.Substring(start, end - start);

        AssertContains(methodText, "NotificationFilter.ShouldCapture");
        AssertFalse(methodText.Contains("IsBlockedWindowsNotification", StringComparison.Ordinal));
    }),
    ("Gun ozeti bildirim karti dogru alanlara baglanir", () =>
    {
        var xaml = File.ReadAllText(FindRepoFile("QuickNoteApp", "Windows", "QuickNoteWindow.xaml"));
        var start = xaml.IndexOf("x:Name=\"DailySummaryNotificationsList\"", StringComparison.Ordinal);
        var end = xaml.IndexOf("</ItemsControl>", start, StringComparison.Ordinal);
        var template = xaml.Substring(start, end - start);

        AssertContains(template, "Text=\"{Binding Body}\"");
        AssertContains(template, "ReceivedAt");
        AssertFalse(template.Contains("Text=\"{Binding Content}\"", StringComparison.Ordinal));
        AssertFalse(template.Contains("CreatedAt", StringComparison.Ordinal));
    }),
    ("Scroll alanlari modern stil kullanir", () =>
    {
        var appXaml = File.ReadAllText(FindRepoFile("QuickNoteApp", "App.xaml"));

        AssertContains(appXaml, "ModernScrollThumb");
        AssertContains(appXaml, "TargetType=\"ScrollBar\"");
        AssertContains(appXaml, "ScrollBar.PageUpCommand");
        AssertContains(appXaml, "ScrollBar.PageRightCommand");
    }),
    ("Modal servisleri uygulama temasini kullanir", () =>
    {
        foreach (var file in new[]
        {
            FindRepoFile("QuickNoteApp", "Services", "EmailDialogService.cs"),
            FindRepoFile("QuickNoteApp", "Services", "TaskListDialogService.cs"),
            FindRepoFile("QuickNoteApp", "Services", "CalendarEventTimeDialogService.cs")
        })
        {
            var text = File.ReadAllText(file);
            AssertContains(text, "DialogTheme.Current()");
            AssertFalse(text.Contains("#0D0F12", StringComparison.OrdinalIgnoreCase));
            AssertFalse(text.Contains("#161920", StringComparison.OrdinalIgnoreCase));
            AssertFalse(text.Contains("Background = System.Windows.Media.Brushes.White", StringComparison.OrdinalIgnoreCase));
        }
    }),
    ("Editor hizli araclarinda Takvim butonu bulunur", () =>
    {
        var xaml = File.ReadAllText(FindRepoFile("QuickNoteApp", "Windows", "QuickNoteWindow.xaml"));
        var code = File.ReadAllText(FindRepoFile("QuickNoteApp", "Windows", "QuickNoteWindow.xaml.cs"));

        AssertContains(xaml, "Content=\"Takvim\"");
        AssertContains(xaml, "Click=\"TakvimButton_Click\"");
        AssertContains(code, "TakvimButton_Click");
    }),
    ("Takvim Gemini durum cevabini nota eklemeden durur", () =>
    {
        var text = File.ReadAllText(FindRepoFile("QuickNoteApp", "Windows", "QuickNoteWindow.xaml.cs"));
        var start = text.IndexOf("private async Task SendCalendarQuestionToGeminiAsync", StringComparison.Ordinal);
        var end = text.IndexOf("private async Task<List<CalendarEvent>> LoadUpcomingImportantCalendarEventsAsync", StringComparison.Ordinal);
        var methodText = text.Substring(start, end - start);

        AssertContains(methodText, "IsGeminiStatus(response)");
        AssertContains(methodText, "return;");
    }),
    ("Panel onerisi buton metni null guvenli okunur", () =>
    {
        var text = File.ReadAllText(FindRepoFile("QuickNoteApp", "Windows", "QuickNoteWindow.xaml.cs"));

        AssertContains(text, "btn.Content?.ToString()");
        AssertFalse(text.Contains("btn.Content.ToString()", StringComparison.Ordinal));
    }),
    ("Dikte sonucu tirnak icindeki metni alir", () =>
    {
        var raw = "The transcription is: \"Küçük kurbağa küçük kurbağa kuyruğun nerede\"Küçük kurbağa küçük kurbağa kuyruğun nerede";
        var cleaned = DictationTextCleaner.Clean(raw);

        AssertEqual("Küçük kurbağa küçük kurbağa kuyruğun nerede", cleaned);
    })
};

var failed = 0;

foreach (var (name, test) in tests)
{
    try
    {
        test();
        Console.WriteLine($"PASS: {name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"FAIL: {name} - {ex.Message}");
    }
}

if (failed > 0)
{
    Console.WriteLine($"{failed} test basarisiz.");
    return 1;
}

Console.WriteLine("Tum filtre testleri basarili.");
return 0;

static void AssertTrue(bool value)
{
    if (!value)
        throw new InvalidOperationException("true bekleniyordu, false geldi.");
}

static void AssertFalse(bool value)
{
    if (value)
        throw new InvalidOperationException("false bekleniyordu, true geldi.");
}

static void AssertContains(string text, string expected)
{
    if (!text.Contains(expected, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException($"'{expected}' metni bulunamadi.");
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"'{expected}' bekleniyordu, '{actual}' geldi.");
}

static string CreateTempDatabasePath()
{
    var dir = Path.Combine(Path.GetTempPath(), "QuickNoteAppTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    return Path.Combine(dir, "data.db");
}

static ResourceDictionary CreateThemeResources()
{
    var resources = new ResourceDictionary();
    foreach (var key in new[] { "BgDeep", "BgPanel", "BgCard", "BgInput", "BorderSoft", "BorderHover", "TextPrimary", "TextSecondary", "TextMuted", "AccentPurple", "AccentCyan" })
        resources[key] = Colors.Transparent;

    foreach (var key in new[] { "BgDeepBrush", "BgPanelBrush", "BgCardBrush", "BgInputBrush", "BorderSoftBrush", "BorderHoverBrush", "TextPrimaryBrush", "TextSecondaryBrush", "TextMutedBrush", "AccentPurpleBrush", "AccentCyanBrush", "ButtonBrush", "ButtonHoverBrush", "ButtonPressedBrush", "NavHoverBrush", "NavSelectedBrush", "NoteCardBrush", "EditorSurfaceBrush", "EditorToolbarBrush", "CommandCenterBrush", "SubtleSurfaceBrush", "DividerBrush", "WarningPanelBrush", "WarningBorderBrush", "TimeBadgeBrush" })
        resources[key] = new SolidColorBrush(Colors.Transparent);

    return resources;
}

static string BrushToHex(SolidColorBrush brush) => ColorToHex(brush.Color);

static string ColorToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

static string FindRepoFile(params string[] parts)
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir != null)
    {
        var path = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
        if (File.Exists(path))
            return path;

        dir = dir.Parent;
    }

    throw new FileNotFoundException("Repo dosyas? bulunamad?: " + Path.Combine(parts));
}







