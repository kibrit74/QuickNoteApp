using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Keys = System.Windows.Forms.Keys;

namespace QuickNoteApp.Services;

/// <summary>
/// RegisterHotKey Win32 API'sini sarmalar. Windows'ta global bir kısayol tanımlamak için
/// mesaj alacak bir HWND gerekiyor - burada görünmez bir "message-only" pencere kullanıyoruz,
/// böylece hiçbir görsel pencere açmadan arkaplanda WM_HOTKEY mesajlarını dinleyebiliyoruz.
/// </summary>
public class HotkeyManager : IDisposable
{
    public const int NOTE_HOTKEY_ID = 1;
    public const int REVIEW_HOTKEY_ID = 2;

    private const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly HwndSource _source;
    private readonly List<int> _registeredIds = new();

    public event EventHandler<int>? HotkeyPressed;

    public HotkeyManager()
    {
        // Boş, görünmez bir mesaj penceresi - sadece WM_HOTKEY mesajlarını yakalamak için var.
        var parameters = new HwndSourceParameters("QuickNoteAppHotkeySink")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0x00000000, // WS_OVERLAPPED, görünmez kalacak
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public void Register(int id, ModifierKeys modifiers, Keys key)
    {
        var fsModifiers = (uint)ToWin32Modifiers(modifiers);
        var vk = (uint)key;

        if (!RegisterHotKey(_source.Handle, id, fsModifiers, vk))
        {
            throw new InvalidOperationException(
                $"Kısayol kaydedilemedi (id={id}). Muhtemelen başka bir uygulama aynı kombinasyonu kullanıyor.");
        }
        _registeredIds.Add(id);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            HotkeyPressed?.Invoke(this, id);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private static int ToWin32Modifiers(ModifierKeys modifiers)
    {
        // Win32 MOD_* sabitleri: MOD_ALT=1, MOD_CONTROL=2, MOD_SHIFT=4, MOD_WIN=8
        int result = 0;
        if (modifiers.HasFlag(ModifierKeys.Alt)) result |= 1;
        if (modifiers.HasFlag(ModifierKeys.Control)) result |= 2;
        if (modifiers.HasFlag(ModifierKeys.Shift)) result |= 4;
        if (modifiers.HasFlag(ModifierKeys.Windows)) result |= 8;
        return result;
    }

    public void Dispose()
    {
        foreach (var id in _registeredIds)
            UnregisterHotKey(_source.Handle, id);

        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
