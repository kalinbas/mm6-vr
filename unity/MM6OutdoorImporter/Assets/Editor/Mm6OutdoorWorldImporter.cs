using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class Mm6OutdoorWorldImporter
{
    private const string WorldMapName = "mm6_world";
    private const string WorldAssetRoot = "Assets/MM6Imported/" + WorldMapName;
    private const string WorldScenesRoot = WorldAssetRoot + "/Scenes";
    private const string WorldScenePath = WorldScenesRoot + "/" + WorldMapName + ".unity";
    private const string PrimarySpawnMapName = "oute3";
    private const int ExpectedOutdoorMapCount = 15;

    [MenuItem("Tools/MM6/Import Stitched Full Outdoor World")]
    public static void ImportStitchedFullOutdoorWorld()
    {
        string[] packageFolders = Mm6OutdoorImporter.GetBundledPackageFolders();
        if (packageFolders.Length == 0)
        {
            EditorUtility.DisplayDialog(
                "MM6 World Import",
                "No bundled outdoor packages were found under MM6Packages.",
                "OK"
            );
            return;
        }

        List<WorldMapInfo> mapInfos = LoadWorldMapInfos(packageFolders);
        if (mapInfos.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "MM6 World Import",
                "No outdoor world packages with names like outa1 to oute3 were found.",
                "OK"
            );
            return;
        }

        mapInfos.Sort((a, b) =>
        {
            int rowComparison = a.Row.CompareTo(b.Row);
            return rowComparison != 0 ? rowComparison : a.Column.CompareTo(b.Column);
        });

        Mm6OutdoorImporter.ImportPackageFolders(packageFolders, showPerMapDialogs: false, refreshViewerAssets: false);

        EnsureFolder("Assets/MM6Imported");
        EnsureFolder(WorldAssetRoot);
        EnsureFolder(WorldScenesRoot);

        Scene worldScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SetActiveScene(worldScene);

        GameObject worldRoot = new GameObject("MM6World");
        GameObject primaryMapRoot = null;

        for (int i = 0; i < mapInfos.Count; i++)
        {
            WorldMapInfo info = mapInfos[i];
            GameObject clonedMapRoot = CloneImportedMapRootIntoWorld(worldScene, info);
            if (clonedMapRoot == null)
            {
                continue;
            }

            clonedMapRoot.transform.SetParent(worldRoot.transform, false);
            clonedMapRoot.transform.localPosition = info.WorldOffset;
            bool isPrimarySpawnMap = string.Equals(info.MapName, PrimarySpawnMapName, StringComparison.OrdinalIgnoreCase);
            if (!isPrimarySpawnMap)
            {
                RemoveStandalonePlayers(clonedMapRoot);
                RemoveOutdoorEnvironment(clonedMapRoot);
            }
            else
            {
                primaryMapRoot = clonedMapRoot;
            }
        }

        if (primaryMapRoot == null && worldRoot.transform.childCount > 0)
        {
            primaryMapRoot = worldRoot.transform.GetChild(0).gameObject;
        }

        ConfigureWorldMarker(worldRoot, primaryMapRoot);

        EditorSceneManager.MarkSceneDirty(worldScene);
        EditorSceneManager.SaveScene(worldScene, WorldScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Mm6ViewerSetup.RefreshGeneratedViewerAssets();

        string suffix = mapInfos.Count < ExpectedOutdoorMapCount
            ? " Imported " + mapInfos.Count + " of " + ExpectedOutdoorMapCount + " outdoor regions."
            : " Imported all 15 outdoor regions.";
        EditorUtility.DisplayDialog(
            "MM6 World Import",
            "Built stitched world scene at " + WorldScenePath + "." + suffix,
            "OK"
        );
    }

    private static List<WorldMapInfo> LoadWorldMapInfos(IEnumerable<string> packageFolders)
    {
        var mapInfos = new List<WorldMapInfo>();
        if (packageFolders == null)
        {
            return mapInfos;
        }

        foreach (string packageFolder in packageFolders)
        {
            if (string.IsNullOrWhiteSpace(packageFolder))
            {
                continue;
            }

            string jsonPath = Path.Combine(packageFolder, "map.json");
            if (!File.Exists(jsonPath))
            {
                continue;
            }

            WorldPackageMeta package = JsonUtility.FromJson<WorldPackageMeta>(File.ReadAllText(jsonPath));
            if (package == null || string.IsNullOrEmpty(package.mapName) || package.terrain == null)
            {
                continue;
            }

            int column;
            int row;
            if (!TryGetOutdoorGridCoordinates(package.mapName, out column, out row))
            {
                continue;
            }

            int mapStep = Mathf.Max(1, (package.terrain.gridSize - 1) * package.terrain.cellSize);
            Vector3 worldOffset = new Vector3(
                (column - 2) * mapStep,
                0f,
                (1 - row) * mapStep
            );

            mapInfos.Add(new WorldMapInfo
            {
                MapName = package.mapName,
                PackageFolder = packageFolder,
                ScenePath = BuildImportedScenePath(package.mapName),
                Column = column,
                Row = row,
                WorldOffset = worldOffset,
            });
        }

        return mapInfos;
    }

    private static bool TryGetOutdoorGridCoordinates(string mapName, out int column, out int row)
    {
        column = -1;
        row = -1;
        if (string.IsNullOrEmpty(mapName) || mapName.Length != 5)
        {
            return false;
        }

        if (!(mapName[0] == 'o' || mapName[0] == 'O') ||
            !(mapName[1] == 'u' || mapName[1] == 'U') ||
            !(mapName[2] == 't' || mapName[2] == 'T'))
        {
            return false;
        }

        char columnChar = char.ToLowerInvariant(mapName[3]);
        char rowChar = mapName[4];
        if (columnChar < 'a' || columnChar > 'e' || rowChar < '1' || rowChar > '3')
        {
            return false;
        }

        column = columnChar - 'a';
        row = rowChar - '1';
        return true;
    }

    private static GameObject CloneImportedMapRootIntoWorld(Scene worldScene, WorldMapInfo info)
    {
        if (string.IsNullOrEmpty(info.ScenePath) || !File.Exists(ToAbsoluteAssetPath(info.ScenePath)))
        {
            Debug.LogWarning("MM6 world import skipped missing scene " + info.ScenePath);
            return null;
        }

        Scene importedScene = EditorSceneManager.OpenScene(info.ScenePath, OpenSceneMode.Additive);
        try
        {
            GameObject sourceRoot = FindImportedMapRoot(importedScene);
            if (sourceRoot == null)
            {
                Debug.LogWarning("MM6 world import could not find a map root in " + info.ScenePath);
                return null;
            }

            EditorSceneManager.SetActiveScene(worldScene);
            GameObject clone = UnityEngine.Object.Instantiate(sourceRoot);
            clone.name = sourceRoot.name;
            SceneManager.MoveGameObjectToScene(clone, worldScene);
            return clone;
        }
        finally
        {
            EditorSceneManager.CloseScene(importedScene, true);
        }
    }

    private static GameObject FindImportedMapRoot(Scene scene)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            GameObject root = roots[i];
            if (root != null && root.GetComponent<Mm6ImportedMapScene>() != null)
            {
                return root;
            }
        }

        return roots.Length > 0 ? roots[0] : null;
    }

    private static void RemoveStandalonePlayers(GameObject mapRoot)
    {
        Mm6FirstPersonController[] players = mapRoot.GetComponentsInChildren<Mm6FirstPersonController>(true);
        for (int i = 0; i < players.Length; i++)
        {
            Mm6FirstPersonController player = players[i];
            if (player != null && player.gameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(player.gameObject);
            }
        }
    }

    private static void RemoveOutdoorEnvironment(GameObject mapRoot)
    {
        Mm6OutdoorEnvironmentController[] environments =
            mapRoot.GetComponentsInChildren<Mm6OutdoorEnvironmentController>(true);
        for (int i = 0; i < environments.Length; i++)
        {
            Mm6OutdoorEnvironmentController environment = environments[i];
            if (environment != null && environment.gameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(environment.gameObject);
            }
        }
    }

    private static void ConfigureWorldMarker(GameObject worldRoot, GameObject primaryMapRoot)
    {
        Mm6ImportedMapScene marker = worldRoot.GetComponent<Mm6ImportedMapScene>();
        if (marker == null)
        {
            marker = worldRoot.AddComponent<Mm6ImportedMapScene>();
        }

        marker.mapName = WorldMapName;
        marker.mapType = "outdoor-world";
        marker.localSpawnPosition = Vector3.zero;
        marker.localSpawnForward = Vector3.forward;

        if (primaryMapRoot == null)
        {
            return;
        }

        Mm6ImportedMapScene primaryMarker = primaryMapRoot.GetComponent<Mm6ImportedMapScene>();
        if (primaryMarker == null)
        {
            return;
        }

        marker.localSpawnPosition = worldRoot.transform.InverseTransformPoint(primaryMarker.ResolveSpawnPosition());
        marker.localSpawnForward = worldRoot.transform.InverseTransformDirection(primaryMarker.ResolveSpawnForward());
    }

    private static string BuildImportedScenePath(string mapName)
    {
        string safeName = SanitizeFileName(mapName);
        return "Assets/MM6Imported/" + safeName + "/Scenes/" + safeName + ".unity";
    }

    private static void EnsureFolder(string assetPath)
    {
        string[] parts = assetPath.Split('/');
        if (parts.Length == 0)
        {
            return;
        }

        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }
            current = next;
        }
    }

    private static string ToAbsoluteAssetPath(string assetPath)
    {
        return Path.Combine(Directory.GetCurrentDirectory(), assetPath);
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "unnamed";
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        char[] chars = name.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0 || chars[i] == '/' || chars[i] == '\\')
            {
                chars[i] = '_';
            }
        }

        return new string(chars).Trim();
    }

    [Serializable]
    private sealed class WorldPackageMeta
    {
        public string mapName;
        public WorldTerrainMeta terrain;
    }

    [Serializable]
    private sealed class WorldTerrainMeta
    {
        public int gridSize;
        public int cellSize;
    }

    private sealed class WorldMapInfo
    {
        public string MapName;
        public string PackageFolder;
        public string ScenePath;
        public int Column;
        public int Row;
        public Vector3 WorldOffset;
    }
}
