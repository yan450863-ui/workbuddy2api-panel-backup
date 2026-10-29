using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace WorkBuddyLocalConsole
{
    internal static class FirstRun
    {
        internal static void Initialize(string directory)
        {
            string configPath = Path.Combine(directory, "config.json");
            if (File.Exists(configPath)) return;
            string example = Path.Combine(directory, "config.example.json");
            if (!File.Exists(example)) return;
            var serializer = new JavaScriptSerializer();
            var config = serializer.DeserializeObject(File.ReadAllText(example)) as Dictionary<string, object>;
            if (config == null) throw new InvalidOperationException("示例配置格式不正确。");
            byte[] key = new byte[32];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create()) random.GetBytes(key);
            config["api_key"] = Convert.ToBase64String(key).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            config["listen"] = "127.0.0.1:7863";
            Directory.CreateDirectory(Path.Combine(directory, "auths"));
            Directory.CreateDirectory(Path.Combine(directory, "data"));
            // CreateNew preserves an existing configuration even if another process raced us.
            byte[] bytes = new UTF8Encoding(false).GetBytes(serializer.Serialize(config));
            try {
                using (FileStream file = new FileStream(configPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) file.Write(bytes, 0, bytes.Length);
            } catch (IOException) { if (!File.Exists(configPath)) throw; }
        }
    }
}
