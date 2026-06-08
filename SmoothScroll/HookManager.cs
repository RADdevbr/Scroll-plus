using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace SmoothScroll;

/// <summary>
/// Event raised when the low-level hook observes a real (non-injected)
/// WM_MOUSEWHEEL. Set <see cref="Handled"/> to true to swallow the original
/// event so the smoothing engine can re-inject it.
/// </summary>
public sealed class MouseWheelHookEventArgs : EventArgs
{
    public int Delta { get; }
    public Point ScreenPoint { get; }
    public bool Handled { get; set; }

    public MouseWheelHookEventArgs(int delta, Point screenPoint)
    {
        Delta = delta;
        ScreenPoint = screenPoint;
    }
}

/// <summary>
/// Installs a global low-level mouse hook (WH_MOUSE_LL) to capture
/// WM_MOUSEWHEEL events across the whole desktop.
///
/// Notes:
///  - WH_MOUSE_LL does NOT require administrator privileges and does not inject
///    a DLL into other processes; the callback runs inside this process and is
///    serviced by the application's message loop.
///  - Injected input (e.g. our own SendInput fallback) is flagged with
///    LLMHF_INJECTED and ignored here to avoid feedback loops. PostMessage does
///    not generate low-level hook events at all.
/// </summary>
public sealed class HookManager : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const uint LLMHF_INJECTED = 0x00000001;

    // Keep the delegate alive for the lifetime of the hook so the GC does not
    // collect it (which would crash on the next callback).
    private LowLevelMouseProc? _proc;
    private IntPtr _hookId = IntPtr.Zero;

    public event EventHandler<MouseWheelHookEventArgs>? MouseWheel;

    public bool IsInstalled => _hookId != IntPtr.Zero;

    /// <summary>Installs the hook. Throws <see cref="Win32Exception"/> on failure.</summary>
    public void Install()
    {
        if (_hookId != IntPtr.Zero)
            return;

        _proc = HookCallback;
        using Process curProcess = Process.GetCurrentProcess();
        using ProcessModule curModule = curProcess.MainModule!;
        _hookId = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);

        if (_hookId == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to install WH_MOUSE_LL hook.");
    }

    public void Uninstall()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
        _proc = null;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (int)wParam == WM_MOUSEWHEEL)
        {
            MSLLHOOKSTRUCT data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            bool injected = (data.flags & LLMHF_INJECTED) != 0;

            if (!injected)
            {
                // Wheel delta lives in the high-order word of mouseData (signed).
                int delta = (short)((data.mouseData >> 16) & 0xFFFF);
                var args = new MouseWheelHookEventArgs(delta, new Point(data.pt.x, data.pt.y));

                try
                {
                    MouseWheel?.Invoke(this, args);
                }
                catch
                {
                    // Never let a handler exception escape into the hook chain.
                }

                if (args.Handled)
                    return (IntPtr)1; // Swallow the original event.
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose() => Uninstall();

    #region Win32 interop

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    #endregion
}
