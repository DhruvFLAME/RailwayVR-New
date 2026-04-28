// XRJoystickObjectController.cs
using UnityEngine;
using UnityEngine.InputSystem;

public class XRJoystickObjectController : MonoBehaviour
{
    [Header("Right Joystick: Move Sphere In X-Z Plane")]
    public Transform sphereToMove;
    public InputActionProperty rightJoystickAction;
    public float sphereMoveSpeed = 2f;

    [Header("Left Joystick: Orbit Platform Around Center")]
    public Transform platformToOrbit;
    public Transform orbitCenter;
    public InputActionProperty leftJoystickAction;
    public float orbitDegreesPerSecond = 90f;

    [Header("Settings")]
    public float deadzone = 0.15f;

    private void OnEnable()
    {
        rightJoystickAction.action?.Enable();
        leftJoystickAction.action?.Enable();
    }

    private void OnDisable()
    {
        rightJoystickAction.action?.Disable();
        leftJoystickAction.action?.Disable();
    }

    private void Update()
    {
        MoveSphereWithRightStick();
        OrbitPlatformWithLeftStick();
    }

    private void MoveSphereWithRightStick()
    {
        if (sphereToMove == null || rightJoystickAction.action == null)
            return;

        Vector2 stick = rightJoystickAction.action.ReadValue<Vector2>();

        if (stick.magnitude < deadzone)
            return;

        Vector3 movement = new Vector3(stick.x, 0f, stick.y);
        sphereToMove.position += movement * sphereMoveSpeed * Time.deltaTime;
    }

    private void OrbitPlatformWithLeftStick()
    {
        if (platformToOrbit == null || orbitCenter == null || leftJoystickAction.action == null)
            return;

        Vector2 stick = leftJoystickAction.action.ReadValue<Vector2>();

        if (Mathf.Abs(stick.x) < deadzone)
            return;

        // Right stick input should move platform counterclockwise.
        // Left stick input should move platform clockwise.
        float angle = stick.x * orbitDegreesPerSecond * Time.deltaTime;

        platformToOrbit.RotateAround(
            orbitCenter.position,
            Vector3.up,
            angle
        );
    }
}
