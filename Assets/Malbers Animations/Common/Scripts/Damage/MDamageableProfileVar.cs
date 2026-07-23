using UnityEngine;

namespace MalbersAnimations
{
    [CreateAssetMenu(fileName = "Damageable Profile", menuName = "Malbers Animations/Damageable Profile")]
    public class MDamageableProfileVar : ScriptableObject
    {
        [Tooltip("The Damageable Profile to use")]
        public MDamageableProfile value;

        public void SetValue(MDamageableProfile newValue) => value = newValue;

        public void Apply(MDamageable mDamageable)
        {
            if (mDamageable != null) mDamageable.Profile_Set(value);
        }
    }
}
