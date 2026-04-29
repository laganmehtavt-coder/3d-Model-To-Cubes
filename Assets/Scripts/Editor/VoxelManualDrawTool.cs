using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;

public class VoxelManualDrawTool : EditorWindow {

    private int _resolution = 32;
    private Color[] _grid;
    private int[] _groups;
    
    private Color _currentColor = Color.white;
    private int _currentGroupId = 1;
    private bool _paintGroups = false;

    private float _worldSize = 10f;
    private float _cubeScale = 1.0f;
    private float _gapX = 0.0f;
    private float _gapY = 0.0f;
    private bool _use3DDepth = true;
    private int _depthLayers = 6;

    private Texture2D _gridPreview;
    private bool _isDirty = true;
    private Vector2 _scrollPos;

    [MenuItem("Tools/Voxel Manual Draw Tool")]
    public static void Open() => GetWindow<VoxelManualDrawTool>("Voxel Drawer");

    private void OnEnable() {
        InitializeGrid();
    }

    private void InitializeGrid() {
        _grid = new Color[_resolution * _resolution];
        _groups = new int[_resolution * _resolution];
        for (int i = 0; i < _grid.Length; i++) {
            _grid[i] = new Color(0, 0, 0, 0); // Transparent
            _groups[i] = 1;
        }
        _isDirty = true;
    }

    private void OnGUI() {
        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("Voxel Manual Editor", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope()) {
            // Left Panel: Tools & Settings
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(250))) {
                DrawSettings();
            }

            // Right Panel: Drawing Area
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
                DrawCanvas();
            }
        }

        EditorGUILayout.Space(10);
        if (GUILayout.Button("💾 SAVE AS SOS ASSET", GUILayout.Height(40))) {
            SaveSOS();
        }
    }

    private Color[] _palette = new Color[] { Color.white, Color.red, Color.green, Color.blue, Color.yellow, Color.cyan, Color.magenta, Color.black };
    private Texture2D _importTexture;
    private Color _colorToReplace = Color.white;

    private void DrawSettings() {
        EditorGUI.BeginChangeCheck();
        
        int newRes = EditorGUILayout.IntSlider("Resolution", _resolution, 8, 128);
        if (newRes != _resolution) {
            if (EditorUtility.DisplayDialog("Change Resolution?", "Changing resolution will clear your current drawing. Continue?", "Yes", "No")) {
                _resolution = newRes;
                InitializeGrid();
            }
        }

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Import Image", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope()) {
            _importTexture = (Texture2D)EditorGUILayout.ObjectField(_importTexture, typeof(Texture2D), false);
            if (GUILayout.Button("Load Image", GUILayout.Width(80))) {
                LoadFromImage();
            }
        }

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Paint & Update Colors", EditorStyles.boldLabel);
        
        using (new EditorGUILayout.HorizontalScope()) {
            _currentColor = EditorGUILayout.ColorField("Active Color", _currentColor);
            if (GUILayout.Button("Transparent", GUILayout.Width(90))) {
                _currentColor = new Color(0, 0, 0, 0);
            }
        }
        
        // Palette
        using (new EditorGUILayout.HorizontalScope()) {
            foreach (var c in _palette) {
                if (GUILayout.Button("", GUILayout.Width(20), GUILayout.Height(20))) {
                    _currentColor = c;
                }
                EditorGUI.DrawRect(GUILayoutUtility.GetLastRect(), c);
            }
        }

        using (new EditorGUILayout.HorizontalScope()) {
            _colorToReplace = EditorGUILayout.ColorField(GUIContent.none, _colorToReplace, false, true, false, GUILayout.Width(40));
            if (GUILayout.Button("Replace with Active Color")) {
                ReplaceColor(_colorToReplace, _currentColor);
            }
        }

        _currentGroupId = EditorGUILayout.IntSlider("Group ID", _currentGroupId, 1, 20);
        _paintGroups = EditorGUILayout.Toggle("Paint Group Only", _paintGroups);
        EditorGUILayout.HelpBox("Left Click: Paint\nRight Click: Erase\nAlt + Click: Pick Color", MessageType.None);

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Model Defaults", EditorStyles.boldLabel);
        _worldSize = EditorGUILayout.FloatField("World Size", _worldSize);
        _cubeScale = EditorGUILayout.Slider("Cube Scale", _cubeScale, 0.1f, 1.0f);
        _gapX = EditorGUILayout.Slider("Gap X", _gapX, 0.0f, 0.5f);
        _gapY = EditorGUILayout.Slider("Gap Y", _gapY, 0.0f, 0.5f);
        _use3DDepth = EditorGUILayout.Toggle("3D Depth", _use3DDepth);
        _depthLayers = EditorGUILayout.IntSlider("Depth Layers", _depthLayers, 1, 20);

        if (GUILayout.Button("Clear Canvas")) {
            if (EditorUtility.DisplayDialog("Clear?", "Clear everything?", "Yes", "No")) InitializeGrid();
        }

        if (EditorGUI.EndChangeCheck()) {
            _isDirty = true;
        }
    }

    private void DrawCanvas() {
        if (_gridPreview == null || _gridPreview.width != _resolution) {
            _gridPreview = new Texture2D(_resolution, _resolution);
            _gridPreview.filterMode = FilterMode.Point;
            _isDirty = true;
        }

        if (_isDirty) {
            _gridPreview.SetPixels(_grid);
            _gridPreview.Apply();
            _isDirty = false;
        }

        // Get available space in the current vertical scope
        // We subtract some padding for safety
        float availWidth = position.width - 270; 
        float availHeight = position.height - 80;
        float size = Mathf.Min(availWidth, availHeight);

        // Center the rect in the layout area
        Rect layoutRect = GUILayoutUtility.GetRect(availWidth, availHeight);
        Rect rect = new Rect(
            layoutRect.x + (availWidth - size) / 2f,
            layoutRect.y + (availHeight - size) / 2f,
            size,
            size
        );

        // Draw Checkerboard Background
        DrawCheckerboard(rect);
        
        // Draw the Texture
        GUI.DrawTexture(rect, _gridPreview, ScaleMode.StretchToFill);

        // Handle Input & Draw Cursor
        HandleMouseInputAndCursor(rect);
    }

    private void DrawCheckerboard(Rect rect) {
        if (Event.current.type != EventType.Repaint) return;
        
        float checkSize = rect.width / _resolution;
        for (int y = 0; y < _resolution; y++) {
            for (int x = 0; x < _resolution; x++) {
                if ((x + y) % 2 == 0) {
                    // GUI Y is top-down, so we invert Y for drawing
                    Rect r = new Rect(rect.x + x * checkSize, rect.y + (_resolution - 1 - y) * checkSize, checkSize, checkSize);
                    EditorGUI.DrawRect(r, new Color(0.15f, 0.15f, 0.15f, 1f));
                }
            }
        }
    }

    private void HandleMouseInputAndCursor(Rect rect) {
        Event e = Event.current;
        Vector2 mousePos = e.mousePosition;

        // Calculate relative coordinates (0 to 1)
        float relX = (mousePos.x - rect.x) / rect.width;
        float relY = 1.0f - (mousePos.y - rect.y) / rect.height; // Invert Y for texture space

        int x = Mathf.Clamp(Mathf.FloorToInt(relX * _resolution), 0, _resolution - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt(relY * _resolution), 0, _resolution - 1);

        // Draw Cursor Highlight
        if (rect.Contains(mousePos)) {
            float cellSize = rect.width / _resolution;
            Rect cursorRect = new Rect(rect.x + x * cellSize, rect.y + (_resolution - 1 - y) * cellSize, cellSize, cellSize);
            EditorGUI.DrawRect(cursorRect, new Color(1, 1, 1, 0.4f));
            Repaint(); // Force update to keep cursor smooth
        }

        if ((e.type == EventType.MouseDrag || e.type == EventType.MouseDown) && rect.Contains(mousePos)) {
            int idx = y * _resolution + x;
            
            if (e.alt) { // Alt + Click: Pick Color
                if (_grid[idx].a > 0.05f) {
                    _currentColor = _grid[idx];
                    _currentGroupId = _groups[idx];
                }
            } else if (e.button == 0) { // Left click: Paint
                if (_paintGroups) {
                    _groups[idx] = _currentGroupId;
                } else {
                    _grid[idx] = _currentColor;
                    _groups[idx] = _currentGroupId;
                }
            } else if (e.button == 1) { // Right click: Erase
                _grid[idx] = new Color(0, 0, 0, 0);
                _groups[idx] = 1;
            }

            _isDirty = true;
            e.Use();
        }
    }

    private void LoadFromImage() {
        if (_importTexture == null) return;
        
        RenderTexture rt = RenderTexture.GetTemporary(_resolution, _resolution);
        Graphics.Blit(_importTexture, rt);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D readable = new Texture2D(_resolution, _resolution);
        readable.ReadPixels(new Rect(0, 0, _resolution, _resolution), 0, 0);
        readable.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        _grid = readable.GetPixels();
        _groups = new int[_resolution * _resolution];
        for (int i = 0; i < _groups.Length; i++) _groups[i] = 1;
        
        DestroyImmediate(readable);
        _isDirty = true;
    }

    private void ReplaceColor(Color target, Color replacement) {
        for (int i = 0; i < _grid.Length; i++) {
            if (ColorsMatch(_grid[i], target)) {
                _grid[i] = replacement;
            }
        }
        _isDirty = true;
    }

    private bool ColorsMatch(Color a, Color b) {
        return Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && 
               Mathf.Abs(a.b - b.b) < 0.01f && Mathf.Abs(a.a - b.a) < 0.01f;
    }

    private void SaveSOS() {
        string path = EditorUtility.SaveFilePanelInProject("Save Voxel Model", "NewVoxelModel", "asset", "Save Voxel Data to SOS");
        if (string.IsNullOrEmpty(path)) return;

        string directory = Path.GetDirectoryName(path);
        string fileName = Path.GetFileNameWithoutExtension(path);
        string texturePath = Path.Combine(directory, fileName + "_Tex.png");

        // Create and save texture
        Texture2D tex = new Texture2D(_resolution, _resolution);
        tex.SetPixels(_grid);
        tex.Apply();
        byte[] bytes = tex.EncodeToPNG();
        File.WriteAllBytes(texturePath, bytes);
        AssetDatabase.ImportAsset(texturePath);
        
        Texture2D savedTex = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        // Ensure texture is readable and point filtered
        TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
        if (importer != null) {
            importer.isReadable = true;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        // Create VoxelModelData asset
        VoxelModelData asset = CreateInstance<VoxelModelData>();
        asset.sourceImage = savedTex;
        asset.resolution = _resolution;
        asset.worldSize = _worldSize;
        asset.cubeSize = _cubeScale;
        asset.gapX = _gapX;
        asset.gapY = _gapY;
        asset.use3DDepth = _use3DDepth;
        asset.depthLayers = _depthLayers;
        asset.useManualGroups = true;
        asset.savedGroups = (int[])_groups.Clone();

        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Success", "Model saved to " + path, "OK");
    }
}
