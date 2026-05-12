using UnityEngine;

[DisallowMultipleComponent]
public sealed class Mm6ImportedMapScene : MonoBehaviour
{
    public string mapName;
    public string mapType;
    public Vector3 localSpawnPosition;
    public Vector3 localSpawnForward = Vector3.forward;

    private void Awake()
    {
        if (Mm6ViewerApp.IsViewerRuntimeActive)
        {
            SetStandalonePlayersActive(false);
        }
    }

    public Vector3 ResolveSpawnPosition()
    {
        return transform.TransformPoint(localSpawnPosition);
    }

    public Vector3 ResolveSpawnForward()
    {
        Vector3 worldForward = transform.TransformDirection(localSpawnForward);
        worldForward.y = 0f;
        if (worldForward.sqrMagnitude < 0.001f)
        {
            return Vector3.forward;
        }

        return worldForward.normalized;
    }

    public void PrepareForViewer(Camera targetCamera)
    {
        SetStandalonePlayersActive(false);
        RetargetEnvironment(targetCamera);
    }

    private void SetStandalonePlayersActive(bool active)
    {
        Mm6FirstPersonController[] controllers = GetComponentsInChildren<Mm6FirstPersonController>(true);
        for (int i = 0; i < controllers.Length; i++)
        {
            Mm6FirstPersonController controller = controllers[i];
            if (controller != null && controller.gameObject != null)
            {
                controller.gameObject.SetActive(active);
            }
        }
    }

    private void RetargetEnvironment(Camera targetCamera)
    {
        if (targetCamera == null)
        {
            return;
        }

        Mm6OutdoorEnvironmentController[] environments =
            GetComponentsInChildren<Mm6OutdoorEnvironmentController>(true);
        for (int i = 0; i < environments.Length; i++)
        {
            Mm6OutdoorEnvironmentController environment = environments[i];
            if (environment != null)
            {
                environment.targetCamera = targetCamera;
            }
        }
    }
}
