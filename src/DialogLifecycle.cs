using System;
using System.Collections.Generic;

namespace Explorer2Dialog
{
    internal sealed class DialogInstance
    {
        public readonly IntPtr Window;
        public readonly uint Pid, Thread, BirthTick;
        public readonly long Generation;
        public int Visits; // Written on the WinEvent thread.
        public volatile bool Closed;
        private string lastTarget;

        public DialogInstance(IntPtr window, uint pid, uint thread, uint time, long generation)
        {
            Window = window;
            Pid = pid;
            Thread = thread;
            BirthTick = time;
            Generation = generation;
        }

        // The caller holds this instance's lock when reading or selecting a target.
        public bool HasTarget(string path) { return FolderPath.Same(lastTarget, path); }

        public bool TryUseFolder(string path)
        {
            if (HasTarget(path)) return false;
            lastTarget = path;
            return true;
        }

        public bool Exists
        {
            get
            {
                uint pid;
                return !Closed && Windows.GetWindowThreadProcessId(Window, out pid) == Thread &&
                    pid == Pid && Windows.IsWindowVisible(Window);
            }
        }

        public bool IsForeground { get { return Exists && Windows.GetForegroundWindow() == Window; } }
    }

    internal sealed class DialogLifecycle
    {
        private readonly Dictionary<IntPtr, DialogInstance> instances = new Dictionary<IntPtr, DialogInstance>();
        private long next;

        public DialogInstance AtForeground(IntPtr hwnd, uint pid, uint thread, uint time)
        {
            DialogInstance current;
            if (instances.TryGetValue(hwnd, out current))
            {
                if (current.Pid == pid && current.Thread == thread && !current.Closed) return current;
                Forget(current);
            }

            if (instances.Count >= 64)
            {
                var stale = new List<DialogInstance>();
                foreach (var item in instances.Values)
                    if (!item.Exists) stale.Add(item);
                foreach (var item in stale) Forget(item);
            }

            current = new DialogInstance(hwnd, pid, thread, time, ++next);
            instances[hwnd] = current;
            return current;
        }

        public bool Close(IntPtr hwnd, uint time)
        {
            DialogInstance item;
            if (!instances.TryGetValue(hwnd, out item) || unchecked((int)(time - item.BirthTick)) < 0) return false;
            Forget(item);
            return true;
        }

        private void Forget(DialogInstance item)
        {
            item.Closed = true;
            instances.Remove(item.Window);
            if (item.Visits > 0) Log.Line("closed id={0} hwnd={1} pid={2}", item.Generation, Windows.Hex(item.Window), item.Pid);
        }

        public void Clear()
        {
            foreach (var item in instances.Values) item.Closed = true;
            instances.Clear();
        }
    }
}
