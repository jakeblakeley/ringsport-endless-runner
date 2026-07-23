using System;
using UnityEngine;

namespace MalbersAnimations.IK
{
    [Serializable]
    public abstract class IKProcessor
    {
        [HideInInspector] public string name;
        [HideInInspector] public bool Active = true;

        [Tooltip("Weight Applied for the Processor")]
        [HideInInspector][Range(0, 1)] public float Weight = 1;

        [Tooltip("Target transform reference from the IK Set [Targets Array]. Index Value. Target applied to the Avatar IK Goal")]
        [Hide("showIndex")]
        [Min(-1)] public int TargetIndex = 0;

        [HideInInspector] public string AnimParameter;
        [HideInInspector] public int AnimParameterHash;

        [HideInInspector]
        [Tooltip("Tag to identify the IK Processor. Useful for the IK Manager to find a specific processor with the same tag")]
        public IKTag Tag;

        /// <summary>  Bone reference for the IK Processor.  </summary>
        public Transform Bone { get; set; }


        public virtual Transform GetTarget(IKSet set)
        {
            if (!RequireTargets) return null;

            /// Get the Target from the IK Set. If there's a Tag, get the Target from the IK Manager Global Targets with that Tag,
            if (Tag == null)
            {
                if (set.Targets.Length > TargetIndex)
                    return set.Targets[TargetIndex].Value;
            }
            else //if not, get the Target from the IK Set Targets Array with the Target Index
            {
                return set.Owner.GlobalTargets_Dic.TryGetValue(Tag, out var target) ? target : null;
            }
            return null;
        }

        public virtual Transform GetTarget(IKSet set, IKTag Tag)
        {
            if (Tag == null) return null;

            if (set.Owner == null)
            {
                Debug.LogWarning($"The IK Set <B>[{set.Name}]</B> has no Owner. Please assign the IK Set to an IK Manager.", set.Owner);
                return null;
            }

            return set.Owner.GetTargetByTag(Tag);
        }


        /// <summary> Tells the IK Manager if it needs Targets to check</summary>
        public abstract bool RequireTargets { get; }

        public virtual void Start(IKSet IKSet, Animator anim, int index)
        {
            Bone = GetTarget(IKSet);

            if (Bone == null && RequireTargets && Tag != null)
            {
                Debug.LogWarning($"The IK Set <B>[{IKSet.Name}]</B> has no Target set for the [IK Processor: {name}] with the Tag <B><color=white>{Tag.name}</Color></B>. " +
                    $"Please add a reference in the [Targets Global] list .", anim);
            }
        }

        /// <summary>
        /// Cache the initial values of the IK Processor. This is useful for the IK Manager to restore the original values of the IK Processor when it gets disabled or when the weight is zero.
        /// </summary>
        public virtual void CacheValue(IKSet iKSet, Animator anim, int index) { }

        public virtual void OnEnable(IKSet IKSet, Animator anim, int index) { }

        public virtual void OnDisable(IKSet IKSet, Animator anim, int index) { }

        public virtual void OnAnimatorIK(IKSet IKSet, Animator anim, int index, float weight, int layer) { }

        /// <summary> LateUpdate to call all IK Processors that need to be updated after the Animator IK  </summary>
        /// <param name="IKSet">IK Set</param>
        /// <param name="anim">Reference for the animator controller</param>
        /// <param name="index">index of the Processor in the IK Set List</param>
        /// <param name="weight">Weight of the Processor</param>
        public virtual void LateUpdate(IKSet IKSet, Animator anim, int index, float weight) { }
        public virtual void OnDrawGizmos(IKManager manager, IKSet IKSet, Animator anim, float weight) { }

        ///<summary> Verify if the IKProcessor is set correctly.If it needs some references </summary>
        public abstract void Verify(IKManager manager, IKSet set, Animator animator, int index);

        /// <summary> Process the Animation Curve in case there's one in the IK Processor</summary>
        public float GetProcessorAnimWeight(Animator animator)
            => AnimParameterHash != 0 ? animator.GetFloat(AnimParameterHash) : 1;

        internal virtual void OnSceneGUI(IKSet set, Animator animator, UnityEngine.Object target, int index) { }


        [HideInInspector] public bool showIndex;

        internal virtual void OnValidate(IKManager manager, IKSet set)
        {
            showIndex = RequireTargets && Tag == null;
        }
    }
    [System.Serializable] //MWC: Required so [SerializeReference] fields in subclasses don't trigger missing-attribute warning
    public abstract class IKProcessorOnAnimIK : IKProcessor
    {
        // public override RequireTargets => false;

        [Min(-1)]
        [Tooltip("Layer Index to apply the IK. If the Animator has more than one layer, you can specify in which layer you want to apply the IK. If the value is 0, it will be applied in the Base Layer.")]
        public int LayerIndex = 0;

        public override void OnAnimatorIK(IKSet set, Animator anim, int index, float weight, int layer)
        {
            if (LayerIndex > 0 && LayerIndex != layer) return;
            OnAnimatorIK(set, anim, index, weight);
        }

        public abstract void OnAnimatorIK(IKSet set, Animator anim, int index, float weight);

    }
}