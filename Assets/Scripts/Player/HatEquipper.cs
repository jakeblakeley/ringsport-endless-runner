using UnityEngine;
using RingSport.Core;

namespace RingSport.Player
{
    /// <summary>
    /// Wears the selected hat on the dog's head. One anchor is created under
    /// the Head bone so the hat rides every animation; each hat prefab's own
    /// root position/rotation/scale IS its fitting offset, so fits are tuned
    /// on the prefab, not here. Hats load from Resources on demand and the
    /// previous hat's assets are released, so only the worn hat occupies
    /// memory. Added to the Player prefab by Tools > RingSport > Setup Hats.
    /// </summary>
    public class HatEquipper : MonoBehaviour
    {
        private const string AnchorName = "HatAnchor";
        private const string HeadBoneName = "Head";
        private const string ModelRootName = "Dog Model";
        private const string LeftEarBoneName = "L Ear";
        private const string RightEarBoneName = "R Ear";

        private Transform anchor;
        private GameObject currentHat;
        private GameObject droppedHat; // last death's physics prop, cleared early on run (re)start
        private string currentId = "";
        private bool anchorAligned;

        private Transform leftEar;
        private Transform rightEar;
        private Vector3 leftEarScale = Vector3.one;
        private Vector3 rightEarScale = Vector3.one;
        private bool earsHidden;

        /// <summary>Live worn hat instance (the editor fit tuner edits it in place). Null when bare.</summary>
        public Transform WornHat => currentHat != null ? currentHat.transform : null;

        /// <summary>Current ear-hide state (the editor fit tuner previews the HideEars flag through this).</summary>
        public bool EarsHidden => earsHidden;

        /// <summary>Editor fit-tuner override: flip the ear bones live without touching the catalog.</summary>
        public void SetEarsHiddenLive(bool hide)
        {
            SetEarsHidden(hide);
        }

        private void Start()
        {
            ApplySelected();
        }

        private void LateUpdate()
        {
            // Glue the anchor against the pose the player actually sees. At
            // Start the animator hasn't evaluated yet, so aligning there uses
            // the glb bind pose - which sits most of a right angle away from
            // the animated idle at the head bone, tipping every hat with it.
            // The first LateUpdate runs after the animation pass has posed the
            // dog, so the alignment (and the worn hat) lands upright.
            if (!anchorAligned && anchor != null)
                AlignAnchor();

            // Enforced after animation in case any clip writes ear scales
            if (earsHidden)
            {
                if (leftEar != null)
                    leftEar.localScale = Vector3.zero;
                if (rightEar != null)
                    rightEar.localScale = Vector3.zero;
            }
        }

        /// <summary>Sync the worn hat with HatManager.SelectedId (idempotent).</summary>
        public void ApplySelected()
        {
            // Sweep before trusting any cached state: the anchor must hold
            // exactly the tracked hat, nothing else
            PurgeStrayHats();

            string id = HatManager.SelectedId;
            if (id == currentId && (currentHat != null || id.Length == 0))
                return;

            if (currentHat != null)
            {
                // Detach before the deferred Destroy so the dying instance
                // never counts as an anchor child for a same-frame sweep
                currentHat.transform.SetParent(null);
                Destroy(currentHat);
                currentHat = null;
            }
            currentId = "";

            if (id.Length > 0)
            {
                GameObject prefab = HatManager.LoadHatPrefab(id);
                Transform mount = prefab != null ? EnsureAnchor() : null;
                if (prefab != null && mount != null)
                {
                    // worldPositionStays=false keeps the prefab root's authored
                    // local TRS - that root transform is the per-hat fitting offset
                    currentHat = Instantiate(prefab, mount, false);
                    currentId = id;
                }
            }

            // Enclosing hats collapse the ear bones so the ears don't clip
            // through the shell; everything else keeps them
            SetEarsHidden(HatManager.HideEarsFor(currentId));

            // No unload sweep here - browsing the selector must stay
            // hitch-free. GameManager sweeps unused assets on the Home/Playing
            // swaps, behind the screen fade's held black.
        }

        /// <summary>
        /// Destroys anything under the hat anchor that this component didn't
        /// put there. Strays are real: an editor prefab-apply once serialized
        /// a live worn hat (plus the runtime anchor) into Player.prefab, and
        /// every dog after that wore the baked hat under the equipped one -
        /// so every wear path sweeps the anchor before trusting it.
        /// </summary>
        private void PurgeStrayHats()
        {
            if (anchor == null)
            {
                // Nothing equipped yet this session, but the prefab itself may
                // carry a baked anchor with baked contents - adopt it so its
                // strays get swept (and AlignAnchor later fixes its rotation)
                Transform head = FindChildByName(transform, HeadBoneName);
                Transform baked = head != null ? head.Find(AnchorName) : null;
                if (baked == null)
                    return;
                anchor = baked;
            }

            for (int i = anchor.childCount - 1; i >= 0; i--)
            {
                Transform child = anchor.GetChild(i);
                if (currentHat != null && child == currentHat.transform)
                    continue;

                GameLog.Warn($"[HatEquipper] Stray '{child.name}' under the hat anchor - removing.");
                child.SetParent(null);
                Destroy(child.gameObject);
            }
        }

        /// <summary>
        /// Death beat: the worn hat pops off and lands heavy. Called by
        /// PlayerRagdoll as it swaps the animated dog for the corpse. The next
        /// ApplySelected (home entry / level start) wears a fresh instance.
        /// </summary>
        public void DropHat()
        {
            // The corpse swap runs next; leave the ears how nature made them
            // so the next wear cycle starts clean
            SetEarsHidden(false);

            if (currentHat == null)
            {
                currentId = "";
                return;
            }

            GameObject dropped = currentHat;
            currentHat = null;
            currentId = "";

            dropped.transform.SetParent(null, true);

            var box = dropped.AddComponent<BoxCollider>();
            FitColliderToMeshes(dropped, box);

            var body = dropped.AddComponent<Rigidbody>();
            body.mass = 8f; // heavy: thuds down instead of bouncing around
            body.linearDamping = 0.2f;
            body.angularDamping = 2.5f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            // Up and off the head first, with a touch of the death knockback
            body.linearVelocity = new Vector3(Random.Range(-0.6f, 0.6f), 4.5f, -2.5f);
            body.angularVelocity = Random.insideUnitSphere * 4f;

            // Same rule as the ragdoll: never fight the player's invisible capsule
            foreach (var playerCollider in GetComponentsInChildren<Collider>(true))
                Physics.IgnoreCollision(box, playerCollider, true);

            // The fade into the game-over screen covers the cleanup; a quick
            // retry clears it sooner via ClearDroppedHat
            Destroy(dropped, 6f);
            droppedHat = dropped;
        }

        /// <summary>
        /// Immediately removes the last death's dropped hat. Runs when a run
        /// (re)starts - a fast retry beats the drop's own 6s cleanup, and the
        /// resumed run would drive right past the corpse hat otherwise.
        /// </summary>
        public void ClearDroppedHat()
        {
            if (droppedHat != null)
            {
                Destroy(droppedHat);
                droppedHat = null;
            }
        }

        /// <summary>Hat prefabs carry no colliders - box up their combined mesh bounds.</summary>
        private static void FitColliderToMeshes(GameObject root, BoxCollider box)
        {
            var filters = root.GetComponentsInChildren<MeshFilter>();
            Bounds local = default;
            bool first = true;

            foreach (var filter in filters)
            {
                if (filter.sharedMesh == null)
                    continue;

                Bounds meshBounds = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = meshBounds.center + Vector3.Scale(meshBounds.extents,
                        new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    Vector3 point = root.transform.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    if (first)
                    {
                        local = new Bounds(point, Vector3.zero);
                        first = false;
                    }
                    else
                    {
                        local.Encapsulate(point);
                    }
                }
            }

            if (first)
            {
                box.size = Vector3.one * 0.25f;
                return;
            }

            box.center = local.center;
            box.size = Vector3.Max(local.size, Vector3.one * 0.05f);
        }

        private Transform EnsureAnchor()
        {
            if (anchor != null)
                return anchor;

            Transform head = FindChildByName(transform, HeadBoneName);
            if (head == null)
            {
                GameLog.Warn("[HatEquipper] No 'Head' bone found under the player - hat not equipped.");
                return null;
            }

            anchor = head.Find(AnchorName);
            if (anchor == null)
            {
                anchor = new GameObject(AnchorName).transform;
                anchor.SetParent(head, false);
                anchor.localPosition = Vector3.zero;
                anchor.localRotation = Quaternion.identity;
                anchor.localScale = Vector3.one;
            }

            // Alignment waits for LateUpdate (see above) so it samples the
            // animated pose, not the bind pose
            return anchor;
        }

        /// <summary>
        /// Scales the ear bones to zero (and back) for hats that fully enclose
        /// the skull. Always writes the bones rather than trusting the cached
        /// flag: a prefab-apply once baked the hidden (zero) ear scales into
        /// Player.prefab, and a flag-matches early-out left them stuck at zero
        /// through every wear cycle after a death.
        /// </summary>
        private void SetEarsHidden(bool hide)
        {
            if (leftEar == null)
            {
                leftEar = FindChildByName(transform, LeftEarBoneName);
                if (leftEar != null)
                    leftEarScale = SafeEarScale(leftEar.localScale);
            }
            if (rightEar == null)
            {
                rightEar = FindChildByName(transform, RightEarBoneName);
                if (rightEar != null)
                    rightEarScale = SafeEarScale(rightEar.localScale);
            }

            if (leftEar == null && rightEar == null)
            {
                GameLog.Warn("[HatEquipper] No ear bones found - HideEars ignored.");
                return;
            }

            if (leftEar != null)
                leftEar.localScale = hide ? Vector3.zero : leftEarScale;
            if (rightEar != null)
                rightEar.localScale = hide ? Vector3.zero : rightEarScale;
            earsHidden = hide;
        }

        /// <summary>
        /// The restore scale is captured from the bone the first time we touch
        /// it. If that capture reads (near) zero - the ears were already hidden,
        /// e.g. baked that way into the prefab - restoring it would be a no-op
        /// forever, so fall back to the model's natural full size.
        /// </summary>
        private static Vector3 SafeEarScale(Vector3 captured)
        {
            return captured.sqrMagnitude < 0.0001f ? Vector3.one : captured;
        }

        /// <summary>
        /// The glb head bone's local axes are arbitrary; aligning the anchor
        /// to the model root's frame once (in the animated idle) makes the
        /// prefab offsets read as plain "up above the head / forward over the
        /// nose" regardless of the rig's bone conventions.
        /// </summary>
        private void AlignAnchor()
        {
            Transform modelRoot = FindChildByName(transform, ModelRootName);
            anchor.rotation = modelRoot != null ? modelRoot.rotation : transform.rotation;
            anchor.localPosition = Vector3.zero;
            anchor.localScale = Vector3.one;
            anchorAligned = true;
        }

        private static Transform FindChildByName(Transform root, string name)
        {
            if (root.name == name)
                return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindChildByName(root.GetChild(i), name);
                if (found != null)
                    return found;
            }

            return null;
        }
    }
}
