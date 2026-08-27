namespace XMouse;

public class MainForm : Form
{
    private readonly MouseHookEngine _engine;
    private readonly HotkeyManager _hotkeyManager = new();
    private RemapConfig _config;

    private NotifyIcon _trayIcon = null!;
    private ContextMenuStrip _trayMenu = null!;
    private ToolStripMenuItem _enabledMenuItem = null!;

    private ComboBox _leftCombo = null!;
    private ComboBox _rightCombo = null!;
    private ComboBox _middleCombo = null!;
    private ComboBox _scrollUpCombo = null!;
    private ComboBox _scrollDownCombo = null!;
    private NumericUpDown _intervalNumeric = null!;
    private CheckBox _enabledCheck = null!;
    private CheckBox _startupCheck = null!;
    private CheckBox _startMinimizedCheck = null!;
    private HotkeyBox _hotkeyBox = null!;

    private static readonly (MouseAction Value, string Label)[] ButtonActions =
    [
        (MouseAction.None, "Normal (tidak diubah)"),
        (MouseAction.LeftClick, "Klik Kiri"),
        (MouseAction.RightClick, "Klik Kanan"),
        (MouseAction.MiddleClick, "Klik Tengah"),
        (MouseAction.DoubleLeftClick, "Double Klik Kiri"),
        (MouseAction.DoubleRightClick, "Double Klik Kanan"),
        (MouseAction.DoubleMiddleClick, "Double Klik Tengah"),
        (MouseAction.Disabled, "Nonaktifkan"),
    ];

    private static readonly (ScrollAction Value, string Label)[] ScrollActions =
    [
        (ScrollAction.None, "Normal (scroll biasa)"),
        (ScrollAction.LeftClickPerTick, "Klik Kiri per Scroll"),
        (ScrollAction.RightClickPerTick, "Klik Kanan per Scroll"),
        (ScrollAction.MiddleClickPerTick, "Klik Tengah per Scroll"),
        (ScrollAction.Disabled, "Nonaktifkan Scroll"),
    ];

    public MainForm(bool startMinimized)
    {
        _config = ConfigManager.Load();
        _engine = new MouseHookEngine(_config);

        InitializeUi();
        BuildTrayIcon();
        LoadConfigIntoUi();

        _hotkeyManager.HotkeyPressed += OnToggleHotkeyPressed;
        _hotkeyManager.Register(_config.ToggleHotkey);

        // _engine.Start() bisa throw InvalidOperationException kalau pasang hook
        // gagal (mis. butuh Administrator) -- biarkan exception naik ke Program.cs
        // supaya pengguna dapat pesan jelas, bukan aplikasi diam-diam tidak jalan.
        _engine.Start();

        if (startMinimized || _config.StartMinimized)
        {
            WindowState = FormWindowState.Minimized;
            ShowInTaskbar = false;
            Load += (_, _) => Hide();
        }
    }

    private void InitializeUi()
    {
        Text = "xmouse - Pengatur Mouse";
        Width = 460;
        Height = 510;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 2,
            RowCount = 12,
            AutoSize = true,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));

        int row = 0;

        _enabledCheck = new CheckBox { Text = "Aktifkan remap mouse", AutoSize = true, Checked = true };
        _enabledCheck.CheckedChanged += (_, _) => { _config.Enabled = _enabledCheck.Checked; ApplyAndSave(); };
        layout.Controls.Add(_enabledCheck, 0, row);
        layout.SetColumnSpan(_enabledCheck, 2);
        row++;

        AddSectionLabel(layout, "Tombol Mouse", ref row);

        layout.Controls.Add(new Label { Text = "Klik Kiri ->", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        _leftCombo = CreateButtonActionCombo();
        _leftCombo.SelectedIndexChanged += (_, _) => { _config.LeftButtonAction = ((ValueTuple<MouseAction, string>)_leftCombo.SelectedItem!).Item1; ApplyAndSave(); };
        layout.Controls.Add(_leftCombo, 1, row);
        row++;

        layout.Controls.Add(new Label { Text = "Klik Kanan ->", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        _rightCombo = CreateButtonActionCombo();
        _rightCombo.SelectedIndexChanged += (_, _) => { _config.RightButtonAction = ((ValueTuple<MouseAction, string>)_rightCombo.SelectedItem!).Item1; ApplyAndSave(); };
        layout.Controls.Add(_rightCombo, 1, row);
        row++;

        layout.Controls.Add(new Label { Text = "Klik Tengah ->", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        _middleCombo = CreateButtonActionCombo();
        _middleCombo.SelectedIndexChanged += (_, _) => { _config.MiddleButtonAction = ((ValueTuple<MouseAction, string>)_middleCombo.SelectedItem!).Item1; ApplyAndSave(); };
        layout.Controls.Add(_middleCombo, 1, row);
        row++;

        AddSectionLabel(layout, "Scroll Wheel", ref row);

        layout.Controls.Add(new Label { Text = "Scroll Atas ->", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        _scrollUpCombo = CreateScrollActionCombo();
        _scrollUpCombo.SelectedIndexChanged += (_, _) => { _config.ScrollUpAction = ((ValueTuple<ScrollAction, string>)_scrollUpCombo.SelectedItem!).Item1; ApplyAndSave(); };
        layout.Controls.Add(_scrollUpCombo, 1, row);
        row++;

        layout.Controls.Add(new Label { Text = "Scroll Bawah ->", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        _scrollDownCombo = CreateScrollActionCombo();
        _scrollDownCombo.SelectedIndexChanged += (_, _) => { _config.ScrollDownAction = ((ValueTuple<ScrollAction, string>)_scrollDownCombo.SelectedItem!).Item1; ApplyAndSave(); };
        layout.Controls.Add(_scrollDownCombo, 1, row);
        row++;

        AddSectionLabel(layout, "Lainnya", ref row);

        layout.Controls.Add(new Label { Text = "Keybind aktif/nonaktifkan ->", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        _hotkeyBox = new HotkeyBox { Width = 220 };
        _hotkeyBox.HotkeyChanged += (_, _) =>
        {
            _config.ToggleHotkey = _hotkeyBox.Value;
            if (!_hotkeyManager.Register(_config.ToggleHotkey))
            {
                MessageBox.Show(this,
                    "Kombinasi tombol itu sudah dipakai aplikasi lain. Coba kombinasi lain.",
                    "xmouse", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            ApplyAndSave();
        };
        layout.Controls.Add(_hotkeyBox, 1, row);
        row++;

        layout.Controls.Add(new Label { Text = "Interval double-click (ms)", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        _intervalNumeric = new NumericUpDown { Minimum = 10, Maximum = 500, Value = 50, Width = 100 };
        _intervalNumeric.ValueChanged += (_, _) => { _config.DoubleClickIntervalMs = (int)_intervalNumeric.Value; ApplyAndSave(); };
        layout.Controls.Add(_intervalNumeric, 1, row);
        row++;

        _startMinimizedCheck = new CheckBox { Text = "Mulai dalam keadaan minimize", AutoSize = true };
        _startMinimizedCheck.CheckedChanged += (_, _) => { _config.StartMinimized = _startMinimizedCheck.Checked; ApplyAndSave(); };
        layout.Controls.Add(_startMinimizedCheck, 0, row);
        layout.SetColumnSpan(_startMinimizedCheck, 2);
        row++;

        _startupCheck = new CheckBox { Text = "Jalankan otomatis saat Windows startup", AutoSize = true };
        _startupCheck.CheckedChanged += (_, _) =>
        {
            _config.RunOnStartup = _startupCheck.Checked;
            ConfigManager.SetRunOnStartup(_startupCheck.Checked);
            ApplyAndSave();
        };
        layout.Controls.Add(_startupCheck, 0, row);
        layout.SetColumnSpan(_startupCheck, 2);
        row++;

        var hint = new Label
        {
            Text = "Tips: tutup jendela ini untuk minimize ke tray. xmouse tetap berjalan di background.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            MaximumSize = new Size(420, 0),
        };
        layout.Controls.Add(hint, 0, row);
        layout.SetColumnSpan(hint, 2);

        Controls.Add(layout);

        FormClosing += MainForm_FormClosing;
    }

    private static void AddSectionLabel(TableLayoutPanel layout, string text, ref int row)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            Margin = new Padding(0, 12, 0, 4),
        };
        layout.Controls.Add(label, 0, row);
        layout.SetColumnSpan(label, 2);
        row++;
    }

    private static ComboBox CreateButtonActionCombo()
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        foreach (var item in ButtonActions) combo.Items.Add(item);
        combo.DisplayMember = "Item2";
        return combo;
    }

    private static ComboBox CreateScrollActionCombo()
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        foreach (var item in ScrollActions) combo.Items.Add(item);
        combo.DisplayMember = "Item2";
        return combo;
    }

    private void LoadConfigIntoUi()
    {
        _enabledCheck.Checked = _config.Enabled;
        SelectByValue(_leftCombo, _config.LeftButtonAction);
        SelectByValue(_rightCombo, _config.RightButtonAction);
        SelectByValue(_middleCombo, _config.MiddleButtonAction);
        SelectByValue(_scrollUpCombo, _config.ScrollUpAction);
        SelectByValue(_scrollDownCombo, _config.ScrollDownAction);
        _intervalNumeric.Value = Math.Clamp(_config.DoubleClickIntervalMs, (int)_intervalNumeric.Minimum, (int)_intervalNumeric.Maximum);
        _startMinimizedCheck.Checked = _config.StartMinimized;
        _startupCheck.Checked = _config.RunOnStartup;
        _hotkeyBox.Value = _config.ToggleHotkey;
    }

    /// <summary>Dipanggil dari HotkeyManager (thread UI, lewat message-only window) saat keybind ditekan di mana pun.</summary>
    private void OnToggleHotkeyPressed()
    {
        _config.Enabled = !_config.Enabled;
        _enabledCheck.Checked = _config.Enabled; // trigger ApplyAndSave via event yang sudah ada
        _trayIcon.ShowBalloonTip(800, "xmouse",
            _config.Enabled ? "Remap diaktifkan" : "Remap dijeda (pause)",
            ToolTipIcon.Info);
    }

    private static void SelectByValue(ComboBox combo, MouseAction value)
    {
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (((ValueTuple<MouseAction, string>)combo.Items[i]!).Item1 == value)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        combo.SelectedIndex = 0;
    }

    private static void SelectByValue(ComboBox combo, ScrollAction value)
    {
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (((ValueTuple<ScrollAction, string>)combo.Items[i]!).Item1 == value)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        combo.SelectedIndex = 0;
    }

    private void ApplyAndSave()
    {
        _engine.UpdateConfig(_config);
        ConfigManager.Save(_config);
        UpdateTrayState();
    }

    private void BuildTrayIcon()
    {
        _trayMenu = new ContextMenuStrip();

        _enabledMenuItem = new ToolStripMenuItem("Aktif", null, (_, _) =>
        {
            _config.Enabled = !_config.Enabled;
            _enabledCheck.Checked = _config.Enabled; // akan trigger ApplyAndSave via event
        }) { CheckOnClick = false };
        _trayMenu.Items.Add(_enabledMenuItem);

        _trayMenu.Items.Add(new ToolStripSeparator());

        _trayMenu.Items.Add("Buka Pengaturan", null, (_, _) => ShowFromTray());
        _trayMenu.Items.Add("Keluar", null, (_, _) => ExitApplication());

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "xmouse",
            ContextMenuStrip = _trayMenu,
        };
        _trayIcon.DoubleClick += (_, _) => ShowFromTray();

        UpdateTrayState();
    }

    private void UpdateTrayState()
    {
        _enabledMenuItem.Text = _config.Enabled ? "Aktif ✓" : "Nonaktif";
        _trayIcon.Text = _config.Enabled ? "xmouse - aktif" : "xmouse - nonaktif";
    }

    private void ShowFromTray()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            // minimize ke tray, jangan benar-benar keluar
            e.Cancel = true;
            Hide();
            ShowInTaskbar = false;
        }
    }

    private void ExitApplication()
    {
        // Sembunyikan tray icon dulu sebelum Dispose engine, supaya UI terasa
        // responsif langsung -- Dispose() (Stop hook + join worker thread, maks
        // 500ms) tidak lagi membuat tray icon menggantung terlihat oleh pengguna.
        _trayIcon.Visible = false;
        _engine.Dispose();
        _hotkeyManager.Dispose();
        _trayIcon.Dispose();
        Application.Exit();
    }
}
