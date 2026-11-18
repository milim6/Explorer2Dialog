using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.CSharp.RuntimeBinder;

namespace Explorer2Dialog
{
    internal sealed class ExplorerTracker : IDisposable
    {
        private sealed class Sample
        {
            public readonly IntPtr Window;
            public readonly long Revision;
            public readonly bool Departure;

            public Sample(IntPtr window, long revision, bool departure)
            {
                Window = window;
                Revision = revision;
                Departure = departure;
            }
        }

        private readonly FolderCache cache;
        private readonly object gate = new object();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly Thread worker;
        private IntPtr foregroundExplorer;
        private Sample latest;
        private volatile bool stopped;

        public ExplorerTracker(FolderCache cache)
        {
            this.cache = cache;
            worker = new Thread(Track) { IsBackground = true, Name = "Explorer STA" };
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
        }

        public void ForegroundChanged(IntPtr foreground)
        {
            var explorer = Windows.IsExplorer(foreground) ? foreground : IntPtr.Zero;
            lock (gate)
            {
                if (explorer == foregroundExplorer) return;
                var departing = explorer == IntPtr.Zero;
                var sampleWindow = departing ? foregroundExplorer : explorer;
                foregroundExplorer = explorer;
                latest = new Sample(sampleWindow, cache.BeginSample(), departing);
                wake.Set();
            }
        }

        private void Track()
        {
            Sample completedDeparture = null;
            using (wake)
            using (var shell = new ShellFolderReader())
            {
                while (!stopped)
                {
                    Sample sample;
                    lock (gate) sample = latest;
                    if (sample == null || sample == completedDeparture)
                    {
                        if (!stopped) wake.WaitOne();
                        continue;
                    }

                    string folder = null;
                    bool stable = false;
                    try
                    {
                        stable = shell.TryRead(sample.Window, out folder);
                        // A ShellWindows row can appear a little later than the foreground event.
                        if (!stable && sample.Departure && !stopped)
                        {
                            Thread.Sleep(30);
                            stable = shell.TryRead(sample.Window, out folder);
                        }
                    }
                    catch (Exception e)
                    {
                        shell.Reset();
                        Log.Error("explorer-read", e);
                    }

                    cache.Publish(sample.Revision, stable, folder);
                    if (sample.Departure)
                    {
                        shell.ReleaseActive();
                        completedDeparture = sample;
                    }
                    else if (!stopped) wake.WaitOne(200); // A single active Explorer window, not a desktop-wide scan.
                }
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (stopped) return;
                stopped = true;
                cache.Stop();
                wake.Set();
            }
            worker.Join(250);
        }
    }

    // PoC's IShellWindows -> IWebBrowser2 -> IShellBrowser path, all on the same STA.
    internal sealed class ShellFolderReader : IDisposable
    {
        [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IServiceProvider
        {
            [PreserveSig]
            int QueryService(ref Guid service, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellBrowser browser);
        }

        [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellBrowser
        {
            // IOleWindow.GetWindow is the first slot after IUnknown.
            [PreserveSig] int GetWindow(out IntPtr hwnd);
        }

        private const string TabClass = "ShellTabWindowClass";
        private object application, shellWindows, activeBrowser;
        private IntPtr activeTop, activeTab;
        private string activeUrl, activeFolder;

        public bool TryRead(IntPtr hwnd, out string path)
        {
            path = null;
            if (!Windows.IsExplorer(hwnd) || Windows.IsIconic(hwnd)) return true;
            var tab = Windows.FirstChild(hwnd, TabClass);
            if (activeBrowser == null || activeTop != hwnd || activeTab != tab)
            {
                ReleaseActive();
                activeBrowser = FindBrowser(hwnd, tab);
                activeTop = hwnd;
                activeTab = tab;
            }
            if (activeBrowser == null) return false;

            var url = (string)((dynamic)activeBrowser).LocationURL;
            if (Windows.FirstChild(hwnd, TabClass) != tab) return false;
            if (url != activeUrl)
            {
                activeUrl = url;
                activeFolder = FolderPath.FromFileUrl(url);
            }
            path = activeFolder;
            return true; // Virtual Shell folders may legitimately have no path.
        }

        private object FindBrowser(IntPtr hwnd, IntPtr tab)
        {
            if (application == null) application = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application", true));
            if (shellWindows == null) shellWindows = ((dynamic)application).Windows();
            int count = ((dynamic)shellWindows).Count;
            object found = null;
            try
            {
                for (var i = 0; i < count; i++)
                {
                    object row = null;
                    try
                    {
                        row = ((dynamic)shellWindows).Item(i);
                        if (row == null || new IntPtr(Convert.ToInt64(((dynamic)row).HWND)) != hwnd) continue;
                        if (tab != IntPtr.Zero && BrowserWindow(row) != tab) continue;
                        if (found != null) return null; // Ambiguous top-level fallback.
                        found = row;
                        row = null;
                        if (tab != IntPtr.Zero) break;
                    }
                    catch (COMException) { } // ShellWindows can change during enumeration.
                    catch (RuntimeBinderException) { } // The collection may contain other dispatch objects.
                    finally { Release(row); }
                }

                var result = found;
                found = null;
                return result;
            }
            finally { Release(found); }
        }

        private static IntPtr BrowserWindow(object browser)
        {
            var provider = browser as IServiceProvider;
            if (provider == null) return IntPtr.Zero;
            var iid = typeof(IShellBrowser).GUID;
            var sid = iid; // SID_SShellBrowser, as tested in the PoC on the Explorer STA.
            IShellBrowser shell = null;
            try
            {
                IntPtr hwnd;
                return provider.QueryService(ref sid, ref iid, out shell) == 0 && shell != null &&
                    shell.GetWindow(out hwnd) == 0 ? hwnd : IntPtr.Zero;
            }
            finally { Release(shell); }
        }

        private static void Release(object value)
        {
            if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
        }

        public void ReleaseActive()
        {
            Release(activeBrowser);
            activeBrowser = null;
            activeUrl = null;
            activeFolder = null;
        }

        public void Reset()
        {
            ReleaseActive();
            Release(shellWindows);
            Release(application);
            shellWindows = null;
            application = null;
        }

        public void Dispose() { Reset(); }
    }
}
