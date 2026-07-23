using UnityEngine;

namespace MalbersAnimations.Reactions
{
    [System.Serializable, AddTypeMenu("General/Member Set (Old)")]
    public class MemberSetReaction : Reaction
    {
        public override string DynamicName => Member.Description();

        public MemberValueSetter Member;
        public override System.Type ReactionType => typeof(Component);

        protected override bool _TryReact(Component component)
        {
            Member.Apply(component);
            return true;
        }
    }

    [System.Serializable, AddTypeMenu("General/Member Set (New)")]
    public class MemberValuesSetReaction : Reaction
    {
        public override string DynamicName => Member.Description();

        public MemberValuesSetter Member;
        public override System.Type ReactionType => typeof(Component);

        protected override bool _TryReact(Component component)
        {
            Member.Apply(component);
            return true;
        }
    }
}
