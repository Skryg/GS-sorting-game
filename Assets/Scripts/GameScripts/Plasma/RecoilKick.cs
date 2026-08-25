using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RecoilKick : MonoBehaviour
{
    [Header("Kick amounts")]
    [Tooltip("How far back the gun jumps (local meters)")]
    public float positionKick = 0.05f;
    [Tooltip("How much the gun tilts up (degrees)")]
    public float rotationKick = 8f;

    [Header("Recovery")]
    [Tooltip("Higher = snappier return")]
    public float returnSpeed = 12f;
    [Tooltip("Higher = punchier kick")]
    public float snappiness = 20f;

    private Vector3 basePos;
    private Quaternion baseRot;
    private Vector3 currentPosOffset;
    private Vector3 currentRotOffset;
    private Vector3 targetPosOffset;
    private Vector3 targetRotOffset;

    void Start()
    {
        basePos = transform.localPosition;
        baseRot = transform.localRotation;
    }

    void Update()
    {
        targetPosOffset = Vector3.Lerp(targetPosOffset, Vector3.zero, returnSpeed * Time.deltaTime);
        targetRotOffset = Vector3.Lerp(targetRotOffset, Vector3.zero, returnSpeed * Time.deltaTime);

        currentPosOffset = Vector3.Lerp(currentPosOffset, targetPosOffset, snappiness * Time.deltaTime);
        currentRotOffset = Vector3.Lerp(currentRotOffset, targetRotOffset, snappiness * Time.deltaTime);

        transform.localPosition = basePos + currentPosOffset;
        transform.localRotation = baseRot * Quaternion.Euler(currentRotOffset);
    }

    public void Kick()
    {
        targetPosOffset += new Vector3(0f, 0f, -positionKick);
        targetRotOffset += new Vector3(-rotationKick, 0f, 0f);
    }
}