// ============================================================
// RailwayGenerator.cs
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using UnityEngine.Splines;
using Unity.Mathematics;

#if UNITY_EDITOR
using UnityEditor;
#endif

[RequireComponent(typeof(SplineContainer))]
[ExecuteAlways]
public class RailwayGenerator : MonoBehaviour
{
    [Header("Track Shape")]
    public float trackWidth = 1.5f;
    public float railWidth = 0.15f;
    public float railHeight = 0.12f;

    [Range(8, 512)]
    public int resolution = 64;

    [Header("Sleepers")]
    public GameObject sleeperPrefab;
    public float sleeperSpacing = 0.6f;

    [Header("Materials")]
    public Material railMaterial;
    public Material sleeperMaterial;

    [Header("Auto Rebuild")]
    public bool liveUpdate = true;

    // Child name constants — single source of truth
    private const string LEFT_RAIL_NAME     = "__RailwayGen_Left__";
    private const string RIGHT_RAIL_NAME    = "__RailwayGen_Right__";
    private const string SLEEPERS_ROOT_NAME = "__RailwayGen_Sleepers__";

    private SplineContainer _splineContainer;
    private bool _isBuilding = false;

    // ─────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────

    private void OnEnable()
    {
        Spline.Changed += OnSplineChanged;
    }

    private void OnDisable()
    {
        Spline.Changed -= OnSplineChanged;
    }

    private void OnSplineChanged(Spline spline, int knotIndex, SplineModification modification)
    {
        if (this == null || !this) return;
        _splineContainer = GetComponent<SplineContainer>();
        if (_splineContainer != null && _splineContainer.Splines.Contains(spline))
            BuildTrack();
    }

    private void Start()
    {
        BuildTrack();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!liveUpdate) return;
        EditorApplication.delayCall -= SafeBuildTrack;
        EditorApplication.delayCall += SafeBuildTrack;
    }

    private void SafeBuildTrack()
    {
        if (this == null || !this) return;
        if (!gameObject || !isActiveAndEnabled) return;
        BuildTrack();
    }
#endif

    // ─────────────────────────────────────────────
    // Build
    // ─────────────────────────────────────────────

    public void BuildTrack()
    {
        if (this == null || !this || gameObject == null) return;
        if (_isBuilding) return;
        _isBuilding = true;

        _splineContainer = GetComponent<SplineContainer>();

        if (_splineContainer == null || _splineContainer.Spline == null)
        {
            _isBuilding = false;
            return;
        }

        // ── FIX: destroy by name, not by stale reference ──────────────────
        // After a domain reload (script recompile) Unity nulls all private
        // non-serialized fields, so the old DestroyImmediate(ref) was a no-op
        // and generated duplicate geometry every rebuild.
        DestroyChildrenByName(LEFT_RAIL_NAME, RIGHT_RAIL_NAME, SLEEPERS_ROOT_NAME);
        // ──────────────────────────────────────────────────────────────────

        GameObject leftRailGO     = CreateChild(LEFT_RAIL_NAME);
        GameObject rightRailGO    = CreateChild(RIGHT_RAIL_NAME);
        GameObject sleepersRoot   = CreateChild(SLEEPERS_ROOT_NAME);

        List<TrackFrame> frames = SampleSpline(resolution);

        Mesh leftMesh  = BuildRailMesh(frames, -trackWidth * 0.5f);
        Mesh rightMesh = BuildRailMesh(frames,  trackWidth * 0.5f);

        AssignMesh(leftRailGO,  leftMesh,  railMaterial);
        AssignMesh(rightRailGO, rightMesh, railMaterial);

        SpawnSleepers(frames, sleepersRoot);

        _isBuilding = false;
    }

    /// <summary>
    /// Finds and destroys any children whose names match the given set.
    /// Works even after domain reloads because it searches by name, not reference.
    /// </summary>
    private void DestroyChildrenByName(params string[] names)
    {
        var nameSet = new HashSet<string>(names);
        var toDestroy = new List<GameObject>();

        foreach (Transform child in transform)
            if (nameSet.Contains(child.name))
                toDestroy.Add(child.gameObject);

        foreach (var go in toDestroy)
            DestroyImmediate(go);
    }

    // ─────────────────────────────────────────────
    // Frame Sampling
    // ─────────────────────────────────────────────

    private struct TrackFrame
    {
        public Vector3 Position;
        public Vector3 Forward;
        public Vector3 Up;
        public Vector3 Right;
        public float T;
    }

    private List<TrackFrame> SampleSpline(int steps)
    {
        var spline = _splineContainer.Spline;
        var frames = new List<TrackFrame>(steps + 1);

        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            spline.Evaluate(t, out float3 pos, out float3 tangent, out float3 upVec);

            Vector3 worldPos     = transform.TransformPoint((Vector3)pos);
            Vector3 worldForward = transform.TransformDirection(((Vector3)tangent).normalized);

            // ── Robust up/right frame ─────────────────────────────────────
            // Using the spline's evaluated up vector (which is driven by knot
            // rotation) gives correct banking through loop-de-loops and banked
            // corners. We then re-orthogonalise to avoid drift.
            Vector3 worldUp = transform.TransformDirection(((Vector3)upVec).normalized);

            // Guard against degenerate tangent (e.g. duplicate knots)
            if (worldForward.sqrMagnitude < 0.0001f)
                worldForward = i > 0 ? frames[i - 1].Forward : Vector3.forward;

            // Re-orthogonalise: right must be perpendicular to both forward and up
            Vector3 right = Vector3.Cross(worldUp, worldForward).normalized;
            Vector3 up    = Vector3.Cross(worldForward, right).normalized;  // recompute clean up

            frames.Add(new TrackFrame
            {
                Position = worldPos,
                Forward  = worldForward,
                Up       = up,
                Right    = right,
                T        = t
            });
        }

        return frames;
    }

    // ─────────────────────────────────────────────
    // Mesh Generation
    // ─────────────────────────────────────────────

    private Mesh BuildRailMesh(List<TrackFrame> frames, float lateralOffset)
    {
        int sectionCount = frames.Count;
        int vertCount    = sectionCount * 4;

        var vertices  = new Vector3[vertCount];
        var normals   = new Vector3[vertCount];
        var uvs       = new Vector2[vertCount];
        var triangles = new List<int>();

        float halfRailW = railWidth * 0.5f;

        for (int i = 0; i < sectionCount; i++)
        {
            TrackFrame f      = frames[i];
            Vector3    centre = f.Position + f.Right * lateralOffset;

            Vector3 bottomLeft  = centre - f.Right * halfRailW - f.Up * railHeight;
            Vector3 bottomRight = centre + f.Right * halfRailW - f.Up * railHeight;
            Vector3 topLeft     = centre - f.Right * halfRailW;
            Vector3 topRight    = centre + f.Right * halfRailW;

            int b = i * 4;
            vertices[b + 0] = bottomLeft;
            vertices[b + 1] = bottomRight;
            vertices[b + 2] = topLeft;
            vertices[b + 3] = topRight;

            normals[b + 0] = f.Up;
            normals[b + 1] = f.Up;
            normals[b + 2] = f.Up;
            normals[b + 3] = f.Up;

            float u = f.T * 10f;
            uvs[b + 0] = new Vector2(u, 0f);
            uvs[b + 1] = new Vector2(u, 1f);
            uvs[b + 2] = new Vector2(u, 0f);
            uvs[b + 3] = new Vector2(u, 1f);

            if (i > 0)
            {
                int prev = (i - 1) * 4;
                int curr = i * 4;
                AddQuad(triangles, prev + 2, prev + 3, curr + 2, curr + 3); // top face
                AddQuad(triangles, curr + 0, curr + 1, prev + 0, prev + 1); // bottom face
                AddQuad(triangles, prev + 0, prev + 2, curr + 0, curr + 2); // left side
                AddQuad(triangles, curr + 1, curr + 3, prev + 1, prev + 3); // right side
            }
        }

        // End caps
        AddQuad(triangles, 2, 3, 0, 1);
        int last = (sectionCount - 1) * 4;
        AddQuad(triangles, last + 0, last + 1, last + 2, last + 3);

        var mesh = new Mesh
        {
            name      = "Rail",
            vertices  = vertices,
            normals   = normals,
            uv        = uvs,
            triangles = triangles.ToArray()
        };
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void AddQuad(List<int> tris, int a, int b, int c, int d)
    {
        tris.Add(a); tris.Add(b); tris.Add(c);
        tris.Add(b); tris.Add(d); tris.Add(c);
    }

    // ─────────────────────────────────────────────
    // Sleepers
    // ─────────────────────────────────────────────

    private void SpawnSleepers(List<TrackFrame> frames, GameObject root)
    {
        if (sleeperPrefab == null) return;

        float accumulated  = 0f;
        bool  firstSpawned = false;

        for (int i = 1; i < frames.Count; i++)
        {
            accumulated += Vector3.Distance(frames[i - 1].Position, frames[i].Position);

            if (!firstSpawned || accumulated >= sleeperSpacing)
            {
                Quaternion rot     = Quaternion.LookRotation(frames[i].Forward, frames[i].Up);
                GameObject sleeper = Instantiate(sleeperPrefab, frames[i].Position, rot, root.transform);
                sleeper.name       = "Sleeper";
                accumulated        = 0f;
                firstSpawned       = true;
            }
        }
    }

    // ─────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────

    private GameObject CreateChild(string childName)
    {
        var go = new GameObject(childName);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale    = Vector3.one;
        return go;
    }

    private static void AssignMesh(GameObject go, Mesh mesh, Material mat)
    {
        if (go == null || mesh == null)
            return;

        // Ensure GameObject is still valid (important after DestroyImmediate)
        if (!go)
            return;

        MeshFilter mf = go.GetComponent<MeshFilter>();
        if (mf == null)
        {
            mf = go.AddComponent<MeshFilter>();
            if (mf == null) return; // safety guard
        }

        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        if (mr == null)
        {
            mr = go.AddComponent<MeshRenderer>();
            if (mr == null) return; // safety guard
        }

        // Final safety check before assignment
        if (mf != null)
            mf.sharedMesh = mesh;

        if (mr != null)
            mr.sharedMaterial = mat;
    }
}

// ============================================================
// Editor
// ============================================================

#if UNITY_EDITOR
[CustomEditor(typeof(RailwayGenerator))]
public class RailwayGeneratorEditor : Editor
{
    // ── Scene GUI ────────────────────────────────────────────

    private void OnSceneGUI()
    {
        var gen             = (RailwayGenerator)target;
        var splineContainer = gen.GetComponent<SplineContainer>();
        if (splineContainer == null) return;

        var spline = splineContainer.Spline;

        // ── Toolbar button (top-left of Scene view) ───────────
        Handles.BeginGUI();
        GUILayout.BeginArea(new Rect(10, 10, 180, 80));
        GUILayout.BeginVertical(EditorStyles.helpBox);

        if (GUILayout.Button("＋ Add Knot at End", GUILayout.Height(28)))
        {
            Undo.RecordObject(splineContainer, "Add Spline Knot");
            AddKnotAtEnd(spline);
            EditorUtility.SetDirty(splineContainer);
            gen.BuildTrack();
        }

        if (GUILayout.Button("✕ Remove Last Knot", GUILayout.Height(28)))
        {
            if (spline.Count > 2)
            {
                Undo.RecordObject(splineContainer, "Remove Spline Knot");
                spline.RemoveAt(spline.Count - 1);
                EditorUtility.SetDirty(splineContainer);
                gen.BuildTrack();
            }
        }

        GUILayout.EndVertical();
        GUILayout.EndArea();
        Handles.EndGUI();

        // ── Per-knot rotation handles ─────────────────────────
        for (int i = 0; i < spline.Count; i++)
        {
            BezierKnot knot     = spline[i];
            Vector3    worldPos = gen.transform.TransformPoint((Vector3)(float3)knot.Position);
            Quaternion worldRot = gen.transform.rotation * (Quaternion)knot.Rotation;

            // Draw a small label so it's clear which knot you're rotating
            Handles.Label(worldPos + Vector3.up * 0.3f,
                $"Knot {i}",
                new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = Color.yellow } });

            EditorGUI.BeginChangeCheck();
            Quaternion newWorldRot = Handles.RotationHandle(worldRot, worldPos);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(splineContainer, "Rotate Spline Knot");
                knot.Rotation = (quaternion)(Quaternion.Inverse(gen.transform.rotation) * newWorldRot);
                spline.SetKnot(i, knot);
                EditorUtility.SetDirty(splineContainer);
                gen.BuildTrack();
            }
        }
    }

    private static void AddKnotAtEnd(Spline spline)
    {
        if (spline.Count == 0)
        {
            spline.Add(new BezierKnot(float3.zero));
            return;
        }

        // Place the new knot one unit ahead of the last one along its forward direction
        BezierKnot last         = spline[spline.Count - 1];
        float3      lastForward = math.mul(last.Rotation, math.float3(0, 0, 1));
        float3      newPos      = last.Position + lastForward * 2f;
        spline.Add(new BezierKnot(newPos, last.TangentIn, last.TangentOut, last.Rotation));
    }

    // ── Inspector ─────────────────────────────────────────────

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        GUILayout.Space(10);
        if (GUILayout.Button("Rebuild Track", GUILayout.Height(32)))
            ((RailwayGenerator)target).BuildTrack();
    }
}
#endif
