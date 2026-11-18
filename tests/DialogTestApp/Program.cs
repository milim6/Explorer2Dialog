using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;

namespace Explorer2Dialog
{
    internal sealed class FixtureWindow : Form
    {
        private readonly string initial;
        private readonly string log;
        private readonly int pid = Process.GetCurrentProcess().Id;
        private readonly Label status = new Label { AutoSize = true, MaximumSize = new Size(520, 0) };
        private int saveCount;

        public FixtureWindow(string initial, string project, string label)
        {
            this.initial = initial;
            var logs = Path.Combine(project, "logs");
            Directory.CreateDirectory(logs);
            log = Path.Combine(logs, "dialog-testapp-" + pid + ".log");
            Text = "Explorer2Dialog Test" + (label == null ? "" : " - " + label);
            ClientSize = new Size(580, 265);
            StartPosition = FormStartPosition.CenterScreen;

            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                Padding = new Padding(12)
            };
            Controls.Add(panel);
            Add(panel, "&Open File", () => FileDialog(false, false));
            Add(panel, "&Save File", () => FileDialog(true, false));
            Add(panel, "&Legacy Open", () => FileDialog(false, true));
            Add(panel, "Legacy Sa&ve", () => FileDialog(true, true));
            Add(panel, "S&ettings", Settings);
            Add(panel, "&Message", () =>
            {
                Record("show message");
                MessageBox.Show(this, "Ordinary message for the negative control.", "MVP Message");
                Record("closed message");
            });
            panel.Controls.Add(status);
            using (var identity = WindowsIdentity.GetCurrent())
            {
                bool admin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                Record("pid=" + pid + " admin=" + admin + " initial=" + initial);
            }
        }

        private static void Add(FlowLayoutPanel panel, string text, Action action)
        {
            var button = new Button { Text = text, Width = 140 };
            button.Click += delegate { action(); };
            panel.Controls.Add(button);
        }

        private void FileDialog(bool save, bool legacy)
        {
            using (FileDialog dialog = save ? (FileDialog)new SaveFileDialog() : new OpenFileDialog())
            {
                dialog.AutoUpgradeEnabled = !legacy;
                dialog.InitialDirectory = initial;
                dialog.RestoreDirectory = true;
                dialog.Filter = "All files (*.*)|*.*";
                dialog.CheckPathExists = true;
                if (save)
                    dialog.FileName = "mvp-save-保留-" + pid + "-" + (++saveCount) + ".txt";
                var mode = (legacy ? "legacy-" : "modern-") + (save ? "save" : "open");
                Record("show " + mode + " initial='" + initial + "' filename='" + dialog.FileName + "'");
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    Record("cancel " + mode);
                    return;
                }
                try
                {
                    if (save)
                    {
                        // The test fixture only creates a new file.
                        using (var output = new StreamWriter(new FileStream(dialog.FileName, FileMode.CreateNew)))
                            output.WriteLine("Explorer2Dialog MVP");
                    }
                    else
                    {
                        using (var input = File.OpenRead(dialog.FileName))
                            input.ReadByte();
                    }
                    Record("OK " + mode + " file='" + dialog.FileName + "'");
                }
                catch (Exception e)
                {
                    Record("ERROR " + mode + " " + e.GetType().Name + ": " + e.Message);
                }
            }
        }

        private void Settings()
        {
            using (var settings = new Form { Text = "MVP Settings", ClientSize = new Size(280, 105),
                StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false })
            {
                var name = new TextBox { Left = 14, Top = 12, Width = 250, Text = "Settings value" };
                var ok = new Button { Left = 14, Top = 46, Text = "OK", DialogResult = DialogResult.OK };
                settings.Controls.AddRange(new Control[] { name, ok });
                settings.AcceptButton = ok;
                Record("show settings");
                settings.ShowDialog(this);
                Record("closed settings");
            }
        }

        private void Record(string message)
        {
            status.Text = message;
            File.AppendAllText(log, DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + Environment.NewLine, Encoding.UTF8);
        }

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            var project = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (project != null && !Directory.Exists(Path.Combine(project.FullName, "tests", "fixtures", "Initial")))
                project = project.Parent;
            if (project == null)
            {
                MessageBox.Show("Place DialogTestApp under the Explorer2Dialog directory.");
                return;
            }
            string initial = Path.Combine(project.FullName, "tests", "fixtures", "Initial");
            string label = null;
            for (int i = 0; i < args.Length; i += 2)
            {
                if (i + 1 < args.Length && args[i] == "--initial") initial = args[i + 1];
                else if (i + 1 < args.Length && args[i] == "--label") label = args[i + 1];
                else
                {
                    MessageBox.Show("Usage: DialogTestApp [--initial DIRECTORY] [--label TEXT]");
                    return;
                }
            }
            if (!Directory.Exists(initial))
            {
                MessageBox.Show("Initial directory is unavailable: " + initial);
                return;
            }
            Application.Run(new FixtureWindow(initial, project.FullName, label));
        }
    }
}
