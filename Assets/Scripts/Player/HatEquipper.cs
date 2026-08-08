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

        private Transform anchor;
        private GameObject currentHat;
        private string currentId = "";
        private bool anchorAligned;

        private void Start()
        {
            ApplySelected();
        }

        /// <summary>Sync the worn hat with HatManager.SelectedId (idempotent).</summary>
        public void ApplySelected()
        {
            string id = HatManager.SelectedId;
            if (id == currentId && (currentHat != null || id.Length == 0))
                return;

            if (currentHat != null)
            {
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

            // No unload sweep here - browsing the selector must stay
            // hitch-free. GameManager sweeps unused assets on the Home/Playing
            // swaps, behind the screen fade's held black.
        }

        /// <summary>
        /// Death beat: the worn hat pops off and lands heavy. Called by
        /// PlayerRagdoll as it swaps the animated dog for the corpse. The next
        /// ApplySelected (home entry / level start) wears a fresh instance.
        /// </summary>
        public void DropHat()
        {
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

            // The fade into the game-over screen covers the cleanup
            Destroy(dropped, 6f);
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
            }

            if (!anchorAligned)
            {
                // The glb head bone's local axes are arbitrary; aligning the
                // anchor to the model root's frame once (idle pose) makes the
                // prefab offsets read as plain "up above the head / forward
                // over the nose" regardless of the rig's bone conventions.
                Transform modelRoot = FindChildByName(transform, ModelRootName);
                anchor.rotation = modelRoot != null ? modelRoot.rotation : transform.rotation;
                anchor.localPosition = Vector3.zero;
                anchor.localScale = Vector3.one;
                anchorAligned = true;
            }

            return anchor;
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
