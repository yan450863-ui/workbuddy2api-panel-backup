using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Reflection;

[assembly: AssemblyTitle("本地反代统一启动器")]
[assembly: AssemblyDescription("启动、停止并打开本机 WorkBuddy2API 管理页面")]
[assembly: AssemblyCompany("Local")]
[assembly: AssemblyProduct("本地反代统一启动器")]
[assembly: AssemblyVersion("1.5.2.0")]
[assembly: AssemblyFileVersion("1.5.2.0")]

namespace WorkBuddyLocalConsole
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool created;
            using (Mutex mutex = new Mutex(true, @"Local\WorkBuddy2ApiLocalConsole", out created))
            {
                if (!created)
                {
                    for (int attempt = 0; attempt < 10; attempt++)
                    {
                        try { using (EventWaitHandle signal = EventWaitHandle.OpenExisting(@"Local\WorkBuddy2ApiShowConsole")) signal.Set(); break; }
                        catch (WaitHandleCannotBeOpenedException) { Thread.Sleep(100); }
                    }
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try { FirstRun.Initialize(AppDomain.CurrentDomain.BaseDirectory); }
                catch (Exception ex) { MessageBox.Show("首次配置失败：" + ex.Message); return; }
                MainForm form = new MainForm();
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--panel") >= 0)
                    form.Shown += delegate { form.OpenManagementPage(); };
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--remote") >= 0)
                    form.Shown += delegate { form.OpenRemoteConnection(); };
                using (EventWaitHandle signal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\WorkBuddy2ApiShowConsole"))
                {
                    // A second launch restores the existing window, including a hidden one.
                    RegisteredWaitHandle registration = ThreadPool.RegisterWaitForSingleObject(signal, delegate(object state, bool timedOut) {
                        try { form.BeginInvoke(new Action(delegate {
                            form.Show(); form.WindowState = FormWindowState.Normal; form.BringToFront(); form.Activate();
                        })); } catch (InvalidOperationException) { }
                    }, null, Timeout.Infinite, false);
                    try { Application.Run(form); }
                    finally { registration.Unregister(null); }
                }
                GC.KeepAlive(mutex);
            }
        }
    }

    internal sealed class ServiceOption
    {
        public string Name;


        public override string ToString()
        {
            return Name;
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly string deployDirectory;
        private readonly ComboBox serviceSelector;
        private readonly Button startButton;
        private readonly Button stopButton;
        private readonly Button openButton;
        private readonly Button refreshButton;
        private readonly TextBox apiAddress;
        private readonly TextBox apiKey;
        private readonly TextBox managementAddress;
        private readonly Label messageLabel;
        private readonly Label modelLabel;
        private readonly Label actionLabel;
        private readonly Button showKeyButton;
        private readonly Button copyKeyButton;
        private ConnectionInfo connection;
        private int working;
        private ManagementForm managementForm;
        private RemoteConnectionForm remoteForm;
        private Panel lanDrawer;
        private CheckBox lanEnabled;
        private TextBox lanBaseUrl;
        private TextBox lanApiPath;
        private Label lanStatus;
        private Button lanApplyButton;
        private Button copyLanAddress;
        private readonly Func<bool, string> firewallConfigurator;

        public MainForm() : this(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) { }

        internal MainForm(string directory) : this(directory, null) { }

        internal MainForm(string directory, Func<bool, string> configureFirewall)
        {
            deployDirectory = directory;
            firewallConfigurator = configureFirewall ?? delegate(bool enabled) { return LanConnection.ConfigureFirewall(deployDirectory, enabled); };
            Text = "本地反代统一启动器";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;
            ClientSize = new Size(760, 545);
            BackColor = Color.FromArgb(244, 247, 251);
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);

            Label title = new Label
            {
                Text = "本地反代服务",
                AutoSize = true,
                Font = new Font(Font.FontFamily, 18f, FontStyle.Bold),
                ForeColor = Color.FromArgb(31, 43, 57),
                Location = new Point(28, 20)
            };
            Label serviceLabel = new Label
            {
                Text = "服务",
                AutoSize = true,
                ForeColor = Color.FromArgb(73, 87, 103),
                Location = new Point(30, 81)
            };
            serviceSelector = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font(Font.FontFamily, 10f, FontStyle.Regular),
                Location = new Point(78, 76),
                Size = new Size(230, 32)
            };
            serviceSelector.Items.Add(new ServiceOption
            {
                Name = "WorkBuddy2API"
            });


            refreshButton = MakeButton("刷新配置", Color.FromArgb(231, 236, 242), Color.FromArgb(41, 54, 69));
            refreshButton.SetBounds(624, 74, 108, 34);
            refreshButton.Click += delegate { ReloadConnection(); };
            AddLabel("API 基础地址（其他软件的 Base URL）", 28, 123);
            apiAddress = AddTextBox(28, 147, 594, false);
            Button copyAddress = AddSmallButton("复制地址", 634, 145, 98);
            copyAddress.Click += delegate { CopyValue(apiAddress.Text, "API 地址"); };

            AddLabel("API 密钥", 28, 189);
            apiKey = AddTextBox(28, 213, 502, true);
            showKeyButton = AddSmallButton("显示", 542, 211, 80);
            copyKeyButton = AddSmallButton("复制密钥", 634, 211, 98);
            showKeyButton.Click += delegate { ToggleSecret(apiKey, showKeyButton); };
            copyKeyButton.Click += delegate { CopyValue(apiKey.Text, "API 密钥"); };

            AddLabel("管理页面地址", 28, 255);
            managementAddress = AddTextBox(28, 279, 594, false);
            Button copyManagement = AddSmallButton("复制地址", 634, 277, 98);
            copyManagement.Click += delegate { CopyValue(managementAddress.Text, "管理页面地址"); };

            messageLabel = AddLabel(String.Empty, 28, 315);
            messageLabel.AutoSize = false;
            messageLabel.Size = new Size(704, 45);
            modelLabel = AddLabel(String.Empty, 28, 393);
            modelLabel.AutoSize = false;
            modelLabel.Size = new Size(704, 38);
            modelLabel.ForeColor = Color.FromArgb(85, 101, 116);

            startButton = MakeButton("启动服务", Color.FromArgb(29, 128, 90), Color.White);
            stopButton = MakeButton("停止服务", Color.FromArgb(231, 236, 242), Color.FromArgb(41, 54, 69));
            openButton = MakeButton("打开管理页面", Color.FromArgb(43, 101, 186), Color.White);
            startButton.Location = new Point(28, 439);
            stopButton.Location = new Point(268, 439);
            openButton.Location = new Point(508, 439);
            startButton.Size = new Size(224, 44);
            stopButton.Size = new Size(224, 44);
            openButton.Size = new Size(224, 44);

            startButton.Click += delegate { RunManager("Start"); };
            stopButton.Click += delegate { RunManager("Stop"); };
            openButton.Click += delegate { OpenManagementPage(); };

            Controls.Add(title);
            Controls.Add(serviceLabel);
            Controls.Add(serviceSelector);
            Controls.Add(refreshButton);
            Controls.Add(startButton);
            Controls.Add(stopButton);
            Controls.Add(openButton);
            actionLabel = AddLabel("读取本地配置；按需手动启动服务。", 28, 506);
            actionLabel.AutoSize = false;
            actionLabel.Size = new Size(704, 30);
            serviceSelector.SelectedIndexChanged += delegate { ReloadConnection(); };
            serviceSelector.SelectedIndex = 0;
            InitializeLanDrawer();
            Button remote = MakeButton("远程连接", Color.FromArgb(231, 236, 242), Color.FromArgb(41, 54, 69));
            remote.SetBounds(424, 24, 124, 36);
            Controls.Add(remote);
            remote.Click += delegate { OpenRemoteConnection(); };
        }

        internal void OpenRemoteConnection()
        {
            if (remoteForm == null || remoteForm.IsDisposed) {
                remoteForm = new RemoteConnectionForm(deployDirectory);
                remoteForm.Show(this);
            } else { remoteForm.WindowState = FormWindowState.Normal; remoteForm.Activate(); }
        }

        private void InitializeLanDrawer()
        {
            Button toggle = MakeButton("局域网连接 ›", Color.FromArgb(231, 236, 242), Color.FromArgb(41, 54, 69));
            toggle.SetBounds(560, 24, 172, 36);
            Controls.Add(toggle);
            lanDrawer = new Panel { BackColor = Color.White, Location = new Point(760, 0), Size = new Size(380, 545), Visible = false };
            Controls.Add(lanDrawer);
            Label heading = new Label { Text = "局域网连接", AutoSize = true, Font = new Font(Font.FontFamily, 16f, FontStyle.Bold), Location = new Point(22, 27) };
            Button close = MakeButton("收起", Color.FromArgb(231, 236, 242), Color.FromArgb(41, 54, 69));
            close.SetBounds(286, 24, 72, 34);
            lanEnabled = new CheckBox { Text = "开启局域网连接", AutoSize = true, Location = new Point(24, 88) };
            lanStatus = new Label { AutoSize = false, Size = new Size(332, 65), Location = new Point(24, 124), ForeColor = Color.FromArgb(73, 87, 103) };
            Label baseLabel = new Label { Text = "API Base URL", AutoSize = true, Location = new Point(24, 201) };
            lanBaseUrl = new TextBox { ReadOnly = true, BackColor = Color.FromArgb(244, 247, 251), Location = new Point(24, 229), Size = new Size(332, 30) };
            copyLanAddress = MakeButton("复制地址", Color.FromArgb(231, 236, 242), Color.FromArgb(41, 54, 69));
            copyLanAddress.SetBounds(250, 267, 106, 32);
            Label pathLabel = new Label { Text = "API 路径", AutoSize = true, Location = new Point(24, 315) };
            lanApiPath = new TextBox { ReadOnly = true, Text = LanConnection.ApiPath, BackColor = Color.FromArgb(244, 247, 251), Location = new Point(24, 343), Size = new Size(332, 30) };
            Button copyPath = MakeButton("复制路径", Color.FromArgb(231, 236, 242), Color.FromArgb(41, 54, 69));
            copyPath.SetBounds(250, 381, 106, 32);
            Label hint = new Label { Text = "手机与电脑连接同一路由器。\nAPI Key 使用主窗口的“复制密钥”。\n手机选择 OpenAI，Response API 关闭。", AutoSize = false, Location = new Point(24, 423), Size = new Size(332, 64), ForeColor = Color.FromArgb(85, 101, 116) };
            lanApplyButton = MakeButton("应用设置", Color.FromArgb(43, 101, 186), Color.White);
            lanApplyButton.SetBounds(24, 493, 332, 36);
            lanDrawer.Controls.AddRange(new Control[] { heading, close, lanEnabled, lanStatus, baseLabel, lanBaseUrl, copyLanAddress, pathLabel, lanApiPath, copyPath, hint, lanApplyButton });
            toggle.Click += delegate { ToggleLanDrawer(); };
            close.Click += delegate { ToggleLanDrawer(); };
            copyLanAddress.Click += delegate { CopyValue(lanBaseUrl.Text, "局域网 API 地址"); };
            copyPath.Click += delegate { CopyValue(lanApiPath.Text, "API 路径"); };
            lanApplyButton.Click += delegate { ApplyLanSettings(); };
            lanEnabled.CheckedChanged += delegate { lanStatus.Text = "点击“应用设置”保存。首次配置防火墙时，Windows 会请求管理员确认。"; };
            ReloadLan();
        }

        private void ToggleLanDrawer()
        {
            bool show = !lanDrawer.Visible;
            if (show) ReloadLan();
            ClientSize = new Size(show ? 1140 : 760, 545);
            lanDrawer.Visible = show;
        }

        private void ReloadLan()
        {
            if (lanDrawer == null) return;
            LanConnection info = LanConnection.Load(deployDirectory);
            lanEnabled.Checked = info.Enabled;
            lanBaseUrl.Text = info.BaseUrl;
            copyLanAddress.Enabled = info.BaseUrl.Length > 0;
            lanStatus.Text = info.Address.Length == 0 ? "未找到局域网地址，请连接 Wi-Fi 或网线后刷新。"
                : !info.Enabled ? "当前仅允许电脑本机连接。勾选后点击“应用设置”即可启用。"
                : info.FirewallReady ? "已开启，手机可使用下方地址连接。重启电脑后仍生效。"
                : "局域网监听已开启，防火墙尚待配置。点击“应用设置”完成放行。";
        }

        private void ApplyLanSettings()
        {
            if (Interlocked.CompareExchange(ref working, 1, 0) != 0) return;
            bool enabled = lanEnabled.Checked;
            ServiceOption service = SelectedService();
            SetButtonsEnabled(false);
            lanStatus.Text = "正在应用设置，请在 Windows 提示中确认管理员授权…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error = null;
                try
                {
                    error = firewallConfigurator(enabled);
                    if (error == null)
                    {
                        LanConnection before = LanConnection.Load(deployDirectory);
                        if (before.Enabled != enabled)
                        {
                            bool running = LanConnection.IsServiceRunning(deployDirectory);
                            if (running)
                            {
                                ManagerResult stop = ExecuteManager(service, "Stop");
                                if (stop.ExitCode != 0) throw new InvalidOperationException("停止网关失败，配置未修改。" );
                            }
                            string original = null;
                            try
                            {
                                original = LanConnection.SetListen(deployDirectory, enabled);
                                if (running && ExecuteManager(service, "Start").ExitCode != 0) throw new InvalidOperationException("重启网关失败，已尝试恢复原配置。" );
                            }
                            catch
                            {
                                if (original != null) LanConnection.WriteConfig(Path.Combine(deployDirectory, "config.json"), original);
                                if (running) ExecuteManager(service, "Start");
                                throw;
                            }
                        }
                    }
                }
                catch (Exception ex) { error = ex.Message; }
                finally { Interlocked.Exchange(ref working, 0); }
                if (IsDisposed || Disposing) return;
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        if (IsDisposed || Disposing) return;
                        SetButtonsEnabled(true);
                        ReloadConnection();
                        if (error != null) lanStatus.Text = error;
                        else actionLabel.Text = enabled ? "局域网连接设置已保存。" : "已关闭局域网连接，仅保留电脑本机访问。";
                    }));
                }
                catch (InvalidOperationException) { }
            });
        }

        private Label AddLabel(string text, int x, int y)
        {
            Label label = new Label { Text = text, AutoSize = true, ForeColor = Color.FromArgb(73, 87, 103), Location = new Point(x, y) };
            Controls.Add(label);
            return label;
        }

        private TextBox AddTextBox(int x, int y, int width, bool secret)
        {
            TextBox textBox = new TextBox { ReadOnly = true, UseSystemPasswordChar = secret, Location = new Point(x, y), Size = new Size(width, 30), BackColor = Color.White };
            Controls.Add(textBox);
            return textBox;
        }

        private Button AddSmallButton(string text, int x, int y, int width)
        {
            Button button = MakeButton(text, Color.FromArgb(231, 236, 242), Color.FromArgb(41, 54, 69));
            button.Font = new Font(Font.FontFamily, 9f);
            button.SetBounds(x, y, width, 32);
            Controls.Add(button);
            return button;
        }

        private void ReloadConnection()
        {
            ServiceOption service = SelectedService();
            if (service == null) return;
            connection = ConnectionInfo.Load(deployDirectory);
            apiAddress.Text = connection.ApiBaseUrl;
            apiKey.UseSystemPasswordChar = true;
            apiKey.Text = connection.ApiKey;
            showKeyButton.Text = "显示";
            showKeyButton.Enabled = copyKeyButton.Enabled = connection.ApiKey.Length > 0;
            managementAddress.Text = connection.ManagementUrl;
            messageLabel.Text = connection.Message;
            messageLabel.Location = new Point(28, 325);
            modelLabel.Text = connection.ModelHint;
            actionLabel.Text = "配置已读取。修改管理页配置后，可点“刷新配置”。";
            ReloadLan();
        }

        private void ToggleSecret(TextBox textBox, Button button)
        {
            textBox.UseSystemPasswordChar = !textBox.UseSystemPasswordChar;
            button.Text = textBox.UseSystemPasswordChar ? "显示" : "隐藏";
        }

        private void CopyValue(string value, string name)
        {
            if (String.IsNullOrEmpty(value)) { actionLabel.Text = name + "尚未配置，请先启动服务或检查配置。"; return; }
            try { Clipboard.SetText(value); actionLabel.Text = name + "已复制。"; }
            catch { actionLabel.Text = "剪贴板暂时不可用，请稍后再试。"; }
        }

        private Button MakeButton(string text, Color background, Color foreground)
        {
            Button button = new Button
            {
                Text = text,
                BackColor = background,
                ForeColor = foreground,
                FlatStyle = FlatStyle.Flat,
                Font = new Font(Font.FontFamily, 10f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(background, 0.08f);
            button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(background, 0.08f);
            return button;
        }

        private ServiceOption SelectedService()
        {
            return serviceSelector.SelectedItem as ServiceOption;
        }

        private void RunManager(string action)
        {
            ServiceOption service = SelectedService();
            if (service == null || Interlocked.CompareExchange(ref working, 1, 0) != 0) return;

            SetButtonsEnabled(false);
            actionLabel.Text = action == "Start" ? "正在启动服务…" : "正在停止服务…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                ManagerResult result;
                try { result = ExecuteManager(service, action); }
                catch (Exception ex) { result = new ManagerResult { ExitCode = 1, Error = ex.Message }; }
                finally { Interlocked.Exchange(ref working, 0); }
                if (IsDisposed || Disposing) return;
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        if (IsDisposed || Disposing) return;
                        SetButtonsEnabled(true);
                        ReloadConnection();
                        if (result.ExitCode != 0)
                        {
                            string detail = (result.Error + Environment.NewLine + result.Output).Trim();
                            if (detail.Length == 0) detail = "管理脚本返回代码：" + result.ExitCode;
                            if (detail.Length > 1800) detail = detail.Substring(0, 1800) + "…";
                            actionLabel.Text = "服务操作失败，可重试。";
                            MessageBox.Show(this, detail, service.Name + " 服务操作失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                        else actionLabel.Text = service.Name + (action == "Start" ? " 启动命令已完成。" : " 停止命令已完成。");
                    }));
                }
                catch (InvalidOperationException) { }
            });
        }

        private ManagerResult ExecuteManager(ServiceOption service, string action)
        {
            string workingDirectory = deployDirectory;
            string script = Path.Combine(deployDirectory, "管理工具", "管理服务.ps1");
            string scriptArguments = " -Action " + action;
            if (!File.Exists(script)) return new ManagerResult { ExitCode = 2, Error = "管理脚本缺失：" + script };

            string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            if (!File.Exists(powershell)) powershell = "powershell.exe";
            ProcessStartInfo start = new ProcessStartInfo
            {
                FileName = powershell,
                Arguments = "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + script + "\"" + scriptArguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            StringBuilder output = new StringBuilder();
            StringBuilder error = new StringBuilder();
            object sync = new object();
            using (Process process = new Process { StartInfo = start })
            {
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) lock (sync) output.AppendLine(e.Data); };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) lock (sync) error.AppendLine(e.Data); };
                try
                {
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    if (!process.WaitForExit(45000))
                    {
                        try { process.Kill(); } catch { }
                        return new ManagerResult { ExitCode = 124, Error = "服务操作超过 45 秒。" };
                    }
                    // 子进程可能继承管道句柄。脚本已退出后，不能无限等待输出 EOF。
                    // 异步输出仅作错误诊断；先结束读取再释放进程，不等待网关退出。
                    try { process.CancelOutputRead(); } catch (InvalidOperationException) { }
                    try { process.CancelErrorRead(); } catch (InvalidOperationException) { }
                    lock (sync) return new ManagerResult { ExitCode = process.ExitCode, Output = output.ToString(), Error = error.ToString() };
                }
                catch (Exception ex)
                {
                    return new ManagerResult { ExitCode = 1, Error = ex.Message };
                }
            }
        }

        internal void OpenManagementPage()
        {
            try
            {
                ReloadConnection();
                if (managementForm == null || managementForm.IsDisposed)
                {
                    managementForm = new ManagementForm(connection.ManagementUrl, deployDirectory);
                    managementForm.Show(this);
                }
                else
                {
                    if (managementForm.WindowState == FormWindowState.Minimized) managementForm.WindowState = FormWindowState.Normal;
                    managementForm.Activate();
                }
                actionLabel.Text = "已打开应用内管理窗口。";
            }
            catch (Exception ex)
            {
                actionLabel.Text = "管理窗口打开失败。";
                messageLabel.Text = ex.Message;
            }
        }

        private void SetButtonsEnabled(bool enabled)
        {
            startButton.Enabled = enabled;
            stopButton.Enabled = enabled;
            serviceSelector.Enabled = enabled;
            refreshButton.Enabled = true;
            if (lanApplyButton != null) lanApplyButton.Enabled = enabled;
            if (lanEnabled != null) lanEnabled.Enabled = enabled;
        }

        private sealed class ManagerResult
        {
            public int ExitCode;
            public string Output;
            public string Error;
        }
    }
}


