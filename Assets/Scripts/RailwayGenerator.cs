// ============================================================
// RailwayGenerator.cs (FIXED)
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

    private SplineContainer _splineContainer;

    private GameObject _leftRailGO;
    private GameObject _rightRailGO;
    private GameObject _sleepersRoot;

    private bool _isBuilding = false;

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

    public void BuildTrack()
    {
        if (this == null || !this || gameObject == null)
            return;

        if (_isBuilding) return;
        _isBuilding = true;

        _splineContainer = GetComponent<SplineContainer>();

        if (_splineContainer == null || _splineContainer.Spline == null)
        {
            _isBuilding = false;
            return;
        }

        if (_leftRailGO != null) DestroyImmediate(_leftRailGO);
        if (_rightRailGO != null) DestroyImmediate(_rightRailGO);
        if (_sleepersRoot != null) DestroyImmediate(_sleepersRoot);

        _leftRailGO = CreateChild("__RailwayGen_Left__");
        _rightRailGO = CreateChild("__RailwayGen_Right__");
        _sleepersRoot = CreateChild("__RailwayGen_Sleepers__");

        List<TrackFrame> frames = SampleSpline(resolution);

        Mesh leftMesh = BuildRailMesh(frames, -trackWidth * 0.5f);
        Mesh rightMesh = BuildRailMesh(frames, trackWidth * 0.5f);

        AssignMesh(_leftRailGO, leftMesh, railMaterial);
        AssignMesh(_rightRailGO, rightMesh, railMaterial);

        SpawnSleepers(frames);

        _isBuilding = false;
    }

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

            Vector3 worldPos = transform.TransformPoint((Vector3)pos);
            Vector3 worldForward = transform.TransformDirection(((Vector3)tangent).normalized);
            Vector3 worldUp = transform.TransformDirection(((Vector3)upVec).normalized);

            Vector3 right = Vector3.Cross(worldUp, worldForward).normalized;
            Vector3 up = worldUp.normalized;

            frames.Add(new TrackFrame
            {
                Position = worldPos,
                Forward = worldForward,
                Up = up,
                Right = right,
                T = t
            });
        }

        return frames;
    }

    private Mesh BuildRailMesh(List<TrackFrame> frames, float lateralOffset)
    {
        int sectionCount = frames.Count;
        int vertCount = sectionCount * 4;

        var vertices = new Vector3[vertCount];
        var normals = new Vector3[vertCount];
        var uvs = new Vector2[vertCount];
        var triangles = new List<int>();

        float halfRailW = railWidth * 0.5f;

        for (int i = 0; i < sectionCount; i++)
        {
            TrackFrame f = frames[i];

            Vector3 centre = f.Position + f.Right * lateralOffset;

            Vector3 bottomLeft = centre - f.Right * halfRailW - f.Up * railHeight;
            Vector3 bottomRight = centre + f.Right * halfRailW - f.Up * railHeight;
            Vector3 topLeft = centre - f.Right * halfRailW;
            Vector3 topRight = centre + f.Right * halfRailW;

            int base4 = i * 4;

            vertices[base4 + 0] = bottomLeft;
            vertices[base4 + 1] = bottomRight;
            vertices[base4 + 2] = topLeft;
            vertices[base4 + 3] = topRight;

            normals[base4 + 0] = f.Up;
            normals[base4 + 1] = f.Up;
            normals[base4 + 2] = f.Up;
            normals[base4 + 3] = f.Up;

            float u = f.T * 10f;

            uvs[base4 + 0] = new Vector2(u, 0f);
            uvs[base4 + 1] = new Vector2(u, 1f);
            uvs[base4 + 2] = new Vector2(u, 0f);
            uvs[base4 + 3] = new Vector2(u, 1f);

            if (i > 0)
            {
                int prev = (i - 1) * 4;
                int curr = i * 4;

                AddQuad(triangles, prev + 2, prev + 3, curr + 2, curr + 3);
                AddQuad(triangles, curr + 0, curr + 1, prev + 0, prev + 1);
                AddQuad(triangles, prev + 0, prev + 2, curr + 0, curr + 2);
                AddQuad(triangles, curr + 1, curr + 3, prev + 1, prev + 3);
            }
        }

        AddQuad(triangles, 2, 3, 0, 1);

        int last = (sectionCount - 1) * 4;
        AddQuad(triangles, last + 0, last + 1, last + 2, last + 3);

        Mesh mesh = new Mesh();
        mesh.name = "Rail";
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateBounds();

        return mesh;
    }

    private static void AddQuad(List<int> tris, int a, int b, int c, int d)
    {
        tris.Add(a); tris.Add(b); tris.Add(c);
        tris.Add(b); tris.Add(d); tris.Add(c);
    }

    private void SpawnSleepers(List<TrackFrame> frames)
    {
        if (sleeperPrefab == null) return;

        float accumulated = 0f;
        bool firstSpawned = false;

        for (int i = 1; i < frames.Count; i++)
        {
            float segmentLength = Vector3.Distance(frames[i - 1].Position, frames[i].Position);
            accumulated += segmentLength;

            if (!firstSpawned || accumulated >= sleeperSpacing)
            {
                SpawnSleeper(frames[i]);
                accumulated = 0f;
                firstSpawned = true;
            }
        }
    }

    private void SpawnSleeper(TrackFrame frame)
    {
        Quaternion rot = Quaternion.LookRotation(frame.Forward, frame.Up);

        GameObject sleeper = Instantiate(sleeperPrefab, frame.Position, rot, _sleepersRoot.transform);
        sleeper.name = "Sleeper";
    }

    private GameObject CreateChild(string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        return go;
    }

    // ✅ FIXED METHOD (ONLY CHANGE)
    private static void AssignMesh(GameObject go, Mesh mesh, Material mat)
    {
        if (go == null) return;

        MeshFilter mf = go.GetComponent<MeshFilter>();
        if (mf == null)
            mf = go.AddComponent<MeshFilter>();

        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        if (mr == null)
            mr = go.AddComponent<MeshRenderer>();

        if (mf != null)
            mf.sharedMesh = mesh;

        if (mr != null)
            mr.sharedMaterial = mat;
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(RailwayGenerator))]
public class RailwayGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GUILayout.Space(10);

        if (GUILayout.Button("Rebuild Track", GUILayout.Height(32)))
        {
            ((RailwayGenerator)target).BuildTrack();
        }
    }
}
#endif
