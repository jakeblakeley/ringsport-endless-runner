using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RingSport.Editor
{
    /// <summary>
    /// Generates the death ragdoll for the caicos dog.
    ///
    /// PlayerRagdoll used to spawn Malbers' "Wolf Lite Ragdoll.prefab" and copy the
    /// live skeleton's pose onto it by bone name. That prefab carries the wolf's
    /// own mesh and bind pose, so with a different dog on screen the corpse was the
    /// wrong animal. This builds an equivalent from the caicos model instead: a
    /// prefab VARIANT of caicos.glb (so a re-export still flows through) with
    /// rigidbodies, colliders and character joints laid out from the rig's own bone
    /// geometry, mirroring which bones Malbers gave bodies to.
    ///
    /// Run from Tools > RingSport > Setup Dog Player; regenerated whenever the
    /// model is re-imported.
    /// </summary>
    public static class CaicosRagdollSetup
    {
        public const string RagdollPrefabPath = "Assets/Prefabs/CaicosRagdoll.prefab";

        private const float MassPerBody = 1f;
        private const float MinRadius = 0.02f;
        private const float MaxRadius = 0.14f;
        private const float RadiusFraction = 0.32f;
        // Leaf bones (feet, hands, tail tip) have no child to measure against
        private const float LeafLengthFraction = 0.6f;

        /// <summary>Bones that get a body, in parent-first order.</summary>
        private static readonly string[] BodyBones =
        {
            "Pelvis", "Spine", "Spine2", "Neck", "Head",
            "L Thigh", "L Calf", "L HorseLink", "L Foot",
            "R Thigh", "R Calf", "R HorseLink", "R Foot",
            "L UpperArm", "L Forearm", "L Hand",
            "R UpperArm", "R Forearm", "R Hand",
            "Tail", "Tail1", "Tail2", "Tail3",
        };

        /// <summary>
        /// Bones whose collider is a box rather than a capsule - the torso blocks
        /// and the flat extremities. Everything else is a limb capsule.
        /// </summary>
        private static readonly HashSet<string> BoxBones = new()
        {
            "Spine", "Spine2", "Neck", "L Foot", "R Foot", "L Hand", "R Hand",
            "Tail", "Tail1", "Tail2", "Tail3",
        };

        /// <summary>
        /// Rebuilds the ragdoll prefab from the current model. Returns the asset,
        /// or null if the model is missing or has none of the expected bones.
        /// </summary>
        public static GameObject Build(GameObject modelPrefab, float modelScale)
        {
            if (modelPrefab == null)
                return null;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab);
            if (instance == null)
            {
                Debug.LogError("[CaicosRagdollSetup] Could not instantiate the model prefab.");
                return null;
            }

            try
            {
                instance.name = "CaicosRagdoll";
                instance.transform.localScale = Vector3.one * modelScale;

                var bones = instance.GetComponentsInChildren<Transform>(true)
                    .GroupBy(t => t.name)
                    .ToDictionary(g => g.Key, g => g.First());

                var present = BodyBones.Where(bones.ContainsKey).ToArray();
                if (present.Length < BodyBones.Length / 2)
                {
                    Debug.LogError($"[CaicosRagdollSetup] The model only has {present.Length} of the {BodyBones.Length} expected ragdoll bones - skipping ragdoll generation.");
                    return null;
                }
                if (present.Length < BodyBones.Length)
                {
                    var missing = BodyBones.Where(b => !bones.ContainsKey(b));
                    Debug.LogWarning($"[CaicosRagdollSetup] Bones missing from the model, left out of the ragdoll: {string.Join(", ", missing)}");
                }

                var bodies = new Dictionary<string, Rigidbody>();
                foreach (var name in present)
                {
                    var bone = bones[name];

                    var body = bone.gameObject.AddComponent<Rigidbody>();
                    body.mass = MassPerBody;
                    body.useGravity = true;
                    bodies[name] = body;

                    AddCollider(bone, name);

                    // Chain to the nearest ancestor that already has a body; the
                    // first one (Pelvis) is the free root of the ragdoll.
                    var parentBody = FindParentBody(bone, bodies);
                    if (parentBody != null)
                        AddJoint(bone, parentBody, name);
                }

                var asset = PrefabUtility.SaveAsPrefabAsset(instance, RagdollPrefabPath, out bool success);
                if (!success)
                {
                    Debug.LogError($"[CaicosRagdollSetup] Failed to save {RagdollPrefabPath}.");
                    return null;
                }

                Debug.Log($"[CaicosRagdollSetup] Built {RagdollPrefabPath} with {bodies.Count} bodies (scale {modelScale:F3}).");
                return asset;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static Rigidbody FindParentBody(Transform bone, Dictionary<string, Rigidbody> bodies)
        {
            for (var t = bone.parent; t != null; t = t.parent)
            {
                if (bodies.TryGetValue(t.name, out var body))
                    return body;
            }
            return null;
        }

        /// <summary>
        /// The vector from a bone to the child it "points at", in the bone's own
        /// local space. Uses the child furthest from the bone so that a joint with
        /// several children (the head, the spine) still measures along the body.
        /// </summary>
        private static Vector3 BoneVector(Transform bone)
        {
            Vector3 best = Vector3.zero;
            float bestLength = 0f;
            foreach (Transform child in bone)
            {
                // Skip the mesh node - it sits at the rig root and would measure
                // to the origin rather than along a limb
                if (child.GetComponent<Renderer>() != null)
                    continue;
                float length = child.localPosition.magnitude;
                if (length > bestLength)
                {
                    bestLength = length;
                    best = child.localPosition;
                }
            }
            return best;
        }

        private static void AddCollider(Transform bone, string name)
        {
            Vector3 vector = BoneVector(bone);
            float length = vector.magnitude;

            if (length < 1e-4f)
            {
                // A leaf: size it from the bone's own offset from its parent, which
                // is the closest thing to a length this bone has
                vector = bone.localPosition;
                length = vector.magnitude * LeafLengthFraction;
                if (length < 1e-4f)
                {
                    length = 0.05f;
                    vector = Vector3.up * length;
                }
            }

            float radius = Mathf.Clamp(length * RadiusFraction, MinRadius, MaxRadius);
            Vector3 direction = vector.normalized;

            if (BoxBones.Contains(name))
            {
                var box = bone.gameObject.AddComponent<BoxCollider>();
                box.center = direction * (length * 0.5f);
                // Thickness across the bone, length along it
                box.size = new Vector3(
                    Mathf.Abs(direction.x) * length + radius * 2f,
                    Mathf.Abs(direction.y) * length + radius * 2f,
                    Mathf.Abs(direction.z) * length + radius * 2f);
                return;
            }

            var capsule = bone.gameObject.AddComponent<CapsuleCollider>();
            capsule.direction = DominantAxis(direction);
            capsule.center = direction * (length * 0.5f);
            capsule.radius = radius;
            capsule.height = length + radius * 2f;
        }

        /// <summary>
        /// Joint limits by body part. A dog's knees and hocks are near-hinges and
        /// barely twist, so uniform limits let the legs corkscrew into poses no
        /// real animal could hold. Elbows/knees get almost no twist and very
        /// little sideways swing; the spine stays modest; only the tail is loose.
        /// </summary>
        private static (float lowTwist, float highTwist, float swing1, float swing2) LimitsFor(string name)
        {
            if (name.EndsWith("Calf") || name.EndsWith("Forearm") || name.EndsWith("HorseLink"))
                return (-5f, 5f, 20f, 5f);      // hinges
            if (name.EndsWith("Foot") || name.EndsWith("Hand"))
                return (-5f, 5f, 15f, 8f);      // paws
            if (name.EndsWith("Thigh") || name.EndsWith("UpperArm"))
                return (-10f, 10f, 28f, 12f);   // hips and shoulders
            if (name.StartsWith("Tail"))
                return (-15f, 15f, 35f, 25f);   // the one part that should flop
            return (-10f, 10f, 22f, 12f);       // spine, neck, head
        }

        /// <summary>CapsuleCollider.direction for the axis the bone mostly runs along.</summary>
        private static int DominantAxis(Vector3 direction)
        {
            float x = Mathf.Abs(direction.x), y = Mathf.Abs(direction.y), z = Mathf.Abs(direction.z);
            if (x >= y && x >= z)
                return 0;
            return y >= z ? 1 : 2;
        }

        private static void AddJoint(Transform bone, Rigidbody parentBody, string name)
        {
            var joint = bone.gameObject.AddComponent<CharacterJoint>();
            joint.connectedBody = parentBody;
            joint.anchor = Vector3.zero;
            joint.autoConfigureConnectedAnchor = true;

            // Twist runs down the bone; swing is measured off it
            Vector3 vector = BoneVector(bone);
            Vector3 twist = vector.sqrMagnitude > 1e-8f ? vector.normalized : Vector3.up;
            joint.axis = twist;
            joint.swingAxis = Vector3.Cross(twist, Mathf.Abs(twist.y) < 0.9f ? Vector3.up : Vector3.right).normalized;

            var (lowTwist, highTwist, swing1, swing2) = LimitsFor(name);
            joint.lowTwistLimit = new SoftJointLimit { limit = lowTwist };
            joint.highTwistLimit = new SoftJointLimit { limit = highTwist };
            joint.swing1Limit = new SoftJointLimit { limit = swing1 };
            joint.swing2Limit = new SoftJointLimit { limit = swing2 };

            // PlayerRagdoll turns projection on at spawn; setting it here too keeps
            // the prefab usable on its own
            joint.enableProjection = true;
            joint.projectionDistance = 0.05f;
            joint.projectionAngle = 30f;
        }
    }
}
