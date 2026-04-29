using UnityEngine;
using System.Collections;

public class Block : MonoBehaviour {
    [Header("Block Data")]
    public Color blockColor;
    public Vector3Int gridPos;
    public int groupId = 1;

    [Header("State")]
    public bool isFalling = false;
    private bool hitGround = false;
    
    private Rigidbody rb;
    private BoxCollider col;
    private Renderer rend;

    void Awake() {
        rend = GetComponent<Renderer>();
        if (rend != null) {
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", blockColor);
            mpb.SetColor("_Color", blockColor);
            rend.SetPropertyBlock(mpb);
        }

        col = gameObject.GetComponent<BoxCollider>();
        if (col == null) col = gameObject.AddComponent<BoxCollider>();
        col.size = new Vector3(0.95f, 0.95f, 0.95f);

        rb = gameObject.GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true; 
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
    }

    void Start() {
        if (BlockManager.Instance != null) 
            BlockManager.Instance.Register(this);
    }

    public void ActivatePhysics(Vector3 initialVelocity, Vector3 extraImpulse = default) {
        if (isFalling) return;
        isFalling = true;

        rb.isKinematic = false;
        rb.velocity = initialVelocity;

        // Add a "Natural Pop"
        Vector3 pop = new Vector3(Random.Range(-0.2f, 0.2f), Random.Range(0f, 0.5f), Random.Range(-0.2f, 0.2f));
        rb.AddForce(pop + extraImpulse, ForceMode.Impulse);
        rb.AddTorque(Random.insideUnitSphere * 15f, ForceMode.Impulse);

        if (BlockManager.Instance != null) {
            BlockManager.Instance.NotifySupportRemoved(gridPos);
            BlockManager.Instance.Unregister(this);
        }

        StartCoroutine(AutoCleanupFallback());
    }

    private void OnCollisionEnter(Collision collision) {
        if (!isFalling || hitGround) return;

        if (collision.gameObject.CompareTag("Ground") || collision.gameObject.name.ToLower().Contains("ground")) {
            hitGround = true;
            if (BlockManager.Instance != null)
                BlockManager.Instance.PlaySound(BlockManager.Instance.hitGroundSound, 0.15f);
            
            StartCoroutine(RemovePhysicsAfterDelay(0.4f));
        }
    }

    IEnumerator RemovePhysicsAfterDelay(float delay) {
        yield return new WaitForSeconds(delay);
        if (rb != null) {
            rb.isKinematic = true;
            Destroy(rb);
        }
        if (col != null) Destroy(col);
        isFalling = false;
    }

    IEnumerator AutoCleanupFallback() {
        yield return new WaitForSeconds(10.0f);
        if (isFalling && !hitGround) StartCoroutine(RemovePhysicsAfterDelay(0.1f));
    }
}
