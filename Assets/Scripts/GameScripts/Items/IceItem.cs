using GaussianSplatting.Runtime;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// An item frozen in ice. It starts melting once picked up and is lost (like a fallen item) if it melts away
// before it is sorted. Sorting it works as for any other item.
public class IceItem : MonoBehaviour
{
    public const float MeltSeconds = 6f;
    private static readonly Color IceTint = new Color(0.72f, 0.9f, 1f, 0.4f);

    private GaussianSplatRenderer splats;
    private XRGrabInteractable grab;
    private float melted = -1f;

    private void Awake()
    {
        splats = GetComponent<GaussianSplatRenderer>();
        grab = GetComponent<XRGrabInteractable>();
        if (splats != null) splats.tint = IceTint;
        if (grab != null) grab.selectEntered.AddListener(OnGrab);
    }

    private void OnDestroy()
    {
        if (grab != null) grab.selectEntered.RemoveListener(OnGrab);
    }

    private void OnGrab(SelectEnterEventArgs args)
    {
        if (melted < 0f) melted = 0f;
    }

    private void Update()
    {
        // already sorted, fallen or crumbling
        if (melted < 0f || gameObject.IsOutOfPlay()) return;

        melted += Time.deltaTime / MeltSeconds;
        if (splats != null) splats.melt = Mathf.Min(melted, 1f);
        if (melted < 1f) return;

        // nothing is left to hold
        if (grab != null) grab.enabled = false;
        gameObject.TakeOutOfPlay();
        GameService.Instance.PunishForObjectFallen();
        Destroy(gameObject);
    }
}
