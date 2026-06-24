using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace SmoothScroll;

/// <summary>
/// Detects the active foreground window, matches it against the configured
/// target list (by process name or window-title fragment), and resolves the
/// concrete child window under the cursor that should receive the scroll.
/// </summary>
public sealed class WindowTargeting
{
    /// <summary>
    /// Resolves the window that should receive the smoothed scroll.
    /// </summary>
    /// <param name="screenPoint">Cursor position in screen coordinates.</param>
    /// <param name="settings">Current settings (used for target matching).</param>
    /// <param name="isTarget">
    /// True when the foreground window matches the configured target list.
    /// </param>
    /// <param name="isExcluded">
    /// True when the foreground window matches the exclusion list; such windows
    /// should never be smoothed, even in global mode.
    /// </param>
    /// <returns>The window handle to deliver wheel messages to, or Zero.</returns>
    public IntPtr ResolveScrollTarget(Point screenPoint, AppSettings settings, out bool isTarget, out bool isExcluded)
    {
        isTarget = false;
        isExcluded = false;

        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
            return IntPtr.Zero;

        string title = GetWindowTitle(foreground);
        string process = GetProcessName(foreground);
        isTarget = MatchesTarget(title, process, settings.TargetWindows);
        isExcluded = MatchesTarget(title, process, settings.ExcludedWindows);

        // Wheel messages are hit-tested against the window directly under the
        // cursor, so deliver there for correct behavior with multi-pane viewers.
        IntPtr under = WindowFromPoint(new POINT { x = screenPoint.X, y = screenPoint.Y });
        return under != IntPtr.Zero ? under : foreground;
    }

    /// <summary>True if title contains, or process name matches, any target entry.</summary>
    public static bool MatchesTarget(string title, string processName, IEnumerable<string> targets)
    {
        foreach (string raw in targets)
        {
            string pattern = raw?.Trim() ?? string.Empty;
            if (pattern.Length == 0)
                continue;

            if (title.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;

            // Match against process name with or without a trailing ".exe".
            if (processName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;

            string patternNoExe = pattern.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? pattern[..^4]
                : pattern;
            if (processName.Equals(patternNoExe, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static string GetWindowTitle(IntPtr hwnd)
    {
        int length = GetWindowTextLength(hwnd);
        if (length <= 0)
            return string.Empty;

        var sb = new StringBuilder(length + 1);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string GetProcessName(IntPtr hwnd)
    {
        try
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0)
                return string.Empty;

            using Process p = Process.GetProcessById((int)pid);
            return p.ProcessName; // already without ".exe"
        }
        catch
        {
            return string.Empty;
        }
    }

    #region Win32 interop

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    #endregion
}
