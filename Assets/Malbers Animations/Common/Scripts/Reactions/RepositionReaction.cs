using UnityEngine;

namespace MalbersAnimations.Reactions
{
    [System.Serializable, AddTypeMenu("General/Reposition")]
    public class RepositionReaction : Reaction
    {
        public override string DynamicName => $"Reposition ";
        public override System.Type ReactionType => typeof(Transform);

        [RequiredField] public Transform Parent;

        public string Child;

        public bool Position = true;
        public bool Rotation = true;
        public bool Scale = false;

        protected override bool _TryReact(Component component)
        {
            if (Parent == null) return false;
            var Target = component.transform;

            var referenceTranform = Parent;

            if (!string.IsNullOrEmpty(Child))
            {
                referenceTranform = Parent.FindGrandChild(Child);
            }


            Debug.Log($"referenceTranform : [{referenceTranform}]");

            Target.SetPositionAndRotation(Position ? referenceTranform.position : Target.position, Rotation ? referenceTranform.rotation : Target.rotation);
            Target.localScale = Scale ? referenceTranform.localScale : Target.localScale;

            return true;
        }
    }
}
