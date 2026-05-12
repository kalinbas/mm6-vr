using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

public static class Mm6ViewerSetup
{
    private const string ViewerRoot = "Assets/MM6Viewer";
    private const string ViewerAudioRoot = ViewerRoot + "/Audio";
    private const string ViewerScenePath = ViewerRoot + "/Scenes/MM6Viewer.unity";
    private const string ViewerCatalogPath = ViewerRoot + "/Resources/Mm6ViewerCatalog.asset";
    private const string ImportedScenesRoot = "Assets/MM6Imported";
    private const string NewSorpigalScenePath = ImportedScenesRoot + "/oute3/Scenes/oute3.unity";
    private const string SingleOutdoorPackagesRoot = "MM6SinglePackages";
    private const string NewSorpigalPackagePath = SingleOutdoorPackagesRoot + "/oute3";
    private const string OriginalDataRootName = "originaldata";
    private const string OriginalGameFolderName = "Might and Magic 6";
    private const string SoundtrackFolderName = "Sounds";
    private const string XrGeneralSettingsAssetPath = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";
    private const string OpenXrLoaderType = "UnityEngine.XR.OpenXR.OpenXRLoader";
    private const string MetaQuestFeatureId = "com.unity.openxr.feature.metaquest";
    private const string OculusTouchFeatureId = "com.unity.openxr.feature.input.oculustouch";

    [MenuItem("Tools/MM6/Setup Viewer Application")]
    public static void SetupViewerApplication()
    {
        ConfigureQuest2ProjectSettings(showDialog: false);
        RefreshGeneratedViewerAssets(showDialog: true);
    }

    [MenuItem("Tools/MM6/Configure Quest 2 Project Settings")]
    public static void ConfigureQuest2ProjectSettingsMenu()
    {
        ConfigureQuest2ProjectSettings(showDialog: true);
    }

    [MenuItem("Tools/MM6/Prepare New Sorpigal Quest Test")]
    public static void PrepareNewSorpigalQuestTestMenu()
    {
        PrepareNewSorpigalQuestTest(showDialog: true);
    }

    public static bool PrepareNewSorpigalQuestTest(bool showDialog = false)
    {
        ConfigureQuest2ProjectSettings(showDialog: false);

        string packageFolder = Path.Combine(Directory.GetCurrentDirectory(), NewSorpigalPackagePath);
        string packageJsonPath = Path.Combine(packageFolder, "map.json");
        if (!File.Exists(packageJsonPath))
        {
            string message =
                "New Sorpigal package is missing at:\n" + packageFolder +
                "\n\nBuild it first with:\npython3 tools/build_mm6_new_sorpigal_package.py";
            if (showDialog && !Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("MM6 Viewer", message, "OK");
            }
            else
            {
                Debug.LogError(message);
            }
            return false;
        }

        string importedScenePath = Mm6OutdoorImporter.ImportPackageFolderForAutomation(
            packageFolder,
            refreshViewerAssets: false
        );
        if (string.IsNullOrEmpty(importedScenePath))
        {
            string message = "Failed to import the New Sorpigal package from:\n" + packageFolder;
            if (showDialog && !Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("MM6 Viewer", message, "OK");
            }
            else
            {
                Debug.LogError(message);
            }
            return false;
        }

        RefreshGeneratedViewerAssetsForScenePaths(new[] { NormalizeAssetPath(importedScenePath) }, showDialog: false);

        if (showDialog && !Application.isBatchMode)
        {
            EditorUtility.DisplayDialog(
                "MM6 Viewer",
                "Quest test setup is ready for New Sorpigal.\n\n" +
                "Viewer scene: " + ViewerScenePath + "\n" +
                "Imported map scene: " + NewSorpigalScenePath + "\n\n" +
                "Build Settings now include only the viewer scene and New Sorpigal.",
                "OK"
            );
        }

        return true;
    }

    public static void RefreshGeneratedViewerAssets(bool showDialog = false)
    {
        RefreshGeneratedViewerAssetsInternal(showDialog, null);
    }

    public static void RefreshGeneratedViewerAssetsForScenePaths(IEnumerable<string> scenePaths, bool showDialog = false)
    {
        HashSet<string> normalizedScenePaths = null;
        if (scenePaths != null)
        {
            normalizedScenePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string scenePath in scenePaths)
            {
                string normalizedPath = NormalizeAssetPath(scenePath);
                if (!string.IsNullOrEmpty(normalizedPath))
                {
                    normalizedScenePaths.Add(normalizedPath);
                }
            }
        }

        RefreshGeneratedViewerAssetsInternal(showDialog, normalizedScenePaths);
    }

    private static void RefreshGeneratedViewerAssetsInternal(bool showDialog, HashSet<string> includedScenePaths)
    {
        SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            EnsureFolder(ViewerRoot);
            EnsureFolder(ViewerAudioRoot);
            EnsureFolder(ViewerRoot + "/Scenes");
            EnsureFolder(ViewerRoot + "/Resources");

            List<Mm6ViewerCatalog.MapEntry> entries = RefreshImportedSceneMetadata(includedScenePaths);
            Mm6ViewerCatalog catalog = CreateOrUpdateCatalog(entries);
            CreateOrUpdateViewerScene(catalog);
            UpdateBuildSettings(catalog);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (showDialog)
            {
                EditorUtility.DisplayDialog(
                    "MM6 Viewer",
                    "Viewer scene, catalog, and build settings are ready.",
                    "OK"
                );
            }
        }
        finally
        {
            RestorePreviousSceneSetup(previousSetup);
        }
    }

    public static void ConfigureQuest2ProjectSettings(bool showDialog = false)
    {
        bool changed = false;

        if (PlayerSettings.colorSpace != ColorSpace.Linear)
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            changed = true;
        }

        if (PlayerSettings.Android.minSdkVersion < AndroidSdkVersions.AndroidApiLevel29)
        {
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            changed = true;
        }

        XRGeneralSettingsPerBuildTarget xrGeneralSettings =
            AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(XrGeneralSettingsAssetPath);
        if (xrGeneralSettings != null)
        {
            if (!xrGeneralSettings.HasSettingsForBuildTarget(BuildTargetGroup.Android))
            {
                xrGeneralSettings.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Android);
                changed = true;
            }

            if (!xrGeneralSettings.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
            {
                xrGeneralSettings.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
                changed = true;
            }

            XRGeneralSettings androidSettings = xrGeneralSettings.SettingsForBuildTarget(BuildTargetGroup.Android);
            XRManagerSettings androidManager = xrGeneralSettings.ManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            if (androidSettings != null)
            {
                if (!androidSettings.InitManagerOnStart)
                {
                    androidSettings.InitManagerOnStart = true;
                    changed = true;
                }
                EditorUtility.SetDirty(androidSettings);
            }

            if (androidManager != null)
            {
                if (!androidManager.automaticLoading)
                {
                    androidManager.automaticLoading = true;
                    changed = true;
                }
                if (!androidManager.automaticRunning)
                {
                    androidManager.automaticRunning = true;
                    changed = true;
                }

                if (XRPackageMetadataStore.AssignLoader(androidManager, OpenXrLoaderType, BuildTargetGroup.Android))
                {
                    changed = true;
                }

                EditorUtility.SetDirty(androidManager);
            }

            EditorUtility.SetDirty(xrGeneralSettings);
        }

        FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);

        OpenXRSettings androidOpenXrSettings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
        if (androidOpenXrSettings != null)
        {
            changed |= EnableOpenXrFeature(BuildTargetGroup.Android, MetaQuestFeatureId);
            changed |= EnableOpenXrFeature(BuildTargetGroup.Android, OculusTouchFeatureId);
            EditorUtility.SetDirty(androidOpenXrSettings);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (showDialog)
        {
            string message = changed
                ? "Quest 2 project settings were updated."
                : "Quest 2 project settings already matched the current setup rules.";
            EditorUtility.DisplayDialog("MM6 Viewer", message, "OK");
        }
    }

    private static List<Mm6ViewerCatalog.MapEntry> RefreshImportedSceneMetadata(HashSet<string> includedScenePaths)
    {
        List<Mm6ViewerCatalog.MapEntry> entries = new List<Mm6ViewerCatalog.MapEntry>();
        string[] sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { ImportedScenesRoot });
        Array.Sort(sceneGuids, StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < sceneGuids.Length; i++)
        {
            string scenePath = AssetDatabase.GUIDToAssetPath(sceneGuids[i]);
            if (string.IsNullOrEmpty(scenePath))
            {
                continue;
            }

            string normalizedScenePath = NormalizeAssetPath(scenePath);
            if (includedScenePaths != null &&
                includedScenePaths.Count > 0 &&
                !includedScenePaths.Contains(normalizedScenePath))
            {
                continue;
            }

            Scene scene = EditorSceneManager.GetSceneByPath(scenePath);
            bool openedHere = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                openedHere = true;
            }

            try
            {
                Mm6ViewerCatalog.MapEntry entry = EnsureSceneMetadata(scene, scenePath);
                if (entry != null)
                {
                    entries.Add(entry);
                }
            }
            finally
            {
                if (openedHere)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        entries.Sort((a, b) => string.Compare(a.mapName, b.mapName, StringComparison.OrdinalIgnoreCase));
        return entries;
    }

    private static Mm6ViewerCatalog.MapEntry EnsureSceneMetadata(Scene scene, string scenePath)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        if (roots == null || roots.Length == 0)
        {
            return null;
        }

        GameObject mapRoot = roots[0];
        Mm6ImportedMapScene marker = mapRoot.GetComponent<Mm6ImportedMapScene>();
        if (marker == null)
        {
            marker = mapRoot.AddComponent<Mm6ImportedMapScene>();
        }

        Mm6FirstPersonController player = FindInScene<Mm6FirstPersonController>(scene);
        if (player != null)
        {
            marker.localSpawnPosition = mapRoot.transform.InverseTransformPoint(player.transform.position);
            marker.localSpawnForward = mapRoot.transform.InverseTransformDirection(player.transform.forward);

            Mm6PlayerAvatar avatar = player.GetComponent<Mm6PlayerAvatar>();
            if (avatar == null)
            {
                avatar = player.gameObject.AddComponent<Mm6PlayerAvatar>();
            }

            Camera playerCamera = player.GetComponentInChildren<Camera>(true);
            avatar.viewCamera = playerCamera;
            avatar.headTransform = playerCamera != null ? playerCamera.transform : player.transform;
        }
        else if (marker.localSpawnForward.sqrMagnitude < 0.001f)
        {
            marker.localSpawnForward = Vector3.forward;
        }

        marker.mapName = string.IsNullOrEmpty(marker.mapName) ? mapRoot.name : marker.mapName;
        marker.mapType = DetermineMapType(scene, mapRoot);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        return new Mm6ViewerCatalog.MapEntry
        {
            mapName = marker.mapName,
            mapType = marker.mapType,
            sceneName = Path.GetFileNameWithoutExtension(scenePath),
            scenePath = scenePath,
        };
    }

    private static string DetermineMapType(Scene scene, GameObject root)
    {
        Mm6OutdoorEnvironmentController outdoor = FindInScene<Mm6OutdoorEnvironmentController>(scene);
        if (outdoor != null)
        {
            return "outdoor";
        }

        return "indoor";
    }

    private static Mm6ViewerCatalog CreateOrUpdateCatalog(List<Mm6ViewerCatalog.MapEntry> entries)
    {
        Mm6ViewerCatalog catalog = AssetDatabase.LoadAssetAtPath<Mm6ViewerCatalog>(ViewerCatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<Mm6ViewerCatalog>();
            AssetDatabase.CreateAsset(catalog, ViewerCatalogPath);
        }

        catalog.maps = entries != null ? entries.ToArray() : Array.Empty<Mm6ViewerCatalog.MapEntry>();
        EditorUtility.SetDirty(catalog);
        return catalog;
    }

    private static void CreateOrUpdateViewerScene(Mm6ViewerCatalog catalog)
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject root = new GameObject("MM6Viewer");
        Mm6ViewerApp app = root.AddComponent<Mm6ViewerApp>();
        app.catalog = catalog;
        app.soundtrackClip = PrepareFirstSoundtrackClip();
        app.autoLoadFirstMap = true;
        app.allowReturnToMapSelector = false;
        EditorSceneManager.SaveScene(scene, ViewerScenePath);
    }

    private static void UpdateBuildSettings(Mm6ViewerCatalog catalog)
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>
        {
            new EditorBuildSettingsScene(ViewerScenePath, true)
        };

        if (catalog != null && catalog.maps != null)
        {
            for (int i = 0; i < catalog.maps.Length; i++)
            {
                Mm6ViewerCatalog.MapEntry entry = catalog.maps[i];
                if (entry != null && !string.IsNullOrEmpty(entry.scenePath))
                {
                    scenes.Add(new EditorBuildSettingsScene(entry.scenePath, true));
                }
            }
        }

        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static T FindInScene<T>(Scene scene) where T : Component
    {
        T[] objects = Resources.FindObjectsOfTypeAll<T>();

        for (int i = 0; i < objects.Length; i++)
        {
            T candidate = objects[i];
            if (candidate != null && candidate.gameObject.scene == scene)
            {
                return candidate;
            }
        }

        return null;
    }

    private static void EnsureFolder(string assetFolder)
    {
        if (AssetDatabase.IsValidFolder(assetFolder))
        {
            return;
        }

        string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
        string folderName = Path.GetFileName(assetFolder);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
        {
            EnsureFolder(parent);
        }

        AssetDatabase.CreateFolder(parent, folderName);
    }

    private static bool EnableOpenXrFeature(BuildTargetGroup buildTargetGroup, string featureId)
    {
        var feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(buildTargetGroup, featureId);
        if (feature == null)
        {
            return false;
        }

        if (feature.enabled)
        {
            return false;
        }

        feature.enabled = true;
        EditorUtility.SetDirty(feature);
        return true;
    }

    private static AudioClip PrepareFirstSoundtrackClip()
    {
        string sourcePath = FindFirstSoundtrackSourcePath();
        if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
        {
            return null;
        }

        string assetPath = ViewerAudioRoot + "/" + Path.GetFileName(sourcePath);
        string absoluteAssetPath = Path.Combine(
            Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath,
            assetPath
        );

        string destinationDirectory = Path.GetDirectoryName(absoluteAssetPath);
        if (!string.IsNullOrEmpty(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        bool shouldCopy = !File.Exists(absoluteAssetPath) ||
            File.GetLastWriteTimeUtc(sourcePath) != File.GetLastWriteTimeUtc(absoluteAssetPath) ||
            new FileInfo(sourcePath).Length != new FileInfo(absoluteAssetPath).Length;
        if (shouldCopy)
        {
            File.Copy(sourcePath, absoluteAssetPath, true);
        }

        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        ConfigureSoundtrackImporter(assetPath);
        return AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
    }

    private static void ConfigureSoundtrackImporter(string assetPath)
    {
        AudioImporter importer = AssetImporter.GetAtPath(assetPath) as AudioImporter;
        if (importer == null)
        {
            return;
        }

        importer.forceToMono = false;
        importer.loadInBackground = true;

        AudioImporterSampleSettings sampleSettings = importer.defaultSampleSettings;
        sampleSettings.loadType = AudioClipLoadType.Streaming;
        sampleSettings.compressionFormat = AudioCompressionFormat.Vorbis;
        sampleSettings.quality = 0.7f;
        sampleSettings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
        sampleSettings.preloadAudioData = false;
        importer.defaultSampleSettings = sampleSettings;

        importer.SaveAndReimport();
    }

    private static string FindFirstSoundtrackSourcePath()
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        string workspaceRoot = Directory.GetParent(Directory.GetParent(projectRoot)?.FullName ?? projectRoot)?.FullName;
        if (string.IsNullOrEmpty(workspaceRoot))
        {
            return string.Empty;
        }

        string soundtrackRoot = Path.Combine(
            workspaceRoot,
            OriginalDataRootName,
            OriginalGameFolderName,
            SoundtrackFolderName
        );
        if (!Directory.Exists(soundtrackRoot))
        {
            return string.Empty;
        }

        string[] soundtrackPaths = Directory.GetFiles(soundtrackRoot, "*.*", SearchOption.TopDirectoryOnly);
        Array.Sort(soundtrackPaths, CompareSoundtrackPaths);
        for (int i = 0; i < soundtrackPaths.Length; i++)
        {
            string extension = Path.GetExtension(soundtrackPaths[i]);
            if (string.Equals(extension, ".mp3", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".ogg", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase))
            {
                return soundtrackPaths[i];
            }
        }

        return string.Empty;
    }

    private static int CompareSoundtrackPaths(string leftPath, string rightPath)
    {
        if (TryParseTrackNumber(leftPath, out int leftTrack) && TryParseTrackNumber(rightPath, out int rightTrack))
        {
            int trackComparison = leftTrack.CompareTo(rightTrack);
            if (trackComparison != 0)
            {
                return trackComparison;
            }
        }

        return string.Compare(
            Path.GetFileName(leftPath),
            Path.GetFileName(rightPath),
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static bool TryParseTrackNumber(string path, out int trackNumber)
    {
        return int.TryParse(Path.GetFileNameWithoutExtension(path), out trackNumber);
    }

    private static string NormalizeAssetPath(string assetPath)
    {
        return string.IsNullOrEmpty(assetPath) ? string.Empty : assetPath.Replace('\\', '/');
    }

    private static void RestorePreviousSceneSetup(SceneSetup[] previousSetup)
    {
        if (previousSetup != null && previousSetup.Length > 0)
        {
            EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
            return;
        }

        if (SceneManager.sceneCount == 0 || !SceneManager.GetActiveScene().IsValid())
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }
}
