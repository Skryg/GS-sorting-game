using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
public class HealthManager : MonoBehaviour
{
    // Start is called before the first frame update
    public Image image;
    void Awake()
    {
        image = GetComponent<Image>();
    }

    public void UpdateHealth(float newHealth)
    {
        image.fillAmount = newHealth;
    }
}
