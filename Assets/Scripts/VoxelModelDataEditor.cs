using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

[CustomEditor(typeof(VoxelModelData))]
public class VoxelModelDataEditor : Editor {
    private Vector2 _selectionStart;
    private Vector2 _selectionEnd;
    private bool _isDragging = false;
    private Texture2D _preview;
    private Color[] _visualizationColors;
    private Vector2 _paletteScroll;

    public override void OnInspectorGUI() {
        serializedObject.Update();

        // 1. Draw Default Inspector
        DrawDefaultInspector();

        VoxelModelData data = (VoxelModelData)target;

        if (data.sourceImage == null) {
            EditorGUILayout.Space(10);
            EditorGUILayout.HelpBox("Select a Source Image to enable Group Editing.", MessageType.Info);
            return;
        }

        // 2. SAFETY: Auto-resize array if resolution changed
        int res = data.resolution;
        if (data.savedGroups == null || data.savedGroups.Length != res * res) {
            Debug.Log("VoxelData: Resizing savedGroups array to " + (res * res));
            int[] newGroups = new int[res * res];
            for (int i = 0; i < newGroups.Length; i++) {
                if (data.savedGroups != null && i < data.savedGroups.Length)
                    newGroups[i] = data.savedGroups[i];
                else
                    newGroups[i] = 1;
            }
            data.savedGroups = newGroups;
            EditorUtility.SetDirty(data);
        }

        EditorGUILayout.Space(20);
        EditorGUILayout.LabelField("COLOR PALETTE & OVERRIDES", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope()) {
            if (GUILayout.Button("🔍 Refresh Colors"))
                RefreshColorMappings(data);
            if (GUILayout.Button("✨ Simplify Palette"))
                SimplifyPalette(data);
        }
        data.colorTolerance = EditorGUILayout.Slider("Merge Tolerance", data.colorTolerance, 0f, 0.5f);

        // ─────────────────────────────────────────────────────────────────
        // TASK 1 — Smart Consolidate block
        EditorGUILayout.Space(8);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("🎯 SMART COLOR CONSOLIDATION", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Groups similar shades, then overrides all with the most-used shade per group.\n" +
            "► Ignore Alpha ON  → #000000FF + #000000AB + #000000FB = ONE black  ✅ (use this for your image)\n" +
            "► Ignore Alpha OFF → different alpha = different color",
            MessageType.Info);

        _consolidateIgnoreAlpha = EditorGUILayout.ToggleLeft(
            "Ignore Alpha Channel When Grouping  (fixes 28-shade black problem)",
            _consolidateIgnoreAlpha);

        using (new EditorGUILayout.HorizontalScope()) {
            if (GUILayout.Button("🎯 Smart Consolidate Colors", GUILayout.Height(30))) {
                SmartConsolidateColors(data);
                EditorUtility.SetDirty(data);
            }
        }
        EditorGUILayout.EndVertical();
        // ─────────────────────────────────────────────────────────────────

        EditorGUILayout.Space(5);
        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox)) {
            data.targetColorCount = EditorGUILayout.IntSlider("Target Colors", data.targetColorCount, 1, 25);
            if (GUILayout.Button("🎨 Convert to " + data.targetColorCount))
                QuantizePalette(data);
        }

        DrawColorPalette(data);

        EditorGUILayout.Space(20);
        EditorGUILayout.LabelField("GROUP EDITOR (Drag to Select)", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Current Group ID: " + data.currentEditingGroupId);

        // 3. Update Preview Texture
        InitVisualizationColors();
        UpdatePreview(data);

        // 4. Draw Interactive Area
        DrawInteractiveArea(data);

        // 5. Buttons
        EditorGUILayout.Space(10);
        using (new EditorGUILayout.HorizontalScope()) {
            if (GUILayout.Button("✨ Auto Color Group")) {
                AutoGenerateGroups(data);
                EditorUtility.SetDirty(data);
            }
            if (GUILayout.Button("🚮 Reset Groups")) {
                if (EditorUtility.DisplayDialog("Reset", "Reset all groups to 1?", "Yes", "No")) {
                    for (int i = 0; i < data.savedGroups.Length; i++)
                        data.savedGroups[i] = 1;
                    EditorUtility.SetDirty(data);
                }
            }
        }

        if (GUI.changed) {
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(data);
        }
    }

    // =========================================================================
    // TASK 1 — Smart Consolidate Colors
    // =========================================================================
    // Toggle: ignore alpha when grouping (catches your case: #000000FF vs #000000AB etc.)
    private bool _consolidateIgnoreAlpha = true;

    /// <summary>
    /// 1. Reads every pixel and counts how many pixels each Color32 covers.
    /// 2. Groups color mappings by RGB similarity (alpha optionally ignored).
    ///    This is critical for images like yours where shades differ ONLY in
    ///    alpha (#000000FF, #000000FB, #000000AB → all same black family).
    /// 3. Picks the Color32 with the HIGHEST pixel count as dominant per group.
    /// 4. Overrides every mapping in that group → result: one flat color per family.
    /// </summary>
    private void SmartConsolidateColors(VoxelModelData data) {
        if (data.sourceImage == null || data.colorMappings == null || data.colorMappings.Count == 0) {
            Debug.LogWarning("[VoxelEditor] SmartConsolidate: No source image or color mappings. Run Refresh Colors first.");
            return;
        }

        // ── Step 1: Count pixels per Color32 ─────────────────────────────
        int res = data.resolution;
        RenderTexture rt = RenderTexture.GetTemporary(res, res);
        Graphics.Blit(data.sourceImage, rt);
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

        // ── Step 2: Sort all mappings by pixel count (descending) ────────
        // This ensures dominant colors are processed first and act as "anchors"
        List<ColorMapping> sortedMappings = new List<ColorMapping>(data.colorMappings);
        sortedMappings.Sort((a, b) => {
            int countA = pixelCounts.ContainsKey((Color32)a.originalColor) ? pixelCounts[(Color32)a.originalColor] : 0;
            int countB = pixelCounts.ContainsKey((Color32)b.originalColor) ? pixelCounts[(Color32)b.originalColor] : 0;
            return countB.CompareTo(countA); // Descending
        });

        // ── Step 3: Group by similarity using dominant anchors ───────────
        float tol = data.colorTolerance;
        List<List<ColorMapping>> groups = new List<List<ColorMapping>>();
        HashSet<ColorMapping> assigned = new HashSet<ColorMapping>();

        foreach (var mapping in sortedMappings) {
            if (assigned.Contains(mapping)) continue;

            // This mapping is the most dominant unassigned color -> it's our new anchor
            List<ColorMapping> newGroup = new List<ColorMapping>();
            newGroup.Add(mapping);
            assigned.Add(mapping);

            Color anchorColor = mapping.originalColor;

            // Find all other unassigned mappings that are close to this anchor
            foreach (var other in sortedMappings) {
                if (assigned.Contains(other)) continue;

                float dist;
                if (_consolidateIgnoreAlpha) {
                    dist = Vector3.Distance(
                        new Vector3(anchorColor.r, anchorColor.g, anchorColor.b),
                        new Vector3(other.originalColor.r, other.originalColor.g, other.originalColor.b)
                    );
                } else {
                    dist = Vector4.Distance((Vector4)anchorColor, (Vector4)other.originalColor);
                }

                if (dist < tol) {
                    newGroup.Add(other);
                    assigned.Add(other);
                }
            }
            groups.Add(newGroup);
        }

        // ── Step 4: Apply override (anchor is always dominantColor) ──────
        int mergedCount = 0;
        foreach (var group in groups) {
            Color dominantColor = group[0].originalColor;
            if (_consolidateIgnoreAlpha) {
                Color32 c32 = (Color32)dominantColor;
                c32.a = 255;
                dominantColor = (Color)c32;
            }

            foreach (var mapping in group) {
                mapping.overrideColor = dominantColor;
            }
            if (group.Count > 1) mergedCount++;
        }

        Debug.Log($"[VoxelEditor] SmartConsolidate: Processed {data.colorMappings.Count} colors into {groups.Count} groups. " +
                  $"{mergedCount} group(s) had multiple shades merged into dominant anchors.");
    }
    // =========================================================================

    private void InitVisualizationColors() {
        if (_visualizationColors == null) {
            _visualizationColors = new Color[256];
            _visualizationColors[0] = new Color(0, 0, 0, 0);
            for (int i = 1; i < 256; i++) {
                _visualizationColors[i] = Color.HSVToRGB((i * 0.137f) % 1.0f, 0.85f, 1.0f);
                _visualizationColors[i].a = 0.5f;
            }
        }
    }

    private void UpdatePreview(VoxelModelData data) {
        int res = data.resolution;
        if (_preview == null || _preview.width != res || _preview.height != res) {
            if (_preview != null)
                DestroyImmediate(_preview);
            _preview = new Texture2D(res, res);
            _preview.filterMode = FilterMode.Point;
            _preview.wrapMode = TextureWrapMode.Clamp;
        }

        RenderTexture rt = RenderTexture.GetTemporary(res, res);
        Graphics.Blit(data.sourceImage, rt);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;

        Texture2D temp = new Texture2D(res, res);
        temp.ReadPixels(new Rect(0, 0, res, res), 0, 0);
        temp.Apply();

        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        Color[] pix = temp.GetPixels();
        for (int i = 0; i < pix.Length; i++) {
            if (pix[i].a < 0.05f)
                continue;

            Color32 currentC32 = (Color32)pix[i];
            ColorMapping mapping = data.colorMappings.Find(m => ColorsMatch((Color32)m.originalColor, currentC32));
            if (mapping != null) {
                pix[i] = mapping.overrideColor;
            }

            int gid = data.savedGroups[i];
            if (gid > 1) {
                Color overlay = _visualizationColors[gid % 256];
                pix[i] = Color.Lerp(pix[i], overlay, overlay.a);
            }
        }

        _preview.SetPixels(pix);
        _preview.Apply();
        DestroyImmediate(temp);
    }

    private void QuantizePalette(VoxelModelData data) {
        if (data.sourceImage == null)
            return;

        int res = data.resolution;
        RenderTexture rt = RenderTexture.GetTemporary(res, res);
        Graphics.Blit(data.sourceImage, rt);
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
            if (c.a < 0.1f)
                continue;
            Color32 c32 = (Color32)c;
            if (colorCounts.ContainsKey(c32))
                colorCounts[c32]++;
            else
                colorCounts[c32] = 1;
        }
        DestroyImmediate(tex);

        List<KeyValuePair<Color32, int>> sortedColors = new List<KeyValuePair<Color32, int>>(colorCounts);
        sortedColors.Sort((a, b) => b.Value.CompareTo(a.Value));

        int count = Mathf.Min(data.targetColorCount, sortedColors.Count);
        List<Color> centroids = new List<Color>();
        for (int i = 0; i < count; i++)
            centroids.Add((Color)sortedColors[i].Key);

        foreach (var mapping in data.colorMappings) {
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
        EditorUtility.SetDirty(data);
    }

    private void SnapToTarget(VoxelModelData data) {
        if (data.colorMappings == null || data.colorMappings.Count == 0)
            return;

        float threshold = data.colorTolerance;
        Color target = data.snapTargetColor;

        foreach (var mapping in data.colorMappings) {
            float dist = Vector4.Distance((Vector4)mapping.originalColor, (Vector4)target);
            if (dist < threshold) {
                mapping.overrideColor = target;
            }
        }
        EditorUtility.SetDirty(data);
    }

    private void SimplifyPalette(VoxelModelData data) {
        if (data.colorMappings == null || data.colorMappings.Count == 0)
            return;

        List<ColorMapping> processed = new List<ColorMapping>();
        float threshold = data.colorTolerance;

        foreach (var mapping in data.colorMappings) {
            ColorMapping match = processed.Find(p => {
                float dist = Vector4.Distance((Vector4)p.originalColor, (Vector4)mapping.originalColor);
                return dist < threshold;
            });

            if (match != null) {
                mapping.overrideColor = match.overrideColor;
            } else {
                processed.Add(mapping);
            }
        }
        EditorUtility.SetDirty(data);
    }

    private void RefreshColorMappings(VoxelModelData data) {
        if (data.sourceImage == null)
            return;

        int res = data.resolution;
        RenderTexture rt = RenderTexture.GetTemporary(res, res);
        Graphics.Blit(data.sourceImage, rt);
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
            if (c.a > 0.05f)
                uniqueColors.Add((Color32)c);
        }
        DestroyImmediate(tex);

        List<ColorMapping> newList = new List<ColorMapping>();
        foreach (var c32 in uniqueColors) {
            Color c = (Color)c32;
            ColorMapping existing = data.colorMappings.Find(m => ColorsMatch((Color32)m.originalColor, c32));
            if (existing != null) {
                newList.Add(existing);
            } else {
                newList.Add(new ColorMapping { originalColor = c, overrideColor = c });
            }
        }
        data.colorMappings = newList;
        EditorUtility.SetDirty(data);
    }

    private bool ColorsMatch(Color32 a, Color32 b) {
        return a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
    }

    private void DrawColorPalette(VoxelModelData data) {
        if (data.colorMappings == null || data.colorMappings.Count == 0) {
            EditorGUILayout.HelpBox("No colors found. Click Refresh Colors.", MessageType.None);
            return;
        }

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField($"Total Colors: {data.colorMappings.Count}", EditorStyles.miniBoldLabel);

        _paletteScroll = EditorGUILayout.BeginScrollView(_paletteScroll, GUILayout.Height(300));
        for (int i = 0; i < data.colorMappings.Count; i++) {
            var mapping = data.colorMappings[i];
            EditorGUILayout.BeginHorizontal();

            string hex = ColorUtility.ToHtmlStringRGBA(mapping.originalColor);
            EditorGUILayout.ColorField(GUIContent.none, mapping.originalColor, false, true, false, GUILayout.Width(40));
            EditorGUILayout.LabelField($"#{hex}", GUILayout.Width(80));

            EditorGUILayout.LabelField("→", GUILayout.Width(20));

            EditorGUI.BeginChangeCheck();
            mapping.overrideColor = EditorGUILayout.ColorField(GUIContent.none, mapping.overrideColor, false, true, false, GUILayout.Width(40));
            if (EditorGUI.EndChangeCheck()) {
                EditorUtility.SetDirty(data);
            }

            string overHex = ColorUtility.ToHtmlStringRGBA(mapping.overrideColor);
            EditorGUILayout.LabelField($"#{overHex}", GUILayout.Width(80));

            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private void DrawInteractiveArea(VoxelModelData data) {
        float size = Mathf.Min(EditorGUIUtility.currentViewWidth - 60, 400);
        Rect rect = GUILayoutUtility.GetRect(size, size);
        rect.x = (EditorGUIUtility.currentViewWidth - size) / 2;

        EditorGUI.DrawRect(rect, new Color(0.1f, 0.1f, 0.1f, 1f));

        if (_preview != null) {
            GUI.DrawTexture(rect, _preview, ScaleMode.ScaleToFit);
        }

        Event e = Event.current;
        Vector2 mousePos = e.mousePosition;

        if (rect.Contains(mousePos)) {
            if (e.type == EventType.MouseDown && e.button == 0) {
                _selectionStart = mousePos;
                _selectionEnd = mousePos;
                _isDragging = true;
                e.Use();
            }
        }

        if (_isDragging) {
            if (e.type == EventType.MouseDrag) {
                _selectionEnd = mousePos;
                Repaint();
                e.Use();
            }
            if (e.type == EventType.MouseUp) {
                ApplySelection(rect, data);
                _isDragging = false;
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssets();
                Repaint();
                e.Use();
            }

            Rect visualRect = new Rect(
                Mathf.Min(_selectionStart.x, _selectionEnd.x),
                Mathf.Min(_selectionStart.y, _selectionEnd.y),
                Mathf.Abs(_selectionStart.x - _selectionEnd.x),
                Mathf.Abs(_selectionStart.y - _selectionEnd.y)
            );
            EditorGUI.DrawRect(visualRect, new Color(0, 1, 1, 0.3f));
        }
    }

    private void ApplySelection(Rect rect, VoxelModelData data) {
        int res = data.resolution;
        float xMinL = (Mathf.Min(_selectionStart.x, _selectionEnd.x) - rect.x) / rect.width;
        float xMaxL = (Mathf.Max(_selectionStart.x, _selectionEnd.x) - rect.x) / rect.width;
        float yMinL = (Mathf.Min(_selectionStart.y, _selectionEnd.y) - rect.y) / rect.height;
        float yMaxL = (Mathf.Max(_selectionStart.y, _selectionEnd.y) - rect.y) / rect.height;

        int x1 = Mathf.Clamp(Mathf.FloorToInt(xMinL * res), 0, res - 1);
        int x2 = Mathf.Clamp(Mathf.FloorToInt(xMaxL * res), 0, res - 1);
        int y1 = Mathf.Clamp(Mathf.FloorToInt((1.0f - yMaxL) * res), 0, res - 1);
        int y2 = Mathf.Clamp(Mathf.FloorToInt((1.0f - yMinL) * res), 0, res - 1);

        for (int y = y1; y <= y2; y++) {
            for (int x = x1; x <= x2; x++) {
                data.savedGroups[y * res + x] = data.currentEditingGroupId;
            }
        }
    }

    private void AutoGenerateGroups(VoxelModelData data) {
        int res = data.resolution;
        RenderTexture rt = RenderTexture.GetTemporary(res, res);
        Graphics.Blit(data.sourceImage, rt);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(res, res);
        tex.ReadPixels(new Rect(0, 0, res, res), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        Color[] pixels = tex.GetPixels();
        bool[] visited = new bool[res * res];
        int nextId = 1;

        for (int y = 0; y < res; y++) {
            for (int x = 0; x < res; x++) {
                int idx = y * res + x;
                if (pixels[idx].a < 0.1f || visited[idx])
                    continue;

                Queue<Vector2Int> q = new Queue<Vector2Int>();
                q.Enqueue(new Vector2Int(x, y));
                visited[idx] = true;
                Color32 target = (Color32)pixels[idx];

                while (q.Count > 0) {
                    Vector2Int curr = q.Dequeue();
                    data.savedGroups[curr.y * res + curr.x] = nextId;

                    Vector2Int[] neighbors = {
                        new Vector2Int(curr.x+1, curr.y),
                        new Vector2Int(curr.x-1, curr.y),
                        new Vector2Int(curr.x,   curr.y+1),
                        new Vector2Int(curr.x,   curr.y-1)
                    };
                    foreach (var n in neighbors) {
                        if (n.x >= 0 && n.x < res && n.y >= 0 && n.y < res) {
                            int nIdx = n.y * res + n.x;
                            if (!visited[nIdx] && pixels[nIdx].a > 0.1f) {
                                Color32 c = (Color32)pixels[nIdx];
                                if (c.r == target.r && c.g == target.g && c.b == target.b) {
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
    }
}
