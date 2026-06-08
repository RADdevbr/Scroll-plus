namespace SmoothScroll.UI;

/// <summary>
/// Settings panel shown from the tray. Edits the shared <see cref="AppSettings"/>
/// instance in place (so changes take effect live) and invokes a callback so the
/// owner can persist them to disk and refresh the tray.
///
/// Layout uses TableLayoutPanel / docking + AutoScroll rather than absolute
/// coordinates, so it stays correct across DPI scaling and font sizes.
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly Action _onChanged;

    // Guards programmatic control updates from re-triggering change handlers.
    private bool _suppress;

    private TableLayoutPanel _root = null!;

    private CheckBox _enabledCheck = null!;
    private CheckBox _targetOnlyCheck = null!;

    private TrackBar _sensitivityBar = null!;
    private Label _sensitivityValue = null!;

    private TrackBar _frictionBar = null!;
    private Label _frictionValue = null!;

    private TrackBar _stepsBar = null!;
    private Label _stepsValue = null!;

    private TrackBar _frameBar = null!;
    private Label _frameValue = null!;

    private ListBox _targetsList = null!;
    private TextBox _targetInput = null!;

    public SettingsForm(AppSettings settings, Action onChanged)
    {
        _settings = settings;
        _onChanged = onChanged;

        BuildUi();
        SyncFromSettings();
    }

    private void BuildUi()
    {
        Text = "SmoothScroll Settings";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;
        Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(460, 680);
        MinimumSize = new Size(440, 480);

        // Single-column, auto-sizing rows, scrolls vertically if it ever overflows.
        _root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            AutoScroll = true,
            Padding = new Padding(14),
        };
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        Controls.Add(_root);

        // --- Master enable ---
        _enabledCheck = new CheckBox { Text = "Enable smoothing", AutoSize = true };
        _enabledCheck.CheckedChanged += (_, _) =>
        {
            if (_suppress) return;
            _settings.Enabled = _enabledCheck.Checked;
            Commit();
        };
        AddRow(_enabledCheck);

        // --- Sensitivity (0.5x .. 3.0x) ---
        _sensitivityValue = NewValueLabel();
        AddRow(NewHeader("Sensitivity (raw delta multiplier)", _sensitivityValue));
        _sensitivityBar = NewBar(5, 30, 5); // value / 10
        _sensitivityBar.ValueChanged += (_, _) =>
        {
            if (_suppress) return;
            _settings.Sensitivity = _sensitivityBar.Value / 10.0;
            _sensitivityValue.Text = $"{_settings.Sensitivity:0.0}x";
            Commit();
        };
        AddRow(_sensitivityBar);

        // --- Friction (0.80 .. 0.98) ---
        _frictionValue = NewValueLabel();
        AddRow(NewHeader("Friction (inertia decay per frame)", _frictionValue));
        _frictionBar = NewBar(80, 98, 2); // value / 100
        _frictionBar.ValueChanged += (_, _) =>
        {
            if (_suppress) return;
            _settings.Friction = _frictionBar.Value / 100.0;
            _frictionValue.Text = $"{_settings.Friction:0.00}";
            Commit();
        };
        AddRow(_frictionBar);

        // --- Steps per event (4 .. 20) ---
        _stepsValue = NewValueLabel();
        AddRow(NewHeader("Steps per event (messages per scroll)", _stepsValue));
        _stepsBar = NewBar(4, 20, 2);
        _stepsBar.ValueChanged += (_, _) =>
        {
            if (_suppress) return;
            _settings.StepsPerEvent = _stepsBar.Value;
            _stepsValue.Text = _settings.StepsPerEvent.ToString();
            Commit();
        };
        AddRow(_stepsBar);

        // --- Frame interval in ms (4 .. 16 ms ≈ 250 .. 60 fps) ---
        _frameValue = NewValueLabel();
        AddRow(NewHeader("Frame interval (scroll cadence)", _frameValue));
        _frameBar = NewBar(4, 16, 1); // value = milliseconds per frame
        _frameBar.ValueChanged += (_, _) =>
        {
            if (_suppress) return;
            _settings.FrameIntervalMs = _frameBar.Value;
            _frameValue.Text = $"{_settings.FrameIntervalMs} ms (~{1000 / _settings.FrameIntervalMs} fps)";
            Commit();
        };
        AddRow(_frameBar);

        // --- Scope checkbox ---
        _targetOnlyCheck = new CheckBox
        {
            Text = "Apply only to target windows (unchecked = apply globally)",
            AutoSize = true,
        };
        _targetOnlyCheck.CheckedChanged += (_, _) =>
        {
            if (_suppress) return;
            _settings.TargetOnly = _targetOnlyCheck.Checked;
            UpdateTargetControlsEnabled();
            Commit();
        };
        AddRow(_targetOnlyCheck);

        // --- Target windows list ---
        AddRow(new Label { Text = "Target windows (process name or title fragment)", AutoSize = true });

        _targetsList = new ListBox { Height = 130, IntegralHeight = false };
        AddRow(_targetsList);

        // Input row: textbox (stretch) + Add + Remove.
        var inputRow = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Dock = DockStyle.Fill };
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _targetInput = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 6, 0) };
        _targetInput.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                AddTarget();
            }
        };

        var addButton = new Button { Text = "Add", AutoSize = true, Margin = new Padding(0, 0, 6, 0) };
        addButton.Click += (_, _) => AddTarget();

        var removeButton = new Button { Text = "Remove", AutoSize = true, Margin = new Padding(0) };
        removeButton.Click += (_, _) => RemoveSelectedTarget();

        inputRow.Controls.Add(_targetInput, 0, 0);
        inputRow.Controls.Add(addButton, 1, 0);
        inputRow.Controls.Add(removeButton, 2, 0);
        AddRow(inputRow);

        // --- Bottom buttons (right-aligned) ---
        var buttonRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 10, 0, 0),
        };
        var closeButton = new Button { Text = "Close", AutoSize = true };
        closeButton.Click += (_, _) => Hide();
        var resetButton = new Button { Text = "Reset defaults", AutoSize = true, Margin = new Padding(8, 0, 0, 0) };
        resetButton.Click += (_, _) => ResetDefaults();
        buttonRow.Controls.Add(closeButton);
        buttonRow.Controls.Add(resetButton);
        AddRow(buttonRow);

        AcceptButton = closeButton;
    }

    /// <summary>Adds a control as the next full-width row of the layout.</summary>
    private void AddRow(Control c)
    {
        c.Margin = new Padding(0, 4, 0, 4);
        // Docked composites already fill the cell; stretch everything else width-wise.
        if (c.Dock != DockStyle.Fill)
            c.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _root.Controls.Add(c);
    }

    private static Label NewValueLabel() => new() { AutoSize = true, Anchor = AnchorStyles.Right };

    /// <summary>A header row: caption on the left, live value on the right.</summary>
    private static TableLayoutPanel NewHeader(string caption, Label valueLabel)
    {
        var t = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        t.Controls.Add(valueLabel, 1, 0);
        return t;
    }

    private static TrackBar NewBar(int min, int max, int tick) => new()
    {
        Minimum = min,
        Maximum = max,
        TickFrequency = tick,
        SmallChange = 1,
        LargeChange = Math.Max(1, tick),
        Height = 45,
    };

    private void AddTarget()
    {
        string value = _targetInput.Text.Trim();
        if (value.Length == 0)
            return;

        if (!_settings.TargetWindows.Any(t => t.Equals(value, StringComparison.OrdinalIgnoreCase)))
        {
            _settings.TargetWindows.Add(value);
            _targetsList.Items.Add(value);
            Commit();
        }

        _targetInput.Clear();
        _targetInput.Focus();
    }

    private void RemoveSelectedTarget()
    {
        if (_targetsList.SelectedItem is not string selected)
            return;

        _settings.TargetWindows.RemoveAll(t => t.Equals(selected, StringComparison.OrdinalIgnoreCase));
        _targetsList.Items.Remove(selected);
        Commit();
    }

    private void ResetDefaults()
    {
        var d = new AppSettings();
        _settings.Enabled = d.Enabled;
        _settings.Sensitivity = d.Sensitivity;
        _settings.Friction = d.Friction;
        _settings.StepsPerEvent = d.StepsPerEvent;
        _settings.FrameIntervalMs = d.FrameIntervalMs;
        _settings.TargetOnly = d.TargetOnly;
        _settings.TargetWindows.Clear();
        _settings.TargetWindows.AddRange(d.TargetWindows);

        SyncFromSettings();
        Commit();
    }

    private void UpdateTargetControlsEnabled()
    {
        // Target list is only meaningful in target-only mode.
        _targetsList.Enabled = _settings.TargetOnly;
        _targetInput.Enabled = _settings.TargetOnly;
    }

    /// <summary>Pushes the current settings into the controls (no change events).</summary>
    public void SyncFromSettings()
    {
        if (InvokeRequired)
        {
            BeginInvoke(SyncFromSettings);
            return;
        }

        _suppress = true;
        try
        {
            _enabledCheck.Checked = _settings.Enabled;
            _targetOnlyCheck.Checked = _settings.TargetOnly;

            _sensitivityBar.Value = Clamp((int)Math.Round(_settings.Sensitivity * 10), _sensitivityBar.Minimum, _sensitivityBar.Maximum);
            _sensitivityValue.Text = $"{_settings.Sensitivity:0.0}x";

            _frictionBar.Value = Clamp((int)Math.Round(_settings.Friction * 100), _frictionBar.Minimum, _frictionBar.Maximum);
            _frictionValue.Text = $"{_settings.Friction:0.00}";

            _stepsBar.Value = Clamp(_settings.StepsPerEvent, _stepsBar.Minimum, _stepsBar.Maximum);
            _stepsValue.Text = _settings.StepsPerEvent.ToString();

            _frameBar.Value = Clamp(_settings.FrameIntervalMs, _frameBar.Minimum, _frameBar.Maximum);
            _frameValue.Text = $"{_frameBar.Value} ms (~{1000 / _frameBar.Value} fps)";

            _targetsList.Items.Clear();
            foreach (string t in _settings.TargetWindows)
                _targetsList.Items.Add(t);

            UpdateTargetControlsEnabled();
        }
        finally
        {
            _suppress = false;
        }
    }

    private static int Clamp(int value, int min, int max) => Math.Min(max, Math.Max(min, value));

    private void Commit() => _onChanged();

    /// <summary>Closing via the window 'X' hides to tray instead of exiting.</summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }
}
