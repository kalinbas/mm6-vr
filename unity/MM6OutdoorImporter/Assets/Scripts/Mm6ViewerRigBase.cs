using UnityEngine;

public abstract class Mm6ViewerRigBase : MonoBehaviour
{
    public abstract Camera ViewCamera { get; }
    public abstract Transform PlayerTransform { get; }
    public abstract bool IsVr { get; }

    public abstract void SetExplorationEnabled(bool enabled);
    public abstract void PlaceAt(Vector3 position, Vector3 forward);
    public abstract int ConsumeMenuStepDelta();
    public abstract bool ConsumeMenuConfirmRequested();
    public abstract bool ConsumeReturnToMenuRequested();
}
