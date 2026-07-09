# Calendar Gemini Planning Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a visible Takvim button and make Gemini CLI planning prompts use QuickNote's Google Calendar context.

**Architecture:** Keep the stable local flow: QuickNote reads Google Calendar through the existing `GoogleAuthService` and `CalendarPollingService`, then sends a compact text context to Gemini CLI. No MCP or Drive integration is added in this phase.

**Tech Stack:** WPF, C#/.NET 8, existing Google Calendar REST integration, existing Gemini CLI wrapper, manual console tests in `QuickNoteApp.Tests`.

## Global Constraints

- Responses and user-facing copy stay Turkish and simple.
- Do not add MCP or Google Drive in this phase.
- Keep the change narrow; reuse existing calendar and Gemini services.
- Errors or empty Gemini status messages must not be appended as note content.
- Existing not summary behavior must keep working for normal note prompts.

---

## File Structure

- Modify: `QuickNoteApp/Services/GeminiSearchIntent.cs`
  - Owns simple intent detection for Gemini prompts.
- Modify: `QuickNoteApp/Services/GeminiTaskListPrompt.cs`
  - Builds the planning prompt from calendar events, notes, and notifications.
- Modify: `QuickNoteApp/Windows/QuickNoteWindow.xaml`
  - Adds the visible `Takvim` quick action button.
- Modify: `QuickNoteApp/Windows/QuickNoteWindow.xaml.cs`
  - Adds button handler and hardens calendar Gemini flow so status/errors are not appended to notes.
- Modify: `QuickNoteApp.Tests/Program.cs`
  - Adds focused behavior checks.

---

### Task 1: Takvim Planning Intent

**Files:**
- Modify: `QuickNoteApp/Services/GeminiSearchIntent.cs`
- Test: `QuickNoteApp.Tests/Program.cs`

**Interfaces:**
- Consumes: `GeminiSearchIntent.ShouldUseCalendar(string prompt)`
- Produces: Calendar prompts such as `gunumu planla` and `takvimime gore plan yap` return `true`.

- [ ] **Step 1: Write the failing tests**

Add these tests near existing `GeminiSearchIntent.ShouldUseCalendar` tests in `QuickNoteApp.Tests/Program.cs`:

```csharp
("Gemini gun planlama istegini takvim aramasi sayar", () =>
{
    AssertTrue(GeminiSearchIntent.ShouldUseCalendar("gunumu planla"));
    AssertTrue(GeminiSearchIntent.ShouldUseCalendar("bugunumu takvimime gore planla"));
    AssertTrue(GeminiSearchIntent.ShouldUseCalendar("yarinimi planla"));
}),
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet run --project QuickNoteApp.Tests\QuickNoteApp.Tests.csproj
```

Expected: the new test fails because planning phrases are not all calendar intent yet. If the command is blocked by a running `QuickNoteApp.exe` or old WPF `obj` outputs, note that and use the targeted temporary console check described in Task 5.

- [ ] **Step 3: Write minimal implementation**

In `QuickNoteApp/Services/GeminiSearchIntent.cs`, add planning keywords to the calendar keyword list:

```csharp
private static readonly string[] CalendarKeywords =
[
    "takvim",
    "etkinlik",
    "program",
    "ajanda",
    "calendar",
    "toplantı",
    "toplanti",
    "randevu",
    "günümü planla",
    "gunumu planla",
    "bugünümü planla",
    "bugunumu planla",
    "yarınımı planla",
    "yarinimi planla",
    "plan yap",
    "planla"
];
```

Keep the existing `çevir/cevir` exclusion.

- [ ] **Step 4: Run test to verify it passes**

Run:

```powershell
dotnet run --project QuickNoteApp.Tests\QuickNoteApp.Tests.csproj
```

Expected: the new intent test passes, or targeted check passes if full WPF build is blocked.

---

### Task 2: Planning Prompt Uses Calendar Context Clearly

**Files:**
- Modify: `QuickNoteApp/Services/GeminiTaskListPrompt.cs`
- Test: `QuickNoteApp.Tests/Program.cs`

**Interfaces:**
- Consumes: `GeminiTaskListPrompt.Build(List<CalendarEvent> events, List<NoteItem> notes, List<NotificationLogItem> notifications, DateTime date)`
- Produces: A Turkish prompt that explicitly asks Gemini to plan the day according to calendar events, notes, and notifications.

- [ ] **Step 1: Write the failing test**

Add this test near existing task-list/calendar prompt tests in `QuickNoteApp.Tests/Program.cs`:

```csharp
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
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet run --project QuickNoteApp.Tests\QuickNoteApp.Tests.csproj
```

Expected: failure if the prompt does not yet include the exact planning wording.

- [ ] **Step 3: Write minimal implementation**

Update `GeminiTaskListPrompt.Build` so the instruction starts with clear planning language. Keep existing event/note/notification formatting. The opening text should include this idea:

```csharp
builder.AppendLine($"{date:dd.MM.yyyy} icin takvim verilerine gore uygulanabilir bir gun plani hazirla.");
builder.AppendLine("Cevabi kisa, saat sirali ve Turkce yaz. Takvimdeki sabit etkinlikleri degistirme; bosluklara odaklanma bloklari ve kucuk gorevler yerlestir.");
```

- [ ] **Step 4: Run test to verify it passes**

Run:

```powershell
dotnet run --project QuickNoteApp.Tests\QuickNoteApp.Tests.csproj
```

Expected: new prompt test passes.

---

### Task 3: Takvim Button Opens Calendar View

**Files:**
- Modify: `QuickNoteApp/Windows/QuickNoteWindow.xaml`
- Modify: `QuickNoteApp/Windows/QuickNoteWindow.xaml.cs`
- Test: `QuickNoteApp.Tests/Program.cs`

**Interfaces:**
- Consumes: existing dashboard controls `PanelimRadio`, `DashboardCalendar`, `DashboardCalendarEventsPanel`, and `LoadCalendarEventsForDateAsync(DateTime date)`.
- Produces: `TakvimButton_Click(object sender, RoutedEventArgs e)` handler.

- [ ] **Step 1: Write the failing XAML/code presence test**

Add this test near existing XAML presence tests in `QuickNoteApp.Tests/Program.cs`:

```csharp
("Editor hizli araclarinda Takvim butonu bulunur", () =>
{
    var xaml = File.ReadAllText(FindRepoFile("QuickNoteApp", "Windows", "QuickNoteWindow.xaml"));
    var code = File.ReadAllText(FindRepoFile("QuickNoteApp", "Windows", "QuickNoteWindow.xaml.cs"));

    AssertContains(xaml, "Content=\"Takvim\"");
    AssertContains(xaml, "Click=\"TakvimButton_Click\"");
    AssertContains(code, "TakvimButton_Click");
}),
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet run --project QuickNoteApp.Tests\QuickNoteApp.Tests.csproj
```

Expected: failure because the button/handler does not exist yet.

- [ ] **Step 3: Add the button**

In `QuickNoteApp/Windows/QuickNoteWindow.xaml`, in the `AI VE CIKTI` quick action `WrapPanel`, add:

```xml
<Button Content="Takvim" ToolTip="Takvimi goster" Style="{StaticResource CompactActionButton}" Click="TakvimButton_Click" />
```

Place it near `Gün Özeti` and `Plan`.

- [ ] **Step 4: Add the handler**

In `QuickNoteApp/Windows/QuickNoteWindow.xaml.cs`, add this method near other button handlers:

```csharp
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
```

- [ ] **Step 5: Run test to verify it passes**

Run:

```powershell
dotnet run --project QuickNoteApp.Tests\QuickNoteApp.Tests.csproj
```

Expected: button presence test passes.

---

### Task 4: Calendar Gemini Flow Does Not Append Status/Error Text

**Files:**
- Modify: `QuickNoteApp/Windows/QuickNoteWindow.xaml.cs`
- Test: `QuickNoteApp.Tests/Program.cs`

**Interfaces:**
- Consumes: existing `IsGeminiStatus(string response)` and `SendCalendarQuestionToGeminiAsync(string question)`.
- Produces: calendar Gemini flow returns early on empty/status response.

- [ ] **Step 1: Write the failing static behavior test**

Add this test near other `QuickNoteWindow.xaml.cs` text checks:

```csharp
("Takvim Gemini durum cevabini nota eklemeden durur", () =>
{
    var text = File.ReadAllText(FindRepoFile("QuickNoteApp", "Windows", "QuickNoteWindow.xaml.cs"));
    var start = text.IndexOf("private async Task SendCalendarQuestionToGeminiAsync", StringComparison.Ordinal);
    var end = text.IndexOf("private async Task<List<CalendarEvent>> LoadUpcomingImportantCalendarEventsAsync", StringComparison.Ordinal);
    var methodText = text.Substring(start, end - start);

    AssertContains(methodText, "IsGeminiStatus(response)");
    AssertContains(methodText, "return;");
}),
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet run --project QuickNoteApp.Tests\QuickNoteApp.Tests.csproj
```

Expected: failure if calendar flow currently sets status but still appends response to the note.

- [ ] **Step 3: Add early return**

In `SendCalendarQuestionToGeminiAsync`, change the status block to:

```csharp
if (string.IsNullOrWhiteSpace(response) || IsGeminiStatus(response))
{
    CopilotStatusText.Text = string.IsNullOrWhiteSpace(response) ? "Gemini takvim cevabi vermedi." : response;
    return;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run:

```powershell
dotnet run --project QuickNoteApp.Tests\QuickNoteApp.Tests.csproj
```

Expected: static behavior test passes.

---

### Task 5: Verification and Fallback Checks

**Files:**
- No production file changes unless verification exposes an issue.

**Interfaces:**
- Consumes: all changes from Tasks 1-4.
- Produces: clear verification evidence.

- [ ] **Step 1: Try full test run**

Run:

```powershell
dotnet run --project QuickNoteApp.Tests\QuickNoteApp.Tests.csproj
```

Expected: all tests pass. If blocked by `QuickNoteApp.exe` being in use, close the app or document the lock.

- [ ] **Step 2: If full test run is blocked by old WPF generated files, run targeted check**

Create a temporary console project outside the repo, copy these files into it, and run focused checks:

- `QuickNoteApp/Services/GeminiSearchIntent.cs`
- `QuickNoteApp/Services/GeminiTaskListPrompt.cs`
- model classes needed by `GeminiTaskListPrompt`

Checks must cover:

```csharp
GeminiSearchIntent.ShouldUseCalendar("gunumu planla") == true
GeminiSearchIntent.ShouldUseCalendar("bugunumu takvimime gore planla") == true
GeminiSearchIntent.ShouldUseCalendar("Bu metni kisaca ozetle") == false
GeminiTaskListPrompt.Build(...).Contains("gun plani") == true
```

- [ ] **Step 3: Report verification honestly**

Final report must include:

- Whether full tests ran.
- If not, exact blocker.
- Which targeted checks passed.
- Files changed.

---
