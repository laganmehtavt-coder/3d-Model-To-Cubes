using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

public class RemoteVoxelTool : EditorWindow {
    private Texture2D img;
    private GameObject prefab;
    private int res = 80;
    private float size = 10f;
    private float bodyThickness = 2.0f;
    private float buttonHeight = 1.0f;
    private float buttonSensitivity = 0.4f;
    private float gapX = 0f, gapY = 0f, gapZ = 0f;
    private bool saveToSOS = false;
    private Color backColor = Color.white;

    [MenuItem("Tools/Voxel Tools/6. Remote Maker")]
    public static void Open() => GetWindow<RemoteVoxelTool>("Remote Maker");

    private void OnGUI() {
        GUILayout.Label("ACTUAL 3D REMOTE FACTORY (With 3D Buttons)", EditorStyles.boldLabel);
        img = (Texture2D)EditorGUILayout.ObjectField("Remote Sprite", img, typeof(Texture2D), false);
        prefab = (GameObject)EditorGUILayout.ObjectField("Cube Prefab", prefab, typeof(GameObject), false);
        
        EditorGUILayout.Space(5);
        res = EditorGUILayout.IntSlider("Resolution", res, 32, 128);
        size = EditorGUILayout.FloatField("World Size", size);
        
        EditorGUILayout.Space(5);
        GUILayout.Label("Thickness Settings", EditorStyles.miniBoldLabel);
        bodyThickness = EditorGUILayout.Slider("Back Thickness", bodyThickness, 1.0f, 5.0f);
        buttonHeight = EditorGUILayout.Slider("Button 3D Height", buttonHeight, 0.0f, 2.0f);
        buttonSensitivity = EditorGUILayout.Slider("Button Detection", buttonSensitivity, 0.1f, 0.9f);
        
        EditorGUILayout.Space(5);
        GUILayout.Label("Gaps / Spacing", EditorStyles.miniBoldLabel);
        gapX = EditorGUILayout.Slider("Gap X", gapX, 0f, 0.2f);
        gapY = EditorGUILayout.Slider("Gap Y", gapY, 0f, 0.2f);
        gapZ = EditorGUILayout.Slider("Gap Z", gapZ, 0f, 0.2f);
        
        backColor = EditorGUILayout.ColorField("Remote Body Color", backColor);
        saveToSOS = EditorGUILayout.Toggle("Save to SOS Folder?", saveToSOS);

        if (GUILayout.Button("GENERATE ACTUAL 3D REMOTE", GUILayout.Height(50))) Generate();
    }

    private void Generate() {
        if (!img || !prefab) return;
        GameObject root = new GameObject("ACTUAL_3D_REMOTE_" + img.name);
        root.AddComponent<ModelRotator>();

        float vox = size / res;
        Texture2D tex = GetReadableTex();
        Color[] pix = tex.GetPixels();

        HashSet<Vector3Int> grid = new HashSet<Vector3Int>();
        Dictionary<Vector3Int, Color> colorMap = new Dictionary<Vector3Int, Color>();

        // 1. Edge Distance Map for rounded back
        float[,] dMap = new float[res, res];
        for (int y = 0; y < res; y++) {
            for (int x = 0; x < res; x++) {
                if (pix[y * res + x].a < 0.1f) { dMap[x, y] = 0; continue; }
                float minDist = res;
                for (int ey = 0; ey < res; ey++) {
                    for (int ex = 0; ex < res; ex++) {
                        if (pix[ey * res + ex].a < 0.1f) {
                            float d = Vector2.Distance(new Vector2(x, y), new Vector2(ex, ey));
                            if (d < minDist) minDist = d;
                        }
                    }
                }
                dMap[x, y] = minDist;
            }
        }

        // 2. Volumetric Generation
        for (int y = 0; y < res; y++) {
            for (int x = 0; x < res; x++) {
                Color c = pix[y * res + x];
                if (c.a < 0.1f) continue;

                float dist = dMap[x, y];
                // Rounded back thickness
                int zBack = Mathf.CeilToInt(Mathf.Sqrt(dist) * bodyThickness);
                
                // 3D Button Logic: If pixel is different from "average" remote color or just dark/light enough
                // We use grayscale sensitivity to pop buttons forward
                bool isButton = c.grayscale < buttonSensitivity || c.grayscale > (1.0f - buttonSensitivity * 0.5f);
                int zFront = isButton ? Mathf.RoundToInt(buttonHeight) : 0;

                for (int z = -zBack; z <= zFront; z++) {
                    Vector3Int pos = new Vector3Int(x, y, z);
                    grid.Add(pos);
                    
                    // Color: Z > 0 (Buttons) get image color. 
                    // Z = 0 (Face) gets image color.
                    // Z < 0 (Back) gets Body Color.
                    if (z >= 0) colorMap[pos] = c;
                    else colorMap[pos] = backColor;
                }
            }
        }

        // 3. Instantiate
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
        Debug.Log("Actual 3D Remote with Buttons Generated!");
    }

    private void SaveSOSAsset() {
        string path = "Assets/SOS/" + img.name + "_Remote.asset";
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
