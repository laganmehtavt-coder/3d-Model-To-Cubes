using UnityEngine;

public class ModelRotator : MonoBehaviour {
    [Header("Settings")]
    public float sensitivity = 0.4f;
    public float smoothness = 0.1f;
    
    private float _rotationVelocity;
    private Vector3 _lastMousePosition;

    void Update() {
        // Handle Mouse Input
        if (Input.GetMouseButtonDown(0)) {
            _lastMousePosition = Input.mousePosition;
        }

        if (Input.GetMouseButton(0)) {
            Vector3 delta = Input.mousePosition - _lastMousePosition;
            // Calculate velocity based on mouse movement
            _rotationVelocity = delta.x * sensitivity;
            _lastMousePosition = Input.mousePosition;
        }

        // Apply rotation with interpolation for smoothness
        // This ensures the rotation continues slightly and slows down naturally
        transform.Rotate(Vector3.up, -_rotationVelocity, Space.World);

        // Damping: Gradually reduce velocity to 0
        _rotationVelocity = Mathf.Lerp(_rotationVelocity, 0, smoothness);
    }
}
