# Gemini Flash Dikte Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Windows speech recognition with microphone recording plus Gemini CLI Flash transcription from the existing `Dikte Et` button.

**Architecture:** The window records a temporary WAV file, sends it to Gemini CLI with `gemini-2.5-flash`, appends the returned Turkish text to the note, then deletes the temp file. Prompt/model selection and recorder state are kept in small service classes so tests can cover the stable behavior without touching the microphone.

**Tech Stack:** WPF, .NET 8, NAudio for WAV recording, existing Gemini CLI integration, manual console tests in `QuickNoteApp.Tests`.

---

### Task 1: Gemini transcription prompt and model

**Files:**
- Create: `QuickNoteApp/Services/GeminiTranscriptionPrompt.cs`
- Modify: `QuickNoteApp.Tests/Program.cs`

- [ ] Write failing tests for Flash model name and Turkish-only transcription prompt.
- [ ] Run `dotnet run --project .\QuickNoteApp.Tests\QuickNoteApp.Tests.csproj` and verify the new tests fail because the class does not exist.
- [ ] Add `GeminiTranscriptionPrompt` with `ModelName = "gemini-2.5-flash"` and `Build()` returning a concise Turkish transcription instruction.
- [ ] Run tests and verify they pass.

### Task 2: Gemini CLI audio transcription

**Files:**
- Modify: `QuickNoteApp/Services/GeminiCliService.cs`
- Modify: `QuickNoteApp.Tests/Program.cs`

- [ ] Write failing tests proving Gemini command uses the requested model and references attached audio files.
- [ ] Run tests and verify they fail because command/payload helpers do not exist.
- [ ] Add internal helpers for building the Gemini command and audio payload.
- [ ] Add `TranscribeAudioAsync(string audioPath)` that sends the WAV file to Gemini CLI with the Flash model.
- [ ] Run tests and verify they pass.

### Task 3: Microphone WAV recorder

**Files:**
- Modify: `QuickNoteApp/QuickNoteApp.csproj`
- Create: `QuickNoteApp/Services/AudioRecorderService.cs`
- Modify: `QuickNoteApp.Tests/Program.cs`

- [ ] Add NAudio package.
- [ ] Write failing tests for temporary WAV path creation and initial recording state.
- [ ] Add `AudioRecorderService` using `WaveInEvent` and `WaveFileWriter`.
- [ ] Run tests and verify they pass.

### Task 4: Wire existing Dikte button

**Files:**
- Modify: `QuickNoteApp/Windows/QuickNoteWindow.xaml.cs`

- [ ] Replace Windows `SpeechRecognizer` fields and event handlers with `AudioRecorderService`.
- [ ] On first click: start recording and show `Durdur`.
- [ ] On second click: stop recording, transcribe with Gemini Flash, append text to `NoteInput`, then delete the temp WAV.
- [ ] Show simple Turkish error messages for missing Gemini CLI, microphone errors, or empty transcription.
- [ ] Run character scan, tests, and build.
