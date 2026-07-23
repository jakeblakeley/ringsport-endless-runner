using MalbersAnimations.Controller;
using UnityEngine;

namespace MalbersAnimations.Reactions
{
    [System.Serializable, AddTypeMenu("Malbers/Animal/State Enable-Disable")]
    public class StateEnableReaction : MReaction
    {
        override public string DynamicName => "State " + (states.include ? " Enable [" : " Disable [") + (states != null && states.IDs != null ? states.IDs.Count : "0") + "]";

        public IDListCheck<StateID> states;

        protected override bool _TryReact(Component component)
        {
            var animal = component as MAnimal;

            foreach (var id in states.IDs)
            {
                if (animal.State_TryGet(id.ID, out var st))
                {
                    st.Enable(states.include);
                }
            }
            return true;
        }
    }
}
