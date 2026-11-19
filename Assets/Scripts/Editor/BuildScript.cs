using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// 打包：Windows 64 位单机版。
    ///
    /// 命令行能跑就不用点编辑器（早期一次命令行打包报过
    /// "Native extension for WindowsStandalone target not found"，怀疑是环境问题，
    /// 所以这里把失败原因原样打出来，方便判断是模块缺失还是别的）。
    ///
    /// 用法：
    ///   菜单 Tools → 过年啦 → 打包 Windows 版
    ///   命令行 -executeMethod Game.EditorTools.BuildScript.BuildWindowsFromCommandLine
    /// </summary>
    public static class BuildScript
    {
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string OutputDir = "Build";
        const string ExeName = "HappyNewYear.exe";

        [MenuItem("Tools/过年啦/打包 Windows 版", false, 40)]
        public static void BuildWindows()
        {
            Run(false);
        }

        public static void BuildWindowsFromCommandLine()
        {
            Run(true);
        }

        static void Run(bool fromCommandLine)
        {
            if (!File.Exists(ScenePath))
            {
                Fail(fromCommandLine, "找不到场景：" + ScenePath);
                return;
            }

            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", OutputDir));
            Directory.CreateDirectory(dir);
            string exe = Path.Combine(dir, ExeName);

            Debug.Log("[Build] 目标 " + exe);
            Debug.Log("[Build] 模块检查：Windows Standalone "
                + (BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64)
                    ? "已安装" : "**未安装**"));

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = exe,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            Debug.Log("[Build] 结果 " + summary.result
                + "，大小 " + (summary.totalSize / 1048576.0).ToString("F1") + " MB"
                + "，耗时 " + summary.totalTime.TotalSeconds.ToString("F0") + " 秒"
                + "，错误 " + summary.totalErrors + "，警告 " + summary.totalWarnings);

            bool ok = summary.result == BuildResult.Succeeded;
            if (ok) Debug.Log("[Build] 成功：" + exe);
            else Debug.LogError("[Build] 失败，见上方 Console");

            if (fromCommandLine) EditorApplication.Exit(ok ? 0 : 1);
            else if (!ok) EditorUtility.DisplayDialog("打包失败", "原因见 Console 的 [Build] 日志", "好");
        }

        static void Fail(bool fromCommandLine, string message)
        {
            Debug.LogError("[Build] " + message);
            if (fromCommandLine) EditorApplication.Exit(1);
        }
    }
}
