using System.Collections.Generic;
using UnityEngine;

namespace RingSport.Player
{
    /// <summary>
    /// Swaps the animated dog for the Malbers Wolf Lite ragdoll on death: the
    /// corpse is posed to match the live skeleton, knocked back off whatever was
    /// hit, and pulled down by extra gravity while falling. Once it settles (or
    /// a hard timeout passes) it is frozen kinematic so it can't twitch. Wired
    /// by Tools > RingSport > Setup Dog Player.
    /// </summary>
    public class PlayerRagdoll : MonoBehaviour
    {
        [SerializeField] private GameObject ragdollPrefab;
        [Tooltip("The animated dog model that gets hidden while the ragdoll is active.")]
        [SerializeField] private GameObject dogModel;

        [Header("Death Impulse")]
        [Tooltip("Initial velocity of every body part; -Z knocks the dog back off the obstacle it ran into.")]
        [SerializeField] private Vector3 bounceVelocity = new Vector3(0f, 4.5f, -6f);
        [SerializeField] private float randomTumble = 3f;
        [Tooltip("Extra downward acceleration on top of normal gravity, for a heavy fall.")]
        [SerializeField] private float extraGravity = 30f;
        [Tooltip("How long the extra gravity is applied. It must stop once the corpse is down, or the bodies get pinned into the floor and twitch.")]
        [SerializeField] private float extraGravityDuration = 1.25f;

        [Header("Settling")]
        [Tooltip("When every body part moves slower than this (m/s)...")]
        [SerializeField] private float settleSpeedThreshold = 0.25f;
        [Tooltip("...for this long (seconds), the ragdoll is frozen in place.")]
        [SerializeField] private float settleDelay = 0.4f;
        [Tooltip("Hard cap: freeze this long after death even if the ragdoll never fully settles, so jitter can't continue indefinitely.")]
        [SerializeField] private float maxActiveTime = 4f;

        private GameObject instance;
        private Rigidbody[] bodies = System.Array.Empty<Rigidbody>();
        private float activatedTime;
        private float stillTimer;
        private bool isFrozen;

        public bool HasRagdoll => ragdollPrefab != null && dogModel != null;
        public bool IsActive => instance != null;

        public void ActivateRagdoll()
        {
            if (instance != null || !HasRagdoll)
                return;

            Transform source = dogModel.transform;
            instance = Instantiate(ragdollPrefab, source.position, source.rotation);

            // Strip the Malbers behaviours; joints, rigidbodies and colliders stay
            foreach (var behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true))
                Destroy(behaviour);
            foreach (var anim in instance.GetComponentsInChildren<Animator>(true))
                anim.enabled = false;

            CopyPose(source, instance.transform);

            // The corpse spawns inside the player's CharacterController capsule
            // (and the Playermodel trigger); it must not collide with them or the
            // solver fights the invisible capsule and the ragdoll jitters.
            var playerColliders = GetComponentsInChildren<Collider>(true);
            var ragdollColliders = instance.GetComponentsInChildren<Collider>(true);
            foreach (var ragdollCollider in ragdollColliders)
            {
                foreach (var playerCollider in playerColliders)
                    Physics.IgnoreCollision(ragdollCollider, playerCollider, true);
            }

            // Joint projection snaps drifting limbs back to their sockets instead
            // of letting the solver oscillate toward them - the main anti-twitch
            foreach (var joint in instance.GetComponentsInChildren<CharacterJoint>(true))
            {
                joint.enableProjection = true;
                joint.projectionDistance = 0.05f;
                joint.projectionAngle = 30f;
            }

            bodies = instance.GetComponentsInChildren<Rigidbody>(true);
            foreach (var body in bodies)
            {
                body.isKinematic = false;
                body.useGravity = true;
                // Fast bodies vs thin obstacle colliders - don't tunnel through
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                // Physics steps at 50Hz; without interpolation the rendered corpse
                // stutters even when the simulation is calm
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.solverIterations = 8;
                body.sleepThreshold = 0.05f;
                body.linearVelocity = bounceVelocity;
                body.angularVelocity = Random.insideUnitSphere * randomTumble;
            }

            activatedTime = Time.time;
            stillTimer = 0f;
            isFrozen = false;

            // The worn hat pops off with the corpse - heavy, so it lands
            // rather than bouncing away
            GetComponent<HatEquipper>()?.DropHat();

            dogModel.SetActive(false);
        }

        public void Clear()
        {
            if (instance != null)
                Destroy(instance);

            instance = null;
            bodies = System.Array.Empty<Rigidbody>();
            stillTimer = 0f;
            isFrozen = false;

            if (dogModel != null)
                dogModel.SetActive(true);
        }

        private void FixedUpdate()
        {
            if (instance == null || isFrozen)
                return;

            float elapsed = Time.time - activatedTime;
            if (elapsed >= maxActiveTime)
            {
                Freeze();
                return;
            }

            bool applyExtraGravity = elapsed < extraGravityDuration;
            float maxSpeed = 0f;

            foreach (var body in bodies)
            {
                if (body == null)
                    continue;

                if (applyExtraGravity && !body.IsSleeping())
                    body.AddForce(Vector3.down * extraGravity, ForceMode.Acceleration);

                float speed = body.linearVelocity.magnitude;
                if (speed > maxSpeed)
                    maxSpeed = speed;
            }

            if (applyExtraGravity)
                return;

            // Freeze the corpse once it has genuinely come to rest, so joint
            // micro-corrections can't make it twitch on the ground
            if (maxSpeed < settleSpeedThreshold)
            {
                stillTimer += Time.fixedDeltaTime;
                if (stillTimer >= settleDelay)
                    Freeze();
            }
            else
            {
                stillTimer = 0f;
            }
        }

        private void Freeze()
        {
            isFrozen = true;

            foreach (var body in bodies)
            {
                if (body == null)
                    continue;

                // Kinematic bodies don't support continuous collision - switch
                // modes first to avoid a warning
                body.collisionDetectionMode = CollisionDetectionMode.Discrete;
                body.isKinematic = true;
            }
        }

        /// <summary>Poses the ragdoll skeleton to match the animated one, bone names matched.</summary>
        private static void CopyPose(Transform source, Transform target)
        {
            var sourceBones = new Dictionary<string, Transform>();
            foreach (var bone in source.GetComponentsInChildren<Transform>(true))
                sourceBones[bone.name] = bone;

            foreach (var bone in target.GetComponentsInChildren<Transform>(true))
            {
                if (bone == target)
                    continue;

                if (sourceBones.TryGetValue(bone.name, out var match))
                {
                    bone.localPosition = match.localPosition;
                    bone.localRotation = match.localRotation;
                }
            }
        }
    }
}
