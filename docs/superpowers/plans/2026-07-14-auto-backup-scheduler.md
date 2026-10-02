# Auto Backup Scheduler Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** QuickNoteApp açılışında günlük otomatik SQLite yedeği almak, eski yedekleri sınırlı tutmak ve ayarlardan klasör/geri yükleme yönetimi sunmak.

**Architecture:** `AutoBackupService` yedek klasörü, retention ve son yedek tarihini `AppMeta` üzerinden yönetir. `App` açılışta günde bir kez yedek alır. Ayarlar ekranındaki veritabanı bakım kartı yedek klasörü seçme, 7/14 yedek saklama, manuel otomatik yedek alma ve son yedeği geri yükleme aksiyonlarını gösterir.

**Tech Stack:** WPF, .NET 8, SQLite, mevcut `DatabaseService`, mevcut console test runner.

## Global Constraints

- Kullanıcı verisi silinmeden önce onay istenecek.
- Varsayılan yedek klasörü `%AppData%\QuickNoteApp\Backups` olacak.
- Varsayılan saklama sayısı 14 yedek olacak.
- Restore işlemi uygulama kapanışı gerektirecek.
- Yeni sistem mevcut manuel yedek butonunu bozmayacak.

---

### Task 1: AutoBackupService

**Files:**
- Create: `QuickNoteApp/Services/AutoBackupService.cs`
- Test: `QuickNoteApp.Tests/Program.cs`

**Interfaces:**
- Produces: `AutoBackupService.RunStartupBackup(DatabaseService db, DateTime now): AutoBackupResult`
- Produces: `AutoBackupService.GetBackupDirectory(DatabaseService db): string`
- Produces: `AutoBackupService.SetBackupDirectory(DatabaseService db, string path): void`
- Produces: `AutoBackupService.GetRetentionCount(DatabaseService db): int`
- Produces: `AutoBackupService.SetRetentionCount(DatabaseService db, int count): void`
- Produces: `AutoBackupService.GetLatestBackupPath(DatabaseService db): string?`

- [ ] Add tests for daily backup, duplicate same-day skip, retention cleanup and latest backup detection.
- [ ] Run tests and confirm compile fails because `AutoBackupService` is missing.
- [ ] Implement the service.
- [ ] Run tests and confirm pass.

### Task 2: Startup Integration

**Files:**
- Modify: `QuickNoteApp/App.xaml.cs`

**Interfaces:**
- Consumes: `AutoBackupService.RunStartupBackup(DatabaseService db, DateTime now): AutoBackupResult`

- [ ] Call `AutoBackupService.RunStartupBackup` once during idle startup.
- [ ] Log success, skip and errors through existing `StartupLog`.

### Task 3: Settings UI

**Files:**
- Modify: `QuickNoteApp/Windows/QuickNoteWindow.xaml`
- Modify: `QuickNoteApp/Windows/QuickNoteWindow.xaml.cs`

**Interfaces:**
- Consumes: `AutoBackupService` methods from Task 1.
- Consumes: `DatabaseService.RestoreDatabaseFromBackup(string backupPath): void`

- [ ] Add compact auto backup status rows to the existing Database Maintenance card.
- [ ] Add folder select, 7/14 retention selector, “Şimdi Otomatik Yedek Al” and “Son Yedeği Geri Yükle” controls.
- [ ] Add handlers that update settings and refresh stats.
- [ ] Restore latest backup only after confirmation, then shut down the app.

### Task 4: Verification

**Files:**
- Test: `QuickNoteApp.Tests/Program.cs`
- Build: `QuickNoteApp/QuickNoteApp.csproj`

- [ ] Run `dotnet run --project QuickNoteApp.Tests/QuickNoteApp.Tests.csproj`.
- [ ] Run `dotnet build QuickNoteApp/QuickNoteApp.csproj`.
- [ ] Confirm zero build errors and no new warnings.
