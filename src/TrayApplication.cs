using System.Drawing;
using System.Windows.Forms;

namespace Explorer2Dialog
{
    internal sealed class TrayApplication : ApplicationContext
    {
        private readonly NotifyIcon icon;
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private readonly Timer deadline = new Timer();

        public TrayApplication(FolderCache cache, DialogDetector detector, int seconds)
        {
            var toggle = new ToolStripMenuItem("自动跳转（开启）") { Checked = true, CheckOnClick = true };
            toggle.CheckedChanged += delegate
            {
                detector.Enabled = toggle.Checked;
                toggle.Text = toggle.Checked ? "自动跳转（开启）" : "自动跳转（暂停）";
                icon.Text = toggle.Checked ? "Explorer2Dialog" : "Explorer2Dialog（已暂停）";
            };
            menu.Items.Add(toggle);
            menu.Items.Add("查看当前缓存目录", null, (sender, args) =>
                MessageBox.Show(cache.Current ?? "尚未记录 Explorer 文件夹。", "Explorer2Dialog"));
            menu.Items.Add("退出程序", null, (sender, args) => ExitThread());
            icon = new NotifyIcon { Icon = SystemIcons.Application, Text = "Explorer2Dialog", ContextMenuStrip = menu, Visible = true };
            if (seconds > 0)
            {
                deadline.Interval = seconds * 1000;
                deadline.Tick += (sender, args) => ExitThread();
                deadline.Start();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                deadline.Dispose();
                icon.Visible = false;
                icon.Dispose();
                menu.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
