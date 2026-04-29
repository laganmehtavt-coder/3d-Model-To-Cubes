using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

public class ThreeDToCubesTool : EditorWindow {
    private GameObject sourceModel;
    private GameObject cubePrefab;
    private Material voxelMaterial;

    private float cubeSize = 0.12f;
    private Vector3 gaps = Vector3.zero;
    private bool fillInside = true;
    private bool hollowOut = true;
    private float brightness = 1.0f;

    private List<ColorMapping> colorMappings = new List<ColorMapping>();
    private Vector2 paletteScroll;
    private float colorTolerance = 0.05f;

    private Editor voxelEditor;
    private Editor sourceEditor;
    private GameObject previewRoot;

    [System.Serializable]
    public class ColorMapping { public Color originalColor; public Color overrideColor; }

    private struct VoxelInfo {
        public Color color;
        public int groupId;
    }

    private Dictionary<Vector3Int, VoxelInfo> voxels = new Dictionary<Vector3Int, VoxelInfo>();
    private List<Renderer> sourceRenderers = new List<Renderer>();

    [MenuItem("Tools/3D TO CUBES PRO")]
    public static void Open() {
        var window = GetWindow<ThreeDToCubesTool>("3D to Cubes Pro Ultra");
        window.minSize = new Vector2(1100, 750);
    }

    private void OnEnable() => RefreshPreview();
    private void OnDisable() => Cleanup();

    private void OnGUI() {
        GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel) {
            fontSize = 22,
            alignment = TextAnchor.MiddleCenter,
            fixedHeight = 50,
            normal = { textColor = new Color(0, 1f, 0.9f) }
        };
        GUILayout.Label("3D TO CUBES PRO - COMPLETE COLOR SCANNER", headerStyle);

        EditorGUILayout.BeginHorizontal();

        // SIDEBAR
        EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(320), GUILayout.ExpandHeight(true));

        GUILayout.Label("SCANNER SETTINGS", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();

        sourceModel = (GameObject)EditorGUILayout.ObjectField("Source 3D Model", sourceModel, typeof(GameObject), true);
        cubePrefab = (GameObject)EditorGUILayout.ObjectField("Cube Prefab", cubePrefab, typeof(GameObject), false);
        voxelMaterial = (Material)EditorGUILayout.ObjectField("Voxel Material", voxelMaterial, typeof(Material), false);

        EditorGUILayout.Space(5);
        using (new EditorGUILayout.HorizontalScope()) {
            EditorGUILayout.PrefixLabel("Cube Detail");
            cubeSize = EditorGUILayout.Slider(cubeSize, 0.0001f, 3.0f);
            cubeSize = EditorGUILayout.FloatField(cubeSize, GUILayout.Width(50));
        }

        gaps = EditorGUILayout.Vector3Field("Spacing Gaps", gaps);
        fillInside = EditorGUILayout.Toggle("Solid Fill", fillInside);
        hollowOut = EditorGUILayout.Toggle("Hollow (Empty Inside)", hollowOut);
        brightness = EditorGUILayout.Slider("Color Brightness", brightness, 0.5f, 2.5f);

        if (EditorGUI.EndChangeCheck())
            RefreshPreview();

        GUILayout.Label("COLOR PALETTE & OVERRIDES", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope()) {
            if (GUILayout.Button("🔍 Scan"))
                ScanPalette();
            if (GUILayout.Button("✨ Clear"))
                ClearOverrides();
        }

        if (GUILayout.Button("APPLY COLOR CHANGES", GUILayout.Height(25)))
            RefreshPreview();

        colorTolerance = EditorGUILayout.Slider("Match Tol.", colorTolerance, 0.01f, 0.3f);

        paletteScroll = EditorGUILayout.BeginScrollView(paletteScroll, GUILayout.Height(180));
        for (int i = 0; i < colorMappings.Count; i++) {
            var mapping = colorMappings[i];
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.ColorField(GUIContent.none, mapping.originalColor, false, true, false, GUILayout.Width(35));
            EditorGUILayout.LabelField("→", GUILayout.Width(15));
            EditorGUI.BeginChangeCheck();
            mapping.overrideColor = EditorGUILayout.ColorField(GUIContent.none, mapping.overrideColor, false, true, false, GUILayout.Width(35));
            if (EditorGUI.EndChangeCheck())
                RefreshPreview();
            if (GUILayout.Button("Set", GUILayout.Width(45)))
                RefreshPreview();
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        GUILayout.Label($"TOTAL CUBES: {voxels.Count:N0}",
            new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, fontSize = 13, normal = { textColor = Color.yellow } });

        GUI.backgroundColor = new Color(0, 0.9f, 0.3f);
        if (GUILayout.Button("GENERATE COMPLETE MODEL", GUILayout.Height(50))) {
            if (sourceModel && cubePrefab)
                StartVoxelization();
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.EndVertical();

        // PREVIEWS
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox, GUILayout.ExpandWidth(true));

        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        GUILayout.Label("ACTUAL MODEL", new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 14, fontStyle = FontStyle.Bold });
        Rect actualRect = GUILayoutUtility.GetRect(200, 200, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        if (sourceModel != null) {
            if (sourceEditor == null || sourceEditor.target != sourceModel)
                sourceEditor = Editor.CreateEditor(sourceModel);
            sourceEditor.OnInteractivePreviewGUI(actualRect, EditorStyles.helpBox);
        } else
            GUI.Box(actualRect, "Assign Source Model", EditorStyles.centeredGreyMiniLabel);
        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        GUILayout.Label("VOXELIZED RESULT (Prefab Preview)", new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 14, fontStyle = FontStyle.Bold });
        Rect voxelRect = GUILayoutUtility.GetRect(200, 200, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

        if (previewRoot != null) {
            if (voxelEditor == null || voxelEditor.target != previewRoot)
                voxelEditor = Editor.CreateEditor(previewRoot);
            voxelEditor.OnInteractivePreviewGUI(voxelRect, EditorStyles.helpBox);
        } else
            GUI.Box(voxelRect, "Assign Cube Prefab aur Scan karo", EditorStyles.centeredGreyMiniLabel);
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndHorizontal();
    }

    private Bounds GetBounds() {
        Bounds b = new Bounds();
        if (sourceRenderers.Count > 0) {
            b = sourceRenderers[0].bounds;
            foreach (var r in sourceRenderers)
                b.Encapsulate(r.bounds);
        }
        return b;
    }

    private void RefreshPreview() {
        if (sourceModel == null)
            return;
        Cleanup();

        GameObject scanTemp = Instantiate(sourceModel);
        scanTemp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        sourceRenderers.Clear();
        foreach (var renderer in scanTemp.GetComponentsInChildren<Renderer>()) {
            Mesh mesh = renderer is MeshRenderer mr ? mr.GetComponent<MeshFilter>()?.sharedMesh :
                        renderer is SkinnedMeshRenderer smr ? smr.sharedMesh : null;

            if (mesh != null) {
                if (!renderer.gameObject.GetComponent<MeshCollider>()) {
                    var col = renderer.gameObject.AddComponent<MeshCollider>();
                    col.sharedMesh = mesh;
                }
                sourceRenderers.Add(renderer);
            }
        }

        Bounds b = GetBounds();
        voxels.Clear();
        PerformFullScan(b);
        BuildPrefabPreview(b);

        DestroyImmediate(scanTemp);
        Repaint();
    }

    private void PerformFullScan(Bounds b) {
        float scanStep = cubeSize * 0.5f;
        Vector3 min = b.min;
        Vector3 max = b.max;

        for (float x = min.x; x <= max.x; x += scanStep)
            for (float y = min.y; y <= max.y; y += scanStep) {
                RaycastAndAdd(new Ray(new Vector3(x, y, min.z - 5f), Vector3.forward), (max.z - min.z) + 10f, b);
                RaycastAndAdd(new Ray(new Vector3(x, y, max.z + 5f), Vector3.back), (max.z - min.z) + 10f, b);
            }

        for (float z = min.z; z <= max.z; z += scanStep)
            for (float y = min.y; y <= max.y; y += scanStep) {
                RaycastAndAdd(new Ray(new Vector3(min.x - 5f, y, z), Vector3.right), (max.x - min.x) + 10f, b);
                RaycastAndAdd(new Ray(new Vector3(max.x + 5f, y, z), Vector3.left), (max.x - min.x) + 10f, b);
            }

        for (float x = min.x; x <= max.x; x += scanStep)
            for (float z = min.z; z <= max.z; z += scanStep) {
                RaycastAndAdd(new Ray(new Vector3(x, min.y - 5f, z), Vector3.up), (max.y - min.y) + 10f, b);
                RaycastAndAdd(new Ray(new Vector3(x, max.y + 5f, z), Vector3.down), (max.y - min.y) + 10f, b);
            }

        if (fillInside)
            FillInternalGaps(b);
        if (hollowOut)
            HollowOut();
    }

    private void RaycastAndAdd(Ray ray, float dist, Bounds b) {
        foreach (RaycastHit h in Physics.RaycastAll(ray, dist)) {
            if (h.collider == null)
                continue;

            Vector3Int gp = new Vector3Int(
                Mathf.RoundToInt((h.point.x - b.min.x) / cubeSize),
                Mathf.RoundToInt((h.point.y - b.min.y) / cubeSize),
                Mathf.RoundToInt((h.point.z - b.min.z) / cubeSize)
            );

            if (!voxels.ContainsKey(gp)) {
                Color c = GetHitColor(h);
                voxels[gp] = new VoxelInfo { color = c, groupId = 1 };
            }
        }
    }

    private Color GetHitColor(RaycastHit hit) {
        Renderer r = hit.collider.GetComponent<Renderer>();
        if (r == null || r.sharedMaterial == null)
            return Color.gray;

        Texture2D tex = null;
        string[] props = { "_MainTex", "_BaseMap", "_Albedo", "_BaseColorMap", "_Diffuse" };
        foreach (string p in props) {
            if (r.sharedMaterial.HasProperty(p)) {
                tex = r.sharedMaterial.GetTexture(p) as Texture2D;
                if (tex != null)
                    break;
            }
        }

        if (tex != null && hit.textureCoord != Vector2.zero) {
            try {
                string path = AssetDatabase.GetAssetPath(tex);
                if (!string.IsNullOrEmpty(path)) {
                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer != null && !importer.isReadable) {
                        importer.isReadable = true;
                        importer.SaveAndReimport();
                    }
                }
                return tex.GetPixelBilinear(hit.textureCoord.x, hit.textureCoord.y);
            } catch { }
        }

        if (r.sharedMaterial.HasProperty("_BaseColor"))
            return r.sharedMaterial.GetColor("_BaseColor");
        if (r.sharedMaterial.HasProperty("_Color"))
            return r.sharedMaterial.GetColor("_Color");

        return Color.gray;
    }

    private void BuildPrefabPreview(Bounds b) {
        if (previewRoot)
            DestroyImmediate(previewRoot);
        previewRoot = new GameObject("VoxelPrefabPreview");
        previewRoot.hideFlags = HideFlags.HideAndDontSave;

        if (cubePrefab == null)
            return;

        Material baseMat = voxelMaterial ? voxelMaterial : new Material(Shader.Find("Standard"));

        foreach (var v in voxels) {
            GameObject cube = (GameObject)PrefabUtility.InstantiatePrefab(cubePrefab, previewRoot.transform);

            Vector3 pos = b.min + new Vector3(
                v.Key.x * (cubeSize + gaps.x) + cubeSize * 0.5f,
                v.Key.y * (cubeSize + gaps.y) + cubeSize * 0.5f,
                v.Key.z * (cubeSize + gaps.z) + cubeSize * 0.5f);

            cube.transform.position = pos;
            cube.transform.localScale = Vector3.one * cubeSize * 0.98f;

            Color finalColor = ApplyOverrides(v.Value.color) * brightness;

            // === PREVIEW COLOR FIX ===
            Renderer rend = cube.GetComponentInChildren<Renderer>();
            if (rend != null) {
                // Preview scene mein MaterialPropertyBlock kaam nahi karta, 
                // isliye temporary material instance use kar rahe hain
                Material tempMat = new Material(baseMat);
                tempMat.color = finalColor;
                if (tempMat.HasProperty("_BaseColor"))
                    tempMat.SetColor("_BaseColor", finalColor);
                if (tempMat.HasProperty("_EmissionColor"))
                    tempMat.SetColor("_EmissionColor", finalColor * 0.1f);

                rend.material = tempMat;
            }
        }
    }

    private Color ApplyOverrides(Color c) {
        foreach (var m in colorMappings)
            if (Vector4.Distance((Vector4)c, (Vector4)m.originalColor) < colorTolerance)
                return m.overrideColor;
        return c;
    }

    private void ScanPalette() {
        if (voxels.Count == 0)
            return;

        List<Color> uniqueColors = new List<Color>();
        foreach (var v in voxels.Values) {
            bool found = false;
            foreach (var ex in uniqueColors) {
                if (Vector4.Distance((Vector4)v.color, (Vector4)ex) < colorTolerance) {
                    found = true;
                    break;
                }
            }
            if (!found)
                uniqueColors.Add(v.color);
        }

        colorMappings.Clear();
        foreach (var c in uniqueColors)
            colorMappings.Add(new ColorMapping { originalColor = c, overrideColor = c });
    }

    private void ClearOverrides() {
        foreach (var m in colorMappings)
            m.overrideColor = m.originalColor;
        RefreshPreview();
    }

    private void StartVoxelization() {
        if (!cubePrefab)
            return;

        GameObject root = new GameObject(sourceModel.name + "_VoxelComplete");
        Bounds b = GetBounds();
        Material mat = voxelMaterial ? new Material(voxelMaterial) : new Material(Shader.Find("Standard"));

        int count = 0;
        foreach (var v in voxels) {
            count++;
            if (count % 500 == 0)
                EditorUtility.DisplayProgressBar("Generating...", $"{count}/{voxels.Count}", count / (float)voxels.Count);

            GameObject cube = (GameObject)PrefabUtility.InstantiatePrefab(cubePrefab, root.transform);

            Vector3 pos = b.min + new Vector3(
                v.Key.x * (cubeSize + gaps.x) + cubeSize * 0.5f,
                v.Key.y * (cubeSize + gaps.y) + cubeSize * 0.5f,
                v.Key.z * (cubeSize + gaps.z) + cubeSize * 0.5f);

            cube.transform.position = pos;
            cube.transform.localScale = Vector3.one * cubeSize * 0.98f;

            Color finalColor = ApplyOverrides(v.Value.color) * brightness;

            Renderer rend = cube.GetComponentInChildren<Renderer>();
            if (rend != null) {
                rend.sharedMaterial = mat;
                MaterialPropertyBlock mpb = new MaterialPropertyBlock();
                mpb.SetColor("_Color", finalColor);
                if (rend.sharedMaterial.HasProperty("_BaseColor"))
                    mpb.SetColor("_BaseColor", finalColor);
                rend.SetPropertyBlock(mpb);
            }

            // Custom component support (agar apka 'Block' script hai)
            /*
            var block = cube.GetComponent<Block>();
            if (block != null)
            {
                block.blockColor = finalColor;
            }
            */
        }

        EditorUtility.ClearProgressBar();
        Selection.activeGameObject = root;
        Debug.Log($"SUCCESS: Generated {voxels.Count} cubes with colors!");
    }

    private void FillInternalGaps(Bounds b) {
        var keys = voxels.Keys.ToList();
        if (keys.Count < 2)
            return;

        int minX = keys.Min(k => k.x), maxX = keys.Max(k => k.x);
        int minY = keys.Min(k => k.y), maxY = keys.Max(k => k.y);

        for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++) {
                var zInColumn = keys.Where(k => k.x == x && k.y == y).OrderBy(k => k.z).ToList();
                if (zInColumn.Count >= 2) {
                    int firstZ = zInColumn[0].z;
                    int lastZ = zInColumn[zInColumn.Count - 1].z;
                    for (int z = firstZ + 1; z < lastZ; z++) {
                        Vector3Int pos = new Vector3Int(x, y, z);
                        if (!voxels.ContainsKey(pos)) {
                            var nearest = zInColumn.OrderBy(k => Mathf.Abs(k.z - z)).First();
                            voxels[pos] = voxels[nearest];
                        }
                    }
                }
            }
    }

    private void HollowOut() {
        var keys = voxels.Keys.ToList();
        HashSet<Vector3Int> toRemove = new HashSet<Vector3Int>();

        foreach (var k in keys) {
            if (voxels.ContainsKey(k + Vector3Int.up) &&
                voxels.ContainsKey(k + Vector3Int.down) &&
                voxels.ContainsKey(k + Vector3Int.left) &&
                voxels.ContainsKey(k + Vector3Int.right) &&
                voxels.ContainsKey(k + Vector3Int.forward) &&
                voxels.ContainsKey(k + Vector3Int.back)) {
                toRemove.Add(k);
            }
        }
        foreach (var k in toRemove)
            voxels.Remove(k);
    }

    private void Cleanup() {
        if (previewRoot)
            DestroyImmediate(previewRoot);
        if (voxelEditor)
            DestroyImmediate(voxelEditor);
        if (sourceEditor)
            DestroyImmediate(sourceEditor);
    }
}