using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class Mm6ViewerApp : MonoBehaviour
{
    private const int MaxVisibleMenuRows = 8;
    private const float MenuListRowSpacing = 15f;

    public static bool IsViewerRuntimeActive { get; private set; }

    public Mm6ViewerCatalog catalog;
    public float menuDistance = 220f;
    public float menuVerticalOffset = -18f;
    public Color menuBackgroundColor = new Color(0.05f, 0.08f, 0.12f, 0.92f);
    public Color menuPanelColor = new Color(0.09f, 0.13f, 0.19f, 0.96f);
    public Color menuRowColor = new Color(0.12f, 0.17f, 0.24f, 0.98f);
    public Color menuRowSelectedColor = new Color(0.18f, 0.47f, 0.68f, 0.98f);
    public Color menuTextMutedColor = new Color(0.74f, 0.82f, 0.9f, 1f);
    public Color menuAccentColor = new Color(0.45f, 0.86f, 1f, 1f);
    public Color menuStatusColor = new Color(1f, 0.92f, 0.72f, 1f);
    public AudioClip soundtrackClip;
    [Range(0f, 1f)] public float soundtrackVolume = 0.55f;
    public bool autoLoadFirstMap = true;
    public bool allowReturnToMapSelector = false;

    private Mm6ViewerRigBase _rig;
    private AudioSource _soundtrackSource;
    private Transform _menuRoot;
    private MeshRenderer _menuBackground;
    private Transform _menuListRoot;
    private TextMesh _titleText;
    private TextMesh _modeText;
    private TextMesh _detailText;
    private TextMesh _hintText;
    private TextMesh _statusText;
    private TextMesh _scrollHintText;
    private readonly List<Mm6ViewerCatalog.MapEntry> _maps = new List<Mm6ViewerCatalog.MapEntry>();
    private readonly List<MenuRowVisual> _menuRows = new List<MenuRowVisual>();
    private Scene _loadedMapScene;
    private bool _hasLoadedMapScene;
    private bool _busy;
    private bool _autoLoadStarted;
    private int _selectedMapIndex;
    private string _statusMessage = string.Empty;

    private sealed class MenuRowVisual
    {
        public Transform root;
        public MeshRenderer backgroundRenderer;
        public TextMesh nameText;
        public TextMesh metaText;
    }

    private void Awake()
    {
        IsViewerRuntimeActive = true;
        LoadCatalog();
        if (!autoLoadFirstMap)
        {
            CreateMenuBoard();
        }
        CreateSoundtrackSource();
    }

    private IEnumerator Start()
    {
        yield return null;
        CreateRig();
        StartSoundtrack();
        ReturnRigToMenuPose();

        if (autoLoadFirstMap && _maps.Count > 0)
        {
            _autoLoadStarted = true;
            StartCoroutine(LoadSelectedMapRoutine(_maps[_selectedMapIndex]));
        }
    }

    private void OnDestroy()
    {
        IsViewerRuntimeActive = false;
    }

    private void Update()
    {
        if (_rig == null)
        {
            return;
        }

        UpdateMenuBoardTransform();

        if (_hasLoadedMapScene)
        {
            if (allowReturnToMapSelector && _rig.ConsumeReturnToMenuRequested())
            {
                StartCoroutine(ReturnToMenuRoutine());
            }
        }
        else if (!autoLoadFirstMap)
        {
            int step = _rig.ConsumeMenuStepDelta();
            if (step != 0 && _maps.Count > 0)
            {
                _selectedMapIndex = (_selectedMapIndex + step + _maps.Count) % _maps.Count;
            }

            if (_rig.ConsumeMenuConfirmRequested() && !_busy)
            {
                if (_maps.Count == 0)
                {
                    _statusMessage = "No imported scenes are in the viewer catalog yet.";
                }
                else
                {
                    StartCoroutine(LoadSelectedMapRoutine(_maps[_selectedMapIndex]));
                }
            }
        }
        else if (!_autoLoadStarted && !_busy && _maps.Count > 0)
        {
            _autoLoadStarted = true;
            StartCoroutine(LoadSelectedMapRoutine(_maps[_selectedMapIndex]));
        }

        UpdateMenuBoardContent();
    }

    private void LoadCatalog()
    {
        if (catalog == null)
        {
            catalog = Resources.Load<Mm6ViewerCatalog>("Mm6ViewerCatalog");
        }

        _maps.Clear();
        if (catalog != null && catalog.maps != null)
        {
            for (int i = 0; i < catalog.maps.Length; i++)
            {
                Mm6ViewerCatalog.MapEntry entry = catalog.maps[i];
                if (entry != null && !string.IsNullOrEmpty(entry.sceneName))
                {
                    _maps.Add(entry);
                }
            }
        }

        _maps.Sort((a, b) => string.Compare(a.mapName, b.mapName, System.StringComparison.OrdinalIgnoreCase));
        _selectedMapIndex = Mathf.Clamp(_selectedMapIndex, 0, Mathf.Max(0, _maps.Count - 1));
    }

    private void CreateRig()
    {
        bool preferVr = Mm6ViewerVrRig.IsVrRuntimeAvailable();
        GameObject rigObject = new GameObject(preferVr ? "VrRig" : "DesktopRig");
        rigObject.transform.SetParent(transform, false);

        if (preferVr)
        {
            _rig = rigObject.AddComponent<Mm6ViewerVrRig>();
        }
        else
        {
            _rig = rigObject.AddComponent<Mm6ViewerDesktopRig>();
        }

        _rig.SetExplorationEnabled(false);
    }

    private void CreateSoundtrackSource()
    {
        if (soundtrackClip == null)
        {
            return;
        }

        GameObject soundtrackObject = new GameObject("Soundtrack");
        soundtrackObject.transform.SetParent(transform, false);

        _soundtrackSource = soundtrackObject.AddComponent<AudioSource>();
        _soundtrackSource.clip = soundtrackClip;
        _soundtrackSource.loop = true;
        _soundtrackSource.playOnAwake = false;
        _soundtrackSource.spatialBlend = 0f;
        _soundtrackSource.volume = soundtrackVolume;
        _soundtrackSource.ignoreListenerPause = true;
    }

    private void StartSoundtrack()
    {
        if (_soundtrackSource == null || _soundtrackSource.clip == null || _soundtrackSource.isPlaying)
        {
            return;
        }

        _soundtrackSource.Play();
    }

    private void CreateMenuBoard()
    {
        _menuRoot = new GameObject("MenuBoard").transform;
        _menuRoot.SetParent(transform, false);

        _menuBackground = CreateQuad(
            "Background",
            _menuRoot,
            Vector3.zero,
            new Vector3(270f, 152f, 1f),
            menuBackgroundColor
        );

        CreateQuad(
            "ListPanel",
            _menuRoot,
            new Vector3(-61f, 3f, -0.5f),
            new Vector3(122f, 122f, 1f),
            menuPanelColor
        );

        CreateQuad(
            "DetailPanel",
            _menuRoot,
            new Vector3(60f, 3f, -0.5f),
            new Vector3(112f, 122f, 1f),
            menuPanelColor
        );

        _titleText = CreateText(
            "Title",
            _menuRoot,
            new Vector3(-127f, 68f, -1f),
            TextAnchor.UpperLeft,
            TextAlignment.Left,
            56,
            3.3f,
            Color.white
        );

        _modeText = CreateText(
            "Mode",
            _menuRoot,
            new Vector3(-127f, 56f, -1f),
            TextAnchor.UpperLeft,
            TextAlignment.Left,
            34,
            2.1f,
            menuAccentColor
        );

        _scrollHintText = CreateText(
            "ScrollHint",
            _menuRoot,
            new Vector3(-118f, 44f, -1f),
            TextAnchor.UpperLeft,
            TextAlignment.Left,
            26,
            1.55f,
            menuTextMutedColor
        );

        _menuListRoot = new GameObject("MapList").transform;
        _menuListRoot.SetParent(_menuRoot, false);
        _menuListRoot.localPosition = new Vector3(-61f, 30f, -1f);

        _detailText = CreateText(
            "Detail",
            _menuRoot,
            new Vector3(10f, 53f, -1f),
            TextAnchor.UpperLeft,
            TextAlignment.Left,
            36,
            2.2f,
            Color.white
        );

        _statusText = CreateText(
            "Status",
            _menuRoot,
            new Vector3(10f, -38f, -1f),
            TextAnchor.UpperLeft,
            TextAlignment.Left,
            28,
            1.85f,
            menuStatusColor
        );

        _hintText = CreateText(
            "Hints",
            _menuRoot,
            new Vector3(-127f, -58f, -1f),
            TextAnchor.UpperLeft,
            TextAlignment.Left,
            28,
            1.75f,
            menuTextMutedColor
        );

        _titleText.text = "MM6 Viewer";
        _menuRoot.gameObject.SetActive(!autoLoadFirstMap);
    }

    private void UpdateMenuBoardTransform()
    {
        if (_menuRoot == null || _rig == null || _rig.ViewCamera == null)
        {
            return;
        }

        bool visible = !autoLoadFirstMap && !_hasLoadedMapScene;
        _menuRoot.gameObject.SetActive(visible);
        if (!visible)
        {
            return;
        }

        Transform cameraTransform = _rig.ViewCamera.transform;
        Vector3 forward = cameraTransform.forward;
        if (_rig.IsVr)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.forward;
            }
            forward.Normalize();
        }

        Vector3 position = cameraTransform.position + forward * menuDistance + Vector3.up * menuVerticalOffset;
        _menuRoot.position = position;
        _menuRoot.rotation = Quaternion.LookRotation(_menuRoot.position - cameraTransform.position, Vector3.up);
    }

    private void UpdateMenuBoardContent()
    {
        if (_titleText == null)
        {
            return;
        }

        _titleText.text = "MM6 Viewer";
        _modeText.text = _rig != null && _rig.IsVr
            ? "Quest / VR Mode"
            : "Desktop Mode";

        _hintText.text = BuildHintText();
        _statusText.text = string.IsNullOrEmpty(_statusMessage)
            ? "Highlight a map, then confirm to load it."
            : _statusMessage;

        if (_maps.Count == 0)
        {
            EnsureMenuRowCount(0);
            _scrollHintText.text = "No imported maps are in the viewer catalog yet.";
            _detailText.text =
                "No Maps Found\n\n" +
                "Import one or more outdoor or indoor scenes first,\n" +
                "then run the viewer setup again so they appear here.";
            return;
        }

        _selectedMapIndex = Mathf.Clamp(_selectedMapIndex, 0, _maps.Count - 1);

        int visibleCount = Mathf.Min(MaxVisibleMenuRows, _maps.Count);
        int firstVisibleIndex = Mathf.Clamp(
            _selectedMapIndex - (visibleCount / 2),
            0,
            Mathf.Max(0, _maps.Count - visibleCount)
        );

        EnsureMenuRowCount(visibleCount);
        for (int i = 0; i < _menuRows.Count; i++)
        {
            MenuRowVisual row = _menuRows[i];
            if (i >= visibleCount)
            {
                row.root.gameObject.SetActive(false);
                continue;
            }

            int mapIndex = firstVisibleIndex + i;
            Mm6ViewerCatalog.MapEntry map = _maps[mapIndex];
            bool selected = mapIndex == _selectedMapIndex;

            row.root.gameObject.SetActive(true);
            row.nameText.text = map.mapName;
            row.metaText.text = BuildRowMetaText(map, mapIndex);
            ApplyRowStyle(row, selected);
        }

        bool hasMoreAbove = firstVisibleIndex > 0;
        bool hasMoreBelow = firstVisibleIndex + visibleCount < _maps.Count;
        if (hasMoreAbove && hasMoreBelow)
        {
            _scrollHintText.text = "More maps above and below";
        }
        else if (hasMoreAbove)
        {
            _scrollHintText.text = "More maps above";
        }
        else if (hasMoreBelow)
        {
            _scrollHintText.text = "More maps below";
        }
        else
        {
            _scrollHintText.text = _maps.Count + " map" + (_maps.Count == 1 ? string.Empty : "s") + " available";
        }

        _detailText.text = BuildDetailText(_maps[_selectedMapIndex]);
    }

    private IEnumerator LoadSelectedMapRoutine(Mm6ViewerCatalog.MapEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.sceneName))
        {
            yield break;
        }

        _busy = true;
        _statusMessage = "Loading " + entry.mapName + "...";
        yield return null;

        if (_hasLoadedMapScene)
        {
            yield return UnloadCurrentMapScene();
        }

        AsyncOperation loadOperation = SceneManager.LoadSceneAsync(entry.sceneName, LoadSceneMode.Additive);
        if (loadOperation == null)
        {
            _statusMessage = "Failed to start loading scene " + entry.sceneName + ".";
            _busy = false;
            yield break;
        }

        while (!loadOperation.isDone)
        {
            yield return null;
        }

        Scene scene = SceneManager.GetSceneByName(entry.sceneName);
        if (!scene.IsValid() || !scene.isLoaded)
        {
            _statusMessage = "Scene " + entry.sceneName + " did not finish loading.";
            _busy = false;
            yield break;
        }

        _loadedMapScene = scene;
        _hasLoadedMapScene = true;
        PrepareLoadedScene(scene, entry);
        _rig.SetExplorationEnabled(true);
        _statusMessage = string.Empty;
        _busy = false;
    }

    private IEnumerator ReturnToMenuRoutine()
    {
        if (_busy)
        {
            yield break;
        }

        _busy = true;
        _rig.SetExplorationEnabled(false);
        _statusMessage = "Returning to menu...";

        yield return UnloadCurrentMapScene();

        ReturnRigToMenuPose();
        _statusMessage = string.Empty;
        _busy = false;
    }

    private IEnumerator UnloadCurrentMapScene()
    {
        if (!_hasLoadedMapScene || !_loadedMapScene.IsValid() || !_loadedMapScene.isLoaded)
        {
            _hasLoadedMapScene = false;
            yield break;
        }

        AsyncOperation unloadOperation = SceneManager.UnloadSceneAsync(_loadedMapScene);
        if (unloadOperation != null)
        {
            while (!unloadOperation.isDone)
            {
                yield return null;
            }
        }

        _hasLoadedMapScene = false;
    }

    private void PrepareLoadedScene(Scene scene, Mm6ViewerCatalog.MapEntry entry)
    {
        Mm6ImportedMapScene marker = FindSceneMarker(scene);
        if (marker != null)
        {
            marker.PrepareForViewer(_rig.ViewCamera);
            _rig.PlaceAt(marker.ResolveSpawnPosition(), marker.ResolveSpawnForward());
            return;
        }

        DisableStandalonePlayers(scene);
        _rig.PlaceAt(Vector3.zero, Vector3.forward);

        Mm6OutdoorEnvironmentController environment = FindSceneObject<Mm6OutdoorEnvironmentController>(scene);
        if (environment != null)
        {
            environment.targetCamera = _rig.ViewCamera;
        }

        _statusMessage = "Loaded " + entry.mapName + " with fallback spawn data.";
    }

    private void ReturnRigToMenuPose()
    {
        if (_rig == null)
        {
            return;
        }

        _rig.PlaceAt(Vector3.zero, Vector3.forward);
        _rig.SetExplorationEnabled(false);
    }

    private static Mm6ImportedMapScene FindSceneMarker(Scene scene)
    {
        Mm6ImportedMapScene[] markers = Resources.FindObjectsOfTypeAll<Mm6ImportedMapScene>();

        for (int i = 0; i < markers.Length; i++)
        {
            Mm6ImportedMapScene marker = markers[i];
            if (marker != null && marker.gameObject.scene == scene)
            {
                return marker;
            }
        }

        return null;
    }

    private static T FindSceneObject<T>(Scene scene) where T : Component
    {
        T[] components = Resources.FindObjectsOfTypeAll<T>();
        for (int i = 0; i < components.Length; i++)
        {
            T component = components[i];
            if (component != null && component.gameObject.scene == scene)
            {
                return component;
            }
        }

        return null;
    }

    private static void DisableStandalonePlayers(Scene scene)
    {
        Mm6FirstPersonController[] players = Resources.FindObjectsOfTypeAll<Mm6FirstPersonController>();

        for (int i = 0; i < players.Length; i++)
        {
            Mm6FirstPersonController player = players[i];
            if (player != null && player.gameObject.scene == scene)
            {
                player.gameObject.SetActive(false);
            }
        }
    }

    private void EnsureMenuRowCount(int count)
    {
        while (_menuRows.Count < count)
        {
            _menuRows.Add(CreateMenuRowVisual(_menuRows.Count));
        }

        for (int i = 0; i < _menuRows.Count; i++)
        {
            _menuRows[i].root.gameObject.SetActive(i < count);
        }
    }

    private MenuRowVisual CreateMenuRowVisual(int index)
    {
        MenuRowVisual row = new MenuRowVisual();
        row.root = new GameObject("Row" + index).transform;
        row.root.SetParent(_menuListRoot, false);
        row.root.localPosition = new Vector3(0f, -index * MenuListRowSpacing, 0f);

        row.backgroundRenderer = CreateQuad(
            "Background",
            row.root,
            Vector3.zero,
            new Vector3(112f, 13.5f, 1f),
            menuRowColor
        );

        row.nameText = CreateText(
            "Name",
            row.root,
            new Vector3(-53f, 2.5f, -1f),
            TextAnchor.MiddleLeft,
            TextAlignment.Left,
            34,
            1.9f,
            Color.white
        );

        row.metaText = CreateText(
            "Meta",
            row.root,
            new Vector3(-53f, -3.2f, -1f),
            TextAnchor.MiddleLeft,
            TextAlignment.Left,
            24,
            1.35f,
            menuTextMutedColor
        );

        return row;
    }

    private void ApplyRowStyle(MenuRowVisual row, bool selected)
    {
        if (row == null)
        {
            return;
        }

        if (row.backgroundRenderer != null && row.backgroundRenderer.sharedMaterial != null)
        {
            row.backgroundRenderer.sharedMaterial.color = selected
                ? menuRowSelectedColor
                : menuRowColor;
        }

        if (row.nameText != null)
        {
            row.nameText.color = selected ? Color.white : new Color(0.92f, 0.96f, 1f, 1f);
        }

        if (row.metaText != null)
        {
            row.metaText.color = selected ? new Color(0.92f, 0.98f, 1f, 1f) : menuTextMutedColor;
        }
    }

    private string BuildRowMetaText(Mm6ViewerCatalog.MapEntry map, int mapIndex)
    {
        string typeLabel = string.IsNullOrEmpty(map.mapType) ? "map" : map.mapType;
        return "#" + (mapIndex + 1) + "  " + typeLabel + "  " + map.sceneName;
    }

    private string BuildDetailText(Mm6ViewerCatalog.MapEntry map)
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        builder.AppendLine("Selected Map");
        builder.AppendLine();
        builder.AppendLine(map.mapName);
        builder.AppendLine();
        builder.Append("Type: ");
        builder.AppendLine(string.IsNullOrEmpty(map.mapType) ? "unknown" : map.mapType);
        builder.Append("Scene: ");
        builder.AppendLine(map.sceneName);
        builder.Append("Catalog Position: ");
        builder.Append(_selectedMapIndex + 1);
        builder.Append(" / ");
        builder.AppendLine(_maps.Count.ToString());
        builder.AppendLine();
        builder.AppendLine("Load this scene to start exploring.");
        builder.AppendLine("When a map is active, return here to switch to another one.");
        return builder.ToString();
    }

    private string BuildHintText()
    {
        if (_rig != null && _rig.IsVr)
        {
            if (autoLoadFirstMap)
            {
                return
                    "In Map\n" +
                    "Left stick: move\n" +
                    "Left secondary: jump\n" +
                    "Right trigger: teleport\n" +
                    "Left primary: toggle stick movement\n" +
                    "Right stick: snap turn";
            }

            return
                "Map Selector\n" +
                "Left or right stick up/down: choose map\n" +
                "Right trigger or primary button: load map\n" +
                "Secondary button: return from map\n\n" +
                "In Map\n" +
                "Left stick: move\n" +
                "Left secondary: jump\n" +
                "Right trigger: teleport\n" +
                "Left primary: toggle stick movement\n" +
                "Right stick: snap turn";
        }

        if (autoLoadFirstMap)
        {
            return
                "In Map\n" +
                "WASD and mouse: move\n" +
                "Left/Right: turn\n" +
                "Space: jump\n" +
                "Esc: unlock cursor";
        }

        return
            "Map Selector\n" +
            "W/S or Up/Down: choose map\n" +
            "Enter or Space: load map\n\n" +
            "In Map\n" +
            "WASD and mouse: move\n" +
            "Tab: return to selector";
    }

    private static MeshRenderer CreateQuad(
        string name,
        Transform parent,
        Vector3 localPosition,
        Vector3 localScale,
        Color color
    )
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        quad.transform.SetParent(parent, false);
        quad.transform.localPosition = localPosition;
        quad.transform.localRotation = Quaternion.identity;
        quad.transform.localScale = localScale;

        Collider collider = quad.GetComponent<Collider>();
        if (collider != null)
        {
            UnityEngine.Object.Destroy(collider);
        }

        MeshRenderer renderer = quad.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            Shader shader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Standard");
            Material material = new Material(shader);
            material.color = color;
            renderer.sharedMaterial = material;
        }

        return renderer;
    }

    private static TextMesh CreateText(
        string name,
        Transform parent,
        Vector3 localPosition,
        TextAnchor anchor,
        TextAlignment alignment,
        int fontSize,
        float characterSize,
        Color color
    )
    {
        GameObject textObject = new GameObject(name);
        textObject.transform.SetParent(parent, false);
        textObject.transform.localPosition = localPosition;
        textObject.transform.localRotation = Quaternion.identity;

        TextMesh textMesh = textObject.AddComponent<TextMesh>();
        textMesh.anchor = anchor;
        textMesh.alignment = alignment;
        textMesh.fontSize = fontSize;
        textMesh.characterSize = characterSize;
        textMesh.color = color;
        textMesh.text = string.Empty;
        return textMesh;
    }
}
