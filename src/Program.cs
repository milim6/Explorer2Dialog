using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;

namespace Explorer2Dialog
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            bool diagnostics = false;
            var pids = new HashSet<uint>();
            int seconds = 0;
            for (int i = 0; i < args.Length; i++)
            {
                uint pid;
                if (args[i] == "--diagnostics") diagnostics = true;
                else if (args[i] == "--pid" && i + 1 < args.Length && uint.TryParse(args[i + 1], out pid) && pid > 0)
                { pids.Add(pid); i++; }
                else if (args[i] == "--seconds" && i + 1 < args.Length &&
                    int.TryParse(args[i + 1], out seconds) && seconds >= 1 && seconds <= 7200) i++;
                else
                {
                    MessageBox.Show("用法  Explorer2Dialog.exe [--diagnostics] [--pid PID] [--seconds N]", "Explorer2Dialog");
                    return 2;
                }
            }

            bool first;
            using (var instance = new Mutex(true, @"Local\Explorer2Dialog", out first))
            {
                if (!first) return 0;
                Log.Initialize(diagnostics);
                Log.Line("started integrity={0} x64={1} diagnostics={2} filter_count={3}",
                    ProcessIntegrity.Read(ProcessIntegrity.GetCurrentProcessId()), Environment.Is64BitProcess,
                    diagnostics, pids.Count);
                try
                {
                    Application.EnableVisualStyles();
                    var cache = new FolderCache();
                    using (var tracker = new ExplorerTracker(cache))
                    using (var detector = new DialogDetector(cache, pids))
                    using (var events = new WindowEventMonitor((kind, hwnd, time) =>
                    {
                        if (kind == Windows.Foreground) tracker.ForegroundChanged(Windows.GetForegroundWindow());
                        detector.OnEvent(kind, hwnd, time);
                    }))
                    using (var tray = new TrayApplication(cache, detector, seconds))
                    {
                        tracker.ForegroundChanged(Windows.GetForegroundWindow());
                        Application.Run(tray);
                    }
                    return 0;
                }
                catch (Exception e)
                {
                    Log.Error("program", e);
                    MessageBox.Show("Explorer2Dialog 无法继续运行。请查看日志中的错误码。", "Explorer2Dialog");
                    return 1;
                }
                finally
                {
                    Log.Line("stopped");
                    instance.ReleaseMutex();
                }
            }
        }
    }
}
