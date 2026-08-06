using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using RingSport.Level;

namespace RingSport.Editor
{
    /// <summary>
    /// Builds the Humanoid avatar for the decoy model (Assets/Models/decoy.glb),
    /// which is what lets the new model keep every Malbers human animation.
    ///
    /// The model was authored in Blender ON THE MALBERS STEVE SKELETON: bone for
    /// bone it is the same rig - same names (the Blender round trip turned the
    /// spaces into underscores, "R_L Thigh" -> "R_L_Thigh"), same local
    /// rotations to three decimals, same bone lengths, just expressed in
    /// centimetres under a 0.01 scale on R_CG. So the human bone mapping is
    /// lifted straight off Steve's ModelImporter and re-pointed at the new bone
    /// names; the result is a real Humanoid avatar, and Mecanim then retargets
    /// the Malbers clips onto it for free.
    ///
    /// (This is why the decoy needs nothing like CaicosRetarget: the dog had to
    /// have every clip baked because a quadruped has no humanoid rig to
    /// retarget through. A human does.)
    /// </summary>
    public static class DecoyAvatarSetup
    {
        public const string DecoyModelGuid = "a6f4857f8ac8248b7873259e7c934358"; // Assets/Models/decoy.glb
        public const string SteveModelGuid = "d9a8fb1864c033e4b990da223ac23ead"; // Steve_v2.fbx - donor of the human bone mapping
        public const string AvatarPath = "Assets/Animations/Decoy/DecoyAvatar.asset";

        /// <summary>
        /// Yaw (degrees about Y) that turns the model to face +Z, which is both
        /// what the game expects of the decoy and what Unity's humanoid builder
        /// assumes of a reference pose. The Blender export comes out facing
        /// sideways; left uncorrected the avatar infers the body's axes from a
        /// pose that is turned 90 degrees, and every clip plays scrambled
        /// (limbs in the wrong plane, not merely rotated).
        ///
        /// Measured off the rig rather than hardcoded, so a re-export at a
        /// different orientation still lands: the vector from the left hip to
        /// the right hip is the character's own +X, and its cross with up is
        /// the direction it faces.
        /// </summary>
        public static float MeasureFacingYaw(GameObject modelInstance)
        {
            Vector3 hips = SideAxis(modelInstance, "L Thigh", "R Thigh");
            Vector3 shoulders = SideAxis(modelInstance, "L UpperArm", "R UpperArm");

            Vector3 right = hips + shoulders; // agreeing halves reinforce, a bad one cancels
            if (right.sqrMagnitude < 1e-6f)
            {
                Debug.LogWarning("[DecoyAvatarSetup] Could not read the decoy's facing off its hips or shoulders - leaving it unrotated.");
                return 0f;
            }

            Vector3 forward = Vector3.Cross(right.normalized, Vector3.up);
            float yaw = Vector3.SignedAngle(forward, Vector3.forward, Vector3.up);
            Debug.Log($"[DecoyAvatarSetup] Facing: the model's hips run {hips.normalized}, shoulders {shoulders.normalized} " +
                      $"-> it faces {forward.normalized}, so the model is turned {yaw:F1}° to face +Z.");
            return yaw;
        }

        /// <summary>
        /// Turns the rig to face +Z, in the one place where the bind pose, the
        /// avatar's reference pose and the animated result all stay in
        /// agreement: the skeleton's OWN root bone (R_CG). It sits below the
        /// Animator and is not a human bone, so nothing overwrites it at
        /// runtime and its rotation counts exactly once.
        ///
        /// Rotating the model root instead double-counts - the avatar bakes the
        /// reference orientation in, and then the transform multiplies on top,
        /// which turns the running decoy 90 degrees the other way.
        /// </summary>
        public static void ApplyFacingYaw(GameObject modelInstance, float yaw)
        {
            if (Mathf.Abs(yaw) < 0.01f)
                return;

            var rigRoot = FindRigRoot(modelInstance);
            if (rigRoot == null)
            {
                Debug.LogWarning("[DecoyAvatarSetup] The rig has no root bone above the hips, so the model's facing cannot be corrected there. The decoy will run sideways - re-export it facing +Z.");
                return;
            }
            rigRoot.rotation = Quaternion.Euler(0f, yaw, 0f) * rigRoot.rotation;
        }

        /// <summary>
        /// The bone the whole skeleton hangs off: the hips' parent, as long as
        /// that is a bone of its own and not the model root.
        /// </summary>
        private static Transform FindRigRoot(GameObject modelInstance)
        {
            foreach (var bone in modelInstance.GetComponentsInChildren<Transform>(true))
            {
                if (DecoyController.NormalizeBoneName(bone.name) != "Pelvis")
                    continue;
                var parent = bone.parent;
                return parent != null && parent != modelInstance.transform ? parent : null;
            }
            return null;
        }

        /// <summary>Left-to-right vector across a bone pair, flattened to the ground plane.</summary>
        private static Vector3 SideAxis(GameObject modelInstance, string leftBone, string rightBone)
        {
            Transform left = null, right = null;
            foreach (var bone in modelInstance.GetComponentsInChildren<Transform>(true))
            {
                string key = DecoyController.NormalizeBoneName(bone.name);
                if (left == null && key == leftBone)
                    left = bone;
                else if (right == null && key == rightBone)
                    right = bone;
            }
            if (left == null || right == null)
                return Vector3.zero;

            Vector3 axis = right.position - left.position;
            axis.y = 0f;
            return axis.sqrMagnitude < 1e-6f ? Vector3.zero : axis.normalized;
        }

        /// <summary>
        /// Builds (or rebuilds in place) the avatar asset from a live instance of
        /// the decoy model. The instance must be named, and turned, exactly as it
        /// will be in the Decoy prefab - the avatar binds its skeleton by
        /// transform name starting at the root, and bakes in the reference pose.
        /// </summary>
        public static Avatar Build(GameObject modelInstance)
        {
            if (modelInstance == null)
                return null;

            var stevePath = AssetDatabase.GUIDToAssetPath(SteveModelGuid);
            var steveImporter = string.IsNullOrEmpty(stevePath) ? null : AssetImporter.GetAtPath(stevePath) as ModelImporter;
            if (steveImporter == null)
            {
                Debug.LogError("[DecoyAvatarSetup] Steve_v2.fbx not found - it is the source of the human bone mapping. Is the Malbers Animations package intact?");
                return null;
            }

            var source = steveImporter.humanDescription;

            // Both rigs use the same bone names once normalized (R_ prefix
            // stripped, underscores read as spaces) - the same rule the runtime
            // uses to match animated bones to ragdoll bones.
            var byName = new Dictionary<string, Transform>();
            foreach (var bone in modelInstance.GetComponentsInChildren<Transform>(true))
            {
                string key = DecoyController.NormalizeBoneName(bone.name);
                if (!byName.ContainsKey(key))
                    byName[key] = bone;
            }

            var human = new List<HumanBone>();
            var unmapped = new List<string>();
            foreach (var bone in source.human)
            {
                if (!byName.TryGetValue(DecoyController.NormalizeBoneName(bone.boneName), out var match))
                {
                    unmapped.Add($"{bone.humanName} (<- {bone.boneName})");
                    continue;
                }
                human.Add(new HumanBone
                {
                    humanName = bone.humanName,
                    boneName = match.name,
                    limit = bone.limit,
                });
            }

            if (unmapped.Count > 0)
                Debug.LogWarning($"[DecoyAvatarSetup] {unmapped.Count} of Steve's human bones have no match on the decoy model and were left out: {string.Join(", ", unmapped)}");

            // The skeleton array is the avatar's REFERENCE POSE - what muscle
            // values are measured against - and it is not simply the rest pose:
            // Steve's rest pose is an A-pose (upper arms dropped ~50 degrees)
            // and Unity's importer stores an enforced T-pose for it instead.
            // Read straight from the decoy's transforms, the arms end up ~50
            // degrees high in every clip, so the same correction is carried
            // across bone by bone.
            var corrections = BuildTPoseCorrections(steveImporter, stevePath);
            var skeleton = modelInstance.GetComponentsInChildren<Transform>(true)
                .Select(t =>
                {
                    var rotation = t.localRotation;
                    if (corrections.TryGetValue(DecoyController.NormalizeBoneName(t.name), out var correction))
                        rotation *= correction;
                    return new SkeletonBone
                    {
                        name = t.name,
                        position = t.localPosition,
                        rotation = rotation,
                        scale = t.localScale,
                    };
                })
                .ToArray();

            var description = new HumanDescription
            {
                human = human.ToArray(),
                skeleton = skeleton,
                // Muscle tuning comes from Steve so the clips play with the same
                // stretch and twist distribution they were authored against
                upperArmTwist = source.upperArmTwist,
                lowerArmTwist = source.lowerArmTwist,
                upperLegTwist = source.upperLegTwist,
                lowerLegTwist = source.lowerLegTwist,
                armStretch = source.armStretch,
                legStretch = source.legStretch,
                feetSpacing = source.feetSpacing,
                hasTranslationDoF = source.hasTranslationDoF,
            };

            var built = AvatarBuilder.BuildHumanAvatar(modelInstance, description);
            if (built == null || !built.isValid || !built.isHuman)
            {
                Debug.LogError($"[DecoyAvatarSetup] Could not build a Humanoid avatar for the decoy model (valid={built != null && built.isValid}, human={built != null && built.isHuman}). The Malbers clips are humanoid, so without one the decoy cannot be animated.");
                if (built != null)
                    Object.DestroyImmediate(built);
                return null;
            }
            built.name = "DecoyAvatar";

            // Rebuild in place when it already exists so the asset keeps its GUID
            var existing = AssetDatabase.LoadAssetAtPath<Avatar>(AvatarPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(built, existing);
                Object.DestroyImmediate(built);
                EditorUtility.SetDirty(existing);
                if (!existing.isValid || !existing.isHuman)
                {
                    Debug.LogError($"[DecoyAvatarSetup] The rebuilt avatar at {AvatarPath} came back invalid - delete it and re-run Tools > RingSport > Setup Decoy.");
                    return null;
                }
                Debug.Log($"[DecoyAvatarSetup] Rebuilt {AvatarPath} ({human.Count} human bones mapped onto {skeleton.Length} transforms).");
                return existing;
            }

            AssetDatabase.CreateAsset(built, AvatarPath);
            Debug.Log($"[DecoyAvatarSetup] Created {AvatarPath} ({human.Count} human bones mapped onto {skeleton.Length} transforms).");
            return built;
        }

        /// <summary>
        /// Per bone, the rotation that takes the Malbers rig from the rest pose
        /// in the FBX to the reference pose Unity actually stored in Steve's
        /// avatar - i.e. whatever "Enforce T-Pose" did when the model was
        /// configured. Expressed in each bone's own local frame, which the
        /// decoy shares (its bones carry identical local rotations), so the
        /// same correction applies unchanged. Bones that were never adjusted
        /// come back as identity and cost nothing.
        /// </summary>
        private static Dictionary<string, Quaternion> BuildTPoseCorrections(ModelImporter steveImporter, string stevePath)
        {
            var corrections = new Dictionary<string, Quaternion>();
            var stevePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(stevePath);
            if (stevePrefab == null)
                return corrections;

            var steve = Object.Instantiate(stevePrefab);
            steve.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var rest = new Dictionary<string, Quaternion>();
                foreach (var bone in steve.GetComponentsInChildren<Transform>(true))
                {
                    string key = DecoyController.NormalizeBoneName(bone.name);
                    if (!rest.ContainsKey(key))
                        rest[key] = bone.localRotation;
                }

                float worst = 0f;
                string worstBone = "none";
                foreach (var stored in steveImporter.humanDescription.skeleton)
                {
                    string key = DecoyController.NormalizeBoneName(stored.name);
                    if (!rest.TryGetValue(key, out var restRotation))
                        continue;

                    var correction = Quaternion.Inverse(restRotation) * stored.rotation;
                    corrections[key] = correction;

                    float angle = Quaternion.Angle(Quaternion.identity, correction);
                    if (angle > worst)
                    {
                        worst = angle;
                        worstBone = key;
                    }
                }

                Debug.Log($"[DecoyAvatarSetup] Reference pose: carried Steve's stored T-pose across {corrections.Count} bones " +
                          $"(largest correction {worst:F1}° on {worstBone}).");
            }
            finally
            {
                Object.DestroyImmediate(steve);
            }
            return corrections;
        }
    }
}
