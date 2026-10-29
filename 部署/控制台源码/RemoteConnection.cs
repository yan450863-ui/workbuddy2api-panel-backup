using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Web.Script.Serialization;

namespace WorkBuddyLocalConsole
{
    internal sealed class RemoteState
    {
        public bool Running;
        public string Address = "", LoginUrl = "", Message = "远程连接已关闭。点击开启后，首次需要登录 Tailscale。";
    }

    internal static class RemoteConnection
    {
        internal static string Executable(string directory) { return Path.Combine(directory, "管理工具", "WorkBuddyRemote.exe"); }
        internal static string StateDirectory(string directory) { return Path.Combine(directory, "data", "private-remote"); }
        internal static Process FindProcess(string directory) {
            foreach (Process process in Process.GetProcessesByName("WorkBuddyRemote")) {
                try { if (String.Equals(process.MainModule.FileName, Executable(directory), StringComparison.OrdinalIgnoreCase)) return process; } catch { }
                process.Dispose();
            }
            return null;
        }
        internal static RemoteState Load(string directory) {
            RemoteState state = new RemoteState();
            using (Process process = FindProcess(directory)) {
                if (process == null) {
                    string previous = Path.Combine(StateDirectory(directory), "status.json");
                    try {
                        if (File.Exists(previous) && File.ReadAllText(previous).Contains("\"phase\":\"error\"")) state.Message = "远程组件启动失败，请检查网络后重新开启。";
                    } catch { }
                    return state;
                }
                state.Running = true;
                state.Message = "正在连接私人网络…";
                string path = Path.Combine(StateDirectory(directory), "status.json");
                if (!File.Exists(path)) return state;
                try {
                    var data = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path)) as Dictionary<string, object>;
                    if (Convert.ToInt32(data["pid"]) != process.Id) return state;
                    long updated = Convert.ToInt64(data["updated"]);
                    if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - updated > 15) { state.Message = "组件响应较慢，稍后刷新或关闭后重试。"; return state; }
                    string phase = Convert.ToString(data["phase"]);
                    if (phase == "Running") {
                        Uri address;
                        if (Uri.TryCreate(Convert.ToString(data["address"]), UriKind.Absolute, out address) && address.Scheme == "http" && address.Port == 17863)
                            state.Address = address.AbsoluteUri.TrimEnd('/');
                        state.Message = "远程连接已开启。手机开启 Tailscale，并登录同一账号。";
                    } else {
                        Uri login;
                        if (Uri.TryCreate(Convert.ToString(data["loginUrl"]), UriKind.Absolute, out login) && login.Scheme == "https" && login.Host == "login.tailscale.com") state.LoginUrl = login.AbsoluteUri;
                        state.Message = state.LoginUrl.Length > 0 ? "请点击“登录授权”，将内置节点加入你的私人网络。" : "正在连接私人网络（" + phase + "）…";
                    }
                } catch { state.Message = "正在更新连接状态…"; }
            }
            return state;
        }
        internal static void Start(string directory) {
            using (Process running = FindProcess(directory)) if (running != null) return;
            if (!File.Exists(Executable(directory))) throw new InvalidOperationException("内置远程组件缺失，请重新构建或恢复程序文件。");
            ConnectionInfo info = ConnectionInfo.Load(directory);
            if (String.IsNullOrWhiteSpace(info.ApiKey)) throw new InvalidOperationException("请先为反代设置 API 密钥。");
            string state = StateDirectory(directory);
            Directory.CreateDirectory(state);
            string stop = Path.Combine(state, "stop");
            if (File.Exists(stop)) File.Delete(stop);
            var start = new ProcessStartInfo(Executable(directory)) {
                Arguments = "--state-dir \"" + state + "\" --config \"" + Path.Combine(directory, "config.json") + "\" --port " + new Uri(info.ApiBaseUrl).Port,
                WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
            };
            // Never inherit unrelated machine authentication or forced-login settings.
            foreach (string name in new string[] { "TS_AUTHKEY", "TS_AUTH_KEY", "TS_CLIENT_SECRET", "TSNET_FORCE_LOGIN" }) start.EnvironmentVariables.Remove(name);
            using (Process process = Process.Start(start)) { }
        }
        internal static void Stop(string directory) {
            using (Process process = FindProcess(directory)) {
                if (process == null) return;
                File.WriteAllText(Path.Combine(StateDirectory(directory), "stop"), "stop");
                if (!process.WaitForExit(10000)) { process.Kill(); process.WaitForExit(3000); }
            }
        }
    }
}
