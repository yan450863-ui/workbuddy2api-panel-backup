using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace WorkBuddyLocalConsole
{
    internal sealed class ConnectionInfo
    {
        public string ApiBaseUrl;
        public string ManagementUrl;
        public string ApiKey = String.Empty;
        public string ModelHint;
        public string Message;
        public bool ManagementEnabled = true;

        public static ConnectionInfo Load(string deployDirectory)
        {
            ConnectionInfo info = new ConnectionInfo
            {
                ApiBaseUrl = "http://127.0.0.1:7863/v1",
                ManagementUrl = "http://127.0.0.1:7863/panel/",
                ModelHint = "模型：从管理页面或 /v1/models 查看。"
            };
            string path = Path.Combine(deployDirectory, "config.json");
            if (!File.Exists(path))
            {
                info.Message = "未找到部署/config.json，请先检查部署文件。";
                return info;
            }

            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                Dictionary<string, object> config = serializer.DeserializeObject(File.ReadAllText(path)) as Dictionary<string, object>;
                if (config == null) throw new FormatException();
                string address = Address(GetString(config, "listen"));
                if (address.Length > 0)
                {
                    info.ApiBaseUrl = address + "/v1";
                    info.ManagementUrl = address + "/panel/";
                }

                info.ApiKey = GetString(config, "api_key");
                info.Message = "WorkBuddy 管理页使用上方 API 密钥登录。";
            }
            catch
            {
                info.ApiKey = String.Empty;
                info.Message = "无法读取配置。请检查所选服务的 config.json 格式及文件访问权限。";
            }
            return info;
        }

        private static string GetString(Dictionary<string, object> config, string name)
        {
            object value;
            return config.TryGetValue(name, out value) ? value as string ?? String.Empty : String.Empty;
        }

        private static string Address(string listen)
        {
            if (String.IsNullOrWhiteSpace(listen)) return String.Empty;
            Uri uri;
            if (!Uri.TryCreate("http://" + listen, UriKind.Absolute, out uri)) return String.Empty;
            UriBuilder builder = new UriBuilder(uri);
            if (builder.Host == "0.0.0.0" || builder.Host == "[::]" || builder.Host == "::") builder.Host = "127.0.0.1";
            return builder.Uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        }
    }
}

