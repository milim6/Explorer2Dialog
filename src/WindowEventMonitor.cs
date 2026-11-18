using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Explorer2Dialog
{
    internal static class Windows
    {
        public const uint Foreground = 0x0003;
        public const uint Destroy = 0x8001, Hide = 0x8003;

        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsChild(IntPtr parent, IntPtr child);
        [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr parent, int id);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        private delegate bool EnumerateWindow(IntPtr window, IntPtr unused);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumerateWindow callback, IntPtr unused);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hwnd, StringBuilder buffer, int length);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);

        public static IntPtr Root(IntPtr hwnd) { return GetAncestor(hwnd, 2); }

        public static string Class(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return "";
            var buffer = new StringBuilder(128);
            GetClassName(hwnd, buffer, buffer.Capacity);
            return buffer.ToString();
        }

        public static bool IsExplorer(IntPtr hwnd)
        {
            var name = Class(hwnd);
            return name == "CabinetWClass" || name == "ExploreWClass";
        }

        public static bool HasVisibleDialog(IntPtr hwnd)
        {
            var popup = GetWindow(hwnd, 6); // GW_ENABLEDPOPUP
            return popup != hwnd && Class(popup) == "#32770" && IsWindowVisible(popup);
        }

        public static IntPtr FindSingleVisibleWindow(Func<IntPtr, bool> predicate)
        {
            var found = IntPtr.Zero;
            EnumWindows((window, unused) =>
            {
                if (!IsWindowVisible(window) || !predicate(window)) return true;
                if (found == IntPtr.Zero) { found = window; return true; }
                found = IntPtr.Zero;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        public static IntPtr FirstChild(IntPtr hwnd, string className)
        {
            return hwnd == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(hwnd, IntPtr.Zero, className, null);
        }

        public static string Hex(IntPtr hwnd) { return "0x" + hwnd.ToInt64().ToString("X"); }
        public static uint NowTick { get { return unchecked((uint)Environment.TickCount); } }

        public static double Milliseconds(long from, long to)
        {
            return (to - from) * 1000.0 / Stopwatch.Frequency;
        }
    }

    internal sealed class WindowEventMonitor : IDisposable
    {
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void WinEvent(IntPtr hook, uint type, IntPtr hwnd, int objectId, int childId,
            uint thread, uint timestamp);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module,
            WinEvent callback, uint pid, uint thread, uint flags);
        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hook);

        private readonly List<IntPtr> hooks = new List<IntPtr>();
        private readonly WinEvent callback;
        private GCHandle callbackRoot;

        public WindowEventMonitor(Action<uint, IntPtr, uint> onEvent)
        {
            uint[] ranges = { Windows.Foreground, Windows.Foreground, Windows.Destroy, Windows.Hide };
            callback = delegate(IntPtr hook, uint type, IntPtr hwnd, int objectId, int childId,
                uint thread, uint timestamp)
            {
                if (hwnd == IntPtr.Zero || objectId != 0 || childId != 0) return;
                try { onEvent(type, hwnd, timestamp); }
                catch (Exception e) { Log.Error("win-event", e); }
            };
            callbackRoot = GCHandle.Alloc(callback);
            try
            {
                for (var i = 0; i < ranges.Length; i += 2)
                {
                    // OUTOFCONTEXT | SKIPOWNPROCESS. Callbacks run on the registering message loop.
                    var hook = SetWinEventHook(ranges[i], ranges[i + 1], IntPtr.Zero, callback, 0, 0, 2);
                    if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                    hooks.Add(hook);
                }
            }
            catch { Dispose(); throw; }
        }

        public void Dispose()
        {
            foreach (var hook in hooks) UnhookWinEvent(hook);
            hooks.Clear();
            if (callbackRoot.IsAllocated) callbackRoot.Free();
        }
    }

}
