using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ObjectFallen : MonoBehaviour
{

    private const float VanishDelay = 1.0f;
    // Start is called before the first frame update

    private GameService gameService;

    private void Start()
    {
        gameService = FindObjectOfType<GameService>();
    }


    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.layer == LayerMask.NameToLayer("DraggableObjectInactive"))
        {
            return;
        }

        if (collision.gameObject.TryGetComponent<ObjectIdentifier>(out var objectIdentifier))
        {
            gameService.PunishForObjectFallen();
            collision.gameObject.layer = LayerMask.NameToLayer("DraggableObjectInactive");
            this.FadeOutAndDestroyGaussianObject(collision.gameObject, VanishDelay);
        }
    }
}
