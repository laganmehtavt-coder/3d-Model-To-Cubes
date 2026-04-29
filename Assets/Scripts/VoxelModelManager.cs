using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class VoxelModelManager : MonoBehaviour {

    [Header("Models (SOS)")]
    public List<VoxelModelData> models = new List<VoxelModelData>();

    [Header("Fallback Images")]
    public List<Texture2D> fallbackImages = new List<Texture2D>();

    [Header("Prefab")]
    public GameObject cubePrefab;
    
    [Header("UI Controls")]
    public RawImage previewRaw;
    public TextMeshProUGUI indexText;
    public TextMeshProUGUI fpsText;
    public Toggle showGroupsToggle;

    [Header("Global Defaults")]
    public int defaultResolution = 64;
    public float defaultWorldSize = 10f;
    public float defaultCubeSize = 1f;
    public float defaultGapX = 0f;
    public float defaultGapY = 0f;
    public bool default3DDepth = true;
    public int defaultDepthLayers = 6;

    [Header("Performance")]
    public int targetFPS = 60;

    private float deltaTime;
    private int currentIndex = 0;
    private const string ParentName = "AutoVoxelModel";
    private Color[] groupDebugColors;

    void Start() {
        Application.targetFrameRate = (Application.platform == RuntimePlatform.Android) ? 60 : targetFPS;
        QualitySettings.vSyncCount = 0;
        InitDebugColors();

        if (models.Count > 0 || fallbackImages.Count > 0) {
            LoadModel(0);
        }
    }

    void InitDebugColors() {
        groupDebugColors = new Color[256];
        groupDebugColors[0] = new Color(0, 0, 0, 0);
        for (int i = 1; i < 256; i++) {
            groupDebugColors[i] = Color.HSVToRGB((i * 0.137f) % 1.0f, 0.8f, 1.0f);
        }
    }

    void Update() {
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
        if (fpsText != null) fpsText.text = "FPS: " + Mathf.Ceil(1f / deltaTime);
    }

    public void Next() {
        int count = GetTotalCount();
        if (count == 0) return;
        currentIndex = (currentIndex + 1) % count;
        LoadModel(currentIndex);
    }

    public void Previous() {
        int count = GetTotalCount();
        if (count == 0) return;
        currentIndex = (currentIndex - 1 + count) % count;
        LoadModel(currentIndex);
    }

    private int GetTotalCount() => Mathf.Max(models.Count, fallbackImages.Count);

    void LoadModel(int index) {
        if (cubePrefab == null || index < 0) return;

        Texture2D tex = null;
        VoxelModelData data = null;

        if (models.Count > 0 && index < models.Count) {
            data = models[index];
            tex = data.sourceImage;
        } else if (fallbackImages.Count > 0 && index < fallbackImages.Count) {
            tex = fallbackImages[index];
        }

        if (tex == null && (data == null || !data.is3DModel)) return;

        if (indexText != null) indexText.text = (index + 1) + " / " + GetTotalCount();
        Generate(tex, data);
    }

    void Generate(Texture2D tex, VoxelModelData data = null) {
        // Cleanup - much faster than a loop
        GameObject old = GameObject.Find(ParentName);
        if (old != null) DestroyImmediate(old);
        
        if (BlockManager.Instance != null) BlockManager.Instance.ClearGrid();

        // 3D MODEL PATH
        if (data != null && data.is3DModel && data.voxels3D != null && data.voxels3D.Count > 0) {
            GenerateFrom3DData(data);
            return;
        }

        if (tex == null) return;

        // ... rest of existing Generate logic (2D)
        int res = (data != null) ? data.resolution : defaultResolution;
        float wSize = (data != null) ? data.worldSize : defaultWorldSize;
        float cScale = (data != null) ? data.cubeSize : defaultCubeSize;
        float gX = (data != null) ? data.gapX : defaultGapX;
        float gY = (data != null) ? data.gapY : defaultGapY;
        bool depth = (data != null) ? data.use3DDepth : default3DDepth;
        int layersMax = (data != null) ? data.depthLayers : defaultDepthLayers;

        float size = wSize / res;
        if (BlockManager.Instance != null) {
            BlockManager.Instance.resolution = res;
            BlockManager.Instance.worldSize = wSize;
            BlockManager.Instance.gapX = gX;
            BlockManager.Instance.gapY = gY;
        }

        Color[] pixels = GetPixelsFromTexture(tex, res, res);
        int[,] groups = new int[res, res];

        if (data != null && data.useManualGroups && data.savedGroups != null && data.savedGroups.Length == res * res) {
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                    groups[x, y] = data.savedGroups[y * res + x];
        } else {
            groups = AutoGroupSmart(pixels, res);
        }

        UpdatePreviewImage(tex, pixels, groups, res);

        Transform parent = new GameObject(ParentName).transform;
        MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        int baseColorId = Shader.PropertyToID("_BaseColor");
        int colorId = Shader.PropertyToID("_Color");

        for (int y = 0; y < res; y++) {
            for (int x = 0; x < res; x++) {
                Color c = pixels[y * res + x];
                if (c.a < 0.1f) continue;

                // Position Calculation
                Vector3 basePos = new Vector3((x - res * 0.5f) * (size + gX), (y - res * 0.5f) * (size + gY), 0);
                int lCount = depth ? Mathf.Max(1, Mathf.RoundToInt(c.grayscale * layersMax)) : 1;
                int gid = groups[x, y];

                mpb.SetColor(baseColorId, c);
                mpb.SetColor(colorId, c);

                // RUPALI WORKFLOW: Calculate Z-Offsets based on Shape Type
                float xOffset = (float)x / res * 2f - 1f; // Normalize X to -1 to 1
                float yOffset = (float)y / res * 2f - 1f; // Normalize Y to -1 to 1
                float zBase = 0f;

                if (data != null) {
                    switch (data.shapeType) {
                        case VoxelShapeType.Cylinder:
                            // Square root for circle arc (x^2 + z^2 = r^2)
                            float rSquared = data.radius * data.radius;
                            float xScaled = xOffset * data.radius;
                            zBase = -Mathf.Sqrt(Mathf.Max(0, rSquared - xScaled * xScaled));
                            break;

                        case VoxelShapeType.Sphere:
                            float distFromCenter = Mathf.Sqrt(xOffset * xOffset + yOffset * yOffset);
                            zBase = -Mathf.Sqrt(Mathf.Max(0, (data.radius * data.radius) - (distFromCenter * distFromCenter)));
                            break;

                        case VoxelShapeType.Curved:
                            // Sine wave curvature for organic feel
                            zBase = Mathf.Sin(xOffset * Mathf.PI * 0.5f) * data.curvatureStrength;
                            break;
                    }
                }

                for (int i = 0; i < lCount; i++) {
                    float zFinal = zBase + (i * size * ((data != null) ? data.depthScale : 1.0f));
                    GameObject go = Instantiate(cubePrefab, basePos + new Vector3(0, 0, zFinal), Quaternion.identity, parent);
                    go.transform.localScale = Vector3.one * size * cScale;
                    
                    Block b = go.GetComponent<Block>();
                    if (b != null) { 
                        b.blockColor = c; 
                        b.gridPos = new Vector3Int(x, y, i); 
                        b.groupId = gid; 
                        BlockManager.Instance.Register(b);
                    }
                    
                    Renderer r = go.GetComponentInChildren<Renderer>();
                    if (r != null) r.SetPropertyBlock(mpb);
                }
            }
        }
    }

    void UpdatePreviewImage(Texture2D original, Color[] pixels, int[,] groups, int res) {
        if (previewRaw == null) return;
        if (showGroupsToggle != null && showGroupsToggle.isOn) {
            Texture2D gp = new Texture2D(res, res);
            gp.filterMode = FilterMode.Point;
            Color[] p = new Color[res * res];
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                    p[y * res + x] = (groups[x, y] == 0) ? Color.clear : groupDebugColors[groups[x, y] % 256];
            gp.SetPixels(p); gp.Apply();
            previewRaw.texture = gp;
        } else {
            previewRaw.texture = original;
        }
    }

    // ─────────────────────────────────────────────
    //  3D GENERATION
    // ─────────────────────────────────────────────
    void GenerateFrom3DData(VoxelModelData data) {
        Transform parent = new GameObject(ParentName).transform;
        MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        int baseColorId = Shader.PropertyToID("_BaseColor");
        int colorId = Shader.PropertyToID("_Color");

        float size = data.worldSize / data.resolution;
        float cScale = data.cubeSize;

        foreach (var v in data.voxels3D) {
            Vector3 pos = new Vector3(
                (v.position.x - data.resolution * 0.5f) * (size + data.gapX),
                (v.position.y - data.resolution * 0.5f) * (size + data.gapY),
                v.position.z * size // Depth
            );

            mpb.SetColor(baseColorId, v.color);
            mpb.SetColor(colorId, v.color);

            GameObject go = Instantiate(cubePrefab, pos, Quaternion.identity, parent);
            go.transform.localScale = Vector3.one * size * cScale;

            Block b = go.GetComponent<Block>();
            if (b != null) {
                b.blockColor = v.color;
                b.gridPos = v.position;
                b.groupId = v.groupId;
                BlockManager.Instance.Register(b);
            }

            Renderer r = go.GetComponentInChildren<Renderer>();
            if (r != null) r.SetPropertyBlock(mpb);
        }
        
        if (previewRaw != null) previewRaw.texture = null; // Clear 2D preview
    }

    private int[,] AutoGroupSmart(Color[] pixels, int res) {
        int[,] groups = new int[res, res];
        bool[,] visited = new bool[res, res];
        int nextId = 1;
        for (int y = 0; y < res; y++) {
            for (int x = 0; x < res; x++) {
                if (pixels[y * res + x].a < 0.1f || visited[x, y]) continue;
                Queue<Vector2Int> q = new Queue<Vector2Int>();
                q.Enqueue(new Vector2Int(x, y));
                visited[x, y] = true;
                Color32 target = pixels[y * res + x];
                while (q.Count > 0) {
                    Vector2Int c = q.Dequeue();
                    groups[c.x, c.y] = nextId;
                    Vector2Int[] neighbors = { new Vector2Int(c.x+1,c.y), new Vector2Int(c.x-1,c.y), new Vector2Int(c.x,c.y+1), new Vector2Int(c.x,c.y-1) };
                    foreach (var n in neighbors) {
                        if (n.x >= 0 && n.x < res && n.y >= 0 && n.y < res && !visited[n.x, n.y]) {
                            Color32 nc = pixels[n.y * res + n.x];
                            if (nc.a > 0.1f && nc.r == target.r && nc.g == target.g && nc.b == target.b) {
                                visited[n.x, n.y] = true; q.Enqueue(n);
                            }
                        }
                    }
                }
                nextId++;
            }
        }
        return groups;
    }

    private Color[] GetPixelsFromTexture(Texture2D src, int w, int h) {
        RenderTexture rt = RenderTexture.GetTemporary(w, h);
        Graphics.Blit(src, rt);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D readable = new Texture2D(w, h);
        readable.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        readable.Apply();
        Color[] p = readable.GetPixels();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        DestroyImmediate(readable);
        return p;
    }
}
