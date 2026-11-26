using System;
using UnityEngine;
using UnityEngine.EventSystems;

public class SliderRelease : MonoBehaviour, IPointerUpHandler
{
    public Action Released;

    public void OnPointerUp(PointerEventData eventData)
    {
        Released?.Invoke();
    }
}
