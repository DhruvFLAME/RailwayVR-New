// ============================================================
// RollercoasterCart.cs
// Moves an XR camera rig along a SplineContainer.
// Attach this script to a "Cart" GameObject.
// ============================================================

using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class RollercoasterCart : MonoBehaviour
{
    // ─────────────────────────────────────────────
    // Inspector – Spline Reference
    // ─────────────────────────────────────────────

    [Header("Spline")]
    [Tooltip("The SplineContainer that defines the track path.")]
    public SplineContainer splineContainer;

    // ─────────────────────────────────────────────
    // Inspector – Movement
    // ─────────────────────────────────────────────

    [Header("Movement")]
    [Tooltip("Start moving when the scene plays.")]
    public bool autoStart = true;

    [Tooltip("Loop back to the start when the end is reached.")]
    public bool loop = true;

    [Tooltip("Use constant speed (true) or gravity-based speed (false).")]
    public bool constantSpeed = true;

    [Tooltip("Constant speed in world-units per second.")]
    [Min(0.01f)]
    public float speed = 5f;

    [Tooltip("Gravity acceleration in world-units per second² (gravity mode only).")]
    public float gravity = 9.81f;

    [Tooltip("Minimum speed so the cart never fully stops (gravity mode only).")]
    [Min(0f)]
    public float minSpeed = 0.5f;

    [Tooltip("Maximum speed cap (gravity mode only).")]
    public float maxSpeed = 20f;

    // ─────────────────────────────────────────────
    // Inspector – VR / Comfort
    // ─────────────────────────────────────────────

    [Header("VR & Comfort")]
    [Tooltip("Drag the XR Origin (or XR Rig) GameObject here.")]
    public Transform xrOrigin;

    [Tooltip("Smooth rotation interpolation speed (lower = smoother, higher = snappier).")]
    [Range(1f, 30f)]
    public float rotationSmoothing = 10f;

    [Tooltip("Reduced-motion mode: limits maximum angular change per frame.")]
    public bool reducedMotionMode = false;

    [Tooltip("Max degrees the cart may rotate per second in reduced-motion mode.")]
    [Range(10f, 180f)]
    public float maxAngularSpeed = 60f;

    // ─────────────────────────────────────────────
    // Inspector – Editor Preview
    // ─────────────────────────────────────────────

    [Header("Editor Preview")]
    [Tooltip("Preview cart position in edit mode (t = 0 … 1).")]
    [Range(0f, 1f)]
    public float previewT = 0f;

    // ─────────────────────────────────────────────
    // Private state
    // ─────────────────────────────────────────────

    // Normalised spline parameter [0..1]
    private float _t = 0f;

    // Current world-space speed (used in gravity mode)
    private float _currentSpeed;

    // Is the cart currently running?
    private bool _isRunning = false;

    // Cached spline length (world units) – avoids repeated calculation
    private float _splineLength = 0f;

    // Last smoothed rotation (avoids flip/jitter)
    private Quaternion _smoothedRotation = Quaternion.identity;

    // ─────────────────────────────────────────────
    // Unity messages
    // ─────────────────────────────────────────────

    private void Start()
    {
        if (!Application.isPlaying) return;

        InitCart();

        if (autoStart)
            StartCart();
    }

    private void Update()
    {
        // In edit mode: just show preview position, do nothing else
        if (!Application.isPlaying)
        {
            PreviewInEditor();
            return;
        }

        if (!_isRunning) return;

        AdvanceAlongSpline();
        PositionCartOnSpline();
        AttachXROrigin();
    }

    // ─────────────────────────────────────────────
    // Public controls
    // ─────────────────────────────────────────────

    public void StartCart()
    {
        _isRunning = true;
    }

    public void StopCart()
    {
        _isRunning = false;
    }

    public void ToggleCart()
    {
        _isRunning = !_isRunning;
    }

    // ─────────────────────────────────────────────
    // Initialisation
    // ─────────────────────────────────────────────

    private void InitCart()
    {
        if (splineContainer == null)
        {
            Debug.LogError("[RollercoasterCart] No SplineContainer assigned!", this);
            return;
        }

        // Cache the spline length so we can convert world-speed → t-delta efficiently
        _splineLength = splineContainer.CalculateLength();

        _currentSpeed    = speed;
        _smoothedRotation = transform.rotation;

        // Snap cart to the start of the spline immediately
        PositionCartOnSpline();
    }

    // ─────────────────────────────────────────────
    // Core movement
    // ─────────────────────────────────────────────

    /// <summary>
    /// Moves the normalised parameter _t forward each frame.
    /// Handles constant-speed and gravity-based modes.
    /// </summary>
    private void AdvanceAlongSpline()
    {
        if (splineContainer == null) return;

        // ── Gravity mode ──────────────────────────────────
        if (!constantSpeed)
        {
            // Sample the spline height at current and next tiny step
            // to estimate the slope angle.
            float lookAhead = 0.005f; // small look-ahead in t space
            float tNext = Mathf.Clamp01(_t + lookAhead);

            splineContainer.Spline.Evaluate(_t,    out float3 posA, out _, out _);
            splineContainer.Spline.Evaluate(tNext, out float3 posB, out _, out _);

            // Convert from spline local space to world
            Vector3 worldA = splineContainer.transform.TransformPoint((Vector3)posA);
            Vector3 worldB = splineContainer.transform.TransformPoint((Vector3)posB);

            // Height difference: positive means going down (accelerate)
            float heightDelta = worldA.y - worldB.y;

            // Slope angle drives acceleration (simple energy model)
            float slopeAccel = gravity * Mathf.Sign(heightDelta) *
                               Mathf.Abs(Mathf.Sin(Mathf.Atan2(heightDelta,
                                   Vector3.Distance(worldA, worldB))));

            _currentSpeed += slopeAccel * Time.deltaTime;
            _currentSpeed  = Mathf.Clamp(_currentSpeed, minSpeed, maxSpeed);
        }
        else
        {
            // Constant speed – just use the inspector value
            _currentSpeed = speed;
        }

        // Convert world-space speed to a change in normalised t
        // t advances by (speed / splineLength) per second
        float tDelta = (_currentSpeed / _splineLength) * Time.deltaTime;
        _t += tDelta;

        // Handle looping or clamping
        if (_t >= 1f)
        {
            if (loop)
                _t -= 1f; // wrap around
            else
            {
                _t = 1f;
                StopCart(); // reached the end
            }
        }
    }

    /// <summary>
    /// Reads the spline at _t and positions + rotates the cart.
    /// </summary>
    private void PositionCartOnSpline()
    {
        if (splineContainer == null) return;

        // Get position, tangent, and up from the spline
        splineContainer.Spline.Evaluate(_t,
            out float3 localPos,
            out float3 localTangent,
            out float3 localUp);

        // Transform from spline-local space to world space
        Vector3 worldPos     = splineContainer.transform.TransformPoint((Vector3)localPos);
        Vector3 worldForward = splineContainer.transform.TransformDirection(((Vector3)localTangent).normalized);
        Vector3 worldUp      = splineContainer.transform.TransformDirection(((Vector3)localUp).normalized);

        // Guard against degenerate tangents (can happen at knot overlaps)
        if (worldForward.sqrMagnitude < 0.001f) return;

        // Build the target rotation from the spline's own orientation
        Quaternion targetRotation = Quaternion.LookRotation(worldForward, worldUp);

        // Smooth the rotation to prevent sudden snaps or jitter
        if (reducedMotionMode)
        {
            // Limit how many degrees we can rotate each frame
            float maxDeg = maxAngularSpeed * Time.deltaTime;
            _smoothedRotation = Quaternion.RotateTowards(_smoothedRotation, targetRotation, maxDeg);
        }
        else
        {
            _smoothedRotation = Quaternion.Slerp(_smoothedRotation, targetRotation,
                                                  rotationSmoothing * Time.deltaTime);
        }

        // Apply position and rotation to the cart
        transform.position = worldPos;
        transform.rotation = _smoothedRotation;
    }

    /// <summary>
    /// Snaps the XR Origin to the cart so the player rides inside it.
    /// The XR Origin's local offset is zeroed so the camera sits at the cart centre.
    /// </summary>
    private void AttachXROrigin()
    {
        if (xrOrigin == null) return;

        // Parent the XR Origin to the cart once (keeps hierarchy clean)
        if (xrOrigin.parent != transform)
        {
            xrOrigin.SetParent(transform, worldPositionStays: false);
            xrOrigin.localPosition = Vector3.zero;
            xrOrigin.localRotation = Quaternion.identity;
        }
    }

    // ─────────────────────────────────────────────
    // Editor preview
    // ─────────────────────────────────────────────

    private void PreviewInEditor()
    {
        if (splineContainer == null) return;

        // Sample spline at the preview slider value
        splineContainer.Spline.Evaluate(previewT,
            out float3 localPos,
            out float3 localTangent,
            out float3 localUp);

        Vector3 worldPos     = splineContainer.transform.TransformPoint((Vector3)localPos);
        Vector3 worldForward = splineContainer.transform.TransformDirection(((Vector3)localTangent).normalized);
        Vector3 worldUp      = splineContainer.transform.TransformDirection(((Vector3)localUp).normalized);

        if (worldForward.sqrMagnitude < 0.001f) return;

        transform.position = worldPos;
        transform.rotation = Quaternion.LookRotation(worldForward, worldUp);
    }

    // ─────────────────────────────────────────────
    // Gizmos
    // ─────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (splineContainer == null) return;

        // Draw a sphere at the cart's current (or preview) position on the spline
        float displayT = Application.isPlaying ? _t : previewT;

        splineContainer.Spline.Evaluate(displayT,
            out float3 localPos, out float3 localTangent, out float3 localUp);

        Vector3 worldPos     = splineContainer.transform.TransformPoint((Vector3)localPos);
        Vector3 worldForward = splineContainer.transform.TransformDirection(((Vector3)localTangent).normalized);
        Vector3 worldUp      = splineContainer.transform.TransformDirection(((Vector3)localUp).normalized);

        // Yellow sphere = cart centre
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(worldPos, 0.2f);

        // Blue line = forward direction
        Gizmos.color = Color.blue;
        Gizmos.DrawLine(worldPos, worldPos + worldForward * 0.8f);

        // Green line = up direction
        Gizmos.color = Color.green;
        Gizmos.DrawLine(worldPos, worldPos + worldUp * 0.5f);

        // Label showing current t value
        Handles.Label(worldPos + Vector3.up * 0.5f,
            $"t = {displayT:F3}\nspd = {_currentSpeed:F1}");
    }
#endif
}


// ============================================================
// RollercoasterCartEditor.cs  (nested in the same file)
// Adds Start / Stop / Reset buttons to the Inspector.
// ============================================================
#if UNITY_EDITOR
[CustomEditor(typeof(RollercoasterCart))]
public class RollercoasterCartEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GUILayout.Space(10);

        var cart = (RollercoasterCart)target;

        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            EditorGUILayout.LabelField("Runtime Controls", EditorStyles.boldLabel);

            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("▶  Start",  GUILayout.Height(28))) cart.StartCart();
                if (GUILayout.Button("⏸  Stop",   GUILayout.Height(28))) cart.StopCart();
                if (GUILayout.Button("↺  Toggle", GUILayout.Height(28))) cart.ToggleCart();
            }
        }

        if (!Application.isPlaying)
        {
            GUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Use the 'Preview T' slider above to scrub the cart along the spline in edit mode.",
                MessageType.Info);
        }
    }
}
#endif
