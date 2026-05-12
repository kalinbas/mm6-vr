using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class Mm6OutdoorImporter
{
    private const string ImportRoot = "Assets/MM6Imported";
    private const string BundledPackagesRoot = "MM6Packages";
    private const string SpriteShaderAssetPath = "Assets/Shaders/MM6BillboardCutout.shader";
    private const float Mm6NormalScale = 65536f;
    private const float Mm6HorizontalVFlipNormalZ = 0xE6CA;

    [MenuItem("Tools/MM6/Import Outdoor Map Package...")]
    public static void ImportOutdoorMapPackage()
    {
        string packageFolder = EditorUtility.OpenFolderPanel(
            "Select an exported MM6 outdoor map package",
            string.Empty,
            string.Empty
        );
        if (string.IsNullOrEmpty(packageFolder))
        {
            return;
        }

        ImportPackageFolder(packageFolder, showDialog: true, refreshViewerAssets: true);
    }

    [MenuItem("Tools/MM6/Import Bundled Packages In Project")]
    public static void ImportBundledPackagesInProjectMenu()
    {
        ImportBundledPackagesInProject(showCompletionDialog: true, showPerMapDialogs: false);
    }

    public static string ImportPackageFolderForAutomation(string packageFolder, bool refreshViewerAssets)
    {
        return ImportPackageFolder(packageFolder, showDialog: false, refreshViewerAssets: refreshViewerAssets);
    }

    public static string[] GetBundledPackageFolders()
    {
        string packagesRoot = Path.Combine(Directory.GetCurrentDirectory(), BundledPackagesRoot);
        if (!Directory.Exists(packagesRoot))
        {
            return Array.Empty<string>();
        }

        string[] jsonPaths = Directory.GetFiles(packagesRoot, "map.json", SearchOption.AllDirectories);
        Array.Sort(jsonPaths, StringComparer.OrdinalIgnoreCase);

        var packageFolders = new List<string>(jsonPaths.Length);
        for (int i = 0; i < jsonPaths.Length; i++)
        {
            string packageFolder = Path.GetDirectoryName(jsonPaths[i]);
            if (!string.IsNullOrEmpty(packageFolder))
            {
                packageFolders.Add(packageFolder);
            }
        }

        return packageFolders.ToArray();
    }

    public static List<string> ImportBundledPackagesInProject(bool showCompletionDialog, bool showPerMapDialogs)
    {
        string packagesRoot = Path.Combine(Directory.GetCurrentDirectory(), BundledPackagesRoot);
        if (!Directory.Exists(packagesRoot))
        {
            if (showCompletionDialog)
            {
                EditorUtility.DisplayDialog(
                    "MM6 Import",
                    "No bundled package folder was found at " + packagesRoot,
                    "OK"
                );
            }
            return new List<string>();
        }

        string[] packageFolders = GetBundledPackageFolders();
        if (packageFolders.Length == 0)
        {
            if (showCompletionDialog)
            {
                EditorUtility.DisplayDialog(
                    "MM6 Import",
                    "No bundled map.json files were found under " + packagesRoot,
                    "OK"
                );
            }
            return new List<string>();
        }

        List<string> importedScenes = ImportPackageFolders(packageFolders, showPerMapDialogs, refreshViewerAssets: false);
        Mm6ViewerSetup.RefreshGeneratedViewerAssets();

        if (showCompletionDialog)
        {
            EditorUtility.DisplayDialog(
                "MM6 Import",
                "Imported " + importedScenes.Count + " bundled outdoor map scenes.",
                "OK"
            );
        }

        return importedScenes;
    }

    public static List<string> ImportPackageFolders(
        IEnumerable<string> packageFolders,
        bool showPerMapDialogs,
        bool refreshViewerAssets
    )
    {
        var importedScenes = new List<string>();
        if (packageFolders == null)
        {
            return importedScenes;
        }

        foreach (string packageFolder in packageFolders)
        {
            if (string.IsNullOrWhiteSpace(packageFolder))
            {
                continue;
            }

            string importedScenePath = ImportPackageFolder(
                packageFolder,
                showDialog: showPerMapDialogs,
                refreshViewerAssets: false
            );
            if (!string.IsNullOrEmpty(importedScenePath))
            {
                importedScenes.Add(importedScenePath);
            }
        }

        if (refreshViewerAssets)
        {
            Mm6ViewerSetup.RefreshGeneratedViewerAssets();
        }

        return importedScenes;
    }

    private static string ImportPackageFolder(string packageFolder, bool showDialog, bool refreshViewerAssets)
    {
        string jsonPath = Path.Combine(packageFolder, "map.json");
        if (!File.Exists(jsonPath))
        {
            if (showDialog)
            {
                EditorUtility.DisplayDialog(
                    "MM6 Import",
                    "The selected folder does not contain map.json.",
                    "OK"
                );
            }
            return string.Empty;
        }

        string json = File.ReadAllText(jsonPath);
        MapPackage package = JsonUtility.FromJson<MapPackage>(json);
        if (package == null || string.IsNullOrEmpty(package.mapName))
        {
            if (showDialog)
            {
                EditorUtility.DisplayDialog(
                    "MM6 Import",
                    "Failed to parse map.json.",
                    "OK"
                );
            }
            return string.Empty;
        }

        ImportContext context = new ImportContext(packageFolder, package);
        return context.Import(showDialog, refreshViewerAssets);
    }

    private sealed class ImportContext
    {
        private readonly string _packageFolder;
        private readonly MapPackage _package;
        private readonly string _assetRoot;
        private readonly string _texturesRoot;
        private readonly string _materialsRoot;
        private readonly string _environmentMaterialsRoot;
        private readonly string _meshesRoot;
        private readonly string _scenesRoot;
        private readonly Dictionary<string, TextureAssetInfo> _bitmapTextures = new Dictionary<string, TextureAssetInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TextureAssetInfo> _spriteTextures = new Dictionary<string, TextureAssetInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Material> _bitmapMaterials = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Material> _spriteMaterials = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        private Material _missingBitmapMaterial;
        private Material _missingSpriteMaterial;
        private Mesh _billboardQuad;

        public ImportContext(string packageFolder, MapPackage package)
        {
            _packageFolder = packageFolder;
            _package = package;
            _assetRoot = ImportRoot + "/" + SanitizeFileName(package.mapName);
            _texturesRoot = _assetRoot + "/Textures";
            _materialsRoot = _assetRoot + "/Materials";
            _environmentMaterialsRoot = _materialsRoot + "/Environment";
            _meshesRoot = _assetRoot + "/Meshes";
            _scenesRoot = _assetRoot + "/Scenes";
        }

        public string Import(bool showDialog, bool refreshViewerAssets)
        {
            EnsureFolder(ImportRoot);
            EnsureFolder(_assetRoot);
            EnsureFolder(_texturesRoot);
            EnsureFolder(_texturesRoot + "/Bitmaps");
            EnsureFolder(_texturesRoot + "/Sprites");
            EnsureFolder(_materialsRoot);
            EnsureFolder(_materialsRoot + "/Bitmaps");
            EnsureFolder(_materialsRoot + "/Sprites");
            EnsureFolder(_environmentMaterialsRoot);
            EnsureFolder(_meshesRoot);
            EnsureFolder(_meshesRoot + "/Environment");
            EnsureFolder(_meshesRoot + "/Terrain");
            EnsureFolder(_meshesRoot + "/Models");
            EnsureFolder(_scenesRoot);

            ImportTextureGroup(_package.textures != null ? _package.textures.bitmaps : null, false);
            ImportTextureGroup(_package.textures != null ? _package.textures.sprites : null, true);

            _billboardQuad = CreateOrReplaceAsset(
                _meshesRoot + "/billboard_quad.asset",
                CreateBillboardQuad()
            );

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SpawnPlacement spawn = DetermineSpawnPlacement();

            GameObject root = new GameObject(_package.mapName);
            GameObject environmentRoot = new GameObject("Environment");
            GameObject terrainRoot = new GameObject("Terrain");
            GameObject modelsRoot = new GameObject("Models");
            GameObject decorationsRoot = new GameObject("Decorations");
            GameObject townsfolkRoot = new GameObject("Townsfolk");
            GameObject monstersRoot = new GameObject("Monsters");
            environmentRoot.transform.SetParent(root.transform, false);
            terrainRoot.transform.SetParent(root.transform, false);
            modelsRoot.transform.SetParent(root.transform, false);
            decorationsRoot.transform.SetParent(root.transform, false);
            townsfolkRoot.transform.SetParent(root.transform, false);
            monstersRoot.transform.SetParent(root.transform, false);

            BuildTerrain(terrainRoot.transform);
            BuildModels(modelsRoot.transform);
            Physics.SyncTransforms();
            BuildDecorations(decorationsRoot.transform);
            BuildTownsfolk(townsfolkRoot.transform);
            BuildMonsters(monstersRoot.transform);
            Camera playerCamera = CreatePlayer(root.transform, spawn);
            CreateEnvironment(environmentRoot.transform, playerCamera);
            ConfigureImportedMapRoot(root, spawn);

            if (_package.terrain != null && _package.terrain.approximateTexturing)
            {
                Debug.LogWarning(
                    "MM6 terrain textures are using base tileset fallbacks because dtile.bin was not part of the exported package."
                );
            }

            string scenePath = _scenesRoot + "/" + SanitizeFileName(_package.mapName) + ".unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            if (refreshViewerAssets)
            {
                Mm6ViewerSetup.RefreshGeneratedViewerAssets();
            }

            if (showDialog)
            {
                EditorUtility.DisplayDialog(
                    "MM6 Import",
                    "Imported " + _package.mapName + " to " + scenePath,
                    "OK"
                );
            }

            return scenePath;
        }

        private void CreateEnvironment(Transform root, Camera targetCamera)
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.6f, 0.62f, 0.67f);
            RenderSettings.fogMode = FogMode.Linear;

            EnvironmentSection environment = ResolveEnvironmentSection();

            GameObject skyAnchor = new GameObject("SkyAnchor");
            skyAnchor.transform.SetParent(root, false);

            GameObject skyDome = new GameObject("SkyDome");
            skyDome.transform.SetParent(skyAnchor.transform, false);
            skyDome.transform.localScale = Vector3.one * 120000f;
            MeshFilter skyFilter = skyDome.AddComponent<MeshFilter>();
            skyFilter.sharedMesh = CreateSkyDomeMesh();
            MeshRenderer skyRenderer = skyDome.AddComponent<MeshRenderer>();
            ConfigureEnvironmentRenderer(skyRenderer);
            skyRenderer.sharedMaterial = CreateMaterial(
                _environmentMaterialsRoot + "/sky_dome.mat",
                Shader.Find("Unlit/Transparent") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Standard"),
                GetBitmapTexture(environment.daySkyTextureName).Texture,
                Color.white,
                true
            );

            GameObject sunLightObject = new GameObject("Sun Light");
            sunLightObject.transform.SetParent(root, false);
            Light sunLight = sunLightObject.AddComponent<Light>();
            sunLight.type = LightType.Directional;
            sunLight.intensity = 1.15f;
            sunLight.shadows = LightShadows.Soft;
            sunLight.color = new Color(1f, 0.97f, 0.92f);

            GameObject moonLightObject = new GameObject("Moon Light");
            moonLightObject.transform.SetParent(root, false);
            Light moonLight = moonLightObject.AddComponent<Light>();
            moonLight.type = LightType.Directional;
            moonLight.intensity = 0.18f;
            moonLight.shadows = LightShadows.None;
            moonLight.color = new Color(0.62f, 0.7f, 1f);

            GameObject sunDisc = CreatePrimitiveWithoutCollider(PrimitiveType.Sphere, "Sun Disc", skyAnchor.transform);
            sunDisc.transform.localScale = Vector3.one * 6000f;
            MeshRenderer sunDiscRenderer = sunDisc.GetComponent<MeshRenderer>();
            ConfigureEnvironmentRenderer(sunDiscRenderer);
            sunDiscRenderer.sharedMaterial = CreateMaterial(
                _environmentMaterialsRoot + "/sun_disc.mat",
                Shader.Find("Unlit/Color") ?? Shader.Find("Unlit/Transparent") ?? Shader.Find("Standard"),
                null,
                new Color(1f, 0.92f, 0.55f, 1f),
                false
            );

            GameObject moonDisc = CreatePrimitiveWithoutCollider(PrimitiveType.Sphere, "Moon Disc", skyAnchor.transform);
            moonDisc.transform.localScale = Vector3.one * 4500f;
            MeshRenderer moonDiscRenderer = moonDisc.GetComponent<MeshRenderer>();
            ConfigureEnvironmentRenderer(moonDiscRenderer);
            moonDiscRenderer.sharedMaterial = CreateMaterial(
                _environmentMaterialsRoot + "/moon_disc.mat",
                Shader.Find("Unlit/Color") ?? Shader.Find("Unlit/Transparent") ?? Shader.Find("Standard"),
                null,
                new Color(0.78f, 0.82f, 1f, 1f),
                false
            );

            ParticleSystem snowParticles = CreateSnowParticleSystem(skyAnchor.transform);

            Mm6OutdoorEnvironmentController controller = root.gameObject.AddComponent<Mm6OutdoorEnvironmentController>();
            controller.mapName = _package.mapName;
            controller.targetCamera = targetCamera;
            controller.skyAnchor = skyAnchor.transform;
            controller.skyRenderer = skyRenderer;
            controller.sunDiscRenderer = sunDiscRenderer;
            controller.moonDiscRenderer = moonDiscRenderer;
            controller.sunLight = sunLight;
            controller.moonLight = moonLight;
            controller.snowParticles = snowParticles;
            controller.daySkyTexture = GetBitmapTexture(environment.daySkyTextureName).Texture;
            controller.alternateDaySkyTexture = GetBitmapTexture(environment.alternateDaySkyTextureName).Texture;
            controller.snowSkyTexture = GetBitmapTexture(environment.snowSkyTextureName).Texture;
            controller.startHour = environment.startHour > 0f ? environment.startHour : 12f;
            controller.hoursPerRealSecond = environment.timeScaleHoursPerRealSecond > 0f
                ? environment.timeScaleHoursPerRealSecond
                : 1f / 120f;
            controller.startDayOfMonth = environment.startDayOfMonth > 0 ? environment.startDayOfMonth : 1;
            controller.startMonth = environment.startMonth;
            controller.fogEnabled = false;
            controller.fogWeakDistance = 0f;
            controller.fogStrongDistance = 0f;
            controller.allowSnow = false;
            controller.sunPathYawDegrees = environment.sunPathYawDegrees;
            controller.weatherSeed = environment.weatherSeed;
            controller.weatherMode = Mm6OutdoorEnvironmentController.WeatherMode.Clear;
        }

        private Mesh CreateSkyDomeMesh()
        {
            GameObject tempPrimitive = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            try
            {
                MeshFilter sourceFilter = tempPrimitive.GetComponent<MeshFilter>();
                Mesh sourceMesh = sourceFilter != null ? sourceFilter.sharedMesh : null;
                Mesh skyMesh = CreateDoubleSidedMesh(sourceMesh, "MM6_SkyDome");
                return CreateOrReplaceAsset(_meshesRoot + "/Environment/sky_dome.asset", skyMesh);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tempPrimitive);
            }
        }

        private void ImportTextureGroup(TextureEntry[] entries, bool sprite)
        {
            if (entries == null)
            {
                return;
            }

            string targetFolder = _texturesRoot + (sprite ? "/Sprites" : "/Bitmaps");
            var destination = sprite ? _spriteTextures : _bitmapTextures;

            foreach (TextureEntry entry in entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.name))
                {
                    continue;
                }

                TextureAssetInfo info = new TextureAssetInfo
                {
                    Width = entry.width,
                    Height = entry.height,
                    UvWidth = entry.uvWidth > 0 ? entry.uvWidth : entry.width,
                    UvHeight = entry.uvHeight > 0 ? entry.uvHeight : entry.height,
                    AssetPath = string.Empty,
                    Texture = null,
                };

                string sourcePath = ResolveTextureSourcePath(entry);
                if (entry.found && !string.IsNullOrEmpty(sourcePath) && File.Exists(sourcePath))
                {
                    string fileName = SanitizeFileName(entry.name) + Path.GetExtension(sourcePath);
                    string assetPath = targetFolder + "/" + fileName;
                    string absoluteAssetPath = ToAbsoluteAssetPath(assetPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(absoluteAssetPath) ?? string.Empty);
                    File.Copy(sourcePath, absoluteAssetPath, true);

                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                    ConfigureTextureImporter(assetPath, sprite);
                    Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);

                    info.AssetPath = assetPath;
                    info.Texture = texture;
                    if (texture != null)
                    {
                        info.Width = texture.width;
                        info.Height = texture.height;
                    }
                }

                destination[entry.name] = info;
            }
        }

        private string ResolveTextureSourcePath(TextureEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.sourcePath))
            {
                return string.Empty;
            }

            if (Path.IsPathRooted(entry.sourcePath))
            {
                return entry.sourcePath;
            }

            return Path.GetFullPath(Path.Combine(_packageFolder, entry.sourcePath));
        }

        private void ConfigureTextureImporter(string assetPath, bool sprite)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            importer.textureType = TextureImporterType.Default;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = !sprite;
            importer.wrapMode = sprite ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
        }

        private void BuildTerrain(Transform parent)
        {
            if (_package.terrain == null || _package.terrain.heights == null || _package.terrain.cellTextureIndices == null)
            {
                return;
            }

            Dictionary<string, MeshBuilder> builders = new Dictionary<string, MeshBuilder>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, MeshBuilder> underlayBuilders = new Dictionary<string, MeshBuilder>(StringComparer.OrdinalIgnoreCase);
            int gridSize = _package.terrain.gridSize;
            int cellSize = _package.terrain.cellSize;
            int heightScale = _package.terrain.heightScale;
            string waterTextureName = ResolveWaterTerrainTextureName();

            for (int y = 0; y < gridSize - 1; y++)
            {
                for (int x = 0; x < gridSize - 1; x++)
                {
                    int cellIndex = y * (gridSize - 1) + x;
                    int textureIndex = GetArrayValue(_package.terrain.cellTextureIndices, cellIndex, 0);
                    string textureName = GetArrayValue(_package.terrain.textureNames, textureIndex, "DirtTyl");
                    MeshBuilder builder = GetOrCreateBuilder(builders, textureName);

                    Vector3 nw = TerrainVertex(x, y, gridSize, cellSize, heightScale, _package.terrain.heights);
                    Vector3 ne = TerrainVertex(x + 1, y, gridSize, cellSize, heightScale, _package.terrain.heights);
                    Vector3 sw = TerrainVertex(x, y + 1, gridSize, cellSize, heightScale, _package.terrain.heights);
                    Vector3 se = TerrainVertex(x + 1, y + 1, gridSize, cellSize, heightScale, _package.terrain.heights);
                    builder.AddQuad(nw, ne, sw, se);

                    if (!string.IsNullOrEmpty(waterTextureName) && IsWaterTransitionTexture(textureName))
                    {
                        MeshBuilder underlayBuilder = GetOrCreateBuilder(underlayBuilders, waterTextureName);
                        Vector3 underlayOffset = new Vector3(0f, -0.25f, 0f);
                        underlayBuilder.AddQuad(
                            nw + underlayOffset,
                            ne + underlayOffset,
                            sw + underlayOffset,
                            se + underlayOffset
                        );
                    }
                }
            }

            foreach (KeyValuePair<string, MeshBuilder> pair in builders)
            {
                if (pair.Value.VertexCount == 0)
                {
                    continue;
                }

                string safeName = SanitizeFileName(pair.Key);
                Mesh mesh = pair.Value.ToMesh("terrain_" + safeName, recalculateNormals: true);
                mesh = CreateOrReplaceAsset(_meshesRoot + "/Terrain/" + safeName + ".asset", mesh);

                GameObject terrainObject = new GameObject(pair.Key);
                terrainObject.transform.SetParent(parent, false);
                MeshFilter filter = terrainObject.AddComponent<MeshFilter>();
                MeshRenderer renderer = terrainObject.AddComponent<MeshRenderer>();
                filter.sharedMesh = mesh;
                renderer.sharedMaterial = GetBitmapMaterial(pair.Key);
                MeshCollider collider = terrainObject.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
            }

            foreach (KeyValuePair<string, MeshBuilder> pair in underlayBuilders)
            {
                if (pair.Value.VertexCount == 0)
                {
                    continue;
                }

                string safeName = SanitizeFileName(pair.Key) + "_underlay";
                Mesh mesh = pair.Value.ToMesh("terrain_" + safeName, recalculateNormals: true);
                mesh = CreateOrReplaceAsset(_meshesRoot + "/Terrain/" + safeName + ".asset", mesh);

                GameObject terrainObject = new GameObject(pair.Key + "_Underlay");
                terrainObject.transform.SetParent(parent, false);
                MeshFilter filter = terrainObject.AddComponent<MeshFilter>();
                MeshRenderer renderer = terrainObject.AddComponent<MeshRenderer>();
                filter.sharedMesh = mesh;
                renderer.sharedMaterial = GetBitmapMaterial(pair.Key);
            }
        }

        private void BuildModels(Transform parent)
        {
            if (_package.models == null)
            {
                return;
            }

            for (int modelIndex = 0; modelIndex < _package.models.Length; modelIndex++)
            {
                ModelData model = _package.models[modelIndex];
                if (model == null || model.verticesUnity == null || model.faces == null)
                {
                    continue;
                }

                GameObject modelRoot = new GameObject(string.IsNullOrEmpty(model.name) ? "Model_" + modelIndex : model.name);
                modelRoot.transform.SetParent(parent, false);

                Dictionary<string, MeshBuilder> builders = new Dictionary<string, MeshBuilder>(StringComparer.OrdinalIgnoreCase);
                foreach (FaceData face in model.faces)
                {
                    if (face == null || face.vertexIndices == null || face.vertexIndices.Length < 3)
                    {
                        continue;
                    }

                    string textureName = string.IsNullOrEmpty(face.textureName) ? "MissingTexture" : face.textureName;
                    MeshBuilder builder = GetOrCreateBuilder(builders, textureName);

                    Vector3 normal = ConvertNormal(face.normal);
                    bool vFlip = face.normal != null && face.normal.Length > 2 && Mathf.Abs(face.normal[2]) >= Mm6HorizontalVFlipNormalZ;
                    TextureAssetInfo textureInfo = GetBitmapTexture(textureName);
                    float textureWidth = Mathf.Max(1f, textureInfo.UvWidth > 0 ? textureInfo.UvWidth : 128);
                    float textureHeight = Mathf.Max(1f, textureInfo.UvHeight > 0 ? textureInfo.UvHeight : 128);
                    bool flipReadableEntranceSignU = string.Equals(model.name, "EntranceN", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(textureName, "1-Wall", StringComparison.OrdinalIgnoreCase);

                    List<Vector3> polygonVertices = new List<Vector3>(face.vertexIndices.Length);
                    List<Vector2> polygonUvs = new List<Vector2>(face.vertexIndices.Length);

                    bool validFace = true;
                    for (int i = 0; i < face.vertexIndices.Length; i++)
                    {
                        int vertexIndex = face.vertexIndices[i];
                        if (vertexIndex < 0 || vertexIndex >= model.verticesUnity.Length)
                        {
                            validFace = false;
                            break;
                        }

                        polygonVertices.Add(model.verticesUnity[vertexIndex].ToVector3());
                        float u = (face.bitmapU + GetArrayValue(face.uList, i, 0)) / textureWidth;
                        float v = (face.bitmapV + GetArrayValue(face.vList, i, 0)) / textureHeight;
                        if (!vFlip)
                        {
                            v = 1f - v;
                        }
                        if (flipReadableEntranceSignU)
                        {
                            u = 1f - u;
                        }
                        polygonUvs.Add(new Vector2(u, v));
                    }

                    if (validFace)
                    {
                        builder.AddPolygon(polygonVertices, polygonUvs, normal);
                    }
                }

                foreach (KeyValuePair<string, MeshBuilder> pair in builders)
                {
                    if (pair.Value.VertexCount == 0)
                    {
                        continue;
                    }

                    string safeTextureName = SanitizeFileName(pair.Key);
                    Mesh mesh = pair.Value.ToMesh(modelRoot.name + "_" + safeTextureName, recalculateNormals: false);
                    mesh = CreateOrReplaceAsset(
                        _meshesRoot + "/Models/" + modelIndex.ToString("D3") + "_" + safeTextureName + ".asset",
                        mesh
                    );

                    GameObject section = new GameObject(pair.Key);
                    section.transform.SetParent(modelRoot.transform, false);
                    MeshFilter filter = section.AddComponent<MeshFilter>();
                    MeshRenderer renderer = section.AddComponent<MeshRenderer>();
                    filter.sharedMesh = mesh;
                    renderer.sharedMaterial = GetBitmapMaterial(pair.Key);
                    MeshCollider collider = section.AddComponent<MeshCollider>();
                    collider.sharedMesh = mesh;
                }
            }
        }

        private void BuildDecorations(Transform parent)
        {
            if (_package.decorations == null)
            {
                return;
            }

            for (int i = 0; i < _package.decorations.Length; i++)
            {
                DecorationData decoration = _package.decorations[i];
                if (decoration == null || decoration.positionUnity == null || string.IsNullOrEmpty(decoration.textureInfoName))
                {
                    continue;
                }

                GameObject go = new GameObject(string.IsNullOrEmpty(decoration.name) ? "Decoration_" + i : decoration.name);
                go.transform.SetParent(parent, false);

                go.transform.position = decoration.positionUnity.ToVector3();
                go.transform.localScale = new Vector3(Mathf.Max(1f, decoration.width), Mathf.Max(1f, decoration.height), 1f);

                MeshFilter filter = go.AddComponent<MeshFilter>();
                MeshRenderer renderer = go.AddComponent<MeshRenderer>();
                filter.sharedMesh = _billboardQuad;
                renderer.sharedMaterial = GetSpriteMaterial(decoration.textureInfoName);
                go.AddComponent<Mm6Billboard>();
                ConfigureSpriteAnimation(
                    go,
                    renderer,
                    decoration.animationTextureInfoNames,
                    decoration.animationFrameDurationsSeconds,
                    decoration.animationStartOffsetSeconds
                );
            }
        }

        private void BuildTownsfolk(Transform parent)
        {
            if (_package.townsfolk == null)
            {
                return;
            }

            for (int i = 0; i < _package.townsfolk.Length; i++)
            {
                TownspersonData townsperson = _package.townsfolk[i];
                if (townsperson == null || townsperson.positionUnity == null || string.IsNullOrEmpty(townsperson.textureInfoName))
                {
                    continue;
                }

                string objectName = !string.IsNullOrEmpty(townsperson.displayName)
                    ? townsperson.displayName
                    : !string.IsNullOrEmpty(townsperson.name)
                        ? townsperson.name
                        : "Townsperson_" + i;

                GameObject go = new GameObject(objectName);
                go.transform.SetParent(parent, false);
                go.transform.position = townsperson.positionUnity.ToVector3();
                go.transform.localScale = new Vector3(
                    Mathf.Max(1f, townsperson.width),
                    Mathf.Max(1f, townsperson.height),
                    1f
                );

                MeshFilter filter = go.AddComponent<MeshFilter>();
                MeshRenderer renderer = go.AddComponent<MeshRenderer>();
                filter.sharedMesh = _billboardQuad;
                renderer.sharedMaterial = GetSpriteMaterial(townsperson.textureInfoName);
                go.AddComponent<Mm6Billboard>();
                ConfigureSpriteAnimation(
                    go,
                    renderer,
                    townsperson.animationTextureInfoNames,
                    townsperson.animationFrameDurationsSeconds,
                    townsperson.animationStartOffsetSeconds
                );
            }
        }

        private void BuildMonsters(Transform parent)
        {
            if (_package.monsters == null)
            {
                return;
            }

            for (int i = 0; i < _package.monsters.Length; i++)
            {
                MonsterData monster = _package.monsters[i];
                if (monster == null || monster.positionUnity == null || string.IsNullOrEmpty(monster.textureInfoName))
                {
                    continue;
                }

                string objectName = !string.IsNullOrEmpty(monster.displayName)
                    ? monster.displayName
                    : !string.IsNullOrEmpty(monster.name)
                        ? monster.name
                        : "Monster_" + i;

                GameObject go = new GameObject(objectName);
                go.transform.SetParent(parent, false);
                go.transform.position = ResolveMonsterPosition(monster.positionUnity.ToVector3());
                go.transform.localScale = new Vector3(
                    Mathf.Max(1f, monster.width),
                    Mathf.Max(1f, monster.height),
                    1f
                );

                MeshFilter filter = go.AddComponent<MeshFilter>();
                MeshRenderer renderer = go.AddComponent<MeshRenderer>();
                filter.sharedMesh = _billboardQuad;
                renderer.sharedMaterial = GetSpriteMaterial(monster.textureInfoName);
                go.AddComponent<Mm6Billboard>();
                ConfigureMonsterBehavior(
                    go,
                    renderer,
                    monster
                );
            }
        }

        private void ConfigureMonsterBehavior(GameObject target, MeshRenderer renderer, MonsterData monster)
        {
            if (target == null || renderer == null || monster == null)
            {
                return;
            }

            ResolvedSpriteClip standingClip = ResolveSpriteClip(
                monster.textureInfoName,
                monster.animationTextureInfoNames,
                monster.animationFrameDurationsSeconds
            );
            ResolvedSpriteClip idleClip = ResolveSpriteClip(
                !string.IsNullOrEmpty(monster.idleTextureInfoName) ? monster.idleTextureInfoName : monster.textureInfoName,
                monster.idleAnimationTextureInfoNames,
                monster.idleAnimationFrameDurationsSeconds
            );
            ResolvedSpriteClip walkingClip = ResolveSpriteClip(
                !string.IsNullOrEmpty(monster.walkingTextureInfoName) ? monster.walkingTextureInfoName : monster.textureInfoName,
                monster.walkingAnimationTextureInfoNames,
                monster.walkingAnimationFrameDurationsSeconds
            );

            Mm6MonsterController controller = target.AddComponent<Mm6MonsterController>();
            controller.targetRenderer = renderer;
            controller.standingFrames = standingClip.Frames;
            controller.standingFrameDurationsSeconds = standingClip.Durations;
            controller.idleFrames = idleClip.Frames.Length > 0 ? idleClip.Frames : standingClip.Frames;
            controller.idleFrameDurationsSeconds = idleClip.Durations.Length > 0
                ? idleClip.Durations
                : standingClip.Durations;
            controller.standingStateDurationSeconds = Mathf.Max(0.25f, monster.standingStateDurationSeconds);
            controller.walkingFrames = walkingClip.Frames.Length > 0 ? walkingClip.Frames : standingClip.Frames;
            controller.walkingFrameDurationsSeconds = walkingClip.Durations.Length > 0
                ? walkingClip.Durations
                : standingClip.Durations;
            controller.animationStartOffsetSeconds = monster.animationStartOffsetSeconds;
            controller.moveSpeed = Mathf.Max(0f, monster.moveSpeed);
            controller.activationDistance = Mathf.Max(0f, monster.activationDistance);
            controller.loseInterestDistance = Mathf.Max(controller.activationDistance, monster.loseInterestDistance);
            controller.stopDistance = Mathf.Max(0f, monster.stopDistance);
        }

        private ResolvedSpriteClip ResolveSpriteClip(
            string primaryTextureInfoName,
            string[] animationTextureInfoNames,
            float[] animationFrameDurationsSeconds
        )
        {
            List<Texture2D> frames = new List<Texture2D>();
            List<float> durations = new List<float>();

            if (animationTextureInfoNames != null && animationTextureInfoNames.Length > 0)
            {
                for (int i = 0; i < animationTextureInfoNames.Length; i++)
                {
                    string textureInfoName = animationTextureInfoNames[i];
                    if (string.IsNullOrEmpty(textureInfoName))
                    {
                        continue;
                    }

                    Texture2D texture = GetSpriteTexture(textureInfoName).Texture as Texture2D;
                    if (texture == null)
                    {
                        continue;
                    }

                    frames.Add(texture);
                    float duration = 0.125f;
                    if (animationFrameDurationsSeconds != null && i < animationFrameDurationsSeconds.Length)
                    {
                        duration = Mathf.Max(0.01f, animationFrameDurationsSeconds[i]);
                    }
                    durations.Add(duration);
                }
            }

            if (frames.Count == 0 && !string.IsNullOrEmpty(primaryTextureInfoName))
            {
                Texture2D texture = GetSpriteTexture(primaryTextureInfoName).Texture as Texture2D;
                if (texture != null)
                {
                    frames.Add(texture);
                    durations.Add(0.125f);
                }
            }

            return new ResolvedSpriteClip(frames.ToArray(), durations.ToArray());
        }

        private Vector3 ResolveMonsterPosition(Vector3 position)
        {
            float groundedY;
            if (TryGetGroundHeight(position, out groundedY))
            {
                position.y = groundedY;
            }

            return position;
        }

        private bool TryGetGroundHeight(Vector3 position, out float groundedY)
        {
            groundedY = position.y;

            bool found = false;
            float terrainHeight;
            if (TrySampleTerrainHeight(position.x, position.z, out terrainHeight))
            {
                groundedY = terrainHeight;
                found = true;
            }

            float rayStartY = Mathf.Max(position.y, groundedY) + 4096f;
            RaycastHit hit;
            if (Physics.Raycast(
                new Vector3(position.x, rayStartY, position.z),
                Vector3.down,
                out hit,
                rayStartY + 4096f,
                ~0,
                QueryTriggerInteraction.Ignore
            ))
            {
                groundedY = !found ? hit.point.y : Mathf.Max(groundedY, hit.point.y);
                found = true;
            }

            return found;
        }

        private bool TrySampleTerrainHeight(float worldX, float worldZ, out float height)
        {
            height = 0f;
            if (_package.terrain == null || _package.terrain.heights == null)
            {
                return false;
            }

            int gridSize = _package.terrain.gridSize;
            int cellSize = _package.terrain.cellSize;
            int heightScale = _package.terrain.heightScale;
            if (gridSize <= 1 || cellSize <= 0 || heightScale <= 0)
            {
                return false;
            }

            if (_package.terrain.heights.Length < gridSize * gridSize)
            {
                return false;
            }

            float gridX = gridSize * 0.5f - worldX / cellSize;
            float gridY = worldZ / cellSize + gridSize * 0.5f;
            gridX = Mathf.Clamp(gridX, 0f, gridSize - 1.001f);
            gridY = Mathf.Clamp(gridY, 0f, gridSize - 1.001f);

            int x0 = Mathf.FloorToInt(gridX);
            int y0 = Mathf.FloorToInt(gridY);
            int x1 = Mathf.Min(gridSize - 1, x0 + 1);
            int y1 = Mathf.Min(gridSize - 1, y0 + 1);
            float tx = gridX - x0;
            float ty = gridY - y0;

            float h00 = _package.terrain.heights[y0 * gridSize + x0] * heightScale;
            float h10 = _package.terrain.heights[y0 * gridSize + x1] * heightScale;
            float h01 = _package.terrain.heights[y1 * gridSize + x0] * heightScale;
            float h11 = _package.terrain.heights[y1 * gridSize + x1] * heightScale;
            float h0 = Mathf.Lerp(h00, h10, tx);
            float h1 = Mathf.Lerp(h01, h11, tx);
            height = Mathf.Lerp(h0, h1, ty);
            return true;
        }

        private void ConfigureSpriteAnimation(
            GameObject target,
            MeshRenderer renderer,
            string[] animationTextureInfoNames,
            float[] animationFrameDurationsSeconds,
            float animationStartOffsetSeconds
        )
        {
            if (target == null || renderer == null || animationTextureInfoNames == null || animationTextureInfoNames.Length <= 1)
            {
                return;
            }

            List<Texture2D> frames = new List<Texture2D>(animationTextureInfoNames.Length);
            List<float> durations = new List<float>(animationTextureInfoNames.Length);

            for (int i = 0; i < animationTextureInfoNames.Length; i++)
            {
                string textureName = animationTextureInfoNames[i];
                if (string.IsNullOrEmpty(textureName))
                {
                    continue;
                }

                Texture2D texture = GetSpriteTexture(textureName).Texture as Texture2D;
                if (texture == null)
                {
                    continue;
                }

                frames.Add(texture);
                float duration = 0.125f;
                if (animationFrameDurationsSeconds != null && i < animationFrameDurationsSeconds.Length)
                {
                    duration = Mathf.Max(0.01f, animationFrameDurationsSeconds[i]);
                }
                durations.Add(duration);
            }

            if (frames.Count <= 1)
            {
                return;
            }

            Mm6SpriteAnimator animator = target.AddComponent<Mm6SpriteAnimator>();
            animator.targetRenderer = renderer;
            animator.frames = frames.ToArray();
            animator.frameDurationsSeconds = durations.ToArray();
            animator.startOffsetSeconds = Mathf.Max(0f, animationStartOffsetSeconds);
        }

        private void ConfigureImportedMapRoot(GameObject root, SpawnPlacement spawn)
        {
            if (root == null)
            {
                return;
            }

            Mm6ImportedMapScene marker = root.GetComponent<Mm6ImportedMapScene>();
            if (marker == null)
            {
                marker = root.AddComponent<Mm6ImportedMapScene>();
            }

            marker.mapName = _package.mapName;
            marker.mapType = "outdoor";
            marker.localSpawnPosition = spawn.Position;
            marker.localSpawnForward = spawn.Rotation * Vector3.forward;
        }

        private Camera CreatePlayer(Transform parent, SpawnPlacement spawn)
        {
            GameObject player = new GameObject("Player");
            player.transform.SetParent(parent, false);
            player.transform.position = spawn.Position;
            player.transform.rotation = spawn.Rotation;

            CharacterController controller = player.AddComponent<CharacterController>();
            controller.height = 180f;
            controller.radius = 28f;
            controller.center = new Vector3(0f, controller.height * 0.5f, 0f);
            controller.stepOffset = 35f;
            controller.skinWidth = 4f;
            controller.minMoveDistance = 0f;

            GameObject cameraPivot = new GameObject("CameraPivot");
            cameraPivot.transform.SetParent(player.transform, false);
            cameraPivot.transform.localPosition = new Vector3(0f, 150f, 0f);

            GameObject cameraObject = new GameObject("PlayerCamera");
            cameraObject.transform.SetParent(cameraPivot.transform, false);
            cameraObject.transform.localPosition = Vector3.zero;
            cameraObject.transform.localRotation = Quaternion.identity;
            cameraObject.tag = "MainCamera";

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 1f;
            camera.farClipPlane = 1000000f;
            camera.fieldOfView = 75f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.09f, 0.12f, 0.2f);

            cameraObject.AddComponent<AudioListener>();

            Mm6FirstPersonController fps = player.AddComponent<Mm6FirstPersonController>();
            fps.lookPivot = cameraPivot.transform;

            Mm6PlayerAvatar avatar = player.AddComponent<Mm6PlayerAvatar>();
            avatar.viewCamera = camera;
            avatar.headTransform = cameraObject.transform;
            return camera;
        }

        private EnvironmentSection ResolveEnvironmentSection()
        {
            EnvironmentSection environment = _package.environment ?? new EnvironmentSection();
            if (string.IsNullOrEmpty(environment.daySkyTextureName))
            {
                environment.daySkyTextureName = ResolveAvailableBitmapName("PLANSKY1", "PLANSKY2", "sky19");
            }
            if (string.IsNullOrEmpty(environment.alternateDaySkyTextureName))
            {
                environment.alternateDaySkyTextureName = ResolveAvailableBitmapName("PLANSKY2", "PLANSKY1");
            }
            if (string.IsNullOrEmpty(environment.snowSkyTextureName))
            {
                environment.snowSkyTextureName = ResolveAvailableBitmapName("sky19");
            }
            if (environment.weatherSeed == 0)
            {
                environment.weatherSeed = Mathf.Abs((_package.mapName ?? "mm6").ToLowerInvariant().GetHashCode());
            }
            return environment;
        }

        private string ResolveAvailableBitmapName(params string[] candidates)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                string candidate = candidates[i];
                if (string.IsNullOrEmpty(candidate))
                {
                    continue;
                }

                TextureAssetInfo info;
                if (_bitmapTextures.TryGetValue(candidate, out info) && info != null && info.Texture != null)
                {
                    return candidate;
                }
            }

            return string.Empty;
        }

        private ParticleSystem CreateSnowParticleSystem(Transform parent)
        {
            GameObject go = new GameObject("Snow");
            go.transform.SetParent(parent, false);
            ParticleSystem particleSystem = go.AddComponent<ParticleSystem>();
            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();

            ParticleSystem.MainModule main = particleSystem.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 6f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(180f, 260f);
            main.startSize = new ParticleSystem.MinMaxCurve(22f, 42f);
            main.maxParticles = 2048;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            ParticleSystem.EmissionModule emission = particleSystem.emission;
            emission.rateOverTime = 160f;

            ParticleSystem.ShapeModule shape = particleSystem.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(14000f, 3500f, 14000f);
            shape.position = new Vector3(0f, 1400f, 0f);

            ParticleSystem.VelocityOverLifetimeModule velocity = particleSystem.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(-25f, 25f);
            velocity.y = new ParticleSystem.MinMaxCurve(-80f, -140f);
            velocity.z = new ParticleSystem.MinMaxCurve(-25f, 25f);

            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particleSystem.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(new Color(0.9f, 0.95f, 1f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.9f, 0.15f),
                    new GradientAlphaKey(0.9f, 0.85f),
                    new GradientAlphaKey(0f, 1f),
                }
            );
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.sharedMaterial = CreateMaterial(
                _environmentMaterialsRoot + "/snow_particles.mat",
                Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Particles/Alpha Blended") ?? Shader.Find("Unlit/Color"),
                null,
                Color.white,
                true
            );

            particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return particleSystem;
        }

        private static GameObject CreatePrimitiveWithoutCollider(PrimitiveType primitiveType, string name, Transform parent)
        {
            GameObject go = GameObject.CreatePrimitive(primitiveType);
            go.name = name;
            go.transform.SetParent(parent, false);

            Collider collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }

            return go;
        }

        private static void ConfigureEnvironmentRenderer(Renderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.lightProbeUsage = LightProbeUsage.Off;
        }

        private SpawnPlacement DetermineSpawnPlacement()
        {
            Vector3 fallbackPosition = new Vector3(0f, 256f, 0f);
            Quaternion fallbackRotation = Quaternion.identity;

            if (_package.spawnPoints == null || _package.spawnPoints.Length == 0)
            {
                return new SpawnPlacement(fallbackPosition, fallbackRotation);
            }

            if (string.Equals(_package.mapName, "oute3", StringComparison.OrdinalIgnoreCase))
            {
                SpawnPlacement entrancePlacement;
                if (TryFindNewSorpigalEntrancePlacement(out entrancePlacement))
                {
                    return entrancePlacement;
                }

                Vector3 villageTarget;
                SpawnPointData villageSpawn;
                if (TryFindVillageEntranceSpawn(out villageSpawn, out villageTarget))
                {
                    return BuildSpawnPlacement(villageSpawn.positionUnity.ToVector3(), villageTarget);
                }
            }

            SpawnPointData firstSpawn = _package.spawnPoints[0];
            Vector3 firstPosition = firstSpawn != null && firstSpawn.positionUnity != null
                ? firstSpawn.positionUnity.ToVector3()
                : fallbackPosition;
            return BuildSpawnPlacement(firstPosition, firstPosition + Vector3.forward * 1024f);
        }

        private bool TryFindNewSorpigalEntrancePlacement(out SpawnPlacement placement)
        {
            placement = new SpawnPlacement(Vector3.zero, Quaternion.identity);

            ModelData entranceModel;
            if (!TryFindModelByName("EntranceN", out entranceModel))
            {
                return false;
            }

            Vector3 min;
            Vector3 max;
            if (!TryGetModelBounds(entranceModel, out min, out max))
            {
                return false;
            }

            float centerX = (min.x + max.x) * 0.5f;
            float groundY = min.y;
            float outsideOffset = 320f;
            float lookDepth = 768f;

            Vector3 spawnPosition = new Vector3(centerX, groundY, max.z + outsideOffset);
            Vector3 lookTarget = new Vector3(centerX, groundY, min.z - lookDepth);
            placement = BuildSpawnPlacement(spawnPosition, lookTarget);
            return true;
        }

        private bool TryFindVillageEntranceSpawn(out SpawnPointData bestSpawn, out Vector3 villageTarget)
        {
            bestSpawn = null;
            villageTarget = Vector3.zero;

            Vector3 landmarkCenter;
            if (!TryComputeVillageLandmarkCenter(out landmarkCenter))
            {
                return false;
            }

            float bestDistance = float.MaxValue;
            for (int i = 0; i < _package.spawnPoints.Length; i++)
            {
                SpawnPointData spawn = _package.spawnPoints[i];
                if (spawn == null || spawn.positionUnity == null)
                {
                    continue;
                }

                Vector3 spawnPosition = spawn.positionUnity.ToVector3();
                float distance = Vector3.Distance(spawnPosition, landmarkCenter);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestSpawn = spawn;
                }
            }

            if (bestSpawn == null)
            {
                return false;
            }

            villageTarget = landmarkCenter;
            return true;
        }

        private bool TryFindModelByName(string expectedName, out ModelData result)
        {
            result = null;
            if (_package.models == null || _package.models.Length == 0)
            {
                return false;
            }

            for (int modelIndex = 0; modelIndex < _package.models.Length; modelIndex++)
            {
                ModelData model = _package.models[modelIndex];
                if (model == null)
                {
                    continue;
                }

                if (string.Equals(model.name, expectedName, StringComparison.OrdinalIgnoreCase))
                {
                    result = model;
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetModelBounds(ModelData model, out Vector3 min, out Vector3 max)
        {
            min = Vector3.zero;
            max = Vector3.zero;
            if (model == null || model.verticesUnity == null || model.verticesUnity.Length == 0)
            {
                return false;
            }

            min = model.verticesUnity[0].ToVector3();
            max = min;
            for (int vertexIndex = 1; vertexIndex < model.verticesUnity.Length; vertexIndex++)
            {
                Vector3 vertex = model.verticesUnity[vertexIndex].ToVector3();
                min = Vector3.Min(min, vertex);
                max = Vector3.Max(max, vertex);
            }

            return true;
        }

        private bool TryComputeVillageLandmarkCenter(out Vector3 center)
        {
            center = Vector3.zero;
            if (_package.models == null || _package.models.Length == 0)
            {
                return false;
            }

            string[] keywords = { "temple", "fountain", "sgn", "stbl" };
            Vector3 sum = Vector3.zero;
            int count = 0;

            for (int modelIndex = 0; modelIndex < _package.models.Length; modelIndex++)
            {
                ModelData model = _package.models[modelIndex];
                if (model == null || model.verticesUnity == null || model.verticesUnity.Length == 0)
                {
                    continue;
                }

                string name = model.name ?? string.Empty;
                bool matches = false;
                for (int keywordIndex = 0; keywordIndex < keywords.Length; keywordIndex++)
                {
                    if (name.IndexOf(keywords[keywordIndex], StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        matches = true;
                        break;
                    }
                }

                if (!matches)
                {
                    continue;
                }

                Vector3 min;
                Vector3 max;
                if (!TryGetModelBounds(model, out min, out max))
                {
                    continue;
                }

                sum += (min + max) * 0.5f;
                count++;
            }

            if (count == 0)
            {
                return false;
            }

            center = sum / count;
            return true;
        }

        private static SpawnPlacement BuildSpawnPlacement(Vector3 spawnPosition, Vector3 lookTarget)
        {
            Vector3 adjustedPosition = spawnPosition + new Vector3(0f, 24f, 0f);
            Vector3 lookDirection = lookTarget - adjustedPosition;
            lookDirection.y = 0f;
            if (lookDirection.sqrMagnitude < 0.001f)
            {
                lookDirection = Vector3.forward;
            }

            Quaternion rotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
            return new SpawnPlacement(adjustedPosition, rotation);
        }

        private TextureAssetInfo GetBitmapTexture(string name)
        {
            TextureAssetInfo info;
            if (_bitmapTextures.TryGetValue(name, out info))
            {
                return info;
            }

            return new TextureAssetInfo
            {
                Width = 128,
                Height = 128,
                UvWidth = 128,
                UvHeight = 128,
                Texture = null,
                AssetPath = string.Empty,
            };
        }

        private TextureAssetInfo GetSpriteTexture(string name)
        {
            TextureAssetInfo info;
            if (_spriteTextures.TryGetValue(name, out info))
            {
                return info;
            }

            return new TextureAssetInfo
            {
                Width = 128,
                Height = 128,
                UvWidth = 128,
                UvHeight = 128,
                Texture = null,
                AssetPath = string.Empty,
            };
        }

        private Material GetBitmapMaterial(string textureName)
        {
            Material material;
            if (_bitmapMaterials.TryGetValue(textureName, out material))
            {
                return material;
            }

            TextureAssetInfo info = GetBitmapTexture(textureName);
            if (info.Texture == null)
            {
                if (_missingBitmapMaterial == null)
                {
                    _missingBitmapMaterial = CreateMaterial(
                        _materialsRoot + "/Bitmaps/missing_bitmap.mat",
                        Shader.Find("Standard") ?? Shader.Find("Diffuse"),
                        null,
                        new Color(1f, 0f, 1f),
                        false
                    );
                }
                return _missingBitmapMaterial;
            }

            material = CreateMaterial(
                _materialsRoot + "/Bitmaps/" + SanitizeFileName(textureName) + ".mat",
                Shader.Find("Standard") ?? Shader.Find("Diffuse"),
                info.Texture,
                Color.white,
                false,
                RequiresCutoutBitmap(textureName)
            );
            _bitmapMaterials[textureName] = material;
            return material;
        }

        private Material GetSpriteMaterial(string textureName)
        {
            Material material;
            if (_spriteMaterials.TryGetValue(textureName, out material))
            {
                return material;
            }

            TextureAssetInfo info = GetSpriteTexture(textureName);
            if (info.Texture == null)
            {
                if (_missingSpriteMaterial == null)
                {
                    _missingSpriteMaterial = CreateMaterial(
                        _materialsRoot + "/Sprites/missing_sprite.mat",
                        GetSpriteShader(),
                        null,
                        new Color(1f, 0f, 1f, 0.75f),
                        true
                    );
                }
                return _missingSpriteMaterial;
            }

            material = CreateMaterial(
                _materialsRoot + "/Sprites/" + SanitizeFileName(textureName) + ".mat",
                GetSpriteShader(),
                info.Texture,
                Color.white,
                true
            );
            _spriteMaterials[textureName] = material;
            return material;
        }

        private static Shader GetSpriteShader()
        {
            return AssetDatabase.LoadAssetAtPath<Shader>(SpriteShaderAssetPath)
                ?? Shader.Find("Sprites/Default")
                ?? Shader.Find("Unlit/Transparent")
                ?? Shader.Find("Standard");
        }

        private static bool RequiresCutoutBitmap(string textureName)
        {
            if (string.IsNullOrEmpty(textureName))
            {
                return false;
            }

            return textureName.StartsWith("wtrdr", StringComparison.OrdinalIgnoreCase)
                || textureName.StartsWith("hwtrdr", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWaterTransitionTexture(string textureName)
        {
            return RequiresCutoutBitmap(textureName);
        }

        private string ResolveWaterTerrainTextureName()
        {
            if (_package == null || _package.terrain == null || _package.terrain.tilesets == null)
            {
                return "WtrTyl";
            }

            for (int i = 0; i < _package.terrain.tilesets.Length; i++)
            {
                TerrainTilesetData tileset = _package.terrain.tilesets[i];
                if (tileset == null)
                {
                    continue;
                }

                if (string.Equals(tileset.groupName, "water", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(tileset.baseTexture))
                {
                    return tileset.baseTexture;
                }
            }

            return "WtrTyl";
        }
        private Material CreateMaterial(string assetPath, Shader shader, Texture texture, Color color, bool transparent, bool cutout = false)
        {
            Material material = new Material(shader);
            material.name = Path.GetFileNameWithoutExtension(assetPath);
            material.color = color;
            if (texture != null)
            {
                material.mainTexture = texture;
            }

            if (shader != null && shader.name == "Standard")
            {
                material.SetFloat("_Glossiness", 0f);
                if (cutout)
                {
                    material.SetFloat("_Mode", 1f);
                    material.SetInt("_SrcBlend", (int)BlendMode.One);
                    material.SetInt("_DstBlend", (int)BlendMode.Zero);
                    material.SetInt("_ZWrite", 1);
                    material.EnableKeyword("_ALPHATEST_ON");
                    material.DisableKeyword("_ALPHABLEND_ON");
                    material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    if (material.HasProperty("_Cutoff"))
                    {
                        material.SetFloat("_Cutoff", 0.5f);
                    }
                    material.renderQueue = (int)RenderQueue.AlphaTest;
                }
                else if (transparent)
                {
                    material.SetFloat("_Mode", 3f);
                    material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                    material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                    material.SetInt("_ZWrite", 0);
                    material.DisableKeyword("_ALPHATEST_ON");
                    material.EnableKeyword("_ALPHABLEND_ON");
                    material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    material.renderQueue = (int)RenderQueue.Transparent;
                }
            }
            else if ((transparent || cutout) && material.HasProperty("_Cutoff"))
            {
                material.SetFloat("_Cutoff", 0.5f);
                material.renderQueue = cutout ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Transparent;
            }

            return CreateOrReplaceAsset(assetPath, material);
        }
    }

    private static MeshBuilder GetOrCreateBuilder(Dictionary<string, MeshBuilder> builders, string textureName)
    {
        MeshBuilder builder;
        if (!builders.TryGetValue(textureName, out builder))
        {
            builder = new MeshBuilder();
            builders[textureName] = builder;
        }
        return builder;
    }

    private static Vector3 TerrainVertex(int x, int y, int gridSize, int cellSize, int heightScale, int[] heights)
    {
        int index = y * gridSize + x;
        float worldX = (64 - x) * cellSize;
        float worldY = heights[index] * heightScale;
        float worldZ = -(64 - y) * cellSize;
        return new Vector3(worldX, worldY, worldZ);
    }

    private static Vector3 ConvertNormal(int[] normal)
    {
        if (normal == null || normal.Length < 3)
        {
            return Vector3.up;
        }

        Vector3 value = new Vector3(
            -normal[0] / Mm6NormalScale,
            normal[2] / Mm6NormalScale,
            -normal[1] / Mm6NormalScale
        );
        return value.sqrMagnitude > 0.0001f ? value.normalized : Vector3.up;
    }

    private static TAsset CreateOrReplaceAsset<TAsset>(string assetPath, TAsset asset)
        where TAsset : UnityEngine.Object
    {
        if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null)
        {
            AssetDatabase.DeleteAsset(assetPath);
        }
        AssetDatabase.CreateAsset(asset, assetPath);
        return asset;
    }

    private static Mesh CreateBillboardQuad()
    {
        Mesh mesh = new Mesh();
        mesh.name = "MM6_BillboardQuad";
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, 0f, 0f),
            new Vector3(0.5f, 0f, 0f),
            new Vector3(-0.5f, 1f, 0f),
            new Vector3(0.5f, 1f, 0f),
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
        };
        mesh.normals = new[]
        {
            Vector3.forward,
            Vector3.forward,
            Vector3.forward,
            Vector3.forward,
        };
        // Keep the quad visible even if a camera tag is missing or a material
        // still uses backface culling.
        mesh.triangles = new[]
        {
            0, 2, 1,
            2, 3, 1,
            1, 2, 0,
            1, 3, 2,
        };
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh CreateDoubleSidedMesh(Mesh sourceMesh, string meshName)
    {
        Mesh mesh = new Mesh();
        mesh.name = meshName;

        if (sourceMesh == null)
        {
            return mesh;
        }

        mesh.vertices = sourceMesh.vertices;
        mesh.normals = sourceMesh.normals;
        mesh.uv = sourceMesh.uv;
        mesh.uv2 = sourceMesh.uv2;
        mesh.tangents = sourceMesh.tangents;
        mesh.colors = sourceMesh.colors;
        mesh.bounds = sourceMesh.bounds;
        mesh.subMeshCount = sourceMesh.subMeshCount;

        for (int subMeshIndex = 0; subMeshIndex < sourceMesh.subMeshCount; subMeshIndex++)
        {
            int[] sourceTriangles = sourceMesh.GetTriangles(subMeshIndex);
            int[] doubledTriangles = new int[sourceTriangles.Length * 2];
            Array.Copy(sourceTriangles, 0, doubledTriangles, 0, sourceTriangles.Length);

            for (int triangleIndex = 0; triangleIndex < sourceTriangles.Length; triangleIndex += 3)
            {
                int writeIndex = sourceTriangles.Length + triangleIndex;
                doubledTriangles[writeIndex] = sourceTriangles[triangleIndex];
                doubledTriangles[writeIndex + 1] = sourceTriangles[triangleIndex + 2];
                doubledTriangles[writeIndex + 2] = sourceTriangles[triangleIndex + 1];
            }

            mesh.SetTriangles(doubledTriangles, subMeshIndex);
        }

        mesh.RecalculateBounds();
        return mesh;
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

    private static int GetArrayValue(int[] values, int index, int fallback)
    {
        if (values == null || index < 0 || index >= values.Length)
        {
            return fallback;
        }
        return values[index];
    }

    private static string GetArrayValue(string[] values, int index, string fallback)
    {
        if (values == null || index < 0 || index >= values.Length || string.IsNullOrEmpty(values[index]))
        {
            return fallback;
        }
        return values[index];
    }

    [Serializable]
    private sealed class MapPackage
    {
        public string mapName;
        public TerrainSection terrain;
        public ModelData[] models;
        public DecorationData[] decorations;
        public TownspersonData[] townsfolk;
        public MonsterData[] monsters;
        public SpawnPointData[] spawnPoints;
        public EnvironmentSection environment;
        public TextureCollection textures;
    }

    [Serializable]
    private sealed class TerrainSection
    {
        public int gridSize;
        public int cellSize;
        public int heightScale;
        public int[] heights;
        public TerrainTilesetData[] tilesets;
        public string[] textureNames;
        public int[] cellTextureIndices;
        public bool approximateTexturing;
    }

    [Serializable]
    private sealed class TerrainTilesetData
    {
        public int group;
        public string groupName;
        public int offset;
        public string baseTexture;
    }

    [Serializable]
    private sealed class TextureCollection
    {
        public TextureEntry[] bitmaps;
        public TextureEntry[] sprites;
    }

    [Serializable]
    private sealed class TextureEntry
    {
        public string name;
        public string sourcePath;
        public int width;
        public int height;
        public int uvWidth;
        public int uvHeight;
        public bool found;
    }

    [Serializable]
    private sealed class ModelData
    {
        public string name;
        public Vec3Data[] verticesUnity;
        public FaceData[] faces;
    }

    [Serializable]
    private sealed class FaceData
    {
        public string textureName;
        public int[] normal;
        public int[] vertexIndices;
        public int[] uList;
        public int[] vList;
        public int bitmapU;
        public int bitmapV;
    }

    [Serializable]
    private sealed class DecorationData
    {
        public string name;
        public string textureInfoName;
        public Vec3Data positionUnity;
        public float width;
        public float height;
        public string[] animationTextureInfoNames;
        public float[] animationFrameDurationsSeconds;
        public float animationStartOffsetSeconds;
    }

    [Serializable]
    private sealed class TownspersonData
    {
        public string name;
        public string displayName;
        public int npcId;
        public int monsterId;
        public string textureInfoName;
        public Vec3Data positionUnity;
        public float width;
        public float height;
        public string[] animationTextureInfoNames;
        public float[] animationFrameDurationsSeconds;
        public float animationStartOffsetSeconds;
    }

    [Serializable]
    private sealed class SpawnPointData
    {
        public Vec3Data positionUnity;
        public int radius;
        public int kind;
        public int index;
        public int bits;
    }

    [Serializable]
    private sealed class EnvironmentSection
    {
        public string daySkyTextureName;
        public string alternateDaySkyTextureName;
        public string snowSkyTextureName;
        public float startHour;
        public float timeScaleHoursPerRealSecond;
        public int startDayOfMonth;
        public int startMonth;
        public bool fogEnabled;
        public float fogWeakDistance;
        public float fogStrongDistance;
        public bool allowSnow;
        public float sunPathYawDegrees;
        public int weatherSeed;
        public string weatherMode;
    }

    [Serializable]
    private sealed class MonsterData
    {
        public string name;
        public string displayName;
        public string textureInfoName;
        public Vec3Data positionUnity;
        public float width;
        public float height;
        public string[] animationTextureInfoNames;
        public float[] animationFrameDurationsSeconds;
        public float animationStartOffsetSeconds;
        public string idleTextureInfoName;
        public string[] idleAnimationTextureInfoNames;
        public float[] idleAnimationFrameDurationsSeconds;
        public float standingStateDurationSeconds;
        public string walkingTextureInfoName;
        public string[] walkingAnimationTextureInfoNames;
        public float[] walkingAnimationFrameDurationsSeconds;
        public float moveSpeed;
        public float activationDistance;
        public float loseInterestDistance;
        public float stopDistance;
    }

    private readonly struct ResolvedSpriteClip
    {
        public readonly Texture2D[] Frames;
        public readonly float[] Durations;

        public ResolvedSpriteClip(Texture2D[] frames, float[] durations)
        {
            Frames = frames ?? Array.Empty<Texture2D>();
            Durations = durations ?? Array.Empty<float>();
        }
    }

    [Serializable]
    private sealed class Vec3Data
    {
        public float x;
        public float y;
        public float z;

        public Vector3 ToVector3()
        {
            return new Vector3(x, y, z);
        }
    }

    private sealed class TextureAssetInfo
    {
        public Texture2D Texture;
        public string AssetPath;
        public int Width;
        public int Height;
        public int UvWidth;
        public int UvHeight;
    }

    private readonly struct SpawnPlacement
    {
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;

        public SpawnPlacement(Vector3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }
    }

    private sealed class MeshBuilder
    {
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<int> _triangles = new List<int>();

        public int VertexCount
        {
            get { return _vertices.Count; }
        }

        public void AddQuad(Vector3 nw, Vector3 ne, Vector3 sw, Vector3 se)
        {
            int start = _vertices.Count;
            _vertices.Add(nw);
            _vertices.Add(ne);
            _vertices.Add(sw);
            _vertices.Add(se);

            _uvs.Add(new Vector2(0f, 1f));
            _uvs.Add(new Vector2(1f, 1f));
            _uvs.Add(new Vector2(0f, 0f));
            _uvs.Add(new Vector2(1f, 0f));

            _triangles.Add(start + 0);
            _triangles.Add(start + 1);
            _triangles.Add(start + 3);
            _triangles.Add(start + 0);
            _triangles.Add(start + 3);
            _triangles.Add(start + 2);
        }

        public void AddPolygon(IList<Vector3> vertices, IList<Vector2> uvs, Vector3 normal)
        {
            if (vertices == null || uvs == null || vertices.Count < 3 || vertices.Count != uvs.Count)
            {
                return;
            }

            int start = _vertices.Count;
            for (int i = 0; i < vertices.Count; i++)
            {
                _vertices.Add(vertices[i]);
                _uvs.Add(uvs[i]);
                _normals.Add(normal);
            }

            for (int i = 1; i < vertices.Count - 1; i++)
            {
                _triangles.Add(start + 0);
                _triangles.Add(start + i + 1);
                _triangles.Add(start + i);
            }
        }

        public Mesh ToMesh(string name, bool recalculateNormals)
        {
            Mesh mesh = new Mesh();
            mesh.name = name;
            if (_vertices.Count > 65535)
            {
                mesh.indexFormat = IndexFormat.UInt32;
            }

            mesh.SetVertices(_vertices);
            mesh.SetUVs(0, _uvs);
            mesh.SetTriangles(_triangles, 0);

            if (recalculateNormals || _normals.Count != _vertices.Count)
            {
                mesh.RecalculateNormals();
            }
            else
            {
                mesh.SetNormals(_normals);
            }

            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
