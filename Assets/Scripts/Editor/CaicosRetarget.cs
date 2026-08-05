using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RingSport.Editor
{
    /// <summary>
    /// Bakes Malbers Wolf Lite animation clips onto the caicos rig.
    ///
    /// The two skeletons share bone names and topology (caicos was modelled on the
    /// Wolf Lite rig) but nothing else lines up: the Malbers clips carry a POSITION
    /// curve for almost every bone, so played directly they would snap caicos's
    /// bones onto the wolf's offsets - i.e. force wolf proportions onto a different
    /// dog and tear the mesh off its bind pose. The bones also use different local
    /// axes (Malbers/3ds Max runs bones down local -X, Blender down local +Y), so
    /// the raw local rotations land ~90 degrees out per joint.
    ///
    /// So we transfer MOTION rather than transforms: for every bone, the source
    /// clip's world-space rotation delta from the wolf's own rest pose is applied
    /// to caicos's rest pose. Bone offsets are never written, so caicos keeps its
    /// own proportions; only the root (and any bone the source genuinely
    /// translates) gets a position curve, scaled by the measured size ratio.
    ///
    /// Both rest poses are read from the imported models themselves, so
    /// re-exporting caicos.glb and re-running the setup is all that's needed after
    /// a rig change - there is nothing to hand-maintain.
    /// </summary>
    public static class CaicosRetarget
    {
        // Bump to force DogPlayerSetup to rebake the clips after changing the math
        public const int RetargetVersion = 9;

        public const string CaicosModelGuid = "fce7057b86bb34da89a047199b66035b"; // Assets/Models/caicos.glb
        public const string WolfModelGuid = "08e48789449aae64095cc114539cb217";   // Wolf Lite v2.fbx
        public const string OutputFolder = "Assets/Animations/Player/Caicos";

        // A key is dropped when it sits this close to the straight line between its
        // neighbours. 0.0015 on a quaternion component is well under 0.2 degrees.
        private const float RotationKeyTolerance = 0.0015f;
        private const float PositionKeyTolerance = 0.0005f;
        // Below this a bone is considered not to translate at all, so it keeps its
        // own rest offset - which is what preserves caicos's proportions.
        private const float StaticPositionTolerance = 0.0002f;

        private const int MinSampleRate = 30;

        /// <summary>
        /// Corrections folded into caicos's reference pose before the motion is
        /// transferred, as a pitch (degrees) about the model's left-right axis.
        /// Positive tips the bone's far end DOWN. Because they change the pose the
        /// motion is measured against, every baked clip inherits them while
        /// keeping whatever the source animates on top.
        ///
        /// Adjusting a bone alone moves everything below it (they follow their
        /// parent) while leaving their own orientation intact; withDescendants
        /// rotates the whole branch rigidly instead.
        /// </summary>
        private static readonly (string bone, float pitch, bool withDescendants)[] RestPoseAdjustments =
        {
            // The Malbers clips hold the mouth open and caicos is modelled with it
            // open too, so the gap closes more slowly than it looks: 20 degrees
            // still reads as a snarl, 45 shuts it completely and 50 pushes a fang
            // through the lip. 25 leaves it slightly open, which is the look we
            // want. Tongue rides along with the jaw.
            ("Jaw", -25f, true),
            // The wolf's clips carry the head higher and further back than caicos
            // was modelled with. Measured against her own rest pose, the baked
            // idle lifts the head 11 degrees and tucks the muzzle 10 - put both
            // back so the in-game carriage matches the model.
            ("Neck", 11f, false),
            ("Head", -10f, true),
        };

        // Caicos's lower legs are proportionally longer than the wolf's (the
        // hock-to-paw segment is 1.55x), so transferring joint angles alone lands
        // the paws somewhere else - they float and skate. Each leg is solved so
        // the paw goes where the source clip put it instead.
        private const int IkIterations = 20;
        private const float IkDamping = 0.75f;
        private const float IkTolerance = 0.001f;

        internal sealed class IkChain
        {
            public int[] Joints;     // rotated to reach the target, root first
            public int End;          // the paw, kept at the orientation the transfer gave it
            public int WolfHip;
            public int WolfEnd;
        }

        private static readonly (string[] joints, string end)[] LegChains =
        {
            (new[] { "L Thigh", "L Calf", "L HorseLink" }, "L Foot"),
            (new[] { "R Thigh", "R Calf", "R HorseLink" }, "R Foot"),
            (new[] { "L UpperArm", "L Forearm" }, "L Hand"),
            (new[] { "R UpperArm", "R Forearm" }, "R Hand"),
        };

        /// <summary>
        /// Live rig instances plus their rest pose, held for the duration of a
        /// batch so 20-odd clips don't each pay for instantiating two models.
        /// </summary>
        public sealed class Session : IDisposable
        {
            internal Rig Wolf;
            internal Rig Caicos;
            internal IkChain[] Chains = Array.Empty<IkChain>();
            /// <summary>caicos size / wolf size, from the rest poses (see MeasureSize).</summary>
            public float SizeRatio { get; internal set; }

            public void Dispose()
            {
                Wolf?.Dispose();
                Caicos?.Dispose();
                Wolf = null;
                Caicos = null;
            }
        }

        internal sealed class Rig : IDisposable
        {
            public GameObject Instance;
            public Transform Root;
            public Transform[] Bones;          // parent-first, excludes the root itself
            public string[] Paths;
            public int[] ParentIndex;          // -1 = the model root
            public Dictionary<string, int> IndexByName;

            public Quaternion[] RestWorldRot;
            public Vector3[] RestWorldPos;
            public Quaternion[] RestLocalRot;
            public Vector3[] RestLocalPos;
            public float[] RestWorldScale;     // accumulated uniform scale at the bone

            public void Dispose()
            {
                if (Instance != null)
                    UnityEngine.Object.DestroyImmediate(Instance);
                Instance = null;
            }
        }

        /// <summary>
        /// Opens a retarget session. Returns null (and logs) if either model is
        /// missing or the two rigs have no bones in common.
        /// </summary>
        public static Session BeginSession()
        {
            var wolfPrefab = LoadModelPrefab(WolfModelGuid, "Wolf Lite v2.fbx");
            var caicosPrefab = LoadModelPrefab(CaicosModelGuid, "caicos.glb");
            if (wolfPrefab == null || caicosPrefab == null)
                return null;

            var session = new Session
            {
                Wolf = BuildRig(wolfPrefab),
                Caicos = BuildRig(caicosPrefab)
            };

            int shared = session.Caicos.IndexByName.Keys.Count(session.Wolf.IndexByName.ContainsKey);
            if (shared < 8)
            {
                Debug.LogError($"[CaicosRetarget] The rigs share only {shared} bone names - caicos.glb does not look like it is built on the Wolf Lite skeleton. Aborting.");
                session.Dispose();
                return null;
            }

            session.SizeRatio = MeasureSize(session.Caicos) / Mathf.Max(1e-4f, MeasureSize(session.Wolf));

            var missing = session.Wolf.IndexByName.Keys.Where(n => !session.Caicos.IndexByName.ContainsKey(n)).ToList();
            if (missing.Count > 0)
                Debug.LogWarning($"[CaicosRetarget] {missing.Count} wolf bone(s) have no caicos counterpart and will not be animated: {string.Join(", ", missing)}");

            ApplyRestPoseAdjustments(session.Caicos);
            session.Chains = BuildIkChains(session);

            Debug.Log($"[CaicosRetarget] Rigs matched: {shared} shared bones, size ratio caicos/wolf = {session.SizeRatio:F3}, {session.Chains.Length} leg chains solved by IK.");
            return session;
        }

        /// <summary>
        /// Rotates a bone and its whole subtree in the reference pose. Only the
        /// world rest rotations move: the motion transfer measures against them,
        /// so the correction ends up in every clip, and bone offsets (and with
        /// them the model's proportions) are untouched.
        /// </summary>
        private static void ApplyRestPoseAdjustments(Rig rig)
        {
            foreach (var (boneName, pitch, withDescendants) in RestPoseAdjustments)
            {
                if (!rig.IndexByName.TryGetValue(boneName, out int root))
                {
                    Debug.LogWarning($"[CaicosRetarget] Rest pose adjustment skipped - no '{boneName}' bone in the model.");
                    continue;
                }

                var correction = Quaternion.AngleAxis(pitch, Vector3.right);
                if (withDescendants)
                {
                    foreach (int b in Subtree(rig, root))
                        rig.RestWorldRot[b] = correction * rig.RestWorldRot[b];
                }
                else
                {
                    rig.RestWorldRot[root] = correction * rig.RestWorldRot[root];
                }

                Debug.Log($"[CaicosRetarget] Reference pose: '{boneName}'{(withDescendants ? " and its subtree" : "")} pitched {pitch:F1} degrees.");
            }
        }

        private static IEnumerable<int> Subtree(Rig rig, int root)
        {
            yield return root;
            for (int b = root + 1; b < rig.Bones.Length; b++)
            {
                // Parent-first ordering means a subtree is contiguous only until
                // the first bone whose parent chain leaves it, so walk up to check
                for (int p = rig.ParentIndex[b]; p >= 0; p = rig.ParentIndex[p])
                {
                    if (p == root)
                    {
                        yield return b;
                        break;
                    }
                }
            }
        }

        private static IkChain[] BuildIkChains(Session session)
        {
            var chains = new List<IkChain>();
            foreach (var (joints, end) in LegChains)
            {
                if (!session.Caicos.IndexByName.TryGetValue(end, out int endIndex) ||
                    !session.Wolf.IndexByName.TryGetValue(end, out int wolfEnd) ||
                    !session.Wolf.IndexByName.TryGetValue(joints[0], out int wolfHip) ||
                    joints.Any(j => !session.Caicos.IndexByName.ContainsKey(j)))
                {
                    Debug.LogWarning($"[CaicosRetarget] Leg chain ending at '{end}' is incomplete - that leg is left to the plain angle transfer.");
                    continue;
                }

                chains.Add(new IkChain
                {
                    Joints = joints.Select(j => session.Caicos.IndexByName[j]).ToArray(),
                    End = endIndex,
                    WolfHip = wolfHip,
                    WolfEnd = wolfEnd
                });
            }
            return chains.ToArray();
        }

        /// <summary>
        /// Bakes <paramref name="source"/> onto the caicos rig and writes it to
        /// <paramref name="outputPath"/>, overwriting any previous bake.
        /// </summary>
        public static AnimationClip RetargetClip(Session session, AnimationClip source, string outputPath)
        {
            if (session == null || source == null)
                return null;

            var wolf = session.Wolf;
            var caicos = session.Caicos;

            int frameCount = Mathf.Max(2, Mathf.CeilToInt(source.length * Mathf.Max(MinSampleRate, source.frameRate)) + 1);
            float step = source.length / (frameCount - 1);

            int boneCount = caicos.Bones.Length;
            var localRot = new Quaternion[boneCount][];
            var localPos = new Vector3[boneCount][];
            var translates = new bool[boneCount];
            for (int b = 0; b < boneCount; b++)
            {
                localRot[b] = new Quaternion[frameCount];
                localPos[b] = new Vector3[frameCount];
            }

            // Retargeted world rotations for the current frame, needed to turn a
            // bone's world rotation back into a local one (parent-first order
            // guarantees the parent is already resolved).
            var worldRot = new Quaternion[boneCount];
            var deltas = new Quaternion[boneCount];
            var wolfIndex = Enumerable.Range(0, boneCount).Select(b => WolfIndexFor(session, b)).ToArray();

            for (int f = 0; f < frameCount; f++)
            {
                source.SampleAnimation(wolf.Instance, f * step);

                for (int b = 0; b < boneCount; b++)
                {
                    int w = wolfIndex[b];
                    int parent = caicos.ParentIndex[b];

                    // A bone the wolf rig doesn't have (the skinned mesh node, or
                    // anything caicos added) rides along with its parent rather
                    // than holding still - holding still would counter-rotate it
                    // out of the pose.
                    deltas[b] = w >= 0
                        ? wolf.Bones[w].rotation * Quaternion.Inverse(wolf.RestWorldRot[w])
                        : parent < 0 ? Quaternion.identity : deltas[parent];

                    worldRot[b] = deltas[b] * caicos.RestWorldRot[b];

                    var parentWorld = parent < 0 ? Quaternion.identity : worldRot[parent];
                    var local = Quaternion.Inverse(parentWorld) * worldRot[b];

                    // Keep the quaternion on the same hemisphere as the previous
                    // frame or the curve takes the long way round mid-clip
                    if (f > 0 && Quaternion.Dot(local, localRot[b][f - 1]) < 0f)
                        local = new Quaternion(-local.x, -local.y, -local.z, -local.w);
                    localRot[b][f] = local;

                    // Position: bones keep their own rest offset (that is what
                    // preserves caicos's proportions). Only bones the source
                    // genuinely translates get a curve, and only by the delta.
                    Vector3 pos = caicos.RestLocalPos[b];
                    if (w >= 0)
                    {
                        Vector3 sourceDelta = wolf.Bones[w].localPosition - wolf.RestLocalPos[w];
                        if (sourceDelta.sqrMagnitude > StaticPositionTolerance * StaticPositionTolerance)
                        {
                            translates[b] = true;
                            int wParent = wolf.ParentIndex[w];
                            int cParent = caicos.ParentIndex[b];
                            // Take the delta out of the wolf's parent frame into
                            // model space, then into caicos's parent frame
                            var modelDelta = (wParent < 0 ? Quaternion.identity : wolf.RestWorldRot[wParent]) * sourceDelta;
                            float parentScale = cParent < 0 ? 1f : caicos.RestWorldScale[cParent];
                            var localDelta = Quaternion.Inverse(cParent < 0 ? Quaternion.identity : caicos.RestWorldRot[cParent])
                                             * (modelDelta * session.SizeRatio);
                            pos += localDelta / Mathf.Max(1e-4f, parentScale);
                        }
                    }
                    localPos[b][f] = pos;
                }

                // Plant the paws. The angle transfer alone puts them wherever
                // caicos's longer lower legs happen to reach; drive them instead
                // to where the source clip had them, which is what stops the
                // floating and skating.
                if (session.Chains.Length > 0)
                {
                    ApplyPose(caicos, localRot, localPos, f);
                    SolveLegs(session);
                    for (int b = 0; b < boneCount; b++)
                    {
                        var solved = caicos.Bones[b].localRotation;
                        if (f > 0 && Quaternion.Dot(solved, localRot[b][f - 1]) < 0f)
                            solved = new Quaternion(-solved.x, -solved.y, -solved.z, -solved.w);
                        localRot[b][f] = solved;
                    }
                }
            }

            var clip = new AnimationClip
            {
                name = System.IO.Path.GetFileNameWithoutExtension(outputPath),
                frameRate = Mathf.Max(MinSampleRate, source.frameRate),
                wrapMode = source.wrapMode
            };

            int rotationCurves = 0, positionCurves = 0;
            for (int b = 0; b < boneCount; b++)
            {
                string path = caicos.Paths[b];

                // Every bone the wolf rig drives gets a rotation curve, even when
                // the result is constant - a constant curve collapses to two keys,
                // and leaving it out would make the pose depend on the Animator
                // restoring default values when a state doesn't touch that bone.
                if (wolfIndex[b] >= 0)
                {
                    WriteQuaternionCurves(clip, path, localRot[b], step);
                    rotationCurves++;
                }

                if (translates[b] && IsPositionAnimated(localPos[b], caicos.RestLocalPos[b]))
                {
                    WriteVectorCurves(clip, path, localPos[b], step);
                    positionCurves++;
                }
            }

            var settings = AnimationUtility.GetAnimationClipSettings(source);
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            var events = AnimationUtility.GetAnimationEvents(source);
            if (events != null && events.Length > 0)
                AnimationUtility.SetAnimationEvents(clip, events);

            EnsureFolder(OutputFolder);
            AssetDatabase.DeleteAsset(outputPath);
            AssetDatabase.CreateAsset(clip, outputPath);

            Debug.Log($"[CaicosRetarget] {source.name} -> {clip.name}: {frameCount} frames, {rotationCurves} rotation / {positionCurves} position curve sets.");
            return clip;
        }

        private static void ApplyPose(Rig rig, Quaternion[][] localRot, Vector3[][] localPos, int frame)
        {
            for (int b = 0; b < rig.Bones.Length; b++)
            {
                rig.Bones[b].localRotation = localRot[b][frame];
                rig.Bones[b].localPosition = localPos[b][frame];
            }
        }

        /// <summary>
        /// Damped CCD per leg, run on the live rig so Unity does the forward
        /// kinematics.
        ///
        /// The target is built from the hip-to-paw VECTOR, not the paw's position:
        /// take how far the source swung that vector from its own rest, and how
        /// much it extended as a fraction of its rest length, and apply both to
        /// caicos's rest leg. Translating the paw instead (an earlier attempt)
        /// dragged it toward the body, because caicos's leg is longer than the
        /// wolf's and the difference showed up as a shortfall the solver had to
        /// swallow by folding the leg. This way rest maps to rest exactly, a fully
        /// extended stride stays fully extended, and only the swing and reach
        /// come from the source.
        /// </summary>
        private static void SolveLegs(Session session)
        {
            var caicos = session.Caicos;
            var wolf = session.Wolf;

            foreach (var chain in session.Chains)
            {
                var end = caicos.Bones[chain.End];
                var hip = caicos.Bones[chain.Joints[0]];

                var wolfRestLeg = wolf.RestWorldPos[chain.WolfEnd] - wolf.RestWorldPos[chain.WolfHip];
                var wolfLeg = wolf.Bones[chain.WolfEnd].position - wolf.Bones[chain.WolfHip].position;
                var restLeg = caicos.RestWorldPos[chain.End] - caicos.RestWorldPos[chain.Joints[0]];
                if (wolfRestLeg.sqrMagnitude < 1e-8f || restLeg.sqrMagnitude < 1e-8f)
                    continue;

                var swing = Quaternion.FromToRotation(wolfRestLeg, wolfLeg);
                float extension = wolfLeg.magnitude / wolfRestLeg.magnitude;
                var target = hip.position + swing * restLeg.normalized * (restLeg.magnitude * extension);

                // The paw's orientation is the source's, not the solver's
                var endRotation = end.rotation;

                for (int iteration = 0; iteration < IkIterations; iteration++)
                {
                    if ((end.position - target).sqrMagnitude < IkTolerance * IkTolerance)
                        break;

                    for (int j = chain.Joints.Length - 1; j >= 0; j--)
                    {
                        var joint = caicos.Bones[chain.Joints[j]];
                        var toEnd = end.position - joint.position;
                        var toTarget = target - joint.position;
                        if (toEnd.sqrMagnitude < 1e-8f || toTarget.sqrMagnitude < 1e-8f)
                            continue;

                        var correction = Quaternion.FromToRotation(toEnd, toTarget);
                        joint.rotation = Quaternion.Slerp(Quaternion.identity, correction, IkDamping) * joint.rotation;
                    }
                }

                end.rotation = endRotation;
            }
        }

        /// <summary>
        /// Lowest foot height and pelvis height over a clip, sampled on the caicos
        /// rig. Used to check that retargeted feet still meet the ground - caicos's
        /// legs are proportioned differently, so the wolf's joint angles can leave
        /// the dog floating or sunk.
        /// </summary>
        public static (float minFoot, float maxFoot, float meanFoot) MeasureFootHeights(Session session, AnimationClip clip, int samples = 24)
        {
            var caicos = session?.Caicos;
            if (caicos == null || clip == null)
                return (0f, 0f, 0f);

            var feet = new[] { "L Foot", "R Foot", "L Hand", "R Hand" }
                .Where(caicos.IndexByName.ContainsKey)
                .Select(n => caicos.Bones[caicos.IndexByName[n]])
                .ToArray();
            if (feet.Length == 0)
                return (0f, 0f, 0f);

            float min = float.MaxValue, max = float.MinValue, sum = 0f;
            for (int i = 0; i <= samples; i++)
            {
                clip.SampleAnimation(caicos.Instance, clip.length * i / samples);
                float lowest = feet.Min(f => f.position.y);
                min = Mathf.Min(min, lowest);
                max = Mathf.Max(max, lowest);
                sum += lowest;
            }
            return (min, max, sum / (samples + 1));
        }

        /// <summary>Restores the caicos rig instance to its rest pose after sampling.</summary>
        public static void ResetCaicosPose(Session session)
        {
            var rig = session?.Caicos;
            if (rig == null)
                return;
            for (int i = 0; i < rig.Bones.Length; i++)
            {
                rig.Bones[i].localPosition = rig.RestLocalPos[i];
                rig.Bones[i].localRotation = rig.RestLocalRot[i];
            }
        }

        // ---------------------------------------------------------------- rigs

        private static GameObject LoadModelPrefab(string guid, string label)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                Debug.LogError($"[CaicosRetarget] {label} not found (guid {guid}).");
            return prefab;
        }

        private static Rig BuildRig(GameObject prefab)
        {
            var instance = UnityEngine.Object.Instantiate(prefab);
            instance.hideFlags = HideFlags.HideAndDontSave;
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;

            // SampleAnimation goes through an Animator when the model has one, and
            // an Avatar makes it treat the root bone's curve as root motion: the
            // Malbers model reports its CG frozen at rest while the clip actually
            // moves it 0.24 -> 2.10m. Strip the Animator so both rigs are sampled
            // literally, exactly as the curves are written.
            foreach (var animator in instance.GetComponentsInChildren<Animator>(true))
                UnityEngine.Object.DestroyImmediate(animator);

            var rig = new Rig { Instance = instance, Root = instance.transform };

            var bones = new List<Transform>();
            var parents = new List<int>();
            void Walk(Transform t, int parentIndex)
            {
                foreach (Transform child in t)
                {
                    int index = bones.Count;
                    bones.Add(child);
                    parents.Add(parentIndex);
                    Walk(child, index);
                }
            }
            Walk(rig.Root, -1);

            rig.Bones = bones.ToArray();
            rig.ParentIndex = parents.ToArray();
            rig.Paths = rig.Bones.Select(b => AnimationUtility.CalculateTransformPath(b, rig.Root)).ToArray();
            rig.IndexByName = new Dictionary<string, int>();
            for (int i = 0; i < rig.Bones.Length; i++)
                rig.IndexByName[rig.Bones[i].name] = i;

            rig.RestWorldRot = rig.Bones.Select(b => b.rotation).ToArray();
            rig.RestWorldPos = rig.Bones.Select(b => b.position).ToArray();
            rig.RestLocalRot = rig.Bones.Select(b => b.localRotation).ToArray();
            rig.RestLocalPos = rig.Bones.Select(b => b.localPosition).ToArray();
            rig.RestWorldScale = new float[rig.Bones.Length];
            for (int i = 0; i < rig.Bones.Length; i++)
            {
                float parentScale = rig.ParentIndex[i] < 0 ? 1f : rig.RestWorldScale[rig.ParentIndex[i]];
                rig.RestWorldScale[i] = parentScale * rig.Bones[i].localScale.x;
            }

            return rig;
        }

        private static int WolfIndexFor(Session session, int caicosIndex)
        {
            var name = session.Caicos.Bones[caicosIndex].name;
            return session.Wolf.IndexByName.TryGetValue(name, out int w) ? w : -1;
        }

        /// <summary>
        /// RMS distance of every bone from the rig's centroid - a scale measure
        /// that doesn't care which single bone you pick or how the dog is posed.
        /// </summary>
        private static float MeasureSize(Rig rig)
        {
            if (rig.Bones.Length == 0)
                return 1f;
            var points = rig.RestWorldPos;
            var centre = Vector3.zero;
            foreach (var p in points)
                centre += p;
            centre /= points.Length;
            float sum = 0f;
            foreach (var p in points)
                sum += (p - centre).sqrMagnitude;
            return Mathf.Sqrt(sum / points.Length);
        }

        // -------------------------------------------------------------- curves

        private static bool IsPositionAnimated(Vector3[] frames, Vector3 rest)
        {
            foreach (var p in frames)
            {
                if ((p - rest).sqrMagnitude > StaticPositionTolerance * StaticPositionTolerance)
                    return true;
            }
            return false;
        }

        private static void WriteQuaternionCurves(AnimationClip clip, string path, Quaternion[] frames, float step)
        {
            var components = new float[4][];
            for (int c = 0; c < 4; c++)
                components[c] = new float[frames.Length];
            for (int f = 0; f < frames.Length; f++)
            {
                components[0][f] = frames[f].x;
                components[1][f] = frames[f].y;
                components[2][f] = frames[f].z;
                components[3][f] = frames[f].w;
            }

            var keep = SelectKeyFrames(components, RotationKeyTolerance);
            string[] names = { "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
            for (int c = 0; c < 4; c++)
                SetCurve(clip, path, names[c], components[c], keep, step);
        }

        private static void WriteVectorCurves(AnimationClip clip, string path, Vector3[] frames, float step)
        {
            var components = new float[3][];
            for (int c = 0; c < 3; c++)
                components[c] = new float[frames.Length];
            for (int f = 0; f < frames.Length; f++)
            {
                components[0][f] = frames[f].x;
                components[1][f] = frames[f].y;
                components[2][f] = frames[f].z;
            }

            var keep = SelectKeyFrames(components, PositionKeyTolerance);
            string[] names = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z" };
            for (int c = 0; c < 3; c++)
                SetCurve(clip, path, names[c], components[c], keep, step);
        }

        /// <summary>
        /// Frame indices worth keeping: the first, the last, and any frame that
        /// strays from the straight line between its kept neighbours. The
        /// components are tested together so a quaternion's four curves always
        /// keep the same keys - dropping them independently would let the
        /// components drift out of sync between keys.
        /// </summary>
        private static List<int> SelectKeyFrames(float[][] components, float tolerance)
        {
            int n = components[0].Length;
            var keep = new List<int> { 0 };
            if (n == 1)
                return keep;

            int last = 0;
            for (int i = 1; i < n - 1; i++)
            {
                float t = (i - last) / (float)(i + 1 - last);
                bool needed = false;
                for (int c = 0; c < components.Length && !needed; c++)
                {
                    float predicted = Mathf.Lerp(components[c][last], components[c][i + 1], t);
                    needed = Mathf.Abs(components[c][i] - predicted) > tolerance;
                }
                if (needed)
                {
                    keep.Add(i);
                    last = i;
                }
            }
            keep.Add(n - 1);
            return keep;
        }

        private static void SetCurve(AnimationClip clip, string path, string property, float[] values, List<int> keep, float step)
        {
            var keys = new Keyframe[keep.Count];
            for (int k = 0; k < keep.Count; k++)
                keys[k] = new Keyframe(keep[k] * step, values[keep[k]]);

            var curve = new AnimationCurve(keys);
            for (int k = 0; k < keys.Length; k++)
                curve.SmoothTangents(k, 0f);

            var binding = EditorCurveBinding.FloatCurve(path, typeof(Transform), property);
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            var parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
