using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class Mm6IndoorImporter
{
    private const string ImportRoot = "Assets/MM6Imported";
    private const string BundledPackagesRoot = "MM6IndoorPackages";
    private const string SpriteShaderAssetPath = "Assets/Shaders/MM6BillboardCutout.shader";

    [MenuItem("Tools/MM6/Import Indoor Map Package...")]
    public static void ImportIndoorMapPackage()
    {
        string packageFolder = EditorUtility.OpenFolderPanel(
            "Select an exported MM6 indoor map package",
            string.Empty,
            string.Empty
        );
        if (string.IsNullOrEmpty(packageFolder))
        {
            return;
        }

        ImportPackageFolder(packageFolder);
    }

    [MenuItem("Tools/MM6/Import Bundled Indoor Packages In Project")]
    public static void ImportBundledIndoorPackagesInProject()
    {
        string packagesRoot = Path.Combine(Directory.GetCurrentDirectory(), BundledPackagesRoot);
        if (!Directory.Exists(packagesRoot))
        {
            EditorUtility.DisplayDialog(
                "MM6 Indoor Import",
                "No bundled indoor package folder was found at " + packagesRoot,
                "OK"
            );
            return;
        }

        string[] jsonPaths = Directory.GetFiles(packagesRoot, "map.json", SearchOption.AllDirectories);
        Array.Sort(jsonPaths, StringComparer.OrdinalIgnoreCase);
        if (jsonPaths.Length == 0)
        {
            EditorUtility.DisplayDialog(
                "MM6 Indoor Import",
                "No bundled indoor map.json files were found under " + packagesRoot,
                "OK"
            );
            return;
        }

        foreach (string jsonPath in jsonPaths)
        {
            string packageFolder = Path.GetDirectoryName(jsonPath);
            if (!string.IsNullOrEmpty(packageFolder))
            {
                ImportPackageFolder(packageFolder);
            }
        }
    }

    private static void ImportPackageFolder(string packageFolder)
    {
        string jsonPath = Path.Combine(packageFolder, "map.json");
        if (!File.Exists(jsonPath))
        {
            EditorUtility.DisplayDialog(
                "MM6 Indoor Import",
                "The selected folder does not contain map.json.",
                "OK"
            );
            return;
        }

        string json = File.ReadAllText(jsonPath);
        MapPackage package = JsonUtility.FromJson<MapPackage>(json);
        if (package == null || string.IsNullOrEmpty(package.mapName))
        {
            EditorUtility.DisplayDialog(
                "MM6 Indoor Import",
                "Failed to parse map.json.",
                "OK"
            );
            return;
        }

        if (!string.IsNullOrEmpty(package.mapType) &&
            !string.Equals(package.mapType, "indoor", StringComparison.OrdinalIgnoreCase))
        {
            EditorUtility.DisplayDialog(
                "MM6 Indoor Import",
                "This package is not marked as an indoor map.",
                "OK"
            );
            return;
        }

        ImportContext context = new ImportContext(packageFolder, package);
        context.Import();
    }

    private sealed class ImportContext
    {
        private readonly string _packageFolder;
        private readonly MapPackage _package;
        private readonly string _assetRoot;
        private readonly string _texturesRoot;
        private readonly string _materialsRoot;
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
            _meshesRoot = _assetRoot + "/Meshes";
            _scenesRoot = _assetRoot + "/Scenes";
        }

        public void Import()
        {
            EnsureFolder(ImportRoot);
            EnsureFolder(_assetRoot);
            EnsureFolder(_texturesRoot);
            EnsureFolder(_texturesRoot + "/Bitmaps");
            EnsureFolder(_texturesRoot + "/Sprites");
            EnsureFolder(_materialsRoot);
            EnsureFolder(_materialsRoot + "/Bitmaps");
            EnsureFolder(_materialsRoot + "/Sprites");
            EnsureFolder(_meshesRoot);
            EnsureFolder(_meshesRoot + "/Geometry");
            EnsureFolder(_meshesRoot + "/Billboards");
            EnsureFolder(_scenesRoot);

            ImportTextureGroup(_package.textures != null ? _package.textures.bitmaps : null, false);
            ImportTextureGroup(_package.textures != null ? _package.textures.sprites : null, true);

            _billboardQuad = CreateOrReplaceAsset(
                _meshesRoot + "/Billboards/billboard_quad.asset",
                CreateBillboardQuad()
            );

            string scenePath = _scenesRoot + "/" + SanitizeFileName(_package.mapName) + ".unity";
            var previousActiveScene = EditorSceneManager.GetActiveScene();
            var existingScene = EditorSceneManager.GetSceneByPath(scenePath);
            if (existingScene.IsValid() && existingScene.isLoaded)
            {
                EditorSceneManager.CloseScene(existingScene, true);
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SetActiveScene(scene);
            SpawnPlacement spawn = DetermineSpawnPlacement();

            GameObject root = new GameObject(_package.mapName);
            GameObject geometryRoot = new GameObject("Geometry");
            GameObject decorationsRoot = new GameObject("Decorations");
            GameObject monstersRoot = new GameObject("Monsters");
            GameObject lightsRoot = new GameObject("Lights");
            geometryRoot.transform.SetParent(root.transform, false);
            decorationsRoot.transform.SetParent(root.transform, false);
            monstersRoot.transform.SetParent(root.transform, false);
            lightsRoot.transform.SetParent(root.transform, false);

            CreateLighting(root.transform);
            BuildGeometry(geometryRoot.transform);
            Physics.SyncTransforms();
            BuildDecorations(decorationsRoot.transform);
            BuildMonsters(monstersRoot.transform);
            BuildLights(lightsRoot.transform);
            CreatePlayer(root.transform, spawn);
            ConfigureImportedMapRoot(root, spawn);

            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Mm6ViewerSetup.RefreshGeneratedViewerAssets();

            if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
            {
                EditorSceneManager.SetActiveScene(previousActiveScene);
            }

            EditorUtility.DisplayDialog(
                "MM6 Indoor Import",
                "Imported " + _package.mapName + " to " + scenePath,
                "OK"
            );
        }

        private void CreateLighting(Transform root)
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.18f, 0.18f, 0.2f);

            GameObject lightObject = new GameObject("Preview Light");
            lightObject.transform.SetParent(root, false);
            lightObject.transform.rotation = Quaternion.Euler(52f, -28f, 0f);

            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.35f;
            light.shadows = LightShadows.None;
        }

        private void ImportTextureGroup(TextureEntry[] entries, bool sprite)
        {
            if (entries == null)
            {
                return;
            }

            string targetFolder = _texturesRoot + (sprite ? "/Sprites" : "/Bitmaps");
            Dictionary<string, TextureAssetInfo> destination = sprite ? _spriteTextures : _bitmapTextures;

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

        private void BuildGeometry(Transform parent)
        {
            if (_package.faces == null || _package.verticesUnity == null)
            {
                return;
            }

            Dictionary<int, Dictionary<string, MeshBuilder>> buildersBySector = new Dictionary<int, Dictionary<string, MeshBuilder>>();

            for (int faceIndex = 0; faceIndex < _package.faces.Length; faceIndex++)
            {
                FaceData face = _package.faces[faceIndex];
                if (!ShouldRenderFace(face))
                {
                    continue;
                }

                int sectorId = Mathf.Max(0, face.sectorId);
                Dictionary<string, MeshBuilder> sectorBuilders;
                if (!buildersBySector.TryGetValue(sectorId, out sectorBuilders))
                {
                    sectorBuilders = new Dictionary<string, MeshBuilder>(StringComparer.OrdinalIgnoreCase);
                    buildersBySector[sectorId] = sectorBuilders;
                }

                string textureName = string.IsNullOrEmpty(face.textureInfoName) ? face.textureName : face.textureInfoName;
                if (string.IsNullOrEmpty(textureName))
                {
                    textureName = "MissingTexture";
                }

                MeshBuilder builder = GetOrCreateBuilder(sectorBuilders, textureName);
                TextureAssetInfo textureInfo = GetBitmapTexture(textureName);
                float textureWidth = Mathf.Max(1f, textureInfo.UvWidth > 0 ? textureInfo.UvWidth : textureInfo.Width);
                float textureHeight = Mathf.Max(1f, textureInfo.UvHeight > 0 ? textureInfo.UvHeight : textureInfo.Height);
                Vector3 normal = face.normal != null ? face.normal.ToVector3() : Vector3.up;
                if (normal.sqrMagnitude < 0.0001f)
                {
                    normal = Vector3.up;
                }
                else
                {
                    normal.Normalize();
                }

                List<Vector3> polygonVertices = new List<Vector3>(face.vertexIndices.Length);
                List<Vector2> polygonUvs = new List<Vector2>(face.vertexIndices.Length);

                bool validFace = true;
                for (int i = 0; i < face.vertexIndices.Length; i++)
                {
                    int vertexIndex = face.vertexIndices[i];
                    if (vertexIndex < 0 || vertexIndex >= _package.verticesUnity.Length)
                    {
                        validFace = false;
                        break;
                    }

                    polygonVertices.Add(_package.verticesUnity[vertexIndex].ToVector3());
                    float u = (GetArrayValue(face.uList, i, 0) + face.textureDeltaU) / textureWidth;
                    float v = (GetArrayValue(face.vList, i, 0) + face.textureDeltaV) / textureHeight;
                    polygonUvs.Add(new Vector2(u, v));
                }

                if (validFace)
                {
                    builder.AddPolygon(polygonVertices, polygonUvs, normal);
                }
            }

            foreach (KeyValuePair<int, Dictionary<string, MeshBuilder>> sectorPair in buildersBySector)
            {
                GameObject sectorRoot = new GameObject("Sector_" + sectorPair.Key.ToString("D3"));
                sectorRoot.transform.SetParent(parent, false);

                foreach (KeyValuePair<string, MeshBuilder> meshPair in sectorPair.Value)
                {
                    if (meshPair.Value.VertexCount == 0)
                    {
                        continue;
                    }

                    string safeTextureName = SanitizeFileName(meshPair.Key);
                    Mesh mesh = meshPair.Value.ToMesh(
                        "sector_" + sectorPair.Key.ToString("D3") + "_" + safeTextureName,
                        recalculateNormals: false
                    );
                    mesh = CreateOrReplaceAsset(
                        _meshesRoot + "/Geometry/" + sectorPair.Key.ToString("D3") + "_" + safeTextureName + ".asset",
                        mesh
                    );

                    GameObject section = new GameObject(meshPair.Key);
                    section.transform.SetParent(sectorRoot.transform, false);
                    MeshFilter filter = section.AddComponent<MeshFilter>();
                    MeshRenderer renderer = section.AddComponent<MeshRenderer>();
                    filter.sharedMesh = mesh;
                    renderer.sharedMaterial = GetBitmapMaterial(meshPair.Key);
                    MeshCollider collider = section.AddComponent<MeshCollider>();
                    collider.sharedMesh = mesh;
                }
            }
        }

        private bool ShouldRenderFace(FaceData face)
        {
            if (face == null || face.vertexIndices == null || face.vertexIndices.Length < 3)
            {
                return false;
            }

            if (face.flags != null)
            {
                if (face.flags.isPortal || face.flags.isInvisible)
                {
                    return false;
                }
            }

            return !string.IsNullOrEmpty(face.textureName) || !string.IsNullOrEmpty(face.textureInfoName);
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
                go.transform.position = monster.positionUnity.ToVector3();
                go.transform.localScale = new Vector3(Mathf.Max(1f, monster.width), Mathf.Max(1f, monster.height), 1f);

                MeshFilter filter = go.AddComponent<MeshFilter>();
                MeshRenderer renderer = go.AddComponent<MeshRenderer>();
                filter.sharedMesh = _billboardQuad;
                renderer.sharedMaterial = GetSpriteMaterial(monster.textureInfoName);
                go.AddComponent<Mm6Billboard>();
                ConfigureMonsterBehavior(go, renderer, monster);
            }
        }

        private void BuildLights(Transform parent)
        {
            if (_package.lights == null)
            {
                return;
            }

            for (int i = 0; i < _package.lights.Length; i++)
            {
                LightData lightData = _package.lights[i];
                if (lightData == null || lightData.positionUnity == null || lightData.color == null)
                {
                    continue;
                }

                GameObject go = new GameObject("Light_" + i.ToString("D3"));
                go.transform.SetParent(parent, false);
                go.transform.position = lightData.positionUnity.ToVector3();

                Light light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = Mathf.Max(128f, lightData.radius);
                light.intensity = Mathf.Max(0.25f, lightData.brightness / 24f);
                light.color = lightData.color.ToColor();
                light.shadows = LightShadows.None;
            }
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

            Mm6MonsterController controller = target.AddComponent<Mm6MonsterController>();
            controller.targetRenderer = renderer;
            controller.standingFrames = standingClip.Frames;
            controller.standingFrameDurationsSeconds = standingClip.Durations;
            controller.idleFrames = idleClip.Frames.Length > 0 ? idleClip.Frames : standingClip.Frames;
            controller.idleFrameDurationsSeconds = idleClip.Durations.Length > 0
                ? idleClip.Durations
                : standingClip.Durations;
            controller.standingStateDurationSeconds = Mathf.Max(0.25f, monster.standingStateDurationSeconds);
            controller.animationStartOffsetSeconds = monster.animationStartOffsetSeconds;
            controller.moveSpeed = 0f;
            controller.activationDistance = 0f;
            controller.loseInterestDistance = 0f;
            controller.stopDistance = 0f;
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
            marker.mapType = string.IsNullOrEmpty(_package.mapType) ? "indoor" : _package.mapType;
            marker.localSpawnPosition = spawn.Position;
            marker.localSpawnForward = spawn.Rotation * Vector3.forward;
        }

        private void CreatePlayer(Transform parent, SpawnPlacement spawn)
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
            camera.farClipPlane = 60000f;
            camera.fieldOfView = 75f;

            cameraObject.AddComponent<AudioListener>();

            Mm6FirstPersonController fps = player.AddComponent<Mm6FirstPersonController>();
            fps.lookPivot = cameraPivot.transform;

            Mm6PlayerAvatar avatar = player.AddComponent<Mm6PlayerAvatar>();
            avatar.viewCamera = camera;
            avatar.headTransform = cameraObject.transform;
        }

        private SpawnPlacement DetermineSpawnPlacement()
        {
            Vector3 fallbackPosition = new Vector3(0f, 128f, 0f);
            Vector3 lookDirection = Vector3.forward;

            if (_package.playerStart != null && _package.playerStart.positionUnity != null)
            {
                fallbackPosition = _package.playerStart.positionUnity.ToVector3();
                if (_package.playerStart.forwardUnity != null)
                {
                    lookDirection = _package.playerStart.forwardUnity.ToVector3();
                }
            }
            else if (_package.spawnPoints != null && _package.spawnPoints.Length > 0 && _package.spawnPoints[0] != null && _package.spawnPoints[0].positionUnity != null)
            {
                fallbackPosition = _package.spawnPoints[0].positionUnity.ToVector3();
            }

            fallbackPosition.y += 8f;

            lookDirection.y = 0f;
            if (lookDirection.sqrMagnitude < 0.0001f)
            {
                lookDirection = Vector3.forward;
            }

            return new SpawnPlacement(
                fallbackPosition,
                Quaternion.LookRotation(lookDirection.normalized, Vector3.up)
            );
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
                false
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

        private Material CreateMaterial(string assetPath, Shader shader, Texture texture, Color color, bool transparent)
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
                if (transparent)
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
            else if (transparent && material.HasProperty("_Cutoff"))
            {
                material.SetFloat("_Cutoff", 0.5f);
                material.renderQueue = (int)RenderQueue.AlphaTest;
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

    [Serializable]
    private sealed class MapPackage
    {
        public string mapType;
        public string mapName;
        public Vec3Data[] verticesUnity;
        public FaceData[] faces;
        public SectorData[] sectors;
        public DecorationData[] decorations;
        public MonsterData[] monsters;
        public LightData[] lights;
        public SpawnPointData[] spawnPoints;
        public PlayerStartData playerStart;
        public TextureCollection textures;
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
    private sealed class FaceData
    {
        public int index;
        public string textureName;
        public string textureInfoName;
        public int sectorId;
        public int backSectorId;
        public int polygonType;
        public int attributes;
        public FaceFlags flags;
        public Vec3Data normal;
        public int[] vertexIndices;
        public int[] uList;
        public int[] vList;
        public int textureDeltaU;
        public int textureDeltaV;
    }

    [Serializable]
    private sealed class SectorData
    {
        public int index;
        public int[] faceIds;
        public int[] floorIds;
        public int[] wallIds;
        public int[] ceilingIds;
        public int[] portalIds;
        public int[] decorationIds;
        public int[] lightIds;
    }

    [Serializable]
    private sealed class FaceFlags
    {
        public bool isPortal;
        public bool isFluid;
        public bool isInvisible;
        public bool isAnimated;
        public bool isIndoorSky;
        public bool isClickable;
        public bool isEthereal;
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
        public string idleTextureInfoName;
        public string[] idleAnimationTextureInfoNames;
        public float[] idleAnimationFrameDurationsSeconds;
        public float animationStartOffsetSeconds;
        public float standingStateDurationSeconds;
    }

    [Serializable]
    private sealed class LightData
    {
        public Vec3Data positionUnity;
        public float radius;
        public ColorData color;
        public float brightness;
    }

    [Serializable]
    private sealed class SpawnPointData
    {
        public Vec3Data positionUnity;
        public int radius;
        public int kind;
        public int index;
        public int bits;
        public int group;
    }

    [Serializable]
    private sealed class PlayerStartData
    {
        public string source;
        public int faceId;
        public int sectorId;
        public int spawnPointIndex;
        public Vec3Data positionUnity;
        public Vec3Data forwardUnity;
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

    [Serializable]
    private sealed class ColorData
    {
        public float r;
        public float g;
        public float b;

        public Color ToColor()
        {
            return new Color(
                Mathf.Clamp01(r / 255f),
                Mathf.Clamp01(g / 255f),
                Mathf.Clamp01(b / 255f),
                1f
            );
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
                _triangles.Add(start + i);
                _triangles.Add(start + i + 1);
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
