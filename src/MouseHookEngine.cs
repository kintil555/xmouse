using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using static XMouse.NativeMethods;

namespace XMouse;

/// <summary>
/// Memasang low-level mouse hook (WH_MOUSE_LL) dan menerapkan remap
/// sesuai <see cref="RemapConfig"/> yang aktif.
///
/// PENTING (stabilitas): Windows memaksa hook WH_MOUSE_LL punya batas waktu respons
/// (LowLevelHooksTimeout, default ~300ms) yang berjalan di thread UI. Kalau callback
/// ini telat merespons -- termasuk karena memanggil SendInput/mouse_event yang
/// menunggu OS memproses input -- Windows bisa mem-freeze seluruh input sistem
/// ("Not Responding") atau diam-diam melepas hook kita. Karena itu:
///   1) HookCallback WAJIB selesai secepat mungkin (idealnya < 1ms), tidak pernah
///      memanggil apapun yang bisa blocking.
///   2) Semua aksi "berat" (mengirim klik sintetis) dilempar ke background queue,
///      diproses oleh worker thread terpisah -- bukan dieksekusi langsung di callback.
/// </summary>
public sealed class MouseHookEngine : IDisposable
{
    private IntPtr _hookHandle = IntPtr.Zero;
    private readonly LowLevelMouseProc _proc;
    private volatile RemapConfig _config;

    // Menandai klik yang sedang kita proses ulang (suppress asli, lalu kita ganti).
    private volatile bool _suppressNextLeftUpEcho;
    private volatile bool _suppressNextRightUpEcho;
    private volatile bool _suppressNextMiddleUpEcho;

    // Queue aksi klik sintetis + worker thread khusus, supaya hook callback
    // tidak pernah menunggu SendInput/mouse_event selesai.
    private readonly BlockingCollection<(uint down, uint up, int repeat)> _actionQueue = new();
    private Thread? _workerThread;
    private volatile bool _running;

    public MouseHookEngine(RemapConfig initialConfig)
    {
        _config = initialConfig;
        _proc = HookCallback; // simpan referensi delegate agar tidak di-GC
    }

    public void UpdateConfig(RemapConfig config) => _config = config;

    public void Start()
    {
        if (_hookHandle != IntPtr.Zero) return;

        _running = true;
        _workerThread = new Thread(ActionWorkerLoop)
        {
            IsBackground = true,
            Name = "xmouse-action-worker",
        };
        _workerThread.Start();

        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _hookHandle = SetWindowsHookEx(
            WH_MOUSE_LL,
            _proc,
            GetModuleHandle(curModule.ModuleName),
            0);

        if (_hookHandle == IntPtr.Zero)
        {
            _running = false;
            _actionQueue.CompleteAdding();
            throw new InvalidOperationException(
                "Gagal memasang mouse hook. Coba jalankan xmouse sebagai Administrator.");
        }
    }

    public void Stop()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }

        _running = false;
        if (!_actionQueue.IsAddingCompleted)
            _actionQueue.CompleteAdding();

        // Beri worker thread kesempatan keluar dengan bersih (timeout singkat,
        // jangan sampai Dispose/Stop ikut nge-block UI thread lama-lama).
        _workerThread?.Join(TimeSpan.FromMilliseconds(500));
    }

    /// <summary>
    /// Worker thread terpisah yang benar-benar mengirim klik sintetis ke OS.
    /// Berjalan di luar hook callback sehingga tidak pernah membuat
    /// WH_MOUSE_LL telat merespons.
    /// </summary>
    private void ActionWorkerLoop()
    {
        try
        {
            foreach (var (down, up, repeat) in _actionQueue.GetConsumingEnumerable())
            {
                for (int i = 0; i < repeat; i++)
                {
                    mouse_event(down, 0, 0, 0, (UIntPtr)XMOUSE_INJECTED_SIGNATURE);
                    mouse_event(up, 0, 0, 0, (UIntPtr)XMOUSE_INJECTED_SIGNATURE);
                }
            }
        }
        catch (InvalidOperationException)
        {
            // Queue sudah CompleteAdding + kosong -> keluar dengan tenang.
        }
    }

    private void EnqueueClick(uint down, uint up, int repeat = 1)
    {
        if (!_running) return;
        try
        {
            _actionQueue.Add((down, up, repeat));
        }
        catch (InvalidOperationException)
        {
            // Queue sedang ditutup (aplikasi sedang Stop) -> abaikan.
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // Jalur keluar tercepat mungkin: hindari kerja apapun kalau hook nonaktif
        // atau nCode negatif (harus selalu diteruskan apa adanya ke CallNextHookEx).
        if (nCode < 0)
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        var config = _config; // baca sekali (volatile), hindari race saat config berubah
        if (!config.Enabled)
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        int msg = wParam.ToInt32();

        // Filter pesan yang sama sekali tidak kita proses SEBELUM marshaling struct
        // (WM_MOUSEMOVE sangat sering terjadi, terutama di mouse gaming polling-rate
        // tinggi -- marshaling per event ini adalah overhead yang tidak perlu).
        if (msg != WM_LBUTTONDOWN && msg != WM_LBUTTONUP &&
            msg != WM_RBUTTONDOWN && msg != WM_RBUTTONUP &&
            msg != WM_MBUTTONDOWN && msg != WM_MBUTTONUP &&
            msg != WM_MOUSEWHEEL)
        {
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        // Baca dwExtraInfo lebih dulu tanpa marshal seluruh struct (field terakhir,
        // offset tetap: 2 int (POINT) + 3 uint = 20 byte pada x86, tapi karena struct
        // punya IntPtr, offset berbeda antara x86/x64 -- marshal parsial lewat
        // Marshal.ReadIntPtr jauh lebih murah daripada PtrToStructure penuh untuk
        // WM_LBUTTONDOWN/UP dkk yang tidak butuh field lain sama sekali.
        int extraInfoOffset = IntPtr.Size == 8 ? 16 : 12; // POINT(8) + mouseData(4) + flags(4) + time(4)
        IntPtr extraInfo = Marshal.ReadIntPtr(lParam, extraInfoOffset);
        if ((uint)extraInfo.ToInt64() == XMOUSE_INJECTED_SIGNATURE)
        {
            // Event injeksi kita sendiri -> selalu teruskan apa adanya, jangan diproses ulang
            // (mencegah rekursi tak terbatas).
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        switch (msg)
        {
            case WM_LBUTTONDOWN:
                return HandleButtonDown(config.LeftButtonAction, ref _suppressNextLeftUpEcho, nCode, wParam, lParam);
            case WM_LBUTTONUP:
                if (_suppressNextLeftUpEcho)
                {
                    _suppressNextLeftUpEcho = false;
                    return (IntPtr)1;
                }
                break;

            case WM_RBUTTONDOWN:
                return HandleButtonDown(config.RightButtonAction, ref _suppressNextRightUpEcho, nCode, wParam, lParam);
            case WM_RBUTTONUP:
                if (_suppressNextRightUpEcho)
                {
                    _suppressNextRightUpEcho = false;
                    return (IntPtr)1;
                }
                break;

            case WM_MBUTTONDOWN:
                return HandleButtonDown(config.MiddleButtonAction, ref _suppressNextMiddleUpEcho, nCode, wParam, lParam);
            case WM_MBUTTONUP:
                if (_suppressNextMiddleUpEcho)
                {
                    _suppressNextMiddleUpEcho = false;
                    return (IntPtr)1;
                }
                break;

            case WM_MOUSEWHEEL:
                // Hanya wheel yang butuh mouseData -> marshal struct penuh di sini saja.
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                return HandleWheel(data, config, nCode, wParam, lParam);
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private IntPtr HandleButtonDown(
        MouseAction action,
        ref bool suppressUpEcho,
        int nCode, IntPtr wParam, IntPtr lParam)
    {
        switch (action)
        {
            case MouseAction.None:
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

            case MouseAction.Disabled:
                suppressUpEcho = true;
                return (IntPtr)1;

            case MouseAction.LeftClick:
                suppressUpEcho = true;
                EnqueueClick(MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP);
                return (IntPtr)1;

            case MouseAction.RightClick:
                suppressUpEcho = true;
                EnqueueClick(MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP);
                return (IntPtr)1;

            case MouseAction.MiddleClick:
                suppressUpEcho = true;
                EnqueueClick(MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP);
                return (IntPtr)1;

            case MouseAction.DoubleLeftClick:
                suppressUpEcho = true;
                EnqueueClick(MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP, repeat: 2);
                return (IntPtr)1;

            case MouseAction.DoubleRightClick:
                suppressUpEcho = true;
                EnqueueClick(MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP, repeat: 2);
                return (IntPtr)1;

            case MouseAction.DoubleMiddleClick:
                suppressUpEcho = true;
                EnqueueClick(MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP, repeat: 2);
                return (IntPtr)1;

            default:
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }
    }

    private IntPtr HandleWheel(MSLLHOOKSTRUCT data, RemapConfig config, int nCode, IntPtr wParam, IntPtr lParam)
    {
        short delta = (short)((data.mouseData >> 16) & 0xFFFF);
        bool isUp = delta > 0;

        var action = isUp ? config.ScrollUpAction : config.ScrollDownAction;

        switch (action)
        {
            case ScrollAction.None:
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

            case ScrollAction.Disabled:
                return (IntPtr)1;

            case ScrollAction.LeftClickPerTick:
                EnqueueClick(MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP);
                return (IntPtr)1;

            case ScrollAction.RightClickPerTick:
                EnqueueClick(MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP);
                return (IntPtr)1;

            case ScrollAction.MiddleClickPerTick:
                EnqueueClick(MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP);
                return (IntPtr)1;

            default:
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }
    }

    public void Dispose() => Stop();
}
