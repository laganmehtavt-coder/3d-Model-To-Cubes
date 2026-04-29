using UnityEngine;
using System.Collections.Generic;

public enum VoxelShapeType { Flat, Cylinder, Sphere, Curved }

[System.Serializable]
public struct VoxelData3D {
    public Vector3Int position;
    public Color color;
    public int groupId;
}

[CreateAssetMenu(fileName = "NewVoxelData", menuName = "Voxel/Voxel Model Data")]
public class VoxelModelData : ScriptableObject {
    [Header("Source Image (2D Only)")]
    public Texture2D sourceImage;

    [Header("Voxel Mesh Settings")]
    public int resolution = 64;
    public float worldSize = 10f;
    [Range(0.1f, 2f)] public float cubeSize = 1.0f;
    
    [Header("Gaps")]
    public float gapX = 0f;
    public float gapY = 0f;

    [Header("Advanced 3D Logic (Rupali Workflow)")]
    public VoxelShapeType shapeType = VoxelShapeType.Flat;
    public float radius = 5f; // Used for cylinder/sphere
    public float depthScale = 1.0f; // Multiplier for depth layers
    public bool use3DDepth = true;
    [Range(1, 40)] public int depthLayers = 6;
    public float curvatureStrength = 1.0f; // Sine wave strength for 'Curved' type

    [Header("3D Model Data (Auto-Generated)")]
    public bool is3DModel = false;
    public List<VoxelData3D> voxels3D = new List<VoxelData3D>();

    [Header("Grouping")]
    public bool useManualGroups = false;
    public int currentEditingGroupId = 1;

    [Header("Color Overrides")]
    public float colorTolerance = 0.1f;
    public Color snapTargetColor = Color.black;
    public int targetColorCount = 5;
    public List<ColorMapping> colorMappings = new List<ColorMapping>();

    [HideInInspector] public int[] savedGroups; 
}

[System.Serializable]
public class ColorMapping {
    public Color originalColor;
    public Color overrideColor;
    public string colorName; // Optional: for display
}
