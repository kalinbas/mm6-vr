using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public sealed class Mm6Billboard : MonoBehaviour
{
    private void LateUpdate()
    {
        Camera activeCamera = ResolveCamera();

        if (activeCamera == null)
        {
            return;
        }

        FaceCamera(activeCamera);
    }

    private void FaceCamera(Camera activeCamera)
    {
        if (activeCamera == null)
        {
            return;
        }

#if UNITY_EDITOR
        if (!Application.isPlaying && SceneView.lastActiveSceneView != null)
        {
            activeCamera = SceneView.lastActiveSceneView.camera;
        }
#endif

        if (activeCamera == null)
        {
            return;
        }

        // Keep billboards upright in Unity and rotate only around the vertical
        // axis. MM6 uses Z-up coordinates; in this Unity importer that maps to
        // yaw around world Y instead of following camera pitch/roll.
        Vector3 toCamera = activeCamera.transform.position - transform.position;
        toCamera.y = 0f;

        if (toCamera.sqrMagnitude < 0.0001f)
        {
            toCamera = activeCamera.transform.forward;
            toCamera.y = 0f;
        }

        if (toCamera.sqrMagnitude < 0.0001f)
        {
            return;
        }

        transform.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
    }

    private static Camera ResolveCamera()
    {
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
}
