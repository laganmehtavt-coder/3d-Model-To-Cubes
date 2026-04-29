using UnityEngine;

[RequireComponent(typeof(RectTransform))]
public class SafeArea : MonoBehaviour {
    private RectTransform rectTransform;
    private Rect lastSafeArea = new Rect(0, 0, 0, 0);
    private Vector2 lastScreenSize = new Vector2(0, 0);
    private ScreenOrientation lastOrientation = ScreenOrientation.AutoRotation;

    void Awake() {
        rectTransform = GetComponent<RectTransform>();
        Refresh();
    }

    void Update() {
        // Refresh only if something changed to save performance
        if (lastSafeArea != Screen.safeArea || 
            lastScreenSize.x != Screen.width || 
            lastScreenSize.y != Screen.height || 
            lastOrientation != Screen.orientation) 
        {
            Refresh();
        }
    }

    void Refresh() {
        Rect safeArea = Screen.safeArea;

        if (safeArea != lastSafeArea) {
            ApplySafeArea(safeArea);
        }
    }

    void ApplySafeArea(Rect r) {
        lastSafeArea = r;
        lastScreenSize.x = Screen.width;
        lastScreenSize.y = Screen.height;
        lastOrientation = Screen.orientation;

        // Convert safe area rectangle from pixels to normalized anchor coordinates (0.0 to 1.0)
        Vector2 anchorMin = r.position;
        Vector2 anchorMax = r.position + r.size;

        anchorMin.x /= Screen.width;
        anchorMin.y /= Screen.height;
        anchorMax.x /= Screen.width;
        anchorMax.y /= Screen.height;

        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;

        Debug.Log($"Safe Area Applied: {r.x}, {r.y}, {r.width}, {r.height}");
    }
}
