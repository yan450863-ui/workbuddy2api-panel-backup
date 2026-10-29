using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace WorkBuddyLocalConsole
{
    internal sealed class ManagementForm : Form
    {
        private readonly WebView2 view;
        private readonly Label status;
        private readonly string managementUrl;
        private readonly string profileDirectory;
        internal bool PageLoaded;
        internal string Failure = String.Empty;

        internal ManagementForm(string url, string deployDirectory) : this(url, deployDirectory, false) { }

        internal ManagementForm(string url, string deployDirectory, bool authorization)
        {
            Uri address;
            if (!Uri.TryCreate(url, UriKind.Absolute, out address) || (address.Scheme != "http" && address.Scheme != "https"))
                throw new ArgumentException("管理页面地址必须为 HTTP 或 HTTPS。");
            managementUrl = address.AbsoluteUri;
            profileDirectory = Path.Combine(deployDirectory, "data", authorization ? "remote-login-webview" : "management-webview");
            Text = "WorkBuddy 管理页面";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1180, 820);
            MinimumSize = new Size(800, 600);
            Font = new Font("Microsoft YaHei UI", 9f);
            FlowLayoutPanel toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(6), WrapContents = false };
            Button home = new Button { Text = authorization ? "返回登录页" : "返回管理页", AutoSize = true };
            Button back = new Button { Text = "后退", AutoSize = true };
            Button reload = new Button { Text = "重新加载", AutoSize = true };
            status = new Label { Text = "正在初始化管理窗口…", AutoSize = true, Padding = new Padding(10, 5, 0, 0) };
            toolbar.Controls.Add(home);
            toolbar.Controls.Add(back);
            toolbar.Controls.Add(reload);
            toolbar.Controls.Add(status);
            view = new WebView2 { Dock = DockStyle.Fill };
            Controls.Add(view);
            Controls.Add(toolbar);
            home.Click += delegate { if (view.CoreWebView2 != null) view.CoreWebView2.Navigate(managementUrl); };
            back.Click += delegate { if (view.CoreWebView2 != null && view.CoreWebView2.CanGoBack) view.CoreWebView2.GoBack(); };
            reload.Click += delegate { if (view.CoreWebView2 != null) view.CoreWebView2.Reload(); };
            Shown += delegate { InitializeView(); };
        }

        private async void InitializeView()
        {
            try
            {
                var environment = await CoreWebView2Environment.CreateAsync(null, profileDirectory);
                if (IsDisposed || Disposing) return;
                await view.EnsureCoreWebView2Async(environment);
                if (IsDisposed || Disposing) return;
                view.CoreWebView2.NavigationStarting += delegate { PageLoaded = false; Failure = String.Empty; status.Text = "正在加载…"; };
                view.CoreWebView2.NavigationCompleted += delegate(object sender, CoreWebView2NavigationCompletedEventArgs e)
                {
                    PageLoaded = e.IsSuccess;
                    Failure = e.IsSuccess ? String.Empty : e.WebErrorStatus.ToString();
                    status.Text = e.IsSuccess ? "管理页面已加载" : "加载失败，请检查服务是否启动：" + Failure;
                };
                view.CoreWebView2.NewWindowRequested += delegate(object sender, CoreWebView2NewWindowRequestedEventArgs e)
                {
                    e.Handled = true;
                    Uri next;
                    if (Uri.TryCreate(e.Uri, UriKind.Absolute, out next) && (next.Scheme == "http" || next.Scheme == "https"))
                        view.CoreWebView2.Navigate(next.AbsoluteUri);
                };
                view.CoreWebView2.Navigate(managementUrl);
            }
            catch (Exception ex)
            {
                if (IsDisposed || Disposing) return;
                Failure = ex.Message;
                status.Text = "无法初始化管理窗口：" + ex.Message;
            }
        }
    }
}
