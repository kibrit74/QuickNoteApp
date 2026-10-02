using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using QuickNoteApp.Services;

namespace QuickNoteApp.Windows
{
    public partial class SetupWizardWindow : Window
    {
        private readonly DatabaseService _db;
        private readonly GoogleAuthService _googleAuth;
        private readonly NotificationListenerService _notificationListener;

        private bool _isGeminiCliOk;
        private bool _isGeminiLoginOk;
        private bool _isCalendarOk;
        private bool _isMicrophoneOk;
        private bool _isNotificationOk;

        public SetupWizardWindow(DatabaseService db, GoogleAuthService googleAuth, NotificationListenerService notificationListener)
        {
            InitializeComponent();
            _db = db;
            _googleAuth = googleAuth;
            _notificationListener = notificationListener;

            Loaded += async (s, e) => await RunChecksAsync();
        }

        public async Task RunChecksAsync()
        {
            try
            {
                RecheckBtn.IsEnabled = false;
                RecheckBtn.Content = "🔍 Denetleniyor...";

                // 1. Antigravity CLI (agy)
                var geminiCmd = GeminiCliService.ResolveGeminiCommand();
                if (geminiCmd == null)
                {
                    _isGeminiCliOk = false;
                    SetStatus(GeminiCliDot, GeminiCliText, "Eksik (Antigravity CLI (agy) kurulu değil)", Colors.Red);
                    GeminiCliBtn.Visibility = Visibility.Visible;
                }
                else
                {
                    _isGeminiCliOk = true;
                    SetStatus(GeminiCliDot, GeminiCliText, "Kurulu (agy)", Colors.Green);
                    GeminiCliBtn.Visibility = Visibility.Collapsed;
                }

                // 2. Antigravity CLI Login
                if (!_isGeminiCliOk)
                {
                    _isGeminiLoginOk = false;
                    SetStatus(GeminiLoginDot, GeminiLoginText, "Giriş Gerekli (agy kurulu değil)", Colors.Red);
                    GeminiLoginBtn.Visibility = Visibility.Collapsed;
                }
                else
                {
                    var (success, version, error) = await GeminiCliService.TestCliAsync();
                    if (success)
                    {
                        _isGeminiLoginOk = true;
                        SetStatus(GeminiLoginDot, GeminiLoginText, $"Hazır ({version})", Colors.Green);
                        GeminiLoginBtn.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        _isGeminiLoginOk = false;
                        SetStatus(GeminiLoginDot, GeminiLoginText, "Giriş Yapılmamış", Colors.Red);
                        GeminiLoginBtn.Visibility = Visibility.Visible;
                    }
                }

                // 3. Google Calendar
                var authState = _googleAuth.GetState();
                if (authState.IsSignedIn)
                {
                    if (authState.IsExpired)
                    {
                        _isCalendarOk = false;
                        SetStatus(CalendarDot, CalendarText, "Bağlı (Oturum Süresi Dolmuş)", Colors.Yellow);
                        CalendarBtn.Content = "Yenile";
                        CalendarBtn.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        _isCalendarOk = true;
                        SetStatus(CalendarDot, CalendarText, $"Bağlı ({authState.Email})", Colors.Green);
                        CalendarBtn.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    _isCalendarOk = false;
                    SetStatus(CalendarDot, CalendarText, "Bağlantı Yok", Colors.Red);
                    CalendarBtn.Content = "Bağlan";
                    CalendarBtn.Visibility = Visibility.Visible;
                }

                // 4. Microphone Permission
                try
                {
                    var settings = new global::Windows.Media.Capture.MediaCaptureInitializationSettings
                    {
                        StreamingCaptureMode = global::Windows.Media.Capture.StreamingCaptureMode.Audio
                    };
                    using (var capture = new global::Windows.Media.Capture.MediaCapture())
                    {
                        await capture.InitializeAsync(settings);
                    }
                    _isMicrophoneOk = true;
                    SetStatus(MicrophoneDot, MicrophoneText, "İzin Verildi", Colors.Green);
                    MicrophoneBtn.Visibility = Visibility.Collapsed;
                }
                catch (UnauthorizedAccessException)
                {
                    _isMicrophoneOk = false;
                    SetStatus(MicrophoneDot, MicrophoneText, "İzin Verilmedi", Colors.Red);
                    MicrophoneBtn.Visibility = Visibility.Visible;
                }
                catch (Exception)
                {
                    _isMicrophoneOk = true; // Non-critical fallback for missing mic/testing
                    SetStatus(MicrophoneDot, MicrophoneText, "Kullanımda veya mikrofon yok", Colors.Orange);
                    MicrophoneBtn.Visibility = Visibility.Visible;
                }

                // 5. Windows Notification Permission
                try
                {
                    var listener = global::Windows.UI.Notifications.Management.UserNotificationListener.Current;
                    var status = listener.GetAccessStatus();
                    if (status == global::Windows.UI.Notifications.Management.UserNotificationListenerAccessStatus.Allowed)
                    {
                        _isNotificationOk = true;
                        SetStatus(NotificationDot, NotificationText, "İzin Verildi", Colors.Green);
                        NotificationBtn.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        _isNotificationOk = false;
                        SetStatus(NotificationDot, NotificationText, $"İzin Verilmedi ({status})", Colors.Red);
                        NotificationBtn.Visibility = Visibility.Visible;
                    }
                }
                catch (Exception ex)
                {
                    _isNotificationOk = false;
                    SetStatus(NotificationDot, NotificationText, $"Hata: {ex.Message}", Colors.Red);
                    NotificationBtn.Visibility = Visibility.Collapsed;
                }

                // 6. Outlook
                try
                {
                    var outlookType = Type.GetTypeFromProgID("Outlook.Application");
                    if (outlookType == null)
                    {
                        SetStatus(OutlookDot, OutlookText, "Kurulu değil (Opsiyonel)", Colors.Gray);
                    }
                    else
                    {
                        SetStatus(OutlookDot, OutlookText, "Hazır", Colors.Green);
                    }
                }
                catch (Exception)
                {
                    SetStatus(OutlookDot, OutlookText, "Bilinmiyor", Colors.Gray);
                }

                // Enable/disable start button based on critical checks
                // Critical checks: Gemini CLI, Gemini Login, and permissions
                bool criticalOk = _isGeminiCliOk && _isGeminiLoginOk && _isMicrophoneOk && _isNotificationOk;
                bool allOk = criticalOk && _isCalendarOk;
                StartAppBtn.IsEnabled = true; // Always allow starting, but color it differently or guide
                if (allOk)
                {
                    StartAppBtn.Content = "🚀 Başlamaya Hazır! Başlat";
                }
                else if (criticalOk)
                {
                    StartAppBtn.Content = "🚀 Kritik Adımlar Tamam! Başlat";
                }
                else
                {
                    StartAppBtn.Content = "🚀 Kurulumu Geç ve Başlat";
                }
            }
            finally
            {
                RecheckBtn.IsEnabled = true;
                RecheckBtn.Content = "🔄 Yeniden Denetle";
            }
        }

        private void SetStatus(System.Windows.Shapes.Ellipse dot, System.Windows.Controls.TextBlock text, string statusMsg, System.Windows.Media.Color color)
        {
            dot.Fill = new SolidColorBrush(color);
            text.Text = statusMsg;
        }

        private void GeminiCliBtn_Click(object sender, RoutedEventArgs e)
        {
            var scriptPath = FindInstallerScript();
            if (scriptPath == null)
            {
                System.Windows.Clipboard.SetText("agy");
                System.Windows.MessageBox.Show(this, "Kurulum betiği bulunamadı. Komut panoya kopyalandı:\nagy", "Antigravity CLI (agy) Kurulumu", MessageBoxButton.OK, MessageBoxImage.Information);
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
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, $"Kurulum betiği başlatılamadı: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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

        private void GeminiLoginBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var geminiCommand = GeminiCliService.ResolveGeminiCommand() ?? "agy";
                if (geminiCommand.Contains("gemini", StringComparison.OrdinalIgnoreCase))
                {
                    var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    var agyCandidates = new[]
                    {
                        Path.Combine(localAppData, "agy", "bin", "agy.exe"),
                        Path.Combine(userProfile, "AppData", "Local", "agy", "bin", "agy.exe"),
                        Path.Combine(appData, "npm", "agy.cmd")
                    };
                    foreach (var cand in agyCandidates)
                    {
                        if (File.Exists(cand))
                        {
                            geminiCommand = cand;
                            break;
                        }
                    }
                }

                ProcessStartInfo psi;

                if (geminiCommand.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || geminiCommand.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
                {
                    psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c call \"{geminiCommand}\"",
                        UseShellExecute = false,
                        CreateNoWindow = false
                    };
                }
                else
                {
                    psi = new ProcessStartInfo
                    {
                        FileName = geminiCommand,
                        UseShellExecute = false,
                        CreateNoWindow = false
                    };
                }

                psi.EnvironmentVariables["ANTIGRAVITY_CLI_ALIAS"] = "antigravity";
                psi.EnvironmentVariables["GEMINI_CLI_TRUST_WORKSPACE"] = "true";

                var process = Process.Start(psi);
                if (process != null)
                {
                    Task.Run(async () =>
                    {
                        await process.WaitForExitAsync();
                        await Dispatcher.InvokeAsync(async () =>
                        {
                            await RunChecksAsync();
                        });
                    });
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, $"Giriş başlatılamadı: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void CalendarBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CalendarBtn.IsEnabled = false;
                var result = await _googleAuth.SignInAsync();
                if (result.Success)
                {
                    await RunChecksAsync();
                }
                else
                {
                    System.Windows.MessageBox.Show(this, $"Bağlantı başarısız: {result.Error}", "Google Takvim", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            finally
            {
                CalendarBtn.IsEnabled = true;
            }
        }

        private void MicrophoneBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:privacy-microphone") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, $"Mikrofon ayarları açılamadı: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void NotificationBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                NotificationBtn.IsEnabled = false;
                await _notificationListener.EnsureAccessAndStartAsync();
                await RunChecksAsync();
            }
            finally
            {
                NotificationBtn.IsEnabled = true;
            }
        }

        private async void RecheckBtn_Click(object sender, RoutedEventArgs e)
        {
            await RunChecksAsync();
        }

        private void StartAppBtn_Click(object sender, RoutedEventArgs e)
        {
            _db.SetMeta("SetupOnboardingCompleted", "true");
            DialogResult = true;
            Close();
        }

        private void SkipBtn_Click(object sender, RoutedEventArgs e)
        {
            _db.SetMeta("SetupOnboardingCompleted", "true");
            DialogResult = true;
            Close();
        }
    }
}
