using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

namespace OvercookedReplay {
    // hy's demo saves actions and results in a different file from the initial
    // seed/config. Only the matching upstream engine can recover chef positions.
    internal static class SharedTrajectory {
        public static ReplayFile Load(string path) {
            const string suffix = "_trajectory.json";
            if (!path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("无法识别该 JSON 数组。hy 动作轨迹使用 *_trajectory.json 文件名。");
            string summary = path.Substring(0, path.Length - suffix.Length) + "_summary.json";
            if (!File.Exists(summary))
                throw new FileNotFoundException("动作轨迹缺少同目录的 " + Path.GetFileName(summary) + "（内含版本、配置和种子）。");
            string root = Application.isEditor
                ? Path.GetFullPath(Path.Combine(Application.dataPath, "../../../output/Windows"))
                : Path.GetDirectoryName(Application.dataPath);
            string helper = Path.Combine(root, "SharedTrajectory", "SharedTrajectory.exe");
            if (!File.Exists(helper))
                throw new FileNotFoundException("此动作轨迹需要 Windows 包内的 SharedTrajectory 目录。");
            string output = Path.GetTempFileName();
            try {
                // Windows paths cannot contain quotes. Never invoke a shell or
                // use the selected replay's directory as an executable search path.
                var start = new ProcessStartInfo(helper,
                    $"\"{Path.GetFullPath(path)}\" \"{Path.GetFullPath(summary)}\" \"{output}\"") {
                    WorkingDirectory = Path.GetDirectoryName(helper), UseShellExecute = false,
                    CreateNoWindow = true, RedirectStandardError = true, StandardErrorEncoding = Encoding.UTF8,
                };
                using (var process = Process.Start(start)) {
                    var error = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(120000)) {
                        process.Kill();
                        process.WaitForExit();
                        throw new TimeoutException("读取共享厨房动作轨迹超时");
                    }
                    if (process.ExitCode != 0)
                        throw new InvalidDataException("共享厨房轨迹校验失败：" + error.GetAwaiter().GetResult().Trim());
                }
                return ReplayFile.Parse(File.ReadAllText(output));
            } finally { File.Delete(output); }
        }
    }
}
