using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;

namespace Explorer2Dialog
{
    internal sealed class NavigationSkipped : Exception
    {
        public NavigationSkipped(string reason) : base(reason) { }
    }

    internal sealed class InputGuard
    {
        private readonly DialogProbe probe;

        public InputGuard(DialogProbe probe) { this.probe = probe; }

        public void RequireForeground()
        {
            if (probe.Following || !probe.Current)
                throw new NavigationSkipped(probe.Expired ? "deadline" : "foreground-or-lifetime");
        }

        public void RequireIdle()
        {
            RequireForeground();
            if (!probe.InputMonitored) throw new NavigationSkipped("input-monitor-unavailable");
            if (probe.UserInput) throw new NavigationSkipped("user-input");
        }

        public bool Allow(IntPtr expectedFocus)
        {
            RequireIdle();
            return Keyboard.Focus(probe.Dialog) == expectedFocus && expectedFocus != IntPtr.Zero;
        }

        public void Chord(IntPtr focus, ushort modifier, ushort key)
        {
            Keyboard.Chord(() => Allow(focus), modifier, key);
        }

        public void ReplaceText(IntPtr focus, string text)
        {
            Keyboard.ReplaceText(() => Allow(focus), text);
        }
    }

    internal static class DialogNavigator
    {
        public static void Navigate(DialogProbe probe, Picker picker, string target)
        {
            var guard = new InputGuard(probe);
            guard.RequireIdle();
            probe.Phase = "target";
            CheckAccessible(target);
            guard.RequireIdle();

            var originalName = Uia.Value(picker.Filename);
            if (originalName == null) throw new NavigationSkipped("filename-value-unavailable");
            var origin = Uia.CommittedFolder(picker.Breadcrumb);
            if (!FolderPath.Same(origin, target))
            {
                probe.Phase = "address";
                var before = Keyboard.Focus(probe.Dialog);
                if (before == IntPtr.Zero) throw new NavigationSkipped("initial-focus-unavailable");
                // Enter can leave focus on the committed breadcrumb toolbar. Only an active text edit blocks Alt+D.
                if (Windows.IsChild(picker.RebarHandle, before) && Windows.Class(before) == "Edit")
                    throw new NavigationSkipped("address-edit-active");
                guard.Chord(before, 0x12, 0x44); // Alt+D is used only after classifying the picker and its current focus.
                probe.Method = "Alt+D";

                var editor = WaitForEditor(probe.Dialog, picker, guard);
                if (editor == null) throw new NavigationSkipped("address-edit-not-found");
                var focus = new IntPtr(editor.Current.NativeWindowHandle);
                probe.AddressReady = probe.Elapsed;

                probe.Phase = "input";
                var value = Uia.Pattern<ValuePattern>(editor, ValuePattern.Pattern);
                var editOrigin = FolderPath.Normalize(value == null ? null : value.Current.Value);
                if (origin != null && !FolderPath.Same(origin, editOrigin))
                    throw new NavigationSkipped("address-edited");
                bool written = false;
                if (value != null && !value.Current.IsReadOnly)
                {
                    if (!guard.Allow(focus)) throw new NavigationSkipped("address-focus-changed");
                    try
                    {
                        value.SetValue(target);
                        written = FolderPath.Same(FolderPath.Normalize(value.Current.Value), target);
                    }
                    catch (InvalidOperationException) { } // The provider may require keyboard input instead.
                }
                if (written) probe.Method += "+ValuePattern";
                else
                {
                    guard.ReplaceText(focus, target);
                    probe.Method += "+Unicode";
                    if (!WaitUntil(() =>
                    {
                        if (!guard.Allow(focus)) throw new NavigationSkipped("address-focus-changed");
                        return FolderPath.Same(FolderPath.Normalize(Uia.Value(editor)), target);
                    }, guard.RequireForeground, 1700))
                        throw new NavigationSkipped("address-text-not-confirmed");
                }
                probe.InputReady = probe.Elapsed;

                probe.Phase = "enter";
                guard.Chord(focus, 0, 0x0D);
                probe.Method += "+Enter";
            }
            else probe.Method = "already";

            Confirm(probe, picker, target, originalName, guard.RequireForeground);
        }

        public static void Follow(DialogProbe probe, Picker picker, Action requireCurrent)
        {
            requireCurrent();
            probe.Phase = "target";
            string target = probe.ExplorerFolder;
            CheckAccessible(target);
            var originalName = ReadFilename(picker);
            if (originalName == null) throw new NavigationSkipped("filename-value-unavailable");

            if (FolderPath.Same(ReadCommittedFolder(picker), target)) probe.Method = "already";
            else
            {
                var focus = Keyboard.Focus(probe.Dialog);
                if (focus != IntPtr.Zero && Windows.IsChild(picker.RebarHandle, focus) && Windows.Class(focus) == "Edit")
                    throw new NavigationSkipped("address-edit-active");
                probe.Phase = "tree";
                probe.Method = "folder-tree";
                DialogFolderTree.Navigate(probe.Dialog.Window, new IntPtr(picker.Tree.Current.NativeWindowHandle), target, requireCurrent);
            }
            Confirm(probe, picker, target, originalName, requireCurrent);
        }

        private static void Confirm(DialogProbe probe, Picker picker, string target, string originalName, Action requireCurrent)
        {
            probe.Phase = "confirm";
            if (!WaitUntil(() => FolderPath.Same(ReadCommittedFolder(picker), target), requireCurrent, 1750))
                throw new NavigationSkipped("directory-not-confirmed");
            probe.DirectoryReady = probe.Elapsed;
            requireCurrent();
            if (!FileViewAvailable(picker)) throw new NavigationSkipped("file-view-unavailable");
            if (!string.Equals(ReadFilename(picker), originalName, StringComparison.Ordinal))
                throw new NavigationSkipped("filename-changed");
            requireCurrent();
            probe.Confirmed = probe.Elapsed;
            probe.Record("confirmed", "none");
        }

        private static bool FileViewAvailable(Picker picker)
        {
            try { if (!picker.List.Current.IsOffscreen) return true; }
            catch (ElementNotAvailableException) { }
            var view = picker.FindList();
            return view != null && !view.Current.IsOffscreen;
        }

        private static string ReadFilename(Picker picker)
        {
            try { return Uia.Value(picker.Filename); }
            catch (ElementNotAvailableException) { return Uia.Value(picker.FindFilename(true)); }
        }

        private static void CheckAccessible(string folder)
        {
            if ((File.GetAttributes(folder) & FileAttributes.Directory) == 0)
                throw new NavigationSkipped("not-a-directory");
            using (var entries = Directory.EnumerateFileSystemEntries(folder).GetEnumerator()) entries.MoveNext();
        }

        private static AutomationElement WaitForEditor(DialogInstance dialog, Picker picker, InputGuard guard)
        {
            AutomationElement edit = null;
            return WaitUntil(() =>
            {
                guard.RequireIdle();
                var focus = Keyboard.Focus(dialog);
                if (focus == IntPtr.Zero || !Windows.IsChild(picker.RebarHandle, focus) ||
                    Windows.Class(focus) != "Edit") return false;
                edit = Uia.Find(picker.Rebar, Uia.Type(ControlType.Edit), Uia.Class("Edit"), Uia.Id("41477"),
                    Uia.Eq(AutomationElement.NativeWindowHandleProperty, focus.ToInt32()));
                return edit != null && guard.Allow(focus);
            }, guard.RequireForeground, 700) ? edit : null;
        }

        private static string ReadCommittedFolder(Picker picker)
        {
            try { return Uia.CommittedFolder(picker.Breadcrumb); }
            catch (ElementNotAvailableException)
            {
                picker.Breadcrumb = Uia.Breadcrumb(picker.Rebar);
                return Uia.CommittedFolder(picker.Breadcrumb);
            }
        }

        private static bool WaitUntil(Func<bool> check, Action requireCurrent, int milliseconds)
        {
            var timer = Stopwatch.StartNew();
            do
            {
                requireCurrent();
                if (check()) return true;
                Thread.Sleep(24);
            } while (timer.ElapsedMilliseconds < milliseconds);
            return false;
        }
    }

    // Same x64 INPUT union and UTF-16 SendInput path as the PoC.
    internal static class Keyboard
    {
        public static readonly UIntPtr InputTag = new UIntPtr(unchecked((uint)Guid.NewGuid().GetHashCode()) | 1U);

        [StructLayout(LayoutKind.Sequential)]
        private struct Input
        {
            public uint Type;
            public InputData Data;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputData
        {
            [FieldOffset(0)] public KeyData Key;
            [FieldOffset(0)] public MouseData Mouse;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyData
        {
            public ushort VirtualKey, Scan;
            public uint Flags, Time;
            public UIntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MouseData
        {
            public int X, Y;
            public uint Wheel, Flags, Time;
            public UIntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GuiThreadInfo
        {
            public uint Size, Flags;
            public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
            public int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, [In] Input[] inputs, int inputSize);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
        public static IntPtr Focus(DialogInstance dialog)
        {
            var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf(typeof(GuiThreadInfo)) };
            // Reject move/size, menu modes, and focus outside the exact dialog.
            return GetGUIThreadInfo(dialog.Thread, ref info) && (info.Flags & 0x1E) == 0 &&
                Windows.Root(info.Focus) == dialog.Window ? info.Focus : IntPtr.Zero;
        }

        public static void Chord(Func<bool> ready, ushort modifier, ushort key)
        {
            var inputs = new List<Input>();
            AddChord(inputs, modifier, key);
            Send(ready, inputs);
        }

        public static void ReplaceText(Func<bool> ready, string text)
        {
            var inputs = new List<Input>();
            AddChord(inputs, 0x11, 0x41);
            foreach (var ch in text)
            {
                inputs.Add(Key(0, ch, 4)); // KEYEVENTF_UNICODE, one UTF-16 code unit.
                inputs.Add(Key(0, ch, 4 | 2));
            }
            Send(ready, inputs);
        }

        private static void AddChord(List<Input> inputs, ushort modifier, ushort key)
        {
            if (modifier != 0) inputs.Add(Key(modifier, 0, 0));
            inputs.Add(Key(key, 0, 0));
            inputs.Add(Key(key, 0, 2));
            if (modifier != 0) inputs.Add(Key(modifier, 0, 2));
        }

        private static Input Key(ushort key, ushort scan, uint flags)
        {
            return new Input { Type = 1, Data = new InputData
                { Key = new KeyData { VirtualKey = key, Scan = scan, Flags = flags, ExtraInfo = InputTag } } };
        }

        private static void Send(Func<bool> ready, List<Input> events)
        {
            foreach (int key in new[] { 0x01, 0x02, 0x04, 0x05, 0x06, 0x10, 0x11, 0x12, 0x5B, 0x5C })
                if ((GetAsyncKeyState(key) & 0x8000) != 0) throw new NavigationSkipped("held-input");

            var inputs = events.ToArray();
            if (!ready()) throw new NavigationSkipped("focus-changed-before-input");
            uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Input)));
            if (sent != inputs.Length) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }
}
