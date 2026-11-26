using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using GaussianSplatting.Runtime;
using GaussianSplatting.Runtime.GaMeS;
using Unity.Mathematics;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Bends a GaMeS splat while it is held: parts far from the grab point lag behind hand motion and sag under gravity.
[RequireComponent(typeof(GSVerse))]
public class SplatGrabBend : MonoBehaviour
{
    [SerializeField] private GSVerse _gsVerse;
    [SerializeField] private XRGrabInteractable _grab;

    [Header("Spring")]
    public float stiffness = 60f;
    public float damping = 8f;

    [Header("Bend")]
    [Tooltip("How strongly hand acceleration bends the object (world units per m/s^2).")]
    public float inertia = 0.01f;
    [Tooltip("How far the free end sags under gravity while held (world units).")]
    public float gravitySag = 0.5f;
    [Tooltip("Upper limit of the free end displacement (world units).")]
    public float maxBend = 0.15f;
    [Tooltip("Higher = only the far end bends, the part near the hand stays rigid.")]
    public float falloffExponent = 2f;

    [SerializeField] private float settleEpsilon = 1e-4f;

    [Tooltip("Re-cooks the convex MeshCollider from the bent mesh.")]
    public bool updateCollider = true;
    [Tooltip("How far the free end has to move before the collider is re-cooked (world units). 0 = on every change.")]
    public float colliderStep = 0.02f;

    // smaller changes of the free end are not worth rebuilding the splats (world units)
    private const float RebuildStep = 0.0005f;

    private NativeArray<float> _weights;
    private bool _held;
    private Transform _grabAttach;
    private Vector3 _bend;
    private Vector3 _bendVelocity;
    private Vector3 _prevPos;
    private Vector3 _prevVel;
    private bool _wasBent;
    private bool _needsWeights;
    private bool _weightsChanged;
    private Vector3 _appliedBend;
    private Vector3 _colliderBend;

    // GSVerse throws during Awake when its data is missing, so the inspector checks it up front.
    public static bool CanBend(GameObject target, out string reason)
    {
        var splatRenderer = target.GetComponent<GaussianSplatRenderer>();
        if (splatRenderer == null || !(splatRenderer.asset is GaussianGaMeSSplatAsset games))
        {
            reason = "Needs a GaussianSplatRenderer with a GaMeS splat asset.";
            return false;
        }
        if (!GaMeSAssetRebuilder.CanRebuild(games, out reason))
            return false;
        if (Resources.Load<GameObject>(games.objPath) == null)
        {
            reason = $"Mesh '{games.objPath}' not found in Resources.";
            return false;
        }
        reason = null;
        return true;
    }

    void Awake()
    {
        if (_gsVerse == null) _gsVerse = GetComponent<GSVerse>();
        if (_grab == null) _grab = GetComponent<XRGrabInteractable>();
    }

    void OnEnable()
    {
        if (_grab == null) return;
        _grab.selectEntered.AddListener(OnGrab);
        _grab.selectExited.AddListener(OnRelease);
    }

    void OnDisable()
    {
        if (_grab == null) return;
        _grab.selectEntered.RemoveListener(OnGrab);
        _grab.selectExited.RemoveListener(OnRelease);
    }

    void OnDestroy()
    {
        if (_weights.IsCreated) _weights.Dispose();
    }

    void OnGrab(SelectEnterEventArgs args)
    {
        _held = true;
        _grabAttach = args.interactorObject.GetAttachTransform(_grab);
        _prevPos = transform.position;
        _prevVel = Vector3.zero;
        // the object snaps to the hand after this event, so weights are computed next frame
        _needsWeights = true;
    }

    void OnRelease(SelectExitEventArgs args)
    {
        _held = false;
        _grabAttach = null;
    }

    void ComputeWeights(Vector3 grabWorld)
    {
        var original = _gsVerse.originalVertices;
        if (!original.IsCreated) return;

        if (!_weights.IsCreated || _weights.Length != original.Length)
        {
            if (_weights.IsCreated) _weights.Dispose();
            _weights = new NativeArray<float>(original.Length, Allocator.Persistent);
        }

        new ComputeWeightsJob
        {
            original = original,
            localToWorld = transform.localToWorldMatrix,
            grabWorld = grabWorld,
            falloffExponent = falloffExponent,
            weights = _weights
        }.Run();
    }

    [BurstCompile]
    struct ComputeWeightsJob : IJob
    {
        [ReadOnly] public NativeArray<float3> original;
        public float4x4 localToWorld;
        public float3 grabWorld;
        public float falloffExponent;
        public NativeArray<float> weights;

        public void Execute()
        {
            // distances in world units so non-uniform scale doesn't skew the falloff
            float maxDist = 1e-6f;
            for (int i = 0; i < original.Length; i++)
            {
                float d = math.distance(math.transform(localToWorld, original[i]), grabWorld);
                weights[i] = d;
                maxDist = math.max(maxDist, d);
            }

            for (int i = 0; i < weights.Length; i++)
                weights[i] = math.pow(weights[i] / maxDist, falloffExponent);
        }
    }

    [BurstCompile]
    struct ApplyBendJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> original;
        [ReadOnly] public NativeArray<float> weights;
        public float3 bendLocal;
        public NativeArray<float3> displaced;

        public void Execute(int index)
        {
            displaced[index] = original[index] + bendLocal * weights[index];
        }
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || _gsVerse == null) return;

        if (_needsWeights && _held)
        {
            ComputeWeights(_grabAttach != null ? _grabAttach.position : transform.position);
            _needsWeights = false;
            _weightsChanged = true;
            _prevPos = transform.position;
            _prevVel = Vector3.zero;
        }
        if (!_weights.IsCreated) return;

        Vector3 pos = transform.position;
        Vector3 vel = (pos - _prevPos) / dt;
        Vector3 accel = _held ? (vel - _prevVel) / dt : Vector3.zero;
        _prevPos = pos;
        _prevVel = vel;

        // the free end is pulled opposite to acceleration and down by gravity, a spring pulls it back
        Vector3 target = -accel * inertia + (_held ? Vector3.down * gravitySag : Vector3.zero);
        Vector3 force = (target - _bend) * stiffness - _bendVelocity * damping;
        _bendVelocity += force * dt;
        _bend = Vector3.ClampMagnitude(_bend + _bendVelocity * dt, maxBend);

        bool bent = _bend.sqrMagnitude > settleEpsilon * settleEpsilon || _bendVelocity.sqrMagnitude > settleEpsilon;
        if (!bent && !_wasBent) return;
        if (!bent) _bend = _bendVelocity = Vector3.zero;

        // local, because turning a bent object changes its shape as well
        Vector3 bendLocal = transform.InverseTransformVector(_bend);
        bool settled = !bent && _wasBent;
        if (settled || _weightsChanged || BendDistance(bendLocal, _appliedBend) > RebuildStep)
        {
            bool cook = updateCollider && (settled || BendDistance(bendLocal, _colliderBend) > colliderStep);
            if (cook) _colliderBend = bendLocal;
            // keep a collider update that a still pending rebuild has not picked up yet
            _gsVerse.needsColliderUpdate = cook || (_gsVerse.needsRebuild && _gsVerse.needsColliderUpdate);
            _appliedBend = bendLocal;
            _weightsChanged = false;
            ApplyBend(bendLocal);
        }
        _wasBent = bent;
    }

    float BendDistance(Vector3 a, Vector3 b)
    {
        return transform.TransformVector(a - b).magnitude;
    }

    void ApplyBend(float3 bendLocal)
    {
        var displaced = _gsVerse.displacedVertices;
        var original = _gsVerse.originalVertices;
        if (!displaced.IsCreated || displaced.Length != _weights.Length) return;

        new ApplyBendJob
        {
            original = original,
            weights = _weights,
            bendLocal = bendLocal,
            displaced = displaced
        }.Schedule(displaced.Length, 2048).Complete();

        _gsVerse.needsRebuild = true;
    }
}

#if UNITY_EDITOR
[UnityEditor.CustomEditor(typeof(SplatGrabBend))]
class SplatGrabBendEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        if (!SplatGrabBend.CanBend(((SplatGrabBend)target).gameObject, out var reason))
            UnityEditor.EditorGUILayout.HelpBox(reason + " The object will throw when spawned.", UnityEditor.MessageType.Error);
        DrawDefaultInspector();
    }
}
#endif
