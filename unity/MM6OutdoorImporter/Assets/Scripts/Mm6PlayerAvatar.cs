using UnityEngine;

[DisallowMultipleComponent]
public sealed class Mm6PlayerAvatar : MonoBehaviour
{
    public Camera viewCamera;
    public Transform headTransform;

    public Transform EffectiveTransform
    {
        get { return headTransform != null ? headTransform : transform; }
    }
}
