using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class Mm6OutdoorEnvironmentController : MonoBehaviour
{
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int MainTexStId = Shader.PropertyToID("_MainTex_ST");
    private static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    public enum WeatherMode
    {
        Auto,
        Clear,
        LightFog,
        Foggy,
        DenseFog,
    }

    private enum ResolvedWeather
    {
        Clear,
        LightFog,
        Foggy,
        DenseFog,
    }

    public string mapName = "outdoor";
    public Camera targetCamera;
    public Transform skyAnchor;
    public Renderer skyRenderer;
    public Renderer sunDiscRenderer;
    public Renderer moonDiscRenderer;
    public Light sunLight;
    public Light moonLight;
    public ParticleSystem snowParticles;
    public Texture2D daySkyTexture;
    public Texture2D alternateDaySkyTexture;
    public Texture2D snowSkyTexture;
    public bool autoAdvanceTime = true;
    public float startHour = 12f;
    public float hoursPerRealSecond = 1f / 120f;
    public int startDayOfMonth = 1;
    public int startMonth = 6;
    public bool fogEnabled = false;
    public float fogWeakDistance = 0f;
    public float fogStrongDistance = 0f;
    public bool allowSnow = false;
    public float sunPathYawDegrees = -32f;
    public int weatherSeed = 1;
    public WeatherMode weatherMode = WeatherMode.Clear;
    public float celestialDistance = 90000f;
    public float sunDiscSize = 6000f;
    public float moonDiscSize = 4500f;

    private MaterialPropertyBlock _skyPropertyBlock;
    private MaterialPropertyBlock _sunDiscPropertyBlock;
    private MaterialPropertyBlock _moonDiscPropertyBlock;
    private float _currentHour;
    private int _currentDayOfMonth;
    private int _currentMonth;
    private int _lastAppliedMinute = -1;
    private int _cachedWeatherDayKey = int.MinValue;
    private ResolvedWeather _cachedWeather = ResolvedWeather.Clear;

    private void OnEnable()
    {
        ResetClock();
        EnsureSkyRendererState();
        ApplyEnvironment(force: true);
    }

    private void OnValidate()
    {
        ResetClock();
        EnsureSkyRendererState();
        ApplyEnvironment(force: true);
    }

    private void Update()
    {
        if (Application.isPlaying)
        {
            if (autoAdvanceTime)
            {
                AdvanceClock(Time.unscaledDeltaTime);
            }
        }
        else if (!Application.isPlaying)
        {
            ResetClock();
        }
    }

    private void LateUpdate()
    {
        ApplyEnvironment(force: false);
    }

    private void ResetClock()
    {
        _currentHour = Mathf.Repeat(startHour, 24f);
        _currentDayOfMonth = Mathf.Clamp(startDayOfMonth, 1, 28);
        _currentMonth = NormalizeMonth(startMonth);
        _lastAppliedMinute = -1;
        _cachedWeatherDayKey = int.MinValue;
    }

    private void AdvanceClock(float deltaTime)
    {
        if (deltaTime <= 0f || hoursPerRealSecond <= 0f)
        {
            return;
        }

        _currentHour += hoursPerRealSecond * deltaTime;
        while (_currentHour >= 24f)
        {
            _currentHour -= 24f;
            AdvanceCalendarDay();
        }
    }

    private void AdvanceCalendarDay()
    {
        _currentDayOfMonth++;
        if (_currentDayOfMonth > 28)
        {
            _currentDayOfMonth = 1;
            _currentMonth = NormalizeMonth(_currentMonth + 1);
        }

        _cachedWeatherDayKey = int.MinValue;
    }

    private void ApplyEnvironment(bool force)
    {
        Camera activeCamera = ResolveCamera();
        int minuteKey = Mathf.FloorToInt(_currentHour * 60f);
        if (!force && minuteKey == _lastAppliedMinute && activeCamera == null)
        {
            return;
        }

        Vector3 anchorPosition = activeCamera != null ? activeCamera.transform.position : Vector3.zero;
        if (skyAnchor != null)
        {
            skyAnchor.position = anchorPosition;
        }

        float daylight = EvaluateDaylightFactor(_currentHour);
        float timeFogFactor = EvaluateFogDensityByTime(_currentHour);
        float twilight = EvaluateTwilightFactor(_currentHour);
        ResolvedWeather weather = ResolveWeather();
        bool snowActive = ShouldRenderSnow();

        Vector3 sunDirection = EvaluateArcDirection(_currentHour, 5f, 21f);
        Vector3 moonDirection = EvaluateArcDirection(_currentHour, 21f, 5f);

        float sunStrength = Mathf.Clamp01(daylight);
        float moonStrength = Mathf.Clamp01(1f - daylight);
        moonStrength *= moonStrength;

        Color skyColor = EvaluateSkyColor(daylight, twilight, snowActive);
        Color ambientColor = EvaluateAmbientColor(daylight, twilight, snowActive);
        Color fogColor = EvaluateFogColor(daylight, twilight, snowActive);
        Color backgroundColor = EvaluateBackgroundColor(weather, skyColor, fogColor);

        ApplyLighting(sunDirection, moonDirection, sunStrength, moonStrength, twilight);
        ApplySky(activeCamera, skyColor, ChooseSkyTexture(snowActive));
        ApplyCelestialDisc(sunDiscRenderer, sunDirection, sunDiscSize, anchorPosition, new Color(1f, 0.92f, 0.55f, Mathf.Clamp01(0.2f + sunStrength)));
        ApplyCelestialDisc(moonDiscRenderer, moonDirection, moonDiscSize, anchorPosition, new Color(0.8f, 0.84f, 1f, Mathf.Clamp01(moonStrength)));
        ApplyFog(activeCamera, fogColor, weather, timeFogFactor, snowActive);
        ApplySnow(snowActive);

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = ambientColor;

        if (activeCamera != null)
        {
            activeCamera.backgroundColor = backgroundColor;
            activeCamera.clearFlags = CameraClearFlags.SolidColor;
        }

        _lastAppliedMinute = minuteKey;
    }

    private void ApplyLighting(
        Vector3 sunDirection,
        Vector3 moonDirection,
        float sunStrength,
        float moonStrength,
        float twilight
    )
    {
        if (sunLight != null)
        {
            sunLight.transform.rotation = Quaternion.LookRotation(-sunDirection, Vector3.up);
            sunLight.intensity = 1.15f * sunStrength;
            sunLight.color = Color.Lerp(new Color(1f, 0.66f, 0.42f), new Color(1f, 0.97f, 0.92f), sunStrength);
            sunLight.enabled = sunStrength > 0.001f;
        }

        if (moonLight != null)
        {
            moonLight.transform.rotation = Quaternion.LookRotation(-moonDirection, Vector3.up);
            moonLight.intensity = 0.22f * moonStrength;
            moonLight.color = Color.Lerp(new Color(0.7f, 0.78f, 1f), new Color(0.56f, 0.66f, 1f), twilight * 0.35f);
            moonLight.enabled = moonStrength > 0.001f;
        }
    }

    private void ApplySky(Camera activeCamera, Color tint, Texture2D skyTexture)
    {
        if (skyRenderer == null)
        {
            return;
        }

        EnsureSkyRendererState();

        if (_skyPropertyBlock == null)
        {
            _skyPropertyBlock = new MaterialPropertyBlock();
        }

        Vector2 offset = Vector2.zero;
        if (activeCamera != null)
        {
            float time = GetTimeSeconds();
            offset.x = activeCamera.transform.position.x * 0.00001f + time * 0.0025f;
            offset.y = activeCamera.transform.position.z * 0.00001f + time * 0.0018f;
        }

        skyRenderer.GetPropertyBlock(_skyPropertyBlock);
        _skyPropertyBlock.SetTexture(MainTexId, skyTexture);
        _skyPropertyBlock.SetTexture(BaseMapId, skyTexture);
        _skyPropertyBlock.SetColor(ColorId, tint);
        _skyPropertyBlock.SetColor(BaseColorId, tint);
        _skyPropertyBlock.SetVector(MainTexStId, new Vector4(1f, 1f, offset.x, offset.y));
        _skyPropertyBlock.SetVector(BaseMapStId, new Vector4(1f, 1f, offset.x, offset.y));
        skyRenderer.SetPropertyBlock(_skyPropertyBlock);
    }

    private void EnsureSkyRendererState()
    {
        if (skyRenderer == null)
        {
            return;
        }

        Material material = Application.isPlaying ? skyRenderer.material : skyRenderer.sharedMaterial;
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_Cull"))
        {
            material.SetInt("_Cull", (int)CullMode.Off);
        }
        if (material.HasProperty("_ZWrite"))
        {
            material.SetInt("_ZWrite", 0);
        }

        material.renderQueue = (int)RenderQueue.Background;
    }

    private void ApplyCelestialDisc(Renderer renderer, Vector3 direction, float size, Vector3 anchorPosition, Color color)
    {
        if (renderer == null)
        {
            return;
        }

        renderer.transform.position = anchorPosition + direction * celestialDistance;
        renderer.transform.localScale = Vector3.one * Mathf.Max(1f, size);
        renderer.enabled = color.a > 0.01f;

        MaterialPropertyBlock block = GetDiscPropertyBlock(renderer == sunDiscRenderer);
        renderer.GetPropertyBlock(block);
        block.SetColor(ColorId, color);
        block.SetColor(BaseColorId, color);
        renderer.SetPropertyBlock(block);
    }

    private void ApplyFog(
        Camera activeCamera,
        Color fogColor,
        ResolvedWeather weather,
        float timeFogFactor,
        bool snowActive
    )
    {
        if (!fogEnabled)
        {
            RenderSettings.fog = false;
            return;
        }

        float farClip = activeCamera != null ? activeCamera.farClipPlane : 250000f;
        float fogStart = farClip * 0.72f;
        float fogEnd = farClip * 0.95f;

        switch (weather)
        {
            case ResolvedWeather.LightFog:
                fogStart = Mathf.Max(256f, fogWeakDistance);
                fogEnd = Mathf.Max(fogStart + 256f, fogStrongDistance);
                break;
            case ResolvedWeather.Foggy:
                fogStart = 0f;
                fogEnd = Mathf.Max(1024f, fogStrongDistance);
                break;
            case ResolvedWeather.DenseFog:
                fogStart = 0f;
                fogEnd = Mathf.Max(768f, fogStrongDistance * 0.5f);
                break;
        }

        if (snowActive)
        {
            fogStart *= 0.82f;
            fogEnd *= 0.82f;
        }

        fogStart *= Mathf.Lerp(1f, 0.6f, timeFogFactor * 0.8f);
        fogEnd *= Mathf.Lerp(1f, 0.72f, timeFogFactor * 0.6f);
        fogEnd = Mathf.Max(fogStart + 256f, fogEnd);

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogStartDistance = fogStart;
        RenderSettings.fogEndDistance = fogEnd;
    }

    private void ApplySnow(bool snowActive)
    {
        if (snowParticles == null)
        {
            return;
        }

        var emission = snowParticles.emission;
        emission.enabled = snowActive;

        if (!Application.isPlaying)
        {
            if (snowParticles.isPlaying)
            {
                snowParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            return;
        }

        if (snowActive)
        {
            if (!snowParticles.isPlaying)
            {
                snowParticles.Play(true);
            }
        }
        else if (snowParticles.isPlaying)
        {
            snowParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    private Texture2D ChooseSkyTexture(bool snowActive)
    {
        if (snowActive && snowSkyTexture != null)
        {
            return snowSkyTexture;
        }

        if (alternateDaySkyTexture != null)
        {
            int dayKey = _currentMonth * 32 + _currentDayOfMonth;
            if ((DeterministicSeed(dayKey) & 1) == 1)
            {
                return alternateDaySkyTexture;
            }
        }

        return daySkyTexture != null ? daySkyTexture : snowSkyTexture;
    }

    private ResolvedWeather ResolveWeather()
    {
        if (weatherMode != WeatherMode.Auto)
        {
            return ConvertWeather(weatherMode);
        }

        int dayKey = _currentMonth * 32 + _currentDayOfMonth;
        if (dayKey == _cachedWeatherDayKey)
        {
            return _cachedWeather;
        }

        _cachedWeatherDayKey = dayKey;
        int seed = DeterministicSeed(dayKey);
        float value = (seed & 0x7FFFFFFF) / (float)int.MaxValue;

        if (value < 0.1f)
        {
            _cachedWeather = ResolvedWeather.DenseFog;
        }
        else if (value < 0.24f)
        {
            _cachedWeather = ResolvedWeather.Foggy;
        }
        else if (value < 0.4f)
        {
            _cachedWeather = ResolvedWeather.LightFog;
        }
        else
        {
            _cachedWeather = ResolvedWeather.Clear;
        }

        return _cachedWeather;
    }

    private bool ShouldRenderSnow()
    {
        return allowSnow &&
            snowSkyTexture != null &&
            IsWinterMonth(_currentMonth) &&
            (_currentDayOfMonth % 3) == 0;
    }

    private int DeterministicSeed(int dayKey)
    {
        int seed = weatherSeed != 0 ? weatherSeed : 1;
        seed ^= dayKey * 374761393;
        seed = (seed << 13) ^ seed;
        return seed * (seed * seed * 15731 + 789221) + 1376312589;
    }

    private static ResolvedWeather ConvertWeather(WeatherMode weather)
    {
        switch (weather)
        {
            case WeatherMode.LightFog:
                return ResolvedWeather.LightFog;
            case WeatherMode.Foggy:
                return ResolvedWeather.Foggy;
            case WeatherMode.DenseFog:
                return ResolvedWeather.DenseFog;
            default:
                return ResolvedWeather.Clear;
        }
    }

    private static float EvaluateDaylightFactor(float hour)
    {
        if (hour < 5f || hour >= 21f)
        {
            return 0f;
        }
        if (hour < 6f)
        {
            return hour - 5f;
        }
        if (hour < 20f)
        {
            return 1f;
        }
        return 1f - (hour - 20f);
    }

    private static float EvaluateFogDensityByTime(float hour)
    {
        if (hour < 5f || hour >= 21f)
        {
            return 1f;
        }
        if (hour < 6f)
        {
            return 1f - (hour - 5f);
        }
        if (hour < 20f)
        {
            return 0f;
        }
        return hour - 20f;
    }

    private static float EvaluateTwilightFactor(float hour)
    {
        if (hour >= 5f && hour < 6f)
        {
            float t = hour - 5f;
            return 1f - Mathf.Abs(t - 0.5f) / 0.5f;
        }

        if (hour >= 20f && hour < 21f)
        {
            float t = hour - 20f;
            return 1f - Mathf.Abs(t - 0.5f) / 0.5f;
        }

        return 0f;
    }

    private Vector3 EvaluateArcDirection(float hour, float startHourValue, float endHourValue)
    {
        float wrappedHour = hour;
        float wrappedEnd = endHourValue;
        if (wrappedEnd <= startHourValue)
        {
            wrappedEnd += 24f;
            if (wrappedHour < startHourValue)
            {
                wrappedHour += 24f;
            }
        }

        float t = Mathf.InverseLerp(startHourValue, wrappedEnd, wrappedHour);
        float angle = Mathf.Lerp(0f, Mathf.PI, t);
        Vector3 direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
        return Quaternion.Euler(0f, sunPathYawDegrees, 0f) * direction.normalized;
    }

    private static Color EvaluateSkyColor(float daylight, float twilight, bool snowActive)
    {
        Color night = new Color(0.09f, 0.12f, 0.22f);
        Color day = new Color(0.8f, 0.88f, 1f);
        Color dusk = new Color(1f, 0.58f, 0.38f);
        Color value = Color.Lerp(night, day, daylight);
        value = Color.Lerp(value, dusk, twilight * 0.55f);
        if (snowActive)
        {
            value = Color.Lerp(value, new Color(0.9f, 0.94f, 1f), 0.25f);
        }
        return value;
    }

    private static Color EvaluateAmbientColor(float daylight, float twilight, bool snowActive)
    {
        Color night = new Color(0.11f, 0.13f, 0.19f);
        Color day = new Color(0.6f, 0.62f, 0.67f);
        Color dusk = new Color(0.48f, 0.3f, 0.22f);
        Color value = Color.Lerp(night, day, daylight);
        value = Color.Lerp(value, dusk, twilight * 0.3f);
        if (snowActive)
        {
            value = Color.Lerp(value, new Color(0.7f, 0.74f, 0.8f), 0.2f);
        }
        return value;
    }

    private static Color EvaluateFogColor(float daylight, float twilight, bool snowActive)
    {
        Color night = new Color(0.05f, 0.06f, 0.1f);
        Color day = new Color(0.69f, 0.75f, 0.84f);
        Color dusk = new Color(0.8f, 0.46f, 0.34f);
        Color value = Color.Lerp(night, day, daylight);
        value = Color.Lerp(value, dusk, twilight * 0.35f);
        if (snowActive)
        {
            value = Color.Lerp(value, new Color(0.83f, 0.88f, 0.95f), 0.28f);
        }
        return value;
    }

    private static Color EvaluateBackgroundColor(ResolvedWeather weather, Color skyColor, Color fogColor)
    {
        switch (weather)
        {
            case ResolvedWeather.Clear:
                return skyColor;
            case ResolvedWeather.LightFog:
                return Color.Lerp(skyColor, fogColor, 0.35f);
            case ResolvedWeather.Foggy:
                return Color.Lerp(skyColor, fogColor, 0.7f);
            default:
                return fogColor;
        }
    }

    private MaterialPropertyBlock GetDiscPropertyBlock(bool sun)
    {
        if (sun)
        {
            if (_sunDiscPropertyBlock == null)
            {
                _sunDiscPropertyBlock = new MaterialPropertyBlock();
            }
            return _sunDiscPropertyBlock;
        }

        if (_moonDiscPropertyBlock == null)
        {
            _moonDiscPropertyBlock = new MaterialPropertyBlock();
        }
        return _moonDiscPropertyBlock;
    }

    private Camera ResolveCamera()
    {
        if (targetCamera != null && targetCamera.isActiveAndEnabled)
        {
            return targetCamera;
        }

#if UNITY_EDITOR
        if (!Application.isPlaying && SceneView.lastActiveSceneView != null)
        {
            return SceneView.lastActiveSceneView.camera;
        }
#endif

        if (Camera.main != null && Camera.main.isActiveAndEnabled)
        {
            return Camera.main;
        }

        Camera[] cameras = Camera.allCameras;
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera candidate = cameras[i];
            if (candidate != null && candidate.isActiveAndEnabled)
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool IsWinterMonth(int month)
    {
        int value = NormalizeMonth(month);
        return value == 11 || value == 0 || value == 1;
    }

    private static int NormalizeMonth(int month)
    {
        int value = month % 12;
        if (value < 0)
        {
            value += 12;
        }
        return value;
    }

    private static float GetTimeSeconds()
    {
        if (Application.isPlaying)
        {
            return Time.unscaledTime;
        }

#if UNITY_EDITOR
        return (float)EditorApplication.timeSinceStartup;
#else
        return Time.realtimeSinceStartup;
#endif
    }
}
