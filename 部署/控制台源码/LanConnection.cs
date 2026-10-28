using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace WorkBuddyLocalConsole
{
    internal sealed class LanConnection
    {
        public bool Enabled;
        public bool FirewallReady;
        public string Address = String.Empty;
        public string BaseUrl = String.Empty;
        public int Port = 7863;
        public const string ApiPath = "/chat/completions";

        internal static LanConnection Load(string directory)
        {
            LanConnection info = new LanConnection();
            try
            {
                var config = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(directory, "config.json"))) as Dictionary<string, object>;
                object value;
                string listen = config != null && config.TryGetValue("listen", out value) ? Convert.ToString(value) : String.Empty;
                Uri uri;
                if (Uri.TryCreate("http://" + listen, UriKind.Absolute, out uri))
                {
                    info.Port = uri.Port;
                    info.Enabled = uri.Host != "127.0.0.1" && uri.Host != "localhost" && uri.Host != "[::1]";
                }
                info.Address = FindAddress();
                if (info.Address.Length > 0) info.BaseUrl = "http://" + info.Address + ":" + info.Port + "/v1";
                info.FirewallReady = ReadFirewall(directory, info.Address, info.Port);
            }
            catch { }
            return info;
        }

        private static string FindAddress()
        {
            foreach (NetworkInterface network in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (network.OperationalStatus != OperationalStatus.Up || network.NetworkInterfaceType == NetworkInterfaceType.Loopback || network.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                IPInterfaceProperties properties = network.GetIPProperties();
                bool hasGateway = false;
                foreach (GatewayIPAddressInformation gateway in properties.GatewayAddresses)
                    if (gateway.Address.AddressFamily == AddressFamily.InterNetwork && !gateway.Address.Equals(IPAddress.Any)) hasGateway = true;
                if (!hasGateway) continue;
                foreach (UnicastIPAddressInformation unicast in properties.UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    byte[] bytes = unicast.Address.GetAddressBytes();
                    if (bytes[0] == 10 || (bytes[0] == 192 && bytes[1] == 168) || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)) return unicast.Address.ToString();
                }
            }
            return String.Empty;
        }

        private static bool ReadFirewall(string directory, string address, int port)
        {
            object policy = null, rules = null, rule = null;
            try
            {
                policy = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2"));
                rules = policy.GetType().InvokeMember("Rules", BindingFlags.GetProperty, null, policy, null);
                rule = rules.GetType().InvokeMember("Item", BindingFlags.InvokeMethod, null, rules, new object[] { "WorkBuddy2API-LAN-7863" });
                Func<string, object> get = delegate(string name) { return rule.GetType().InvokeMember(name, BindingFlags.GetProperty, null, rule, null); };
                return (bool)get("Enabled") && (int)get("Action") == 1 && (int)get("Direction") == 1
                    && String.Equals(Convert.ToString(get("ApplicationName")), Path.Combine(directory, "wb2api.exe"), StringComparison.OrdinalIgnoreCase)
                    && Convert.ToString(get("LocalPorts")) == port.ToString()
                    && Convert.ToString(get("LocalAddresses")).Split('/')[0] == address
                    && Convert.ToString(get("RemoteAddresses")).IndexOf("LocalSubnet", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
            finally
            {
                foreach (object item in new object[] { rule, rules, policy }) if (item != null && Marshal.IsComObject(item)) Marshal.FinalReleaseComObject(item);
            }
        }

        internal static bool IsServiceRunning(string directory)
        {
            foreach (Process process in Process.GetProcessesByName("wb2api"))
            {
                using (process)
                {
                    try { if (String.Equals(process.MainModule.FileName, Path.Combine(directory, "wb2api.exe"), StringComparison.OrdinalIgnoreCase)) return true; }
                    catch { }
                }
            }
            return false;
        }

        internal static string ConfigureFirewall(string directory, bool enabled)
        {
            string script = Path.Combine(directory, "管理工具", "配置手机连接.ps1");
            if (!File.Exists(script)) return "局域网配置脚本缺失。";
            string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            ProcessStartInfo start = new ProcessStartInfo(powershell)
            {
                Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" -Action " + (enabled ? "Enable" : "Disable") + " -Quiet",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) start.Verb = "runas";
            try
            {
                using (Process process = Process.Start(start))
                {
                    if (!process.WaitForExit(120000)) return "Windows 配置尚未结束，请稍后刷新查看状态。";
                    if (process.ExitCode == 0) return null;
                }
                string reportPath = Path.Combine(directory, "data", "lan-settings-result.json");
                if (File.Exists(reportPath))
                {
                    var report = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(reportPath)) as Dictionary<string, object>;
                    object error;
                    if (report != null && report.TryGetValue("error", out error)) return Convert.ToString(error);
                }
                return "防火墙设置失败，请重试。";
            }
            catch (System.ComponentModel.Win32Exception ex) { return ex.NativeErrorCode == 1223 ? "已取消 Windows 管理员授权，设置未更改。" : ex.Message; }
            catch (Exception ex) { return ex.Message; }
        }

        internal static string SetListen(string directory, bool enabled)
        {
            string path = Path.Combine(directory, "config.json");
            string original = File.ReadAllText(path);
            var config = new JavaScriptSerializer().DeserializeObject(original) as Dictionary<string, object>;
            object key;
            if (config == null) throw new InvalidOperationException("无法读取网关配置。" );
            if (enabled && (!config.TryGetValue("api_key", out key) || String.IsNullOrWhiteSpace(Convert.ToString(key)))) throw new InvalidOperationException("请先设置 API 密钥，再开启局域网连接。");
            var info = Load(directory);
            Regex pattern = new Regex("\"listen\"\\s*:\\s*\"[^\"]*\"");
            if (pattern.Matches(original).Count != 1) throw new InvalidOperationException("配置中的 listen 字段不明确，请检查配置。" );
            string updated = pattern.Replace(original, "\"listen\": \"" + (enabled ? "0.0.0.0" : "127.0.0.1") + ":" + info.Port + "\"", 1);
            WriteConfig(path, updated);
            return original;
        }

        internal static void WriteConfig(string path, string text)
        {
            string temporary = path + ".lan-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, text, new UTF8Encoding(false));
                File.Replace(temporary, path, null);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
