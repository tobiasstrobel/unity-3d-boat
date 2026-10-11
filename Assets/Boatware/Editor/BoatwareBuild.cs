using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BoatwareBuild
{
    public static void BuildMacPlayer()
    {
        PlayerSettings.productName = "boatware-sim";
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultIsNativeResolution = false;
        PlayerSettings.defaultScreenWidth = 1280;
        PlayerSettings.defaultScreenHeight = 720;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.runInBackground = true;
        PlayerSettings.displayResolutionDialog = ResolutionDialogSetting.Disabled;

        var output = Path.Combine(Directory.GetCurrentDirectory(), "Builds/boatware-sim.app");
        var options = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/GameScene.unity" },
            locationPathName = output,
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
        {
            Debug.LogError("boatware.sim player build failed: " + report.summary.result);
            EditorApplication.Exit(1);
            return;
        }
        Debug.Log("boatware.sim player: " + output);
    }
}
