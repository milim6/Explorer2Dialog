using System;
using System.Runtime.InteropServices;
using System.Threading;
using Accessibility;

namespace Explorer2Dialog
{
    internal static class DialogFolderTree
    {
        private const uint Expand = 0x1102, GetNextItem = 0x110A, SelectItem = 0x110B, MapAccessibleId = 0x112B;
        private const int Root = 0, Next = 1, Child = 4, Caret = 9, ExpandChildren = 2;
        private static readonly Guid ComputerFolder = new Guid("0AC0837C-BBF8-452A-850D-79D08E667CA7");

        [DllImport("oleacc.dll")]
        private static extern int AccessibleObjectFromWindow(IntPtr window, uint objectId, ref Guid iid,
            [MarshalAs(UnmanagedType.Interface)] out IAccessible accessible);

        [DllImport("shell32.dll")]
        private static extern int SHGetKnownFolderIDList(ref Guid folder, uint flags, IntPtr token, out IntPtr idList);

        [DllImport("shell32.dll")]
        private static extern int SHGetNameFromIDList(IntPtr idList, uint kind, out IntPtr name);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam,
            uint flags, uint timeout, out IntPtr result);

        public static void Navigate(IntPtr dialog, IntPtr tree, string target, Action requireCurrent)
        {
            Action requireTree = () =>
            {
                requireCurrent();
                if (!Windows.IsChild(dialog, tree) || Windows.Class(tree) != "SysTreeView32")
                    throw new NavigationSkipped("tree-unavailable");
            };
            requireTree();
            var iid = typeof(IAccessible).GUID;
            IAccessible accessible;
            int hr = AccessibleObjectFromWindow(tree, unchecked((uint)-4), ref iid, out accessible); // OBJID_CLIENT
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
            if (accessible == null) throw new NavigationSkipped("tree-accessibility-unavailable");

            try
            {
                var suffix = "(" + target.Substring(0, 2) + ")";
                var first = Message(tree, GetNextItem, new IntPtr(Root), IntPtr.Zero, requireTree);
                var item = Find(tree, accessible, first, name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase),
                    requireTree, descend: true);
                if (item == IntPtr.Zero)
                {
                    var computerName = DisplayName(ComputerFolder);
                    var computer = Find(tree, accessible, first, name => string.Equals(name, computerName, StringComparison.Ordinal),
                        requireTree, descend: true);
                    if (computer != IntPtr.Zero)
                    {
                        Message(tree, Expand, new IntPtr(ExpandChildren), computer, requireTree);
                        item = WaitForChild(tree, accessible, computer,
                            name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase), requireTree);
                    }
                }
                if (item == IntPtr.Zero) throw new NavigationSkipped("tree-drive-unavailable");

                foreach (var segment in target.Substring(3).Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    Message(tree, Expand, new IntPtr(ExpandChildren), item, requireTree);
                    var child = WaitForChild(tree, accessible, item,
                        name => string.Equals(name, segment, StringComparison.OrdinalIgnoreCase), requireTree);
                    if (child == IntPtr.Zero) throw new NavigationSkipped("tree-folder-unavailable");
                    item = child;
                }

                if (Message(tree, SelectItem, new IntPtr(Caret), item, requireTree) == IntPtr.Zero)
                    throw new NavigationSkipped("tree-selection-rejected");
            }
            finally { Marshal.ReleaseComObject(accessible); }
        }

        private static string DisplayName(Guid folder)
        {
            IntPtr idList, name;
            if (SHGetKnownFolderIDList(ref folder, 0, IntPtr.Zero, out idList) < 0) return null;
            try
            {
                if (SHGetNameFromIDList(idList, 0, out name) < 0) return null; // SIGDN_NORMALDISPLAY
                try { return Marshal.PtrToStringUni(name); }
                finally { Marshal.FreeCoTaskMem(name); }
            }
            finally { Marshal.FreeCoTaskMem(idList); }
        }

        private static IntPtr WaitForChild(IntPtr tree, IAccessible accessible, IntPtr parent, Func<string, bool> matches,
            Action requireTree)
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                var first = Message(tree, GetNextItem, new IntPtr(Child), parent, requireTree);
                var child = Find(tree, accessible, first, matches, requireTree);
                if (child != IntPtr.Zero) return child;
                Thread.Sleep(65);
            }
            return IntPtr.Zero;
        }

        private static IntPtr Find(IntPtr tree, IAccessible accessible, IntPtr first, Func<string, bool> matches,
            Action requireTree, bool descend = false)
        {
            var found = IntPtr.Zero;
            for (var item = first; item != IntPtr.Zero; item = Message(tree, GetNextItem, new IntPtr(Next), item, requireTree))
            {
                int id = Message(tree, MapAccessibleId, item, IntPtr.Zero, requireTree).ToInt32();
                string name = id == 0 ? null : accessible.get_accName(id);
                if (name != null && matches(name))
                {
                    if (descend) return item; // A drive letter identifies the mounted root.
                    if (found != IntPtr.Zero) throw new NavigationSkipped("tree-folder-ambiguous");
                    found = item;
                }
                if (!descend) continue;
                var child = Message(tree, GetNextItem, new IntPtr(Child), item, requireTree);
                found = Find(tree, accessible, child, matches, requireTree, descend: true);
                if (found != IntPtr.Zero) return found;
            }
            return found;
        }

        private static IntPtr Message(IntPtr tree, uint message, IntPtr wParam, IntPtr lParam, Action requireTree)
        {
            requireTree();
            IntPtr result;
            // Parameters are integer flags and opaque tree-item handles owned by this control.
            if (SendMessageTimeout(tree, message, wParam, lParam, 3, 350, out result) == IntPtr.Zero)
                throw new NavigationSkipped("tree-message-timeout");
            return result;
        }
    }
}
