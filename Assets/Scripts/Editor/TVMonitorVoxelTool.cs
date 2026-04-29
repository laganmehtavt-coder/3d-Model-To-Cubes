using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

public class TVMonitorVoxelTool : EditorWindow {
    private Texture2D img;
    private GameObject prefab;
    private int res = 80;
    private float size = 12f;
    private float monitorDepth = 3.0f;
    private float gapX = 0f, gapY = 0f, gapZ = 0f;
    private bool saveToSOS = false;
    private Color casingColor = new Color(0.12f, 0.12f, 0.12f);

    [MenuItem("Tools/Voxel Tools/3. TV & Monitor Maker")]
    public static void Open() => GetWindow<TVMonitorVoxelTool>("TV Maker");

    private void OnGUI() {
        GUILayout.Label("PROFESSIONAL DESKTOP MONITOR FACTORY", EditorStyles.boldLabel);
        img = (Texture2D)EditorGUILayout.ObjectField("Monitor Sprite", img, typeof(Texture2D), false);
        prefab = (GameObject)EditorGUILayout.ObjectField("Cube Prefab", prefab, typeof(GameObject), false);
        
        EditorGUILayout.Space(5);
        res = EditorGUILayout.IntSlider("Resolution", res, 32, 128);
        size = EditorGUILayout.FloatField("World Size", size);
        monitorDepth = EditorGUILayout.Slider("Back Curvature (Depth)", monitorDepth, 1.0f, 8.0f);
        
        EditorGUILayout.Space(5);
        GUILayout.Label("Gaps / Spacing", EditorStyles.miniBoldLabel);
        gapX = EditorGUILayout.Slider("Gap X", gapX, 0f, 0.2f);
        gapY = EditorGUILayout.Slider("Gap Y", gapY, 0f, 0.2f);
        gapZ = EditorGUILayout.Slider("Gap Z", gapZ, 0f, 0.2f);
        
        casingColor = EditorGUILayout.ColorField("Casing Color", casingColor);
        saveToSOS = EditorGUILayout.Toggle("Save to SOS Folder?", saveToSOS);

        if (GUILayout.Button("GENERATE ACTUAL DESKTOP", GUILayout.Height(50))) Generate();
    }

    private void Generate() {
        if (!img || !prefab) return;
        GameObject root = new GameObject("ACTUAL_DESKTOP_MODEL_" + img.name);
        root.AddComponent<ModelRotator>();

        float vox = size / res;
        Texture2D tex = GetReadableTex();
        Color[] pix = tex.GetPixels();

        HashSet<Vector3Int> grid = new HashSet<Vector3Int>();
        Dictionary<Vector3Int, Color> colorMap = new Dictionary<Vector3Int, Color>();

        // 1. Find Bounds
        int minX = res, maxX = 0, minY = res, maxY = 0;
        for (int y = 0; y < res; y++) {
            for (int x = 0; x < res; x++) {
                if (pix[y * res + x].a > 0.1f) {
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                }
            }
        }

        int screenBottom = minY + (int)((maxY - minY) * 0.25f);
        float centerX = (minX + maxX) * 0.5f;
        float centerY = (screenBottom + maxY) * 0.5f;
        float maxDist = Vector2.Distance(new Vector2(minX, screenBottom), new Vector2(centerX, centerY));

        for (int y = minY; y <= maxY; y++) {
            for (int x = minX; x <= maxX; x++) {
                Color c = pix[y * res + x];
                if (c.a < 0.1f) continue;

                bool isScreenArea = y >= screenBottom;

                if (isScreenArea) {
                    // Actual Monitor Logic
                    bool isEdge = (x == minX || x == maxX || y == maxY || y == screenBottom);
                    
                    // Curve Math for the back
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(centerX, centerY));
                    float normDist = Mathf.Clamp01(dist / maxDist);
                    // Spherical bulge for the back
                    int backZ = Mathf.CeilToInt(Mathf.Sqrt(1 - normDist * normDist) * monitorDepth);

                    for (int z = -backZ; z <= 1; z++) {
                        Vector3Int pos = new Vector3Int(x, y, z);
                        grid.Add(pos);

                        if (z == 1) {
                            // Bezel/Frame is at Z=1
                            colorMap[pos] = isEdge ? casingColor : Color.clear; // Clear means we look at Z=0
                            if (isEdge) colorMap[pos] = casingColor;
                            else { grid.Remove(pos); continue; } // Don't place bezel cubes in center
                        }
                        else if (z == 0) {
                            // Recessed Screen is at Z=0
                            colorMap[pos] = isEdge ? casingColor : c;
                        }
                        else {
                            // Back Casing
                            colorMap[pos] = casingColor;
                        }
                    }
                } 
                else {
                    // Solid Volumetric Stand
                    for (int z = -Mathf.RoundToInt(monitorDepth * 0.3f); z <= 0; z++) {
                        Vector3Int pos = new Vector3Int(x, y, z);
                        grid.Add(pos);
                        colorMap[pos] = casingColor;
                    }
                }
            }
        }

        MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        foreach (var pos in grid) {
            if (grid.Contains(pos + Vector3Int.right) && grid.Contains(pos + Vector3Int.left) &&
                grid.Contains(pos + Vector3Int.up) && grid.Contains(pos + Vector3Int.down) &&
                grid.Contains(pos + new Vector3Int(0,0,1)) && grid.Contains(pos + new Vector3Int(0,0,-1))) {
                continue;
            }

            float px = (pos.x - res / 2) * (vox + gapX);
            float py = (pos.y - res / 2) * (vox + gapY);
            float pz = pos.z * (vox + gapZ);

            GameObject g = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
            g.transform.localPosition = new Vector3(px, py, pz);
            g.transform.localScale = Vector3.one * vox * 0.98f;

            Color finalC = colorMap[pos];
            mpb.SetColor("_Color", finalC);
            mpb.SetColor("_BaseColor", finalC);
            var r = g.GetComponentInChildren<Renderer>();
            if (r) r.SetPropertyBlock(mpb);
        }

        if (saveToSOS) SaveSOSAsset();
        DestroyImmediate(tex);
    }

    private void SaveSOSAsset() {
        string path = "Assets/SOS/" + img.name + "_Desktop.asset";
        VoxelModelData data = ScriptableObject.CreateInstance<VoxelModelData>();
        data.sourceImage = img;
        data.resolution = res;
        data.worldSize = size;
        data.gapX = gapX;
        data.gapY = gapY;
        data.shapeType = VoxelShapeType.Flat;
        AssetDatabase.CreateAsset(data, path);
        AssetDatabase.SaveAssets();
    }

    private Texture2D GetReadableTex() {
        RenderTexture rt = RenderTexture.GetTemporary(res, res);
        Graphics.Blit(img, rt);
        RenderTexture.active = rt;
        Texture2D t = new Texture2D(res, res);
        t.ReadPixels(new Rect(0, 0, res, res), 0, 0);
        t.Apply();
        RenderTexture.ReleaseTemporary(rt);
        return t;
    }
}
