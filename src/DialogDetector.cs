using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using Timer = System.Windows.Forms.Timer;

namespace Explorer2Dialog
{
    internal sealed class Picker
    {
        public AutomationElement Root, Shell, Filename, List, Rebar, Breadcrumb, Tree;
        private AutomationElement topFilenameCombo, topFilenameEdit;
        public IntPtr RebarHandle;
        public string Kind = "unsupported";
        public bool Modern, Legacy, HasModernLayout;

        public static bool HasButtons(IntPtr hwnd)
        {
            return Windows.Class(Windows.GetDlgItem(hwnd, 1)) == "Button" &&
                Windows.Class(Windows.GetDlgItem(hwnd, 2)) == "Button";
        }

        public static Picker Read(IntPtr hwnd)
        {
            var picker = new Picker();
            picker.Root = AutomationElement.FromHandle(hwnd);
            if (picker.Root.Current.ControlType != ControlType.Window || !HasButtons(hwnd)) return picker;

            picker.ReadTopControls();
            picker.HasModernLayout = picker.Shell != null && picker.Rebar != null;
            if (picker.HasModernLayout)
            {
                picker.Filename = picker.FindFilename();
                if (picker.Filename == null) return picker;
                picker.List = picker.FindList();
                picker.Tree = Uia.Find(picker.Shell, Uia.Type(ControlType.Tree), Uia.Class("SysTreeView32"), Uia.Id("100"));
                picker.Breadcrumb = Uia.Breadcrumb(picker.Rebar);
                picker.Modern = picker.List != null && picker.Tree != null && picker.Breadcrumb != null;
            }
            else if (Windows.Class(Windows.GetDlgItem(hwnd, 1137)) == "ComboBox")
            {
                picker.Filename = picker.FindFilename();
                picker.Legacy = picker.Filename != null &&
                    Uia.Child(picker.Root, Uia.Class("SysListView32"), Uia.Id("1"),
                        new OrCondition(Uia.Type(ControlType.List), Uia.Type(ControlType.DataGrid))) != null;
            }

            if (picker.Modern || picker.Legacy)
            {
                string title = picker.Root.Current.Name ?? "";
                picker.Kind = title.StartsWith("另存为") || title.StartsWith("保存") ||
                    title.StartsWith("Save", StringComparison.OrdinalIgnoreCase) ? "save" :
                    title.StartsWith("打开") || title.StartsWith("Open", StringComparison.OrdinalIgnoreCase) ? "open" : "file";
            }
            return picker;
        }

        private void ReadTopControls()
        {
            var request = new CacheRequest();
            request.Add(AutomationElement.ClassNameProperty);
            request.Add(AutomationElement.AutomationIdProperty);
            request.Add(AutomationElement.ControlTypeProperty);
            request.Add(AutomationElement.NativeWindowHandleProperty);
            using (request.Activate())
            {
                var selection = new OrCondition(Uia.Class("DUIViewWndClassName"), Uia.Class("ReBarWindow32"),
                    Uia.Id("1148"), Uia.Id("1152"));
                foreach (AutomationElement child in Root.FindAll(TreeScope.Children, selection))
                {
                    var data = child.Cached;
                    if (data.ClassName == "DUIViewWndClassName") Shell = child;
                    else if (data.ClassName == "ReBarWindow32" && data.AutomationId == "40965")
                    {
                        Rebar = child;
                        RebarHandle = new IntPtr(data.NativeWindowHandle);
                    }
                    else if (data.AutomationId == "1148" && data.ControlType == ControlType.ComboBox)
                        topFilenameCombo = child;
                    else if (data.AutomationId == "1152" && data.ControlType == ControlType.Edit && data.ClassName == "Edit")
                        topFilenameEdit = child;
                }
            }
        }

        public AutomationElement FindFilename(bool refresh = false)
        {
            if (refresh)
            {
                topFilenameCombo = topFilenameEdit = null;
                ReadTopControls();
            }
            var combo = topFilenameCombo ?? Uia.Find(Shell, Uia.Type(ControlType.ComboBox), Uia.Id("FileNameControlHost"));
            return Uia.Find(combo, Uia.Type(ControlType.Edit), Uia.Class("Edit")) ?? topFilenameEdit;
        }

        public AutomationElement FindList()
        {
            return Uia.Find(Shell, Uia.Type(ControlType.List), Uia.Class("UIItemsView"));
        }
    }

    internal sealed class DialogProbe
    {
        public readonly DialogInstance Dialog;
        public readonly IntPtr ExplorerWindow;
        public readonly string ExplorerFolder;
        public readonly int Visit;
        public readonly long ForegroundStamp, ReceivedStamp;
        public long Started;
        public Task Work;
        public double Shown = -1, Detected = -1, FolderReady = -1, AddressReady = -1, InputReady = -1, DirectoryReady = -1, Confirmed = -1;
        public string Kind = "unknown", Method = "none", Phase = "detect";
        public bool InputMonitored;
        public volatile bool Revoked, UserInput;

        public DialogProbe(DialogInstance dialog, uint time, IntPtr explorerWindow = default(IntPtr), string explorerFolder = null)
        {
            Dialog = dialog;
            ExplorerWindow = explorerWindow;
            ExplorerFolder = explorerFolder;
            Visit = Following ? 0 : ++dialog.Visits;
            ReceivedStamp = Stopwatch.GetTimestamp();
            int queuedMs = Math.Max(0, unchecked((int)(Windows.NowTick - time)));
            // Use the foreground event time, including the time it spent in the WinEvent queue.
            ForegroundStamp = ReceivedStamp - (long)(queuedMs * (Stopwatch.Frequency / 1000.0));
            if (dialog.Exists) Shown = 0.0;
        }

        public double Elapsed { get { return Windows.Milliseconds(ForegroundStamp, Stopwatch.GetTimestamp()); } }
        public bool Expired { get { return Started != 0 && Windows.Milliseconds(Started, Stopwatch.GetTimestamp()) >= 4500; } }
        public bool Following { get { return ExplorerWindow != IntPtr.Zero; } }
        public bool Current
        {
            get
            {
                return !Revoked && !Expired && (Following
                    ? Dialog.Exists && Windows.GetForegroundWindow() == ExplorerWindow
                    : Dialog.IsForeground);
            }
        }

        public void Record(string result, string reason, int error = 0)
        {
            if (Following)
            {
                Log.Line("follow id={0} hwnd={1} pid={2} explorer={3} kind={4} directory_ms={5:F1} end_ms={6:F1} result={7} phase={8} reason={9} hr=0x{10:X8} method={11}",
                    Dialog.Generation, Windows.Hex(Dialog.Window), Dialog.Pid, Windows.Hex(ExplorerWindow), Kind,
                    DirectoryReady, Elapsed, result, Phase, reason, error, Method);
                return;
            }
            Log.Line("dialog id={0} visit={1} hwnd={2} pid={3} tid={4} kind={5} event=0x3 foreground_qpc={6} " +
                "received_ms={7:F1} shown_ms={8:F1} dispatch_ms={9:F1} detect_ms={10:F1} folder_ms={11:F1} address_ms={12:F1} " +
                "input_ms={13:F1} directory_ms={14:F1} confirm_ms={15:F1} end_ms={16:F1} result={17} phase={18} reason={19} hr=0x{20:X8} method={21}",
                Dialog.Generation, Visit, Windows.Hex(Dialog.Window), Dialog.Pid, Dialog.Thread, Kind,
                ForegroundStamp, Windows.Milliseconds(ForegroundStamp, ReceivedStamp), Shown,
                Started == 0 ? -1 : Windows.Milliseconds(ForegroundStamp, Started), Detected, FolderReady, AddressReady, InputReady,
                DirectoryReady, Confirmed, Elapsed, result, Phase, reason, error, Method);
        }
    }

    internal sealed class DialogDetector : IDisposable
    {
        private readonly FolderCache cache;
        private readonly HashSet<uint> testPids;
        private readonly int? integrity = ProcessIntegrity.Read(ProcessIntegrity.GetCurrentProcessId());
        private readonly DialogLifecycle lifecycle = new DialogLifecycle();
        private readonly DialogInputMonitor input = new DialogInputMonitor();
        private readonly List<DialogProbe> workers = new List<DialogProbe>();
        private readonly Timer timer = new Timer { Interval = 25 };
        private DialogProbe pending;
        private DialogInstance recentDialog;
        private IntPtr lastForeground, followExplorer;
        private string lastFollowFolder;
        private bool enabled = true;

        public DialogDetector(FolderCache cache, HashSet<uint> testPids)
        {
            this.cache = cache;
            this.testPids = testPids;
            timer.Tick += Tick;
            lastForeground = Windows.GetForegroundWindow();
            var existing = IsVisiblePicker(lastForeground) ? lastForeground : Windows.FindSingleVisibleWindow(IsVisiblePicker);
            if (existing != IntPtr.Zero)
            {
                uint pid;
                var thread = Windows.GetWindowThreadProcessId(existing, out pid);
                if (thread != 0) recentDialog = lifecycle.AtForeground(existing, pid, thread, Windows.NowTick);
            }
            if (Windows.IsExplorer(lastForeground)) FollowExplorer(lastForeground);
            else if (Windows.HasVisibleDialog(lastForeground)) input.WatchReturn();
            // Warm the managed UIA client on an MTA worker without enumerating windows.
            Task.Run(() =>
            {
                try { GC.KeepAlive(AutomationElement.RootElement); }
                catch (Exception e) { Log.Error("uia-warmup", e); }
            });
        }

        public bool Enabled
        {
            get { return enabled; }
            set
            {
                if (value == enabled) return;
                enabled = value;
                if (!enabled)
                {
                    pending = null;
                    foreach (var work in workers) work.Revoked = true;
                    input.Stop();
                }
                else
                {
                    var foreground = Windows.GetForegroundWindow();
                    if (Windows.IsExplorer(foreground)) FollowExplorer(foreground);
                }
                Log.Line("mode {0}", enabled ? "enabled" : "paused");
            }
        }

        public void OnEvent(uint kind, IntPtr hwnd, uint time)
        {
            if (kind == Windows.Hide || kind == Windows.Destroy)
            {
                if (lifecycle.Close(hwnd, time))
                {
                    if (recentDialog != null && recentDialog.Closed) recentDialog = null;
                    if (lastForeground == hwnd)
                    {
                        lastForeground = IntPtr.Zero;
                        input.Stop();
                    }
                }
                return;
            }

            if (kind != Windows.Foreground || lastForeground == hwnd) return;
            lastForeground = hwnd;
            foreach (var work in workers) work.Revoked = true;
            pending = null;
            followExplorer = IntPtr.Zero;
            timer.Interval = 25;
            input.Stop();
            if (!enabled || Windows.GetForegroundWindow() != hwnd) return;
            if (Windows.IsExplorer(hwnd))
            {
                FollowExplorer(hwnd);
                return;
            }
            if (Windows.HasVisibleDialog(hwnd))
            {
                input.WatchReturn();
                return;
            }

            uint pid;
            var thread = Windows.GetWindowThreadProcessId(hwnd, out pid);
            if (thread == 0 || (testPids.Count != 0 && !testPids.Contains(pid))) return;
            if (Windows.Class(hwnd) != "#32770" || Windows.Root(hwnd) != hwnd)
            {
                if (Log.Detailed && testPids.Count > 0)
                    Log.Line("ignored class hwnd={0} pid={1}", Windows.Hex(hwnd), pid);
                return;
            }

            var dialog = lifecycle.AtForeground(hwnd, pid, thread, time);
            if (recentDialog != dialog)
            {
                recentDialog = dialog;
                lastFollowFolder = null;
            }
            pending = new DialogProbe(dialog, time);
            input.Watch(pending, time);
            timer.Start();
        }

        private bool IsVisiblePicker(IntPtr window)
        {
            if (!Windows.IsWindowVisible(window) || Windows.Class(window) != "#32770" ||
                Windows.Root(window) != window || !Picker.HasButtons(window)) return false;
            uint pid;
            Windows.GetWindowThreadProcessId(window, out pid);
            return pid != 0 && (testPids.Count == 0 || testPids.Contains(pid)) &&
                ProcessIntegrity.Read(pid) == integrity;
        }

        private void FollowExplorer(IntPtr explorer)
        {
            input.WatchReturn();
            followExplorer = explorer;
            if (recentDialog == null || !recentDialog.Exists) return;
            timer.Interval = 150;
            timer.Start();
        }

        private bool WorkersAvailable { get { return workers.Count < 2 && workers.TrueForAll(w => w.Revoked); } }

        private void StartWork(DialogProbe probe)
        {
            probe.Started = Stopwatch.GetTimestamp();
            probe.Work = Task.Run(() => Process(probe));
            workers.Add(probe);
        }

        private void TryFollowFolder()
        {
            string reason;
            var target = cache.TryRead(0, out reason);
            if (target == null || FolderPath.Same(lastFollowFolder, target)) return;
            lastFollowFolder = target;
            if (target.Length < 3 || target[1] != ':') return; // UNC roots have no drive item to address.
            lock (recentDialog)
            {
                if (recentDialog.HasTarget(target)) return;
            }
            StartWork(new DialogProbe(recentDialog, Windows.NowTick, followExplorer, target));
        }

        private void Tick(object sender, EventArgs args)
        {
            for (int i = workers.Count - 1; i >= 0; i--)
            {
                var work = workers[i];
                if (work.Work.IsCompleted) { workers.RemoveAt(i); continue; }
                if (!work.Revoked && work.Expired)
                {
                    work.Revoked = true;
                    Log.Line("worker deadline id={0} visit={1} phase={2}", work.Dialog.Generation, work.Visit, work.Phase);
                }
            }

            var probe = pending;
            if (probe != null)
            {
                var hwnd = probe.Dialog.Window;
                var elapsed = probe.Elapsed;
                bool visible = probe.Dialog.Exists;
                // A newly created picker can become foreground before the shell displays its controls.
                if (visible && probe.Shown < 0) probe.Shown = elapsed;
                double shownFor = elapsed - probe.Shown;
                if (probe.Dialog.Closed || Windows.GetForegroundWindow() != hwnd) pending = null;
                else if (probe.Shown < 0 ? elapsed > 4500 : shownFor > 1250)
                {
                    pending = null;
                    probe.Record("skipped", probe.Shown < 0 ? "dialog-not-visible" : "late-or-busy");
                }
                else if (visible && shownFor >= (probe.Visit == 1 ? 65 : 0) &&
                    WorkersAvailable &&
                    (Picker.HasButtons(hwnd) || shownFor >= 350))
                {
                    pending = null;
                    StartWork(probe);
                }
            }

            bool waitingToFollow = enabled && followExplorer != IntPtr.Zero && recentDialog != null &&
                recentDialog.Exists && Windows.GetForegroundWindow() == followExplorer;
            if (waitingToFollow && WorkersAvailable) TryFollowFolder();
            if (pending == null && workers.TrueForAll(w => w.Revoked)) input.CompleteNavigation();
            if (!waitingToFollow && pending == null && workers.Count == 0) timer.Stop();
        }

        private void Process(DialogProbe probe)
        {
            var hwnd = probe.Dialog.Window;
            try
            {
                if (!probe.Current) { probe.Record("skipped", "inactive"); return; }
                probe.Phase = "integrity";
                int? level = ProcessIntegrity.Read(probe.Dialog.Pid);
                if (!level.HasValue || level != integrity)
                {
                    Log.Line("integrity id={0} own={1} target={2}", probe.Dialog.Generation, integrity, level);
                    probe.Record("skipped", "different-or-unknown-integrity");
                    return;
                }
                using (var process = System.Diagnostics.Process.GetProcessById((int)probe.Dialog.Pid))
                {
                    if (string.Equals(process.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase))
                    { probe.Record("skipped", "explorer-owner"); return; }
                }

                probe.Phase = "detect";
                if (!Picker.HasButtons(hwnd)) { probe.Record("ignored", "control-ids"); return; }
                var picker = Picker.Read(hwnd);
                if (!picker.Modern && !picker.Legacy && picker.HasModernLayout && probe.Current)
                {
                    Thread.Sleep(55);
                    if (probe.Current) picker = Picker.Read(hwnd);
                }
                probe.Detected = probe.Elapsed;
                probe.Kind = picker.Kind;
                if (!probe.Current) { probe.Record("skipped", "inactive"); return; }
                if (picker.Legacy) { probe.Record("ignored", "legacy-recognized"); return; }
                if (!picker.Modern)
                {
                    if (Log.Detailed)
                        Log.Line("shape id={0} pane={1} rebar={2} filename={3} list={4} tree={5} breadcrumb={6}",
                            probe.Dialog.Generation, picker.Shell != null, picker.Rebar != null,
                            picker.Filename != null, picker.List != null, picker.Tree != null, picker.Breadcrumb != null);
                    probe.Record("ignored", "picker-shape");
                    return;
                }

                if (probe.Following)
                {
                    DialogNavigator.Follow(probe, picker, () => RequireCurrentFolder(probe));
                    lock (probe.Dialog) probe.Dialog.TryUseFolder(probe.ExplorerFolder);
                    return;
                }

                probe.Phase = "folder";
                string reason;
                string target = cache.TryRead(350, out reason);
                probe.FolderReady = probe.Elapsed;
                if (target == null) { probe.Record("skipped", reason); return; }
                lock (probe.Dialog)
                {
                    if (!probe.Current) { probe.Record("skipped", "inactive"); return; }
                    if (!probe.Dialog.TryUseFolder(target)) { probe.Record("skipped", "same-explorer-folder"); return; }
                }
                if (Log.Detailed) Log.Line("target id={0} visit={1}{2}", probe.Dialog.Generation, probe.Visit, Log.PathDetail("folder", target));
                DialogNavigator.Navigate(probe, picker, target);
            }
            catch (NavigationSkipped e) { probe.Record("skipped", e.Message); }
            catch (Exception e) { probe.Record("failed", e.GetType().Name, e.HResult); }
        }

        private void RequireCurrentFolder(DialogProbe probe)
        {
            if (!probe.Current) throw new NavigationSkipped("inactive");
            string reason;
            if (!FolderPath.Same(probe.ExplorerFolder, cache.TryRead(0, out reason)))
                throw new NavigationSkipped("explorer-folder-changed");
        }

        public void Dispose()
        {
            enabled = false;
            pending = null;
            timer.Dispose();
            input.Dispose();
            lifecycle.Clear();
            foreach (var work in workers) work.Revoked = true;
        }
    }
}
