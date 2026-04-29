// VoxelizeObject.cs
using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class VoxelizeObject : MonoBehaviour
{
    [Header("Voxel Settings")]
    public float voxelSize = 0.1f;

    [Header("Mode")]
    [Tooltip("Surface Only = huge cube reduction. Fill = solid interior.")]
    public VoxelMode mode = VoxelMode.SurfaceOnly;

    [Tooltip("Only used in SurfaceWithThickness mode")]
    [Range(1, 10)]
    public int shellThickness = 1;

    [Header("Prefab & Parent")]
    public GameObject cubePrefab;
    public Transform voxelParent;

    [Header("Color Settings")]
    public bool colorFromTexture = true;
    public bool useEmissiveColor = false;

    [Header("Optimization")]
    [Tooltip("Combines all cubes into one mesh for massive performance gain")]
    public bool combineMeshes = true;

    [Header("Preview")]
    public bool showPreview = true;
    public Color previewColor = new Color(0, 1, 0, 0.3f);

    public enum VoxelMode
    {
        SurfaceOnly,
        SurfaceWithThickness,
        Fill
    }

    private List<VoxelData> cachedVoxels = new List<VoxelData>();

    [System.Serializable]
    public struct VoxelData
    {
        public Vector3 position;
        public Color color;
    }

    // ─────────────────────────────────────────────
    //  VOXEL CALCULATION
    // ─────────────────────────────────────────────
    public void CalculateVoxels()
    {
        MeshFilter mf = GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return;

        cachedVoxels.Clear();
        Mesh mesh = mf.sharedMesh;
        Bounds bounds = mesh.bounds;

        Texture2D albedo = null;
        Renderer rend = GetComponent<Renderer>();
        if (colorFromTexture && rend?.sharedMaterial != null)
            albedo = rend.sharedMaterial.mainTexture as Texture2D;

        int gx = Mathf.CeilToInt(bounds.size.x / voxelSize);
        int gy = Mathf.CeilToInt(bounds.size.y / voxelSize);
        int gz = Mathf.CeilToInt(bounds.size.z / voxelSize);

        bool[,,] inside = new bool[gx, gy, gz];

        for (int ix = 0; ix < gx; ix++)
            for (int iy = 0; iy < gy; iy++)
                for (int iz = 0; iz < gz; iz++)
                {
                    Vector3 local = bounds.min + new Vector3(
                        (ix + 0.5f) * voxelSize,
                        (iy + 0.5f) * voxelSize,
                        (iz + 0.5f) * voxelSize);

                    inside[ix, iy, iz] = IsInsideMesh(transform.TransformPoint(local), mesh, transform);
                }

        for (int ix = 0; ix < gx; ix++)
            for (int iy = 0; iy < gy; iy++)
                for (int iz = 0; iz < gz; iz++)
                {
                    if (!inside[ix, iy, iz]) continue;

                    bool spawn = mode switch
                    {
                        VoxelMode.Fill => true,
                        VoxelMode.SurfaceOnly => IsSurface(inside, ix, iy, iz, gx, gy, gz),
                        VoxelMode.SurfaceWithThickness => IsWithinShell(inside, ix, iy, iz, gx, gy, gz, shellThickness),
                        _ => false
                    };

                    if (spawn)
                    {
                        Vector3 local = bounds.min + new Vector3(
                            (ix + 0.5f) * voxelSize,
                            (iy + 0.5f) * voxelSize,
                            (iz + 0.5f) * voxelSize);

                        Color c = Color.white;
                        if (albedo != null)
                            c = SampleColor(local, mesh, albedo);

                        cachedVoxels.Add(new VoxelData { position = transform.TransformPoint(local), color = c });
                    }
                }
    }

    // ─────────────────────────────────────────────
    //  GENERATION
    // ─────────────────────────────────────────────
    public void Generate()
    {
        CalculateVoxels();
        ClearExistingVoxels();

        if (cachedVoxels.Count == 0) return;

        if (voxelParent == null)
        {
            voxelParent = new GameObject(gameObject.name + "_VoxelParent").transform;
            voxelParent.position = transform.position;
        }

        var cubeObjects = new List<GameObject>();

        foreach (var v in cachedVoxels)
        {
            GameObject cube = cubePrefab != null
                ? Instantiate(cubePrefab, v.position, Quaternion.identity, voxelParent)
                : GameObject.CreatePrimitive(PrimitiveType.Cube);

            cube.transform.position = v.position;
            cube.transform.localScale = Vector3.one * voxelSize;
            cube.transform.SetParent(voxelParent);

            if (colorFromTexture)
            {
                var r = cube.GetComponent<Renderer>();
                if (r != null)
                {
                    Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    mat.color = v.color;
                    if (useEmissiveColor)
                    {
                        mat.EnableKeyword("_EMISSION");
                        mat.SetColor("_EmissionColor", v.color);
                    }
                    r.sharedMaterial = mat;
                }
            }
            cubeObjects.Add(cube);
        }

        if (combineMeshes)
        {
            CombineVoxels(cubeObjects);
        }

        Debug.Log($"Successfully generated {cachedVoxels.Count} voxels.");
    }

    public void ClearExistingVoxels()
    {
        if (voxelParent != null)
        {
            if (Application.isPlaying)
                Destroy(voxelParent.gameObject);
            else
                DestroyImmediate(voxelParent.gameObject);
        }
    }

    private void CombineVoxels(List<GameObject> cubes)
    {
        if (cubes.Count == 0) return;

        // Group by material to handle multiple materials if needed
        Dictionary<Material, List<CombineInstance>> combineGroups = new Dictionary<Material, List<CombineInstance>>();

        foreach (var cube in cubes)
        {
            var mf = cube.GetComponent<MeshFilter>();
            var mr = cube.GetComponent<MeshRenderer>();
            if (mf == null || mr == null) continue;

            if (!combineGroups.ContainsKey(mr.sharedMaterial))
                combineGroups[mr.sharedMaterial] = new List<CombineInstance>();

            CombineInstance ci = new CombineInstance();
            ci.mesh = mf.sharedMesh;
            ci.transform = mf.transform.localToWorldMatrix;
            combineGroups[mr.sharedMaterial].Add(ci);
        }

        GameObject combinedRoot = new GameObject(gameObject.name + "_Combined");
        combinedRoot.transform.SetParent(voxelParent);
        combinedRoot.transform.position = transform.position;

        foreach (var group in combineGroups)
        {
            GameObject sub = new GameObject("VoxelBatch_" + group.Key.name);
            sub.transform.SetParent(combinedRoot.transform);
            var mf = sub.AddComponent<MeshFilter>();
            var mr = sub.AddComponent<MeshRenderer>();
            
            Mesh combinedMesh = new Mesh();
            combinedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            combinedMesh.CombineMeshes(group.Value.ToArray(), true, true);
            
            mf.sharedMesh = combinedMesh;
            mr.sharedMaterial = group.Key;
        }

        foreach (var c in cubes) DestroyImmediate(c);
    }

    // ─────────────────────────────────────────────
    //  HELPERS
    // ─────────────────────────────────────────────
    static bool IsSurface(bool[,,] grid, int x, int y, int z, int gx, int gy, int gz)
    {
        return !Get(grid, x - 1, y, z, gx, gy, gz) || !Get(grid, x + 1, y, z, gx, gy, gz)
            || !Get(grid, x, y - 1, z, gx, gy, gz) || !Get(grid, x, y + 1, z, gx, gy, gz)
            || !Get(grid, x, y, z - 1, gx, gy, gz) || !Get(grid, x, y, z + 1, gx, gy, gz);
    }

    static bool IsWithinShell(bool[,,] grid, int x, int y, int z, int gx, int gy, int gz, int thickness)
    {
        for (int dx = -thickness; dx <= thickness; dx++)
            for (int dy = -thickness; dy <= thickness; dy++)
                for (int dz = -thickness; dz <= thickness; dz++)
                {
                    if (!Get(grid, x + dx, y + dy, z + dz, gx, gy, gz)) return true;
                }
        return false;
    }

    static bool Get(bool[,,] g, int x, int y, int z, int gx, int gy, int gz)
    {
        if (x < 0 || y < 0 || z < 0 || x >= gx || y >= gy || z >= gz) return false;
        return g[x, y, z];
    }

    bool IsInsideMesh(Vector3 worldPoint, Mesh mesh, Transform t)
    {
        Ray ray = new Ray(worldPoint, Vector3.right);
        int hits = 0;
        var verts = mesh.vertices;
        var tris = mesh.triangles;

        for (int i = 0; i < tris.Length; i += 3)
        {
            Vector3 v0 = t.TransformPoint(verts[tris[i]]);
            Vector3 v1 = t.TransformPoint(verts[tris[i + 1]]);
            Vector3 v2 = t.TransformPoint(verts[tris[i + 2]]);
            if (RayHitsTriangle(ray, v0, v1, v2)) hits++;
        }
        return hits % 2 == 1;
    }

    bool RayHitsTriangle(Ray ray, Vector3 v0, Vector3 v1, Vector3 v2)
    {
        Vector3 e1 = v1 - v0, e2 = v2 - v0;
        Vector3 h = Vector3.Cross(ray.direction, e2);
        float a = Vector3.Dot(e1, h);
        if (Mathf.Abs(a) < 1e-6f) return false;
        float f = 1f / a;
        Vector3 s = ray.origin - v0;
        float u = f * Vector3.Dot(s, h);
        if (u < 0f || u > 1f) return false;
        Vector3 q = Vector3.Cross(s, e1);
        float v = f * Vector3.Dot(ray.direction, q);
        if (v < 0f || u + v > 1f) return false;
        return f * Vector3.Dot(e2, q) > 1e-6f;
    }

    Color SampleColor(Vector3 localPt, Mesh mesh, Texture2D tex)
    {
        var verts = mesh.vertices;
        var tris = mesh.triangles;
        var uvs = mesh.uv;
        float minD = float.MaxValue;
        Vector2 bestUV = Vector2.zero;

        for (int i = 0; i < tris.Length; i += 3)
        {
            Vector3 c = (verts[tris[i]] + verts[tris[i + 1]] + verts[tris[i + 2]]) / 3f;
            float d = Vector3.Distance(localPt, c);
            if (d < minD) { minD = d; bestUV = (uvs[tris[i]] + uvs[tris[i + 1]] + uvs[tris[i + 2]]) / 3f; }
        }
        return tex.GetPixelBilinear(bestUV.x, bestUV.y);
    }

    // ─────────────────────────────────────────────
    //  GIZMOS (PREVIEW)
    // ─────────────────────────────────────────────
    private void OnDrawGizmosSelected()
    {
        if (!showPreview || cachedVoxels.Count == 0) return;

        foreach (var v in cachedVoxels)
        {
            Gizmos.color = colorFromTexture ? v.color : previewColor;
            Gizmos.DrawCube(v.position, Vector3.one * voxelSize);
        }
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(VoxelizeObject))]
public class VoxelizeObjectEditor : Editor
{
    public override void OnInspectorGUI()
    {
        VoxelizeObject script = (VoxelizeObject)target;

        EditorGUI.BeginChangeCheck();
        DrawDefaultInspector();
        if (EditorGUI.EndChangeCheck())
        {
            // Optional: Auto-recalculate on change if voxel count isn't too high
        }

        GUILayout.Space(15);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Preview Voxels", GUILayout.Height(30)))
        {
            script.CalculateVoxels();
            SceneView.RepaintAll();
        }
        if (GUILayout.Button("Clear All", GUILayout.Height(30)))
        {
            script.ClearExistingVoxels();
        }
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(5);
        GUI.backgroundColor = Color.green;
        if (GUILayout.Button("GENERATE 3D TO CUBE", GUILayout.Height(45)))
        {
            script.Generate();
        }
        GUI.backgroundColor = Color.white;

        if (script.GetComponent<MeshFilter>() == null)
        {
            EditorGUILayout.HelpBox("Please add a MeshFilter to this object to voxelize it.", MessageType.Warning);
        }
    }
}
#endif
