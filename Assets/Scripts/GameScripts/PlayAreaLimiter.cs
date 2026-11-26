using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

// Keeps the player's head inside a box by shifting the XR Origin back whenever the camera leaves it.
// Works for any head movement: simulator WASD, physical walking and locomotion providers.
public class PlayAreaLimiter : MonoBehaviour
{
    [Tooltip("Size of the allowed area in this object's local space. Only X and Z are enforced.")]
    public Vector3 size = new Vector3(2f, 3f, 1.5f);

    [Tooltip("Root of the XR rig. If empty, the object tagged Player is used.")]
    public Transform xrOrigin;

    [Tooltip("Player's head. If empty, Camera.main is used.")]
    public Transform head;

    [Tooltip("Keeps the head at its starting height while the XR Device Simulator runs (blocks its up/down keys). A real headset is never locked, so the player can crouch.")]
    public bool lockHeightInEditor = true;

    private float? lockedHeight;
    private float originStartY;

    void Start()
    {
        if (xrOrigin == null)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) xrOrigin = player.transform;
        }
        if (xrOrigin != null) originStartY = xrOrigin.position.y;
        if (head == null && Camera.main != null)
            head = Camera.main.transform;

        if (xrOrigin == null || head == null)
            Debug.LogWarning("PlayAreaLimiter: XR Origin or head not found, the play area is not enforced.", this);
    }

    void LateUpdate()
    {
        if (xrOrigin == null || head == null) return;

        Vector3 local = transform.InverseTransformPoint(head.position);
        Vector3 half = size * 0.5f;
        Vector3 clamped = new Vector3(Mathf.Clamp(local.x, -half.x, half.x), local.y, Mathf.Clamp(local.z, -half.z, half.z));

        bool lockHeight = lockHeightInEditor && XRDeviceSimulator.instance != null;
        if (!lockHeight)
        {
            if (lockedHeight.HasValue)
            {
                // the simulator was removed (headset started), undo the height it forced
                lockedHeight = null;
                xrOrigin.position = new Vector3(xrOrigin.position.x, originStartY, xrOrigin.position.z);
            }
        }
        else
        {
            // captured on the first frame, after the simulator has placed the head
            if (!lockedHeight.HasValue) lockedHeight = local.y;
            clamped.y = lockedHeight.Value;
        }

        if (clamped == local) return;

        Vector3 correction = transform.TransformPoint(clamped) - head.position;
        if (!lockHeight) correction.y = 0f;
        xrOrigin.position += correction;
    }

    void OnDrawGizmos()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.15f);
        Gizmos.DrawCube(Vector3.zero, size);
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.9f);
        Gizmos.DrawWireCube(Vector3.zero, size);
    }
}
