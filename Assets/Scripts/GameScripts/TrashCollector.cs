using GaussianSplatting.Runtime;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TrashCollector : MonoBehaviour
{
    // Start is called before the first frame update
    private Trash _trash;
    private const float VanishDelay = 1.0f;

    void Start()
    {
        _trash = gameObject.GetComponentInParent<Trash>();
    }

    private bool CheckLayerInactive(GameObject obj)
    {
        return obj.layer == LayerMask.NameToLayer("DraggableObjectInactive");
    }

    private void OnTriggerEnter(Collider other)
    {
        if (CheckLayerInactive(other.gameObject))
        {
            return;
        }
        
        if (other.TryGetComponent<ObjectIdentifier>(out var identifier))
        {
            InnerObjectVanish(other.gameObject);
            _trash.Collect(identifier);
        }
    }

    private void InnerObjectVanish(GameObject obj)
    {
        

        obj.layer = LayerMask.NameToLayer("DraggableObjectInactive");
        this.FadeOutAndDestroyGaussianObject(obj, VanishDelay);
    }

}
