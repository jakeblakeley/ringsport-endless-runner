using MalbersAnimations.Controller;
using MalbersAnimations.Scriptables;
using UnityEngine;


namespace MalbersAnimations.Conditions
{
    [System.Serializable]
    public abstract class C2_MAnimal : ConditionCore
    {
        [Hide(nameof(LocalTarget))] public MAnimal Target;
        protected override void _SetTarget(Object target)
        {
            Target = MTools.VerifyComponent(target, Target);
        }
    }

    //------------------------------------------------------------------------------------------------------------------------------------

    #region Animal General Values 
    [System.Serializable, AddTypeMenu("Animal/General")]
    public class C2_AnimalGeneral : C2_MAnimal
    {
        public override string DynamicName => $"Animal [{Condition}]";

        public enum AnimalCondition
        {
            Grounded, RootMotion, FreeMovement, AlwaysForward, Sleep, AdditivePosition,
            AdditiveRotation, InZone, InGroundChanger, Strafing, CanStrafe, MovementDetected, InTimeline
        }

        public AnimalCondition Condition;

        protected override bool _Evaluate()
        {
            if (Target)
            {
                return Condition switch
                {
                    AnimalCondition.Grounded => Target.Grounded,
                    AnimalCondition.RootMotion => Target.RootMotion,
                    AnimalCondition.FreeMovement => Target.FreeMovement,
                    AnimalCondition.AlwaysForward => Target.AlwaysForward,
                    AnimalCondition.Sleep => Target.Sleep,
                    AnimalCondition.AdditivePosition => Target.UseAdditivePos,
                    AnimalCondition.AdditiveRotation => Target.UseAdditiveRot,
                    AnimalCondition.InZone => Target.InZone,
                    AnimalCondition.InGroundChanger => Target.GroundChanger != null && Target.GroundChanger.Lerp > 0,
                    AnimalCondition.Strafing => Target.Strafe,
                    AnimalCondition.CanStrafe => Target.CanStrafe && Target.ActiveStance.CanStrafe && Target.ActiveState.CanStrafe,
                    AnimalCondition.MovementDetected => Target.MovementDetected,
                    AnimalCondition.InTimeline => Target.InTimeline,
                    _ => false,
                };
            }
            return false;
        }
    }
    #endregion


    [System.Serializable, AddTypeMenu("Animal/In Zone")]
    public class C2_AnimalInZone : C2_MAnimal
    {
        public override string DynamicName => "[Animal] -> In Zone?";
        protected override bool _Evaluate()
        {
            return Target ? Target.InZone : false;
        }
    }

    //------------------------------------------------------------------------------------------------------------------------------------

    #region Animal Modes
    [System.Serializable, AddTypeMenu("Animal/Modes")]
    public class C2_AnimalMode : C2_MAnimal
    {
        public override string DynamicName
        {
            get
            {
                var display = $"Animal Mode {(Value != null && Condition != ModeCondition.PlayingAnyMode ? "[" + Value.name + "]" : "")} [{Condition}";

                string extraData = Condition switch
                {
                    ModeCondition.PlayingAbility => $" '{AbilityName.Value}']",
                    ModeCondition.HasAbility => $" '{AbilityName.Value}']",

                    ModeCondition.PlayingAbilityByIndex => $" {AbilityIndex.Value}]",
                    ModeCondition.HasAbilityIndex => $" {AbilityIndex.Value}]",
                    ModeCondition.ActiveAbilityIndex => $" {AbilityIndex.Value}]",
                    ModeCondition.DefaultAbilityIndex => $" {AbilityIndex.Value}]",
                    _ => "]",
                };

                return display + extraData;
            }
        }

        public enum ModeCondition
        { PlayingAnyMode, PlayingMode, PlayingAbility, PlayingAbilities, PlayingAbilityByIndex, HasMode, HasAbility, HasAbilityIndex, Enabled, ActiveAbilityIndex, DefaultAbilityIndex, PlayingModes }

        public ModeCondition Condition;
        [Hide(nameof(Condition), true, 0, 11)]
        public ModeID Value;
        [Hide(nameof(Condition), 3, 6)]
        public StringReference AbilityName;
        [Hide(nameof(Condition), 4, 7, 9, 10)]
        public IntReference AbilityIndex;

        [Hide(nameof(Condition), 11)]
        public IDList<ModeID> modes;

        public void SetValue(ModeID v) => Value = v;

        private Mode mode;

        protected override bool _Evaluate()
        {
            if (Target == null) return false;

            mode ??= Target.Mode_Get(Value);        //cache the mode

            if (mode == null) return false;

            return Condition switch
            {
                //Check if the Target is playing a mode and if the mode is the one with this ID (if the Value is null, then it will return true if the Target is playing any mode)
                ModeCondition.PlayingMode => Target.IsPlayingMode && (Value == null || Target.ActiveMode.ID == Value),

                //Check if the Target is playing a mode and if the active ability of the active mode is the one with this name (if the AbilityName is null or empty, then it will return true if the Target is playing any ability)               
                ModeCondition.PlayingAbility => Target.IsPlayingMode && (string.IsNullOrEmpty(AbilityName.Value) || Target.ActiveMode.ActiveAbility.Name == AbilityName),

                //Check if the Target has a mode matching the ID 
                ModeCondition.HasMode => mode != null,
                ModeCondition.HasAbility => mode != null && mode.Abilities.Exists(x => x.Name == AbilityName),
                ModeCondition.HasAbilityIndex => mode != null && mode.Abilities.Exists(x => x.Index == AbilityIndex.Value),
                ModeCondition.Enabled => mode != null && mode.Active,
                ModeCondition.PlayingAnyMode => Target.IsPlayingMode,
                ModeCondition.PlayingAbilityByIndex => Target.IsPlayingMode && Target.ActiveMode.ActiveAbility.Index.Value == AbilityIndex.Value,
                ModeCondition.ActiveAbilityIndex => mode != null && mode.AbilityIndex == AbilityIndex,
                ModeCondition.DefaultAbilityIndex => mode != null && mode.DefaultIndex.Value == AbilityIndex,
                ModeCondition.PlayingModes => Target.IsPlayingMode && modes.Contains(Target.ActiveMode.ID),
                _ => false,
            };
        }

        public override void TargetHasChanged()
        {
            if (Target) mode = Target.Mode_Get(Value);        //Update the mode
        }
    }
    #endregion

    //------------------------------------------------------------------------------------------------------------------------------------

    #region Animal States
    [System.Serializable, AddTypeMenu("Animal/States")]
    public class C2_AnimalState : C2_MAnimal
    {
        public override string DynamicName => $"Animal [{Condition}] {(Value != null ? $"[{Value.name}]" : string.Empty)}";
        public enum StateCondition { ActiveState, Enabled, HasState, LastState, SleepFromMode, SleepFromState, SleepFromStance, Pending, IsPersistent }
        public StateCondition Condition = StateCondition.ActiveState;
        public StateID Value;
        private State state;

        public void SetValue(StateID v) => Value = v;

        protected override bool _Evaluate()
        {
            if (!Target) return false;

            if (state == null) state = Target.State_Get(Value); //cache the state

            return Condition switch
            {
                StateCondition.ActiveState => Target.ActiveStateID.ID == Value.ID,    //Check if the Active state is the one with this ID
                StateCondition.HasState => state != null,                       //Check if the State exist on the Current Animal
                StateCondition.Enabled => state.Active,
                StateCondition.SleepFromMode => state.IsSleepFromMode,
                StateCondition.SleepFromState => state.IsSleepFromState,
                StateCondition.SleepFromStance => state.IsSleepFromStance,
                StateCondition.LastState => Target.LastState.ID == Value,       //Check if the LastState is this ID
                StateCondition.Pending => state.IsPending,
                StateCondition.IsPersistent => state.IsPersistent,
                _ => false,
            };
        }

        public override void TargetHasChanged()
        {
            if (Target) state = Target.State_Get(Value); //update the state
        }
    }
    #endregion

    //------------------------------------------------------------------------------------------------------------------------------------

    #region Animal Stances
    [System.Serializable, AddTypeMenu("Animal/Stances")]
    public class C2_AnimalStance : C2_MAnimal
    {
        public override string DynamicName => $"Animal [{Condition}] {(Value != null ? $"[{Value.name}]" : string.Empty)}";

        public enum StanceCondition { CurrentStance, DefaultStance, LastStance, HasStance }
        public StanceCondition Condition;
        public StanceID Value;
        private Stance stance;

        public void SetValue(StanceID v) => Value = v;

        protected override bool _Evaluate()
        {
            if (stance == null && Target != null) stance = Target.Stance_Get(Value); //cache the stance

            if (Target != null && stance != null)
            {
                return Condition switch
                {
                    StanceCondition.CurrentStance => Target.Stance == Value,
                    StanceCondition.DefaultStance => Target.DefaultStanceID == Value,
                    StanceCondition.LastStance => Target.LastStanceID == Value,
                    StanceCondition.HasStance => stance != null,
                    _ => false,
                };
            }
            return false;
        }
    }
    #endregion

    //------------------------------------------------------------------------------------------------------------------------------------

    #region Animal Speeds
    [System.Serializable, AddTypeMenu("Animal/Speeds")]
    public class C2_AnimalSpeed : C2_MAnimal
    {
        public override string DynamicName
        {
            get
            {
                var display = $"Animal [{Condition}";

                string extraData = Condition switch
                {
                    SpeedCondition.VerticalSpeed => $": {MTools.CompareToString(compare)} {Value.Value}]",
                    SpeedCondition.CurrentSpeedSet => $": <{SpeedName.Value}>]",
                    SpeedCondition.CurrentSpeedModifier => $": <{SpeedName.Value}>]",
                    SpeedCondition.ActiveIndex => $": {Value.Value}]",
                    _ => "]",
                };

                return display + extraData;
            }
        }

        public enum SpeedCondition { VerticalSpeed, CurrentSpeedSet, CurrentSpeedModifier, ActiveIndex, IsSprinting, CanSprint }

        public SpeedCondition Condition;

        [Hide(nameof(Condition), (int)SpeedCondition.VerticalSpeed, (int)SpeedCondition.ActiveIndex)]
        public ComparerNumber compare = ComparerNumber.Equal;

        [Hide(nameof(Condition), (int)SpeedCondition.VerticalSpeed, (int)SpeedCondition.ActiveIndex)]
        public FloatReference Value = new();

        [Hide(nameof(Condition), (int)SpeedCondition.CurrentSpeedSet, (int)SpeedCondition.CurrentSpeedModifier)]
        public StringReference SpeedName = new();

        protected override bool _Evaluate()
        {
            if (!Target) return false;

            return Condition switch
            {
                SpeedCondition.VerticalSpeed => Target.VerticalSmooth.MCompare(Value, compare),
                SpeedCondition.CurrentSpeedSet => Target.CurrentSpeedSet.name == SpeedName,
                SpeedCondition.CurrentSpeedModifier => Target.CurrentSpeedModifier.name == SpeedName,
                SpeedCondition.ActiveIndex => Target.CurrentSpeedIndex == (int)Value,
                SpeedCondition.IsSprinting => Target.Sprint,
                SpeedCondition.CanSprint => Target.CanSprint,
                _ => false,
            };
        }
    }
    #endregion

    //------------------------------------------------------------------------------------------------------------------------------------

    #region Animal Strafe
    [System.Serializable, AddTypeMenu("Animal/Strafe")]
    public class C2_AnimalStrafe : C2_MAnimal
    {
        public override string DynamicName => $"Animal [{Condition}]";
        public enum StrafeCondition { Strafing, CanStrafe }
        public StrafeCondition Condition;

        protected override bool _Evaluate()
        {
            if (Target)
            {
                return Condition switch
                {
                    StrafeCondition.Strafing => Target.Strafe,
                    StrafeCondition.CanStrafe => Target.CanStrafe && Target.ActiveStance.CanStrafe && Target.ActiveState.CanStrafe,
                    _ => false,
                };
            }
            return false;
        }
    }


    [System.Serializable, AddTypeMenu("Animal/Move Direction Angle")]
    public class C2_AnimalDirectionAngle : C2_MAnimal
    {
        public FloatReference MinAngle = new(-45);
        public FloatReference MaxAngle = new(45);
        [Tooltip("If true the angle will be compared as absolute value")]
        public BoolReference Abs = new(false); //If true the angle will be compared as absolute value

        public override string DynamicName => $"Animal Move Direction Angle [{MinAngle.Value}]<={angle:f2}<=[{MaxAngle.Value}]";

        private float angle;

        protected override bool _Evaluate()
        {
            if (Target)
            {
                var AxisRaw = Target.Move_Direction;
                angle = Vector3.SignedAngle(Target.Forward, AxisRaw, Target.UpVector); //Get The angle
                if (Abs.Value) angle = Mathf.Abs(angle); //If we are using absolute value, then we take the absolute value of the angle
                var result = angle >= MinAngle && angle <= MaxAngle;

                MDebug.Draw_Arrow(Target.transform.position, Target.Move_Direction, Color.green, 0.5f);
                MDebug.Draw_Arrow(Target.transform.position, Target.Forward, Color.red, 0.5f);


                Debugging($"Animal {Target.name} Move Direction Angle: {angle:F2}", result, Target);

                return result;
            }
            return false;
        }
    }
    #endregion

    //------------------------------------------------------------------------------------------------------------------------------------
}
