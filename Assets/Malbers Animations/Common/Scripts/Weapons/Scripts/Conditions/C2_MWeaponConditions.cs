using MalbersAnimations.Weapons;
using UnityEngine;

namespace MalbersAnimations.Conditions
{
    [System.Serializable]
    public abstract class C2_MWeaponConditions : ConditionCore
    {
        [Hide(nameof(LocalTarget))] public MWeapon Target;
        public virtual void SetTarget(MWeapon n) => Target = n;
        protected override void _SetTarget(Object target) => MTools.VerifyComponent(target, Target);
    }

    [System.Serializable, AddTypeMenu("Weapon/Is Equipped")]
    public class C2_WeaponEquipped : C2_MWeaponConditions
    {
        protected override bool _Evaluate() => Target.IsEquipped;
    }
}