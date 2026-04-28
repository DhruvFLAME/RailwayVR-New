// VRTrackDrawer.cs
// Attach to any GameObject in the scene alongside a
// SplineContainer and RailwayGenerator reference.
//
// Flow:
//   StartDrawing()  → called while VR trigger is held
//   StopDrawing()   → called when trigger is released
//   Points are recorded, smoothed, converted to a Spline,
//   assigned to the SplineContainer, then BuildTrack() fires.
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Splines;
using Unity.Mathematics;

[RequireComponent(typeof(LineRenderer))]
public class VRTrackDrawer : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────
    // Inspector
    // ─────────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("The VR controller tip transform used as the drawing point.")]
    public Transform controller;

    [Tooltip("SplineContainer that the generated spline will be written into.")]
    public SplineContainer splineContainer;

    [Tooltip("RailwayGenerator to call after spline generation.")]
    public RailwayGenerator railwayGenerator;

    [Header("Drawing Settings")]
    [Tooltip("Minimum distance the controller must move before a new point is recorded.")]
    public float minPointDistance = 0.0f;

    [Tooltip("Controls how curved/smooth the generated tangents are. Higher = wider arcs.")]
    public float tangentStrength = 0.5f;

    [Tooltip("Number of smoothing passes applied to raw points before spline generation. 0 = off.")]
    [Range(0, 5)]
    public int smoothingPasses = 2;

    [Header("Preview")]
    [Tooltip("Color of the live LineRenderer preview while drawing.")]
    public Color previewColor = new Color(0.2f, 0.8f, 1f, 1f);
    public float previewWidth = 0.03f;

[Header("Input")]
[Tooltip("Bind to controller trigger, e.g. <XRController>{RightHand}/triggerPressed.")]
public InputActionProperty triggerAction;

[Tooltip("Editor fallback — hold this key to draw without a headset.")]
public KeyCode debugTriggerKey = KeyCode.Space;

private void OnEnable()
{
    var action = triggerAction.action;
    if (action != null)
    {
        action.started  += OnTriggerStarted;
        action.canceled += OnTriggerCanceled;
        action.Enable();
    }
}

private void OnDisable()
{
    var action = triggerAction.action;
    if (action != null)
    {
        action.started  -= OnTriggerStarted;
        action.canceled -= OnTriggerCanceled;
        action.Disable();
    }
}

private void OnTriggerStarted (UnityEngine.InputSystem.InputAction.CallbackContext _) => StartDrawing();
private void OnTriggerCanceled(UnityEngine.InputSystem.InputAction.CallbackContext _) => StopDrawing();

// Keep HandleDebugInput as the keyboard fallback — it still works in Update.    public KeyCode debugTriggerKey = KeyCode.Space;

    // ─────────────────────────────────────────────────────────
    // Private state
    // ─────────────────────────────────────────────────────────

    private readonly List<Vector3> _points = new();
    private bool                   _isDrawing;
    private LineRenderer           _lineRenderer;

    // ─────────────────────────────────────────────────────────
    // Unity lifecycle
    // ─────────────────────────────────────────────────────────

    private void Awake()
    {
        _lineRenderer = GetComponent<LineRenderer>();
        ConfigureLineRenderer();
    }

    private void Update()
    {
        HandleDebugInput();

        if (_isDrawing)
        {
            RecordPoint();
            UpdatePreview();
            DrawDebugLines();
        }
    }

    // ─────────────────────────────────────────────────────────
    // Public API  ← call these from your XR Input handler
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Call this when the VR trigger is first pressed (OnTriggerPressed event).
    /// </summary>
    public void StartDrawing()
    {
        if (_isDrawing) return;

        _isDrawing = true;
        _points.Clear();
        _lineRenderer.positionCount = 0;

        // Immediately record the starting position so the first segment exists
        if (controller != null)
            _points.Add(controller.position);

        Debug.Log("[VRTrackDrawer] Drawing started.");
    }

    /// <summary>
    /// Call this when the VR trigger is released (OnTriggerReleased event).
    /// </summary>
    public void StopDrawing()
    {
        if (!_isDrawing) return;

        _isDrawing = false;
        _lineRenderer.positionCount = 0;

        Debug.Log($"[VRTrackDrawer] Drawing stopped. {_points.Count} raw points collected.");

        if (_points.Count < 2)
        {
            Debug.LogWarning("[VRTrackDrawer] Not enough points to generate a track (minimum 2). Discarding.");
            _points.Clear();
            return;
        }

        GenerateTrack();
    }

    /// <summary>True while a draw session is in progress.</summary>
    public bool IsDrawing => _isDrawing;

    /// <summary>
    /// Aborts the current draw session WITHOUT committing a spline.
    /// Use this when the player teleports or switches perspective mid-draw,
    /// so the recorded points (which span two unrelated spaces) don't pollute the track.
    /// </summary>
    public void CancelDrawing()
    {
        if (!_isDrawing) return;

        _isDrawing = false;
        _points.Clear();
        _lineRenderer.positionCount = 0;

        Debug.Log("[VRTrackDrawer] Drawing cancelled (perspective switch).");
    }
    // ─────────────────────────────────────────────────────────
    // Recording
    // ─────────────────────────────────────────────────────────

    private void RecordPoint()
    {
        if (controller == null) return;

        Vector3 current = controller.position;

        // Only add if we've moved far enough — avoids redundant knots
        if (_points.Count == 0 ||
            Vector3.Distance(_points[_points.Count - 1], current) >= minPointDistance)
        {
            _points.Add(current);
        }
    }

    // ─────────────────────────────────────────────────────────
    // Track generation pipeline
    // ─────────────────────────────────────────────────────────

    private void GenerateTrack()
    {
        // 1 ── Optional smoothing pass
        List<Vector3> workingPoints = smoothingPasses > 0
            ? SmoothPoints(_points, smoothingPasses)
            : new List<Vector3>(_points);

        // 2 ── Build spline
        Spline spline = BuildSpline(workingPoints);

        // 3 ── Assign to container
        if (splineContainer == null)
        {
            Debug.LogError("[VRTrackDrawer] SplineContainer reference is null.");
            return;
        }

        splineContainer.Spline = spline;

        Debug.Log($"[VRTrackDrawer] Spline generated with {spline.Count} knots.");

        // 4 ── Trigger mesh build
        if (railwayGenerator == null)
        {
            Debug.LogError("[VRTrackDrawer] RailwayGenerator reference is null.");
            return;
        }

        railwayGenerator.BuildTrack();
        Debug.Log("[VRTrackDrawer] Track built successfully.");

        // 5 ── Clear raw points
        _points.Clear();
    }

    // ─────────────────────────────────────────────────────────
    // Smoothing
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Laplacian smoothing: each interior point moves toward the average
    /// of its two neighbors. End points are kept fixed.
    /// </summary>
    private static List<Vector3> SmoothPoints(List<Vector3> input, int passes)
    {
        if (input.Count < 3) return new List<Vector3>(input);

        var result = new List<Vector3>(input);

        for (int pass = 0; pass < passes; pass++)
        {
            for (int i = 1; i < result.Count - 1; i++)
                result[i] = (result[i - 1] + result[i] + result[i + 1]) / 3f;
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────
    // Spline building
    // ─────────────────────────────────────────────────────────

    private Spline BuildSpline(List<Vector3> worldPoints)
    {
        var spline = new Spline();

        // Pre-compute a forward direction per point for tangent assignment
        Vector3[] forwards = ComputeForwards(worldPoints);

        for (int i = 0; i < worldPoints.Count; i++)
        {
            // Convert world-space position to the SplineContainer's local space
            float3 localPos = splineContainer.transform.InverseTransformPoint(worldPoints[i]);

            // Tangents are in the SplineContainer's local space as well
            Vector3 worldFwd   = forwards[i];
            float3  localFwd   = splineContainer.transform.InverseTransformDirection(worldFwd);
            float3  inTangent  = -localFwd * tangentStrength;
            float3  outTangent =  localFwd * tangentStrength;

            var knot = new BezierKnot(
                localPos,
                inTangent,
                outTangent,
                quaternion.identity
            );

            spline.Add(knot);
        }

        return spline;
    }

    /// <summary>
    /// Computes a smooth forward direction at each point using its neighbors.
    /// End points use the direction to/from their only neighbor.
    /// </summary>
    private static Vector3[] ComputeForwards(List<Vector3> points)
    {
        int       count    = points.Count;
        Vector3[] forwards = new Vector3[count];

        for (int i = 0; i < count; i++)
        {
            Vector3 dir;

            if (i == 0)
                dir = points[1] - points[0];
            else if (i == count - 1)
                dir = points[count - 1] - points[count - 2];
            else
                dir = points[i + 1] - points[i - 1]; // central difference — smoother

            forwards[i] = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
        }

        return forwards;
    }

    // ─────────────────────────────────────────────────────────
    // Visualization
    // ─────────────────────────────────────────────────────────

    private void UpdatePreview()
    {
        if (_points.Count < 2)
        {
            _lineRenderer.positionCount = 0;
            return;
        }

        _lineRenderer.positionCount = _points.Count;
        _lineRenderer.SetPositions(_points.ToArray());
    }

    private void DrawDebugLines()
    {
        for (int i = 1; i < _points.Count; i++)
            Debug.DrawLine(_points[i - 1], _points[i], previewColor);
    }

    private void ConfigureLineRenderer()
    {
        _lineRenderer.useWorldSpace    = true;
        _lineRenderer.startWidth       = previewWidth;
        _lineRenderer.endWidth         = previewWidth;
        _lineRenderer.positionCount    = 0;
        _lineRenderer.material         = new Material(Shader.Find("Sprites/Default"));
        _lineRenderer.startColor       = previewColor;
        _lineRenderer.endColor         = previewColor;
        _lineRenderer.numCornerVertices = 4;
        _lineRenderer.numCapVertices    = 4;
    }

    // ─────────────────────────────────────────────────────────
    // Debug / Editor keyboard fallback
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Lets you test the drawing system in Play mode without a headset.
    /// Hold the debugTriggerKey (default: Space) to draw.
    /// </summary>
    private void HandleDebugInput()
    {
        if (Input.GetKeyDown(debugTriggerKey)) StartDrawing();
        if (Input.GetKeyUp(debugTriggerKey))   StopDrawing();
    }
}
