using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public sealed class Mm6ViewerDesktopRig : Mm6ViewerRigBase
{
    private CharacterController _controller;
    private Mm6FirstPersonController _fpsController;
    private Camera _viewCamera;
    private Transform _lookPivot;
    private bool _explorationEnabled;

    public override Camera ViewCamera
    {
        get { return _viewCamera; }
    }

    public override Transform PlayerTransform
    {
        get { return transform; }
    }

    public override bool IsVr
    {
        get { return false; }
    }

    private void Awake()
    {
        EnsureRig();
        SetExplorationEnabled(false);
    }

    public override void SetExplorationEnabled(bool enabled)
    {
        _explorationEnabled = enabled;
        if (_fpsController != null)
        {
            _fpsController.enabled = enabled;
        }

        if (!enabled)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    public override void PlaceAt(Vector3 position, Vector3 forward)
    {
        Vector3 flatForward = forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 0.001f)
        {
            flatForward = Vector3.forward;
        }

        transform.position = position;
        transform.rotation = Quaternion.LookRotation(flatForward.normalized, Vector3.up);
        if (_fpsController != null)
        {
            _fpsController.ResetLook();
        }
    }

    public override int ConsumeMenuStepDelta()
    {
        if (_explorationEnabled)
        {
            return 0;
        }

        if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W))
        {
            return -1;
        }

        if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S))
        {
            return 1;
        }

        return 0;
    }

    public override bool ConsumeMenuConfirmRequested()
    {
        if (_explorationEnabled)
        {
            return false;
        }

        return Input.GetKeyDown(KeyCode.Return)
            || Input.GetKeyDown(KeyCode.KeypadEnter)
            || Input.GetKeyDown(KeyCode.Space);
    }

    public override bool ConsumeReturnToMenuRequested()
    {
        if (!_explorationEnabled)
        {
            return false;
        }

        return Input.GetKeyDown(KeyCode.Tab);
    }

    private void EnsureRig()
    {
        _controller = GetComponent<CharacterController>();
        _controller.height = 180f;
        _controller.radius = 28f;
        _controller.center = new Vector3(0f, _controller.height * 0.5f, 0f);
        _controller.stepOffset = 35f;
        _controller.skinWidth = 4f;
        _controller.minMoveDistance = 0f;

        _lookPivot = EnsureChild("CameraPivot", transform);
        _lookPivot.localPosition = new Vector3(0f, 150f, 0f);
        _lookPivot.localRotation = Quaternion.identity;

        Transform cameraTransform = EnsureChild("ViewerCamera", _lookPivot);
        cameraTransform.localPosition = Vector3.zero;
        cameraTransform.localRotation = Quaternion.identity;
        cameraTransform.tag = "MainCamera";

        _viewCamera = cameraTransform.GetComponent<Camera>();
        if (_viewCamera == null)
        {
            _viewCamera = cameraTransform.gameObject.AddComponent<Camera>();
        }
        _viewCamera.nearClipPlane = 1f;
        _viewCamera.farClipPlane = 1000000f;
        _viewCamera.fieldOfView = 75f;
        _viewCamera.clearFlags = CameraClearFlags.SolidColor;
        _viewCamera.backgroundColor = new Color(0.05f, 0.06f, 0.1f);

        if (cameraTransform.GetComponent<AudioListener>() == null)
        {
            cameraTransform.gameObject.AddComponent<AudioListener>();
        }

        _fpsController = GetComponent<Mm6FirstPersonController>();
        if (_fpsController == null)
        {
            _fpsController = gameObject.AddComponent<Mm6FirstPersonController>();
        }
        _fpsController.lookPivot = _lookPivot;

        Mm6PlayerAvatar avatar = GetComponent<Mm6PlayerAvatar>();
        if (avatar == null)
        {
            avatar = gameObject.AddComponent<Mm6PlayerAvatar>();
        }
        avatar.viewCamera = _viewCamera;
        avatar.headTransform = cameraTransform;
    }

    private static Transform EnsureChild(string childName, Transform parent)
    {
        Transform child = parent.Find(childName);
        if (child != null)
        {
            return child;
        }

        GameObject go = new GameObject(childName);
        go.transform.SetParent(parent, false);
        return go.transform;
    }
}
