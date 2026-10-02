using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace QuickNoteApp.Services;

public class GeminiCliService
{
    public static string? ApiKey { get; set; }

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);
    private const string DefaultModelName = "";

    public async Task<string> SummarizeAsync(string instruction, string noteText, string? imagePath = null, IReadOnlyList<string>? filePaths = null)
    {
        var hasFiles = filePaths?.Any(File.Exists) == true;
        if (string.IsNullOrWhiteSpace(noteText) && string.IsNullOrWhiteSpace(imagePath) && !hasFiles)
            return "Gemini'ye gönderilecek metin, resim veya dosya yok.";

        var geminiCommand = ResolveGeminiCommand();
        if (geminiCommand == null)
            return "Antigravity CLI (agy) bulunamadı. Terminalde 'agy --version' çalıştığını kontrol et.";

        var workDir = CreateWorkDirectory();
        try
        {
            var references = new List<string>();
            AddCopiedFileReference(imagePath, workDir, references, "image");

            if (filePaths != null)
            {
                foreach (var filePath in filePaths.Where(File.Exists))
                    AddAttachmentReference(filePath, workDir, references);
            }

            // Keep track of the initial files in workDir
            var initialFiles = Directory.GetFiles(workDir).Select(Path.GetFileName).ToHashSet();

            var payload = BuildPayload(instruction, noteText, references);
            var result = await RunGeminiAsync(workDir, payload, geminiCommand, DefaultModelName);

            // After execution, check for any newly created files in workDir
            var currentFiles = Directory.GetFiles(workDir);
            var newlyCreatedFiles = new List<string>();

            var destDir = (filePaths != null && filePaths.Any(File.Exists))
                ? Path.GetDirectoryName(filePaths.First(File.Exists))
                : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            if (!string.IsNullOrWhiteSpace(destDir) && Directory.Exists(destDir))
            {
                foreach (var file in currentFiles)
                {
                    var fileName = Path.GetFileName(file);
                    if (!initialFiles.Contains(fileName))
                    {
                        var destPath = Path.Combine(destDir, fileName);
                        try
                        {
                            File.Copy(file, destPath, overwrite: true);
                            newlyCreatedFiles.Add(destPath);
                        }
                        catch (Exception)
                        {
                            // Ignore copy errors
                        }
                    }
                }
            }

            if (newlyCreatedFiles.Count > 0)
            {
                result += $"\n\n[Sistem]: Aşağıdaki yeni dosyalar üretildi ve kaydedildi:\n" + string.Join("\n", newlyCreatedFiles.Select(f => $"- {f}"));
            }

            return result;
        }
        finally
        {
            TryDeleteDirectory(workDir);
        }
    }

    public async Task<string> GenerateTaskListAsync(string dailyContext)
    {
        if (string.IsNullOrWhiteSpace(dailyContext))
            return "Görev listesi için kaynak veri bulunamadı.";

        var geminiCommand = ResolveGeminiCommand();
        if (geminiCommand == null)
            return "Antigravity CLI (agy) bulunamadı. Terminalde 'agy --version' çalıştığını kontrol et.";

        var workDir = CreateWorkDirectory();
        try
        {
            return await RunGeminiAsync(workDir, dailyContext, geminiCommand, DefaultModelName);
        }
        finally
        {
            TryDeleteDirectory(workDir);
        }
    }

    public async Task<string> WebSearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "Arama sorgusu boş olamaz.";

        var geminiCommand = ResolveGeminiCommand();
        if (geminiCommand == null)
            return "Antigravity CLI (agy) bulunamadı.";

        var workDir = CreateWorkDirectory();
        try
        {
            var payload = new StringBuilder();
            payload.AppendLine("=== SYSTEM INSTRUCTIONS ===");
            payload.AppendLine("You MUST use the web_search tool to answer the following question. Do NOT answer from your training data alone.");
            payload.AppendLine("Respond in the SAME language as the user's query.");
            payload.AppendLine("Include source URLs where applicable.");
            payload.AppendLine("==========================");
            payload.AppendLine();
            payload.AppendLine(query.Trim());

            return await RunGeminiAsync(workDir, payload.ToString().Trim(), geminiCommand, DefaultModelName);
        }
        finally
        {
            TryDeleteDirectory(workDir);
        }
    }

    public async Task<string> TranscribeAudioAsync(string audioPath)
    {
        if (string.IsNullOrWhiteSpace(audioPath) || !File.Exists(audioPath))
            return "Ses dosyası bulunamadı.";

        var geminiCommand = ResolveGeminiCommand();
        if (geminiCommand == null)
            return "Antigravity CLI (agy) bulunamadı. Terminalde 'agy --version' çalıştığını kontrol et.";

        var workDir = CreateWorkDirectory();
        try
        {
            var references = new List<string>();
            AddCopiedFileReference(audioPath, workDir, references, "dikte");
            var audioFileName = references
                .Select(reference => reference.Split('@').LastOrDefault()?.Trim())
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));

            if (string.IsNullOrWhiteSpace(audioFileName))
                return "Ses dosyası Gemini için hazırlanamadı.";

            var copiedAudioPath = Path.Combine(workDir, audioFileName);
            var payload = BuildAudioPayload(copiedAudioPath);
            return await RunGeminiAsync(workDir, payload, geminiCommand, GeminiTranscriptionPrompt.ModelName);
        }
        finally
        {
            TryDeleteDirectory(workDir);
        }
    }

    public async Task<string> RefineDictationAsync(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return string.Empty;

        var geminiCommand = ResolveGeminiCommand();
        if (geminiCommand == null)
            return rawText;

        var workDir = CreateWorkDirectory();
        try
        {
            var payload = GeminiRefinementPrompt.Build(rawText);
            var result = await RunGeminiAsync(workDir, payload, geminiCommand, GeminiRefinementPrompt.ModelName);
            if (string.IsNullOrWhiteSpace(result) || result.StartsWith("Gemini CLI", StringComparison.OrdinalIgnoreCase))
            {
                return rawText;
            }
            return result;
        }
        catch
        {
            return rawText;
        }
        finally
        {
            TryDeleteDirectory(workDir);
        }
    }

    public static bool IsGeminiCliAvailable()
    {
        return ResolveGeminiCommand() != null;
    }

    public static async Task<(bool Success, string Version, string Error)> TestCliAsync()
    {
        var cmd = ResolveGeminiCommand();
        if (cmd == null)
            return (false, "", "Antigravity CLI (agy) bulunamadı.");

        try
        {
            var isAgy = cmd.Contains("agy", StringComparison.OrdinalIgnoreCase);
            var commandStr = cmd;
            var args = "--version";
            
            var psi = new ProcessStartInfo
            {
                FileName = commandStr.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ? "cmd.exe" : commandStr,
                Arguments = commandStr.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ? $"/d /c \"\"{commandStr}\" {args}\"" : args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var p = Process.Start(psi);
            if (p == null)
                return (false, "", "İşlem başlatılamadı.");

            // Start reading streams immediately to prevent standard stream buffer deadlock
            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)); // 15s timeout
            try
            {
                await p.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { p.Kill(); } catch {}
                return (false, "", "Zaman aşımı (15 saniye doldu).");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (p.ExitCode == 0)
            {
                var ver = stdout.Trim();
                if (string.IsNullOrWhiteSpace(ver)) ver = "Bilinmeyen Sürüm";
                return (true, ver, "");
            }
            else
            {
                return (false, "", string.IsNullOrWhiteSpace(stderr) ? "Çıkış kodu sıfır değil." : stderr.Trim());
            }
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message);
        }
    }

    public static string BuildCommand(string geminiCommand, string modelName)
    {
        var command = string.IsNullOrWhiteSpace(geminiCommand) ? "agy" : geminiCommand;
        var model = string.IsNullOrWhiteSpace(modelName) ? DefaultModelName : modelName;

        if (command.Contains("agy", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(model) ? "--dangerously-skip-permissions --print prompt.txt" : $"--dangerously-skip-permissions --model {model} --print prompt.txt";
        }

        return command.Equals("gemini", StringComparison.OrdinalIgnoreCase)
            ? $"gemini -y -m {model}"
            : $"call \"{command}\" -y -m {model}";
    }

    public static string BuildAudioPayload(string audioPath)
    {
        var cleanPath = Path.GetFullPath(audioPath).Replace('\\', '/');
        if (cleanPath.Length > 2 && cleanPath[1] == ':')
        {
            cleanPath = cleanPath[2..];
        }

        return $"""
        Talimat:
        {GeminiTranscriptionPrompt.Build()}

        Ses dosyası:
        {cleanPath}
        """;
    }

    private static string BuildPayload(string instruction, string noteText, IReadOnlyList<string> fileReferences)
    {
        var builder = new StringBuilder();
        builder.AppendLine("=== SYSTEM INSTRUCTIONS ===");
        builder.AppendLine("1. Respond STRICTLY in the same language as the user's instruction/prompt (e.g., if instruction is in Turkish, respond in Turkish).");
        builder.AppendLine("2. Never create, write, save, convert, or export files. QuickNoteApp will create files from your returned content.");
        builder.AppendLine("3. Do not claim that a file was saved, written, exported, or placed on the desktop. Return only the content that should go into the file.");
        builder.AppendLine("4. Use the attached files directly. For PDFs/images, read the visible text and tables from the attachment and return clean output for the user request.");
        builder.AppendLine("5. If the user asks for a Word/RTF document, return the final document text only. If the user asks for Excel/table output, return clean CSV rows only.");
        builder.AppendLine("6. For simple web questions, use web_search when current information is needed. Keep answers short and useful.");
        builder.AppendLine("==========================");
        builder.AppendLine();
        builder.AppendLine("Talimat:");
        builder.AppendLine(string.IsNullOrWhiteSpace(instruction) ? "Notu özetle. Görsel veya dosya varsa içeriğini oku." : instruction.Trim());
        builder.AppendLine();

        if (!string.IsNullOrWhiteSpace(noteText))
        {
            builder.AppendLine("Not içeriği:");
            builder.AppendLine(noteText.Trim());
            builder.AppendLine();
        }

        if (fileReferences.Count > 0)
        {
            builder.AppendLine("Ek dosyalar:");
            foreach (var reference in fileReferences)
                builder.AppendLine(reference);
            builder.AppendLine("Bu dosyaları talimata göre kullan.");
        }

        return builder.ToString().Trim();
    }

    private static string CreateWorkDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var root = Path.Combine(appData, "QuickNoteApp", "GeminiWork");
        Directory.CreateDirectory(root);

        var workDir = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        return workDir;
    }

    private static void AddAttachmentReference(string filePath, string workDir, List<string> references)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        if (extension == ".xlsx")
        {
            var tableText = ConvertXlsxToText(filePath);
            var textPath = Path.Combine(workDir, MakeSafeFileName(Path.GetFileNameWithoutExtension(filePath)) + "-excel.txt");
            File.WriteAllText(textPath, tableText, new UTF8Encoding(false));
            references.Add($"- Excel verisi: @{Path.GetFileName(textPath)}");
            return;
        }

        AddCopiedFileReference(filePath, workDir, references, MakeSafeFileName(Path.GetFileNameWithoutExtension(filePath)));
    }

    private static void AddCopiedFileReference(string? filePath, string workDir, List<string> references, string fallbackName)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return;

        var extension = Path.GetExtension(filePath);
        if (string.IsNullOrWhiteSpace(extension))
            extension = ".txt";

        var safeName = MakeSafeFileName(fallbackName) + extension.ToLowerInvariant();
        var targetPath = Path.Combine(workDir, safeName);
        File.Copy(filePath, targetPath, overwrite: true);
        references.Add($"- {Path.GetFileName(filePath)}: @{safeName}");
    }

    private static string ConvertXlsxToText(string filePath)
    {
        using var archive = ZipFile.OpenRead(filePath);
        var sharedStrings = ReadSharedStrings(archive);
        var builder = new StringBuilder();

        foreach (var sheetEntry in archive.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e.FullName))
        {
            builder.AppendLine($"Sayfa: {Path.GetFileNameWithoutExtension(sheetEntry.Name)}");
            using var stream = sheetEntry.Open();
            var doc = XDocument.Load(stream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            foreach (var row in doc.Descendants(ns + "row"))
            {
                var cells = row.Elements(ns + "c")
                    .Select(cell => ReadCellValue(cell, sharedStrings, ns))
                    .ToList();

                if (cells.Any(value => !string.IsNullOrWhiteSpace(value)))
                    builder.AppendLine(string.Join("\t", cells));
            }

            builder.AppendLine();
        }

        var text = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(text) ? "Excel dosyasından okunabilir tablo verisi çıkarılamadı." : text;
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry == null)
            return new List<string>();

        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return doc.Descendants(ns + "si")
            .Select(si => string.Concat(si.Descendants(ns + "t").Select(t => t.Value)))
            .ToList();
    }

    private static string ReadCellValue(XElement cell, List<string> sharedStrings, XNamespace ns)
    {
        var type = cell.Attribute("t")?.Value;
        var value = cell.Element(ns + "v")?.Value ?? string.Empty;

        if (type == "s" && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) && index >= 0 && index < sharedStrings.Count)
            return sharedStrings[index];

        if (type == "inlineStr")
            return string.Concat(cell.Descendants(ns + "t").Select(t => t.Value));

        return value;
    }

    public static string? ResolveGeminiCommand()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        var directCandidates = new[]
        {
            Path.Combine(localAppData, "agy", "bin", "agy.exe"),
            Path.Combine(userProfile, "AppData", "Local", "agy", "bin", "agy.exe"),
            Path.Combine(appData, "npm", "agy.cmd"),
            Path.Combine(appData, "npm", "agy.exe"),
            Path.Combine(appData, "npm", "gemini.cmd"),
            Path.Combine(userProfile, "AppData", "Roaming", "npm", "gemini.cmd")
        };

        foreach (var candidate in directCandidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        if (CommandExistsInPath("agy")) return "agy";
        if (CommandExistsInPath("gemini")) return "gemini";

        return null;
    }

    private static bool CommandExistsInPath(string command)
    {
        try
        {
            using var process = StartProcess("cmd.exe", $"/d /c where {command}", Environment.CurrentDirectory, redirectInput: false);
            return process != null && process.WaitForExit(5000) && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string> RunGeminiAsync(string workDir, string payload, string geminiCommand, string modelName)
    {
        var model = string.IsNullOrWhiteSpace(modelName) ? DefaultModelName : modelName;
        
        var promptFilePath = Path.Combine(workDir, "prompt.txt");
        File.WriteAllText(promptFilePath, payload, new UTF8Encoding(false));

        string fileName;
        string arguments;
        bool redirectInput = true;

        bool isAgy = geminiCommand.Contains("agy", StringComparison.OrdinalIgnoreCase);

        if (isAgy)
        {
            fileName = geminiCommand.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ? "cmd.exe" : geminiCommand;
            var agyArgs = BuildCommand(geminiCommand, model);

            arguments = geminiCommand.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
                ? $"/d /c \"\"{geminiCommand}\" {agyArgs}\""
                : agyArgs;
        }
        else
        {
            var command = BuildCommand(geminiCommand, model);
            fileName = "cmd.exe";
            arguments = $"/d /c {command}";
        }

        using var process = StartProcess(fileName, arguments, workDir, redirectInput);
        if (process == null)
            return "CLI başlatılamadı.";

        // Start reading output streams in the background before writing to stdin to prevent deadlocks
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        if (redirectInput)
        {
            await process.StandardInput.WriteAsync(payload);
            await process.StandardInput.FlushAsync();
            process.StandardInput.Close();
        }

        var waitTask = process.WaitForExitAsync();
        var completedTask = await Task.WhenAny(waitTask, Task.Delay(Timeout));

        if (completedTask != waitTask)
        {
            TryKill(process);
            var partialOutput = "";
            var partialError = "";
            try
            {
                partialOutput = CleanOutput(await outputTask);
                partialError = CleanOutput(await errorTask);
            }
            catch {}

            var statusMsg = "CLI zaman aşımına uğradı.";
            if (!string.IsNullOrWhiteSpace(partialOutput))
                statusMsg += "\nÇıktı:\n" + partialOutput;
            if (!string.IsNullOrWhiteSpace(partialError))
                statusMsg += "\nHata:\n" + partialError;

            return statusMsg;
        }

        var output = CleanOutput((await outputTask).Trim());
        var error = CleanError((await errorTask).Trim());

        if (!string.IsNullOrWhiteSpace(output))
            return output;

        if (!string.IsNullOrWhiteSpace(error))
        {
            if (error.Contains("IneligibleTierError", StringComparison.OrdinalIgnoreCase) || error.Contains("UNSUPPORTED_CLIENT", StringComparison.OrdinalIgnoreCase))
            {
                return "Google Oturum Hatası: Google, Gemini Code Assist bireysel ücretsiz CLI oturumlarını kapattı.\nÇözüm: Lütfen https://aistudio.google.com/app/apikey adresinden ücretsiz bir Gemini API Anahtarı alın ve GEMINI_API_KEY ortam değişkenini tanımlayın.";
            }
            return "CLI hata verdi: " + error;
        }

        return $"CLI boş cevap döndürdü. (Çıkış Kodu: {process.ExitCode})";
    }

    private static Process? StartProcess(string fileName, string arguments, string workingDirectory, bool redirectInput)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = redirectInput,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (redirectInput)
        {
            psi.StandardInputEncoding = Encoding.UTF8;
        }

        var key = ApiKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            key = Environment.GetEnvironmentVariable("GEMINI_API_KEY", EnvironmentVariableTarget.User)
               ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY", EnvironmentVariableTarget.Process)
               ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY", EnvironmentVariableTarget.Machine);
        }

        if (!string.IsNullOrWhiteSpace(key))
        {
            psi.EnvironmentVariables["GEMINI_API_KEY"] = key;
        }

        return Process.Start(psi);
    }

    private static string CleanOutput(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        return text
            .Replace("Data collection is disabled.", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    private static string CleanError(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !line.StartsWith("Warning:", StringComparison.OrdinalIgnoreCase))
            .Where(line => !line.StartsWith("Info:", StringComparison.OrdinalIgnoreCase))
            .Where(line => !line.Contains("Windows 10 detected", StringComparison.OrdinalIgnoreCase))
            .Where(line => !line.Contains("Data collection is disabled", StringComparison.OrdinalIgnoreCase));

        return string.Join("\n", lines).Trim();
    }

    private static string MakeSafeFileName(string value)
    {
        var safe = string.IsNullOrWhiteSpace(value) ? "dosya" : value.Trim();
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
            safe = safe.Replace(invalidChar, '-');

        return safe.Length <= 50 ? safe : safe[..50];
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }
}
