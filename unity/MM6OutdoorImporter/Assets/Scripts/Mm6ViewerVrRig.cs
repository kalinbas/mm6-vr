using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public sealed class Mm6ViewerVrRig : Mm6ViewerRigBase
{
    private const float WorldUnitsPerMeter = 100f;
    private const float MovementToggleDebounceSeconds = 0.75f;

    public float moveSpeed = 420f;
    public float jumpSpeed = 575f;
    public float gravity = 2200f;
    public float teleportDistance = 6000f;
    public float floorNormalMinY = 0.5f;
    public float snapTurnDegrees = 45f;
    public float snapTurnCooldownSeconds = 0.25f;
    public bool allowContinuousMovement = true;

    private static readonly List<XRDisplaySubsystem> DisplaySubsystems = new List<XRDisplaySubsystem>();

    private CharacterController _controller;
    private Transform _trackingRoot;
    private Transform _headAnchor;
    private Transform _leftHandAnchor;
    private Transform _rightHandAnchor;
    private Camera _viewCamera;
    private LineRenderer _teleportRay;
    private GameObject _teleportMarker;
    private InputDevice _headDevice;
    private InputDevice _leftHandDevice;
    private InputDevice _rightHandDevice;
    private Vector3 _velocity;
    private bool _explorationEnabled;
    private bool _teleportHeldLastFrame;
    private bool _teleportValid;
    private Vector3 _teleportPoint;
    private float _nextSnapTurnTime;
    private bool _leftPrimaryHeldLastFrame;
    private bool _leftSecondaryHeldLastFrame;
    private bool _rightSecondaryHeldLastFrame;
    private bool _rightTriggerHeldLastFrame;
    private bool _menuAxisEngaged;
    private int _pendingMenuStep;
    private bool _pendingMenuConfirm;
    private bool _pendingReturnToMenu;
    private bool _menuPrimaryHeldLastFrame;
    private float _ignoreMovementToggleUntilTime;

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
        get { return true; }
    }

    public static bool IsVrRuntimeAvailable()
    {
        SubsystemManager.GetSubsystems(DisplaySubsystems);
        for (int i = 0; i < DisplaySubsystems.Count; i++)
        {
            XRDisplaySubsystem subsystem = DisplaySubsystems[i];
            if (subsystem != null && subsystem.running)
            {
                return true;
            }
        }

        return XRSettings.isDeviceActive;
    }

    private void Awake()
    {
        EnsureRig();
        SetExplorationEnabled(false);
    }

    private void Update()
    {
        UpdateTracking();
        SyncCharacterControllerToHead();

        if (_explorationEnabled)
        {
            HandleExplorationInput();
        }
        else
        {
            HandleMenuInput();
            HideTeleportVisuals();
        }
    }

    public override void SetExplorationEnabled(bool enabled)
    {
        _explorationEnabled = enabled;
        _velocity = Vector3.zero;
        _teleportHeldLastFrame = false;
        _teleportValid = false;
        _leftPrimaryHeldLastFrame = false;
        _leftSecondaryHeldLastFrame = false;
        _rightSecondaryHeldLastFrame = false;
        _rightTriggerHeldLastFrame = false;
        if (enabled)
        {
            allowContinuousMovement = true;
            _ignoreMovementToggleUntilTime = Time.unscaledTime + MovementToggleDebounceSeconds;
        }
        HideTeleportVisuals();
    }

    public override void PlaceAt(Vector3 position, Vector3 forward)
    {
        Vector3 flatForward = forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 0.001f)
        {
            flatForward = Vector3.forward;
        }

        transform.rotation = Quaternion.LookRotation(flatForward.normalized, Vector3.up);

        Vector3 headOffset = _headAnchor != null ? _headAnchor.localPosition : Vector3.zero;
        headOffset.y = 0f;

        bool controllerWasEnabled = _controller.enabled;
        _controller.enabled = false;
        transform.position = position - headOffset;
        _controller.enabled = controllerWasEnabled;
    }

    public override int ConsumeMenuStepDelta()
    {
        int value = _pendingMenuStep;
        _pendingMenuStep = 0;
        return value;
    }

    public override bool ConsumeMenuConfirmRequested()
    {
        bool value = _pendingMenuConfirm;
        _pendingMenuConfirm = false;
        return value;
    }

    public override bool ConsumeReturnToMenuRequested()
    {
        bool value = _pendingReturnToMenu;
        _pendingReturnToMenu = false;
        return value;
    }

    private void EnsureRig()
    {
        _controller = GetComponent<CharacterController>();
        _controller.height = 180f;
        _controller.radius = 28f;
        _controller.center = new Vector3(0f, 90f, 0f);
        _controller.stepOffset = 35f;
        _controller.skinWidth = 4f;
        _controller.minMoveDistance = 0f;

        _trackingRoot = EnsureChild("TrackingRoot", transform);
        _headAnchor = EnsureChild("Head", _trackingRoot);
        _leftHandAnchor = EnsureChild("LeftHand", _trackingRoot);
        _rightHandAnchor = EnsureChild("RightHand", _trackingRoot);

        Transform cameraTransform = EnsureChild("VrCamera", _headAnchor);
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
        _viewCamera.clearFlags = CameraClearFlags.SolidColor;
        _viewCamera.backgroundColor = new Color(0.05f, 0.06f, 0.1f);

        if (cameraTransform.GetComponent<AudioListener>() == null)
        {
            cameraTransform.gameObject.AddComponent<AudioListener>();
        }

        Mm6PlayerAvatar avatar = GetComponent<Mm6PlayerAvatar>();
        if (avatar == null)
        {
            avatar = gameObject.AddComponent<Mm6PlayerAvatar>();
        }
        avatar.viewCamera = _viewCamera;
        avatar.headTransform = _headAnchor;

        _teleportRay = CreateTeleportRay();
        _teleportMarker = CreateTeleportMarker();
        HideTeleportVisuals();
    }

    private void UpdateTracking()
    {
        UpdateNode(XRNode.Head, _headAnchor, ref _headDevice);
        UpdateNode(XRNode.LeftHand, _leftHandAnchor, ref _leftHandDevice);
        UpdateNode(XRNode.RightHand, _rightHandAnchor, ref _rightHandDevice);
    }

    private void UpdateNode(XRNode node, Transform target, ref InputDevice device)
    {
        if (target == null)
        {
            return;
        }

        if (!device.isValid)
        {
            device = InputDevices.GetDeviceAtXRNode(node);
        }

        Vector3 position;
        Quaternion rotation;
        bool hasPosition = device.TryGetFeatureValue(CommonUsages.devicePosition, out position);
        bool hasRotation = device.TryGetFeatureValue(CommonUsages.deviceRotation, out rotation);

        if (!hasPosition)
        {
            position = InputTracking.GetLocalPosition(node);
        }

        if (!hasRotation)
        {
            rotation = InputTracking.GetLocalRotation(node);
        }

        target.localPosition = position * WorldUnitsPerMeter;
        target.localRotation = rotation;
    }

    private void SyncCharacterControllerToHead()
    {
        if (_controller == null || _headAnchor == null)
        {
            return;
        }

        float headHeight = Mathf.Clamp(_headAnchor.localPosition.y, 120f, 220f);
        _controller.height = headHeight;
        _controller.center = new Vector3(
            _headAnchor.localPosition.x,
            headHeight * 0.5f + _controller.skinWidth,
            _headAnchor.localPosition.z
        );
    }

    private void HandleMenuInput()
    {
        Vector2 axis = ReadPrimaryAxis(_leftHandDevice);
        if (axis == Vector2.zero)
        {
            axis = ReadPrimaryAxis(_rightHandDevice);
        }

        float vertical = axis.y;
        if (!_menuAxisEngaged && vertical >= 0.65f)
        {
            _pendingMenuStep = -1;
            _menuAxisEngaged = true;
        }
        else if (!_menuAxisEngaged && vertical <= -0.65f)
        {
            _pendingMenuStep = 1;
            _menuAxisEngaged = true;
        }
        else if (Mathf.Abs(vertical) <= 0.3f)
        {
            _menuAxisEngaged = false;
        }

        bool triggerPressed = ReadTriggerPressed(_rightHandDevice);
        bool primaryPressed = ReadButton(_leftHandDevice, CommonUsages.primaryButton)
            || ReadButton(_rightHandDevice, CommonUsages.primaryButton);
        if ((!_rightTriggerHeldLastFrame && triggerPressed) ||
            (!_menuPrimaryHeldLastFrame && primaryPressed))
        {
            _pendingMenuConfirm = true;
        }

        _rightTriggerHeldLastFrame = triggerPressed;
        _menuPrimaryHeldLastFrame = primaryPressed;
    }

    private void HandleExplorationInput()
    {
        Vector2 leftAxis = ReadPrimaryAxis(_leftHandDevice);
        Vector2 rightAxis = ReadPrimaryAxis(_rightHandDevice);
        bool leftPrimaryPressed = ReadButton(_leftHandDevice, CommonUsages.primaryButton);
        bool leftSecondaryPressed = ReadButton(_leftHandDevice, CommonUsages.secondaryButton);
        bool rightSecondaryPressed = ReadButton(_rightHandDevice, CommonUsages.secondaryButton);
        bool teleportPressed = ReadTriggerPressed(_rightHandDevice);
        bool jumpPressed = leftSecondaryPressed && !_leftSecondaryHeldLastFrame;

        if (Time.unscaledTime >= _ignoreMovementToggleUntilTime &&
            leftPrimaryPressed && !_leftPrimaryHeldLastFrame)
        {
            allowContinuousMovement = !allowContinuousMovement;
        }
        _leftPrimaryHeldLastFrame = leftPrimaryPressed;

        if (rightSecondaryPressed && !_rightSecondaryHeldLastFrame)
        {
            _pendingReturnToMenu = true;
        }
        _leftSecondaryHeldLastFrame = leftSecondaryPressed;
        _rightSecondaryHeldLastFrame = rightSecondaryPressed;

        HandleSnapTurn(rightAxis.x);
        HandleTeleport(teleportPressed);
        HandleMovement(leftAxis, teleportPressed, jumpPressed);
    }

    private void HandleMovement(Vector2 axis, bool suppressMovement, bool jumpPressed)
    {
        if (_controller == null)
        {
            return;
        }

        if (_controller.isGrounded && _velocity.y < 0f)
        {
            _velocity.y = -20f;
        }

        Vector3 planarMove = Vector3.zero;
        if (allowContinuousMovement && !suppressMovement)
        {
            Vector3 forward = _headAnchor.forward;
            Vector3 right = _headAnchor.right;
            forward.y = 0f;
            right.y = 0f;
            if (forward.sqrMagnitude > 0.001f)
            {
                forward.Normalize();
            }
            if (right.sqrMagnitude > 0.001f)
            {
                right.Normalize();
            }
            planarMove = (forward * axis.y + right * axis.x) * moveSpeed;
        }

        if (_controller.isGrounded && !suppressMovement && jumpPressed)
        {
            _velocity.y = jumpSpeed;
        }

        _velocity.y -= gravity * Time.deltaTime;
        Vector3 motion = planarMove * Time.deltaTime;
        motion.y = _velocity.y * Time.deltaTime;
        _controller.Move(motion);
    }

    private void HandleSnapTurn(float axisX)
    {
        if (Time.unscaledTime < _nextSnapTurnTime)
        {
            return;
        }

        if (axisX >= 0.7f)
        {
            RotateAroundHead(snapTurnDegrees);
            _nextSnapTurnTime = Time.unscaledTime + snapTurnCooldownSeconds;
        }
        else if (axisX <= -0.7f)
        {
            RotateAroundHead(-snapTurnDegrees);
            _nextSnapTurnTime = Time.unscaledTime + snapTurnCooldownSeconds;
        }
    }

    private void RotateAroundHead(float degrees)
    {
        Vector3 headWorldPosition = _headAnchor.position;
        transform.RotateAround(headWorldPosition, Vector3.up, degrees);
        _velocity = Vector3.zero;
    }

    private void HandleTeleport(bool teleportPressed)
    {
        if (teleportPressed)
        {
            UpdateTeleportVisuals();
        }
        else if (_teleportHeldLastFrame && _teleportValid)
        {
            TeleportTo(_teleportPoint);
        }
        else
        {
            HideTeleportVisuals();
        }

        _teleportHeldLastFrame = teleportPressed;
    }

    private void UpdateTeleportVisuals()
    {
        if (_rightHandAnchor == null || _teleportRay == null)
        {
            return;
        }

        Ray ray = new Ray(_rightHandAnchor.position, _rightHandAnchor.forward);
        RaycastHit hit;
        _teleportValid = Physics.Raycast(
            ray,
            out hit,
            teleportDistance,
            ~0,
            QueryTriggerInteraction.Ignore
        ) && hit.normal.y >= floorNormalMinY;

        _teleportRay.enabled = true;
        _teleportRay.positionCount = 2;
        _teleportRay.SetPosition(0, ray.origin);
        _teleportRay.SetPosition(1, _teleportValid ? hit.point : ray.origin + ray.direction * teleportDistance);

        if (_teleportValid)
        {
            _teleportPoint = hit.point;
            if (_teleportMarker != null)
            {
                _teleportMarker.SetActive(true);
                _teleportMarker.transform.position = hit.point + Vector3.up * 0.5f;
            }
        }
        else
        {
            if (_teleportMarker != null)
            {
                _teleportMarker.SetActive(false);
            }
        }
    }

    private void HideTeleportVisuals()
    {
        _teleportValid = false;
        if (_teleportRay != null)
        {
            _teleportRay.enabled = false;
        }
        if (_teleportMarker != null)
        {
            _teleportMarker.SetActive(false);
        }
    }

    private void TeleportTo(Vector3 destination)
    {
        Vector3 headOffset = _headAnchor != null ? _headAnchor.localPosition : Vector3.zero;
        headOffset.y = 0f;

        bool controllerWasEnabled = _controller.enabled;
        _controller.enabled = false;
        transform.position = destination - headOffset;
        _controller.enabled = controllerWasEnabled;
        _velocity = Vector3.zero;
        HideTeleportVisuals();
    }

    private static Vector2 ReadPrimaryAxis(InputDevice device)
    {
        Vector2 axis;
        return device.TryGetFeatureValue(CommonUsages.primary2DAxis, out axis) ? axis : Vector2.zero;
    }

    private static bool ReadTriggerPressed(InputDevice device)
    {
        float trigger;
        if (device.TryGetFeatureValue(CommonUsages.trigger, out trigger))
        {
            return trigger >= 0.35f;
        }

        bool triggerButton;
        return device.TryGetFeatureValue(CommonUsages.triggerButton, out triggerButton) && triggerButton;
    }

    private static bool ReadButton(InputDevice device, InputFeatureUsage<bool> usage)
    {
        bool value;
        return device.TryGetFeatureValue(usage, out value) && value;
    }

    private LineRenderer CreateTeleportRay()
    {
        GameObject go = new GameObject("TeleportRay");
        go.transform.SetParent(transform, false);
        LineRenderer lineRenderer = go.AddComponent<LineRenderer>();
        lineRenderer.positionCount = 2;
        lineRenderer.widthMultiplier = 1.6f;
        lineRenderer.useWorldSpace = true;
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        lineRenderer.material = new Material(
            Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard")
        );
        lineRenderer.startColor = new Color(0.3f, 0.95f, 1f, 0.95f);
        lineRenderer.endColor = new Color(0.3f, 0.95f, 1f, 0.3f);
        return lineRenderer;
    }

    private GameObject CreateTeleportMarker()
    {
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = "TeleportMarker";
        marker.transform.SetParent(transform, false);
        marker.transform.localScale = new Vector3(16f, 0.5f, 16f);

        Collider collider = marker.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider);
        }

        MeshRenderer renderer = marker.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = new Material(
                Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Standard")
            );
            renderer.sharedMaterial.color = new Color(0.2f, 0.95f, 1f, 0.65f);
        }

        return marker;
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
