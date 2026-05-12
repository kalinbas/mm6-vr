using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class Mm6SpriteAnimator : MonoBehaviour
{
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

    public Renderer targetRenderer;
    public Texture2D[] frames;
    public float[] frameDurationsSeconds;
    public float startOffsetSeconds;

    private MaterialPropertyBlock _propertyBlock;
    private int _lastFrameIndex = -1;

    private void OnEnable()
    {
        _lastFrameIndex = -1;
        ApplyFrame(force: true);
    }

    private void OnValidate()
    {
        _lastFrameIndex = -1;
        ApplyFrame(force: true);
    }

    private void Update()
    {
        ApplyFrame(force: false);
    }

    private void ApplyFrame(bool force)
    {
        Renderer renderer = targetRenderer != null ? targetRenderer : GetComponent<Renderer>();
        if (renderer == null || frames == null || frames.Length == 0)
        {
            return;
        }

        int frameIndex = EvaluateFrameIndex();
        if (!force && frameIndex == _lastFrameIndex)
        {
            return;
        }

        Texture2D texture = frames[Mathf.Clamp(frameIndex, 0, frames.Length - 1)];
        if (texture == null)
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
        _lastFrameIndex = frameIndex;
    }

    private int EvaluateFrameIndex()
    {
        if (frames == null || frames.Length <= 1)
        {
            return 0;
        }

        float totalDuration = 0f;
        for (int i = 0; i < frames.Length; i++)
        {
            totalDuration += GetFrameDuration(i);
        }

        if (totalDuration <= 0.01f)
        {
            return 0;
        }

        float time = Mathf.Repeat(GetTimeSeconds() + Mathf.Max(0f, startOffsetSeconds), totalDuration);
        for (int i = 0; i < frames.Length; i++)
        {
            float duration = GetFrameDuration(i);
            if (time < duration)
            {
                return i;
            }
            time -= duration;
        }

        return frames.Length - 1;
    }

    private float GetFrameDuration(int index)
    {
        if (frameDurationsSeconds == null || index < 0 || index >= frameDurationsSeconds.Length)
        {
            return 0.125f;
        }

        return Mathf.Max(0.01f, frameDurationsSeconds[index]);
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
