using System;

namespace Explorer2Dialog
{
    internal static class CoreChecks
    {
        private static void Main()
        {
            CheckLifecycle();
            CheckFolderCache();
            Console.WriteLine("PASS: dialog lifetime, cache generation and file URL decoding.");
        }

        private static void CheckLifecycle()
        {
            var life = new DialogLifecycle();
            var window = new IntPtr(123);
            uint time = Windows.NowTick;
            var first = life.AtForeground(window, 1, 2, time);
            var open = new DialogProbe(first, time);
            const string folderA = @"C:\Fixture A";
            const string folderB = @"C:\Fixture B";
            Check(first.TryUseFolder(folderA), "first foreground can select the cached folder");

            var sameWindow = life.AtForeground(window, 1, 2, unchecked(time + 1));
            var resumed = new DialogProbe(sameWindow, Windows.NowTick);
            Check(sameWindow == first && open.Visit == 1 && resumed.Visit == 2, "refocus shares the visible lifetime");
            Check(!sameWindow.TryUseFolder(@"c:\fixture a\"), "unchanged Explorer directory preserves manual navigation");
            Check(sameWindow.TryUseFolder(folderB) && sameWindow.TryUseFolder(folderA), "different Explorer directories can rearm navigation");

            Check(life.Close(window, unchecked(time + 1)), "closing discards navigation state");
            var second = life.AtForeground(window, 1, 2, unchecked(time + 2));
            Check(first.Closed && second.Generation > first.Generation && second.TryUseFolder(folderA), "reused HWND starts fresh");
            Check(!life.Close(window, unchecked(time + 1)) && !second.Closed, "stale close is ignored");
            life.Clear();
            Check(second.Closed, "clearing lifecycle invalidates remaining instances");
        }

        private static void CheckFolderCache()
        {
            const string folderA = @"C:\Fixture A";
            const string folderB = @"C:\Fixture B 中文 + #100%";
            var cache = new FolderCache();
            string reason;
            Check(cache.TryRead(0, out reason) == null && reason == "no-folder", "empty startup cache");

            var stale = cache.BeginSample();
            var current = cache.BeginSample();
            cache.Publish(stale, true, folderB);
            Check(cache.TryRead(0, out reason) == null && reason == "explorer-unconfirmed", "outdated sample cannot publish");
            cache.Publish(current, true, folderA);
            cache.Publish(cache.BeginSample(), true, null);
            Check(cache.TryRead(0, out reason) == folderA && reason == null, "retain last valid folder");
            cache.Publish(cache.BeginSample(), false, null);
            Check(cache.Current == folderA && cache.TryRead(0, out reason) == null &&
                reason == "explorer-unconfirmed", "unconfirmed sampling suppresses navigation");
            cache.Publish(cache.BeginSample(), true, folderB);
            Check(cache.TryRead(0, out reason) == folderB && reason == null, "accept current sample");
            cache.Stop();
            Check(cache.TryRead(0, out reason) == null && reason == "stopped", "shutdown suppresses cache reads");

            var url = "file:///C:/Fixture%20B%20%E4%B8%AD%E6%96%87%20%2B%20%23100%25";
            Check(FolderPath.FromFileUrl(url) == folderB, "decode Chinese, space, plus, hash and percent exactly once");
            Check(FolderPath.Normalize(@"\\?\C:\Fixture") == null &&
                FolderPath.FromFileUrl("shell:Downloads") == null, "ignore device and virtual paths");
        }

        private static void Check(bool passed, string name)
        {
            if (!passed) throw new InvalidOperationException(name);
        }
    }
}
