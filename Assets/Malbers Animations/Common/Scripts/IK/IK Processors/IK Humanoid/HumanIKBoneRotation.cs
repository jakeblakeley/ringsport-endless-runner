using UnityEngine;
using MalbersAnimations.Scriptables;




#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.IK
{
    [System.Serializable]
    [AddTypeMenu("Humanoid/IK Human Bone Offset <Rotation>")]
    public class HumanIKBoneRotation : IKProcessorOnAnimIK
    {
        public enum RotationOffsetType
        {
            [InspectorName("Local Rotation Additive")]
            LocalAdditive,
            [InspectorName("Local Rotation Override")]
            LocalOverride,
            [InspectorName("Root Relative Local Rotation Additive")]
            RootRelativeRotationAdditive,
            [InspectorName("Root Relative Local Rotation Override")]
            RootRelativeRotationOverride,
            [InspectorName("Rotation Relative to [Tag]")]
            WorldRotation,
        }

        public RotationOffsetType rotationType;
        [Hide(nameof(rotationType), (int)RotationOffsetType.WorldRotation)]
        public IKTag TargetTag;

        [SearcheableEnum] public HumanBodyBones humanBone;

        private Transform TargetWorldRotation;

        [Tooltip("Rotation Offset applied to the bone")]
        public Vector3Reference Offset;

        public bool gizmos = true;

        public override bool RequireTargets => false;

        public override void Start(IKSet IKSet, Animator anim, int index)
        {
            Bone = anim.GetBoneTransform(humanBone);
            TargetWorldRotation = GetTarget(IKSet, TargetTag);
        }

        public override void OnAnimatorIK(IKSet set, Animator anim, int index, float weight)
        {
            var root = anim.transform;
            var OffsetRot = Quaternion.Euler(Offset.Value);
            var InverseRot = Quaternion.Inverse(Bone.parent.rotation); //This is the Bone Rotation in world coordinates

            Quaternion finalRotation = Quaternion.identity;

            switch (rotationType)
            {
                case RotationOffsetType.LocalAdditive:
                    finalRotation = Bone.localRotation * OffsetRot;
                    break;
                case RotationOffsetType.LocalOverride:
                    finalRotation = OffsetRot;
                    break;
                case RotationOffsetType.WorldRotation:
                    if (TargetWorldRotation == null)
                    {
                        Debug.LogWarning($"<B>[IK Processor: {name}].</B>  Target failed in {TargetTag.name}");
                        Active = false;
                        return;
                    }
                    var TargetRelative = Quaternion.identity;
                    var Target = TargetWorldRotation;

                    if (Target != null)
                        TargetRelative = Target.rotation;

                    finalRotation = InverseRot * TargetRelative * OffsetRot;
                    break;
                case RotationOffsetType.RootRelativeRotationOverride:
                    finalRotation = InverseRot * root.rotation * OffsetRot;
                    break;

                case RotationOffsetType.RootRelativeRotationAdditive:
                    finalRotation = Bone.localRotation * root.rotation * OffsetRot;
                    break;
                default:
                    break;
            }

            if (!(System.Single.IsNaN(finalRotation.x) || System.Single.IsNaN(finalRotation.y) || System.Single.IsNaN(finalRotation.z)))
            {
                var result = Quaternion.Slerp(Bone.localRotation, finalRotation, weight);
                anim.SetBoneLocalRotation(humanBone, result);
            }
        }

        public override void Verify(IKManager manager, IKSet set, Animator animator, int index)
        {
            if (animator.GetBoneTransform(humanBone) == null)
            {
                Debug.LogWarning($"<B>[IK Processor: {name}].</B> The Bone [{humanBone}] is not valid on the Avatar");
                return;
            }
            if (rotationType == RotationOffsetType.WorldRotation && TargetTag == null)
            {
                Debug.LogWarning($"<B>[IK Processor: {name}].</B>  Needs a Target Tag");
                return;
            }

            Debug.Log($"<B>[IK Processor: {name}]</B>  <color=yellow>[OK]</color>");
        }



#if UNITY_EDITOR
        internal override void OnSceneGUI(IKSet set, Animator animator, UnityEngine.Object Target, int index)
        {
            if (gizmos)
            {
                Handles.color = Color.yellow;

                if (Bone != null)
                    Handles.SphereHandleCap(0, Bone.position, Quaternion.identity, 0.04f, EventType.Repaint);

                foreach (Transform child in Bone)
                {
                    Handles.SphereHandleCap(0, child.position, Quaternion.identity, 0.02f, EventType.Repaint);
                    Handles.DrawLine(Bone.position, child.position);
                    Handles.DrawLine(Bone.position, child.position);
                }

                if (Application.isPlaying)
                {
                    Quaternion startRotation;

                    if (Tools.current == Tool.Rotate)
                    {
                        using (var cc = new EditorGUI.ChangeCheckScope())
                        {
                            Vector3 Pos = Bone.position;
                            Quaternion NewRotation = Quaternion.identity;

                            switch (rotationType)
                            {
                                case RotationOffsetType.LocalAdditive:
                                    startRotation = Bone.rotation; //Get the Rotation before IK 
                                    NewRotation = Handles.RotationHandle(startRotation * Quaternion.Euler(Offset.Value), Pos);
                                    NewRotation = Quaternion.Inverse(startRotation) * NewRotation;
                                    break;

                                case RotationOffsetType.LocalOverride:
                                    startRotation = Bone.parent.rotation;
                                    NewRotation = Handles.RotationHandle(startRotation * Quaternion.Euler(Offset.Value), Pos);
                                    NewRotation = Quaternion.Inverse(startRotation) * NewRotation;
                                    break;

                                case RotationOffsetType.RootRelativeRotationOverride:
                                    startRotation = animator.rootRotation; ;
                                    NewRotation = Handles.RotationHandle(startRotation * Quaternion.Euler(Offset.Value), Pos);
                                    NewRotation = Quaternion.Inverse(startRotation) * NewRotation;
                                    break;

                                case RotationOffsetType.RootRelativeRotationAdditive:
                                    startRotation = animator.rootRotation * Bone.rotation; //Get the Rotation before IK 
                                    NewRotation = Handles.RotationHandle(startRotation * Quaternion.Euler(Offset.Value), Pos);
                                    NewRotation = Quaternion.Inverse(startRotation) * NewRotation;
                                    break;

                                default:
                                    break;
                            }

                            if (cc.changed)
                            {
                                Undo.RecordObject(Target, "Change Rot");
                                Offset.Value = NewRotation.eulerAngles;
                                EditorUtility.SetDirty(Target);
                            }
                        }
                    }
                }
            }
        }

#endif
    }
}
