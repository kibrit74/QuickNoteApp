# Inline Smart Search Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Mevcut arama kutusuna yeni buton eklemeden, doğal dil sorgularında Gemini destekli kısa özet göstermek.

**Architecture:** Normal kısa aramalar mevcut hızlı filtreyle çalışmaya devam eder. `GeminiSearchIntent` doğal dil/soru aramalarını ayırır; `QuickNoteWindow` arama yazımı durduktan sonra küçük bir özet panelinde Gemini sonucunu gösterir.

**Tech Stack:** WPF, .NET 8, SQLite FTS, mevcut `GeminiCliService`, mevcut console test runner.

## Global Constraints

- Yeni buton eklenmeyecek.
- Basit kelime aramaları hızlı ve yerel kalacak.
- Gemini sadece doğal dil/soru gibi görünen aramalarda çalışacak.
- Uygulama Türkçe kullanıcı metinleriyle çalışacak.

---

### Task 1: Smart Search Intent

**Files:**
- Modify: `QuickNoteApp/Services/GeminiSearchIntent.cs`
- Test: `QuickNoteApp.Tests/Program.cs`

**Interfaces:**
- Produces: `GeminiSearchIntent.ShouldUseSmartSearch(string prompt): bool`

- [ ] Add failing tests for simple search vs natural language search.
- [ ] Implement `ShouldUseSmartSearch`.
- [ ] Run `dotnet run --project QuickNoteApp.Tests/QuickNoteApp.Tests.csproj`.

### Task 2: Inline Search Summary UI

**Files:**
- Modify: `QuickNoteApp/Windows/QuickNoteWindow.xaml`
- Modify: `QuickNoteApp/Windows/QuickNoteWindow.xaml.cs`

**Interfaces:**
- Consumes: `GeminiSearchIntent.ShouldUseSmartSearch(string prompt): bool`

- [ ] Add a collapsible summary panel under the search box.
- [ ] Debounce search text changes with a timer.
- [ ] Keep normal filtering immediate.
- [ ] Run Gemini only for smart searches, then show summary text.
- [ ] Hide the summary panel for short/simple searches.

### Task 3: Verification

**Files:**
- Test: `QuickNoteApp.Tests/Program.cs`
- Build: `QuickNoteApp/QuickNoteApp.csproj`

- [ ] Run `dotnet run --project QuickNoteApp.Tests/QuickNoteApp.Tests.csproj`.
- [ ] Run `dotnet build QuickNoteApp/QuickNoteApp.csproj`.
- [ ] Confirm no new build warnings or errors.
