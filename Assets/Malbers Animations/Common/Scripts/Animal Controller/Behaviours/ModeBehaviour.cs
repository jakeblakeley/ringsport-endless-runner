using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;
#endif

namespace MalbersAnimations.Controller
{
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/main-components/manimal-controller/modes#mode-behaviour")]
    [AddComponentMenu("Malbers/Mode Behavior")]
    public class ModeBehaviour : StateMachineBehaviour
    {
        public ModeID ModeID;

        [Tooltip("Calls 'Animation Tag Enter' on the Modes")]
        public bool EnterMode = true;
        [Tooltip("Calls 'Animation Tag Exit' on the Modes")]
        public bool ExitMode = true;

        [Tooltip("Calls 'OnModeUpdate' on the Modes")]
        public bool OnModeUpdate = true;

        [Tooltip("Next Ability to do on the Mode.If is set to -1, The Exit On Ability Logic will be ignored.\n" +
            "Used this when you need an ability to finish on another Ability.\n" +
            "E.g. If the wolf is in the Ability SIT, and you activate the HOWL; When HOWL finish you can play again SIT right after")]
        [Min(-1)] public int ExitAbility = -1;

        [Tooltip("True: the Animation will exit automatically after the Exit Time. No need for [EXIT] or [INTERRUPTED] transitions.\n\nImportant:\n" +
            "The Mode Layer needs a Transition to an Empty Animator State from Any State.\n" +
            "Conditions: Mode = 0 ModeStatus = 0")]
        public bool NoExitTransitions = false;

        /// <summary>
        /// Indicates whether to exit immediately without performing further processing.
        /// </summary>
        private bool JustExit = false;

        [Range(0, 1), Tooltip("Time to Exit the Animation Automatically with no exit transitions")]
        public float ExitTime = 0.95f;

        private MAnimal animal;
        private Mode mode;
        private Ability ActiveAbility;

        private bool ExitByModeDifferentLayer;

        public void InitializeBehaviour(MAnimal animal)
        {
            this.animal = animal;

            if (ModeID != null)
            {
                mode = animal.Mode_Get(ModeID.ID);
            }
            else
            {
                Debug.LogWarning("There's a Mode behaviour without an ID. Please check all your Mode Animations states.");
            }
        }

        public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            JustExit = false; //Reset the Do Exit Var
            ExitByModeDifferentLayer = false;

            if (layerIndex == 0)
            {
                Debug.LogError($"Mode Behaviours can't be on the Base Layer. Please check the Animator of [{animator.name}] and remove the Mode Behaviour from the Base Layer", animator);
            }

            //Find the Animal the first time
            if (animal == null) animal = animator.GetComponent<MAnimal>();

            if (animal)
            {
                if (!animal.enabled) JustExit = true; //Skip the Mode if the Animal is Disabled
                mode ??= animal.Mode_Get(ModeID.ID); //Store the mode if is null

                if (ModeID == null) { Debug.LogError("Mode behaviour needs an ID"); return; }
                if (mode == null) { Debug.LogError($"There's no [{ModeID.name}] mode on your character"); return; }


                ActiveAbility = mode.ActiveAbility;
                if (animal.ModeStatus == Int_ID.Loop) return;            //Means is Looping so Skip!!!

                if (EnterMode) mode.AnimationTagEnter(layerIndex);
            }
        }

        public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (JustExit) return;
            if (!ExitMode) return;

            //Means is Looping to itself So Skip the Exit Mode EXTREMELY IMPORTANT
            if (animator.GetCurrentAnimatorStateInfo(layerIndex).fullPathHash == stateInfo.fullPathHash) return;
            if (animator.GetNextAnimatorStateInfo(layerIndex).fullPathHash == stateInfo.fullPathHash) return;

            mode?.AnimationTagExit(ActiveAbility, ExitAbility);
        }


        public override void OnStateMove(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (JustExit) return;
            if (ExitByModeDifferentLayer) return;
            if (animal == null) return; //Safeguard to avoid null references errors


            if (animal.IsPlayingMode && animal.ActiveMode.LayerIndex != layerIndex)
            {
                if (animal.debugModes) Debug.Log($"<B>[{animal.name}]</B> → <COLOR=red>Mode Exit By Different Layer. This Mode? {mode.Name} [Animal Mode?: [{animal.activeMode.Name}- {animal.activeMode.LayerIndex}]] layerIndex [{layerIndex}] </COLOR>");
                ExitByModeDifferentLayer = true;
                animator.Play("Empty", layerIndex, 0.1f); //Exit the Mode by playing the Empty State
                return; //Exit if another Mode is playing on a different Layer
            }


            if (mode != null)
            {
                mode.UpdateMode = OnModeUpdate;             //Send the Update to the Animal Mode Update variable (This affect Charging Modes and Mode Modifiers)
                animal.ModeTime = stateInfo.normalizedTime; //Send the Normalized time to the Animal ModeTime variable
            }

            if (mode != null && mode.ActiveAbility != null && mode.ActiveAbility.Status == AbilityStatus.PlayOnce || animal.ModeStatus != -1) //Exit only 
                ExitModeNoTransition(animator, stateInfo, layerIndex);
        }

        private int JustInTransition;

        private void ExitModeNoTransition(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (NoExitTransitions && ExitMode && !JustExit && stateInfo.normalizedTime >= ExitTime)
            {
                JustInTransition++;

                if (animator.IsInTransition(layerIndex)
                    && (animator.GetNextAnimatorStateInfo(layerIndex).fullPathHash == stateInfo.fullPathHash))
                {
                    JustInTransition = 0;
                    return;
                }

                if (JustInTransition <= 1) return; //Skip the first frame weird issue (BUG)
                if (animal == null || ActiveAbility == null) return; //Safeguard to avoid null references errors

                JustExit = true;

                //Do not Exit if the Ability is Charging or is Forever
                if (ActiveAbility.Status == AbilityStatus.Charged || ActiveAbility.Status == AbilityStatus.Forever)
                {
                    if (animal.ModeStatus != -2) return; //Meaning is not in the Exit or Interrupted State
                }

                if (animal.ActiveMode != null && ActiveAbility != animal.ActiveMode.ActiveAbility)
                {
                    if (animal.debugModes) Debug.Log("Playing different Ability ..ignore exit");
                    return;
                }

                // Debug.Log($"Mode [{mode.Name}] . Ability [{ActiveAbility.Name}] Automatic Exit on [{ExitTime}]");
                mode.AnimationTagExit(ActiveAbility, ExitAbility);

                if (ExitAbility == -1)
                {
                    animal.SetModeStatus(0); //Must set the Mode Status to 0 to allow the Mode to Exit Automatically
                    animal.SetModeTrigger(); //Activate the Optional Trigger (1.5.2)
                }
            }
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(ModeBehaviour))]
    public class ModeBehaviourED : Editor
    {
        private SerializedProperty EnterMode, ExitMode, ModeID, ExitAbility, NoExitTransitions, ExitTime, OnModeUpdate
            ;
        private Color RequiredColor = new(1, 0.4f, 0.4f, 1);

        //MWC: one entering transition (source -> the owning state) and its conditions, cached for display
        private struct EnterTransitionInfo
        {
            public string OwnerState;       // the state this Mode Behaviour lives on
            public string Source;           // "Any State", "Entry", or the source state name
            public string[] Conditions;     // pre-formatted condition lines (param/mode/threshold)
            public string Warning;          // set when a "Mode" condition's ID doesn't match this behaviour's ModeID
        }

        //MWC: cached list of all transitions that ENTER the state owning this behaviour (built in OnEnable)
        private List<EnterTransitionInfo> EnterTransitions = new();
        private bool foldoutEnterTransitions = true;

        private void OnEnable()
        {

            ModeID = serializedObject.FindProperty("ModeID");
            EnterMode = serializedObject.FindProperty("EnterMode");
            ExitMode = serializedObject.FindProperty("ExitMode");
            ExitAbility = serializedObject.FindProperty("ExitAbility");
            NoExitTransitions = serializedObject.FindProperty("NoExitTransitions");
            ExitTime = serializedObject.FindProperty("ExitTime");
            OnModeUpdate = serializedObject.FindProperty("OnModeUpdate");

            BuildEnterTransitions(); //MWC: cache enter-transition conditions once per selection
            Undo.undoRedoPerformed += BuildEnterTransitions; //MWC: refresh when conditions change via undo/redo
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= BuildEnterTransitions; //MWC
        }

        //MWC: locate the state this Mode Behaviour is attached to and collect every transition that enters it
        private void BuildEnterTransitions()
        {
            EnterTransitions = new List<EnterTransitionInfo>();

            // Official API: resolves which Animator object (state/state machine) owns this behaviour
            var contexts = AnimatorController.FindStateMachineBehaviourContext((ModeBehaviour)target);
            if (contexts == null) return;

            foreach (var ctx in contexts)
            {
                if (ctx.animatorObject is not AnimatorState ownerState) continue; // only states have enter transitions
                var controller = ctx.animatorController;
                if (controller == null) continue;

                foreach (var layer in controller.layers)
                    CollectEntering(layer.stateMachine, ownerState, controller);
            }
        }

        //MWC: recurse a state machine (and its sub-machines) collecting transitions whose destination is ownerState
        private void CollectEntering(AnimatorStateMachine sm, AnimatorState ownerState, AnimatorController controller)
        {
            if (sm == null) return;

            // Any State -> ownerState
            foreach (var t in sm.anyStateTransitions)
                AddIfEntering(t, t.destinationState, ownerState, "Any State", controller);

            // Entry -> ownerState
            foreach (var t in sm.entryTransitions)
                AddIfEntering(t, t.destinationState, ownerState, "Entry", controller);

            // State -> ownerState
            foreach (var child in sm.states)
                foreach (var t in child.state.transitions)
                    AddIfEntering(t, t.destinationState, ownerState, child.state.name, controller);

            // Recurse sub-state-machines
            foreach (var sub in sm.stateMachines)
                CollectEntering(sub.stateMachine, ownerState, controller);
        }

        //MWC: add the transition's conditions to the cache when it actually targets ownerState
        private void AddIfEntering(AnimatorTransitionBase transition, AnimatorState destination,
            AnimatorState ownerState, string source, AnimatorController controller)
        {
            if (destination != ownerState) return;

            var conditions = transition.conditions;
            var lines = new string[conditions.Length];
            for (int i = 0; i < conditions.Length; i++)
                lines[i] = FormatCondition(conditions[i], controller);

            EnterTransitions.Add(new EnterTransitionInfo
            {
                OwnerState = ownerState.name,
                Source = source,
                Conditions = lines,
                Warning = CheckModeIDMismatch(conditions), //MWC: validate transition Mode ID against this behaviour's ID
            });
        }

        //MWC: warn when a "Mode" condition encodes a different Mode ID than this behaviour's ModeID
        private string CheckModeIDMismatch(AnimatorCondition[] conditions)
        {
            var modeID = ((ModeBehaviour)target).ModeID;
            if (modeID == null) return null; // nothing to compare against

            foreach (var c in conditions)
            {
                if (!TryGetConditionModeID(c, out int condID)) continue;
                if (condID != modeID.ID)
                    return $"ID mismatch: this Mode Behaviour uses '{modeID.name}' (ID {modeID.ID}), " +
                           $"but a transition condition targets Mode ID {condID} (value {(int)c.threshold}).";
            }
            return null;
        }

        //MWC: extract the Mode ID encoded in a "Mode" int condition (animator value = ID*1000 + ability)
        private bool TryGetConditionModeID(AnimatorCondition c, out int id)
        {
            id = 0;
            if (c.parameter != "Mode") return false; // only the int "Mode" parameter encodes the ID

            int value = Mathf.Abs((int)c.threshold);
            // 'Less' transitions use ID*1000 + 1000 as the upper bound, so step back one to recover the ID
            id = c.mode == AnimatorConditionMode.Less ? (value - 1) / 1000 : value / 1000;
            return true;
        }

        //MWC: format a condition as "param  mode  threshold", using symbols and hiding the threshold where meaningless
        private string FormatCondition(AnimatorCondition c, AnimatorController controller)
        {
            bool isTrigger = IsTriggerParameter(c.parameter, controller);

            switch (c.mode)
            {
                case AnimatorConditionMode.If:
                    return isTrigger ? $"{c.parameter}   Trigger" : $"{c.parameter}   If";
                case AnimatorConditionMode.IfNot:
                    return $"{c.parameter}   IfNot";
                case AnimatorConditionMode.Equals:
                    return $"{c.parameter}   =   {c.threshold}";
                case AnimatorConditionMode.NotEqual:
                    return $"{c.parameter}   !=   {c.threshold}";
                case AnimatorConditionMode.Greater:
                    return $"{c.parameter}   >   {c.threshold}";
                case AnimatorConditionMode.Less:
                    return $"{c.parameter}   <   {c.threshold}";
                default:
                    return $"{c.parameter}   {c.mode}   {c.threshold}";
            }
        }

        //MWC: look up whether an animator parameter is a Trigger (triggers use the 'If' mode, same as bools)
        private bool IsTriggerParameter(string parameter, AnimatorController controller)
        {
            if (controller == null) return false;
            foreach (var p in controller.parameters)
                if (p.name == parameter)
                    return p.type == AnimatorControllerParameterType.Trigger;
            return false;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {


                using (new GUILayout.HorizontalScope())
                {
                    var currentGUIColor = GUI.color;
                    GUI.color = ModeID.objectReferenceValue == null ? RequiredColor : currentGUIColor;
                    EditorGUIUtility.labelWidth = 35;
                    EditorGUILayout.PropertyField(ModeID, new GUIContent("ID"), GUILayout.MinWidth(40));

                    var width = 45;
                    var OnColor = Color.green;

                    GUI.color = EnterMode.boolValue ? OnColor : currentGUIColor;

                    EnterMode.boolValue = GUILayout.Toggle(EnterMode.boolValue,
                                       new GUIContent("Enter", "Notify the Mode on the Animal that the Ability has Started"), EditorStyles.miniButton, GUILayout.Width(width));

                    GUI.color = OnModeUpdate.boolValue ? OnColor : currentGUIColor;
                    OnModeUpdate.boolValue = GUILayout.Toggle(OnModeUpdate.boolValue,
                                       new GUIContent("Update", "Notify the Mode on the Animal that is playing. It Sends Normalized time of the animation. Useful for Abilities that have charge values and Mode Modifiers"), EditorStyles.miniButton, GUILayout.Width(width + 15));

                    GUI.color = ExitMode.boolValue ? OnColor : currentGUIColor;

                    ExitMode.boolValue = GUILayout.Toggle(ExitMode.boolValue,
                                       new GUIContent("Exit", "Notify the Mode on the Animal that the Ability has ended"), EditorStyles.miniButton, GUILayout.Width(width));


                    GUI.color = currentGUIColor;

                    if (ExitMode.boolValue)
                    {
                        var exitAbility = ExitAbility.intValue;
                        var GuiColor = GUI.color;
                        if (exitAbility > 0)
                            GUI.color = Color.yellow + Color.green;
                        EditorGUIUtility.labelWidth = 65;
                        EditorGUILayout.PropertyField(ExitAbility, GUILayout.Width(100));

                        GUI.color = GuiColor;
                    }
                }
                EditorGUIUtility.labelWidth = 0;

                if (ExitMode.boolValue)
                {
                    using (new GUILayout.HorizontalScope())
                    {
                        EditorGUIUtility.labelWidth = 113;
                        EditorGUILayout.PropertyField(NoExitTransitions, GUILayout.MaxWidth(140));
                        EditorGUIUtility.labelWidth = 55;
                        if (NoExitTransitions.boolValue)
                            EditorGUILayout.PropertyField(ExitTime);
                        EditorGUIUtility.labelWidth = 0;

                    }
                }
            }
            serializedObject.ApplyModifiedProperties();

            DrawEnterTransitions(); //MWC
        }

        //MWC: read-only display of the conditions of every transition that enters the owning state, grouped by source
        private void DrawEnterTransitions()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new GUILayout.HorizontalScope())
                {
                    //MWC: align the foldout with the help box edge by removing the foldout's default indent
                    EditorGUI.indentLevel++;
                    foldoutEnterTransitions = EditorGUILayout.Foldout(foldoutEnterTransitions,
                        $"Enter Transitions ({EnterTransitions.Count})", true, EditorStyles.foldoutHeader);
                    if (GUILayout.Button("↻", EditorStyles.miniButton, GUILayout.Width(28)))
                        BuildEnterTransitions(); //MWC: manual refresh (e.g. after editing transitions in the Animator)
                    EditorGUI.indentLevel--;

                }

                if (!foldoutEnterTransitions) return;

                if (EnterTransitions.Count == 0)
                {
                    EditorGUILayout.HelpBox("No transitions enter this state (or it is not part of an Animator Controller).", MessageType.Info);
                    return;
                }

                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    foreach (var info in EnterTransitions)
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.LabelField($"{info.Source}  →  {info.OwnerState}", EditorStyles.boldLabel);

                        if (!string.IsNullOrEmpty(info.Warning)) //MWC: surface Mode ID mismatches
                        {
                            var guicolor = GUI.backgroundColor;
                            GUI.backgroundColor = Color.red + Color.yellow;
                            EditorGUILayout.HelpBox(info.Warning, MessageType.Error);
                            GUI.backgroundColor = guicolor;
                        }

                        if (info.Conditions == null || info.Conditions.Length == 0)
                        {
                            EditorGUILayout.LabelField("• (no conditions)");
                        }
                        else
                        {
                            foreach (var line in info.Conditions)
                                EditorGUILayout.LabelField($"• {line}");
                        }
                        EditorGUI.indentLevel--;
                    }
                }
            }
        }
    }
#endif
}