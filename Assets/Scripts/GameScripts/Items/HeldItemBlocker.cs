using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Transformers;

// Held items follow the hand without physics, so this stops them at the walls of the bins and at the conveyor belt.
// The item slides along a surface instead of passing through it.
[RequireComponent(typeof(Rigidbody))]
public class HeldItemBlocker : XRBaseGrabTransformer
{
    private const float Skin = 0.005f;
    private const float MinRadius = 0.01f;
    private const float MaxRadius = 0.05f;
    // a hand this far from a blocked item has gone around the obstacle
    private const float MaxLag = 0.4f;
    private const int MaxSlides = 3;
    private const int ObstacleLayers = 1 << 0;

    private static readonly RaycastHit[] hits = new RaycastHit[32];
    private static readonly Collider[] overlaps = new Collider[32];

    private Rigidbody body;
    private float radius;
    private Vector3 heldCenter;

    protected override RegistrationMode registrationMode => RegistrationMode.None;

    public static void Apply(GameObject target)
    {
        var grab = target.GetComponent<XRGrabInteractable>();
        if (grab == null) return;

        // XRI adds its own transformer only when there is no other, and it has to run first
        grab.AddSingleGrabTransformer(target.AddComponent<XRGeneralGrabTransformer>());
        grab.AddSingleGrabTransformer(target.AddComponent<HeldItemBlocker>());
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        float smallest = MaxRadius * 2f;
        foreach (var col in GetComponentsInChildren<Collider>())
        {
            Vector3 extents = col.bounds.extents;
            smallest = Mathf.Min(smallest, Mathf.Min(extents.x, Mathf.Min(extents.y, extents.z)));
        }
        radius = Mathf.Clamp(smallest * 0.5f, MinRadius, MaxRadius);
    }

    public override void OnGrab(XRGrabInteractable grabInteractable)
    {
        base.OnGrab(grabInteractable);
        heldCenter = transform.position + transform.rotation * body.centerOfMass;
    }

    public override void Process(XRGrabInteractable grabInteractable, XRInteractionUpdateOrder.UpdatePhase updatePhase, ref Pose targetPose, ref Vector3 localScale)
    {
        Vector3 wanted = targetPose.position + targetPose.rotation * body.centerOfMass;
        Vector3 allowed = Slide(heldCenter, wanted);
        if ((wanted - allowed).sqrMagnitude > MaxLag * MaxLag && !IsInsideObstacle(wanted))
            allowed = wanted;

        heldCenter = allowed;
        targetPose.position += allowed - wanted;
    }

    private static bool IsObstacle(Collider col)
    {
        return col.GetComponentInParent<Trash>() != null || col.GetComponentInParent<ConveyorBelt>() != null;
    }

    private Vector3 Slide(Vector3 from, Vector3 to)
    {
        for (int i = 0; i < MaxSlides; i++)
        {
            Vector3 move = to - from;
            float distance = move.magnitude;
            if (distance < 1e-5f) return from;

            Vector3 direction = move / distance;
            if (!FindObstacle(from, direction, distance + Skin, out var surface)) return to;

            from += direction * Mathf.Max(0f, surface.distance - Skin);
            to = from + Vector3.ProjectOnPlane(to - from, surface.normal);
        }
        return from;
    }

    private bool FindObstacle(Vector3 from, Vector3 direction, float distance, out RaycastHit surface)
    {
        surface = default;
        surface.distance = float.PositiveInfinity;
        int count = Physics.SphereCastNonAlloc(from, radius, direction, hits, distance, ObstacleLayers, QueryTriggerInteraction.Ignore);
        PickNearest(count, ref surface);
        // a sphere that already touches a surface does not report it, the ray still does
        count = Physics.RaycastNonAlloc(from, direction, hits, distance, ObstacleLayers, QueryTriggerInteraction.Ignore);
        PickNearest(count, ref surface);
        return !float.IsPositiveInfinity(surface.distance);
    }

    private static void PickNearest(int count, ref RaycastHit nearest)
    {
        for (int i = 0; i < count; i++)
        {
            if (hits[i].distance > 0f && hits[i].distance < nearest.distance && IsObstacle(hits[i].collider))
                nearest = hits[i];
        }
    }

    private bool IsInsideObstacle(Vector3 point)
    {
        int count = Physics.OverlapSphereNonAlloc(point, radius, overlaps, ObstacleLayers, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            if (IsObstacle(overlaps[i])) return true;
        }
        return false;
    }
}
