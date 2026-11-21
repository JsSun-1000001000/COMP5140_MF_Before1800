using System;
using UnityEditor;
using UnityEditor.Build.Reporting;

public static class BuildPartCDemo
{
    [MenuItem("Before 1800/Build Part C Windows Demo")]
    public static void Build()
    {
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { "Assets/Before1800B/PartB_Greybox.unity" },
            locationPathName = "Builds/PartC/Before1800-PartC.exe",
            target = BuildTarget.StandaloneWindows64
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new Exception("Part C build failed: " + report.summary.result);
    }
}
