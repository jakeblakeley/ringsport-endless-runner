using System.Collections.Generic;
using UnityEngine;

namespace MalbersAnimations.Utilities
{
    public class TriggerTarget : MonoBehaviour
    {
        public HashSet<TriggerProxy> Proxies = new();
        private Collider[] _cachedCollider;

        //MWC: true when a TriggerProxy auto-added this component (vs. a user placing it manually).
        //Only auto-added targets self-destroy once no proxies reference them, so we never delete a user's component.
        public bool AutoAdded { get; internal set; }

        private void Awake()
        {
            //hideFlags = HideFlags.HideInInspector;
            _cachedCollider = GetComponents<Collider>();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_cachedCollider == null || _cachedCollider.Length == 0)
            {
                Debug.LogWarning($"TriggerTarget on {gameObject.name} has no attached collider!", this);
                enabled = false;
            }
#endif
        }

        protected virtual void OnEnable()
        {
            TriggerRegistry.RegisterTarget(this);

            foreach (var col in _cachedCollider)
                TriggerRegistry.RegisterCollider(col, this);
        }

        private void OnDisable()
        {
            if (Proxies != null)
            {
                foreach (var p in Proxies)
                {
                    if (!p) continue;

                    foreach (var col in _cachedCollider) p.TriggerExit(col, false);
                }
                Proxies.Clear();
            }


            TriggerRegistry.UnregisterTarget(this);

            foreach (var col in _cachedCollider)
                TriggerRegistry.UnregisterCollider(col);
        }


        public void AddProxy(TriggerProxy trigger) => Proxies.Add(trigger);

        public void RemoveProxy(TriggerProxy trigger)
        {
            Proxies.Remove(trigger);

            //MWC: clean up the auto-added component once nothing references it, so colliders don't accumulate
            //an orphan TriggerTarget (and a registry entry) for their whole lifetime. Never touch user-added ones.
            if (AutoAdded && Proxies.Count == 0)
                Destroy(this);
        }
    }
}