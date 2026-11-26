using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class WeaponToggle : MonoBehaviour
{
    [Header("Weapon")]
    [Tooltip("The weapon object to show/hide. Make it a child of the hand.")]
    public GameObject weapon;

    [Header("Controller Visual")]
    [Tooltip("Optional: the controller model to hide when the weapon is drawn.")]
    public GameObject controllerModel;

    [Header("Interactors")]
    [Tooltip("Objects (e.g. Near-Far / Poke Interactor) disabled while the weapon is drawn. If empty, near-far, poke and direct interactors under the controller are found automatically.")]
    public GameObject[] disableWhileArmed;

    [Header("Input")]
    [Tooltip("Which hand's A/X button toggles the weapon")]
    public Hand hand = Hand.Right;

    public enum Hand { Left, Right }

    private bool weaponOn;
    private bool wasPressed;
    private bool interactorsSuppressed;

    void Start()
    {
        // start hidden
        if (weapon != null)
            weapon.SetActive(false);

        Transform controllerRoot = controllerModel != null ? controllerModel.transform.parent : transform;

        if (controllerModel == null)
            controllerModel = FindControllerVisual(controllerRoot);

        if (disableWhileArmed == null || disableWhileArmed.Length == 0)
            disableWhileArmed = FindInteractors(controllerRoot);
    }

    void Update()
    {
        bool pressed = ReadButton();
        bool paused = GameService.Instance != null && GameService.Instance.IsPaused;

        // trigger only on the moment of press, not while held
        if (pressed && !wasPressed && !paused)
            ToggleWeapon();

        wasPressed = pressed;
    }

    void LateUpdate()
    {
        // interactors come back while paused so the pause menu stays usable
        bool paused = GameService.Instance != null && GameService.Instance.IsPaused;
        bool suppress = weaponOn && !paused;

        // ControllerInputActionManager re-activates the near-far interactor after teleport, so enforce every frame
        if (suppress || interactorsSuppressed != suppress)
            SetInteractorsActive(!suppress);

        interactorsSuppressed = suppress;
    }

    bool ReadButton()
    {
        // primaryButton = A (right hand) / X (left hand)
        var device = (hand == Hand.Right)
            ? UnityEngine.InputSystem.XR.XRController.rightHand
            : UnityEngine.InputSystem.XR.XRController.leftHand;

        if (device == null) return false;

        var button = device.TryGetChildControl<ButtonControl>("primaryButton");
        return button != null && button.isPressed;
    }

    void ToggleWeapon()
    {
        weaponOn = !weaponOn;
        if (weapon != null)
            weapon.SetActive(weaponOn);
        if (controllerModel != null)
            controllerModel.SetActive(!weaponOn);
    }

    void SetInteractorsActive(bool active)
    {
        if (disableWhileArmed == null) return;
        foreach (var go in disableWhileArmed)
        {
            if (go != null && go.activeSelf != active)
                go.SetActive(active);
        }
    }

    GameObject FindControllerVisual(Transform root)
    {
        if (root == null) return null;

        // XRI Starter Assets name it "Right/Left Controller Visual"
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t != root && t.name.Contains("Controller Visual"))
                return t.gameObject;
        }
        return null;
    }

    GameObject[] FindInteractors(Transform root)
    {
        if (root == null) return new GameObject[0];

        // teleport uses XRRayInteractor, so ray interactors are intentionally left out
        var result = new List<GameObject>();
        foreach (var i in root.GetComponentsInChildren<NearFarInteractor>(true)) result.Add(i.gameObject);
        foreach (var i in root.GetComponentsInChildren<XRPokeInteractor>(true)) result.Add(i.gameObject);
        foreach (var i in root.GetComponentsInChildren<XRDirectInteractor>(true)) result.Add(i.gameObject);
        return result.ToArray();
    }
}
