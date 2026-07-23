using MalbersAnimations.Controller;
using UnityEngine;

namespace MalbersAnimations.Reactions
{
    [System.Serializable]
    [AddTypeMenu("Malbers/Animal/Mode Enable-Disable")]
    public class ModeEnableReaction : MReaction
    {
        override public string DynamicName =>
                "Mode " + (modes.include ? " Enable [" : " Disable [") + (modes != null && modes.IDs != null ? modes.IDs.Count : "0") + "] Temporal " + (TemporalEnable ? "[■]" : "[ ]");

        [Tooltip("Enable or Disable the Mode temporally, this does not deactivate completely the Mode. Disables modes will remain disabled. and it wont be affected by this")]
        public bool TemporalEnable = false;
        public IDListCheck<ModeID> modes;

        protected override bool _TryReact(Component component)
        {
            var animal = component as MAnimal;

            foreach (var id in modes.IDs)
            {
                var mode = animal.Mode_Get(id.ID);

                if (mode != null)
                {
                    if (TemporalEnable)
                    {
                        if (modes.include)
                            mode.Enable_Temporal();
                        else
                            mode.Disable_Temporal();
                    }
                    else
                        mode.Active = modes.include;

                }
            }
            return true;
        }
    }
}
