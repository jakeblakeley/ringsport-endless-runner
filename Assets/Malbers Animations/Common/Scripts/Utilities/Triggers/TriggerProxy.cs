using UnityEngine;
using MalbersAnimations.Events;
using MalbersAnimations.Scriptables;
using System.Collections.Generic;
using System;
using UnityEngine.Events;
using MalbersAnimations.Conditions;
using MalbersAnimations.Reactions;
using System.Collections;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.Utilities
{
    /// <summary>
    /// This is used when the collider is in a different gameObject and you need to check the Collider Events
    /// Create this component at runtime and subscribe to the UnityEvents </summary>
    [AddComponentMenu("Malbers/Utilities/Colliders/Trigger Proxy")]
    public class TriggerProxy : MonoBehaviour
    {
        [Tooltip("Hit Layer for the Trigger Proxy")]
        [SerializeField] private LayerReference hitLayer = new(0);
        public LayerMask Layer { get => hitLayer.Value; set => hitLayer.Value = value; }


        [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;


        [SerializeField] private Tag[] Tags_Legacy; //Old way


        [Tooltip("Search only Tags")] public MTags tags;

        public ColliderEvent OnTrigger_Enter = new();
        public ColliderEvent OnTrigger_Exit = new();
        public ColliderEvent OnTrigger_Stay = new();

        public GameObjectEvent OnGameObjectEnter = new();
        public GameObjectEvent OnGameObjectExit = new();
        public GameObjectEvent OnGameObjectStay = new();
        public UnityEvent OnEmpty = new();

        [Tooltip("ID of the Trigger Proxy. This is used to identify the Trigger Proxy in the Editor and at runtime")]
        public IntReference ID = new(-1);


        public Reaction2 TriggerEnterReaction;
        public Reaction2 TriggerExitReaction;
        public Reaction2 TriggerStayReaction;

        public Reaction2 GameObjectEnterReaction;
        public Reaction2 GameObjectExitReaction;
        public Reaction2 GameObjectStayReaction;

        [SerializeField] private bool m_debug = false;

        public BoolReference useOnTriggerStay = new();

        [Tooltip("Trigger will be disabled the first time it finds a valid collider")]
        public BoolReference OneTimeUse = new();
        [Tooltip("Do not Interact with static colliders")]
        public BoolReference ignoreStatic = new();

        protected internal HashSet<Collider> m_colliders = new(8);
        /// <summary>All the Gameobjects using the Proxy</summary>
        protected internal HashSet<GameObject> EnteringGameObjects = new(8);

        //MWC: reusable buffer so we don't allocate a new List every time we sweep colliders (enter/disable)
        private readonly List<Collider> _tempColliders = new(8);

        [Tooltip("Extra conditions to check to filter the colliders entering OnTrigger Enter")]
        public Conditions2 Conditions = new();

        public Action<GameObject, Collider, TriggerProxy> EnterTriggerInteraction;
        public Action<GameObject, Collider, TriggerProxy> ExitTriggerInteraction;

        /// <summary> Is this component enabled? /summary>
        public bool Active { get => enabled; set => enabled = value; }

        //public int ID { get => m_ID.Value; set => m_ID.Value = value; }

        public QueryTriggerInteraction TriggerInteraction { get => triggerInteraction; set => triggerInteraction = value; }

        /// <summary> Collider Component used for the Trigger Proxy </summary>
        [RequiredField] public Collider ownCollider;
        public Transform Owner { get; set; }

        public virtual bool TrueConditions(Collider other)
        {
            if (!Active) return false;

            if (tags != null && tags.Length > 0)
            {
                if (!other.gameObject.HasMalbersTagInParent(tags)) return false;
            }

            if (ownCollider == null) return false; // You don't have a trigger
            if (other == null) return false; // you are CALLING A ELIMINATED ONE

            if (other.gameObject.isStatic && ignoreStatic.Value) return false; // you are CALLING A ELIMINATED ONE

            if (triggerInteraction == QueryTriggerInteraction.Ignore && other.isTrigger) return false; // Check Trigger Interactions 
            if (!MTools.Layer_in_LayerMask(other.gameObject.layer, Layer)) return false;
            if (transform.SameHierarchy(other.transform)) return false;                 // Do not Interact with yourself
            if (Owner != null && other.transform.SameHierarchy(Owner)) return false;    // Do not Interact with yourself

            if (!Conditions.Evaluate(other)) return false; // Check the conditions

            return true;
        }

        public virtual void OnTriggerEnter(Collider other)
        {
            if (!TrueConditions(other)) return;

            GameObject realRoot = MTools.FindRealRoot(other);

            if (m_colliders.Add(other)) //if the entering collider is not already on the list add it
            {
                //MWC: fire Collider-enter events only when it's actually a new collider (not a duplicate re-send)
                OnTrigger_Enter.Invoke(other); //Invoke when a Collider enters the Trigger
                TriggerEnterReaction.React(other);

                if (m_debug) Debug.Log($"<b>{name}</b> [Entering Collider] -> [{other.name}]", this);

                AddTarget(other);

                RemoveDisabledColliders();
            }

            if (EnteringGameObjects.Contains(realRoot))
            {
                return;
            }
            else
            {
                EnterTriggerInteraction?.Invoke(realRoot, other, this);
                EnteringGameObjects.Add(realRoot);
                OnGameObjectEnter.Invoke(realRoot);
                GameObjectEnterReaction.React(realRoot);

                if (m_debug) Debug.Log($"<b>{name}</b> [Entering GameObject] -> [{realRoot.name}]", this);

                if (OneTimeUse.Value) enabled = false;
            }
        }

        private void RemoveDisabledColliders()
        {
            //MWC: reuse cached buffer instead of allocating a new List on every collider enter
            _tempColliders.Clear();
            _tempColliders.AddRange(m_colliders);

            foreach (var col in _tempColliders)
            {
                if (col == null || !col.enabled)
                {
                    RemoveTrigger(col, true);
                }
            }
        }

        public virtual void OnTriggerExit(Collider other) => TriggerExit(other, true);

        public virtual void TriggerExit(Collider other, bool remove)
        {
            //MWC: Exit must NOT re-evaluate the enter conditions. Active/Layer/Tags/Conditions can change
            //while a collider is inside; if we re-checked them here a changed condition would strand the
            //collider in the set forever (no exit, no OnGameObjectExit, no OnEmpty). Gate on membership instead.
            if (other != null && m_colliders.Contains(other))
                RemoveTrigger(other, remove);
        }

        public virtual void RemoveTrigger(Collider other, bool remove)
        {
            //MWC: 'other' can be a destroyed (null) collider here (e.g. RemoveDisabledColliders detecting a
            //destroyed member). FindRealRoot dereferences collider.transform, so guard against null.
            GameObject realRoot = other != null ? MTools.FindRealRoot(other) : null;

            OnTrigger_Exit.Invoke(other);
            TriggerExitReaction.React(other);

            m_colliders.Remove(other);
            RemoveTarget(other, remove);

            if (m_debug && other) Debug.Log($"<b>{name}</b> [Exit Collider] -> [{other.name}]", this);

            //MWC: only resolve/clean the owning GameObject when we still have a valid root
            if (realRoot != null && EnteringGameObjects.Contains(realRoot)) //Means that the Entering GameObject still exist
            {
                // 0 allocation and lightweight method.
                bool anyMatchingColliders = false;
                foreach (var c in m_colliders)
                {
                    if (c && c.transform.SameHierarchy(realRoot.transform))
                    {
                        anyMatchingColliders = true;
                        break;
                    }
                }

                if (!anyMatchingColliders)
                {
                    EnteringGameObjects.Remove(realRoot);
                    OnGameObjectExit.Invoke(realRoot);
                    GameObjectExitReaction.React(realRoot);
                    ExitTriggerInteraction?.Invoke(realRoot, other, this);

                    if (m_debug) Debug.Log($"<b>{name}</b> [Leaving Gameobject] -> [{realRoot.name}]", this);
                }
            }

            if (m_colliders.Count == 0) ResetTrigger();

        }

        /// <summary>Add a Trigger Target to every new Collider found</summary>
        protected virtual void AddTarget(Collider other)
        {
            if (!other) return;
            var triggerTarget = TriggerRegistry.GetTargetForCollider(other);

            if (!triggerTarget)
            {
                triggerTarget = other.gameObject.AddComponent<TriggerTarget>();
                triggerTarget.AutoAdded = true; //MWC: flag so the component removes itself when no proxies reference it
            }

            triggerTarget.AddProxy(this);
        }


        /// <summary>OnTrigger exit Logic</summary>
        internal void RemoveTarget(Collider other, bool remove)
        {
            var triggerTarget = TriggerRegistry.GetTargetForCollider(other);

            if (!triggerTarget)
            {
                return;
            }

            if (remove)
                triggerTarget.RemoveProxy(this);
        }

        public virtual void ResetTrigger()
        {
            m_colliders.Clear();
            EnteringGameObjects.Clear();
            OnEmpty.Invoke();

            StopAllCoroutines();

            //MWC: Unity throws "Coroutine couldn't be started because the game object is inactive" if we start
            //while the component is disabled (e.g. ResetTrigger called from OnDisable). Guard on active & enabled.
            if (useOnTriggerStay.Value && isActiveAndEnabled)
            {
                StartCoroutine(C_TriggerStay());
            }
        }

        public virtual void OnDisable()
        {
            //MWC: route every tracked collider through the normal RemoveTrigger path so full exit semantics
            //fire on disable — OnTrigger_Exit + TriggerExitReaction per collider, and OnGameObjectExit +
            //GameObjectExitReaction + ExitTriggerInteraction + OnEmpty as each owning GameObject empties.
            //Iterate a copy because RemoveTrigger mutates m_colliders.
            if (m_colliders.Count > 0)
            {
                _tempColliders.Clear();
                _tempColliders.AddRange(m_colliders);

                foreach (var c in _tempColliders)
                    RemoveTrigger(c, true); //the last removal empties the set and calls ResetTrigger()/OnEmpty
            }
            else
            {
                ResetTrigger(); //MWC: nothing was inside, but still clear state / stop the Stay coroutine
            }

            if (m_debug) Debug.Log($"<b>{name}</b> [Exit All Colliders and Triggers] ", this);

            //MWC: guard against a missing collider so disabling the proxy never throws
            if (ownCollider) ownCollider.enabled = false; //Disable the Collider when the Trigger Proxy is disabled
        }

        public virtual void OnEnable()
        {
            //MWC: null-guard so a proxy without a collider doesn't throw on enable
            if (ownCollider) ownCollider.enabled = true; //Enable the Collider when the Trigger Proxy is enabled
            ResetTrigger();
        }

        public virtual void Awake()
        {
            if (ownCollider == null) ownCollider = GetComponent<Collider>();

            if (ownCollider) ownCollider.isTrigger = true;
            else
                Debug.LogWarning("This Script requires a Collider, please add any type of collider", this);

            if (Owner == null) Owner = transform;

            ResetTrigger();
        }

        public virtual void Activate(bool value)
        {
            if (value && !gameObject.activeSelf)
                gameObject.SetActive(true); //Activate the GameObject if it is not active

            enabled = value;
        }

        //protected virtual void Update()
        //{
        //    CheckOntriggerStay();
        //}

        IEnumerator C_TriggerStay()
        {
            while (true)
            {
                yield return null;
                CheckOntriggerStay();
            }
        }

        /// <summary> MWC: Public hook to purge colliders that were individually disabled/destroyed while inside
        /// the trigger. Unity does NOT send OnTriggerExit for a Collider.enabled=false (only for GameObject
        /// deactivation, which TriggerTarget already covers). Call this after toggling a member collider off,
        /// or rely on the automatic sweep when 'Use On Trigger Stay' is enabled. </summary>
        public void RefreshColliders() => RemoveDisabledColliders();

        public virtual void CheckOntriggerStay()
        {
            //MWC: sweep out any colliders that were disabled/destroyed since last frame so Stay/exit stay accurate
            RemoveDisabledColliders();

            foreach (var gos in EnteringGameObjects)
            {
                OnGameObjectStay.Invoke(gos);
                GameObjectStayReaction.React(gos);
            }

            foreach (var col in m_colliders)
            {
                OnTrigger_Stay.Invoke(col);
                TriggerStayReaction.React(col);
            }

        }

        public virtual void SetLayer(LayerMask mask, QueryTriggerInteraction triggerInteraction, Transform Owner, Tag[] tags = null)
        {
            TriggerInteraction = triggerInteraction;

            if (this.tags == null || this.tags.Length == 0)
                this.tags = new MTags(tags);
            else
                this.tags.Merge(tags);

            Layer = mask;
            this.Owner = Owner;

        }

        public virtual void SetLayer(LayerMask mask, QueryTriggerInteraction triggerInteraction, Transform Owner, MTags tags)
        {
            TriggerInteraction = triggerInteraction;

            if (this.tags == null || this.tags.Length == 0)
                this.tags = new MTags(tags);
            else
                this.tags.Merge(tags);


            Layer = mask;
            this.Owner = Owner;

        }

        public static TriggerProxy CheckTriggerProxy
            (Collider col, LayerMask Layer, QueryTriggerInteraction TriggerInteraction, Transform Owner, bool overrideValue = false)
        {
            TriggerProxy Proxy = null;

            if (col == null) return Proxy;

            if (!col.TryGetComponent(out Proxy))
            {
                Proxy = col.gameObject.AddComponent<TriggerProxy>();
                Proxy.SetLayer(Layer, TriggerInteraction, Owner);
            }

            if (overrideValue)
                Proxy.SetLayer(Layer, TriggerInteraction, Owner);
            else
            {
                //merge layer values
                Proxy.Layer |= Layer;
            }

            col.gameObject.SetLayer(2, false);      //Force the Trigger Area to be on the Ignore Raycast Layer
            col.isTrigger = true;                   //Force to be a Trigger

            return Proxy;
        }

        [HideInInspector] public int Editor_Tabs1;

        public bool UpdateMTags;

        private void OnValidate()
        {
            if (!UpdateMTags && Tags_Legacy != null && Tags_Legacy.Length > 0)
            {
                tags = new MTags(Tags_Legacy);
                Tags_Legacy = null;
                // Debug.Log("Updated to MTags AC 1.5.2 ");
                UpdateMTags = true;
            }
        }
    }

    #region Inspector


#if UNITY_EDITOR
    [CanEditMultipleObjects, CustomEditor(typeof(TriggerProxy))]
    public class TriggerProxyEditor : Editor
    {
        SerializedProperty debug, ID,
            OnTrigger_Enter, OnTrigger_Exit, OnEmpty, useOnTriggerStay, OnTrigger_Stay, ignoreStatic, Editor_Tabs1, OneTimeUse,
            triggerInteraction, hitLayer, OnGameObjectEnter, OnGameObjectExit, OnGameObjectStay, Tags, Conditions,
            TriggerEnterReaction, TriggerExitReaction, TriggerStayReaction,
            GameObjectEnterReaction, GameObjectExitReaction, GameObjectStayReaction
            ;

        TriggerProxy m;

        protected string[] Tabs1 = new string[] { "General", "Events", "Reactions" };

        private void OnEnable()
        {
            m = (TriggerProxy)target;
            OnEmpty = serializedObject.FindProperty("OnEmpty");
            triggerInteraction = serializedObject.FindProperty("triggerInteraction");
            useOnTriggerStay = serializedObject.FindProperty("useOnTriggerStay");
            hitLayer = serializedObject.FindProperty("hitLayer");
            debug = serializedObject.FindProperty("m_debug");
            ignoreStatic = serializedObject.FindProperty("ignoreStatic");
            ID = serializedObject.FindProperty("ID");

            OnTrigger_Enter = serializedObject.FindProperty("OnTrigger_Enter");
            OnTrigger_Exit = serializedObject.FindProperty("OnTrigger_Exit");
            OnTrigger_Stay = serializedObject.FindProperty("OnTrigger_Stay");


            OnGameObjectEnter = serializedObject.FindProperty("OnGameObjectEnter");
            OnGameObjectExit = serializedObject.FindProperty("OnGameObjectExit");
            OnGameObjectStay = serializedObject.FindProperty("OnGameObjectStay");

            Tags = serializedObject.FindProperty("tags");
            Editor_Tabs1 = serializedObject.FindProperty("Editor_Tabs1");
            OneTimeUse = serializedObject.FindProperty("OneTimeUse");
            Conditions = serializedObject.FindProperty("Conditions");


            TriggerEnterReaction = serializedObject.FindProperty("TriggerEnterReaction");
            TriggerExitReaction = serializedObject.FindProperty("TriggerExitReaction");
            TriggerStayReaction = serializedObject.FindProperty("TriggerStayReaction");

            GameObjectEnterReaction = serializedObject.FindProperty("GameObjectEnterReaction");
            GameObjectExitReaction = serializedObject.FindProperty("GameObjectExitReaction");
            GameObjectStayReaction = serializedObject.FindProperty("GameObjectStayReaction");
        }


        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            MalbersEditor.DrawDescription("Use this component to do quick OnTrigger Enter/Exit logics");

            Editor_Tabs1.intValue = GUILayout.Toolbar(Editor_Tabs1.intValue, Tabs1);


            switch (Editor_Tabs1.intValue)
            {
                case 0: DrawGeneral(); break;
                case 1: DrawEvents(); break;
                case 2: DrawReactions(); break;
            }

            if (Application.isPlaying)
            {
                using (new EditorGUI.DisabledGroupScope(true))
                {
                    using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.LabelField("Debug", EditorStyles.boldLabel);

                        //   EditorGUILayout.ObjectField("Own Collider", m.trigger, typeof(Collider), false);

                        EditorGUILayout.LabelField("GameObjects (" + m.EnteringGameObjects.Count + ")", EditorStyles.boldLabel);
                        foreach (var item in m.EnteringGameObjects)
                        {
                            if (item != null) EditorGUILayout.ObjectField(item.name, item, typeof(GameObject), false);
                        }

                        EditorGUILayout.LabelField("Colliders (" + m.m_colliders.Count + ")", EditorStyles.boldLabel);

                        foreach (var item in m.m_colliders)
                        {
                            if (item != null) EditorGUILayout.ObjectField(item.name, item, typeof(Collider), false);
                        }

                        //EditorGUILayout.LabelField("Targets (" + m.TriggerTargets.Count + ")", EditorStyles.boldLabel);

                        //foreach (var item in m.TriggerTargets)
                        //{
                        //    if (item != null) EditorGUILayout.ObjectField(item.name, item, typeof(Collider), false);
                        //}
                    }
                    Repaint();
                }
            }
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawReactions()
        {
            EditorGUILayout.PropertyField(TriggerEnterReaction);
            EditorGUILayout.PropertyField(TriggerExitReaction);
            if (m.useOnTriggerStay.Value)
                EditorGUILayout.PropertyField(TriggerStayReaction);

            EditorGUILayout.PropertyField(GameObjectEnterReaction);
            EditorGUILayout.PropertyField(GameObjectExitReaction);
            if (m.useOnTriggerStay.Value)
                EditorGUILayout.PropertyField(GameObjectStayReaction);
        }

        private void DrawGeneral()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(ID);
                    MalbersEditor.DrawDebugIcon(debug);
                }
                EditorGUILayout.PropertyField(hitLayer, new GUIContent("Layer"));

                EditorGUILayout.PropertyField(triggerInteraction);
                EditorGUILayout.PropertyField(useOnTriggerStay);
                EditorGUILayout.PropertyField(OneTimeUse);
                EditorGUILayout.PropertyField(ignoreStatic);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(Tags, true);
                EditorGUILayout.PropertyField(Conditions);
                EditorGUI.indentLevel--;
            }
        }



        private void DrawEvents()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(OnTrigger_Enter, new GUIContent("On Trigger Enter"));
                EditorGUILayout.PropertyField(OnTrigger_Exit, new GUIContent("On Trigger Exit"));
                EditorGUILayout.PropertyField(OnEmpty);
                if (m.useOnTriggerStay.Value)
                    EditorGUILayout.PropertyField(OnTrigger_Stay, new GUIContent("On Trigger Stay"));


                EditorGUILayout.PropertyField(OnGameObjectEnter, new GUIContent("On GameObject Enter "));
                EditorGUILayout.PropertyField(OnGameObjectExit, new GUIContent("On GameObject Exit"));
                if (m.useOnTriggerStay.Value)
                    EditorGUILayout.PropertyField(OnGameObjectStay, new GUIContent("On GameObject Stay"));
            }
        }
    }
#endif
    #endregion
}