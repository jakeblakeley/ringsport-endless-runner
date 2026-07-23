using System.Collections.Generic;
using UnityEngine;
using System;
using System.Reflection;
using System.Collections;
using MalbersAnimations.Scriptables;


#if UNITY_EDITOR
using UnityEditorInternal;
using UnityEditor;
#endif

namespace MalbersAnimations.IK
{
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/secondary-components/ik-manager")]
    [AddComponentMenu("Malbers/IK/IK Manager"), DisallowMultipleComponent]
    [DefaultExecutionOrder(1500)]
    [Unity.Cinemachine.SaveDuringPlay]
    public class IKManager : MonoBehaviour, IIKSource
    {
        [RequiredField] public Animator animator;

        [Range(0f, 1f), Tooltip("Global weight for the All IK Profiles")]
        public float Weight = 1;

        public List<IKSet> sets = new();
        public HashSet<int> animatorHashParams;


        [Tooltip("Assign Tags on Transforms so the IK Processors can use Tags instead of Indexes.")]
        public List<IDPair<IKTag, TransformReference>> GlobalTargets = new();
        public Dictionary<int, Transform> GlobalTargets_Dic { get; private set; } = new();

        public System.Action<Animator, float, float> UpdateIKAction { get; set; }

        public System.Action<Animator, float, float, int> AnimatorIKAction { get; set; }
        public Transform Owner => transform;

        /// <summary> Store the Selected Tab in the inspector</summary>
        [HideInInspector, SerializeField] private int EditorTabs;
        [HideInInspector, SerializeField] internal int SelectedSet;

        /// <summary> Gets or sets the mapping of transforms to their corresponding bind pose rotations.  </summary>
        public Dictionary<Transform, Quaternion> BindPose = new();

        private void Awake()
        {
            animator = animator == null ? this.FindComponent<Animator>() : animator;

            if (animator == null)
            {
                Debug.LogError($"No Animator found on the GameObject [{name}] or its children", this);
                enabled = false;
                return;
            }
            //Store all the FLOAT animParameters of the Animator
            animatorHashParams = new();
            foreach (var parameter in animator.parameters)
            {
                if (parameter.type == UnityEngine.AnimatorControllerParameterType.Float)
                    animatorHashParams.Add(parameter.nameHash);
            }

            // Cache the initial local rotations of all the bones in the animator to be able to restore them later if needed
            var skinnedMeshes = animator.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (var smr in skinnedMeshes)
            {
                foreach (var bone in smr.bones)
                {
                    if (bone != null)
                        BindPose.TryAdd(bone, bone.localRotation);
                }
            }
        }

        private bool GlobalInitialized = false;
        private void InitializeGlobalTargets()
        {
            if (GlobalInitialized) return;

            GlobalTargets ??= new(); //Make sure the Tag Target list is not Null

            //Convert the Global Targets List to a Dictionary for faster access on the IK Processors
            GlobalTargets_Dic = new();

            foreach (var t in GlobalTargets)
            {
                if (t.Value != null && t.ID != null)
                {
                    if (!GlobalTargets_Dic.ContainsKey(t.ID))
                    {
                        GlobalTargets_Dic.Add(t.ID, t.Value.Value);
                    }
                }
            }

            foreach (var set in sets) set.Initialize(this);
            GlobalInitialized = true;
        }

        private void OnEnable()
        {
            animatePhysics = animator.updateMode == AnimatorUpdateMode.Fixed;

            InitializeGlobalTargets();

            if (animatePhysics)
                StartCoroutine(SolveFixedUpdateIK());

            //initialize every IK Set
            foreach (var set in sets)
                set.OnEnable(animator, animatorHashParams);
        }

        private bool animatePhysics;

        private void OnDisable()
        {
            foreach (var set in sets)
            {
                set.OnDisable(animator, animatorHashParams);
            }

            StopAllCoroutines();
        }
        private IEnumerator SolveFixedUpdateIK()
        {
            var wait = new WaitForFixedUpdate();

            while (true)
            {
                yield return wait;

                UpdateIKAction?.Invoke(animator, Weight, Time.fixedDeltaTime);

                foreach (var set in sets)
                {
                    set.CacheValues(animator);
                    set.LateUpdate(animator, Weight, Time.fixedDeltaTime);
                }

            }
        }
        private void LateUpdate()
        {
            if (animatePhysics) return;

            UpdateIKAction?.Invoke(animator, Weight, Time.deltaTime);

            foreach (var set in sets)
            {
                set.CacheValues(animator);
                set.LateUpdate(animator, Weight, Time.deltaTime);
            }
        }

        private void OnAnimatorIK(int LayerIndex)
        {
            var time = animator.updateMode == AnimatorUpdateMode.Normal ? Time.deltaTime : Time.fixedDeltaTime;

            AnimatorIKAction?.Invoke(animator, Weight, time, LayerIndex);


            foreach (var set in sets)
                set.OnAnimatorIK(animator, Weight, time, LayerIndex);
        }



        /// <summary>Activate or deactivate a Set.</summary>
        /// <param name="set"> name of the set</param>
        /// <param name="value">enable: true disable: false</param>
        public void Set_Enable(string set, bool value)
        {
            if (!enabled) return;

            var sets = set.Split(',');

            foreach (var s in sets)
            {
                var NewSet = FindSet(s);
                NewSet?.Enable(value);
            }
        }

        /// <summary>Finds a set by its name and Activates it</summary>
        public void Set_Enable(string set) => Set_Enable(set, true);

        /// <summary>Finds a set by its name and deactivates it</summary>
        public void Set_Disable(string set) => Set_Enable(set, false);

        /// <summary>Finds a set by its name and Activates it</summary>
        public void Set_Weight_1(string set) => Set_Enable(set, false);
        /// <summary>Finds a set by its name and deactivates it</summary>
        public void Set_Weight_0(string set) => Set_Enable(set, false);

        public void Set_Weight(string set, bool value)
        {
            var sets = set.Split(',');
            foreach (var s in sets)
            {
                var NewSet = FindSet(s);
                NewSet?.SetWeight(value);
            }
        }

        /// <summary> Sets a new Target to a IK Set given the set name, and the new index and target transform value </summary>

        public void Target_Set(string set, Transform newTarget, int index)
        {
            var sets = set.Split(',');
            foreach (var s in sets)
            {
                var NewSet = FindSet(s);
                NewSet?.SetTarget(newTarget, index);
            }
        }

        /// <summary>  Finds a IK Set given a name </summary>
        public virtual IKSet FindSet(string set) => sets.Find(x => x.Name == set);

        public void Target_Clear(string set, int index)
        {
            var NewSet = FindSet(set);
            NewSet?.ClearTarget(index);
        }

        public void Target_Clear(string set)
        {
            var sets = set.Split(',');
            foreach (var s in sets)
            {
                var NewSet = FindSet(s);
                NewSet?.ClearAllTargets();
            }
        }

        public void Target_Add_Global(IDPair<IKTag, TransformReference> newTarget)
        {
            if (newTarget.Value.Value != null && newTarget.ID != null)
            {
                //Add or update the target in the dictionary
                if (GlobalTargets_Dic.ContainsKey(newTarget.ID))
                {
                    GlobalTargets_Dic[newTarget.ID] = newTarget.Value.Value;
                }
                else
                {
                    GlobalTargets_Dic.Add(newTarget.ID, newTarget.Value.Value);
                    //  Debug.Log("ADDED TO DIC");
                }


                //update also the list to keep it in sync with the dictionary for inspector visibility and so the IK Processors can find the index of the target in the list if they need to
                var foundTarget = GlobalTargets.Find(x => x.ID.ID == newTarget.ID.ID);

                if (foundTarget != null)
                {
                    foundTarget.Value = new(newTarget.Value.Value);
                }
                else
                {
                    GlobalTargets.Add(new(newTarget.ID, new(newTarget.Value)));
                }
            }
        }

        public void Target_Remove_Global(IDPair<IKTag, TransformReference> newTarget)
        {
            if (newTarget.ID != null)
            {
                if (GlobalTargets_Dic.ContainsKey(newTarget.ID))
                {
                    GlobalTargets_Dic[newTarget.ID] = null;
                }
                //update also the list to keep it in sync with the dictionary for inspector visibility and so the IK Processors can find the index of the target in the list if they need to
                if (GlobalTargets.Find(x => x.ID == newTarget.ID) is IDPair<IKTag, TransformReference> foundTarget)
                {
                    foundTarget.Value = new();
                }
            }
        }

        internal Transform GetTargetByTag(IKTag tag)
        {
            if (GlobalTargets_Dic.TryGetValue(tag, out var target))
            {
                return target;
            }
            return null;
        }

        public void Target_Set(string set, Transform[] targets)
        {
            var sets = set.Split(',');

            foreach (var s in sets)
            {
                var NewSet = FindSet(s);
                NewSet?.SetTargets(targets);
            }
        }


        public void Processor_SetEnable(string set, string processor, bool value)
        {
            var NewSet = FindSet(set);
            NewSet?.Processor_SetEnable(processor, value);
        }

        private void Reset()
        {
            animator = this.FindComponent<Animator>();
        }



        private void AutoFillTargetsOnValidate()
        {
            foreach (var idPair in GlobalTargets)
            {
                if (idPair.ID != null && idPair.Value.Value == null)
                {
                    if (idPair.ID.name.ToLower().Contains("head")) idPair.Value = new TransformReference(transform.FindGrandChild("head"));
                    else if (idPair.ID.name.ToLower().Contains("neck")) idPair.Value = new TransformReference(transform.FindGrandChild("neck"));
                    else if (idPair.ID.name.ToLower().Contains("upperchest")) idPair.Value = new TransformReference(transform.FindGrandChild("upperchest"));
                    else if (idPair.ID.name.ToLower().Contains("chest")) idPair.Value = new TransformReference(transform.FindGrandChild("chest"));
                    else if (idPair.ID.name.ToLower().Contains("root")) idPair.Value = new TransformReference(transform);
                }
            }
        }

        public bool List_GlobalTarget_HasTag(IKTag tag) => GlobalTargets.Exists(x => x.ID == tag);
        public bool List_GlobalTarget_HasTag(string tag) => GlobalTargets.Exists(x => x.ID != null && x.ID.name == tag);
        public Transform List_GlobalTarget_GetTagValue(IKTag tag)
        {
            var target = GlobalTargets.Find(x => x.ID == tag);
            return target?.Value.Value;
        }

        public bool List_GlobalTarget_HasTagValue(IKTag tag) => GlobalTargets.Exists(x => x.ID == tag && x.Value.Value != null);


        private void OnDrawGizmosSelected()
        {
            if (!enabled) return;

            if (sets != null && sets.Count > 0 && animator != null)
            {
                for (int k = 0; k < sets.Count; k++)
                {
                    var set = sets[k];

                    if (!set.active) continue;

                    //Paint the Weight Processors
                    if (set.weightProcessors != null)
                    {
                        foreach (var weightP in set.weightProcessors)
                        {
                            weightP?.OnDrawGizmos(set, animator);
                        }
                    }

                    if (set != null && SelectedSet == k && set.active && set.Processors != null)
                    {
                        for (int i = 0; i < set.Processors.Count; i++)
                        {
                            var link = set.Processors[i];
                            if (link != null && link.Active && set.SelectedIKProcessor == i)
                                link.OnDrawGizmos(this, set, animator, Weight);
                        }
                    }
                }
            }
        }


        private void OnValidate()
        {
            foreach (var set in sets)
            {
                if (set.aimer == null) set.aimer = this.FindComponent<Aim>();

                set.OnValidate(this);
            }

            if (Application.isPlaying)
            {
                OnValidateAction?.Invoke(this);
            }

            //Autofill some common tags like head, hand, foot, etc... if they are not already in the global targets list for easier use on the IK Processors
            AutoFillTargetsOnValidate();
        }
        public System.Action<IKManager> OnValidateAction { get; set; }


    }


#if UNITY_EDITOR
    [CustomEditor(typeof(IKManager))]
    public class IKManagerEditor : Editor
    {
        ReorderableList Reo_Sets;
        ReorderableList Reo_GlobalTargets;

        private readonly Dictionary<string, ReorderableList> Reo_Links = new();
        List<Type> derivedTypes;

        IKManager m;
        private int result;

        SerializedProperty GlobalWeight, IKSets, animator, EditorTabs, SelectedSet, GlobalTargets

            ;

        private List<string> floatAnimParam;

        private void OnEnable()
        {
            m = (IKManager)target;

            animator = serializedObject.FindProperty("animator");
            IKSets = serializedObject.FindProperty("sets");
            EditorTabs = serializedObject.FindProperty("EditorTabs");
            GlobalWeight = serializedObject.FindProperty("Weight");
            SelectedSet = serializedObject.FindProperty("SelectedSet");
            GlobalTargets = serializedObject.FindProperty("GlobalTargets");

            ReordableLists();

            derivedTypes = MTools.GetAllTypes<IKProcessor>();

            FindAllFloatParameters();

            FindBonesFromTags();
        }

        private void ReordableLists()
        {
            Reo_Sets = new ReorderableList(serializedObject, IKSets, true, true, true, true)
            {
                drawHeaderCallback = Draw_Header_Set,
                drawElementCallback = Draw_Element,
                onAddCallback = OnAddCallBack,
                onSelectCallback = (list) => { SelectedSet.intValue = list.index; }
            };

            SelectedSet.intValue = Reo_Sets.index;

            Reo_GlobalTargets = new ReorderableList(serializedObject, GlobalTargets, true, true, true, true)
            {
                drawHeaderCallback = rect =>
                {
                    const float k_ButtonWidth = 90f;
                    var labelRect = new Rect(rect.x, rect.y, rect.width - k_ButtonWidth - 4f, rect.height);

                    EditorGUI.LabelField(labelRect, new GUIContent("Global Targets", "Tag + Transform pairs shared across all IK Sets"));

                    //only show the button if the global targets is empty
                    if (GlobalTargets.arraySize == 0)
                    {
                        var prevColor = GUI.color;
                        var buttonRect = new Rect(rect.xMax - k_ButtonWidth, rect.y, k_ButtonWidth, rect.height);
                        GUI.color = new Color(0.4f, 1f, 0.4f, 1f);
                        if (GUI.Button(buttonRect, new GUIContent("Get Targets", "Collect all unique Transforms from every IKSet and append them to this list")))
                            GatherTargetsFromSets();
                        GUI.color = prevColor;
                    }

                    //add a button to clear all the global targets
                    if (GlobalTargets.arraySize > 0)
                    {
                        var prevColor = GUI.color;
                        var buttonRect = new Rect(rect.xMax - k_ButtonWidth, rect.y, k_ButtonWidth, rect.height);
                        GUI.color = new Color(1f, 0.4f, 0.4f, 1f);
                        if (GUI.Button(buttonRect, new GUIContent("Clear Targets", "Clear all the global targets")))
                        {
                            if (EditorUtility.DisplayDialog("Clear Global Targets", "Are you sure you want to clear all global targets? This action cannot be undone.", "Yes", "No"))
                            {
                                GlobalTargets.arraySize = 0;
                                serializedObject.ApplyModifiedProperties();
                            }

                        }
                        GUI.color = prevColor;
                    }
                },

                drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    rect.y += 2;
                    if (GlobalTargets.arraySize <= index) return;

                    var element = GlobalTargets.GetArrayElementAtIndex(index);
                    var id = element.FindPropertyRelative("ID");
                    var value = element.FindPropertyRelative("Value");

                    var height = EditorGUIUtility.singleLineHeight;
                    var half = (rect.width - 4f) * 0.50f;
                    var space = 15;

                    var idRect = new Rect(rect.x + space, rect.y, half - space, rect.height - 4f);
                    var valueRect = new Rect(rect.x + half + 4f + space, rect.y, rect.width - half - 4f - space, rect.height - 4f);

                    EditorGUIUtility.labelWidth = 1;
                    EditorGUI.PropertyField(idRect, id, GUIContent.none);
                    EditorGUI.PropertyField(valueRect, value, GUIContent.none);
                    EditorGUIUtility.labelWidth = 0;
                },

                elementHeightCallback = _ => EditorGUIUtility.singleLineHeight + 4f,

                onAddCallback = list =>
                {
                    list.serializedProperty.arraySize++;
                    serializedObject.ApplyModifiedProperties();
                },
            };
        }

        private void FindAllFloatParameters()
        {
            try
            {
                if (animator != null && animator.objectReferenceValue != null && floatAnimParam == null)
                {
                    if (m.gameObject.activeInHierarchy)
                    {
                        floatAnimParam = new() { "None" };
                        result = 0;

                        var anim = m.animator;

                        if (anim == null || anim.runtimeAnimatorController == null) return;

                        for (int i = 0; i < anim.parameterCount; i++)
                        {
                            var parameter = anim.GetParameter(i);

                            if (parameter.type == UnityEngine.AnimatorControllerParameterType.Float)
                            {
                                floatAnimParam.Add(parameter.name);
                            }
                        }
                    }
                }

            }
            catch
            {
                //do nothing
            }
        }

        private void OnAddCallBack(ReorderableList list)
        {
            m.sets ??= new();

            m.sets.Add(
                new IKSet()
                {
                    // m_name = $"",
                    name = new StringReference($"newIK Set {list.count}") { UseConstant = true },

                    //Targets = new Scriptables.TransformReference[1],
                    EnableTime = 0.25f,
                    DisableTime = 0.25f
                }
                );

            IKSets.serializedObject.ApplyModifiedProperties();

            EditorUtility.SetDirty(m);
        }
        private void Draw_Header_Set(Rect rect)
        {
            var r = new Rect(rect);
            var WeightRect = new Rect(rect) { x = r.width - 30, width = 65 };
            var a = new Rect(rect) { width = 65 };
            EditorGUI.LabelField(a, new GUIContent("Active", "Enable Disable the IK Set"));
            r.x += 60;
            r.width = 60;
            EditorGUI.LabelField(r, new GUIContent("IK Set", "Name of the IK Profile "));
            EditorGUI.LabelField(WeightRect, new GUIContent("Weight", "Modes are the Animations that can be played on top of the States"));
        }

        private void Draw_Element(Rect rect, int index, bool isActive, bool isFocused)
        {
            rect.y += 2;
            if (IKSets.arraySize <= index) return;

            var ikSet = IKSets.GetArrayElementAtIndex(index);
            var active = ikSet.FindPropertyRelative("active");
            //var name = ikSet.FindPropertyRelative("m_name");
            var Name = ikSet.FindPropertyRelative("name");
            var weight = ikSet.FindPropertyRelative("weight");

            var height = EditorGUIUtility.singleLineHeight;

            var activeRect = new Rect(rect.x, rect.y, 20, height);
            var NameRect = new Rect(rect.x + 35, rect.y, rect.width * 0.7f - 35, height);
            var weightRect = new Rect(rect.width - rect.width * 0.3f + 25, rect.y, rect.width * 0.3f + 12f, height);

            EditorGUIUtility.labelWidth = 30;

            var dC = GUI.color;
            if (index == SelectedSet.intValue)
                GUI.color = Color.yellow;


            active.boolValue = EditorGUI.Toggle(activeRect, GUIContent.none, active.boolValue);
            EditorGUI.PropertyField(NameRect, Name, GUIContent.none);
            EditorGUI.PropertyField(weightRect, weight, new GUIContent(" "));
            EditorGUIUtility.labelWidth = 0;

            GUI.color = dC;
        }

        private static string[] EditorLabel = new string[] { "IK Processors", "Weight Processors", "Events", "Old Targets" };


        /// <summary> Cached style to use to draw the popup button. </summary>
        private GUIStyle popupStyle;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();


            popupStyle ??= new GUIStyle(GUI.skin.GetStyle("PaneOptions"))
            {
                imagePosition = ImagePosition.ImageOnly,
                margin = new RectOffset(0, 0, 3, 0)
            };

            MalbersEditor.DrawDescription("Manage all IK logic for all components");


            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(animator);
                EditorGUILayout.PropertyField(GlobalWeight);
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GlobalWeight.isExpanded = MalbersEditor.Foldout(GlobalWeight.isExpanded, $"Global IK Targets [{Reo_GlobalTargets.count}]");
                if (GlobalWeight.isExpanded)
                    Reo_GlobalTargets.DoLayoutList();
            }

            Reo_Sets.DoLayoutList();

            var index = SelectedSet.intValue = SelectedSet.intValue;

            if (Reo_Sets.count > 0 && index > -1 && index < Reo_Sets.count)
            {
                var selectedSet = IKSets.GetArrayElementAtIndex(index);
                var Name = m.sets[index].name.Value;

                //EditorGUILayout.Space(-18);
                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUI.indentLevel++;
                    {
                        Rect rowRect = EditorGUILayout.GetControlRect();
                        Rect indented = EditorGUI.IndentedRect(rowRect);
                        bool hasPaste = IKSetCopyPaste.HasClipboard;

                        const float BtnW = 58f, Gap = 4f;
                        float buttonsTotal = BtnW + Gap + (hasPaste ? BtnW + Gap : 0f);

                        Rect foldoutRect = new(indented.x, indented.y, indented.width - buttonsTotal, indented.height);
                        Rect copyRect = new(indented.xMax - buttonsTotal + Gap, indented.y, BtnW, indented.height);

                        // Zero indent so Foldout doesn't double-offset the already-indented rect
                        int savedIndent = EditorGUI.indentLevel;
                        EditorGUI.indentLevel = 0;
                        selectedSet.isExpanded = EditorGUI.Foldout(foldoutRect, selectedSet.isExpanded, $"[{Name} - IK Set]", true);
                        EditorGUI.indentLevel = savedIndent;

                        if (GUI.Button(copyRect, new GUIContent("Copy", "Copy IK Processors and Weight Processors from this IK Set")))
                            IKSetCopyPaste.CopyProcessorsFrom(m.sets[index]);

                        if (hasPaste)
                        {
                            Rect pasteRect = new(copyRect.xMax + Gap, indented.y, BtnW, indented.height);
                            if (GUI.Button(pasteRect, new GUIContent("Paste", $"Paste IK Processors and Weight Processors from \"{IKSetCopyPaste.ClipboardSetName}\"")))
                            {
                                Undo.RecordObject(m, "Paste IK Processors");
                                IKSetCopyPaste.PasteProcessorsInto(m.sets[index]);
                                EditorUtility.SetDirty(m);
                            }
                        }
                    }
                    EditorGUI.indentLevel--;

                    //using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                    //{
                    //var defaultGuiColor = GUI.contentColor;
                    //GUI.contentColor = Color.yellow;
                    //selectedSet.isExpanded = MalbersEditor.Foldout(selectedSet.isExpanded, $"[[{selectedSet.displayName}] IK Set]");
                    //GUI.contentColor = defaultGuiColor;

                    var Target = selectedSet.FindPropertyRelative("Targets");

                    var processorsAmount = m.sets[index].Processors.Count;
                    var weightAmount = m.sets[index].weightProcessors.Count;
                    EditorLabel = new string[] { $"IK Set [{processorsAmount}]", $"Weight Processors [{weightAmount}]", "Events" };

                    if (selectedSet.isExpanded)
                    {
                        EditorTabs.intValue = GUILayout.Toolbar(EditorTabs.intValue, EditorLabel);

                        if (EditorTabs.intValue == 0)
                        {
                            DrawProcessors(index, selectedSet, Target);
                        }
                        else if (EditorTabs.intValue == 1)
                        {
                            DrawWeights(index, selectedSet);
                        }
                        else if (EditorTabs.intValue == 2)
                        {
                            DrawEvents(selectedSet);
                        }
                    }
                }
            }
            serializedObject.ApplyModifiedProperties();

            //base.OnInspectorGUI();

        }

        private static void DrawEvents(SerializedProperty selectedSet)
        {
            var OnWeightChanged = selectedSet.FindPropertyRelative("OnWeightChanged");
            var OnSetEnable = selectedSet.FindPropertyRelative("OnSetEnable");
            var OnSetDisable = selectedSet.FindPropertyRelative("OnSetDisable");

            EditorGUILayout.PropertyField(OnWeightChanged);
            EditorGUILayout.PropertyField(OnSetEnable);
            EditorGUILayout.PropertyField(OnSetDisable);
        }

        private void DrawWeights(int index, SerializedProperty selectedSet)
        {
            var weights = selectedSet.FindPropertyRelative("weightProcessors");
            var LerpWeight = selectedSet.FindPropertyRelative("LerpWeight");

            DrawFinalWeight(index);

            EditorGUILayout.PropertyField(LerpWeight);

            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(weights);
            //   EditorGUILayout.PropertyField(Target);
            EditorGUI.indentLevel--;
        }

        private void DrawProcessors(int index, SerializedProperty selectedSet, SerializedProperty Target)
        {
            DrawFinalWeight(index);

            var EnableTime = selectedSet.FindPropertyRelative("EnableTime");
            EnableTime.isExpanded = MalbersEditor.Foldout(EnableTime.isExpanded, $"IK Set General Properties");

            if (EnableTime.isExpanded)
            {

                var InvertAnimParameter = selectedSet.FindPropertyRelative("InvertAnimParameter");
                var DisableTime = selectedSet.FindPropertyRelative("DisableTime");
                var aimer = selectedSet.FindPropertyRelative("aimer");
                var EnterLerp = selectedSet.FindPropertyRelative("EnterLerp");
                var ExitLerp = selectedSet.FindPropertyRelative("ExitLerp");

                // var Name = selectedSet.FindPropertyRelative("name");
                //  EditorGUILayout.PropertyField(Name);

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

                EditorGUILayout.PropertyField(aimer);
            }

            var ClearTargetsOnDisable = selectedSet.FindPropertyRelative("ClearTargetsOnDisable");
            var IKProcesors = selectedSet.FindPropertyRelative("IKProcesors");

            if (Target.arraySize > 0)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(Target);
                EditorGUI.indentLevel--;
                EditorGUILayout.PropertyField(ClearTargetsOnDisable);
            }
            DrawProfile(selectedSet, IKProcesors);
        }

        private void DrawFinalWeight(int index)
        {
            if (Application.isPlaying)
            {
                var guiColor = GUI.color;
                GUI.color = Color.yellow;
                using (new EditorGUI.DisabledGroupScope(true))
                    EditorGUILayout.FloatField("Final Weight", m.sets[index].FinalWeight);
                GUI.color = guiColor;

                //Repaint();
            }
        }

        void DrawProfile(SerializedProperty selectedSet, SerializedProperty link)
        {
            ReorderableList ReoLink;
            string listKey = selectedSet.propertyPath;

            var SelectedIKProcessor = selectedSet.FindPropertyRelative("SelectedIKProcessor");


            if (Reo_Links.ContainsKey(listKey))
            {
                // fetch the reorderable list in dict
                ReoLink = Reo_Links[listKey];
            }
            else
            {
                ReoLink = new ReorderableList(selectedSet.serializedObject, link, true, true, true, true)
                {
                    drawElementCallback = (rect, index, isActive, isFocused) =>
                    {
                        var element = link.GetArrayElementAtIndex(index);
                        if (element.managedReferenceValue == null) return;

                        var active = element.FindPropertyRelative("Active");
                        var name = element.FindPropertyRelative("name");
                        var Weight = element.FindPropertyRelative("Weight");
                        var Tag = element.FindPropertyRelative("Tag");

                        var height = EditorGUIUtility.singleLineHeight;
                        float buttonWidth = 5;

                        var activeRect = new Rect(rect.x, rect.y, 20, height);


                        var IKSet = m.sets[SelectedSet.intValue];

                        if (IKSet == null || IKSet.Processors == null || index >= IKSet.Processors.Count) return;

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

                            AddNewItem(Duplicate, Reo_Links[listKey], selectedSet);
                        }

                        EditorGUIUtility.labelWidth = 0;
                    },

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

                        GUI.color = Color.green;

                        if (GUI.Button(button, "Verify"))
                        {
                            m.sets[SelectedSet.intValue].Verify(m, m.animator);
                        }

                        GUI.color = defaultGuiColor;
                    },

                    onAddDropdownCallback = (Rect buttonRect, ReorderableList list) =>
                    {
                        var menu = new GenericMenu();

                        foreach (var type in derivedTypes)
                        {
                            var att = type.GetCustomAttribute<AddTypeMenuAttribute>(false); //Find the correct name
                            string LabelName = att != null ? att.MenuName : type.Name;

                            menu.AddItem(new GUIContent(LabelName), false, (x) => AddNewItem(x, list, selectedSet), Activator.CreateInstance(type));
                        }
                        menu.ShowAsContext();
                    },

                    onSelectCallback = (list) => { SelectedIKProcessor.intValue = list.index; }
                };

                Reo_Links.Add(listKey, ReoLink);  //Store it on the Editor
            }

            ReoLink.DoLayoutList();
            var index = ReoLink.index = SelectedIKProcessor.intValue;

            if (index != -1 && index < link.arraySize)
            {
                // Debug.Log("SelectedAbility = " + SelectedAbility);
                SerializedProperty ikProcessor = link.GetArrayElementAtIndex(index);

                if (ikProcessor != null && ikProcessor.managedReferenceValue != null)
                {
                    EditorGUILayout.Space(-16);

                    var set = m.sets[SelectedSet.intValue];
                    var processor = set.IKProcesors[index];

                    using (new GUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField($"[{ikProcessor.managedReferenceValue.GetType().Name}]", EditorStyles.boldLabel, GUILayout.Width(150));
                        using (new EditorGUI.DisabledGroupScope(true))
                            EditorGUILayout.ObjectField(GUIContent.none, processor.Bone, typeof(Transform), false, GUILayout.Width(200));
                    }

                    using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        var TargetIndex = ikProcessor.FindPropertyRelative("TargetIndex");

                        var targets = m.sets[SelectedSet.intValue].Targets;
                        var TargLength = targets.Length;
                        var TIndex = TargetIndex.intValue;


                        if (set == null || set.Processors == null || index >= set.Processors.Count) return;


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


                        if (ikProcessor.isExpanded)
                        {
                            if (animator.objectReferenceValue != null)
                            {
                                FindAllFloatParameters();

                                var AnimParameter = ikProcessor.FindPropertyRelative("AnimParameter");
                                var AnimParameterHash = ikProcessor.FindPropertyRelative("AnimParameterHash");

                                using (new GUILayout.HorizontalScope())
                                {
                                    EditorGUI.indentLevel++;
                                    EditorGUI.indentLevel++;
                                    EditorGUILayout.PropertyField(AnimParameter, new GUIContent("Anim Param [IKProcessor]", "Local Anim Parameter to apply to a specific IK Processor. E.g Use the Anim Curve for the Left Hand and another anim curve for the Right Hand"));
                                    EditorGUI.indentLevel--;
                                    EditorGUI.indentLevel--;

                                    if (m.gameObject.activeInHierarchy)
                                    {
                                        using (var cc = new EditorGUI.ChangeCheckScope())
                                        {
                                            result = EditorGUILayout.Popup(result, floatAnimParam.ToArray(), popupStyle, GUILayout.Width(12));

                                            if (cc.changed)
                                            {
                                                //Update the Name using the Animator Float Parameters
                                                Undo.RecordObject(target, "Set Anim Parameter");
                                                AnimParameter.stringValue = result == 0 ? string.Empty : floatAnimParam[result];
                                                //Update the Hash
                                                AnimParameterHash.intValue = result == 0 ? 0 : Animator.StringToHash(AnimParameter.stringValue);
                                                serializedObject.ApplyModifiedProperties();
                                                serializedObject.Update();
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        void AddNewItem(object target, ReorderableList list, SerializedProperty property)
        {
            var index = list.serializedProperty.arraySize;
            list.serializedProperty.arraySize++;
            list.index = index;
            IKProcessor link = (IKProcessor)target;
            link.name = target.GetType().Name;
            var element = list.serializedProperty.GetArrayElementAtIndex(index);
            element.managedReferenceValue = target;
            element.isExpanded = true;

            property.serializedObject.ApplyModifiedProperties();
        }

        private void GatherTargetsFromSets()
        {
            Undo.RecordObject(m, "Gather IKSet Targets");

            m.GlobalTargets ??= new();

            // Build a set of transforms already present for O(1) duplicate check
            var existing = new HashSet<Transform>();
            foreach (var entry in m.GlobalTargets)
            {
                if (entry?.Value?.Value != null)
                    existing.Add(entry.Value.Value);
            }

            int added = 0;
            foreach (var set in m.sets)
            {
                if (set?.Targets == null) continue;
                foreach (var tRef in set.Targets)
                {
                    var t = tRef?.Value;
                    if (t == null || !existing.Add(t)) continue; // Add returns false if already present

                    var newGlobal = new IDPair<IKTag, TransformReference>(FindTagByName(t), new(t));

                    m.GlobalTargets.Add(newGlobal);
                    added++;
                }
            }

            EditorUtility.SetDirty(m);
            serializedObject.Update();
            Debug.Log($"[IKManager] Get Targets: added {added} unique Transform(s) to Global Targets.", m);



        }

        private IKTag FindTagByName(Transform t)
        {
            if (t == m.transform) return MTools.GetInstance<IKTag>("IK_Root");

            var n = t.name.ToLower();

            if (KeyWord(n, "head")) return MTools.GetInstance<IKTag>("IK_Head");
            if (KeyWord(n, "neck")) return MTools.GetInstance<IKTag>("IK_Neck 1");
            if (KeyWord(n, "hip")) return MTools.GetInstance<IKTag>("IK_Hip");
            if (KeyWord(n, "spine0")) return MTools.GetInstance<IKTag>("IK_Spine0");
            if (KeyWord(n, "spine1")) return MTools.GetInstance<IKTag>("IK_Chest");
            if (KeyWord(n, "chest")) return MTools.GetInstance<IKTag>("IK_Chest");
            if (KeyWord(n, "spine2")) return MTools.GetInstance<IKTag>("IK_UpperChest");
            if (KeyWord(n, "uppperchest")) return MTools.GetInstance<IKTag>("IK_UpperChest");


            if (KeyWord(n, "hand") && KeyWord(n, "goal") && KeyWord(n, "left", "_l", " l")) return MTools.GetInstance<IKTag>("IK_LeftHand_Goal");
            if (KeyWord(n, "hand") && KeyWord(n, "goal") && KeyWord(n, "right", "_r", " r")) return MTools.GetInstance<IKTag>("IK_RightHand_Goal");
            if (KeyWord(n, "foot") && KeyWord(n, "goal") && KeyWord(n, "left", "_l", " l")) return MTools.GetInstance<IKTag>("IK_LeftFoot_Goal");
            if (KeyWord(n, "foot") && KeyWord(n, "goal") && KeyWord(n, "right", "_r", " r")) return MTools.GetInstance<IKTag>("IK_RightFoot_Goal");

            if (KeyWord(n, "hand") && KeyWord(n, "left", "_l", " l")) return MTools.GetInstance<IKTag>("IK_LeftHand");
            if (KeyWord(n, "hand") && KeyWord(n, "right", "_r", " r")) return MTools.GetInstance<IKTag>("IK_RightHand");
            if (KeyWord(n, "foot") && KeyWord(n, "left", "_l", " l")) return MTools.GetInstance<IKTag>("IK_LeftFoot");
            if (KeyWord(n, "foot") && KeyWord(n, "right", "_r", " r")) return MTools.GetInstance<IKTag>("IK_RightFoot");

            return null;
        }

        private bool KeyWord(string name, params string[] keyword)
        {
            foreach (var item in keyword)
            {
                if (name.Contains(item.ToLower())) return true;
            }
            return false;
        }


        public void FindBonesFromTags()
        {
            foreach (var item in m.GlobalTargets)
            {
                if (item.Value == null)
                {
                    switch (item.ID.name)
                    {
                        case "head": item.Value = new TransformReference(m.transform.FindGrandChildinSkin("head")); break;
                        default: break;
                    }
                }
            }
        }


        private void OnSceneGUI()
        {
            if (m.sets != null && m.sets.Count > 0 && m.animator != null)
            {
                for (int k = 0; k < m.sets.Count; k++)
                {
                    var set = m.sets[k];

                    if (set != null && m.SelectedSet == k && set.active && set.Processors != null)
                    {
                        for (int i = 0; i < set.Processors.Count; i++)
                        {
                            var link = set.Processors[i];

                            if (link != null && link.Active && set.SelectedIKProcessor == i)
                                link.OnSceneGUI(set, m.animator, target, i);
                        }
                    }
                }
            }
        }
    }
#endif
}