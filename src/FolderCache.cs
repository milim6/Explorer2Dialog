using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Explorer2Dialog
{
    internal sealed class FolderCache
    {
        private readonly object gate = new object();
        private long revision;
        private string folder;
        private bool pending, verified = true, stopped;

        public string Current { get { lock (gate) return folder; } }

        public long BeginSample()
        {
            lock (gate)
            {
                pending = true;
                return ++revision;
            }
        }

        public void Publish(long sample, bool stable, string path)
        {
            bool changed;
            lock (gate)
            {
                if (stopped || revision != sample) return;
                changed = stable && path != null && !FolderPath.Same(folder, path);
                if (changed) folder = path;
                verified = stable;
                pending = false;
                Monitor.PulseAll(gate);
            }
            if (changed) Log.Line("cache updated{0}", Log.PathDetail("folder", path));
        }

        public string TryRead(int timeoutMs, out string reason)
        {
            var timer = Stopwatch.StartNew();
            lock (gate)
            {
                while (pending && !stopped)
                {
                    var remaining = timeoutMs - (int)timer.ElapsedMilliseconds;
                    if (remaining <= 0) break;
                    Monitor.Wait(gate, remaining);
                }
                reason = stopped ? "stopped" : pending || !verified ? "explorer-unconfirmed" :
                    folder == null ? "no-folder" : null;
                return reason == null ? folder : null;
            }
        }

        public void Stop()
        {
            lock (gate)
            {
                stopped = true;
                Monitor.PulseAll(gate);
            }
        }
    }

    internal static class FolderPath
    {
        public static string FromFileUrl(string value)
        {
            Uri uri;
            if (string.IsNullOrEmpty(value) || !value.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ||
                !Uri.TryCreate(value, UriKind.Absolute, out uri) || !uri.IsFile)
                return null;
            return Normalize(uri.LocalPath);
        }

        public static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var drive = path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && IsSeparator(path[2]);
            var unc = path.StartsWith(@"\\", StringComparison.Ordinal) &&
                !path.StartsWith(@"\\?\", StringComparison.Ordinal) && !path.StartsWith(@"\\.\", StringComparison.Ordinal)
                && path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).Length >= 2;
            if (!drive && !unc) return null;
            try
            {
                var absolute = Path.GetFullPath(path);
                return drive && absolute.Length == 3 ? absolute : absolute.TrimEnd('\\');
            }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
            catch (PathTooLongException) { return null; }
        }

        public static bool Same(string first, string second)
        {
            return first != null && second != null &&
                string.Equals(first.TrimEnd('\\'), second.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSeparator(char ch) { return ch == '\\' || ch == '/'; }
    }
}
