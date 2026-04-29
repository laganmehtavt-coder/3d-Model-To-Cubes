using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

public class BusVoxelTool : EditorWindow {
    private Texture2D img;
    private GameObject prefab;
    private int res = 64;
    private float size = 10f;
    private float busWidth = 5.0f;
    private float gapX = 0.0f, gapY = 0.0f, gapZ = 0.0f;
    private int maxColors = 4;
    private Color busBodyColor = new Color(0.9f, 0.6f, 0.1f);

    [MenuItem("Tools/Voxel Tools/4. Bus & Vehicle Maker")]
    public static void Open() => GetWindow<BusVoxelTool>("Bus Maker");

    private void OnGUI() {
        GUILayout.Label("AI SMART BUS FACTORY (With Gaps)", EditorStyles.boldLabel);
        img = (Texture2D)EditorGUILayout.ObjectField("Bus Side Sprite", img, typeof(Texture2D), false);
        prefab = (GameObject)EditorGUILayout.ObjectField("Cube Prefab", prefab, typeof(GameObject), false);
        res = EditorGUILayout.IntSlider("Resolution", res, 16, 128);
        busWidth = EditorGUILayout.Slider("Bus Width (Z-Depth)", busWidth, 2.0f, 10.0f);
        
        EditorGUILayout.Space(5);
        gapX = EditorGUILayout.Slider("Gap X", gapX, 0f, 0.5f);
        gapY = EditorGUILayout.Slider("Gap Y", gapY, 0f, 0.5f);
        gapZ = EditorGUILayout.Slider("Gap Z", gapZ, 0f, 0.5f);
        
        maxColors = EditorGUILayout.IntSlider("Max Colors", maxColors, 2, 8);
        busBodyColor = EditorGUILayout.ColorField("Main Body Color", busBodyColor);

        if (GUILayout.Button("GENERATE BUS WITH GAPS", GUILayout.Height(50))) Generate();
    }

    private void Generate() {
        if (!img || !prefab) return;
        GameObject root = new GameObject("AI_GAPPED_BUS_MODEL");
        float vox = size / res;
        Texture2D tex = GetReadableTex();
        Color[] pix = tex.GetPixels();
        List<Color> palette = GetPalette(pix, maxColors);

        int zHalf = Mathf.RoundToInt(busWidth);
        
        int minX = res, maxX = 0, minY = res, maxY = 0;
        for (int y = 0; y < res; y++) {
            for (int x = 0; x < res; x++) {
                if (pix[y * res + x].a > 0.1f) {
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                }
            }
        }

        for (int y = 0; y < res; y++) {
            for (int x = 0; x < res; x++) {
                Color sideC = pix[y * res + x];
                if (sideC.a < 0.1f) continue;
                sideC = SnapToPalette(sideC, palette);

                bool isTop = (y < res - 1 && pix[(y + 1) * res + x].a < 0.1f) || y == maxY;
                bool isBottom = (y > 0 && pix[(y - 1) * res + x].a < 0.1f) || y == minY;
                bool isFront = (x < res - 1 && pix[y * res + (x + 1)].a < 0.1f) || x == maxX;
                bool isBack = (x > 0 && pix[y * res + (x - 1)].a < 0.1f) || x == minX;

                for (int z = -zHalf; z <= zHalf; z++) {
                    bool isSideFace = (z == -zHalf || z == zHalf);
                    if (isSideFace || isTop || isBottom || isFront || isBack) {
                        Color finalC = isSideFace ? sideC : busBodyColor;
                        if (isFront && sideC.b > 0.4f && sideC.g > 0.4f) finalC = sideC;
                        if (isFront && y > minY + 2 && y < minY + 6 && Mathf.Abs(z) > zHalf - 2) finalC = new Color(1, 0.95f, 0.8f);
                        if (isBack && y > minY + 2 && y < minY + 5 && Mathf.Abs(z) > zHalf - 2) finalC = Color.red;
                        if ((isFront || isBack) && y < minY + 3) finalC = Color.black;

                        CreateCube(x, y, z, finalC, vox, root.transform);
                    }
                }

                if (sideC.grayscale < 0.2f && y < minY + (maxY-minY)*0.3f) {
                    CreateCube(x, y, zHalf + 1, Color.black, vox, root.transform);
                    CreateCube(x, y, -zHalf - 1, Color.black, vox, root.transform);
                }
            }
        }
        DestroyImmediate(tex);
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

    private List<Color> GetPalette(Color[] pix, int count) {
        Dictionary<Color32, int> counts = new Dictionary<Color32, int>();
        foreach (var c in pix) if (c.a > 0.1f) { Color32 c32 = c; if (counts.ContainsKey(c32)) counts[c32]++; else counts[c32] = 1; }
        var sorted = new List<KeyValuePair<Color32, int>>(counts);
        sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
        List<Color> pal = new List<Color>();
        for (int i = 0; i < Mathf.Min(count, sorted.Count); i++) pal.Add(sorted[i].Key);
        return pal;
    }

    private Color SnapToPalette(Color c, List<Color> pal) {
        Color best = c; float min = 2f;
        foreach (var p in pal) { float d = Vector4.Distance(c, p); if (d < min) { min = d; best = p; } }
        return best;
    }

    private void CreateCube(int x, int y, int z, Color c, float vox, Transform p) {
        GameObject g = (GameObject)PrefabUtility.InstantiatePrefab(prefab, p);
        float px = (x - res / 2) * (vox + gapX);
        float py = (y - res / 2) * (vox + gapY);
        float pz = z * (vox + gapZ);
        g.transform.localPosition = new Vector3(px, py, pz);
        g.transform.localScale = Vector3.one * vox * 0.98f;
        var r = g.GetComponentInChildren<Renderer>();
        if (r) {
            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            mpb.SetColor("_Color", c); mpb.SetColor("_BaseColor", c);
            r.SetPropertyBlock(mpb);
        }
    }
}
