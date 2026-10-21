using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TurboGrid.EditorTools
{
    /// <summary>
    /// Compilacion de los builds moviles (menu o batchmode). Las salidas van a
    /// ~/UnityProjects/Builds (fuera de iCloud).
    ///   -executeMethod TurboGrid.EditorTools.TurboGridBuild.BuildAndroid
    ///   -executeMethod TurboGrid.EditorTools.TurboGridBuild.BuildIOS
    /// </summary>
    public static class TurboGridBuild
    {
        private static string BuildsRoot =>
            Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "UnityProjects", "Builds");

        private static string[] Scenes => EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();

        [MenuItem("TurboGrid/Build/Android (APK)", priority = 40)]
        public static void BuildAndroid()
        {
            EditorUserBuildSettings.buildAppBundle = false; // APK instalable directo
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64 | AndroidArchitecture.ARMv7;
            string path = Path.Combine(BuildsRoot, "Android", "TurboGrid.apk");
            Run(BuildTarget.Android, BuildTargetGroup.Android, path);
        }

        [MenuItem("TurboGrid/Build/iOS (proyecto Xcode)", priority = 41)]
        public static void BuildIOS()
        {
            string path = Path.Combine(BuildsRoot, "iOS", "TurboGrid");
            Run(BuildTarget.iOS, BuildTargetGroup.iOS, path);
        }

        private static void Run(BuildTarget target, BuildTargetGroup group, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (EditorUserBuildSettings.activeBuildTarget != target)
                EditorUserBuildSettings.SwitchActiveBuildTarget(group, target);

            var options = new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = path,
                target = target,
                targetGroup = group,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            Debug.Log($"[TurboGridBuild] {target}: {s.result} | {s.totalErrors} errores | {s.totalSize / (1024 * 1024)} MB | {path}");
            if (Application.isBatchMode) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
