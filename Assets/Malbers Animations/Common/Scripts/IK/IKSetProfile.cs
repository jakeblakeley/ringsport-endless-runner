using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
#endif


namespace MalbersAnimations.IK
{
    [CreateAssetMenu(menuName = "Malbers Animations/IK/IK Processor Profile", fileName = "New IK Profile")]
    public class IKSetProfile : ScriptableObject
    {
        public IKSet set;

        public int EditorTabs, SelectedIKProcessor;

        internal void Initialize(IKManager iKManager) => set.Initialize(iKManager);

        internal void ValidateIK(IKManager manager) => set.OnValidate(manager);
    }


    //Draw the IK Set in the Inspector
#if UNITY_EDITOR

    [CustomEditor(typeof(IKSetProfile))]
    public class IKProcessorProfileEditor : Editor
    {
        List<Type> derivedTypes;
        SerializedProperty set,

            Name, InvertAnimParameter, DisableTime, EnterLerp, ExitLerp, IKProcesors, SelectedIKProcessor,
            EditorTabs;

        IKSetProfile m;
        private ReorderableList Reo_Link;

        private void OnEnable()
        {
            set = serializedObject.FindProperty("set");
            SelectedIKProcessor = serializedObject.FindProperty("SelectedIKProcessor");
            EditorTabs = serializedObject.FindProperty("EditorTabs");



            Name = set.FindPropertyRelative("name");

            InvertAnimParameter = set.FindPropertyRelative("InvertAnimParameter");
            DisableTime = set.FindPropertyRelative("DisableTime");
            EnterLerp = set.FindPropertyRelative("EnterLerp");
            ExitLerp = set.FindPropertyRelative("ExitLerp");
            IKProcesors = set.FindPropertyRelative("IKProcesors");

            m = (IKSetProfile)target;
            derivedTypes = MTools.GetAllTypes<IKProcessor>();


            Reo_Link = new ReorderableList(serializedObject, IKProcesors, true, true, true, true)
            {

                drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    var element = IKProcesors.GetArrayElementAtIndex(index);
                    if (element.managedReferenceValue == null) return;

                    var active = element.FindPropertyRelative("Active");
                    var name = element.FindPropertyRelative("name");
                    var Weight = element.FindPropertyRelative("Weight");
                    var Tag = element.FindPropertyRelative("Tag");

                    var height = EditorGUIUtility.singleLineHeight;
                    float buttonWidth = 5;

                    var activeRect = new Rect(rect.x, rect.y, 20, height);


                    var IKSet = m.set;
                    var processor = IKSet.Processors[index];

                    var requireTarget = processor != null && processor.RequireTargets;

                    // Calculate the available width for Name and Tag fields
                    float nameTagStart = rect.x + 20;
                    float nameTagWidth = (rect.width * 0.7f - 20 - buttonWidth);

                    float halfNameTagWidth = requireTarget ? nameTagWidth * 0.5f : nameTagWidth;

                    var NameRect = new Rect(nameTagStart, rect.y, halfNameTagWidth, height);


                    var weightRect = new Rect(rect.width - rect.width * 0.3f + 25 - buttonWidth, rect.y, rect.width * 0.3f + 12f - buttonWidth, height);

                    var dC = GUI.contentColor;

                    if (SelectedIKProcessor.intValue == index) GUI.contentColor = Color.yellow;

                    EditorGUIUtility.labelWidth = 30;
                    active.boolValue = EditorGUI.Toggle(activeRect, GUIContent.none, active.boolValue);
                    EditorGUI.PropertyField(NameRect, name, GUIContent.none);

                    if (requireTarget)
                    {
                        var TagRect = new Rect(nameTagStart + halfNameTagWidth + 20, rect.y, halfNameTagWidth - 15, height);
                        EditorGUI.PropertyField(TagRect, Tag, GUIContent.none);
                    }
                    //using (new EditorGUI.DisabledGroupScope(!requireTarget))
                    //    EditorGUI.PropertyField(IndexRect, TargetIndex, GUIContent.none);

                    EditorGUI.PropertyField(weightRect, Weight, new GUIContent(" "));

                    GUI.contentColor = dC;

                    var DuplicateRect = new Rect(rect.width + 28, rect.y, 20, height);

                    if (GUI.Button(DuplicateRect, new GUIContent("D", "Duplicate/Clone the IK Processor")))
                    {
                        var refValue = element.managedReferenceValue;
                        var Copy = JsonUtility.ToJson(refValue);
                        var Duplicate = JsonUtility.FromJson(Copy, refValue.GetType());

                        AddNewItem(Duplicate, Reo_Link);
                    }

                    EditorGUIUtility.labelWidth = 0;
                },

                //drawElementCallback = (rect, index, isActive, isFocused) =>
                //{
                //    rect.y += 2;

                //    var element = IKProcesors.GetArrayElementAtIndex(index);
                //    if (element.managedReferenceValue == null) return;

                //    var active = element.FindPropertyRelative("Active");
                //    //var weight = element.FindPropertyRelative("weight");

                //    //var IndexValue = element.FindPropertyRelative("Index");
                //    var name = element.FindPropertyRelative("name");
                //    var Weight = element.FindPropertyRelative("Weight");
                //    var TargetIndex = element.FindPropertyRelative("TargetIndex");

                //    var IDRect = new Rect(rect) { height = EditorGUIUtility.singleLineHeight };


                //    var height = EditorGUIUtility.singleLineHeight;

                //    float buttonWidth = 5;

                //    var activeRect = new Rect(rect.x, rect.y, 20, height);
                //    var IndexRect = new Rect(rect.x + 20, rect.y, 35, height);
                //    var NameRect = new Rect(rect.x + 60, rect.y, rect.width * 0.7f - 60 - buttonWidth, height);
                //    var weightRect = new Rect(rect.width - rect.width * 0.3f + 25 - buttonWidth, rect.y, rect.width * 0.3f + 12f - buttonWidth, height);



                //    var dC = GUI.contentColor;

                //    if (SelectedIKProcessor.intValue == index) GUI.contentColor = Color.yellow;

                //    EditorGUIUtility.labelWidth = 30;
                //    active.boolValue = EditorGUI.Toggle(activeRect, GUIContent.none, active.boolValue);
                //    EditorGUI.PropertyField(NameRect, name, GUIContent.none);
                //    EditorGUI.PropertyField(IndexRect, TargetIndex, GUIContent.none);
                //    EditorGUI.PropertyField(weightRect, Weight, new GUIContent(" "));

                //    GUI.contentColor = dC;

                //    var DuplicateRect = new Rect(rect.width + 28, rect.y, 20, height);

                //    if (GUI.Button(DuplicateRect, new GUIContent("D", "Duplicate/Clone the IK Processor")))
                //    {
                //        var refValue = element.managedReferenceValue;
                //        var Copy = JsonUtility.ToJson(refValue);
                //        var Duplicate = JsonUtility.FromJson(Copy, refValue.GetType());

                //        AddNewItem(Duplicate, Reo_Link);
                //    }

                //    EditorGUIUtility.labelWidth = 0;
                //},

                drawHeaderCallback = rect =>
                {
                    var IDRect = new Rect(rect) { height = EditorGUIUtility.singleLineHeight, width = 60 };

                    EditorGUI.LabelField(IDRect, new GUIContent(" Target [I]", "Target Index from the <Targets> array. Set it to -1 if the Processor does not need any Target"));

                    var height = EditorGUIUtility.singleLineHeight;

                    var nameRect = new Rect(IDRect.x + 75, rect.y, 80, height);
                    var WeightRect = new Rect(rect) { x = rect.width - 30, width = 65 };
                    var button = new Rect(WeightRect.x - 55, WeightRect.y, 50, height);

                    EditorGUI.LabelField(nameRect, "IK Processor");
                    EditorGUI.LabelField(WeightRect, "Weight");

                    var defaultGuiColor = GUI.color;
                },

                onAddDropdownCallback = (Rect buttonRect, ReorderableList list) =>
                {
                    var menu = new GenericMenu();

                    foreach (var type in derivedTypes)
                    {
                        var att = type.GetCustomAttribute<AddTypeMenuAttribute>(false); //Find the correct name
                        string LabelName = att != null ? att.MenuName : type.Name;

                        menu.AddItem(new GUIContent(LabelName), false, (x) => AddNewItem(x, list), Activator.CreateInstance(type));
                    }
                    menu.ShowAsContext();
                },

                onSelectCallback = (list) => { SelectedIKProcessor.intValue = list.index; }
            };
        }

        void AddNewItem(object target, ReorderableList list)
        {
            var index = list.serializedProperty.arraySize;
            list.serializedProperty.arraySize++;
            list.index = index;
            IKProcessor link = (IKProcessor)target;
            link.name = target.GetType().Name;
            var element = list.serializedProperty.GetArrayElementAtIndex(index);
            element.managedReferenceValue = target;
            element.isExpanded = true;

            serializedObject.ApplyModifiedProperties();
        }


        private static string[] EditorLabel = new string[] { "IK Processors", "Weight Processors", "Events" };


        public override void OnInspectorGUI()
        {
            serializedObject.Update();


            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                MalbersEditor.Foldout_Bold(true, m.name, MTools.MBlue * 2, true);

                // ── Paste from IKManager clipboard ────────────────────────────────
                DrawPasteFromClipboardButton();
                // ─────────────────────────────────────────────────────────────────

                var processorsAmount = m.set.Processors.Count;
                var weightAmount = m.set.weightProcessors.Count;


                EditorLabel = new string[] { $"IK Set [{processorsAmount}]", $"Weight Processors [{weightAmount}]", "Events" };

                EditorTabs.intValue = GUILayout.Toolbar(EditorTabs.intValue, EditorLabel);

                if (EditorTabs.intValue == 0)
                {
                    // DrawFinalWeight(index);

                    var EnableTime = set.FindPropertyRelative("EnableTime");
                    EnableTime.isExpanded = MalbersEditor.Foldout(EnableTime.isExpanded, $"IK Set General Properties");

                    if (EnableTime.isExpanded)
                    {
                        using (new GUILayout.HorizontalScope())
                        {
                            EditorGUILayout.PropertyField(EnableTime);

                            if (EnableTime.floatValue > 0)

                                EditorGUILayout.PropertyField(EnterLerp, GUIContent.none, GUILayout.MaxWidth(50), GUILayout.MinWidth(5));
                        }

                        using (new GUILayout.HorizontalScope())
                        {
                            EditorGUILayout.PropertyField(DisableTime);
                            if (DisableTime.floatValue > 0)
                                EditorGUILayout.PropertyField(ExitLerp, GUIContent.none, GUILayout.MaxWidth(50), GUILayout.MinWidth(5));
                        }
                    }

                    var IKProcesors = set.FindPropertyRelative("IKProcesors");

                    DrawProfile(IKProcesors);
                }
                else if (EditorTabs.intValue == 1)
                {
                    var weights = set.FindPropertyRelative("weightProcessors");
                    var LerpWeight = set.FindPropertyRelative("LerpWeight");

                    EditorGUILayout.PropertyField(LerpWeight);

                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(weights);
                    EditorGUI.indentLevel--;
                }
                else if (EditorTabs.intValue == 2)
                {
                    var OnWeightChanged = set.FindPropertyRelative("OnWeightChanged");
                    var OnSetEnable = set.FindPropertyRelative("OnSetEnable");
                    var OnSetDisable = set.FindPropertyRelative("OnSetDisable");

                    EditorGUILayout.PropertyField(OnWeightChanged);
                    EditorGUILayout.PropertyField(OnSetEnable);
                    EditorGUILayout.PropertyField(OnSetDisable);
                }
            }
            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// Draws a button that pastes IKProcessors and WeightProcessors from the
        /// IKSetCopyPaste clipboard into this profile's IKSet, leaving Tags untouched.
        /// </summary>
        private void DrawPasteFromClipboardButton()
        {
            if (!IKSetCopyPaste.HasClipboard) return;

            var prevColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.4f, 0.8f, 0.4f);

            if (GUILayout.Button(new GUIContent(
                $"Paste IK Set from Clipboard  [{IKSetCopyPaste.ClipboardSetName}]",
                "Pastes the IKProcessors and WeightProcessors copied from an IKManager.\nTargets (Tags) are kept as-is.")))
            {
                Undo.RecordObject(m, "Paste IKSet Processors");
                var sourceName = IKSetCopyPaste.ClipboardSetName; // capture before paste clears it
                IKSetCopyPaste.PasteProcessorsInto(m.set);
                EditorUtility.SetDirty(m);
                serializedObject.Update();
                Debug.Log($"[IKSetProfile] Pasted processors from \"{sourceName}\" into \"{m.name}\".");
            }

            GUI.backgroundColor = prevColor;
        }

        private void DrawProfile(SerializedProperty link)
        {
            Reo_Link.DoLayoutList();

            var index = Reo_Link.index = SelectedIKProcessor.intValue;

            if (index != -1 && index < link.arraySize)
            {
                SerializedProperty ikProcessor = link.GetArrayElementAtIndex(index);

                if (ikProcessor != null && ikProcessor.managedReferenceValue != null)
                {
                    EditorGUILayout.Space(-16);
                    EditorGUILayout.LabelField($"[{ikProcessor.managedReferenceValue.GetType().Name}]", EditorStyles.boldLabel);

                    using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        var TargetIndex = ikProcessor.FindPropertyRelative("TargetIndex");

                        var targets = m.set.Targets;
                        var TargLength = targets.Length;
                        var TIndex = TargetIndex.intValue;
                        var set = m.set;

                        if (set == null || set.Processors == null || index >= set.Processors.Count) return;

                        var processor = set.IKProcesors[index];
                        var RequireTarget = processor.RequireTargets;


                        string CurrentTarget = TIndex >= 0 && TIndex < TargLength && targets[TIndex].Value ? targets[TIndex].Value.name : "Empty";

                        CurrentTarget = $" [Target: {CurrentTarget}]";

                        if (TIndex == -1 || !RequireTarget) CurrentTarget = "  [No Target Needed]";


                        if (RequireTarget)
                        {
                            if (processor.Tag == null && TIndex >= 0)
                            {
                                if (TIndex >= TargLength)
                                    EditorGUILayout.HelpBox($"The Target Index [{TIndex}] greater than the Set Targets Array [{TargLength}]", MessageType.Warning);
                                else if (targets[TIndex].Value == null)
                                    EditorGUILayout.HelpBox($"The Target Index [{TIndex}] is Empty. Make sure to set the value in the Editor or at Runtime", MessageType.Warning);
                            }
                            else
                            {
                                CurrentTarget = $" [Target: {(processor.Tag ? processor.Tag.name : "Missing")}]";
                            }
                        }

                        EditorGUILayout.PropertyField(ikProcessor, new GUIContent(ikProcessor.displayName + CurrentTarget), true);
                        var AnimParameter = ikProcessor.FindPropertyRelative("AnimParameter");

                        if (ikProcessor.isExpanded)
                        {
                            EditorGUI.indentLevel++;
                            EditorGUILayout.PropertyField(AnimParameter, new GUIContent("Anim Param [IKProcessor]", "Local Anim Parameter to apply to a specific IK Processor. E.g Use the Anim Curve for the Left Hand and another anim curve for the Right Hand"));
                            EditorGUI.indentLevel--;
                        }
                    }
                }
            }
        }
    }
#endif
}