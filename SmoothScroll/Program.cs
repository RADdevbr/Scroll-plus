using System.ComponentModel;
using SmoothScroll.UI;

namespace SmoothScroll;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Single-instance guard: a second instance would install a duplicate
        // hook and double every scroll event.
        using var mutex = new Mutex(initiallyOwned: true, "SmoothScroll_SingleInstance_Mutex", out bool createdNew);
        if (!createdNew)
            return;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());

        GC.KeepAlive(mutex);
    }
}

/// <summary>
/// Headless application context: the app lives in the system tray with no main
/// window. Owns and wires together all the moving parts.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly SettingsManager _settingsManager;
    private readonly AppSettings _settings;
    private readonly InjectionService _injection;
    private readonly WindowTargeting _targeting;
    private readonly ScrollEngine _engine;
    private readonly HookManager _hook;

    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _enabledItem;

    private SettingsForm? _settingsForm;

    public TrayApplicationContext()
    {
        _settingsManager = new SettingsManager();
        _settings = _settingsManager.Load();

        _injection = new InjectionService();
        _targeting = new WindowTargeting();
        _engine = new ScrollEngine(_injection, _settings);
        _hook = new HookManager();
        _hook.MouseWheel += OnMouseWheel;

        // --- Tray icon + context menu ---
        _enabledItem = new ToolStripMenuItem("Enabled", null, (_, _) => ToggleEnabled())
        {
            Checked = _settings.Enabled,
            CheckOnClick = true,
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(_enabledItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Settings…", null, (_, _) => OpenSettings()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitApp()));

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = menu,
        };
        _trayIcon.DoubleClick += (_, _) => OpenSettings();
        UpdateTrayText();

        // --- Install the global hook ---
        try
        {
            _hook.Install();
        }
        catch (Win32Exception ex)
        {
            MessageBox.Show(
                "SmoothScroll could not install the global mouse hook.\n\n" + ex.Message,
                "SmoothScroll",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void OnMouseWheel(object? sender, MouseWheelHookEventArgs e)
    {
        // Disabled -> let the OS deliver the original wheel event untouched.
        if (!_settings.Enabled)
            return;

        IntPtr targetHwnd = _targeting.ResolveScrollTarget(e.ScreenPoint, _settings, out bool isTarget);

        // In target-only mode, ignore everything that is not a configured app.
        if (_settings.TargetOnly && !isTarget)
            return;

        if (targetHwnd == IntPtr.Zero)
            return;

        // Swallow the raw event and hand it to the smoothing engine.
        e.Handled = true;
        _engine.AddScroll(e.Delta, targetHwnd);
    }

    private void ToggleEnabled()
    {
        _settings.Enabled = _enabledItem.Checked;
        if (!_settings.Enabled)
            _engine.Stop();

        _settingsManager.Save(_settings);
        _settingsForm?.SyncFromSettings();
        UpdateTrayText();
    }

    private void OpenSettings()
    {
        if (_settingsForm == null || _settingsForm.IsDisposed)
        {
            _settingsForm = new SettingsForm(_settings, OnSettingsChanged);
        }

        _settingsForm.Show();
        if (_settingsForm.WindowState == FormWindowState.Minimized)
            _settingsForm.WindowState = FormWindowState.Normal;
        _settingsForm.BringToFront();
        _settingsForm.Activate();
    }

    /// <summary>Called by the settings form whenever the user changes a value.</summary>
    private void OnSettingsChanged()
    {
        _settingsManager.Save(_settings);
        _enabledItem.Checked = _settings.Enabled;
        UpdateTrayText();
    }

    private void UpdateTrayText()
    {
        _trayIcon.Text = _settings.Enabled
            ? "SmoothScroll — On"
            : "SmoothScroll — Off";
    }

    private void ExitApp()
    {
        _settingsManager.Save(_settings);
        _hook.Dispose();
        _engine.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hook.Dispose();
            _engine.Dispose();
            _trayIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
