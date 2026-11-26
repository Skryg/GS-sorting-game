using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class BoardManager : MonoBehaviour
{
    // Start is called before the first frame update
    public GameObject Red;
    public GameObject Green;
    public GameObject Yellow;
    public GameObject Blue;

    void Start()
    {
        if (Red == null || Green == null || Yellow == null || Blue == null)
        {
            Debug.LogError("One or more color GameObjects are not assigned in the inspector.");
        }
    }

    public void AssignSpriteToColor(Sprite sprite, BinColor color)
    {
        switch (color)
        {
            case BinColor.Red:
                Red.GetComponent<Image>().sprite = sprite;
                break;
            case BinColor.Green:
                Green.GetComponent<Image>().sprite = sprite;
                break;
            case BinColor.Yellow:
                Yellow.GetComponent<Image>().sprite = sprite;
                break;
            case BinColor.Blue:
                Blue.GetComponent<Image>().sprite = sprite;
                break;
            default:
                Debug.LogError("Invalid color specified.");
                break;
        }
    }

}
