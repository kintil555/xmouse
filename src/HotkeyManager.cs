using System.Windows.Forms;
using static XMouse.NativeMethods;

namespace XMouse;

/// <summary>
/// Registrasi satu global hotkey (via RegisterHotKey) yang dipetakan ke sebuah
/// pesan Windows WM_HOTKEY, ditangkap lewat message-only window tersembunyi.
/// Dipakai untuk toggle enable/disable remap tanpa perlu buka tray/menu.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private const int HotkeyId = 0xB33F; // id unik, cukup satu hotkey global untuk app ini

    private readonly HotkeyWindow _window = new();
    private bool _registered;

    public event Action? HotkeyPressed
    {
        add => _window.HotkeyPressed += value;
        remove => _window.HotkeyPressed -= value;
    }

    /// <summary>Daftarkan ulang hotkey sesuai konfigurasi. Aman dipanggil berkali-kali (unregister dulu).</summary>
    public bool Register(HotkeyConfig config)
    {
        Unregister();

        if (!config.IsSet) return true; // tidak ada keybind -> tidak error, cuma tidak aktif

        uint modifiers = MOD_NOREPEAT;
        if (config.Alt) modifiers |= MOD_ALT;
        if (config.Control) modifiers |= MOD_CONTROL;
        if (config.Shift) modifiers |= MOD_SHIFT;
        if (config.Win) modifiers |= MOD_WIN;

        _registered = RegisterHotKey(_window.Handle, HotkeyId, modifiers, (uint)config.Key);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered)
        {
            UnregisterHotKey(_window.Handle, HotkeyId);
            _registered = false;
        }
    }

    public void Dispose()
    {
        Unregister();
        _window.DestroyHandleSafe();
    }

    /// <summary>Message-only window minimal, satu-satunya tujuannya menerima WM_HOTKEY.</summary>
    private sealed class HotkeyWindow : NativeWindow
    {
        public event Action? HotkeyPressed;

        public HotkeyWindow()
        {
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                HotkeyPressed?.Invoke();
            }
            base.WndProc(ref m);
        }

        public void DestroyHandleSafe()
        {
            if (Handle != IntPtr.Zero) DestroyHandle();
        }
    }
}

/// <summary>Konfigurasi satu kombinasi keybind (modifier + tombol utama).</summary>
public class HotkeyConfig
{
    public bool Control { get; set; }
    public bool Alt { get; set; } = true;
    public bool Shift { get; set; }
    public bool Win { get; set; }

    /// <summary>Tombol utama. Keys.None berarti belum diset / dinonaktifkan.</summary>
    public Keys Key { get; set; } = Keys.F9;

    public bool IsSet => Key != Keys.None;

    public override string ToString()
    {
        if (!IsSet) return "(tidak diset)";
        var parts = new List<string>();
        if (Control) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");
        parts.Add(Key.ToString());
        return string.Join(" + ", parts);
    }
}
