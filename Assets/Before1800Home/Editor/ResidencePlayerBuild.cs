using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Before1800.Home.Editor
{
    public static class ResidencePlayerBuild
    {
        [MenuItem("Before 18/Build residence Mac player")]
        public static void BuildMac()
        {
            string project = Path.GetDirectoryName(Application.dataPath);
            string output = Path.Combine(project, "Builds", "Before1800-Mac");
            Directory.CreateDirectory(output);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { "Assets/Before1800Home/PartB_Residence.unity" },
                locationPathName = Path.Combine(output, "Before1800.app"),
                target = BuildTarget.StandaloneOSX
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("Mac build failed: " + report.summary.result);
            File.Copy(Path.Combine(project, "docs/MiSide-Credits.txt"), Path.Combine(output, "MiSide-Credits.txt"), true);
            File.Copy(Path.Combine(Application.dataPath, "Before1800B/SourceHanSans-LICENSE.txt"),
                Path.Combine(output, "SourceHanSans-LICENSE.txt"), true);
            Debug.Log("Mac playtest built: " + output);
        }

        [MenuItem("Before 18/Build residence Windows player")]
        public static void Build()
        {
            string project = Path.GetDirectoryName(Application.dataPath);
            string output = Path.Combine(project, "Builds", "Before1800-Clock");
            Directory.CreateDirectory(output);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { "Assets/Before1800Home/PartB_Residence.unity" },
                locationPathName = Path.Combine(output, "Before1800.exe"),
                target = BuildTarget.StandaloneWindows64
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("Residence Windows build failed: " + report.summary.result);

            string service = Path.Combine(project, "service");
            string packagedService = Path.Combine(output, "service");
            Directory.CreateDirectory(Path.Combine(packagedService, "app"));
            Directory.CreateDirectory(Path.Combine(packagedService, "prompts"));
            foreach (var file in Directory.GetFiles(Path.Combine(service, "app"), "*.py"))
                File.Copy(file, Path.Combine(packagedService, "app", Path.GetFileName(file)), true);
            foreach (var file in new[] { "requirements.txt", "play_with_lin.py", "prompts/lin_system.txt" })
                File.Copy(Path.Combine(service, file), Path.Combine(packagedService, file), true);
            File.Copy(Path.Combine(service, "Play-With-Lin.cmd"), Path.Combine(output, "Play-With-Lin.cmd"), true);
            File.Copy(Path.Combine(project, "docs/MiSide-Credits.txt"), Path.Combine(output, "MiSide-Credits.txt"), true);
            File.Copy(Path.Combine(Application.dataPath, "Before1800B/SourceHanSans-LICENSE.txt"),
                Path.Combine(output, "SourceHanSans-LICENSE.txt"), true);
            Debug.Log("Residence Windows player built at " + output);
        }
    }
}
