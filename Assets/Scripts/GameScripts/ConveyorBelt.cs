using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class ConveyorBelt : MonoBehaviour
{
    public float Speed { get; private set; } = 0.0f;
    public float beltMovingOffset = 0.45f;
    private Renderer objectRenderer;
    private HashSet<GameObject> collisions = new HashSet<GameObject>();

    // Start is called before the first frame update
    void Start()
    {
        objectRenderer = GetComponent<Renderer>();
    }

    // Update is called once per frame
    private void Update()
    {
        MoveBeltTexture();
        MoveCollidedObjects();
    }

    public void SetConveyorSpeed(float newSpeed)
    {
        StartCoroutine(ChangeConveyorSpeed(newSpeed, 1.0f)); 
    }

    private IEnumerator ChangeConveyorSpeed(float newSpeed, float duration = 1.0f)
    {
        float initialSpeed = Speed;
        float elapsedTime = 0f;
        while (elapsedTime < duration)
        {
            Speed = Mathf.Lerp(initialSpeed, newSpeed, elapsedTime / duration);
            elapsedTime += Time.deltaTime;
            yield return null;
        }
        Speed = newSpeed; 
    }

    private void MoveBeltTexture()
    {
        if (objectRenderer.material is not null)
        {
            objectRenderer.material.mainTextureOffset += beltMovingOffset * Speed * Time.deltaTime * Vector2.up;
        }
    }

    private void MoveCollidedObjects()
    {
        foreach (var gameObject in collisions)
        {
            var grabbed = gameObject.GetComponent<XRGrabInteractable>();
            if (grabbed != null)
            {
                if (!grabbed.isSelected)
                {
                    gameObject.transform.position -= Speed * Time.deltaTime * Vector3.forward;
                }
            }
        }
    }


    private void OnCollisionEnter(Collision collision)
    {   
        if (collision.gameObject.GetComponent<DragObject>() != null)
        {
            collisions.Add(collision.gameObject);
        }
    }

    private void OnCollisionExit(Collision collision)
    {
        collisions.Remove(collision.gameObject);
    }
}
