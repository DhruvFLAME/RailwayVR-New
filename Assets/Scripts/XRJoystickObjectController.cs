using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

public class XRJoystickObjectController : MonoBehaviour
{
    [Header("Right Joystick: Move Sphere In Local X-Z Plane")]
    public Transform sphereToMove;
    public InputActionProperty rightJoystickAction;
    public float sphereMoveSpeed = 2f;

    [Header("Left Joystick: Orbit Platform Around Center")]
    public Transform platformToOrbit;
    public Transform orbitCenter;
    public Transform xrOriginToMoveWithPlatform;
    public InputActionProperty leftJoystickAction;
    public float orbitDegreesPerSecond = 90f;

    [Header("Settings")]
    public float deadzone = 0.15f;
    public bool debugLogs = true;

    private UnityEngine.XR.InputDevice _rightHandDevice;
    private UnityEngine.XR.InputDevice _leftHandDevice;

    private void OnEnable()
    {
        rightJoystickAction.action?.Enable();
        leftJoystickAction.action?.Enable();

        _rightHandDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        _leftHandDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
    }

    private void Update()
    {
        Vector2 rightStick = ReadStick(rightJoystickAction, XRNode.RightHand, ref _rightHandDevice);
        Vector2 leftStick = ReadStick(leftJoystickAction, XRNode.LeftHand, ref _leftHandDevice);

        MoveSphere(rightStick);
        OrbitPlatformAndPlayer(leftStick);
    }

    private Vector2 ReadStick(
        InputActionProperty actionProperty,
        XRNode node,
        ref UnityEngine.XR.InputDevice device
    )
    {
        Vector2 value = Vector2.zero;

        if (actionProperty.action != null)
            value = actionProperty.action.ReadValue<Vector2>();

        if (value.magnitude >= deadzone)
            return value;

        if (!device.isValid)
            device = InputDevices.GetDeviceAtXRNode(node);

        Vector2 xrValue = Vector2.zero;

        if (device.isValid &&
            device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out xrValue))
        {
            value = xrValue;
        }

        return value;
    }

    private void MoveSphere(Vector2 stick)
    {
        if (sphereToMove == null)
            return;

        if (stick.magnitude < deadzone)
            return;

        Vector3 localMovement = new Vector3(stick.x, 0f, stick.y);
        sphereToMove.localPosition += localMovement * sphereMoveSpeed * Time.deltaTime;

        if (debugLogs)
            Debug.Log($"[Joystick] Right stick: {stick}, Sphere local position: {sphereToMove.localPosition}");
    }

    private void OrbitPlatformAndPlayer(Vector2 stick)
    {
        if (platformToOrbit == null || orbitCenter == null)
            return;

        if (Mathf.Abs(stick.x) < deadzone)
            return;

        float angle = stick.x * orbitDegreesPerSecond * Time.deltaTime;

        platformToOrbit.RotateAround(orbitCenter.position, Vector3.up, angle);

        if (xrOriginToMoveWithPlatform != null)
            xrOriginToMoveWithPlatform.RotateAround(orbitCenter.position, Vector3.up, angle);
    }
}
