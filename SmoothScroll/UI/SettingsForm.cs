namespace SmoothScroll.UI;

/// <summary>
/// Settings panel shown from the tray. Edits the shared <see cref="AppSettings"/>
/// instance in place (so changes take effect live) and invokes a callback so the
/// owner can persist them to disk and refresh the tray.
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly Action _onChanged;

    // Guards programmatic control updates from re-triggering change handlers.
    private bool _suppress;

    private CheckBox _enabledCheck = null!;
    private CheckBox _targetOnlyCheck = null!;

    private TrackBar _sensitivityBar = null!;
    private Label _sensitivityValue = null!;

    private TrackBar _frictionBar = null!;
    private Label _frictionValue = null!;

    private TrackBar _stepsBar = null!;
    private Label _stepsValue = null!;

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
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        ClientSize = new Size(420, 560);
        Font = new Font("Segoe UI", 9f);

        int y = 14;
        const int left = 16;
        const int width = 388;

        // --- Master enable ---
        _enabledCheck = new CheckBox
        {
            Text = "Enable smoothing",
            Location = new Point(left, y),
            AutoSize = true,
        };
        _enabledCheck.CheckedChanged += (_, _) =>
        {
            if (_suppress) return;
            _settings.Enabled = _enabledCheck.Checked;
            Commit();
        };
        Controls.Add(_enabledCheck);
        y += 34;

        // --- Sensitivity (0.5x .. 3.0x) ---
        AddSectionLabel("Sensitivity (raw delta multiplier)", left, ref y);
        _sensitivityValue = AddValueLabel(left + width - 60, y - 22);
        _sensitivityBar = new TrackBar
        {
            Location = new Point(left, y),
            Width = width,
            Minimum = 5,   // 0.5x  (value / 10)
            Maximum = 30,  // 3.0x
            TickFrequency = 5,
            SmallChange = 1,
            LargeChange = 5,
        };
        _sensitivityBar.ValueChanged += (_, _) =>
        {
            if (_suppress) return;
            _settings.Sensitivity = _sensitivityBar.Value / 10.0;
            _sensitivityValue.Text = $"{_settings.Sensitivity:0.0}x";
            Commit();
        };
        Controls.Add(_sensitivityBar);
        y += 56;

        // --- Friction (0.80 .. 0.98) ---
        AddSectionLabel("Friction (inertia decay per frame)", left, ref y);
        _frictionValue = AddValueLabel(left + width - 60, y - 22);
        _frictionBar = new TrackBar
        {
            Location = new Point(left, y),
            Width = width,
            Minimum = 80,  // 0.80  (value / 100)
            Maximum = 98,  // 0.98
            TickFrequency = 2,
            SmallChange = 1,
            LargeChange = 2,
        };
        _frictionBar.ValueChanged += (_, _) =>
        {
            if (_suppress) return;
            _settings.Friction = _frictionBar.Value / 100.0;
            _frictionValue.Text = $"{_settings.Friction:0.00}";
            Commit();
        };
        Controls.Add(_frictionBar);
        y += 56;

        // --- Steps per event (4 .. 20) ---
        AddSectionLabel("Steps per event (messages per scroll)", left, ref y);
        _stepsValue = AddValueLabel(left + width - 60, y - 22);
        _stepsBar = new TrackBar
        {
            Location = new Point(left, y),
            Width = width,
            Minimum = 4,
            Maximum = 20,
            TickFrequency = 2,
            SmallChange = 1,
            LargeChange = 2,
        };
        _stepsBar.ValueChanged += (_, _) =>
        {
            if (_suppress) return;
            _settings.StepsPerEvent = _stepsBar.Value;
            _stepsValue.Text = _settings.StepsPerEvent.ToString();
            Commit();
        };
        Controls.Add(_stepsBar);
        y += 56;

        // --- Scope checkbox ---
        _targetOnlyCheck = new CheckBox
        {
            Text = "Apply only to target windows (unchecked = apply globally)",
            Location = new Point(left, y),
            AutoSize = true,
        };
        _targetOnlyCheck.CheckedChanged += (_, _) =>
        {
            if (_suppress) return;
            _settings.TargetOnly = _targetOnlyCheck.Checked;
            UpdateTargetControlsEnabled();
            Commit();
        };
        Controls.Add(_targetOnlyCheck);
        y += 34;

        // --- Target windows list ---
        AddSectionLabel("Target windows (process name or title fragment)", left, ref y);
        _targetsList = new ListBox
        {
            Location = new Point(left, y),
            Width = width,
            Height = 120,
            SelectionMode = SelectionMode.One,
        };
        Controls.Add(_targetsList);
        y += 128;

        _targetInput = new TextBox
        {
            Location = new Point(left, y),
            Width = width - 170,
        };
        _targetInput.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                AddTarget();
            }
        };
        Controls.Add(_targetInput);

        var addButton = new Button
        {
            Text = "Add",
            Location = new Point(left + width - 162, y - 1),
            Width = 78,
        };
        addButton.Click += (_, _) => AddTarget();
        Controls.Add(addButton);

        var removeButton = new Button
        {
            Text = "Remove",
            Location = new Point(left + width - 80, y - 1),
            Width = 80,
        };
        removeButton.Click += (_, _) => RemoveSelectedTarget();
        Controls.Add(removeButton);
        y += 40;

        // --- Close (hides to tray) ---
        var closeButton = new Button
        {
            Text = "Close",
            Location = new Point(left + width - 90, y),
            Width = 90,
            DialogResult = DialogResult.OK,
        };
        closeButton.Click += (_, _) => Hide();
        Controls.Add(closeButton);
        AcceptButton = closeButton;
    }

    private void AddSectionLabel(string text, int left, ref int y)
    {
        var label = new Label
        {
            Text = text,
            Location = new Point(left, y),
            AutoSize = true,
        };
        Controls.Add(label);
        y += 22;
    }

    private Label AddValueLabel(int x, int y)
    {
        var label = new Label
        {
            Location = new Point(x, y),
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleRight,
        };
        Controls.Add(label);
        return label;
    }

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
