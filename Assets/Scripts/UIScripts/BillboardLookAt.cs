using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BillboardLookAt : MonoBehaviour
{
    private Camera cam;

    void Start()
    {
        cam = Camera.main;
    }

    void LateUpdate()
    {
        if (cam == null) return;

        transform.forward = cam.transform.forward;
    }
}
