using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class BottleMaker3D : EditorWindow {
    [System.Serializable]
    public class HeightSection {
        [Range(0, 1)] public float startHeight = 0.8f;
        [Range(0, 1)] public float endHeight = 1.0f;
        public Color sectionColor = Color.white;
        public bool active = true;
        public string label = "Section";
    }

    private Texture2D sourceImage;
    private GameObject cubePrefab;
    private int resolution = 80;
    private float worldSize = 10f;
    private float gapX = 0f, gapY = 0f, gapZ = 0f;
    private Color majorColor = Color.blue;
    private bool useImageColors = true;
    private List<HeightSection> heightSections = new List<HeightSection>();
    
    private Vector2 selectionStart, selectionEnd;
    private bool isSelecting = false;
    private Rect imageRect;
    private Vector2 previewDrag = new Vector2(120, -20);
    private List<VoxelData> previewVoxels = new List<VoxelData>();
    private PreviewRenderUtility previewRenderUtility;
    private Mesh previewMesh;
    private Material previewMaterial;

    private struct VoxelData { public Vector3 position; public Color color; public Vector3 scale; }

    [MenuItem("Tools/3D BOTTOLE MAKER tOOL")]
    public static void Open() => GetWindow<BottleMaker3D>("3D Bottle Maker Pro");

    private void OnEnable() {
        if (previewRenderUtility == null) {
            previewRenderUtility = new PreviewRenderUtility();
            previewRenderUtility.camera.fieldOfView = 30f;
            previewRenderUtility.camera.farClipPlane = 1000f;
            previewRenderUtility.camera.nearClipPlane = 0.1f;
        }
        UpdatePreviewData();
    }

    private void OnDisable() { if (previewRenderUtility != null) previewRenderUtility.Cleanup(); }

    private void OnGUI() {
        EditorGUILayout.BeginHorizontal();
        
        // LEFT: Settings Panel
        EditorGUILayout.BeginVertical(GUILayout.Width(380));
        DrawSettings();
        EditorGUILayout.EndVertical();

        // RIGHT: 3D Preview Panel
        EditorGUILayout.BeginVertical();
        GUILayout.Label("3D INTERACTIVE PREVIEW (Drag to Rotate)", EditorStyles.centeredGreyMiniLabel);
        Rect previewRect = GUILayoutUtility.GetRect(200, 200, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        HandlePreviewInput(previewRect);
        RenderPreview(previewRect);
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
    }

    private void DrawSettings() {
        EditorGUI.BeginChangeCheck();
        GUILayout.Label("BOTTLE FACTORY CONFIG", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);

        sourceImage = (Texture2D)EditorGUILayout.ObjectField("Bottle Image", sourceImage, typeof(Texture2D), false);
        cubePrefab = (GameObject)EditorGUILayout.ObjectField("Cube Prefab", cubePrefab, typeof(GameObject), false);

        if (sourceImage != null) {
            EditorGUILayout.LabelField("Drag on image below to select height range:", EditorStyles.miniLabel);
            imageRect = GUILayoutUtility.GetRect(256, 256, GUILayout.ExpandWidth(false));
            GUI.DrawTexture(imageRect, sourceImage, ScaleMode.ScaleToFit);
            HandleImageInteraction();
            
            if (GUILayout.Button("Create Section From Selection", GUILayout.Width(256))) {
                float h1 = 1f - (selectionStart.y - imageRect.y) / imageRect.height;
                float h2 = 1f - (selectionEnd.y - imageRect.y) / imageRect.height;
                heightSections.Add(new HeightSection { 
                    startHeight = Mathf.Clamp01(Mathf.Min(h1, h2)), 
                    endHeight = Mathf.Clamp01(Mathf.Max(h1, h2)), 
                    sectionColor = majorColor,
                    label = "Section " + (heightSections.Count + 1)
                });
            }
        }

        EditorGUILayout.Space(10);
        resolution = EditorGUILayout.IntSlider("Resolution", resolution, 16, 128);
        worldSize = EditorGUILayout.FloatField("Bottle Height", worldSize);
        
        EditorGUILayout.Space(5);
        GUILayout.Label("SPACING (GAP)", EditorStyles.miniBoldLabel);
        EditorGUILayout.BeginHorizontal();
        gapX = EditorGUILayout.FloatField("X", gapX);
        gapY = EditorGUILayout.FloatField("Y", gapY);
        gapZ = EditorGUILayout.FloatField("Z", gapZ);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(5);
        GUILayout.Label("COLORS", EditorStyles.miniBoldLabel);
        majorColor = EditorGUILayout.ColorField("Base Major Color", majorColor);
        useImageColors = EditorGUILayout.Toggle("Use Image Colors", useImageColors);

        EditorGUILayout.Space(5);
        GUILayout.Label("SECTIONS (" + heightSections.Count + ")", EditorStyles.miniBoldLabel);
        for (int i = 0; i < heightSections.Count; i++) {
            EditorGUILayout.BeginHorizontal();
            heightSections[i].label = EditorGUILayout.TextField(heightSections[i].label, GUILayout.Width(80));
            float s = heightSections[i].startHeight, e = heightSections[i].endHeight;
            EditorGUILayout.MinMaxSlider(ref s, ref e, 0, 1);
            heightSections[i].startHeight = s; heightSections[i].endHeight = e;
            heightSections[i].sectionColor = EditorGUILayout.ColorField(heightSections[i].sectionColor, GUILayout.Width(40));
            if (GUILayout.Button("X", GUILayout.Width(20))) { heightSections.RemoveAt(i); break; }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space(10);
        GUI.backgroundColor = Color.green;
        if (GUILayout.Button("GENERATE 3D BOTTLE MODEL", GUILayout.Height(50))) Generate();
        GUI.backgroundColor = Color.white;

        if (EditorGUI.EndChangeCheck()) UpdatePreviewData();
    }

    private void HandleImageInteraction() {
        Event e = Event.current;
        if (imageRect.Contains(e.mousePosition)) {
            if (e.type == EventType.MouseDown && e.button == 0) { selectionStart = e.mousePosition; isSelecting = true; e.Use(); }
            if (e.type == EventType.MouseDrag && isSelecting) { selectionEnd = e.mousePosition; Repaint(); e.Use(); }
            if (e.type == EventType.MouseUp) isSelecting = false;
        }
        if (isSelecting || selectionStart != Vector2.zero) {
            Rect r = new Rect(selectionStart.x, selectionStart.y, selectionEnd.x - selectionStart.x, selectionEnd.y - selectionStart.y);
            Handles.DrawSolidRectangleWithOutline(r, new Color(1,1,0,0.2f), Color.yellow);
        }
    }

    private void RenderPreview(Rect rect) {
        if (previewRenderUtility == null) return;
        previewRenderUtility.BeginPreview(rect, GUIStyle.none);
        
        float dist = worldSize * 2.5f;
        previewRenderUtility.camera.transform.position = -Vector3.forward * dist;
        previewRenderUtility.camera.transform.rotation = Quaternion.identity;
        previewRenderUtility.camera.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1);
        previewRenderUtility.camera.clearFlags = CameraClearFlags.SolidColor;
        
        previewRenderUtility.lights[0].enabled = true;
        previewRenderUtility.lights[0].transform.rotation = Quaternion.Euler(30, 30, 0);
        previewRenderUtility.lights[0].intensity = 1.0f;

        Matrix4x4 pivot = Matrix4x4.Rotate(Quaternion.Euler(previewDrag.y, previewDrag.x, 0));
        
        if (previewMesh == null) {
            GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            previewMesh = temp.GetComponent<MeshFilter>().sharedMesh;
            DestroyImmediate(temp);
        }
        if (previewMaterial == null) previewMaterial = new Material(Shader.Find("Standard"));

        foreach (var v in previewVoxels) {
            previewMaterial.color = v.color;
            previewRenderUtility.DrawMesh(previewMesh, pivot * Matrix4x4.TRS(v.position, Quaternion.identity, v.scale), previewMaterial, 0);
        }
        
        previewRenderUtility.camera.Render();
        Texture result = previewRenderUtility.EndPreview();
        GUI.DrawTexture(rect, result);
    }

    private void HandlePreviewInput(Rect rect) {
        Event e = Event.current;
        if (rect.Contains(e.mousePosition) && e.type == EventType.MouseDrag) { previewDrag += e.delta; Repaint(); }
    }

    private void UpdatePreviewData() {
        if (!sourceImage) return;
        previewVoxels.Clear();
        Texture2D tex = GetReadableTex(resolution);
        Color[] pixels = tex.GetPixels();
        float vs = worldSize / resolution;

        for (int y = 0; y < resolution; y++) {
            float hPct = (float)y / resolution;
            int xS = -1, xE = -1;
            for (int x = 0; x < resolution; x++) if (pixels[y * resolution + x].a > 0.1f) { if (xS == -1) xS = x; xE = x; }
            if (xS == -1) continue;
            
            float r = (xE - xS) * 0.5f, cX = (xS + xE) * 0.5f;
            Color rowCol = pixels[y * resolution + (int)cX];

            for (int ix = xS; ix <= xE; ix++) {
                for (int iz = -Mathf.CeilToInt(r); iz <= Mathf.CeilToInt(r); iz++) {
                    if ((ix - cX) * (ix - cX) + (iz * iz) <= (r * r)) {
                        bool internalC = (ix - cX) * (ix - cX) + (iz * iz) < (r - 1) * (r - 1) && ix > xS && ix < xE && y > 0 && y < resolution - 1;
                        if (!internalC) {
                            Color finalCol = useImageColors ? pixels[y * resolution + ix] : majorColor;
                            // Check sections
                            foreach(var sec in heightSections) {
                                if(sec.active && hPct >= sec.startHeight && hPct <= sec.endHeight) {
                                    finalCol = sec.sectionColor;
                                    break;
                                }
                            }

                            previewVoxels.Add(new VoxelData { 
                                position = new Vector3((ix - resolution / 2f) * (vs + gapX), (y - resolution / 2f) * (vs + gapY), iz * (vs + gapZ)), 
                                color = finalCol, 
                                scale = Vector3.one * vs 
                            });
                        }
                    }
                }
            }
        }
        DestroyImmediate(tex);
    }

    private void Generate() {
        if (!sourceImage || !cubePrefab) return;
        GameObject root = new GameObject("3D_BOTTLE_" + sourceImage.name);
        root.AddComponent<ModelRotator>();
        UpdatePreviewData(); 
        
        foreach (var v in previewVoxels) {
            GameObject g = (GameObject)PrefabUtility.InstantiatePrefab(cubePrefab, root.transform);
            g.transform.localPosition = v.position;
            g.transform.localScale = v.scale * 0.99f;
            var r = g.GetComponentInChildren<Renderer>();
            if (r) {
                MaterialPropertyBlock mpb = new MaterialPropertyBlock();
                mpb.SetColor("_BaseColor", v.color);
                mpb.SetColor("_Color", v.color);
                r.SetPropertyBlock(mpb);
            }
        }
        Selection.activeGameObject = root;
        Debug.Log("Bottle Generated Successfully!");
    }

    private Texture2D GetReadableTex(int res) {
        RenderTexture rt = RenderTexture.GetTemporary(res, res);
        Graphics.Blit(sourceImage, rt);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D t = new Texture2D(res, res);
        t.ReadPixels(new Rect(0, 0, res, res), 0, 0);
        t.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        return t;
    }
}
