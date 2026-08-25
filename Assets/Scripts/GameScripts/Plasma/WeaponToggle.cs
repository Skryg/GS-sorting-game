using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public class WeaponToggle : MonoBehaviour
{
    [Header("Weapon")]
    [Tooltip("The weapon object to show/hide. Make it a child of the hand.")]
    public GameObject weapon;

    [Header("Input")]
    [Tooltip("Which hand's A/X button toggles the weapon")]
    public Hand hand = Hand.Right;

    public enum Hand { Left, Right }

    private bool weaponOn;
    private bool wasPressed;

    void Start()
    {
        // start hidden
        if (weapon != null)
            weapon.SetActive(false);
    }

    void Update()
    {
        bool pressed = ReadButton();

        // trigger only on the moment of press, not while held
        if (pressed && !wasPressed)
            ToggleWeapon();

        wasPressed = pressed;
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
    }
}
