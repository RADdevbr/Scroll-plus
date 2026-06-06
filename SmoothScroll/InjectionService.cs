using System.Runtime.InteropServices;

namespace SmoothScroll;

/// <summary>
/// Injects synthetic mouse-wheel scroll into a target window.
///
/// Primary path: PostMessage(WM_MOUSEWHEEL) posted directly to the target
/// window handle. This bypasses both the low-level mouse hook (no feedback
/// loop) and the OS hit-testing, which is what lets us drive apps that perform
/// their own rendering / input handling (RadiAnt, Weasis/Java Swing) reliably.
///
/// Fallback path: SendInput with MOUSEEVENTF_WHEEL, used only if PostMessage
/// fails (e.g. an invalid handle). SendInput-injected events carry the
/// LLMHF_INJECTED flag and are ignored by <see cref="HookManager"/>.
/// </summary>
public sealed class InjectionService
{
    private const int WM_MOUSEWHEEL = 0x020A;
    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;

    /// <summary>
    /// Posts a single WM_MOUSEWHEEL of <paramref name="delta"/> to <paramref name="hwnd"/>.
    /// Falls back to SendInput when the post fails.
    /// </summary>
    public void PostWheel(IntPtr hwnd, int delta)
    {
        if (delta == 0)
            return;

        GetCursorPos(out POINT p);

        // lParam = screen coordinates: low word = x, high word = y.
        IntPtr lParam = MakeLParam(p.x, p.y);

        // wParam: high-order word = wheel delta (signed), low-order word = key state (none).
        uint wParamRaw = ((uint)(ushort)(short)delta) << 16;
        IntPtr wParam = new IntPtr(wParamRaw);

        if (hwnd == IntPtr.Zero || !PostMessage(hwnd, WM_MOUSEWHEEL, wParam, lParam))
        {
            SendWheelInput(delta);
        }
    }

    /// <summary>SendInput fallback (drives the window currently under the cursor).</summary>
    private static void SendWheelInput(int delta)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = 0,
                    dy = 0,
                    mouseData = (uint)delta,
                    dwFlags = MOUSEEVENTF_WHEEL,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                }
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private static IntPtr MakeLParam(int loWord, int hiWord)
        => new IntPtr((hiWord << 16) | (loWord & 0xFFFF));

    #region Win32 interop

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    #endregion
}
