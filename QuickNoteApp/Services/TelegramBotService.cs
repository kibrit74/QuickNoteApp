using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using QuickNoteApp.Models;

namespace QuickNoteApp.Services
{
    public class TelegramBotService : IDisposable
    {
        private readonly DatabaseService _db;
        private readonly GeminiCliService _geminiCli;
        private readonly HttpClient _httpClient;
        private readonly CalendarPollingService? _calendarPolling;
        
        private string _token = "";
        private long _chatId;
        private bool _isEnabled;
        
        private CancellationTokenSource? _cts;
        private Task? _pollingTask;
        private long _lastOffset;

        public event Action<string>? StatusChanged;
        public event Action<long, string>? ChatIdDiscovered;
        public event EventHandler? NoteAdded;

        public TelegramBotService(DatabaseService db) : this(db, null)
        {
        }

        public TelegramBotService(DatabaseService db, CalendarPollingService? calendarPolling)
        {
            _db = db;
            _calendarPolling = calendarPolling;
            _geminiCli = new GeminiCliService();
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
            
            LoadSettings();
        }

        public string Token => _token;
        public long ChatId => _chatId;
        public bool IsEnabled => _isEnabled;
        public bool IsRunning => _pollingTask != null && !_pollingTask.IsCompleted;

        public void LoadSettings()
        {
            _token = _db.GetSecret("TelegramBotToken") ?? "";
            
            var chatIdStr = _db.GetMeta("TelegramBotChatId") ?? "0";
            long.TryParse(chatIdStr, out _chatId);

            _isEnabled = _db.GetMeta("TelegramBotEnabled") == "true";
            
            var offsetStr = _db.GetMeta("TelegramBotLastOffset") ?? "0";
            long.TryParse(offsetStr, out _lastOffset);
        }

        public void SaveSettings(string token, long chatId, bool enabled)
        {
            _token = token;
            _chatId = chatId;
            _isEnabled = enabled;

            _db.SetSecret("TelegramBotToken", token);
            _db.SetMeta("TelegramBotChatId", chatId.ToString());
            _db.SetMeta("TelegramBotEnabled", enabled ? "true" : "false");

            if (enabled)
            {
                Start();
            }
            else
            {
                Stop();
            }
        }

        public void Start()
        {
            if (IsRunning) return;
            if (string.IsNullOrWhiteSpace(_token))
            {
                StatusChanged?.Invoke("Hata: Bot Token girilmemiş.");
                return;
            }

            _cts = new CancellationTokenSource();
            _pollingTask = Task.Run(() => PollUpdatesAsync(_cts.Token));
            StatusChanged?.Invoke("Çalışıyor");
        }

        public void Stop()
        {
            if (!IsRunning) return;
            
            try
            {
                _cts?.Cancel();
                _pollingTask?.Wait(2000);
            }
            catch {}
            finally
            {
                _cts?.Dispose();
                _cts = null;
                _pollingTask = null;
                StatusChanged?.Invoke("Durduruldu");
            }
        }

        private async Task PollUpdatesAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var url = $"https://api.telegram.org/bot{_token}/getUpdates?offset={_lastOffset + 1}&timeout=30";
                    var response = await _httpClient.GetAsync(url, cancellationToken);
                    if (!response.IsSuccessStatusCode)
                    {
                        StatusChanged?.Invoke($"Hata: API yanıtı {response.StatusCode}");
                        await Task.Delay(5000, cancellationToken);
                        continue;
                    }

                    var jsonString = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(jsonString);
                    if (!doc.RootElement.TryGetProperty("ok", out var okProp) || !okProp.GetBoolean())
                    {
                        StatusChanged?.Invoke("Hata: Telegram API ok=false döndü.");
                        await Task.Delay(5000, cancellationToken);
                        continue;
                    }

                    if (doc.RootElement.TryGetProperty("result", out var resultArr) && resultArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var update in resultArr.EnumerateArray())
                        {
                            if (update.TryGetProperty("update_id", out var updateIdProp))
                            {
                                _lastOffset = updateIdProp.GetInt64();
                                _db.SetMeta("TelegramBotLastOffset", _lastOffset.ToString());
                            }

                            if (update.TryGetProperty("message", out var messageElement))
                            {
                                await ProcessMessageAsync(messageElement, cancellationToken);
                            }
                        }
                    }

                    StatusChanged?.Invoke("Çalışıyor");
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke($"Hata: {ex.Message}");
                    try { await Task.Delay(5000, cancellationToken); } catch {}
                }
            }
        }

        private async Task ProcessMessageAsync(JsonElement msg, CancellationToken cancellationToken)
        {
            if (!msg.TryGetProperty("chat", out var chatElement) || !chatElement.TryGetProperty("id", out var chatIdProp))
                return;

            long senderChatId = chatIdProp.GetInt64();
            string username = "";
            if (msg.TryGetProperty("from", out var fromElement) && fromElement.TryGetProperty("username", out var userProp))
            {
                username = userProp.GetString() ?? "";
            }

            // Chat ID Discovery mode: if target Chat ID is not configured, or is 0, let's discover it!
            if (_chatId == 0)
            {
                ChatIdDiscovered?.Invoke(senderChatId, username);
                
                // Let's send a friendly response guiding the user to bind it
                await SendTelegramMessageAsync(senderChatId, $"Gelen bağlantı başarılı! Chat ID'niz: {senderChatId}\n\nLütfen bu ID'yi QuickNoteApp ayarlarına kaydedin.");
                return;
            }

            if (senderChatId != _chatId)
            {
                // Unauthorized user
                await SendTelegramMessageAsync(senderChatId, "Yetkisiz Erişim. Bu bot kişisel bir QuickNoteApp uygulamasına bağlıdır.");
                return;
            }

            // Check if there is a caption command first
            string caption = "";
            bool hasCaptionCommand = false;
            if (msg.TryGetProperty("caption", out var captionProp))
            {
                caption = captionProp.GetString() ?? "";
                if (caption.StartsWith("/sor", StringComparison.OrdinalIgnoreCase) || 
                    caption.StartsWith("/gemini", StringComparison.OrdinalIgnoreCase) ||
                    caption.StartsWith("/ask", StringComparison.OrdinalIgnoreCase) ||
                    caption.StartsWith("/plan", StringComparison.OrdinalIgnoreCase) ||
                    caption.StartsWith("/bugun", StringComparison.OrdinalIgnoreCase) ||
                    caption.StartsWith("/ozet", StringComparison.OrdinalIgnoreCase))
                {
                    hasCaptionCommand = true;
                }
            }

            if (hasCaptionCommand)
            {
                if (msg.TryGetProperty("photo", out var photoArrayProp) && photoArrayProp.ValueKind == JsonValueKind.Array && photoArrayProp.GetArrayLength() > 0)
                {
                    var targetFile = await DownloadPhotoHelperAsync(photoArrayProp, cancellationToken);
                    if (targetFile != null)
                    {
                        await ProcessGeminiQueryAsync(caption, targetFile, isImage: true);
                        return;
                    }
                }
                else if (msg.TryGetProperty("document", out var docElement))
                {
                    var targetFile = await DownloadDocumentHelperAsync(docElement, cancellationToken);
                    if (targetFile != null)
                    {
                        string mimeType = "";
                        if (docElement.TryGetProperty("mime_type", out var mimeProp))
                        {
                            mimeType = mimeProp.GetString() ?? "";
                        }
                        var isImage = mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
                        await ProcessGeminiQueryAsync(caption, targetFile, isImage: isImage);
                        return;
                    }
                }
            }

            // Case A: Voice Note
            if (msg.TryGetProperty("voice", out var voiceElement))
            {
                await ProcessVoiceNoteAsync(voiceElement, cancellationToken);
            }
            // Case B: Compressed Photo
            else if (msg.TryGetProperty("photo", out var photoArrayProp) && photoArrayProp.ValueKind == JsonValueKind.Array && photoArrayProp.GetArrayLength() > 0)
            {
                await ProcessPhotoMsgAsync(photoArrayProp, caption, cancellationToken);
            }
            // Case C: Uncompressed Document File (can be image)
            else if (msg.TryGetProperty("document", out var docElement))
            {
                await ProcessDocumentMsgAsync(docElement, caption, cancellationToken);
            }
            // Case D: Text Message
            else if (msg.TryGetProperty("text", out var textProp))
            {
                var text = textProp.GetString() ?? "";
                if (text.StartsWith("/start", StringComparison.OrdinalIgnoreCase))
                {
                    await SendTelegramMessageAsync(_chatId, "QuickNoteApp Botuna Hoş Geldiniz! \ud83d\ude80\n\nKomutlar:\n\ud83d\udc49 /sor [soru] - Gemini asistanina soru sorun (ornek: /sor takvimi ozetle)\n\ud83d\udc49 /plan - Bugunun planini ve ozetini cikarin.\n\ud83d\udc49 [herhangi bir metin] - Dogrudan not olarak kaydedilir.");
                }
                else if (text.StartsWith("/sor", StringComparison.OrdinalIgnoreCase) || 
                         text.StartsWith("/gemini", StringComparison.OrdinalIgnoreCase) ||
                         text.StartsWith("/ask", StringComparison.OrdinalIgnoreCase) ||
                         text.StartsWith("/plan", StringComparison.OrdinalIgnoreCase) ||
                         text.StartsWith("/bugun", StringComparison.OrdinalIgnoreCase) ||
                         text.StartsWith("/ozet", StringComparison.OrdinalIgnoreCase))
                {
                    await ProcessGeminiQueryAsync(text);
                }
                else if (text.StartsWith("/"))
                {
                    await SendTelegramMessageAsync(_chatId, "Bilinmeyen komut. Kullanabileceginiz komutlar:\n\ud83d\udc49 /sor [soru]\n\ud83d\udc49 /plan\n\ud83d\udc49 /start");
                }
                else
                {
                    // Add note
                    var title = "Telegram Notu";
                    var firstLine = text.Split('\n')[0].Trim();
                    if (firstLine.Length > 0)
                    {
                        title = firstLine.Length > 30 ? firstLine[..30] + "..." : firstLine;
                    }
                    
                    _db.AddNote(title, text, null, new List<string> { "Telegram" });
                    NoteAdded?.Invoke(this, EventArgs.Empty);
                    
                    var shortText = text.Length > 20 ? text[..20] + "..." : text;
                    await SendTelegramMessageAsync(_chatId, $"Not basariyla kaydedildi: \"{shortText}\" \ud83d\udcdd");
                }
            }
        }

        private async Task ProcessVoiceNoteAsync(JsonElement voice, CancellationToken cancellationToken)
        {
            if (!voice.TryGetProperty("file_id", out var fileIdProp))
                return;

            string fileId = fileIdProp.GetString() ?? "";
            await SendTelegramMessageAsync(_chatId, "Ses notu alındı, indiriliyor ve Gemini ile metne çevriliyor... 🎙️");

            // 1. Get File Path from Telegram
            var getFileUrl = $"https://api.telegram.org/bot{_token}/getFile?file_id={fileId}";
            var getFileResp = await _httpClient.GetAsync(getFileUrl, cancellationToken);
            if (!getFileResp.IsSuccessStatusCode)
            {
                await SendTelegramMessageAsync(_chatId, "Hata: Ses dosyası bilgisi Telegram'dan alınamadı.");
                return;
            }

            var jsonString = await getFileResp.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(jsonString);
            if (!doc.RootElement.TryGetProperty("ok", out var okProp) || !okProp.GetBoolean() ||
                !doc.RootElement.TryGetProperty("result", out var resultProp) ||
                !resultProp.TryGetProperty("file_path", out var filePathProp))
            {
                await SendTelegramMessageAsync(_chatId, "Hata: Ses dosyasının yolu bulunamadı.");
                return;
            }

            string filePath = filePathProp.GetString() ?? "";

            // 2. Download OGG/OGA file locally
            var fileUrl = $"https://api.telegram.org/file/bot{_token}/{filePath}";
            var tempFile = Path.Combine(_db.AttachmentsDirectory, $"telegram_voice_{Guid.NewGuid():N}.oga");
            
            using (var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using var response = await _httpClient.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    await SendTelegramMessageAsync(_chatId, "Hata: Ses dosyası indirilemedi.");
                    return;
                }
                await response.Content.CopyToAsync(fileStream, cancellationToken);
            }

            try
            {
                // 3. Transcribe with Gemini CLI
                var transcription = await _geminiCli.TranscribeAudioAsync(tempFile);
                if (string.IsNullOrWhiteSpace(transcription))
                {
                    await SendTelegramMessageAsync(_chatId, "Hata: Ses kaydı metne dönüştürülemedi (boş yanıt).");
                    return;
                }

                // 4. Refine text if dictation refiner is enabled
                var refinerEnabled = _db.GetMeta("EnableDictationRefiner") != "false";
                var finalOutput = transcription;
                if (refinerEnabled)
                {
                    finalOutput = await _geminiCli.RefineDictationAsync(transcription);
                }

                // 5. Add Note to Database
                _db.AddNote("Telegram Sesli Not", finalOutput, null, new List<string> { "Telegram", "Ses" });
                NoteAdded?.Invoke(this, EventArgs.Empty);

                // 6. Send Response
                var shortText = finalOutput.Length > 30 ? finalOutput[..30] + "..." : finalOutput;
                await SendTelegramMessageAsync(_chatId, $"Ses notu kaydedildi: \"{shortText}\" 🎙️");
            }
            catch (Exception ex)
            {
                await SendTelegramMessageAsync(_chatId, $"Ses işleme hatası: {ex.Message}");
            }
            finally
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch {}
            }
        }

        private async Task SendTelegramMessageAsync(long chatId, string text)
        {
            try
            {
                var url = $"https://api.telegram.org/bot{_token}/sendMessage";
                var payload = new
                {
                    chat_id = chatId,
                    text = text
                };
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                await _httpClient.PostAsync(url, content);
            }
            catch {}
        }

        private async Task ProcessGeminiQueryAsync(string text, string? attachmentPath = null, bool isImage = false)
        {
            string prompt = "";
            if (text.StartsWith("/sor", StringComparison.OrdinalIgnoreCase))
                prompt = text.Substring(4).Trim();
            else if (text.StartsWith("/gemini", StringComparison.OrdinalIgnoreCase))
                prompt = text.Substring(7).Trim();
            else if (text.StartsWith("/ask", StringComparison.OrdinalIgnoreCase))
                prompt = text.Substring(4).Trim();
            else if (text.StartsWith("/plan", StringComparison.OrdinalIgnoreCase) || 
                     text.StartsWith("/bugun", StringComparison.OrdinalIgnoreCase) || 
                     text.StartsWith("/ozet", StringComparison.OrdinalIgnoreCase))
                prompt = "Bugün için takvimi ve notlarımı özetle";

            if (string.IsNullOrWhiteSpace(prompt))
            {
                await SendTelegramMessageAsync(_chatId, "Lütfen sormak istediğiniz soruyu belirtin. Örnek:\n/sor 16.07.2026 tarihindeki etkinliklerim neler?");
                return;
            }

            await SendTelegramMessageAsync(_chatId, "Gemini yanıt hazırlıyor... ⏳");

            try
            {
                string response = "";
                string? imagePath = isImage ? attachmentPath : null;
                List<string>? filePaths = (!isImage && attachmentPath != null) ? new List<string> { attachmentPath } : null;

                if (GeminiSearchIntent.ShouldUseCalendar(prompt) && _calendarPolling != null)
                {
                    var targetDate = CalendarQueryDateResolver.Resolve(prompt, DateTime.Today);
                    
                    bool isWeeklyRange = prompt.Contains("hafta", StringComparison.OrdinalIgnoreCase) || 
                                         prompt.Contains("7 gün", StringComparison.OrdinalIgnoreCase) || 
                                         prompt.Contains("7 gun", StringComparison.OrdinalIgnoreCase);

                    string calendarSummary;
                    if (isWeeklyRange)
                    {
                        var allEvents = new List<CalendarEvent>();
                        for (int i = 0; i < 7; i++)
                        {
                            var date = targetDate.AddDays(i);
                            var dayEvents = await _calendarPolling.GetEventsForDateAsync(date, date.Date == DateTime.Today);
                            if (dayEvents != null) allEvents.AddRange(dayEvents);
                        }
                        calendarSummary = CalendarSummaryBuilder.BuildRange(targetDate, 7, allEvents);
                    }
                    else
                    {
                        var selectedEvents = await _calendarPolling.GetEventsForDateAsync(targetDate, targetDate.Date == DateTime.Today);
                        var upcomingEvents = await LoadUpcomingImportantCalendarEventsAsync(targetDate);
                        calendarSummary = CalendarSummaryBuilder.Build(targetDate, selectedEvents, upcomingEvents);
                    }

                    var databaseContext = _db.BuildGeminiDatabaseContext(prompt, 100);
                    var targetDateNotes = _db.GetNotesForDate(targetDate);
                    if (targetDateNotes != null && targetDateNotes.Count > 0)
                    {
                        var targetNotesText = string.Join(Environment.NewLine + "---" + Environment.NewLine, 
                            targetDateNotes.Select(n => $"Başlık: {n.Title}\nİçerik: {n.Text}"));
                        databaseContext = $"Hedef Tarih Notları ({targetDate:dd.MM.yyyy}):" + Environment.NewLine + targetNotesText + Environment.NewLine + Environment.NewLine + databaseContext;
                    }

                    var instruction = GeminiCalendarSearchPrompt.Build(prompt, calendarSummary, databaseContext);
                    response = await _geminiCli.SummarizeAsync(instruction, "Takvim ve veritabanı arama sonucunu hazırla.", imagePath, filePaths);
                }
                else
                {
                    var databaseContext = _db.BuildGeminiDatabaseContext(prompt, 150);
                    var instruction = GeminiDatabaseSearchPrompt.Build(prompt, databaseContext);
                    response = await _geminiCli.SummarizeAsync(instruction, "Veritabanı arama sonucunu hazırla.", imagePath, filePaths);
                }

                if (string.IsNullOrWhiteSpace(response))
                {
                    await SendTelegramMessageAsync(_chatId, "Gemini bir yanıt üretemedi.");
                }
                else
                {
                    await SendTelegramMessageAsync(_chatId, response);

                    var promptTitle = $"Gemini: {prompt}";
                    if (promptTitle.Length > 40)
                    {
                        promptTitle = promptTitle[..37] + "...";
                    }

                    var tags = new List<string> { "Telegram", "Gemini" };
                    if (attachmentPath != null)
                    {
                        tags.Add(isImage ? "Görsel" : "Belge");
                    }

                    try
                    {
                        _db.AddNote(promptTitle, response, attachmentPath, tags);
                        NoteAdded?.Invoke(this, EventArgs.Empty);
                    }
                    catch {}
                }
            }
            catch (Exception ex)
            {
                await SendTelegramMessageAsync(_chatId, $"Hata oluştu: {ex.Message}");
            }
        }

        private async Task<List<CalendarEvent>> LoadUpcomingImportantCalendarEventsAsync(DateTime startDate)
        {
            var importantEvents = new List<CalendarEvent>();
            if (_calendarPolling == null) return importantEvents;

            for (var dayOffset = 0; dayOffset < 4; dayOffset++)
            {
                var date = startDate.Date.AddDays(dayOffset);
                var events = await _calendarPolling.GetEventsForDateAsync(date, date.Date == DateTime.Today);
                if (events != null)
                {
                    importantEvents.AddRange(events.Where(ImportantCalendarEventPolicy.IsImportant));
                }
            }

            return importantEvents
                .GroupBy(calendarEvent => string.IsNullOrWhiteSpace(calendarEvent.ExternalId)
                    ? $"{calendarEvent.Summary}|{calendarEvent.StartTime:O}|{calendarEvent.EndTime:O}"
                    : calendarEvent.ExternalId)
                .Select(group => group.First())
                .OrderBy(calendarEvent => calendarEvent.StartTime)
                .ToList();
        }

        private async Task<string?> DownloadPhotoHelperAsync(JsonElement photoArray, CancellationToken cancellationToken)
        {
            var lastIndex = photoArray.GetArrayLength() - 1;
            var photoObj = photoArray[lastIndex];

            if (!photoObj.TryGetProperty("file_id", out var fileIdProp))
                return null;

            string fileId = fileIdProp.GetString() ?? "";

            var getFileUrl = $"https://api.telegram.org/bot{_token}/getFile?file_id={fileId}";
            var getFileResp = await _httpClient.GetAsync(getFileUrl, cancellationToken);
            if (!getFileResp.IsSuccessStatusCode) return null;

            var jsonString = await getFileResp.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(jsonString);
            if (!doc.RootElement.TryGetProperty("ok", out var okProp) || !okProp.GetBoolean() ||
                !doc.RootElement.TryGetProperty("result", out var resultProp) ||
                !resultProp.TryGetProperty("file_path", out var filePathProp))
            {
                return null;
            }

            string filePath = filePathProp.GetString() ?? "";
            var extension = Path.GetExtension(filePath);
            if (string.IsNullOrWhiteSpace(extension)) extension = ".jpg";

            var fileUrl = $"https://api.telegram.org/file/bot{_token}/{filePath}";
            var targetFile = Path.Combine(_db.AttachmentsDirectory, $"telegram_photo_{Guid.NewGuid():N}{extension}");

            using (var fileStream = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using var response = await _httpClient.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode) return null;
                await response.Content.CopyToAsync(fileStream, cancellationToken);
            }

            return targetFile;
        }

        private async Task<string?> DownloadDocumentHelperAsync(JsonElement docElement, CancellationToken cancellationToken)
        {
            if (!docElement.TryGetProperty("file_id", out var fileIdProp))
                return null;

            string fileId = fileIdProp.GetString() ?? "";
            
            string originalFileName = "belge";
            if (docElement.TryGetProperty("file_name", out var fileNameProp))
            {
                originalFileName = fileNameProp.GetString() ?? "belge";
            }

            var getFileUrl = $"https://api.telegram.org/bot{_token}/getFile?file_id={fileId}";
            var getFileResp = await _httpClient.GetAsync(getFileUrl, cancellationToken);
            if (!getFileResp.IsSuccessStatusCode) return null;

            var jsonString = await getFileResp.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(jsonString);
            if (!doc.RootElement.TryGetProperty("ok", out var okProp) || !okProp.GetBoolean() ||
                !doc.RootElement.TryGetProperty("result", out var resultProp) ||
                !resultProp.TryGetProperty("file_path", out var filePathProp))
            {
                return null;
            }

            string filePath = filePathProp.GetString() ?? "";
            var extension = Path.GetExtension(filePath);
            if (string.IsNullOrWhiteSpace(extension)) extension = Path.GetExtension(originalFileName);
            if (string.IsNullOrWhiteSpace(extension)) extension = ".dat";

            var fileUrl = $"https://api.telegram.org/file/bot{_token}/{filePath}";
            var safeCleanName = string.Concat(originalFileName.Split(Path.GetInvalidFileNameChars()));
            var targetFile = Path.Combine(_db.AttachmentsDirectory, $"telegram_file_{Guid.NewGuid():N}_{safeCleanName}");

            using (var fileStream = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using var response = await _httpClient.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode) return null;
                await response.Content.CopyToAsync(fileStream, cancellationToken);
            }

            return targetFile;
        }

        private async Task ProcessPhotoMsgAsync(JsonElement photoArray, string caption, CancellationToken cancellationToken)
        {
            await SendTelegramMessageAsync(_chatId, "Görsel alındı, indiriliyor... 📸");
            var targetFile = await DownloadPhotoHelperAsync(photoArray, cancellationToken);
            if (targetFile == null)
            {
                await SendTelegramMessageAsync(_chatId, "Hata: Görsel indirilemedi.");
                return;
            }

            string title = "Telegram Görseli";
            if (!string.IsNullOrWhiteSpace(caption))
            {
                title = caption.Split('\n')[0].Trim();
                title = title.Length > 30 ? title[..30] + "..." : title;
            }

            string noteText = string.IsNullOrWhiteSpace(caption) ? "Telegram'dan gönderilen görsel." : caption;

            try
            {
                _db.AddNote(title, noteText, targetFile, new List<string> { "Telegram", "Görsel" });
                NoteAdded?.Invoke(this, EventArgs.Empty);
                await SendTelegramMessageAsync(_chatId, $"Görsel notu başarıyla kaydedildi: \"{title}\" 🖼️");
            }
            catch (Exception ex)
            {
                await SendTelegramMessageAsync(_chatId, $"Görsel kaydedilirken hata oluştu: {ex.Message}");
            }
        }

        private async Task ProcessDocumentMsgAsync(JsonElement docElement, string caption, CancellationToken cancellationToken)
        {
            string originalFileName = "belge";
            if (docElement.TryGetProperty("file_name", out var fileNameProp))
            {
                originalFileName = fileNameProp.GetString() ?? "belge";
            }

            string mimeType = "";
            if (docElement.TryGetProperty("mime_type", out var mimeProp))
            {
                mimeType = mimeProp.GetString() ?? "";
            }

            await SendTelegramMessageAsync(_chatId, $"Dosya alindi ({originalFileName}), indiriliyor... \ud83d\udce5");
            var targetFile = await DownloadDocumentHelperAsync(docElement, cancellationToken);
            if (targetFile == null)
            {
                await SendTelegramMessageAsync(_chatId, "Hata: Dosya indirilemedi.");
                return;
            }

            string title = originalFileName;
            if (!string.IsNullOrWhiteSpace(caption))
            {
                title = caption.Split('\n')[0].Trim();
                title = title.Length > 30 ? title[..30] + "..." : title;
            }

            string noteText = string.IsNullOrWhiteSpace(caption) 
                ? $"Telegram'dan gonderilen dosya: {originalFileName}" 
                : $"{caption}\n\n(Dosya: {originalFileName})";

            var isImage = mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
            var tags = isImage 
                ? new List<string> { "Telegram", "Gorsel" }
                : new List<string> { "Telegram", "Belge" };

            try
            {
                _db.AddNote(title, noteText, targetFile, tags);
                NoteAdded?.Invoke(this, EventArgs.Empty);
                await SendTelegramMessageAsync(_chatId, $"Dosya basariyla kaydedildi: \"{title}\" \ud83d\udcc1");
            }
            catch (Exception ex)
            {
                await SendTelegramMessageAsync(_chatId, $"Dosya kaydedilirken hata olustu: {ex.Message}");
            }
        }

        public void Dispose()
        {
            Stop();
            _httpClient.Dispose();
        }
    }
}
