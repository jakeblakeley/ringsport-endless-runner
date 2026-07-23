using MalbersAnimations.Weapons;
using UnityEngine;

namespace MalbersAnimations.Conditions
{
    [System.Serializable]
    public abstract class C2_MWeaponManagerConditions : ConditionCore
    {
        [Hide(nameof(LocalTarget))] public MWeaponManager Target;
        public virtual void SetTarget(MWeaponManager n) => Target = n;
        protected override void _SetTarget(Object target) => VerifyComponent(target, ref Target);
    }


    [System.Serializable, AddTypeMenu("Weapon Manager/Has Weapon Equipped")]
    public class C2_WM_HasWeaponEquipped : C2_MWeaponManagerConditions
    {
        [Tooltip("Weapon ID to check if is equipped")]
        public IDList<WeaponID> weaponID;

        public override string DynamicName
        {
            get
            {
                var log = "WM: Has Weapon Equipped:";

                if (weaponID == null || weaponID.Count == 0) log += "Any Weapon";
                else
                {
                    foreach (var weapon in weaponID.items)
                    {
                        log += $" {weapon.name},";
                    }

                    //remove last comma
                    log = log[..^1];
                }
                return log;
            }
        }
        protected override bool _Evaluate()
        {
            return Target.Weapon != null && weaponID.Contains(Target.Weapon.WeaponType);
        }
    }


    [System.Serializable, AddTypeMenu("Weapon Manager/Current Weapon Aiming")]
    public class C2_WM_WeaponAiming : C2_MWeaponManagerConditions
    {
        public override string DynamicName => $"WM: Is Aiming?";

        protected override bool _Evaluate() => Target.Aim;
    }



    [System.Serializable, AddTypeMenu("Weapon Manager/Current Weapon Action")]
    public class C2_WM_WeaponAction : C2_MWeaponManagerConditions
    {
        [Tooltip("Weapon ID to check if is equipped")]
        public Weapon_Action CurrentAction = Weapon_Action.Attack;

        public override string DynamicName => $"WM: Current Action: {CurrentAction}";

        protected override bool _Evaluate() => Target.WeaponAction == CurrentAction;
    }


}