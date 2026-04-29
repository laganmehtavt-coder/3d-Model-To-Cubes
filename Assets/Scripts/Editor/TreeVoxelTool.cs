using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

public class TreeVoxelTool : EditorWindow {
    private Texture2D img;
    private GameObject prefab;
    private int res = 64;
    private float size = 10f;
    private float depth = 1.0f;
    private int maxColors = 5;
    private bool mirror = true;

    [MenuItem("Tools/Voxel Tools/1. Tree Maker")]
    public static void Open() => GetWindow<TreeVoxelTool>("Tree Maker");

    private void OnGUI() {
        GUILayout.Label("SPECIALIZED TREE MAKER", EditorStyles.boldLabel);
        img = (Texture2D)EditorGUILayout.ObjectField("Tree Sprite", img, typeof(Texture2D), false);
        prefab = (GameObject)EditorGUILayout.ObjectField("Cube Prefab", prefab, typeof(GameObject), false);
        res = EditorGUILayout.IntSlider("Resolution", res, 16, 128);
        depth = EditorGUILayout.Slider("Tree Thickness", depth, 0.5f, 5.0f);
        maxColors = EditorGUILayout.IntSlider("Max Colors (Simplify)", maxColors, 2, 8);
        mirror = EditorGUILayout.Toggle("Mirror Back Face", mirror);

        if (GUILayout.Button("GENERATE 3D TREE", GUILayout.Height(40))) Generate();
    }

    private void Generate() {
        if (!img || !prefab) return;
        GameObject root = new GameObject("3D_TREE");
        float vox = size / res;
        Texture2D tex = GetReadableTex();
        Color[] pix = tex.GetPixels();
        List<Color> palette = GetPalette(pix, maxColors);

        float[,] dMap = new float[res, res];
        for (int y = 0; y < res; y++) {
            for (int x = 0; x < res; x++) {
                if (pix[y * res + x].a < 0.1f) continue;
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

        for (int y = 0; y < res; y++) {
            for (int x = 0; x < res; x++) {
                Color c = pix[y * res + x];
                if (c.a < 0.1f) continue;
                c = SnapToPalette(c, palette);

                float dist = dMap[x, y];
                float ground = (y < res * 0.15f) ? 2.5f : 1.0f;
                int zC = Mathf.CeilToInt(Mathf.Sqrt(dist) * 2.5f * depth * ground);

                for (int z = -zC; z <= zC; z++) {
                    if (z != -zC && z != zC && z != 0) continue; 
                    CreateCube(x, y, z, c, vox, root.transform);
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
        g.transform.localPosition = new Vector3((x - res / 2) * vox, (y - res / 2) * vox, z * vox);
        g.transform.localScale = Vector3.one * vox * 0.98f;
        var r = g.GetComponentInChildren<Renderer>();
        if (r) {
            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            mpb.SetColor("_Color", c); mpb.SetColor("_BaseColor", c);
            r.SetPropertyBlock(mpb);
        }
    }
}
