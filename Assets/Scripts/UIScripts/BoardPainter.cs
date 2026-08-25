using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BoardPainter : MonoBehaviour
{
    public ScoreManager scoreManager;
    public HealthManager healthManager;

    void Start()
    {
        scoreManager = GetComponentInChildren<ScoreManager>();
        healthManager = GetComponentInChildren<HealthManager>();
    }

    public void HealthUpdate(float newHealth)
    {
        healthManager.UpdateHealth(newHealth);
    }

    public void UpdateScore(int newScore)
    {
        scoreManager.UpdatePoints(newScore);
    }
}
