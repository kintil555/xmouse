using System.Diagnostics;
using System.Runtime.InteropServices;
using static XMouse.NativeMethods;

namespace XMouse;

/// <summary>
/// Memasang low-level mouse hook (WH_MOUSE_LL) dan menerapkan remap
/// sesuai <see cref="RemapConfig"/> yang aktif.
/// </summary>
public sealed class MouseHookEngine : IDisposable
{
    private IntPtr _hookHandle = IntPtr.Zero;
    private readonly LowLevelMouseProc _proc;
    private RemapConfig _config;

    // Untuk deteksi double-click buatan per tombol.
    private readonly Stopwatch _leftStopwatch = new();
    private readonly Stopwatch _rightStopwatch = new();
    private readonly Stopwatch _middleStopwatch = new();

    // Menandai klik yang sedang kita proses ulang (suppress asli, lalu kita ganti).
    private bool _suppressNextLeftUpEcho;
    private bool _suppressNextRightUpEcho;
    private bool _suppressNextMiddleUpEcho;

    public MouseHookEngine(RemapConfig initialConfig)
    {
        _config = initialConfig;
        _proc = HookCallback; // simpan referensi delegate agar tidak di-GC
    }

    public void UpdateConfig(RemapConfig config) => _config = config;

    public void Start()
    {
        if (_hookHandle != IntPtr.Zero) return;

        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _hookHandle = SetWindowsHookEx(
            WH_MOUSE_LL,
            _proc,
            GetModuleHandle(curModule.ModuleName),
            0);

        if (_hookHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "Gagal memasang mouse hook. Coba jalankan xmouse sebagai Administrator.");
        }
    }

    public void Stop()
    {
        if (_hookHandle == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || !_config.Enabled)
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

        // Event yang kita injeksikan sendiri (dwExtraInfo == signature) harus selalu
        // diteruskan apa adanya, jangan diproses lagi -> mencegah rekursi tak terbatas.
        if ((uint)data.dwExtraInfo.ToInt64() == XMOUSE_INJECTED_SIGNATURE)
        {
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        int msg = wParam.ToInt32();

        switch (msg)
        {
            case WM_LBUTTONDOWN:
                return HandleButtonDown(_config.LeftButtonAction, _leftStopwatch, ref _suppressNextLeftUpEcho, nCode, wParam, lParam);
            case WM_LBUTTONUP:
                if (_suppressNextLeftUpEcho)
                {
                    _suppressNextLeftUpEcho = false;
                    return (IntPtr)1; // suppress juga event UP asli
                }
                break;

            case WM_RBUTTONDOWN:
                return HandleButtonDown(_config.RightButtonAction, _rightStopwatch, ref _suppressNextRightUpEcho, nCode, wParam, lParam);
            case WM_RBUTTONUP:
                if (_suppressNextRightUpEcho)
                {
                    _suppressNextRightUpEcho = false;
                    return (IntPtr)1;
                }
                break;

            case WM_MBUTTONDOWN:
                return HandleButtonDown(_config.MiddleButtonAction, _middleStopwatch, ref _suppressNextMiddleUpEcho, nCode, wParam, lParam);
            case WM_MBUTTONUP:
                if (_suppressNextMiddleUpEcho)
                {
                    _suppressNextMiddleUpEcho = false;
                    return (IntPtr)1;
                }
                break;

            case WM_MOUSEWHEEL:
                return HandleWheel(data, nCode, wParam, lParam);
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private IntPtr HandleButtonDown(
        MouseAction action,
        Stopwatch clickTimer,
        ref bool suppressUpEcho,
        int nCode, IntPtr wParam, IntPtr lParam)
    {
        switch (action)
        {
            case MouseAction.None:
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

            case MouseAction.Disabled:
                suppressUpEcho = true;
                return (IntPtr)1; // telan DOWN, UP berikutnya juga akan ditelan

            case MouseAction.LeftClick:
                suppressUpEcho = true;
                SendSyntheticClick(MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP);
                return (IntPtr)1;

            case MouseAction.RightClick:
                suppressUpEcho = true;
                SendSyntheticClick(MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP);
                return (IntPtr)1;

            case MouseAction.MiddleClick:
                suppressUpEcho = true;
                SendSyntheticClick(MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP);
                return (IntPtr)1;

            case MouseAction.DoubleLeftClick:
                suppressUpEcho = true;
                SendSyntheticDoubleClick(MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP);
                return (IntPtr)1;

            case MouseAction.DoubleRightClick:
                suppressUpEcho = true;
                SendSyntheticDoubleClick(MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP);
                return (IntPtr)1;

            case MouseAction.DoubleMiddleClick:
                suppressUpEcho = true;
                SendSyntheticDoubleClick(MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP);
                return (IntPtr)1;

            default:
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }
    }

    private IntPtr HandleWheel(MSLLHOOKSTRUCT data, int nCode, IntPtr wParam, IntPtr lParam)
    {
        // mouseData bagian tinggi berisi delta wheel (120 = satu "tick" ke atas, -120 = ke bawah)
        short delta = (short)((data.mouseData >> 16) & 0xFFFF);
        bool isUp = delta > 0;

        var action = isUp ? _config.ScrollUpAction : _config.ScrollDownAction;

        switch (action)
        {
            case ScrollAction.None:
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

            case ScrollAction.Disabled:
                return (IntPtr)1; // telan scroll, tidak diteruskan

            case ScrollAction.LeftClickPerTick:
                SendSyntheticClick(MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP);
                return (IntPtr)1;

            case ScrollAction.RightClickPerTick:
                SendSyntheticClick(MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP);
                return (IntPtr)1;

            case ScrollAction.MiddleClickPerTick:
                SendSyntheticClick(MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP);
                return (IntPtr)1;

            default:
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }
    }

    public void Dispose() => Stop();
}
