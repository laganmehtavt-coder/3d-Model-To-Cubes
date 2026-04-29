using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class BlockManager : MonoBehaviour {
    public static BlockManager Instance;

    [Header("Destruction Settings")]
    public float rippleSpeed = 0.015f;
    public float fallVelocity = 3f;
    public int vipAnchorGroupId = 1;

    [Header("Grid Data (Auto-Filled)")]
    public int resolution = 64;
    public float worldSize = 10f;
    public float gapX = 0.0f;
    public float gapY = 0.0f;

    [Header("Audio")]
    public AudioClip dropSound;
    public AudioClip hitGroundSound;
    private AudioSource audioSource;

    private Dictionary<Vector3Int, Block> grid = new Dictionary<Vector3Int, Block>();
    private Dictionary<int, List<Block>> groupBlocksMap = new Dictionary<int, List<Block>>();
    private Dictionary<int, HashSet<int>> groupAdjacency = new Dictionary<int, HashSet<int>>();
    private HashSet<int> fallingGroups = new HashSet<int>();

    void Awake() {
        if (Instance == null) Instance = this;
        audioSource = gameObject.AddComponent<AudioSource>();
    }

    void Update() {
        if (Input.GetMouseButtonDown(0)) HandleInputClick();
    }

    private void HandleInputClick() {
        if (Camera.main == null) return;
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, 100f);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits) {
            Block b = hit.collider.GetComponentInParent<Block>();
            if (b != null && !fallingGroups.Contains(b.groupId)) {
                TriggerGroupDrop(b.groupId, hit.point, true);
                return; 
            }
        }
    }

    public void TriggerGroupDrop(int gid, Vector3 originPoint, bool isManual = false) {
        if (fallingGroups.Contains(gid)) return;
        
        float volume = isManual ? 0.75f : 0.45f;
        PlaySound(dropSound, volume);
        
        StartCoroutine(DropSequence(gid, originPoint, isManual));
    }

    IEnumerator DropSequence(int gid, Vector3 originPoint, bool isManual = false) {
        if (fallingGroups.Contains(gid)) yield break;
        fallingGroups.Add(gid);

        float currentRipple = isManual ? rippleSpeed * 0.4f : rippleSpeed; 
        float currentFall = isManual ? fallVelocity * 1.1f : fallVelocity;
        Vector3 manualImpulse = isManual ? new Vector3(0, 0.5f, -1.5f) : Vector3.zero;

        if (groupBlocksMap.TryGetValue(gid, out List<Block> blocks)) {
            var sorted = blocks.Where(b => b != null).OrderBy(b => Vector3.Distance(b.transform.position, originPoint)).ToList();
            
            for (int i = 0; i < sorted.Count; i++) {
                if (sorted[i] != null) {
                    Vector3 pos = sorted[i].transform.position;
                    pos.z = isManual ? -1.0f : 0.0f; 
                    sorted[i].transform.position = pos;
                    
                    sorted[i].ActivatePhysics(Vector3.down * currentFall, manualImpulse);
                }
                
                if (i % 6 == 0) yield return new WaitForSeconds(currentRipple);
            }
        }

        if (gid == vipAnchorGroupId) {
            yield return new WaitForSeconds(0.1f);
            List<int> others = groupBlocksMap.Keys.Where(id => !fallingGroups.Contains(id)).ToList();
            foreach (int otherId in others) {
                if (groupBlocksMap[otherId].Count > 0)
                    StartCoroutine(DropSequence(otherId, groupBlocksMap[otherId][0].transform.position, false));
            }
        } else {
            yield return new WaitForSeconds(0.15f);
            CheckStability();
        }
    }

    public void NotifySupportRemoved(Vector3Int pos) {
        Vector3Int[] neighbors = { 
            Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right, 
            new Vector3Int(0,0,1), new Vector3Int(0,0,-1) 
        };

        foreach (var offset in neighbors) {
            Vector3Int nPos = pos + offset;
            if (grid.TryGetValue(nPos, out Block b)) {
                CheckStability();
            }
        }
    }

    public void CheckStability() {
        HashSet<int> supportedGroups = new HashSet<int>();
        Queue<int> q = new Queue<int>();

        foreach (var gid in groupBlocksMap.Keys) {
            if (fallingGroups.Contains(gid)) continue;
            bool touchesGround = groupBlocksMap[gid].Any(b => b.gridPos.y == 0);
            bool isVipAnchor = (gid == vipAnchorGroupId && !fallingGroups.Contains(gid));
            if (touchesGround || isVipAnchor) {
                supportedGroups.Add(gid);
                q.Enqueue(gid);
            }
        }

        while (q.Count > 0) {
            int curr = q.Dequeue();
            if (groupAdjacency.TryGetValue(curr, out var neighbors)) {
                foreach (int n in neighbors) {
                    if (!supportedGroups.Contains(n) && !fallingGroups.Contains(n) && groupBlocksMap.ContainsKey(n)) {
                        supportedGroups.Add(n);
                        q.Enqueue(n);
                    }
                }
            }
        }

        List<int> toDrop = groupBlocksMap.Keys.Where(id => !supportedGroups.Contains(id) && !fallingGroups.Contains(id)).ToList();
        foreach (int gid in toDrop) {
            if (groupBlocksMap[gid].Count > 0)
                StartCoroutine(DropSequence(gid, groupBlocksMap[gid][0].transform.position));
        }
    }

    public void Register(Block b) {
        grid[b.gridPos] = b;
        if (!groupBlocksMap.TryGetValue(b.groupId, out var list)) {
            list = new List<Block>();
            groupBlocksMap[b.groupId] = list;
        }
        list.Add(b);

        Vector3Int[] offsets = { Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right, new Vector3Int(0,0,1), new Vector3Int(0,0,-1) };
        foreach (var off in offsets) {
            if (grid.TryGetValue(b.gridPos + off, out Block other)) {
                if (other.groupId != b.groupId) {
                    if (!groupAdjacency.ContainsKey(b.groupId)) groupAdjacency[b.groupId] = new HashSet<int>();
                    if (!groupAdjacency.ContainsKey(other.groupId)) groupAdjacency[other.groupId] = new HashSet<int>();
                    groupAdjacency[b.groupId].Add(other.groupId);
                    groupAdjacency[other.groupId].Add(b.groupId);
                }
            }
        }
    }

    public void Unregister(Block b) {
        grid.Remove(b.gridPos);
        if (groupBlocksMap.TryGetValue(b.groupId, out var list)) {
            list.Remove(b);
        }
    }

    public void ClearGrid() { 
        grid.Clear(); groupBlocksMap.Clear(); groupAdjacency.Clear(); fallingGroups.Clear(); 
    }

    public void PlaySound(AudioClip clip, float vol) {
        if (clip != null && audioSource != null) audioSource.PlayOneShot(clip, vol);
    }
}
