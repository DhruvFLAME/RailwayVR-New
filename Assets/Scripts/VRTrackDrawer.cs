// VRTrackDrawer.cs

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Splines;
using Unity.Mathematics;

[RequireComponent(typeof(LineRenderer))]
public class VRTrackDrawer : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The actual point used for drawing. Assign Tracker Sphere or Ball Pivot here.")]
    public Transform drawPoint;

    [Tooltip("SplineContainer that the generated spline will be written into.")]
    public SplineContainer splineContainer;

    [Tooltip("RailwayGenerator to call after spline generation.")]
    public RailwayGenerator railwayGenerator;

    [Header("Drawing Settings")]
    public float minPointDistance = 0.02f;
    public float tangentStrength = 0.5f;

    [Range(0, 5)]
    public int smoothingPasses = 2;

    [Header("Preview")]
    public Color previewColor = new Color(0.2f, 0.8f, 1f, 1f);
    public float previewWidth = 0.03f;

    [Header("Input")]
    [Tooltip("Bind to controller trigger, e.g. <XRController>{RightHand}/triggerPressed.")]
    public InputActionProperty triggerAction;

    [Tooltip("Editor fallback — hold this key to draw without a headset.")]
    public KeyCode debugTriggerKey = KeyCode.Space;

    private readonly List<Vector3> _points = new();
    private bool _isDrawing;
    private LineRenderer _lineRenderer;

    public bool IsDrawing => _isDrawing;

    private void Awake()
    {
        _lineRenderer = GetComponent<LineRenderer>();
        ConfigureLineRenderer();
    }

    private void OnEnable()
    {
        var action = triggerAction.action;

        if (action != null)
        {
            action.started += OnTriggerStarted;
            action.canceled += OnTriggerCanceled;
            action.Enable();
        }
    }

    private void OnDisable()
    {
        var action = triggerAction.action;

        if (action != null)
        {
            action.started -= OnTriggerStarted;
            action.canceled -= OnTriggerCanceled;
            action.Disable();
        }
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

    private void OnTriggerStarted(InputAction.CallbackContext _) => StartDrawing();

    private void OnTriggerCanceled(InputAction.CallbackContext _) => StopDrawing();

    public void StartDrawing()
    {
        if (_isDrawing) return;

        if (drawPoint == null)
        {
            Debug.LogError("[VRTrackDrawer] Draw Point is not assigned.");
            return;
        }

        _isDrawing = true;
        _points.Clear();
        _lineRenderer.positionCount = 0;

        _points.Add(drawPoint.position);

        Debug.Log("[VRTrackDrawer] Drawing started.");
    }

    public void StopDrawing()
    {
        if (!_isDrawing) return;

        _isDrawing = false;
        _lineRenderer.positionCount = 0;

        Debug.Log($"[VRTrackDrawer] Drawing stopped. {_points.Count} raw points collected.");

        if (_points.Count < 2)
        {
            Debug.LogWarning("[VRTrackDrawer] Not enough points to generate a track. Discarding.");
            _points.Clear();
            return;
        }

        GenerateTrack();
    }

    public void CancelDrawing()
    {
        if (!_isDrawing) return;

        _isDrawing = false;
        _points.Clear();
        _lineRenderer.positionCount = 0;

        Debug.Log("[VRTrackDrawer] Drawing cancelled.");
    }

    private void RecordPoint()
    {
        if (drawPoint == null) return;

        Vector3 current = drawPoint.position;

        if (_points.Count == 0 ||
            Vector3.Distance(_points[_points.Count - 1], current) >= minPointDistance)
        {
            _points.Add(current);
        }
    }

    private void GenerateTrack()
    {
        List<Vector3> workingPoints = smoothingPasses > 0
            ? SmoothPoints(_points, smoothingPasses)
            : new List<Vector3>(_points);

        if (splineContainer == null)
        {
            Debug.LogError("[VRTrackDrawer] SplineContainer reference is null.");
            return;
        }

        Spline spline = BuildSpline(workingPoints);
        splineContainer.Spline = spline;

        Debug.Log($"[VRTrackDrawer] Spline generated with {spline.Count} knots.");

        if (railwayGenerator == null)
        {
            Debug.LogError("[VRTrackDrawer] RailwayGenerator reference is null.");
            return;
        }

        railwayGenerator.BuildTrack();

        Debug.Log("[VRTrackDrawer] Track built successfully.");

        _points.Clear();
    }

    private Spline BuildSpline(List<Vector3> worldPoints)
    {
        var spline = new Spline();
        Vector3[] forwards = ComputeForwards(worldPoints);

        for (int i = 0; i < worldPoints.Count; i++)
        {
            float3 localPos = splineContainer.transform.InverseTransformPoint(worldPoints[i]);

            Vector3 worldFwd = forwards[i];
            float3 localFwd = splineContainer.transform.InverseTransformDirection(worldFwd);

            float3 inTangent = -localFwd * tangentStrength;
            float3 outTangent = localFwd * tangentStrength;

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

    private static Vector3[] ComputeForwards(List<Vector3> points)
    {
        int count = points.Count;
        Vector3[] forwards = new Vector3[count];

        for (int i = 0; i < count; i++)
        {
            Vector3 dir;

            if (i == 0)
                dir = points[1] - points[0];
            else if (i == count - 1)
                dir = points[count - 1] - points[count - 2];
            else
                dir = points[i + 1] - points[i - 1];

            forwards[i] = dir.sqrMagnitude > 0.0001f
                ? dir.normalized
                : Vector3.forward;
        }

        return forwards;
    }

    private static List<Vector3> SmoothPoints(List<Vector3> input, int passes)
    {
        if (input.Count < 3)
            return new List<Vector3>(input);

        var result = new List<Vector3>(input);

        for (int pass = 0; pass < passes; pass++)
        {
            for (int i = 1; i < result.Count - 1; i++)
            {
                result[i] = (result[i - 1] + result[i] + result[i + 1]) / 3f;
            }
        }

        return result;
    }

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
        {
            Debug.DrawLine(_points[i - 1], _points[i], previewColor);
        }
    }

    private void ConfigureLineRenderer()
    {
        _lineRenderer.useWorldSpace = true;
        _lineRenderer.startWidth = previewWidth;
        _lineRenderer.endWidth = previewWidth;
        _lineRenderer.positionCount = 0;
        _lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        _lineRenderer.startColor = previewColor;
        _lineRenderer.endColor = previewColor;
        _lineRenderer.numCornerVertices = 4;
        _lineRenderer.numCapVertices = 4;
    }

    private void HandleDebugInput()
    {
        if (Input.GetKeyDown(debugTriggerKey))
            StartDrawing();

        if (Input.GetKeyUp(debugTriggerKey))
            StopDrawing();
    }
}
