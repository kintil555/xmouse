namespace XMouse;

/// <summary>
/// Aksi yang bisa dipicu oleh sebuah tombol/scroll mouse.
/// </summary>
public enum MouseAction
{
    None,           // biarkan default (tidak diubah)
    LeftClick,
    RightClick,
    MiddleClick,
    DoubleLeftClick,
    DoubleRightClick,
    DoubleMiddleClick,
    Disabled,       // tombol dinonaktifkan total
}

/// <summary>
/// Aksi khusus untuk scroll wheel.
/// </summary>
public enum ScrollAction
{
    None,           // scroll normal, tidak diubah
    LeftClickPerTick,   // setiap "tick" scroll -> 1x klik kiri
    RightClickPerTick,  // setiap "tick" scroll -> 1x klik kanan
    MiddleClickPerTick, // setiap "tick" scroll -> 1x klik tengah
    Disabled,           // scroll dimatikan total
}

/// <summary>
/// Seluruh konfigurasi remap yang disimpan/di-load dari JSON.
/// </summary>
public class RemapConfig
{
    public bool Enabled { get; set; } = true;

    public MouseAction LeftButtonAction { get; set; } = MouseAction.None;
    public MouseAction RightButtonAction { get; set; } = MouseAction.None;
    public MouseAction MiddleButtonAction { get; set; } = MouseAction.None;

    public ScrollAction ScrollUpAction { get; set; } = ScrollAction.None;
    public ScrollAction ScrollDownAction { get; set; } = ScrollAction.None;

    /// <summary>Jeda maksimum (ms) antar klik agar dihitung sebagai double click buatan.</summary>
    public int DoubleClickIntervalMs { get; set; } = 50;

    /// <summary>Mulai minimize ke tray saat aplikasi dijalankan.</summary>
    public bool StartMinimized { get; set; } = false;

    /// <summary>Jalankan otomatis saat Windows startup.</summary>
    public bool RunOnStartup { get; set; } = false;
}
