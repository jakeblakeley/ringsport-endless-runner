using UnityEditor;
using UnityEngine;

namespace MalbersAnimations.IK
{
    [System.Serializable]
    [AddTypeMenu("Humanoid/IK Goal")]
    public class HumanIKGoal : IKProcessorOnAnimIK
    {
        public override bool RequireTargets => true;
        [Tooltip("Target to to lock any of the limbs ")]
        public AvatarIKGoal goal;

        public bool position = true;
        [Hide("position")]
        public Vector3 OffsetP;
        public bool rotation = true;
        [Hide("rotation")]
        public Vector3 OffsetR;

        [Tooltip("Min and Max Distance to the Goal to modify the weight. Id the distance is lower than the Min the weight is 1. If is greater than the max then the weight is zero")]
        public RangedFloat Distance = new();
        public bool gizmos = true;

        private Transform TargetGoal;
        private Transform TargetBone;

        public override void Start(IKSet set, Animator animator, int index)
        {
            //Cache the Bone
            switch (goal)
            {
                case AvatarIKGoal.LeftFoot:
                    Bone = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                    TargetBone = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                    break;
                case AvatarIKGoal.RightFoot:
                    Bone = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                    TargetBone = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                    break;
                case AvatarIKGoal.LeftHand:
                    Bone = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                    TargetBone = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                    break;
                case AvatarIKGoal.RightHand:
                    Bone = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                    TargetBone = animator.GetBoneTransform(HumanBodyBones.RightHand);
                    break;
                default:
                    break;
            }

            TargetGoal = GetTarget(set, Tag);
        }

        public override void OnAnimatorIK(IKSet set, Animator animator, int index, float weight)
        {
            if (TargetGoal == null)
            {
                TargetGoal = GetTarget(set, Tag);
                return; //If there's no target skip
            }

            //Check Max and Min Distance if is greater than Zero
            if (Distance.Min != 0 && Distance.Max != 0)
            {
                var DistanceFromRoot = Vector3.Distance(Bone.position, TargetGoal.position);
                weight *= DistanceFromRoot.CalculateRangeWeight(Distance.Min, Distance.Max);

                if (gizmos)
                {
                    var dir = (TargetGoal.position - Bone.position).normalized;
                    MDebug.DrawRay(Bone.position, dir * Distance.Max, Color.gray);
                    MDebug.DrawRay(Bone.position, dir * Distance.Min, Color.green);
                }
            }

            if (position)
            {
                animator.SetIKPositionWeight(goal, weight);
                animator.SetIKPosition(goal, TargetGoal.TransformPoint(OffsetP));
            }
            if (rotation)
            {
                animator.SetIKRotationWeight(goal, weight);
                animator.SetIKRotation(goal, TargetGoal.rotation * Quaternion.Euler(OffsetR));
            }
        }

        public override void Verify(IKManager manager, IKSet set, Animator animator, int index)
        {
            if (Tag == null)
            {
                Debug.LogError($"The IK Set <B>[{set.Name}]</B> has no Tag set for the [IK Processor: {name}]. " +
                    $"Please add a reference in the [Targets Global] list .", animator);
                return;
            }
            else
            {
                Debug.Log($"<B>[IK Processor: {name}][HumanIK Goal]</B>  <color=yellow>[OK]</color>");
            }
        }


#if UNITY_EDITOR
        internal override void OnSceneGUI(IKSet set, Animator anim, UnityEngine.Object Target, int index)
        {
            if (!Application.isPlaying) return;

            if (!gizmos || !Active || TargetBone == null || TargetGoal == null) return;

            if (Tools.current == Tool.Rotate)
            {
                using (var cc = new EditorGUI.ChangeCheckScope())
                {
                    var startRotation = TargetGoal.rotation;
                    var NewRotation = Handles.RotationHandle(startRotation * Quaternion.Euler(OffsetR), TargetBone.position);
                    NewRotation = Quaternion.Inverse(startRotation) * NewRotation;

                    if (cc.changed)
                    {
                        Undo.RecordObject(Target, "Change Rot");
                        OffsetR = NewRotation.eulerAngles;
                        EditorUtility.SetDirty(Target);
                    }
                }
            }

            if (Tools.current == Tool.Move)
            {
                var Rotation = Tools.pivotRotation == PivotRotation.Local ? TargetGoal.rotation : Quaternion.identity;

                using (var cc = new EditorGUI.ChangeCheckScope())
                {
                    Vector3 piv = TargetGoal.TransformPoint(OffsetP);
                    Vector3 NewPos = Handles.PositionHandle(piv, Rotation);

                    if (cc.changed)
                    {
                        Undo.RecordObject(Target, "Change Pos");
                        OffsetP = TargetGoal.InverseTransformPoint(NewPos);
                        EditorUtility.SetDirty(Target);
                    }
                }
            }
        }
#endif
    }
}

