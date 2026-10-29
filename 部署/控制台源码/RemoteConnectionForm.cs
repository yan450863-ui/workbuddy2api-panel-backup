using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace WorkBuddyLocalConsole
{
    internal sealed class RemoteConnectionForm : Form
    {
        private readonly string directory;
        private readonly Label status;
        private readonly TextBox address;
        private readonly Button start, stop, login, copy;
        private readonly System.Windows.Forms.Timer timer;
        private RemoteState state = new RemoteState();
        private ManagementForm loginForm;
        private bool busy;
        internal RemoteConnectionForm(string directory) {
            this.directory = directory;
            Text = "私人远程连接 · 内置 Tailscale";
            ClientSize = new Size(540, 480); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
            StartPosition = FormStartPosition.CenterParent; BackColor = Color.FromArgb(244,247,251);
            Font = new Font("Microsoft YaHei UI", 10f);
            Label title = new Label { Text = "在外面连接家里的反代", AutoSize = true, Font = new Font(Font.FontFamily,17f,FontStyle.Bold), Location = new Point(24,24) };
            status = new Label { Location = new Point(24,75), Size = new Size(492,64) };
            start = Button("开启远程",24,147); stop = Button("关闭远程",192,147); login = Button("登录授权",360,147);
            Label baseLabel = new Label { Text = "手机 API Base URL", AutoSize = true, Location = new Point(24,207) };
            address = new TextBox { ReadOnly = true, Location = new Point(24,235), Size = new Size(492,30) };
            copy = Button("复制地址",24,279);
            Button path = Button("复制 API 路径",192,279);
            Button copyLogin = Button("复制登录链接",360,279);
            Label hint = new Label { Text = "API 路径：/chat/completions\nAPI Key：使用主窗口的密钥；Response API 关闭。\n\n电脑无需安装客户端。手机需要安装 Tailscale，登录\n同一账号后开启连接。仅本人账号可访问 API，管理页\n不经此入口开放。电脑需保持联网、反代运行且不睡眠。", Location = new Point(24,337), Size = new Size(492,135), ForeColor = Color.FromArgb(73,87,103) };
            Controls.AddRange(new Control[]{title,status,baseLabel,address,hint});
            start.Click += delegate { Work(delegate { RemoteConnection.Start(directory); }); };
            stop.Click += delegate { Work(delegate { RemoteConnection.Stop(directory); }); };
            copy.Click += delegate { Copy(address.Text); };
            path.Click += delegate { Copy("/chat/completions"); };
            copyLogin.Click += delegate { Copy(state.LoginUrl); };
            login.Click += delegate {
                if (state.LoginUrl.Length == 0) return;
                if (loginForm == null || loginForm.IsDisposed) {
                    loginForm = new ManagementForm(state.LoginUrl, directory, true);
                    loginForm.Text = "Tailscale 私人网络授权"; loginForm.Show(this);
                } else loginForm.Activate();
            };
            timer = new System.Windows.Forms.Timer { Interval = 2000 };
            timer.Tick += delegate { RefreshState(); };
            Shown += delegate { RefreshState(); timer.Start(); };
            FormClosed += delegate { timer.Stop(); timer.Dispose(); };
        }
        private Button Button(string text, int x, int y) {
            Button b = new Button { Text = text, Location = new Point(x,y), Size = new Size(156,38), FlatStyle = FlatStyle.Flat, BackColor = Color.White };
            Controls.Add(b); return b;
        }
        private void Copy(string text) {
            if (text.Length == 0) return;
            try { Clipboard.SetText(text); status.Text = "已复制。"; } catch { status.Text = "剪贴板暂时不可用。"; }
        }
        private void RefreshState() {
            if (busy || IsDisposed) return;
            try {
                state = RemoteConnection.Load(directory);
                status.Text = state.Message; address.Text = state.Address;
                start.Enabled = !state.Running; stop.Enabled = state.Running;
                login.Enabled = state.LoginUrl.Length > 0; copy.Enabled = state.Address.Length > 0;
            } catch (Exception ex) { status.Text = ex.Message; }
        }
        private void Work(Action action) {
            if (busy) return;
            busy = true; start.Enabled = stop.Enabled = login.Enabled = false;
            status.Text = "正在处理…";
            ThreadPool.QueueUserWorkItem(delegate {
                string error = null;
                try { action(); } catch(Exception ex) { error = ex.Message; }
                if (IsDisposed || Disposing) return;
                try { BeginInvoke(new Action(delegate {
                    busy = false; RefreshState(); if (error != null) status.Text = error;
                })); } catch(InvalidOperationException) { }
            });
        }
    }
}
