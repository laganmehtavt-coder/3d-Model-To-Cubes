using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

public class BottleVoxelTool : EditorWindow {
    private Texture2D sourceImage;
    private GameObject cubePrefab;

    private int resolution = 80;
    private float bottleHeight = 15f;
    private float gapX = 0f, gapY = 0f, gapZ = 0f;
    private bool removeInternal = true;
    private bool saveToSOS = false;

    [MenuItem("Tools/Voxel Tools/5. Bottle Volumetric Maker")]
    public static void Open() => GetWindow<BottleVoxelTool>("Bottle Maker");

    private void OnGUI() {
        GUILayout.Label("VOLUMETRIC BOTTLE FACTORY", EditorStyles.boldLabel);
        sourceImage = (Texture2D)EditorGUILayout.ObjectField("Bottle Image", sourceImage, typeof(Texture2D), false);
        cubePrefab = (GameObject)EditorGUILayout.ObjectField("Cube Prefab", cubePrefab, typeof(GameObject), false);
        
        EditorGUILayout.Space(5);
        resolution = EditorGUILayout.IntSlider("Resolution", resolution, 40, 200);
        bottleHeight = EditorGUILayout.FloatField("Bottle Height", bottleHeight);
        
        EditorGUILayout.Space(5);
        GUILayout.Label("Gaps / Spacing", EditorStyles.miniBoldLabel);
        gapX = EditorGUILayout.Slider("Gap X", gapX, 0f, 0.5f);
        gapY = EditorGUILayout.Slider("Gap Y", gapY, 0f, 0.5f);
        gapZ = EditorGUILayout.Slider("Gap Z", gapZ, 0f, 0.5f);

        removeInternal = EditorGUILayout.Toggle("Remove Internal Cubes", removeInternal);
        saveToSOS = EditorGUILayout.Toggle("Save to SOS Folder?", saveToSOS);

        EditorGUILayout.Space(10);
        if (GUILayout.Button("GENERATE 3D BOTTLE", GUILayout.Height(50))) Generate();
    }

    private void Generate() {
        if (!sourceImage || !cubePrefab) return;

        GameObject root = new GameObject("3D_BOTTLE_MODEL_" + sourceImage.name);
        root.AddComponent<ModelRotator>();
        float voxelSize = bottleHeight / resolution;
        Texture2D tex = GetReadableTex(resolution);
        Color[] pixels = tex.GetPixels();

        HashSet<Vector3Int> grid = new HashSet<Vector3Int>();
        Dictionary<Vector3Int, Color> colorMap = new Dictionary<Vector3Int, Color>();

        // 1. Core Logic: Row-by-Row Scanning
        for (int y = 0; y < resolution; y++) {
            int xStart = -1;
            int xEnd = -1;

            for (int x = 0; x < resolution; x++) {
                if (pixels[y * resolution + x].a > 0.1f) {
                    if (xStart == -1) xStart = x;
                    xEnd = x;
                }
            }

            if (xStart == -1) continue;

            float centerX = (xStart + xEnd) * 0.5f;
            float radius = (xEnd - xStart) * 0.5f;

            if (radius <= 0) radius = 0.5f;

            int rInt = Mathf.CeilToInt(radius);
            for (int ix = xStart; ix <= xEnd; ix++) {
                for (int iz = -rInt; iz <= rInt; iz++) {
                    float dx = ix - centerX;
                    float dz = iz;

                    if ((dx * dx) + (dz * dz) <= (radius * radius)) {
                        Vector3Int pos = new Vector3Int(ix, y, iz);
                        grid.Add(pos);
                        colorMap[pos] = pixels[y * resolution + ix];
                    }
                }
            }
        }

        MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        foreach (var pos in grid) {
            if (removeInternal) {
                if (grid.Contains(pos + Vector3Int.right) && grid.Contains(pos + Vector3Int.left) &&
                    grid.Contains(pos + Vector3Int.up) && grid.Contains(pos + Vector3Int.down) &&
                    grid.Contains(pos + new Vector3Int(0,0,1)) && grid.Contains(pos + new Vector3Int(0,0,-1))) {
                    continue;
                }
            }

            float px = (pos.x - resolution * 0.5f) * (voxelSize + gapX);
            float py = (pos.y - resolution * 0.5f) * (voxelSize + gapY);
            float pz = pos.z * (voxelSize + gapZ);

            GameObject g = (GameObject)PrefabUtility.InstantiatePrefab(cubePrefab, root.transform);
            g.transform.localPosition = new Vector3(px, py, pz);
            g.transform.localScale = Vector3.one * voxelSize * 0.98f;

            Color c = colorMap[pos];
            mpb.SetColor("_Color", c);
            mpb.SetColor("_BaseColor", c);
            var r = g.GetComponentInChildren<Renderer>();
            if (r) r.SetPropertyBlock(mpb);
        }

        if (saveToSOS) {
            SaveSOSAsset();
        }

        DestroyImmediate(tex);
        Debug.Log("Bottle Generated Successfully!");
    }

    private void SaveSOSAsset() {
        string path = "Assets/SOS/" + sourceImage.name + "_Bottle.asset";
        VoxelModelData data = ScriptableObject.CreateInstance<VoxelModelData>();
        data.sourceImage = sourceImage;
        data.resolution = resolution;
        data.worldSize = bottleHeight;
        data.gapX = gapX;
        data.gapY = gapY;
        data.shapeType = VoxelShapeType.Cylinder;
        data.use3DDepth = true;

        AssetDatabase.CreateAsset(data, path);
        AssetDatabase.SaveAssets();
        Debug.Log("Saved Voxel Data to: " + path);
    }

    private Texture2D GetReadableTex(int res) {
        RenderTexture rt = RenderTexture.GetTemporary(res, res);
        Graphics.Blit(sourceImage, rt);
        RenderTexture.active = rt;
        Texture2D t = new Texture2D(res, res);
        t.ReadPixels(new Rect(0, 0, res, res), 0, 0);
        t.Apply();
        RenderTexture.ReleaseTemporary(rt);
        return t;
    }
}
