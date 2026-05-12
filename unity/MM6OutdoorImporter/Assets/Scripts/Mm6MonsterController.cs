using UnityEngine;

[DisallowMultipleComponent]
public sealed class Mm6MonsterController : MonoBehaviour
{
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

    public Renderer targetRenderer;
    public Texture2D[] standingFrames;
    public float[] standingFrameDurationsSeconds;
    public Texture2D[] idleFrames;
    public float[] idleFrameDurationsSeconds;
    public float standingStateDurationSeconds = 3f;
    public Texture2D[] walkingFrames;
    public float[] walkingFrameDurationsSeconds;
    public float animationStartOffsetSeconds;
    public float moveSpeed = 0f;
    public float activationDistance = 0f;
    public float loseInterestDistance = 0f;
    public float stopDistance = 0f;

    private MaterialPropertyBlock _propertyBlock;
    private Texture2D _lastTexture;

    private void OnEnable()
    {
        _lastTexture = null;
        ApplyCurrentFrame(force: true);
    }

    private void OnValidate()
    {
        _lastTexture = null;
        ApplyCurrentFrame(force: true);
    }

    private void Update()
    {
        ApplyCurrentFrame(force: false);
    }

    private void ApplyCurrentFrame(bool force)
    {
        Renderer renderer = targetRenderer != null ? targetRenderer : GetComponent<Renderer>();
        ClipSelection selection = EvaluateCurrentClip();
        Texture2D[] frames = selection.Frames;
        if (renderer == null || frames == null || frames.Length == 0)
        {
            return;
        }

        int frameIndex = EvaluateFrameIndex(frames, selection.Durations, selection.ElapsedSeconds, selection.Repeat);
        Texture2D texture = frames[Mathf.Clamp(frameIndex, 0, frames.Length - 1)];
        if (texture == null)
        {
            return;
        }

        if (!force && texture == _lastTexture)
        {
            return;
        }

        if (_propertyBlock == null)
        {
            _propertyBlock = new MaterialPropertyBlock();
        }

        renderer.GetPropertyBlock(_propertyBlock);
        _propertyBlock.SetTexture(MainTexId, texture);
        _propertyBlock.SetTexture(BaseMapId, texture);
        renderer.SetPropertyBlock(_propertyBlock);
        _lastTexture = texture;
    }

    private ClipSelection EvaluateCurrentClip()
    {
        Texture2D[] resolvedStandingFrames = standingFrames != null && standingFrames.Length > 0
            ? standingFrames
            : walkingFrames;
        float[] resolvedStandingDurations = standingFrames != null && standingFrames.Length > 0
            ? standingFrameDurationsSeconds
            : walkingFrameDurationsSeconds;

        if (resolvedStandingFrames == null || resolvedStandingFrames.Length == 0)
        {
            return default;
        }

        Texture2D[] resolvedIdleFrames = idleFrames != null && idleFrames.Length > 0
            ? idleFrames
            : null;
        float[] resolvedIdleDurations = idleFrames != null && idleFrames.Length > 0
            ? idleFrameDurationsSeconds
            : null;

        float elapsed = GetTimeSeconds() + Mathf.Max(0f, animationStartOffsetSeconds);
        if (resolvedIdleFrames == null || resolvedIdleFrames.Length == 0)
        {
            return new ClipSelection(resolvedStandingFrames, resolvedStandingDurations, elapsed, true);
        }

        float idleDuration = CalculateClipDuration(resolvedIdleFrames, resolvedIdleDurations);
        if (idleDuration <= 0.01f)
        {
            return new ClipSelection(resolvedStandingFrames, resolvedStandingDurations, elapsed, true);
        }

        float standDuration = Mathf.Max(0.25f, standingStateDurationSeconds);
        float cycleDuration = standDuration + idleDuration;
        float cycleTime = Mathf.Repeat(elapsed, cycleDuration);
        if (cycleTime < standDuration)
        {
            return new ClipSelection(resolvedStandingFrames, resolvedStandingDurations, cycleTime, true);
        }

        return new ClipSelection(resolvedIdleFrames, resolvedIdleDurations, cycleTime - standDuration, false);
    }

    private int EvaluateFrameIndex(Texture2D[] frames, float[] durations, float elapsedSeconds, bool repeat)
    {
        if (frames == null || frames.Length <= 1)
        {
            return 0;
        }

        float totalDuration = 0f;
        for (int i = 0; i < frames.Length; i++)
        {
            totalDuration += GetFrameDuration(durations, i);
        }

        if (totalDuration <= 0.01f)
        {
            return 0;
        }

        float elapsed = repeat
            ? Mathf.Repeat(elapsedSeconds, totalDuration)
            : Mathf.Clamp(elapsedSeconds, 0f, totalDuration - 0.0001f);
        for (int i = 0; i < frames.Length; i++)
        {
            float duration = GetFrameDuration(durations, i);
            if (elapsed < duration)
            {
                return i;
            }
            elapsed -= duration;
        }

        return frames.Length - 1;
    }

    private static float CalculateClipDuration(Texture2D[] frames, float[] durations)
    {
        if (frames == null || frames.Length == 0)
        {
            return 0f;
        }

        float totalDuration = 0f;
        for (int i = 0; i < frames.Length; i++)
        {
            totalDuration += GetFrameDuration(durations, i);
        }
        return totalDuration;
    }

    private static float GetFrameDuration(float[] durations, int index)
    {
        if (durations == null || index < 0 || index >= durations.Length)
        {
            return 0.125f;
        }

        return Mathf.Max(0.01f, durations[index]);
    }

    private static float GetTimeSeconds()
    {
        return Application.isPlaying ? Time.unscaledTime : Time.realtimeSinceStartup;
    }

    private readonly struct ClipSelection
    {
        public readonly Texture2D[] Frames;
        public readonly float[] Durations;
        public readonly float ElapsedSeconds;
        public readonly bool Repeat;

        public ClipSelection(Texture2D[] frames, float[] durations, float elapsedSeconds, bool repeat)
        {
            Frames = frames;
            Durations = durations;
            ElapsedSeconds = elapsedSeconds;
            Repeat = repeat;
        }
    }
}
