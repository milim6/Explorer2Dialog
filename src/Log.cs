using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Explorer2Dialog
{
    internal static class Log
    {
        private const long FileLimit = 2L * 1024 * 1024;
        private static readonly object Gate = new object();
        private static readonly Encoding Utf8 = new UTF8Encoding(false);
        private static string file;
        private static uint pid;

        public static bool Detailed { get; private set; }

        public static void Initialize(bool detailed)
        {
            Detailed = detailed;
            pid = ProcessIntegrity.GetCurrentProcessId();
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Explorer2Dialog", "Logs");
            file = Path.Combine(folder, detailed ? "diagnostics.log" : "events.log");
            try { Directory.CreateDirectory(folder); }
            catch (IOException e) { Trace.WriteLine(e.GetType().Name); }
            catch (UnauthorizedAccessException e) { Trace.WriteLine(e.GetType().Name); }
        }

        public static string PathDetail(string key, string path)
        {
            return Detailed && path != null ? " " + key + "='" + path.Replace("'", "''") + "'" : "";
        }

        public static void Error(string stage, Exception e)
        {
            Line("error phase={0} type={1} hr=0x{2:X8}", stage, e.GetType().Name, e.HResult);
        }

        public static void Line(string format, params object[] args)
        {
            if (file == null) return;
            string line = string.Format(CultureInfo.InvariantCulture, "{0:O} app={1} {2}{3}",
                DateTimeOffset.Now, pid, string.Format(CultureInfo.InvariantCulture, format, args), Environment.NewLine);
            lock (Gate)
            {
                try
                {
                    var info = new FileInfo(file);
                    if (info.Exists && info.Length >= FileLimit)
                    {
                        var previous = Path.ChangeExtension(file, ".previous.log");
                        File.Delete(previous);
                        File.Move(file, previous);
                    }
                    File.AppendAllText(file, line, Utf8);
                }
                catch (IOException e) { Trace.WriteLine(e.GetType().Name); }
                catch (UnauthorizedAccessException e) { Trace.WriteLine(e.GetType().Name); }
            }
        }
    }
}
