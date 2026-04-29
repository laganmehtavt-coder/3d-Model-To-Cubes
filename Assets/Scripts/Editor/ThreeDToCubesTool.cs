using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

public class ThreeDToCubesTool : EditorWindow {
    private GameObject sourceModel;
    private GameObject cubePrefab;

    private float cubeSize = 0.12f;
    private bool fillSolid = true;
    private bool hollowOut = false;
    private float brightness = 1.0f;
    private bool xRayMode = false;

    private List<ColorMappingStats> colorStats = new List<ColorMappingStats>();
    private Vector2 paletteScroll;
    private float colorTolerance = 0.05f;

    // Interactive Preview State
    private PreviewRenderUtility previewRenderUtility;
    private Vector2 previewDir = new Vector2(135, -30);
    private Vector3 previewPivot = Vector3.zero;
    private float previewZoom = 5f;
    private int activeGroupId = 1;
    private Color activeColor = Color.green;

    private Material previewMaterial;
    private Dictionary<Color, Material> materialCache = new Dictionary<Color, Material>();

    [System.Serializable]
    private class ColorMappingStats {
        public Color originalColor;
        public Color overrideColor;
        public int count;
        public int groupId;
    }

    private struct VoxelInfo {
        public Color color;
        public int groupId;
        public Vector3 position;
    }

    private Dictionary<Vector3Int, VoxelInfo> voxels = new Dictionary<Vector3Int, VoxelInfo>();

    [MenuItem("Tools/3D TO CUBES PRO")]
    public static void Open() {
        var window = GetWindow<ThreeDToCubesTool>("3D to Cubes Pro - God Mode");
        window.minSize = new Vector2(1200, 800);
    }

    private void OnEnable() {
        if (previewRenderUtility == null) previewRenderUtility = new PreviewRenderUtility();
        
        Shader s = Shader.Find("Unlit/Color");
        if (s == null) s = Shader.Find("Standard");
        previewMaterial = new Material(s);
        
        RefreshPreview();
    }

    private void OnDisable() {
        if (previewRenderUtility != null) previewRenderUtility.Cleanup();
        ClearMaterialCache();
        if (previewMaterial) DestroyImmediate(previewMaterial);
    }

    private void ClearMaterialCache() {
        foreach (var mat in materialCache.Values) if (mat) DestroyImmediate(mat);
        materialCache.Clear();
    }

    private void OnGUI() {
        GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel) {
            fontSize = 20,
            alignment = TextAnchor.MiddleCenter,
            fixedHeight = 40,
            normal = { textColor = new Color(0, 0.8f, 1f) }
        };
        GUILayout.Label("3D TO CUBES PRO - INTERACTIVE EDITOR", headerStyle);

        EditorGUILayout.BeginHorizontal();

        // SIDEBAR
        EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(350), GUILayout.ExpandHeight(true));
        DrawSidebar();
        EditorGUILayout.EndVertical();

        // MAIN VIEW (Dual Preview)
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
        
        // Original Model View
        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        GUILayout.Label("SOURCE MODEL", EditorStyles.centeredGreyMiniLabel);
        Rect sourceRect = GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        if (sourceModel) {
            Editor sourceEditor = Editor.CreateEditor(sourceModel);
            sourceEditor.OnInteractivePreviewGUI(sourceRect, EditorStyles.helpBox);
        } else GUI.Box(sourceRect, "Assign Source Model", EditorStyles.centeredGreyMiniLabel);
        EditorGUILayout.EndVertical();

        // Voxel Preview View
        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        GUILayout.Label("VOXEL PREVIEW (Interactive Paint)", EditorStyles.centeredGreyMiniLabel);
        DrawInteractivePreview();
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawSidebar() {
        GUILayout.Label("VOXELIZATION SETTINGS", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        sourceModel = (GameObject)EditorGUILayout.ObjectField("Base Model", sourceModel, typeof(GameObject), true);
        cubePrefab = (GameObject)EditorGUILayout.ObjectField("Cube Prefab", cubePrefab, typeof(GameObject), false);
        
        EditorGUILayout.Space(5);
        cubeSize = EditorGUILayout.Slider("Cube Detail", cubeSize, 0.02f, 1.0f);
        
        using (new EditorGUILayout.HorizontalScope()) {
            fillSolid = EditorGUILayout.ToggleLeft("SOLID FILL", fillSolid, GUILayout.Width(100));
            hollowOut = EditorGUILayout.ToggleLeft("HOLLOW", hollowOut, GUILayout.Width(80));
            xRayMode = EditorGUILayout.ToggleLeft("X-RAY", xRayMode, GUILayout.Width(80));
        }

        if (EditorGUI.EndChangeCheck()) RefreshPreview();

        GUILayout.Space(15);
        GUILayout.Label("PAINT BRUSH", EditorStyles.boldLabel);
        activeColor = EditorGUILayout.ColorField("Color", activeColor);
        activeGroupId = EditorGUILayout.IntField("Group ID", activeGroupId);
        
        EditorGUILayout.HelpBox("RIGHT CLICK: Paint single cube\nRIGHT DRAG: Paint surface\nSCROLL: Zoom", MessageType.None);

        GUILayout.Space(10);
        GUILayout.Label("COLOR PALETTE", EditorStyles.boldLabel);
        paletteScroll = EditorGUILayout.BeginScrollView(paletteScroll, GUILayout.Height(200));
        foreach (var stats in colorStats) {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.ColorField(GUIContent.none, stats.overrideColor, false, false, false, GUILayout.Width(40));
            EditorGUILayout.LabelField($"{stats.count} Cubes", EditorStyles.miniBoldLabel);
            if (GUILayout.Button("Pick", GUILayout.Width(45))) {
                activeColor = stats.overrideColor;
                activeGroupId = stats.groupId;
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        GUILayout.FlexibleSpace();
        GUILayout.Label($"LIVE CUBE COUNT: {voxels.Count:N0}", EditorStyles.boldLabel);

        GUI.backgroundColor = new Color(0, 0.8f, 0.2f);
        if (GUILayout.Button("GENERATE MODEL", GUILayout.Height(50))) StartVoxelization();
        GUI.backgroundColor = Color.white;
    }

    private void DrawInteractivePreview() {
        Rect rect = GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        if (rect.width <= 1) return;

        previewRenderUtility.BeginPreview(rect, EditorStyles.helpBox);
        HandleInput(rect);

        previewRenderUtility.camera.transform.position = previewPivot + Quaternion.Euler(previewDir.y, previewDir.x, 0) * (Vector3.back * previewZoom);
        previewRenderUtility.camera.transform.LookAt(previewPivot);
        previewRenderUtility.camera.backgroundColor = new Color(0.12f, 0.12f, 0.12f, 1);
        previewRenderUtility.camera.farClipPlane = 1000f;

        Mesh cubeMesh = cubePrefab ? cubePrefab.GetComponentInChildren<MeshFilter>()?.sharedMesh : null;
        if (!cubeMesh) cubeMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");

        foreach (var v in voxels.Values) {
            Material mat = GetCachedMaterial(v.color);
            Matrix4x4 trs = Matrix4x4.TRS(v.position, Quaternion.identity, Vector3.one * cubeSize * 0.95f);
            previewRenderUtility.DrawMesh(cubeMesh, trs, mat, 0);
        }

        previewRenderUtility.Render();
        Texture result = previewRenderUtility.EndPreview();
        GUI.DrawTexture(rect, result, ScaleMode.StretchToFill, false);
        Repaint();
    }

    private Material GetCachedMaterial(Color c) {
        if (!materialCache.TryGetValue(c, out Material mat)) {
            mat = new Material(previewMaterial);
            mat.color = c * brightness;
            if (xRayMode) {
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.renderQueue = 3000;
                mat.color = new Color(mat.color.r, mat.color.g, mat.color.b, 0.35f);
            }
            materialCache[c] = mat;
        }
        return mat;
    }

    private void HandleInput(Rect rect) {
        Event e = Event.current;
        if (!rect.Contains(e.mousePosition)) return;

        if (e.type == EventType.MouseDown && e.button == 1) { PaintVoxel(e.mousePosition, rect); e.Use(); }
        else if (e.type == EventType.MouseDrag) {
            if (e.button == 0) { previewDir += e.delta * 0.5f; e.Use(); }
            else if (e.button == 1) { PaintVoxel(e.mousePosition, rect); e.Use(); }
        } else if (e.type == EventType.ScrollWheel) {
            previewZoom += e.delta.y * 0.1f;
            e.Use();
        }
    }

    private void PaintVoxel(Vector2 mousePos, Rect rect) {
        Ray ray = previewRenderUtility.camera.ScreenPointToRay(new Vector2(mousePos.x - rect.x, rect.height - (mousePos.y - rect.y)));
        Vector3Int bestKey = Vector3Int.zero; float minD = float.MaxValue; bool hit = false;

        foreach (var kvp in voxels) {
            if (new Bounds(kvp.Value.position, Vector3.one * cubeSize).IntersectRay(ray, out float d)) {
                if (d < minD) { minD = d; bestKey = kvp.Key; hit = true; }
            }
        }
        if (hit) {
            var v = voxels[bestKey]; v.color = activeColor; v.groupId = activeGroupId; voxels[bestKey] = v;
            UpdateStatsOnly();
        }
    }

    private void RefreshPreview() {
        voxels.Clear();
        ClearMaterialCache();
        if (!sourceModel) return;

        GameObject temp = Instantiate(sourceModel);
        temp.transform.position = Vector3.zero;
        temp.transform.rotation = Quaternion.identity;
        
        foreach (var r in temp.GetComponentsInChildren<Renderer>()) {
            Mesh m = r is MeshRenderer mr ? mr.GetComponent<MeshFilter>()?.sharedMesh : r is SkinnedMeshRenderer smr ? smr.sharedMesh : null;
            if (m && !r.GetComponent<Collider>()) r.gameObject.AddComponent<MeshCollider>().sharedMesh = m;
        }

        Bounds b = GetBoundsFromObj(temp);
        previewPivot = b.center;
        previewZoom = b.size.magnitude * 1.5f;

        PerformScan(b);
        DestroyImmediate(temp);
        ScanPalette();
    }

    private void PerformScan(Bounds b) {
        float step = cubeSize;
        // Efficient scanning from front and back
        for (float x = b.min.x + step * 0.5f; x <= b.max.x; x += step)
            for (float y = b.min.y + step * 0.5f; y <= b.max.y; y += step) {
                Ray ray = new Ray(new Vector3(x, y, b.min.z - 1f), Vector3.forward);
                var hits = Physics.RaycastAll(ray, b.size.z + 2f).OrderBy(h => h.distance).ToList();
                
                if (hits.Count > 0) {
                    if (fillSolid && hits.Count >= 2) {
                        for (int i = 0; i < hits.Count; i += 2) {
                            float startZ = hits[i].point.z;
                            float endZ = (i + 1 < hits.Count) ? hits[i+1].point.z : hits[i].point.z;
                            Color c = GetColorAtHit(hits[i]);
                            for (float z = startZ; z <= endZ + step * 0.1f; z += step) {
                                AddVoxel(new Vector3(x, y, z), c, b);
                            }
                        }
                    } else {
                        foreach (var hit in hits) AddVoxel(hit.point, GetColorAtHit(hit), b);
                    }
                }
            }
        if (hollowOut) HollowOutLogic();
    }

    private void AddVoxel(Vector3 p, Color c, Bounds b) {
        Vector3Int gp = WorldToGrid(p, b);
        if (!voxels.ContainsKey(gp)) voxels[gp] = new VoxelInfo { position = GridToWorld(gp, b), color = c, groupId = 1 };
    }

    private void HollowOutLogic() {
        var keys = voxels.Keys.ToList(); HashSet<Vector3Int> ins = new HashSet<Vector3Int>();
        foreach (var k in keys)
            if (voxels.ContainsKey(k + Vector3Int.up) && voxels.ContainsKey(k + Vector3Int.down) && voxels.ContainsKey(k + Vector3Int.left) && voxels.ContainsKey(k + Vector3Int.right) && voxels.ContainsKey(k + Vector3Int.forward) && voxels.ContainsKey(k + Vector3Int.back)) ins.Add(k);
        foreach (var k in ins) voxels.Remove(k);
    }

    private Vector3Int WorldToGrid(Vector3 p, Bounds b) => new Vector3Int(Mathf.RoundToInt((p.x - b.min.x) / cubeSize), Mathf.RoundToInt((p.y - b.min.y) / cubeSize), Mathf.RoundToInt((p.z - b.min.z) / cubeSize));
    private Vector3 GridToWorld(Vector3Int g, Bounds b) => b.min + new Vector3(g.x * cubeSize + cubeSize * 0.5f, g.y * cubeSize + cubeSize * 0.5f, g.z * cubeSize + cubeSize * 0.5f);

    private Color GetColorAtHit(RaycastHit hit) {
        Renderer r = hit.collider.GetComponent<Renderer>(); if (!r || !r.sharedMaterial) return Color.gray;
        Texture2D tex = r.sharedMaterial.mainTexture as Texture2D;
        if (tex && hit.textureCoord != Vector2.zero) return tex.GetPixelBilinear(hit.textureCoord.x, hit.textureCoord.y);
        return r.sharedMaterial.HasProperty("_BaseColor") ? r.sharedMaterial.GetColor("_BaseColor") : r.sharedMaterial.color;
    }

    private void ScanPalette() {
        Dictionary<Color, int> counts = new Dictionary<Color, int>();
        foreach (var v in voxels.Values) {
            Color c = v.color; bool found = false;
            foreach (var key in counts.Keys) if (Vector4.Distance((Vector4)key, (Vector4)c) < colorTolerance) { counts[key]++; found = true; break; }
            if (!found) counts[c] = 1;
        }
        colorStats = counts.Select(kvp => new ColorMappingStats { originalColor = kvp.Key, overrideColor = kvp.Key, count = kvp.Value, groupId = 1 }).OrderByDescending(s => s.count).ToList();
    }

    private void UpdateStatsOnly() {
        foreach (var s in colorStats) s.count = 0;
        foreach (var v in voxels.Values) {
            var stat = colorStats.FirstOrDefault(s => Vector4.Distance((Vector4)s.overrideColor, (Vector4)v.color) < colorTolerance);
            if (stat != null) stat.count++;
        }
    }

    private void StartVoxelization() {
        if (!cubePrefab || voxels.Count == 0) return;
        GameObject root = new GameObject(sourceModel.name + "_Voxelized");
        root.transform.position = sourceModel.transform.position;
        
        Shader s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Dictionary<Color, Material> genCache = new Dictionary<Color, Material>();

        foreach (var v in voxels.Values) {
            GameObject cube = (GameObject)PrefabUtility.InstantiatePrefab(cubePrefab, root.transform);
            cube.transform.position = v.position; cube.transform.localScale = Vector3.one * cubeSize * 0.98f;
            
            if (!genCache.TryGetValue(v.color, out Material m)) {
                m = new Material(s) { color = v.color }; if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", v.color);
                genCache[v.color] = m;
            }
            
            foreach (var rend in cube.GetComponentsInChildren<Renderer>()) rend.sharedMaterial = m;
            var b = cube.GetComponent<Block>(); if (b) { b.blockColor = v.color; b.groupId = v.groupId; }
        }
        Selection.activeGameObject = root;
    }

    private Bounds GetBoundsFromObj(GameObject obj) {
        Bounds b = new Bounds(); var rs = obj.GetComponentsInChildren<Renderer>();
        if (rs.Length > 0) { b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); }
        return b;
    }
}
