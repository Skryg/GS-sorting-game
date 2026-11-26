using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// A fake item: looks like any other one, but crumbles shortly after it is picked up.
// It gives no points and no penalty, wherever it ends up; shooting it gives points (see ShootableItem).
public class MockItem : MonoBehaviour
{
    private const float MinHoldTime = 0.3f;
    private const float MaxHoldTime = 0.8f;
    private const float HapticAmplitude = 0.5f;
    private const float HapticDuration = 0.25f;

    private XRGrabInteractable grab;

    private void Awake()
    {
        grab = GetComponent<XRGrabInteractable>();
        if (grab != null) grab.selectEntered.AddListener(OnGrab);
    }

    private void OnDestroy()
    {
        if (grab != null) grab.selectEntered.RemoveListener(OnGrab);
    }

    private void OnGrab(SelectEnterEventArgs args)
    {
        StartCoroutine(CrumbleInHand(args.interactorObject as XRBaseInputInteractor));
    }

    private IEnumerator CrumbleInHand(XRBaseInputInteractor hand)
    {
        yield return new WaitForSeconds(Random.Range(MinHoldTime, MaxHoldTime));
        if (ItemCrumble.IsCrumbling(gameObject)) yield break;

        if (hand != null && grab.isSelected) hand.SendHapticImpulse(HapticAmplitude, HapticDuration);
        ItemCrumble.Start(gameObject);
    }
}
