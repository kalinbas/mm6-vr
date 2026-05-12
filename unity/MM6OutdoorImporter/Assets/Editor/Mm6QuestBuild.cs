using System;
using System.IO;
using UnityEditor.Build;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class Mm6QuestBuild
{
    private const string DefaultApplicationIdentifier = "com.kalinbas.mm6viewer";
    private const string DefaultProductName = "MM6 Viewer";
    private const string DefaultCompanyName = "kalinbas";
    private const string OutputArgName = "-mm6BuildOutput";
    private const string DevelopmentArgName = "-mm6DevelopmentBuild";

    [MenuItem("Tools/MM6/Build Quest 2 APK")]
    public static void BuildQuest2ApkMenu()
    {
        string defaultPath = GetDefaultOutputPath();
        string selectedPath = EditorUtility.SaveFilePanel(
            "Build Quest 2 APK",
            Path.GetDirectoryName(defaultPath),
            Path.GetFileName(defaultPath),
            "apk"
        );

        if (string.IsNullOrEmpty(selectedPath))
        {
            return;
        }

        BuildQuest2Apk(selectedPath, developmentBuild: false, showResultDialog: true);
    }

    public static void BuildQuest2Apk()
    {
        string outputPath = GetCommandLineArg(OutputArgName, GetDefaultOutputPath());
        bool developmentBuild = HasCommandLineArg(DevelopmentArgName);
        BuildQuest2Apk(outputPath, developmentBuild, showResultDialog: false);
    }

    private static void BuildQuest2Apk(string outputPath, bool developmentBuild, bool showResultDialog)
    {
        if (string.IsNullOrEmpty(outputPath))
        {
            throw new BuildFailedException("No APK output path was provided.");
        }

        string normalizedOutputPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(normalizedOutputPath) ?? ".");

        ConfigureProjectForQuestBuild();
        if (!Mm6ViewerSetup.PrepareNewSorpigalQuestTest(showDialog: false))
        {
            throw new BuildFailedException(
                "Failed to prepare the New Sorpigal Quest test scene set before building."
            );
        }

        if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
        {
            throw new BuildFailedException("Failed to switch the active build target to Android.");
        }

        BuildOptions options = BuildOptions.None;
        if (developmentBuild)
        {
            options |= BuildOptions.Development | BuildOptions.AllowDebugging;
        }

        BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions
        {
            scenes = GetEnabledScenePaths(),
            locationPathName = normalizedOutputPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = options,
        };

        BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
        if (report == null)
        {
            throw new BuildFailedException("Unity did not return a build report.");
        }

        BuildSummary summary = report.summary;
        if (summary.result != BuildResult.Succeeded)
        {
            throw new BuildFailedException(
                "Quest 2 APK build failed: " + summary.result + ". See the Unity Editor log for details."
            );
        }

        Debug.Log("Quest 2 APK built successfully: " + normalizedOutputPath);
        if (showResultDialog)
        {
            EditorUtility.DisplayDialog(
                "MM6 Viewer",
                "Quest 2 APK built successfully:\n" + normalizedOutputPath,
                "OK"
            );
        }
    }

    private static void ConfigureProjectForQuestBuild()
    {
        if (string.IsNullOrWhiteSpace(PlayerSettings.productName) ||
            string.Equals(PlayerSettings.productName, "MM6OutdoorImporter", StringComparison.Ordinal))
        {
            PlayerSettings.productName = DefaultProductName;
        }

        if (string.IsNullOrWhiteSpace(PlayerSettings.companyName) ||
            string.Equals(PlayerSettings.companyName, "DefaultCompany", StringComparison.Ordinal))
        {
            PlayerSettings.companyName = DefaultCompanyName;
        }

        string applicationIdentifier = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
        if (string.IsNullOrWhiteSpace(applicationIdentifier))
        {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, DefaultApplicationIdentifier);
        }

        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
    }

    private static string[] GetEnabledScenePaths()
    {
        EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
        if (scenes == null || scenes.Length == 0)
        {
            throw new BuildFailedException("Build Settings has no scenes. Run the viewer setup first.");
        }

        int enabledCount = 0;
        for (int i = 0; i < scenes.Length; i++)
        {
            if (scenes[i].enabled && !string.IsNullOrEmpty(scenes[i].path))
            {
                enabledCount++;
            }
        }

        if (enabledCount == 0)
        {
            throw new BuildFailedException("Build Settings has no enabled scenes.");
        }

        string[] result = new string[enabledCount];
        int index = 0;
        for (int i = 0; i < scenes.Length; i++)
        {
            if (scenes[i].enabled && !string.IsNullOrEmpty(scenes[i].path))
            {
                result[index++] = scenes[i].path;
            }
        }

        return result;
    }

    private static string GetDefaultOutputPath()
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        return Path.Combine(
            projectRoot,
            "Builds",
            "Quest2",
            "MM6Viewer-Quest2.apk"
        );
    }

    private static bool HasCommandLineArg(string argName)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], argName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetCommandLineArg(string argName, string fallback)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], argName, StringComparison.Ordinal))
            {
                return args[i + 1];
            }
        }

        return fallback;
    }
}
