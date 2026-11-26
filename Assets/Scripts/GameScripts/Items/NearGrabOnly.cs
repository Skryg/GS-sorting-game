using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Belt items can only be picked up within reach of the hand and drop straight down when released.
[RequireComponent(typeof(XRGrabInteractable))]
public class NearGrabOnly : MonoBehaviour, IXRSelectFilter
{
    [Tooltip("Largest distance between the hand and the object's surface that still allows a grab (metres).")]
    public float maxGrabDistance = 1.2f;

    private XRGrabInteractable grab;

    public bool canProcess => isActiveAndEnabled;

    public static void Apply(GameObject target, float maxDistance)
    {
        if (target.GetComponent<XRGrabInteractable>() == null) return;
        var filter = target.GetComponent<NearGrabOnly>();
        if (filter == null) filter = target.AddComponent<NearGrabOnly>();
        filter.maxGrabDistance = maxDistance;
    }

    private void Awake()
    {
        grab = GetComponent<XRGrabInteractable>();
        grab.throwOnDetach = false;
    }

    private void OnEnable() => grab.selectFilters.Add(this);

    private void OnDisable() => grab.selectFilters.Remove(this);

    public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
    {
        Vector3 hand = interactor.transform.position;
        float limitSqr = maxGrabDistance * maxGrabDistance;
        foreach (var col in grab.colliders)
        {
            if (col == null || !col.enabled || col.isTrigger) continue;
            if ((col.ClosestPoint(hand) - hand).sqrMagnitude <= limitSqr) return true;
        }
        return false;
    }
}
