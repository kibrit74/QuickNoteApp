using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace QuickNoteApp.Services;

public class GeminiCliService
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(25);
    private const string DefaultModelName = "gemini-2.5-flash";

    public async Task<string> SummarizeAsync(string instruction, string noteText, string? imagePath = null, IReadOnlyList<string>? filePaths = null)
    {
        var hasFiles = filePaths?.Any(File.Exists) == true;
        if (string.IsNullOrWhiteSpace(noteText) && string.IsNullOrWhiteSpace(imagePath) && !hasFiles)
            return "Gemini'ye gönderilecek metin, resim veya dosya yok.";

        var geminiCommand = ResolveGeminiCommand();
        if (geminiCommand == null)
            return "Gemini CLI bulunamadı. Terminalde 'gemini --version' çalıştığını kontrol et.";

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

            var payload = BuildPayload(instruction, noteText, references);
            return await RunGeminiAsync(workDir, payload, geminiCommand, DefaultModelName);
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
            return "Gemini CLI bulunamadı. Terminalde 'gemini --version' çalıştığını kontrol et.";

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

    public async Task<string> TranscribeAudioAsync(string audioPath)
    {
        if (string.IsNullOrWhiteSpace(audioPath) || !File.Exists(audioPath))
            return "Ses dosyası bulunamadı.";

        var geminiCommand = ResolveGeminiCommand();
        if (geminiCommand == null)
            return "Gemini CLI bulunamadı. Terminalde 'gemini --version' çalıştığını kontrol et.";

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

    public static string BuildCommand(string geminiCommand, string modelName)
    {
        var command = string.IsNullOrWhiteSpace(geminiCommand) ? "gemini" : geminiCommand;
        var model = string.IsNullOrWhiteSpace(modelName) ? DefaultModelName : modelName;

        return command.Equals("gemini", StringComparison.OrdinalIgnoreCase)
            ? $"gemini -m {model}"
            : $"call \"{command}\" -m {model}";
    }

    public static bool IsGeminiCliAvailable()
    {
        return ResolveGeminiCommand() != null;
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
        Ses dosyası:
        {cleanPath}
        """;
    }

    private static string BuildPayload(string instruction, string noteText, IReadOnlyList<string> fileReferences)
    {
        var builder = new StringBuilder();
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

    private static string? ResolveGeminiCommand()
    {
        var directCandidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "gemini.cmd"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Roaming", "npm", "gemini.cmd")
        };

        foreach (var candidate in directCandidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return CommandExistsInPath("gemini") ? "gemini" : null;
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
        var command = BuildCommand(geminiCommand, modelName);

        using var process = StartProcess("cmd.exe", "/d /c " + command, workDir, redirectInput: true);
        if (process == null)
            return "Gemini CLI başlatılamadı.";

        await process.StandardInput.WriteAsync(payload);
        await process.StandardInput.FlushAsync();
        process.StandardInput.Close();

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        var waitTask = process.WaitForExitAsync();
        var completedTask = await Task.WhenAny(waitTask, Task.Delay(Timeout));

        if (completedTask != waitTask)
        {
            TryKill(process);
            return "Gemini CLI zaman aşımına uğradı. Terminalde oturum açmak gerekebilir.";
        }

        var output = CleanOutput((await outputTask).Trim());
        var error = CleanOutput((await errorTask).Trim());

        if (!string.IsNullOrWhiteSpace(output))
            return output;

        if (!string.IsNullOrWhiteSpace(error))
            return "Gemini CLI hata verdi: " + error;

        return $"Gemini CLI boş cevap döndürdü. (Çıkış Kodu: {process.ExitCode})";
    }

    private static Process? StartProcess(string fileName, string arguments, string workingDirectory, bool redirectInput)
    {
        return Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = redirectInput,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        });
    }

    private static string CleanOutput(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        return text
            .Replace("Data collection is disabled.", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
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

