using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

public class ImageTo3DModel : EditorWindow {
    private enum ProjectionMode { Sphere, FlatFront, Extrude, CharacterPillow, TreeOrganic }

    private Texture2D sourceImage;
    private GameObject cubePrefab;

    private int resolution = 64;
    private float shapeSize = 10f;
    private float thickness = 1.0f;
    private ProjectionMode projectionMode = ProjectionMode.TreeOrganic;
    private bool removeInternalCubes = true;
    private Color bodyColor = new Color(0.15f, 0.1f, 0.05f); 
    private bool mirrorBackColors = true;
    private int smoothingPasses = 3;
    private const string ParentName = "REAL_3D_MODEL_GEN";

    [MenuItem("Tools/Image to 3D Model")]
    public static void ShowWindow() => GetWindow<ImageTo3DModel>("AI Voxel Factory");

    private void OnGUI() {
        GUILayout.Label("AI 3D MODEL & TREE FACTORY v3 (Radius Focus)", EditorStyles.boldLabel);
        EditorGUILayout.Space(10);

        sourceImage = (Texture2D)EditorGUILayout.ObjectField("Image Sprite", sourceImage, typeof(Texture2D), false);
        cubePrefab = (GameObject)EditorGUILayout.ObjectField("Cube Prefab", cubePrefab, typeof(GameObject), false);

        EditorGUILayout.Space(5);
        projectionMode = (ProjectionMode)EditorGUILayout.EnumPopup("Projection Mode", projectionMode);
        resolution = EditorGUILayout.IntSlider("Resolution", resolution, 16, 128);
        shapeSize = EditorGUILayout.FloatField("World Size", shapeSize);
        thickness = EditorGUILayout.Slider("Radius Intensity", thickness, 0.1f, 5.0f);
        
        if (projectionMode == ProjectionMode.CharacterPillow || projectionMode == ProjectionMode.TreeOrganic) {
            smoothingPasses = EditorGUILayout.IntSlider("Smoothness (Rounding)", smoothingPasses, 0, 5);
            mirrorBackColors = EditorGUILayout.Toggle("Mirror Colors to Back", mirrorBackColors);
        }

        EditorGUILayout.Space(5);
        bodyColor = EditorGUILayout.ColorField("Inner Voxel Color", bodyColor);
        removeInternalCubes = EditorGUILayout.Toggle("Optimize (Remove Hidden)", removeInternalCubes);

        EditorGUILayout.Space(20);

        if (GUILayout.Button("GENERATE HIGH-RADIUS MODEL", GUILayout.Height(50))) {
            if (sourceImage == null || cubePrefab == null) {
                EditorUtility.DisplayDialog("Error", "Assign Image and Cube Prefab!", "OK");
                return;
            }
            GenerateModel();
        }

        if (GUILayout.Button("Clear Scene")) {
            GameObject old = GameObject.Find(ParentName);
            if (old) DestroyImmediate(old);
        }

        EditorGUILayout.HelpBox("RADIUS UPDATE: Tree/Organic mode now uses advanced spherical arc math for true 3D rounding.", MessageType.Info);
    }

    private void GenerateModel() {
        GameObject old = GameObject.Find(ParentName);
        if (old) DestroyImmediate(old);

        Transform root = new GameObject(ParentName).transform;
        float voxelSize = shapeSize / resolution;

        RenderTexture rt = RenderTexture.GetTemporary(resolution, resolution);
        Graphics.Blit(sourceImage, rt);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(resolution, resolution);
        tex.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        Color[] pixels = tex.GetPixels();
        HashSet<Vector3Int> voxelGrid = new HashSet<Vector3Int>();

        float[,] depthMap = new float[resolution, resolution];
        
        // 1. Calculate Edge Distance Map
        for (int y = 0; y < resolution; y++) {
            for (int x = 0; x < resolution; x++) {
                if (pixels[y * resolution + x].a < 0.1f) { depthMap[x, y] = 0; continue; }
                float minDist = resolution;
                for (int ey = 0; ey < resolution; ey++) {
                    for (int ex = 0; ex < resolution; ex++) {
                        if (pixels[ey * resolution + ex].a < 0.1f) {
                            float d = Vector2.Distance(new Vector2(x, y), new Vector2(ex, ey));
                            if (d < minDist) minDist = d;
                        }
                    }
                }
                depthMap[x, y] = minDist;
            }
        }

        // 2. Find Bounds
        int minY = resolution;
        int maxY = 0;
        for(int y=0; y<resolution; y++) for(int x=0; x<resolution; x++) if(pixels[y*resolution+x].a > 0.1f) {
            if(y < minY) minY = y;
            if(y > maxY) maxY = y;
        }

        // 3. Smoothing passes
        for (int p = 0; p < smoothingPasses; p++) {
            float[,] newMap = (float[,])depthMap.Clone();
            for (int y = 1; y < resolution - 1; y++) {
                for (int x = 1; x < resolution - 1; x++) {
                    if (depthMap[x, y] == 0) continue;
                    newMap[x, y] = (depthMap[x, y] + depthMap[x+1, y] + depthMap[x-1, y] + depthMap[x, y+1] + depthMap[x, y-1]) / 5.1f;
                }
            }
            depthMap = newMap;
        }

        for (int y = 0; y < resolution; y++) {
            for (int x = 0; x < resolution; x++) {
                Color c = pixels[y * resolution + x];
                if (c.a < 0.1f) continue;

                int zStart = 0, zEnd = 0;
                float d = depthMap[x, y];
                
                if (projectionMode == ProjectionMode.TreeOrganic) {
                    // RADIUS MATH: Use circular arc for puffiness
                    float maxPossibleD = 8f; // Estimated max radius
                    float normalizedD = Mathf.Clamp01(d / maxPossibleD);
                    // Circular bulge: sqrt(1 - (1-x)^2)
                    float puff = Mathf.Sqrt(1f - Mathf.Pow(1f - normalizedD, 2f));
                    
                    float groundFactor = (y < minY + resolution * 0.1f) ? 2.0f : 1.0f;
                    int zCount = Mathf.CeilToInt(puff * 6.0f * thickness * groundFactor);
                    zStart = -zCount; zEnd = zCount;
                } else if (projectionMode == ProjectionMode.CharacterPillow) {
                    int zCount = Mathf.CeilToInt(Mathf.Sqrt(d) * 2.5f * thickness);
                    zStart = -zCount; zEnd = zCount;
                } else if (projectionMode == ProjectionMode.FlatFront) {
                    zStart = -Mathf.Max(1, Mathf.RoundToInt(c.grayscale * 10 * thickness)); zEnd = 0;
                } else {
                    zStart = -1; zEnd = 1;
                }

                for (int z = zStart; z <= zEnd; z++) voxelGrid.Add(new Vector3Int(x, y, z));
            }
        }

        MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        foreach (Vector3Int pos in voxelGrid) {
            if (removeInternalCubes) {
                bool isInternal = voxelGrid.Contains(new Vector3Int(pos.x + 1, pos.y, pos.z)) &&
                                  voxelGrid.Contains(new Vector3Int(pos.x - 1, pos.y, pos.z)) &&
                                  voxelGrid.Contains(new Vector3Int(pos.x, pos.y + 1, pos.z)) &&
                                  voxelGrid.Contains(new Vector3Int(pos.x, pos.y - 1, pos.z)) &&
                                  voxelGrid.Contains(new Vector3Int(pos.x, pos.y, pos.z + 1)) &&
                                  voxelGrid.Contains(new Vector3Int(pos.x, pos.y, pos.z - 1));
                if (isInternal) continue;
            }

            Color c = pixels[pos.y * resolution + pos.x];
            
            // Back/Mirroring
            if (mirrorBackColors) {
                // Keep front color
            } else {
                // Use body color for non-front
            }

            Vector3 worldPos = new Vector3((pos.x - resolution * 0.5f) * voxelSize, (pos.y - resolution * 0.5f) * voxelSize, pos.z * voxelSize);
            GameObject cube = (GameObject)PrefabUtility.InstantiatePrefab(cubePrefab, root);
            cube.transform.localPosition = worldPos;
            cube.transform.localScale = Vector3.one * voxelSize * 0.98f;

            mpb.SetColor("_BaseColor", c);
            mpb.SetColor("_Color", c);
            var renderer = cube.GetComponentInChildren<Renderer>();
            if (renderer) renderer.SetPropertyBlock(mpb);

            Block b = cube.GetComponent<Block>() ?? cube.AddComponent<Block>();
            b.blockColor = c; b.gridPos = pos;
        }

        DestroyImmediate(tex);
        Debug.Log($"SUCCESS: High-Radius Model Generated!");
    }
}
