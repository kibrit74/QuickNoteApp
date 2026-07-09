# Google Calendar And Outlook Reminder Flow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Google hesabı bağlandıktan sonra takvim etkinliklerini güvenilir biçimde okuyup özetleyebilmek, önemli etkinlikler için otomatik hatırlatma üretmek ve Outlook bildirimlerini paketli mod dışında da uygulamaya düşürmek.

**Architecture:** Mevcut `GoogleAuthService` ve `CalendarPollingService` korunacak, ama bunların üstüne tarih aralığı özeti ve “önemli etkinlik” kuralları eklenecek. Outlook tarafında mevcut WinRT bildirim okuyucu korunacak; paketli çalışmayan senaryoda klasik Outlook masaüstü kutusunu COM üzerinden yoklayan bir yedek akış eklenecek.

**Tech Stack:** WPF, .NET 8, SQLite, WinRT `UserNotificationListener`, Outlook COM otomasyonu, mevcut test konsolu

## Global Constraints

- Mevcut WPF mimarisi korunacak, büyük UI yeniden yazımı yapılmayacak.
- Yeni davranışlar önce test ile tanımlanacak, sonra kod yazılacak.
- Outlook çözümü klasik masaüstü Outlook kurulu makinede çalışacak şekilde tasarlanacak.
- WinRT bildirim akışı kaldırılmayacak; sadece yedek bir yol eklenecek.

---

### Task 1: Takvim özetleme davranışını testle tanımla

**Files:**
- Modify: `QuickNoteApp.Tests/Program.cs`
- Create: `QuickNoteApp/Services/CalendarSummaryBuilder.cs`
- Create: `QuickNoteApp/Services/ImportantCalendarEventPolicy.cs`

**Interfaces:**
- Consumes: `CalendarEvent`
- Produces: `CalendarSummaryBuilder.Build(DateTime anchorDate, IReadOnlyList<CalendarEvent> events) : string`
- Produces: `ImportantCalendarEventPolicy.IsImportant(CalendarEvent calendarEvent) : bool`
- Produces: `ImportantCalendarEventPolicy.ShouldNotifyNow(CalendarEvent calendarEvent, DateTime now) : bool`

- [ ] **Step 1: Takvim özeti için failing test yaz**
- [ ] **Step 2: Önemli etkinlik kuralları için failing test yaz**
- [ ] **Step 3: Testleri tek başına çalıştırıp kırmızı olduklarını doğrula**
- [ ] **Step 4: Minimal özetleyici ve önem politikası kodunu yaz**
- [ ] **Step 5: Testleri tekrar çalıştırıp yeşile getir**

### Task 2: Google takvimden otomatik hatırlatma üret

**Files:**
- Modify: `QuickNoteApp/Models/ReminderItem.cs`
- Modify: `QuickNoteApp/Services/DatabaseService.cs`
- Modify: `QuickNoteApp/Services/CalendarPollingService.cs`
- Modify: `QuickNoteApp/Services/ReminderService.cs`
- Create: `QuickNoteApp/Services/CalendarReminderSyncService.cs`

**Interfaces:**
- Consumes: `GoogleAuthService.GetCalendarEventsAsync(DateTime date, CancellationToken cancellationToken = default)`
- Consumes: `ImportantCalendarEventPolicy`
- Produces: `DatabaseService.UpsertCalendarReminder(CalendarEvent calendarEvent, DateTime reminderAt) : void`
- Produces: `DatabaseService.GetDueReminders(DateTime now) : List<ReminderItem>`

- [ ] **Step 1: Takvim etkinliğinden hatırlatma üretimi için failing test ekle**
- [ ] **Step 2: Veritabanında takvim kaynaklı hatırlatma alanlarını tanımla**
- [ ] **Step 3: Aynı etkinlik için tekrar kayıt oluşturmayan senkronizasyonu yaz**
- [ ] **Step 4: Uygulama başlangıcında senkronizasyonu devreye bağla**
- [ ] **Step 5: Test ve derleme ile doğrula**

### Task 3: Google takvim özeti akışını kullanıcı tarafında güçlendir

**Files:**
- Modify: `QuickNoteApp/Windows/QuickNoteWindow.xaml.cs`
- Modify: `QuickNoteApp/Services/GeminiSearchIntent.cs`
- Modify: `QuickNoteApp/Services/GeminiTaskListPrompt.cs`

**Interfaces:**
- Consumes: `CalendarSummaryBuilder.Build(...)`
- Consumes: `CalendarPollingService.GetEventsForDateAsync(DateTime date, bool forceRefresh = false)`
- Produces: daha zengin takvim özeti ve görev planı bağlamı

- [ ] **Step 1: Takvim sorusunun sadece “bugün” ile sınırlı kalmadığını gösteren test ekle**
- [ ] **Step 2: Seçili tarih + yakın yaklaşan önemli etkinlikleri prompt bağlamına ekle**
- [ ] **Step 3: Google girişinden sonra dashboard yenilemeyi güçlendir**
- [ ] **Step 4: Test ve derleme ile doğrula**

### Task 4: Outlook için masaüstü yedek yakalama akışı ekle

**Files:**
- Modify: `QuickNoteApp/Services/DatabaseService.cs`
- Modify: `QuickNoteApp/Services/NotificationListenerService.cs`
- Create: `QuickNoteApp/Services/OutlookDesktopNotificationBridge.cs`
- Modify: `QuickNoteApp/Services/NotificationFilter.cs`
- Modify: `QuickNoteApp/App.xaml.cs`
- Modify: `QuickNoteApp.Tests/Program.cs`

**Interfaces:**
- Consumes: `Type.GetTypeFromProgID("Outlook.Application")`
- Produces: `OutlookDesktopNotificationBridge.Start() : void`
- Produces: `DatabaseService.AddNotification(string appName, string title, string body, DateTime receivedAt) : void`

- [ ] **Step 1: Aynı Outlook mailinin tekrar tekrar kaydedilmediğini testle tanımla**
- [ ] **Step 2: `AddNotification` için zaman damgası alan overload ekle**
- [ ] **Step 3: Klasik Outlook gelen kutusunu yoklayan yedek köprü servisini yaz**
- [ ] **Step 4: WinRT erişimi yoksa bu yedek akışı otomatik devreye al**
- [ ] **Step 5: Test ve derleme ile doğrula**

### Task 5: Son doğrulama

**Files:**
- Test: `QuickNoteApp.Tests/Program.cs`
- Test: `QuickNoteApp/QuickNoteApp.csproj`

**Interfaces:**
- Produces: `dotnet build QuickNoteApp\QuickNoteApp.csproj`
- Produces: `dotnet run --project QuickNoteApp.Tests\QuickNoteApp.Tests.csproj`

- [ ] **Step 1: Ana projeyi derle**
- [ ] **Step 2: Test konsolunu çalıştır**
- [ ] **Step 3: Kalan riskleri not et**

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-07-08-google-calendar-outlook-reminders.md`.

Bu istek için kullanıcı doğrudan “planlayıp sonra kodlama aşamasına geç” dediği için uygulama bu oturumda inline yürütülecek.
