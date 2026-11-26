using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Toggle))]
public class ToggleClickSound : MonoBehaviour, IPointerClickHandler
{
    private Toggle toggle;

    private void Awake()
    {
        toggle = GetComponent<Toggle>();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left && toggle.IsInteractable())
        {
            AudioManager.Play2D(Sfx.Click);
        }
    }
}
