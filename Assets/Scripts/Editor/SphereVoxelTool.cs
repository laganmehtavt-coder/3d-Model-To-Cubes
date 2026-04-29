using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;

public class SphereVoxelTool : EditorWindow {
    private Texture2D sourceImage;
    private GameObject cubePrefab;

    private int resolution = 64;
    private float sphereSize = 10f;
    private float puffiness = 1.0f;
    private float gapX = 0f, gapY = 0f, gapZ = 0f;
    private bool removeInternal = true;
    private bool saveToSOS = false;

    [Header("Actual Ball Logic")]
    private bool useSphericalWrap = true;
    private bool emojiMode = false;
    private bool footballMode = false;
    private bool simplifyColors = true;
    private Color ballColor = Color.white; 
    private Color lineColor = Color.black;
    private float lineThreshold = 0.4f;

    // Icosahedron vertices for perfect football pattern
    private static readonly float phi = (1f + Mathf.Sqrt(5f)) / 2f;
    private static readonly Vector3[] icoVertices = {
        new Vector3(0, 1, phi), new Vector3(0, 1, -phi), new Vector3(0, -1, phi), new Vector3(0, -1, -phi),
        new Vector3(1, phi, 0), new Vector3(1, -phi, 0), new Vector3(-1, phi, 0), new Vector3(-1, -phi, 0),
        new Vector3(phi, 0, 1), new Vector3(phi, 0, -1), new Vector3(-phi, 0, 1), new Vector3(-phi, 0, -1)
    };

    [Header("Glow Settings")]
    private bool useGlow = true;
    private float glowIntensity = 1.5f;
    private float glowThreshold = 0.5f;

    [MenuItem("Tools/Voxel Tools/2. Sphere & Ball Maker")]
    public static void Open() => GetWindow<SphereVoxelTool>("Sphere Maker");

    private void OnGUI() {
        GUILayout.Label("ACTUAL 3D BALL & EMOJI FACTORY", EditorStyles.boldLabel);
        sourceImage = (Texture2D)EditorGUILayout.ObjectField("Ball/Emoji Sprite", sourceImage, typeof(Texture2D), false);
        cubePrefab = (GameObject)EditorGUILayout.ObjectField("Cube Prefab", cubePrefab, typeof(GameObject), false);
        
        EditorGUILayout.Space(5);
        resolution = EditorGUILayout.IntSlider("Resolution", resolution, 32, 128);
        sphereSize = EditorGUILayout.FloatField("World Size", sphereSize);
        puffiness = EditorGUILayout.Slider("Z-Depth Scale", puffiness, 0.5f, 1.5f);

        EditorGUILayout.Space(5);
        GUILayout.Label("Pattern & Style", EditorStyles.miniBoldLabel);
        emojiMode = EditorGUILayout.Toggle("Emoji Mode (Face Front)", emojiMode);
        footballMode = EditorGUILayout.Toggle("Hardcoded Football Pattern", footballMode);
        if (!emojiMode && !footballMode) useSphericalWrap = EditorGUILayout.Toggle("360 Wrap Pattern", useSphericalWrap);
        
        EditorGUILayout.Space(5);
        simplifyColors = EditorGUILayout.Toggle("Use Custom Colors", simplifyColors);
        if (simplifyColors || footballMode || emojiMode) {
            ballColor = EditorGUILayout.ColorField(emojiMode ? "Emoji Base Color" : "Main Color", ballColor);
            lineColor = EditorGUILayout.ColorField("Pattern Color", lineColor);
            if (!footballMode && !emojiMode) lineThreshold = EditorGUILayout.Slider("Line Sensitivity", lineThreshold, 0.1f, 0.9f);
        }

        EditorGUILayout.Space(5);
        GUILayout.Label("Gaps / Spacing", EditorStyles.miniBoldLabel);
        gapX = EditorGUILayout.Slider("Gap X", gapX, 0f, 0.2f);
        gapY = EditorGUILayout.Slider("Gap Y", gapY, 0f, 0.2f);
        gapZ = EditorGUILayout.Slider("Gap Z", gapZ, 0f, 0.2f);

        removeInternal = EditorGUILayout.Toggle("Remove Internal Cubes", removeInternal);
        saveToSOS = EditorGUILayout.Toggle("Save to SOS Folder?", saveToSOS);

        EditorGUILayout.Space(5);
        GUILayout.Label("Post-Process (Glow)", EditorStyles.miniBoldLabel);
        useGlow = EditorGUILayout.Toggle("Bright Color Glow", useGlow);
        if (useGlow) {
            glowIntensity = EditorGUILayout.Slider("Intensity", glowIntensity, 0f, 5f);
            glowThreshold = EditorGUILayout.Slider("Sensitivity", glowThreshold, 0f, 1f);
        }

        EditorGUILayout.Space(10);
        if (GUILayout.Button("GENERATE ACTUAL 3D BALL", GUILayout.Height(50))) Generate();
    }

    private void Generate() {
        if (!sourceImage && !footballMode) return;
        if (!cubePrefab) return;

        GameObject root = new GameObject(footballMode ? "ACTUAL_3D_FOOTBALL" : "ACTUAL_3D_BALL_" + sourceImage.name);
        root.AddComponent<ModelRotator>();
        
        float voxelSize = sphereSize / resolution;
        Texture2D tex = sourceImage ? GetReadableTex(sourceImage.width, sourceImage.height) : null;
        
        HashSet<Vector3Int> grid = new HashSet<Vector3Int>();
        Dictionary<Vector3Int, Color> colorMap = new Dictionary<Vector3Int, Color>();

        float center = (resolution - 1) * 0.5f;
        float radius = resolution * 0.48f;

        for (int z = -resolution/2; z <= resolution/2; z++) {
            for (int y = 0; y < resolution; y++) {
                for (int x = 0; x < resolution; x++) {
                    float dx = x - center;
                    float dy = y - center;
                    float dz = z / puffiness;

                    float dist3D = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (dist3D > radius) continue;

                    Vector3Int pos = new Vector3Int(x, y, z);
                    grid.Add(pos);

                    Color c = ballColor;

                    if (footballMode) {
                        Vector3 pointOnSphere = new Vector3(dx, dy, dz).normalized;
                        float dot1 = -2f, dot2 = -2f;
                        foreach (Vector3 v in icoVertices) {
                            float d = Vector3.Dot(pointOnSphere, v.normalized);
                            if (d > dot1) { dot2 = dot1; dot1 = d; }
                            else if (d > dot2) { dot2 = d; }
                        }
                        if (dot1 > 0.88f) c = lineColor; 
                        else if (Mathf.Abs(dot1 - dot2) < 0.035f) c = lineColor; 
                        else c = ballColor;
                    } else if (tex != null) {
                        if (emojiMode) {
                            if (dz > 0) { 
                                float u = (dx / radius + 1f) * 0.5f;
                                float v = (dy / radius + 1f) * 0.5f;
                                Color imgC = tex.GetPixelBilinear(u, v);
                                if (imgC.a > 0.2f) c = imgC;
                                else c = ballColor;
                            } else {
                                c = ballColor; 
                            }
                        } else if (useSphericalWrap) {
                            float lon = Mathf.Atan2(dz, dx); 
                            float lat = Mathf.Acos(Mathf.Clamp(dy / dist3D, -1f, 1f)); 
                            float u = (lon + Mathf.PI) / (Mathf.PI * 2f);
                            float v = 1f - (lat / Mathf.PI);
                            c = tex.GetPixelBilinear(u, v);
                        } else {
                            float u = (dx / radius + 1f) * 0.5f;
                            float v = (dy / radius + 1f) * 0.5f;
                            c = tex.GetPixelBilinear(u, v);
                        }

                        if (simplifyColors && !emojiMode) {
                            float brightness = c.grayscale;
                            if (brightness < lineThreshold) c = lineColor;
                            else if (c.a > 0.1f) c = ballColor;
                        }
                    }

                    if (c.a < 0.1f) continue; 
                    colorMap[pos] = c;
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

            float px = (pos.x - center) * (voxelSize + gapX);
            float py = (pos.y - center) * (voxelSize + gapY);
            float pz = pos.z * (voxelSize + gapZ);

            GameObject g = (GameObject)PrefabUtility.InstantiatePrefab(cubePrefab, root.transform);
            g.transform.localPosition = new Vector3(px, py, pz);
            g.transform.localScale = Vector3.one * voxelSize * 0.98f;

            if (colorMap.TryGetValue(pos, out Color c)) {
                mpb.SetColor("_Color", c);
                mpb.SetColor("_BaseColor", c);
                
                // Neon Glow Logic
                if (useGlow && c.grayscale > glowThreshold) {
                    mpb.SetColor("_EmissionColor", c * glowIntensity);
                } else {
                    mpb.SetColor("_EmissionColor", Color.black);
                }

                var r = g.GetComponentInChildren<Renderer>();
                if (r) r.SetPropertyBlock(mpb);
            }
        }

        if (saveToSOS && sourceImage != null) SaveSOSAsset();
        DestroyImmediate(tex);
        Debug.Log("Actual 3D Ball Generated Successfully!");
    }

    private void SaveSOSAsset() {
        string path = "Assets/SOS/" + sourceImage.name + "_Ball.asset";
        VoxelModelData data = ScriptableObject.CreateInstance<VoxelModelData>();
        data.sourceImage = sourceImage;
        data.resolution = resolution;
        data.worldSize = sphereSize;
        data.gapX = gapX;
        data.gapY = gapY;
        data.shapeType = VoxelShapeType.Sphere;
        AssetDatabase.CreateAsset(data, path);
        AssetDatabase.SaveAssets();
    }

    private Texture2D GetReadableTex(int width, int height) {
        RenderTexture rt = RenderTexture.GetTemporary(width, height);
        Graphics.Blit(sourceImage, rt);
        RenderTexture.active = rt;
        Texture2D t = new Texture2D(width, height);
        t.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        t.Apply();
        RenderTexture.ReleaseTemporary(rt);
        return t;
    }
}
