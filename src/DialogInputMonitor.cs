using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Explorer2Dialog
{
    internal sealed class DialogInputMonitor : IDisposable
    {
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate IntPtr InputCallback(int code, IntPtr message, IntPtr data);

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyEvent
        {
            public uint VirtualKey, Scan, Flags, Time;
            public UIntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ClientRect
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int kind, InputCallback callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPhysicalPoint(Point point);
        [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr window, ref Point point);
        [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out ClientRect bounds);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);

        private readonly InputCallback keyCallback, mouseCallback;
        private IntPtr keyHook, mouseHook;
        private DialogProbe active;
        private bool waitingForReturn;
        private IntPtr clickedDialog;
        private uint clickTick;

        public DialogInputMonitor()
        {
            keyCallback = ObserveKey;
            mouseCallback = ObserveMouse;
        }

        public void WatchReturn()
        {
            waitingForReturn = true;
            mouseHook = SetWindowsHookEx(14, mouseCallback, GetModuleHandle(null), 0); // WH_MOUSE_LL
        }

        public void Watch(DialogProbe probe, uint foregroundTick)
        {
            int clickAge = unchecked((int)(foregroundTick - clickTick));
            probe.UserInput = clickedDialog == probe.Dialog.Window && clickAge >= -16 && clickAge <= 200;
            clickedDialog = IntPtr.Zero;
            active = probe;
            var module = GetModuleHandle(null);
            keyHook = SetWindowsHookEx(13, keyCallback, module, 0); // WH_KEYBOARD_LL
            mouseHook = SetWindowsHookEx(14, mouseCallback, module, 0); // WH_MOUSE_LL
            probe.InputMonitored = keyHook != IntPtr.Zero && mouseHook != IntPtr.Zero;
            if (!probe.InputMonitored) Stop();
        }

        private IntPtr ObserveKey(int code, IntPtr message, IntPtr data)
        {
            int kind = message.ToInt32();
            if (code == 0 && (kind == 0x100 || kind == 0x104) && IsTargetForeground()) // WM_KEYDOWN, WM_SYSKEYDOWN
            {
                var input = (KeyEvent)Marshal.PtrToStructure(data, typeof(KeyEvent));
                bool ours = (input.Flags & 0x10) != 0 && input.ExtraInfo == Keyboard.InputTag;
                if (!ours && !Modifier(input.VirtualKey)) active.UserInput = true;
            }
            return CallNextHookEx(IntPtr.Zero, code, message, data);
        }

        private IntPtr ObserveMouse(int code, IntPtr message, IntPtr data)
        {
            int kind = message.ToInt32();
            // WM_LBUTTONDOWN, WM_RBUTTONDOWN, WM_MBUTTONDOWN, WM_XBUTTONDOWN, WM_MOUSEWHEEL, WM_MOUSEHWHEEL.
            bool clicked = kind == 0x201 || kind == 0x204 || kind == 0x207 || kind == 0x20B;
            bool scrolled = kind == 0x20A || kind == 0x20E;
            if (code == 0 && (clicked || scrolled))
            {
                var point = new Point(Marshal.ReadInt32(data), Marshal.ReadInt32(data, 4));
                if (IsTargetForeground() && (scrolled || InClient(active.Dialog.Window, point))) active.UserInput = true;
                else if (waitingForReturn && clicked)
                {
                    // Mouse-down can hit the dialog before Windows reports that it is foreground.
                    var hit = Windows.Root(WindowFromPhysicalPoint(point));
                    if (Windows.Class(hit) == "#32770" && InClient(hit, point))
                    {
                        clickedDialog = hit;
                        clickTick = unchecked((uint)Marshal.ReadInt32(data, 16)); // MSLLHOOKSTRUCT.time
                    }
                }
            }
            return CallNextHookEx(IntPtr.Zero, code, message, data);
        }

        private static bool InClient(IntPtr hwnd, Point point)
        {
            // The mouse callback receives per-monitor-aware screen coordinates.
            var previousDpi = SetThreadDpiAwarenessContext(new IntPtr(-4)); // PER_MONITOR_AWARE_V2
            if (previousDpi == IntPtr.Zero) return true;
            try
            {
                ClientRect rect;
                return !ScreenToClient(hwnd, ref point) || !GetClientRect(hwnd, out rect) ||
                    (point.X >= rect.Left && point.Y >= rect.Top && point.X < rect.Right && point.Y < rect.Bottom);
            }
            finally { SetThreadDpiAwarenessContext(previousDpi); }
        }

        private bool IsTargetForeground()
        {
            return active != null && !active.Revoked && Windows.GetForegroundWindow() == active.Dialog.Window;
        }

        private static bool Modifier(uint key)
        {
            return key == 0x10 || key == 0x11 || key == 0x12 || key == 0x5B || key == 0x5C ||
                (key >= 0xA0 && key <= 0xA5);
        }

        public void CompleteNavigation()
        {
            if (active != null) Stop();
        }

        public void Stop()
        {
            active = null;
            waitingForReturn = false;
            if (keyHook != IntPtr.Zero) UnhookWindowsHookEx(keyHook);
            if (mouseHook != IntPtr.Zero) UnhookWindowsHookEx(mouseHook);
            keyHook = mouseHook = IntPtr.Zero;
        }

        public void Dispose() { Stop(); }
    }
}
