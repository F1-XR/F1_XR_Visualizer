using UnityEngine;

namespace F1XR.Experience.Fracture
{
    /// <summary>
    /// The fine debris that falls out of a crack as it opens. One shared particle system
    /// for the whole break, never one per fragment.
    ///
    /// Deliberately not an explosion: no cone, no outward burst, no smoke cloud. Grains
    /// start almost stationary, gravity takes them, and they are gone in about a second.
    /// Think plaster dust off a cracked shell.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShellDustEmitter : MonoBehaviour
    {
        [Header("Emission")]
        [Tooltip("Grains released each time a fragment lets go.")]
        [SerializeField, Range(0, 30)] int grainsPerFragment = 6;

        [Header("Grain")]
        [SerializeField] Vector2 sizeRange = new(0.004f, 0.012f);
        [SerializeField] Vector2 lifetimeRange = new(0.6f, 1.2f);

        [Tooltip("Initial speed. Kept tiny; gravity is meant to do the work.")]
        [SerializeField, Range(0f, 0.5f)] float initialSpeed = 0.12f;

        [SerializeField, Range(0f, 2f)] float gravityModifier = 0.7f;
        [SerializeField] Color dustColor = new(0.85f, 0.83f, 0.78f, 0.85f);

        ParticleSystem system;
        ParticleSystem.EmitParams emitParams;

        void Awake()
        {
            EnsureSystem();
        }

        void OnDestroy()
        {
            if (system != null)
            {
                var renderer = system.GetComponent<ParticleSystemRenderer>();
                if (renderer != null && renderer.sharedMaterial != null &&
                    renderer.sharedMaterial.name.StartsWith("ShellDust"))
                {
                    DestroySafely(renderer.sharedMaterial);
                }
            }
        }

        /// <summary>Drops a small handful of grains at a world position.</summary>
        public void EmitAt(Vector3 worldPosition)
        {
            if (grainsPerFragment <= 0)
                return;

            EnsureSystem();
            if (system == null)
                return;

            emitParams.position = worldPosition;
            emitParams.applyShapeToPosition = false;
            emitParams.startColor = dustColor;
            emitParams.startSize = Random.Range(sizeRange.x, sizeRange.y);
            emitParams.startLifetime = Random.Range(lifetimeRange.x, lifetimeRange.y);

            // Mostly downward with a little sideways drift, so grains trickle rather than
            // spray.
            emitParams.velocity = new Vector3(
                Random.Range(-0.35f, 0.35f),
                Random.Range(-1f, -0.2f),
                Random.Range(-0.35f, 0.35f)) * initialSpeed;

            system.Emit(emitParams, grainsPerFragment);
        }

        void EnsureSystem()
        {
            if (system != null)
                return;

            system = GetComponent<ParticleSystem>();
            if (system == null)
                system = gameObject.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = system.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = gravityModifier;
            main.maxParticles = 2000;
            main.startSpeed = 0f;
            main.startSize = sizeRange.y;
            main.startLifetime = lifetimeRange.y;

            // Emission is entirely manual through Emit; no rate, no bursts.
            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;

            var renderer = system.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                if (renderer.sharedMaterial == null)
                {
                    Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                        ?? Shader.Find("Sprites/Default");

                    if (shader != null)
                    {
                        renderer.sharedMaterial = new Material(shader)
                        {
                            name = "ShellDustMaterial",
                            color = dustColor
                        };
                    }
                }
            }

            system.Play();
        }

        static void DestroySafely(Object target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }
    }
}
