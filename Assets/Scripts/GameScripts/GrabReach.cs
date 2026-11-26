using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

// Shortens the hand rays to the grab reach during play, so the visible laser shows how far items can be picked up.
// The rig retracts idle lines to a short stub; during play they always show the full reach instead.
// Menus (pause, game over) get the full length back so their buttons stay reachable.
public class GrabReach : MonoBehaviour
{
    public float reach = 1f;

    private readonly Dictionary<CurveInteractionCaster, float> originalDistances = new();
    private readonly Dictionary<CurveVisualController, LineDynamicsMode> originalModes = new();
    private GameService gameService;

    private void Start()
    {
        foreach (var interactor in FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (interactor.farInteractionCaster is CurveInteractionCaster caster && !originalDistances.ContainsKey(caster))
                originalDistances[caster] = caster.castDistance;
        }

        foreach (var visual in FindObjectsByType<CurveVisualController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            originalModes[visual] = visual.lineDynamicsMode;

        gameService = GameService.Instance;
        if (gameService != null)
        {
            gameService.PauseChanged += OnPauseChanged;
            gameService.GameOver += Restore;
        }
        Limit();
    }

    private void OnDestroy()
    {
        if (gameService != null)
        {
            gameService.PauseChanged -= OnPauseChanged;
            gameService.GameOver -= Restore;
        }
        Restore();
    }

    private void OnPauseChanged(bool paused)
    {
        if (paused) Restore();
        else Limit();
    }

    private void Limit()
    {
        foreach (var caster in originalDistances.Keys)
            if (caster != null) caster.castDistance = reach;
        foreach (var visual in originalModes.Keys)
            if (visual != null) visual.lineDynamicsMode = LineDynamicsMode.Traditional;
    }

    private void Restore()
    {
        foreach (var pair in originalDistances)
            if (pair.Key != null) pair.Key.castDistance = pair.Value;
        foreach (var pair in originalModes)
            if (pair.Key != null) pair.Key.lineDynamicsMode = pair.Value;
    }
}
