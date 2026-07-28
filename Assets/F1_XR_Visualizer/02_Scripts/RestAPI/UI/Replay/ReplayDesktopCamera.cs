using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace F1XR.RestAPI.UI
{
    public sealed class ReplayDesktopCamera : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField, Min(0.01f)] float moveSpeed = 1.5f;
        [SerializeField, Min(1f)] float fastMoveMultiplier = 3f;
        [SerializeField, Min(0.0001f)] float wheelMoveScale = 0.0025f;

        [Header("Look")]
        [SerializeField, Min(0.001f)] float lookSensitivity = 0.12f;
        [SerializeField, Range(1f, 89f)] float maximumPitch = 85f;

        Vector3 initialPosition;
        Quaternion initialRotation;
        bool looking;
        bool resetHeld;

        void Awake()
        {
            initialPosition = transform.position;
            initialRotation = transform.rotation;
        }

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;

            bool resetPressed = keyboard?.fKey.isPressed == true;
            if (resetPressed && !resetHeld)
                ResetView();
            resetHeld = resetPressed;

            UpdateLook(mouse);
            UpdateMovement(keyboard, mouse);
        }

        void OnDisable()
        {
            resetHeld = false;
            StopLooking();
        }

        void UpdateLook(Mouse mouse)
        {
            if (mouse == null)
            {
                StopLooking();
                return;
            }

            if (!looking &&
                mouse.rightButton.wasPressedThisFrame &&
                EventSystem.current?.IsPointerOverGameObject() != true)
            {
                looking = true;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            if (looking && mouse.rightButton.wasReleasedThisFrame)
                StopLooking();

            if (!looking)
                return;

            Vector2 delta = mouse.delta.ReadValue() * lookSensitivity;
            Vector3 euler = transform.eulerAngles;
            float pitch = NormalizeAngle(euler.x);
            pitch = Mathf.Clamp(pitch - delta.y, -maximumPitch, maximumPitch);
            float yaw = euler.y + delta.x;
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        void UpdateMovement(Keyboard keyboard, Mouse mouse)
        {
            Vector3 direction = Vector3.zero;
            if (keyboard != null)
            {
                if (keyboard.wKey.isPressed)
                    direction += transform.forward;
                if (keyboard.sKey.isPressed)
                    direction -= transform.forward;
                if (keyboard.dKey.isPressed)
                    direction += transform.right;
                if (keyboard.aKey.isPressed)
                    direction -= transform.right;
                if (keyboard.eKey.isPressed)
                    direction += Vector3.up;
                if (keyboard.qKey.isPressed)
                    direction -= Vector3.up;

                float speed = moveSpeed;
                if (keyboard.leftShiftKey.isPressed ||
                    keyboard.rightShiftKey.isPressed)
                {
                    speed *= fastMoveMultiplier;
                }

                if (direction.sqrMagnitude > 0f)
                    transform.position += direction.normalized * speed * Time.unscaledDeltaTime;
            }

            if (mouse == null)
                return;

            float wheel = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(wheel) > 0.01f)
                transform.position += transform.forward * wheel * wheelMoveScale;
        }

        void ResetView()
        {
            transform.SetPositionAndRotation(initialPosition, initialRotation);
        }

        void StopLooking()
        {
            if (!looking)
                return;

            looking = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        static float NormalizeAngle(float angle)
        {
            return angle > 180f ? angle - 360f : angle;
        }
    }
}
