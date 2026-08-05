using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RingSport.Effects
{
    /// <summary>
    /// Replaces an object's real cast shadow with a flat blob decal on the
    /// ground directly below it.
    ///
    /// Falling steaks in the Food Refusal mini level need to telegraph which
    /// lane they are coming down in. A real shadow map does that badly - it is
    /// tiny at spawn height, soft, and costs a shadow-caster pass per steak -
    /// so the renderers stop casting entirely and this draws one alpha-blended
    /// quad pinned to the floor instead. The quad fades up from nothing as the
    /// object spawns so the lane cue arrives with the steak rather than
    /// popping in.
    ///
    /// The decal uses Custom/Mobile/BlobShadow, which applies the same global
    /// arc offset as the floor, so it stays welded to the ground however far
    /// the world is curved at that distance.
    /// </summary>
    [DisallowMultipleComponent]
    public class BlobShadow : MonoBehaviour
    {
        [Header("Decal")]
        [Tooltip("Material for the ground quad - Custom/Mobile/BlobShadow.")]
        [SerializeField] private Material shadowMaterial;
        [Tooltip("World-space width of the decal quad.")]
        [SerializeField] private float size = 2f;
        [Tooltip("Stretch along Z. The mini-level camera looks down at 35 degrees, which foreshortens depth - without this the blob reads as a thin sliver.")]
        [SerializeField] private float depthStretch = 1.8f;
        [Tooltip("Ground plane height the decal is pinned to.")]
        [SerializeField] private float groundY = 0f;
        [Tooltip("Lift above the ground so the decal never z-fights the floor.")]
        [SerializeField] private float groundOffset = 0.02f;

        [Header("Fade In")]
        [Tooltip("Opacity the decal settles at.")]
        [Range(0f, 1f)]
        [SerializeField] private float maxAlpha = 0.55f;
        [Tooltip("Seconds to ramp from invisible to full opacity after spawning.")]
        [SerializeField] private float fadeInSeconds = 0.35f;

        [Header("Caster")]
        [Tooltip("Turn off real shadow casting on this object's renderers.")]
        [SerializeField] private bool disableRealShadows = true;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // Every distinct decal material in the scene, collected as the pooler
        // instantiates the prefabs that use them. Warmup draws the lot.
        private static readonly List<Material> warmupMaterials = new List<Material>();
        private static GameObject warmupRoot;

        private Transform decal;
        private MeshRenderer decalRenderer;
        private MaterialPropertyBlock properties;
        private float alpha;

        private void Awake()
        {
            if (disableRealShadows)
            {
                // Before EnsureDecal, so this only ever touches the object's own
                // geometry and never the decal quad it is about to build.
                foreach (var renderer in GetComponentsInChildren<Renderer>(true))
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
            }

            if (shadowMaterial != null && !warmupMaterials.Contains(shadowMaterial))
                warmupMaterials.Add(shadowMaterial);

            // Built here rather than on first enable: the pooler instantiates
            // every steak up front, so the quad costs nothing mid-game.
            EnsureDecal();
        }

        private void OnEnable()
        {
            if (decal == null)
                return;

            alpha = 0f;
            ApplyAlpha();
            decal.gameObject.SetActive(true);
            PositionDecal();
        }

        private void OnDisable()
        {
            if (decal != null)
                decal.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (decal == null)
                return;

            if (alpha < maxAlpha)
            {
                // Unscaled: mini levels run the rest of the game at timeScale 0
                alpha = fadeInSeconds > 0f
                    ? Mathf.Min(maxAlpha, alpha + maxAlpha * Time.unscaledDeltaTime / fadeInSeconds)
                    : maxAlpha;
                ApplyAlpha();
            }

            PositionDecal();
        }

        /// <summary>Pins the decal flat on the ground under the object.</summary>
        private void PositionDecal()
        {
            Vector3 position = transform.position;
            decal.SetPositionAndRotation(
                new Vector3(position.x, groundY + groundOffset, position.z),
                Quaternion.Euler(90f, 0f, 0f));
            // Local Y becomes world Z once the quad is laid flat
            decal.localScale = new Vector3(size, size * depthStretch, 1f);
        }

        private void ApplyAlpha()
        {
            if (decalRenderer == null)
                return;

            decalRenderer.GetPropertyBlock(properties);
            properties.SetColor(BaseColorId, new Color(0f, 0f, 0f, alpha));
            decalRenderer.SetPropertyBlock(properties);
        }

        /// <summary>
        /// Builds the decal on first enable. It is parented to this object so a
        /// pooled return takes it along, but its transform is driven in world
        /// space every frame - it must not inherit the fall.
        /// </summary>
        private void EnsureDecal()
        {
            if (decal != null || shadowMaterial == null)
                return;

            properties = new MaterialPropertyBlock();

            var go = new GameObject("BlobShadow");
            go.transform.SetParent(transform, false);
            go.layer = gameObject.layer;

            go.AddComponent<MeshFilter>().sharedMesh = QuadMesh();

            decalRenderer = go.AddComponent<MeshRenderer>();
            decalRenderer.sharedMaterial = shadowMaterial;
            decalRenderer.shadowCastingMode = ShadowCastingMode.Off;
            decalRenderer.receiveShadows = false;
            decalRenderer.lightProbeUsage = LightProbeUsage.Off;
            decalRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            decalRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            decal = go.transform;
        }

        // ------------------------------------------------------------------
        // Warmup
        // ------------------------------------------------------------------

        /// <summary>
        /// Draws every blob material once, fully transparent, in front of the
        /// camera for a short window.
        ///
        /// A material's first real draw is where the shader variant gets
        /// compiled and its texture uploaded to the GPU - on WebGL that lands
        /// a frame or two late, so the first steak's shadow appeared untextured
        /// and then popped. Call this when the mini level is preparing (during
        /// the camera move and countdown) and the first shadow is already
        /// resident by the time a steak falls.
        /// </summary>
        /// <param name="position">Somewhere on screen - the lane strip works.</param>
        /// <param name="seconds">How long to keep drawing, in unscaled time.</param>
        public static void Warmup(Vector3 position, float seconds = 1.5f)
        {
            if (warmupMaterials.Count == 0)
                return;

            if (warmupRoot == null)
            {
                warmupRoot = new GameObject("BlobShadowWarmup");
                var block = new MaterialPropertyBlock();

                foreach (var material in warmupMaterials)
                {
                    var quad = new GameObject("Warmup");
                    quad.transform.SetParent(warmupRoot.transform, false);
                    quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    quad.AddComponent<MeshFilter>().sharedMesh = QuadMesh();

                    var renderer = quad.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

                    // Alpha 0 still runs the fragment shader and binds the
                    // texture - the draw is real, it just leaves no mark.
                    renderer.GetPropertyBlock(block);
                    block.SetColor(BaseColorId, new Color(0f, 0f, 0f, 0f));
                    renderer.SetPropertyBlock(block);
                }

                warmupRoot.AddComponent<WarmupWindow>();
            }

            warmupRoot.transform.position = position;
            warmupRoot.SetActive(true);
            warmupRoot.GetComponent<WarmupWindow>().KeepAliveFor(seconds);
        }

        /// <summary>Switches the warmup draws off once the window closes.</summary>
        private class WarmupWindow : MonoBehaviour
        {
            private float until;

            public void KeepAliveFor(float seconds) => until = Time.unscaledTime + seconds;

            private void LateUpdate()
            {
                if (Time.unscaledTime >= until)
                    gameObject.SetActive(false);
            }
        }

        private static Mesh quadMesh;

        /// <summary>Shared unit quad - one mesh for every blob in the scene.</summary>
        private static Mesh QuadMesh()
        {
            if (quadMesh != null)
                return quadMesh;

            var primitive = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quadMesh = primitive.GetComponent<MeshFilter>().sharedMesh;
            Destroy(primitive);
            return quadMesh;
        }
    }
}
