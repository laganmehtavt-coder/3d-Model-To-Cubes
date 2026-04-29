using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;

public class ImageToGridTool : EditorWindow {

    private Texture2D _sourceTexture;
    private GameObject _prefab;

    private float _worldSize = 10f;
    private int _resolution = 64;
    private float _cubeScale = 1.0f;
    private float _gapX = 0.0f;
    private float _gapY = 0.0f;
    private bool _volume = true;
    private int _depthLayers = 6;
    private bool _removeInternalCubes = true;
    
    private int _currentGroupId = 1;
    private int _anchorGroupId = 1;
    private bool _showGroupsInPreview = true;

    private Color[] _visualizationColors;
    private Vector2 _selectionStart;
    private Vector2 _selectionEnd;
    private bool _isDragging = false;

    private Texture2D _preview;
    private bool _previewDirty = true;
    private Vector2 _paletteScroll;

    private int[] _tempGroups;
    private string _parentName = "AutoVoxelModel";
    private List<ColorMapping> _colorMappings = new List<ColorMapping>();
    private int[] _groupDepths = new int[21]; // Overrides for groups 1-20
    private int _targetColorCount = 5;
    private Color _snapTargetColor = Color.black;
    private float _colorTolerance = 0.1f;
    private bool _consolidateIgnoreAlpha = true;

    [MenuItem("Tools/Image → VOXEL MASTER TOOL")]
    public static void Open() => GetWindow<ImageToGridTool>("Voxel Master");

    private void OnEnable() {
        InitVisualizationColors();
    }

    private void InitVisualizationColors() {
        if (_visualizationColors == null) {
            _visualizationColors = new Color[256];
            _visualizationColors[0] = new Color(0,0,0,0);
            for (int i = 1; i < 256; i++) {
                _visualizationColors[i] = Color.HSVToRGB((i * 0.137f) % 1.0f, 0.85f, 1.0f);
                _visualizationColors[i].a = 0.5f;
            }
        }
    }

    private void OnGUI() {
        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("Voxel Generation & SOS Factory", EditorStyles.boldLabel);
        
        using (new EditorGUILayout.HorizontalScope()) {
            // LEFT PANEL: SETTINGS
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(280))) {
                DrawSettings();
            }

            // RIGHT PANEL: PREVIEW
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
                DrawPreviewArea();
            }
        }

        EditorGUILayout.Space(10);
        using (new EditorGUILayout.HorizontalScope()) {
            if (GUILayout.Button("▶ GENERATE & SAVE SOS", GUILayout.Height(40))) {
                Generate(true);
            }
            if (GUILayout.Button("✕ CLEAR SCENE", GUILayout.Height(40), GUILayout.Width(100))) {
                ClearScene();
            }
        }
    }

    private void DrawSettings() {
        EditorGUI.BeginChangeCheck();
        _sourceTexture = (Texture2D)EditorGUILayout.ObjectField("Image", _sourceTexture, typeof(Texture2D), false);
        _prefab = (GameObject)EditorGUILayout.ObjectField("Cube Prefab", _prefab, typeof(GameObject), false);

        _resolution = EditorGUILayout.IntSlider("Resolution (Cubes)", _resolution, 16, 256);
        _worldSize = EditorGUILayout.FloatField("World Size", _worldSize);
        _cubeScale = EditorGUILayout.Slider("Cube Scale", _cubeScale, 0.1f, 1.0f);
        _gapX = EditorGUILayout.Slider("Gap X", _gapX, 0.0f, 0.5f);
        _gapY = EditorGUILayout.Slider("Gap Y", _gapY, 0.0f, 0.5f);

        float unitSize = _worldSize / _resolution;
        float actualSize = unitSize * _cubeScale;
        EditorGUILayout.HelpBox($"Calculated:\nReal Cube Size: {actualSize:F3}\nTotal Potential Cubes: {_resolution * _resolution}", MessageType.None);

        _volume = EditorGUILayout.Toggle("3D Depth", _volume);
        _depthLayers = EditorGUILayout.IntSlider("Depth Layers", _depthLayers, 1, 20);
        _removeInternalCubes = EditorGUILayout.Toggle("Remove Internal Cubes", _removeInternalCubes);

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Color Palette & Overrides", EditorStyles.boldLabel);
        if (GUILayout.Button("🔍 Refresh Colors from Image")) {
            RefreshColorMappings();
            _previewDirty = true;
        }

        // ─────────────────────────────────────────────────────────────────
        // Smart Consolidate UI
        EditorGUILayout.Space(5);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("🎯 SMART COLOR CONSOLIDATION", EditorStyles.boldLabel);
        _colorTolerance = EditorGUILayout.Slider("Merge Tolerance", _colorTolerance, 0f, 0.5f);
        _consolidateIgnoreAlpha = EditorGUILayout.ToggleLeft("Ignore Alpha (Fixes Black issues)", _consolidateIgnoreAlpha);

        if (GUILayout.Button("🎯 Smart Consolidate Colors", GUILayout.Height(25))) {
            SmartConsolidateColors();
            _previewDirty = true;
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(5);
        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox)) {
            _targetColorCount = EditorGUILayout.IntSlider("Target Colors", _targetColorCount, 1, 25);
            if (GUILayout.Button("🎨 Convert to " + _targetColorCount)) {
                QuantizePalette();
                _previewDirty = true;
            }
        }
        // ─────────────────────────────────────────────────────────────────

        DrawColorPalette();

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Grouping", EditorStyles.boldLabel);
        _currentGroupId = EditorGUILayout.IntSlider("Group ID", _currentGroupId, 1, 20);
        _anchorGroupId = EditorGUILayout.IntSlider("Anchor Group (Global)", _anchorGroupId, 1, 20);
        _showGroupsInPreview = EditorGUILayout.Toggle("Show Groups", _showGroupsInPreview);

        if (GUILayout.Button("✨ AUTO GROUP BY COLOR")) { AutoGroup(); _previewDirty = true; }
        if (GUILayout.Button("🚮 RESET GROUPS")) { _tempGroups = null; _previewDirty = true; }

        if (EditorGUI.EndChangeCheck()) {
            _previewDirty = true;
        }

        EditorGUILayout.Space(10);
        DrawGroupDepthSettings();
    }

    private void DrawGroupDepthSettings() {
        EditorGUILayout.LabelField("📏 GROUP DEPTH OVERRIDES", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Set depth for specific Group IDs. 0 = Use Default (Grayscale).", MessageType.None);
        
        for (int i = 1; i <= 10; i++) { // Show first 10 groups for space
            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUILayout.LabelField($"Group {i}", GUILayout.Width(60));
                _groupDepths[i] = EditorGUILayout.IntSlider(_groupDepths[i], 0, 20);
            }
        }
    }

    private int GetGroupDepth(int groupId, float grayscale) {
        if (groupId >= 0 && groupId < _groupDepths.Length && _groupDepths[groupId] > 0) {
            return _groupDepths[groupId];
        }
        return _volume ? Mathf.Max(1, Mathf.RoundToInt(grayscale * _depthLayers)) : 1;
    }

    private void DrawPreviewArea() {
        if (_sourceTexture == null) {
            EditorGUILayout.HelpBox("Assign an image to start.", MessageType.Info);
            return;
        }

        if (_tempGroups == null || _tempGroups.Length != _resolution * _resolution) {
            _tempGroups = new int[_resolution * _resolution];
            for (int i = 0; i < _tempGroups.Length; i++) _tempGroups[i] = 1;
        }

        if (_previewDirty) {
            UpdatePreviewTexture();
            _previewDirty = false;
        }

        float size = Mathf.Min(position.width - 320, position.height - 100);
        Rect rect = GUILayoutUtility.GetRect(size, size);
        rect.x += (position.width - 320 - size) / 2 + 10;

        if (_preview != null) GUI.DrawTexture(rect, _preview, ScaleMode.ScaleToFit);

        HandleDrag(rect);
    }

    private void UpdatePreviewTexture() {
        int res = _resolution;
        int viewRes = Mathf.Clamp(res * 4, 128, 512); 
        
        if (_preview == null || _preview.width != viewRes) {
            if (_preview != null) DestroyImmediate(_preview);
            _preview = new Texture2D(viewRes, viewRes);
            _preview.filterMode = FilterMode.Point;
        }

        RenderTexture rt = RenderTexture.GetTemporary(res, res);
        rt.filterMode = FilterMode.Point;
        Graphics.Blit(_sourceTexture, rt);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D sampledTex = new Texture2D(res, res);
        sampledTex.ReadPixels(new Rect(0, 0, res, res), 0, 0);
        sampledTex.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        Color[] sampledPixels = sampledTex.GetPixels();
        Color[] previewPixels = new Color[viewRes * viewRes];
        float ratio = (float)viewRes / res;

        for (int py = 0; py < viewRes; py++) {
            for (int px = 0; px < viewRes; px++) {
                int gx = Mathf.FloorToInt(px / ratio);
                int gy = Mathf.FloorToInt(py / ratio);
                int idx = gy * res + gx;

                float fx = (px % ratio) / ratio;
                float fy = (py % ratio) / ratio;

                // Simple scaling preview logic
                bool inCube = fx < _cubeScale && fy < _cubeScale;
                
                if (inCube && sampledPixels[idx].a > 0.1f) {
                    Color c = sampledPixels[idx];
                    
                    // Apply Color Override
                    Color32 currentC32 = (Color32)c;
                    var mapping = _colorMappings.Find(m => ColorsMatch((Color32)m.originalColor, currentC32));
                    if (mapping != null) c = mapping.overrideColor;

                    if (_showGroupsInPreview) {
                        int gid = _tempGroups[idx];
                        Color overlay = _visualizationColors[gid % 256];
                        c = Color.Lerp(c, overlay, overlay.a);
                    }
                    previewPixels[py * viewRes + px] = c;
                } else {
                    bool checker = ((px / 10) + (py / 10)) % 2 == 0;
                    previewPixels[py * viewRes + px] = checker ? new Color(0.15f, 0.15f, 0.15f, 1f) : new Color(0.1f, 0.1f, 0.1f, 1f);
                }
            }
        }
        
        _preview.SetPixels(previewPixels);
        _preview.Apply();
        DestroyImmediate(sampledTex);
    }

    private void HandleDrag(Rect rect) {
        Event e = Event.current;
        if (rect.Contains(e.mousePosition)) {
            if (e.type == EventType.MouseDown && e.button == 0) {
                _selectionStart = e.mousePosition;
                _isDragging = true;
                e.Use();
            }
        }

        if (_isDragging) {
            _selectionEnd = e.mousePosition;
            if (e.type == EventType.MouseUp) {
                ApplyBox(rect);
                _isDragging = false;
                _previewDirty = true;
                Repaint();
            }
            if (e.type == EventType.MouseDrag) Repaint();

            Rect vRect = new Rect(Mathf.Min(_selectionStart.x, _selectionEnd.x), Mathf.Min(_selectionStart.y, _selectionEnd.y), Mathf.Abs(_selectionStart.x - _selectionEnd.x), Mathf.Abs(_selectionStart.y - _selectionEnd.y));
            EditorGUI.DrawRect(vRect, new Color(0, 1, 1, 0.3f));
        }
    }

    private void ApplyBox(Rect rect) {
        float x1 = (Mathf.Min(_selectionStart.x, _selectionEnd.x) - rect.x) / rect.width;
        float x2 = (Mathf.Max(_selectionStart.x, _selectionEnd.x) - rect.x) / rect.width;
        float y1 = (Mathf.Min(_selectionStart.y, _selectionEnd.y) - rect.y) / rect.height;
        float y2 = (Mathf.Max(_selectionStart.y, _selectionEnd.y) - rect.y) / rect.height;

        int res = _resolution;
        int ix1 = Mathf.Clamp(Mathf.FloorToInt(x1 * res), 0, res - 1);
        int ix2 = Mathf.Clamp(Mathf.FloorToInt(x2 * res), 0, res - 1);
        int iy1 = Mathf.Clamp(Mathf.FloorToInt((1.0f - y2) * res), 0, res - 1);
        int iy2 = Mathf.Clamp(Mathf.FloorToInt((1.0f - y1) * res), 0, res - 1);

        for (int y = iy1; y <= iy2; y++)
            for (int x = ix1; x <= ix2; x++)
                _tempGroups[y * res + x] = _currentGroupId;
    }

    private void AutoGroup() {
        if (_sourceTexture == null) return;
        int res = _resolution;
        
        // 1. Get the final colors (post-override)
        RenderTexture rt = RenderTexture.GetTemporary(res, res);
        Graphics.Blit(_sourceTexture, rt);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(res, res);
        tex.ReadPixels(new Rect(0, 0, res, res), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        Color[] finalPixels = tex.GetPixels();
        for (int i = 0; i < finalPixels.Length; i++) {
            if (finalPixels[i].a < 0.1f) continue;
            Color32 currentC32 = (Color32)finalPixels[i];
            var mapping = _colorMappings.Find(m => ColorsMatch((Color32)m.originalColor, currentC32));
            if (mapping != null) finalPixels[i] = mapping.overrideColor;
        }

        // 2. Perform Grouping based on these final colors
        bool[] visited = new bool[res * res];
        _tempGroups = new int[res * res];
        int nextId = 1;
        
        for (int y = 0; y < res; y++) {
            for (int x = 0; x < res; x++) {
                int idx = y * res + x;
                if (finalPixels[idx].a < 0.1f || visited[idx]) continue;

                Queue<Vector2Int> q = new Queue<Vector2Int>();
                q.Enqueue(new Vector2Int(x, y));
                visited[idx] = true;
                Color target = finalPixels[idx];

                while (q.Count > 0) {
                    Vector2Int curr = q.Dequeue();
                    _tempGroups[curr.y * res + curr.x] = nextId;

                    Vector2Int[] neighbors = { 
                        new Vector2Int(curr.x+1, curr.y), new Vector2Int(curr.x-1, curr.y), 
                        new Vector2Int(curr.x, curr.y+1), new Vector2Int(curr.x, curr.y-1) 
                    };

                    foreach (var n in neighbors) {
                        if (n.x >= 0 && n.x < res && n.y >= 0 && n.y < res) {
                            int nIdx = n.y * res + n.x;
                            if (!visited[nIdx] && finalPixels[nIdx].a > 0.1f) {
                                // Check if final colors are exactly the same
                                if (finalPixels[nIdx] == target) {
                                    visited[nIdx] = true;
                                    q.Enqueue(n);
                                }
                            }
                        }
                    }
                }
                nextId++;
            }
        }
        DestroyImmediate(tex);
        Debug.Log($"[Voxel Master] Auto-grouped into {nextId - 1} connected color groups.");
    }

    private void Generate(bool saveSOS) {
        if (_sourceTexture == null || _prefab == null) return;
        ClearScene();

        GameObject parent = new GameObject(_parentName);
        float size = _worldSize / _resolution;

        RenderTexture rt = RenderTexture.GetTemporary(_resolution, _resolution);
        Graphics.Blit(_sourceTexture, rt);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(_resolution, _resolution);
        tex.ReadPixels(new Rect(0, 0, _resolution, _resolution), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        Color[] pixels = tex.GetPixels();
        int[] layersPerPixel = new int[_resolution * _resolution];

        // 1. Pre-calculate layer counts
        for (int i = 0; i < pixels.Length; i++) {
            if (pixels[i].a < 0.1f) {
                layersPerPixel[i] = 0;
            } else {
                int gid = _tempGroups[i];
                layersPerPixel[i] = GetGroupDepth(gid, pixels[i].grayscale);
            }
        }

        // 2. Generate
        for (int y = 0; y < _resolution; y++) {
            for (int x = 0; x < _resolution; x++) {
                int idx = y * _resolution + x;
                int layers = layersPerPixel[idx];
                if (layers == 0) continue;

                Color c = pixels[idx];
                // Apply Color Override
                Color32 currentC32 = (Color32)c;
                var mapping = _colorMappings.Find(m => ColorsMatch((Color32)m.originalColor, currentC32));
                if (mapping != null) c = mapping.overrideColor;

                float posX = (x - _resolution * 0.5f) * (size + _gapX);
                float posY = (y - _resolution * 0.5f) * (size + _gapY);
                Vector3 basePos = new Vector3(posX, posY, 0);

                int gid = _tempGroups[idx];

                for (int i = 0; i < layers; i++) {
                    if (_removeInternalCubes) {
                        bool hasUp = (y < _resolution - 1) && (layersPerPixel[(y + 1) * _resolution + x] > i);
                        bool hasDown = (y > 0) && (layersPerPixel[(y - 1) * _resolution + x] > i);
                        bool hasRight = (x < _resolution - 1) && (layersPerPixel[y * _resolution + (x + 1)] > i);
                        bool hasLeft = (x > 0) && (layersPerPixel[y * _resolution + (x - 1)] > i);
                        bool hasFront = (i < layers - 1);
                        bool hasBack = (i > 0);

                        if (hasUp && hasDown && hasRight && hasLeft && hasFront && hasBack) continue;
                    }

                    var go = (GameObject)PrefabUtility.InstantiatePrefab(_prefab, parent.transform);
                    go.transform.localPosition = basePos + new Vector3(0, 0, i * size);
                    go.transform.localScale = Vector3.one * size * _cubeScale;
                    
                    Block b = go.GetComponent<Block>();
                    if (b == null) b = go.AddComponent<Block>();
                    b.blockColor = c;
                    b.gridPos = new Vector3Int(x, y, i);
                    b.groupId = gid;

                    var r = go.GetComponentInChildren<Renderer>();
                    if (r != null) {
                        var mpb = new MaterialPropertyBlock();
                        mpb.SetColor("_BaseColor", c);
                        mpb.SetColor("_Color", c);
                        r.SetPropertyBlock(mpb);
                    }
                }
            }
        }

        if (saveSOS) CreateSOS();
        DestroyImmediate(tex);
    }

    private void CreateSOS() {
        string path = EditorUtility.SaveFilePanelInProject("Save SOS Asset", "VoxelData_" + _sourceTexture.name, "asset", "Save Voxel Data");
        if (string.IsNullOrEmpty(path)) return;

        VoxelModelData asset = CreateInstance<VoxelModelData>();
        asset.sourceImage = _sourceTexture;
        asset.resolution = _resolution;
        asset.worldSize = _worldSize;
        asset.cubeSize = _cubeScale;
        asset.gapX = _gapX;
        asset.gapY = _gapY;
        asset.use3DDepth = _volume;
        asset.depthLayers = _depthLayers;
        asset.useManualGroups = true;
        asset.savedGroups = (int[])_tempGroups.Clone();
        asset.colorMappings = new List<ColorMapping>(_colorMappings);

        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        Debug.Log("SOS Created Successfully at: " + path);
    }

    private void RefreshColorMappings() {
        if (_sourceTexture == null) return;
        
        int res = _resolution;
        RenderTexture rt = RenderTexture.GetTemporary(res, res);
        Graphics.Blit(_sourceTexture, rt);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(res, res);
        tex.ReadPixels(new Rect(0, 0, res, res), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        Color[] pixels = tex.GetPixels();
        HashSet<Color32> uniqueColors = new HashSet<Color32>();
        foreach (var c in pixels) {
            if (c.a > 0.05f) uniqueColors.Add((Color32)c);
        }
        DestroyImmediate(tex);

        List<ColorMapping> newList = new List<ColorMapping>();
        foreach (var c32 in uniqueColors) {
            Color c = (Color)c32;
            ColorMapping existing = _colorMappings.Find(m => ColorsMatch((Color32)m.originalColor, c32));
            if (existing != null) {
                newList.Add(existing);
            } else {
                newList.Add(new ColorMapping { originalColor = c, overrideColor = c });
            }
        }
        _colorMappings = newList;
    }

    private bool ColorsMatch(Color32 a, Color32 b) {
        return a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
    }

    private void DrawColorPalette() {
        if (_colorMappings == null || _colorMappings.Count == 0) {
            EditorGUILayout.HelpBox("No colors found. Click Refresh Colors.", MessageType.None);
            return;
        }

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField($"Total Colors: {_colorMappings.Count}", EditorStyles.miniBoldLabel);
        
        _paletteScroll = EditorGUILayout.BeginScrollView(_paletteScroll, GUILayout.Height(300));
        for (int i = 0; i < _colorMappings.Count; i++) {
            var mapping = _colorMappings[i];
            EditorGUILayout.BeginHorizontal();
            
            string hex = ColorUtility.ToHtmlStringRGBA(mapping.originalColor);
            EditorGUILayout.ColorField(GUIContent.none, mapping.originalColor, false, true, false, GUILayout.Width(40));
            EditorGUILayout.LabelField($"#{hex}", GUILayout.Width(80));

            EditorGUILayout.LabelField("→", GUILayout.Width(20));

            EditorGUI.BeginChangeCheck();
            mapping.overrideColor = EditorGUILayout.ColorField(GUIContent.none, mapping.overrideColor, false, true, false, GUILayout.Width(40));
            if (EditorGUI.EndChangeCheck()) {
                _previewDirty = true;
            }
            
            string overHex = ColorUtility.ToHtmlStringRGBA(mapping.overrideColor);
            EditorGUILayout.LabelField($"#{overHex}", GUILayout.Width(80));

            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private void ClearScene() {
        GameObject obj = GameObject.Find(_parentName);
        if (obj) DestroyImmediate(obj);
    }

    private void SmartConsolidateColors() {
        if (_sourceTexture == null || _colorMappings == null || _colorMappings.Count == 0) return;

        int res = _resolution;
        RenderTexture rt = RenderTexture.GetTemporary(res, res);
        Graphics.Blit(_sourceTexture, rt);
        RenderTexture prevRT = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(res, res);
        tex.ReadPixels(new Rect(0, 0, res, res), 0, 0);
        tex.Apply();
        RenderTexture.active = prevRT;
        RenderTexture.ReleaseTemporary(rt);

        Color[] pixels = tex.GetPixels();
        Dictionary<Color32, int> pixelCounts = new Dictionary<Color32, int>();
        foreach (Color c in pixels) {
            if (c.a < 0.05f) continue;
            Color32 c32 = (Color32)c;
            if (pixelCounts.ContainsKey(c32)) pixelCounts[c32]++;
            else pixelCounts[c32] = 1;
        }
        DestroyImmediate(tex);

        List<ColorMapping> sortedMappings = new List<ColorMapping>(_colorMappings);
        sortedMappings.Sort((a, b) => {
            int countA = pixelCounts.ContainsKey((Color32)a.originalColor) ? pixelCounts[(Color32)a.originalColor] : 0;
            int countB = pixelCounts.ContainsKey((Color32)b.originalColor) ? pixelCounts[(Color32)b.originalColor] : 0;
            return countB.CompareTo(countA);
        });

        List<List<ColorMapping>> groups = new List<List<ColorMapping>>();
        HashSet<ColorMapping> assigned = new HashSet<ColorMapping>();

        foreach (var mapping in sortedMappings) {
            if (assigned.Contains(mapping)) continue;
            List<ColorMapping> newGroup = new List<ColorMapping> { mapping };
            assigned.Add(mapping);
            Color anchorColor = mapping.originalColor;

            foreach (var other in sortedMappings) {
                if (assigned.Contains(other)) continue;
                float dist = _consolidateIgnoreAlpha ? 
                    Vector3.Distance(new Vector3(anchorColor.r, anchorColor.g, anchorColor.b), new Vector3(other.originalColor.r, other.originalColor.g, other.originalColor.b)) :
                    Vector4.Distance((Vector4)anchorColor, (Vector4)other.originalColor);

                if (dist < _colorTolerance) {
                    newGroup.Add(other);
                    assigned.Add(other);
                }
            }
            groups.Add(newGroup);
        }

        foreach (var group in groups) {
            Color dominantColor = group[0].originalColor;
            if (_consolidateIgnoreAlpha) {
                Color32 c32 = (Color32)dominantColor;
                c32.a = 255;
                dominantColor = (Color)c32;
            }
            foreach (var mapping in group) mapping.overrideColor = dominantColor;
        }
        Debug.Log($"[Voxel Master] Consolidated into {groups.Count} color groups.");
    }

    private void QuantizePalette() {
        if (_sourceTexture == null || _colorMappings.Count == 0) return;

        int res = _resolution;
        RenderTexture rt = RenderTexture.GetTemporary(res, res);
        Graphics.Blit(_sourceTexture, rt);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(res, res);
        tex.ReadPixels(new Rect(0, 0, res, res), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        Color[] pixels = tex.GetPixels();
        Dictionary<Color32, int> colorCounts = new Dictionary<Color32, int>();
        foreach (var c in pixels) {
            if (c.a < 0.1f) continue;
            Color32 c32 = (Color32)c;
            if (colorCounts.ContainsKey(c32)) colorCounts[c32]++;
            else colorCounts[c32] = 1;
        }
        DestroyImmediate(tex);

        List<KeyValuePair<Color32, int>> sortedColors = new List<KeyValuePair<Color32, int>>(colorCounts);
        sortedColors.Sort((a, b) => b.Value.CompareTo(a.Value));

        int count = Mathf.Min(_targetColorCount, sortedColors.Count);
        List<Color> centroids = new List<Color>();
        for (int i = 0; i < count; i++) centroids.Add((Color)sortedColors[i].Key);

        foreach (var mapping in _colorMappings) {
            Color bestCentroid = centroids[0];
            float minDist = float.MaxValue;
            foreach (var centroid in centroids) {
                float dist = Vector4.Distance((Vector4)mapping.originalColor, (Vector4)centroid);
                if (dist < minDist) {
                    minDist = dist;
                    bestCentroid = centroid;
                }
            }
            mapping.overrideColor = bestCentroid;
        }
    }
}
