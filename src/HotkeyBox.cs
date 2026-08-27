using System.Windows.Forms;

namespace XMouse;

/// <summary>
/// TextBox read-only yang menangkap kombinasi tombol saat difokuskan:
/// klik lalu tekan kombinasi (mis. Ctrl+Alt+P) langsung terekam sebagai keybind,
/// tidak perlu bolak-balik buka menu/tray untuk mengatur pause/resume.
/// </summary>
public sealed class HotkeyBox : TextBox
{
    private HotkeyConfig _value = new();

    public event EventHandler? HotkeyChanged;

    public HotkeyBox()
    {
        ReadOnly = true;
        ShortcutsEnabled = false;
        Cursor = Cursors.Hand;
        UpdateText();
    }

    public HotkeyConfig Value
    {
        get => _value;
        set { _value = value; UpdateText(); }
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Text = "Tekan kombinasi tombol... (Esc batal, Delete hapus)";
        SelectionStart = Text.Length;
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        UpdateText();
    }

    protected override bool IsInputKey(Keys keyData) => true; // supaya Tab/Esc dsb tertangkap juga

    // Win key tidak muncul di KeyEventArgs.Modifiers, jadi dilacak manual dari raw key code.
    private bool _winDown;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        e.SuppressKeyPress = true;

        if (e.KeyCode is Keys.LWin or Keys.RWin)
        {
            _winDown = true;
            return;
        }

        switch (e.KeyCode)
        {
            case Keys.Escape:
                UpdateText();
                Parent?.Focus();
                return;

            case Keys.Delete:
            case Keys.Back:
                _value = new HotkeyConfig { Key = Keys.None };
                HotkeyChanged?.Invoke(this, EventArgs.Empty);
                UpdateText();
                Parent?.Focus();
                return;

            case Keys.ControlKey:
            case Keys.ShiftKey:
            case Keys.Menu:
                // modifier saja belum jadi keybind valid, tunggu tombol utama
                return;
        }

        var config = new HotkeyConfig
        {
            Control = e.Control,
            Alt = e.Alt,
            Shift = e.Shift,
            Win = _winDown,
            Key = e.KeyCode,
        };

        _value = config;
        HotkeyChanged?.Invoke(this, EventArgs.Empty);
        UpdateText();
        Parent?.Focus();
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.LWin or Keys.RWin) _winDown = false;
        base.OnKeyUp(e);
    }

    private void UpdateText() => Text = _value.ToString();
}
