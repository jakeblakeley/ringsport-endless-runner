using MalbersAnimations.Controller;
using UnityEngine;

namespace MalbersAnimations.Reactions
{
    [System.Serializable]
    [AddTypeMenu("Malbers/Animal/Stance Enable-Disable")]
    public class StanceEnableReaction : MReaction
    {
        override public string DynamicName => "Stance " + (stances.include ? " Enable [" : " Disable [") + (stances != null && stances.IDs != null ? stances.IDs.Count : "0") + "]";

        public IDListCheck<StanceID> stances;

        protected override bool _TryReact(Component component)
        {
            var animal = component as MAnimal;

            foreach (var id in stances.IDs)
            {
                if (animal.Stance_TryGet(id, out var st))
                {
                    st.Enable(stances.include);
                }
            }
            return true;
        }
    }
}
