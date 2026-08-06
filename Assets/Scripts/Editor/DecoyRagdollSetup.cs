using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using RingSport.Level;

namespace RingSport.Editor
{
    /// <summary>
    /// Generates the decoy's catch ragdoll from the decoy model.
    ///
    /// The catch used to spawn Malbers' "Steve Ragdoll.prefab", which carries
    /// Steve's own mesh - with the new decoy on screen the dog would have been
    /// dragging a stranger around. This builds the equivalent from decoy.glb: a
    /// prefab VARIANT of the model (so a re-export flows through) with the same
    /// 16 bodies Malbers gave Steve, their colliders and joints COPIED off that
    /// ragdoll rather than re-derived - the two rigs are the same skeleton, so
    /// the tuned human proportions transfer exactly.
    ///
    /// Two conversions are needed on the way across:
    /// - UNITS. The decoy's bones are in centimetres under a 0.01 scale on
    ///   R_CG, so every length (radius, height, box size, offset) is multiplied
    ///   by the measured ratio between the rigs' bone lengths (100).
    /// - FRAME. Every bone below the pelvis has an identical local rotation on
    ///   both rigs, but R_CG itself is turned (Blender's export folded the axis
    ///   conversion in differently), so collider and joint axes are rotated
    ///   into the decoy bone's own space instead of being copied raw.
    ///
    /// Run from Tools > RingSport > Setup Decoy; regenerated when the model is
    /// re-imported.
    /// </summary>
    public static class DecoyRagdollSetup
    {
        public const string RagdollPrefabPath = "Assets/Prefabs/DecoyRagdoll.prefab";
        private const string SteveRagdollGuid = "820e370bc2f37df44adfbdd3b1536ed1"; // Steve Ragdoll.prefab

        /// <summary>
        /// Rebuilds the ragdoll prefab from the current decoy model. Returns the
        /// asset, or null if the donor ragdoll or the model bones are missing
        /// (the catch then falls back to carrying the animated model).
        /// </summary>
        public static GameObject Build(GameObject modelPrefab)
        {
            if (modelPrefab == null)
                return null;

            var donorPath = AssetDatabase.GUIDToAssetPath(SteveRagdollGuid);
            var donorPrefab = string.IsNullOrEmpty(donorPath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(donorPath);
            if (donorPrefab == null)
            {
                Debug.LogError("[DecoyRagdollSetup] Steve Ragdoll.prefab not found - it is the source of the body/collider/joint layout. Is the Malbers Animations package intact?");
                return null;
            }

            var donor = (GameObject)PrefabUtility.InstantiatePrefab(donorPrefab);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab);
            try
            {
                instance.name = "DecoyRagdoll";
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = Quaternion.identity;
                donor.transform.position = Vector3.zero;
                donor.transform.rotation = Quaternion.identity;

                var bones = new Dictionary<string, Transform>();
                foreach (var bone in instance.GetComponentsInChildren<Transform>(true))
                {
                    string key = DecoyController.NormalizeBoneName(bone.name);
                    if (!bones.ContainsKey(key))
                        bones[key] = bone;
                }

                float ratio = MeasureUnitRatio(donor, bones);
                if (ratio <= 0f)
                    return null;

                var donorBodies = donor.GetComponentsInChildren<Rigidbody>(true);
                var built = new Dictionary<Rigidbody, Rigidbody>();
                var missing = new List<string>();

                // Bodies and colliders first - the joints below need every
                // connected body to already exist
                foreach (var donorBody in donorBodies)
                {
                    string key = DecoyController.NormalizeBoneName(donorBody.name);
                    if (!bones.TryGetValue(key, out var bone))
                    {
                        missing.Add(donorBody.name);
                        continue;
                    }

                    var body = bone.gameObject.AddComponent<Rigidbody>();
                    body.mass = donorBody.mass;
                    body.useGravity = true;
                    body.isKinematic = false;
                    built[donorBody] = body;

                    CopyCollider(donorBody, bone, ratio);
                }

                foreach (var pair in built)
                {
                    var donorJoint = pair.Key.GetComponent<CharacterJoint>();
                    if (donorJoint == null || donorJoint.connectedBody == null)
                        continue;
                    if (!built.TryGetValue(donorJoint.connectedBody, out var connected))
                        continue;

                    CopyJoint(donorJoint, pair.Value, connected, ratio);
                }

                if (missing.Count > 0)
                    Debug.LogWarning($"[DecoyRagdollSetup] Bones on the Malbers ragdoll with no match on the decoy model, left out: {string.Join(", ", missing)}");

                if (built.Count == 0)
                {
                    Debug.LogError("[DecoyRagdollSetup] None of the ragdoll bones matched the decoy model - is it still rigged to the Steve skeleton?");
                    return null;
                }

                EnsureFolder("Assets/Prefabs");
                var asset = PrefabUtility.SaveAsPrefabAsset(instance, RagdollPrefabPath, out bool success);
                if (!success)
                {
                    Debug.LogError($"[DecoyRagdollSetup] Failed to save {RagdollPrefabPath}.");
                    return null;
                }

                Debug.Log($"[DecoyRagdollSetup] Built {RagdollPrefabPath}: {built.Count} bodies copied off the Malbers ragdoll (bone unit ratio {ratio:F1}x).");
                return asset;
            }
            finally
            {
                Object.DestroyImmediate(instance);
                Object.DestroyImmediate(donor);
            }
        }

        /// <summary>
        /// How many decoy bone units make one Malbers unit. The decoy's bones
        /// are authored in centimetres with the metre conversion parked on
        /// R_CG's scale, so colliders sized in metres have to be multiplied by
        /// this to come out the right size in bone space. Measured off the long
        /// bones rather than assumed, so a re-export at a different scale still
        /// lands.
        /// </summary>
        private static float MeasureUnitRatio(GameObject donor, Dictionary<string, Transform> bones)
        {
            var samples = new List<float>();
            foreach (var donorBone in donor.GetComponentsInChildren<Transform>(true))
            {
                string name = DecoyController.NormalizeBoneName(donorBone.name);
                // The rig root and the hips hold where the SKELETON sits rather
                // than a bone length, and the two files park that height on
                // different bones - measuring a unit ratio off them is
                // meaningless (and reads as a wild outlier)
                if (name == "CG" || name == "Pelvis")
                    continue;

                float donorLength = donorBone.localPosition.magnitude;
                if (donorLength < 0.05f)
                    continue; // too short to measure a ratio off
                if (!bones.TryGetValue(name, out var bone))
                    continue;
                samples.Add(bone.localPosition.magnitude / donorLength);
            }

            if (samples.Count == 0)
            {
                Debug.LogError("[DecoyRagdollSetup] Could not measure the decoy rig against the Malbers rig - no shared bones with a measurable length.");
                return -1f;
            }

            samples.Sort();
            float median = samples[samples.Count / 2];

            // A handful of bones are authored with slightly different offsets in
            // the ragdoll file than in the model file, so judge the fit by how
            // many bones agree rather than by the extremes
            int offBy5Percent = samples.Count(s => Mathf.Abs(s / median - 1f) > 0.05f);
            if (offBy5Percent > samples.Count / 4)
            {
                Debug.LogWarning($"[DecoyRagdollSetup] Only {samples.Count - offBy5Percent} of {samples.Count} bones match the Malbers rig's " +
                                 "proportions - the ragdoll colliders will only approximate the body. " +
                                 "Re-export the model on an unmodified Steve skeleton if the ragdoll looks wrong.");
            }
            return median;
        }

        /// <summary>
        /// Maps a vector from the donor bone's local space into the decoy
        /// bone's. Identity for every bone below the pelvis (their local
        /// rotations match exactly); R_CG is the one that is turned.
        /// </summary>
        private static Quaternion FrameDelta(Transform donorBone, Transform bone)
        {
            return Quaternion.Inverse(bone.rotation) * donorBone.rotation;
        }

        private static void CopyCollider(Rigidbody donorBody, Transform bone, float ratio)
        {
            var donorCollider = donorBody.GetComponent<Collider>();
            if (donorCollider == null)
                return;

            var delta = FrameDelta(donorBody.transform, bone);

            switch (donorCollider)
            {
                case CapsuleCollider donorCapsule:
                {
                    var capsule = bone.gameObject.AddComponent<CapsuleCollider>();
                    capsule.center = delta * donorCapsule.center * ratio;
                    capsule.radius = donorCapsule.radius * ratio;
                    capsule.height = donorCapsule.height * ratio;
                    capsule.direction = DominantAxis(delta * AxisVector(donorCapsule.direction));
                    break;
                }
                case BoxCollider donorBox:
                {
                    var box = bone.gameObject.AddComponent<BoxCollider>();
                    box.center = delta * donorBox.center * ratio;
                    Vector3 size = delta * donorBox.size;
                    box.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)) * ratio;
                    break;
                }
                case SphereCollider donorSphere:
                {
                    var sphere = bone.gameObject.AddComponent<SphereCollider>();
                    sphere.center = delta * donorSphere.center * ratio;
                    sphere.radius = donorSphere.radius * ratio;
                    break;
                }
                default:
                    Debug.LogWarning($"[DecoyRagdollSetup] '{donorBody.name}' has an unsupported {donorCollider.GetType().Name} - that bone gets no collider.");
                    break;
            }
        }

        private static void CopyJoint(CharacterJoint donorJoint, Rigidbody body, Rigidbody connected, float ratio)
        {
            var delta = FrameDelta(donorJoint.transform, body.transform);

            var joint = body.gameObject.AddComponent<CharacterJoint>();
            joint.connectedBody = connected;
            joint.anchor = delta * donorJoint.anchor * ratio;
            joint.autoConfigureConnectedAnchor = true;
            joint.axis = delta * donorJoint.axis;
            joint.swingAxis = delta * donorJoint.swingAxis;
            joint.lowTwistLimit = donorJoint.lowTwistLimit;
            joint.highTwistLimit = donorJoint.highTwistLimit;
            joint.swing1Limit = donorJoint.swing1Limit;
            joint.swing2Limit = donorJoint.swing2Limit;

            // DecoyController rebuilds these frames and limits anatomically at
            // spawn (tightenJointLimits); this keeps the prefab usable on its own
            joint.enableProjection = true;
            joint.projectionDistance = 0.05f;
            joint.projectionAngle = 30f;
            joint.enablePreprocessing = false;
        }

        private static Vector3 AxisVector(int direction)
        {
            return direction switch
            {
                0 => Vector3.right,
                1 => Vector3.up,
                _ => Vector3.forward,
            };
        }

        /// <summary>CapsuleCollider.direction for the axis a vector mostly runs along.</summary>
        private static int DominantAxis(Vector3 direction)
        {
            float x = Mathf.Abs(direction.x), y = Mathf.Abs(direction.y), z = Mathf.Abs(direction.z);
            if (x >= y && x >= z)
                return 0;
            return y >= z ? 1 : 2;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            var parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            var name = System.IO.Path.GetFileName(path);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
