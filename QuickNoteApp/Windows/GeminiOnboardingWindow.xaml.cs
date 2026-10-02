using System.Diagnostics;
using System.IO;
using System.Windows;
using QuickNoteApp.Services;

namespace QuickNoteApp.Windows;

public partial class GeminiOnboardingWindow : Window
{
    private const string NodeInstallCommand = "winget install --id OpenJS.NodeJS.LTS --source winget";
    private const string InstallCommand = "agy";
    private const string LoginCommand = "agy";

    private static readonly string StateDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "QuickNoteApp");

    private static readonly string SeenFlagPath = Path.Combine(StateDirectory, "gemini-onboarding-seen.flag");

    public GeminiOnboardingWindow()
    {
        InitializeComponent();
        RefreshGeminiStatus();
    }

    public static void ShowFirstRunIfNeeded(Window owner)
    {
        if (File.Exists(SeenFlagPath))
            return;

        var window = new GeminiOnboardingWindow { Owner = owner };
        window.ShowDialog();
    }

    private void RunInstallerButton_Click(object sender, RoutedEventArgs e)
    {
        var scriptPath = FindInstallerScript();
        if (scriptPath == null)
        {
            CopyToClipboard(InstallCommand, "Kurulum dosyası bulunamadı. Komut panoya kopyalandı.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = true
            });

            CopyStatusText.Text = "Kontrol penceresi açıldı. İşlem bitince terminalde agy yazıp oturum açın.";
        }
        catch (Exception ex)
        {
            CopyToClipboard(InstallCommand, "Pencere açılamadı. Komut panoya kopyalandı.");
            System.Windows.MessageBox.Show(this, ex.Message, "Antigravity CLI (agy) kurulumu açılamadı", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CopyNodeInstallCommandButton_Click(object sender, RoutedEventArgs e)
    {
        CopyToClipboard(NodeInstallCommand, "Node.js kurulum komutu panoya kopyalandı.");
    }
    private void CopyInstallCommandButton_Click(object sender, RoutedEventArgs e)
    {
        CopyToClipboard(InstallCommand, "Kurulum komutu panoya kopyalandı.");
    }

    private void CopyLoginCommandButton_Click(object sender, RoutedEventArgs e)
    {
        CopyToClipboard(LoginCommand, "Oturum açma komutu panoya kopyalandı.");
    }

    private void CloseAndRememberButton_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(StateDirectory);
        File.WriteAllText(SeenFlagPath, DateTimeOffset.Now.ToString("O"));
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void RefreshGeminiStatus()
    {
        GeminiStatusText.Text = GeminiCliService.IsGeminiCliAvailable()
            ? "Antigravity CLI (agy) bulundu. QuickNoteApp hazır."
            : "Antigravity CLI (agy) henüz bulunamadı. Lütfen agy kurulumunu tamamlayın.";
    }

    private void CopyToClipboard(string text, string status)
    {
        System.Windows.Clipboard.SetText(text);
        CopyStatusText.Text = status;
    }

    private static string? FindInstallerScript()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "Tools", "install-gemini-cli.ps1"),
            Path.Combine(baseDirectory, "install-gemini-cli.ps1")
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}