using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.Conditions
{

    public abstract class C2_VarListener : ConditionCore
    {
        public IntReference ID = new(-1);
    }

    //-------------------------------------------------------------------------------------------------------
    [System.Serializable, AddTypeMenu("Values/Bool Listener")]
    public class C2_BoolVarListener : C2_VarListener
    {
        [Hide(nameof(LocalTarget))] public BoolVarListener Target;

        public BoolReference Value1;

        protected override bool _Evaluate()
        {
            return ID.Value == Target.ID || ID.Value == -1 && Value1.Value == Target.Value;
        }


        protected override void _SetTarget(Object target)
        {
            Target = MTools.VerifyComponent(target, Target);
        }
    }


    //-------------------------------------------------------------------------------------------------------
    [System.Serializable, AddTypeMenu("Values/Int Listener")]
    public class C2_IntVarListener : C2_VarListener
    {
        [Hide(nameof(LocalTarget))] public IntVarListener Target;


        public ComparerNumber Condition;
        public IntReference Value1;

        protected override bool _Evaluate()
        {
            return ID.Value == Target.ID || ID.Value == -1 && Value1.Value.MCompare(Target.Value, Condition);
        }


        protected override void _SetTarget(Object target)
        {
            Target = MTools.VerifyComponent(target, Target);
        }
    }

    //-------------------------------------------------------------------------------------------------------
    [System.Serializable, AddTypeMenu("Values/Float Listener")]
    public class C2_FloatVarListener : C2_VarListener
    {
        [Hide(nameof(LocalTarget))] public FloatVarListener Target;
        public ComparerNumber Condition;
        public FloatReference Value1;
        protected override bool _Evaluate()
        {
            return ID.Value == Target.ID || ID.Value == -1 && Value1.Value.MCompare(Target.Value, Condition);
        }
        protected override void _SetTarget(Object target)
        {
            Target = MTools.VerifyComponent(target, Target);
        }
    }

    [System.Serializable, AddTypeMenu("Values/String Listener")]
    public class C2_StringVarListener : C2_VarListener
    {

        [Hide(nameof(LocalTarget))] public StringVarListener Target;
        public StringReference Value1;

        protected override bool _Evaluate()
        {
            return ID.Value == Target.ID || ID.Value == -1 && Value1.Value == Target.Value;
        }

        protected override void _SetTarget(Object target)
        {
            Target = MTools.VerifyComponent(target, Target);
        }
    }
}
