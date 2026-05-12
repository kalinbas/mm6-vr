using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public sealed class Mm6FirstPersonController : MonoBehaviour
{
    public Transform lookPivot;
    public float walkSpeed = 1800f;
    public float sprintMultiplier = 1.75f;
    public float jumpSpeed = 575f;
    public float gravity = 2200f;
    public float mouseSensitivity = 1.8f;
    public float keyboardTurnSpeed = 135f;
    public float lookClamp = 85f;

    private CharacterController _controller;
    private float _pitch;
    private float _verticalVelocity;

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();
        LockCursor(true);
    }

    private void OnDisable()
    {
        LockCursor(false);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            LockCursor(false);
        }
        else if (Input.GetMouseButtonDown(0))
        {
            LockCursor(true);
        }

        HandleLook();
        HandleMovement();
    }

    private void HandleLook()
    {
        if (lookPivot == null)
        {
            return;
        }

        float mouseX = 0f;
        float mouseY = 0f;
        if (Cursor.lockState == CursorLockMode.Locked)
        {
            mouseX = Input.GetAxisRaw("Mouse X") * mouseSensitivity;
            mouseY = Input.GetAxisRaw("Mouse Y") * mouseSensitivity;
        }

        float keyboardYaw = 0f;
        if (Input.GetKey(KeyCode.LeftArrow))
        {
            keyboardYaw -= keyboardTurnSpeed * Time.deltaTime;
        }
        if (Input.GetKey(KeyCode.RightArrow))
        {
            keyboardYaw += keyboardTurnSpeed * Time.deltaTime;
        }

        transform.Rotate(0f, mouseX + keyboardYaw, 0f, Space.Self);

        if (Cursor.lockState == CursorLockMode.Locked)
        {
            _pitch = Mathf.Clamp(_pitch - mouseY, -lookClamp, lookClamp);
            lookPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }
    }

    private void HandleMovement()
    {
        if (_controller == null)
        {
            return;
        }

        if (_controller.isGrounded && _verticalVelocity < 0f)
        {
            _verticalVelocity = -20f;
        }

        float moveX = 0f;
        if (Input.GetKey(KeyCode.A))
        {
            moveX -= 1f;
        }
        if (Input.GetKey(KeyCode.D))
        {
            moveX += 1f;
        }

        float moveZ = 0f;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
        {
            moveZ += 1f;
        }
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
        {
            moveZ -= 1f;
        }

        Vector3 move = (transform.right * moveX + transform.forward * moveZ).normalized;

        float speed = walkSpeed;
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
        {
            speed *= sprintMultiplier;
        }

        if (_controller.isGrounded && Input.GetButtonDown("Jump"))
        {
            _verticalVelocity = jumpSpeed;
        }

        _verticalVelocity -= gravity * Time.deltaTime;
        Vector3 velocity = move * speed + Vector3.up * _verticalVelocity;
        _controller.Move(velocity * Time.deltaTime);
    }

    public void ResetLook()
    {
        _pitch = 0f;
        if (lookPivot != null)
        {
            lookPivot.localRotation = Quaternion.identity;
        }
    }

    private static void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
