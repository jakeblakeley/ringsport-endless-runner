using System.Collections.Generic;
using UnityEngine;
using MalbersAnimations.Scriptables;
using MalbersAnimations.Reactions;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.Controller
{
    /// <summary>
    /// MWC: New reactions-only Zone. A lean sibling of <see cref="Zone"/> that does a single job:
    /// fire Reactions on Enter / Activate / Exit. Mode, State and Stance "zones" are achieved by
    /// composing the matching reactions (ModeReaction, StateReaction, StanceReaction…) on the
    /// Activation/Exit reaction lists, so this component carries no zoneType switch or per-type fields.
    /// </summary>
    [AddComponentMenu("Malbers/Animal Controller/Zone2 (Reaction Zone)")]
    [SelectionBase]
    public class Zone2 : MonoBehaviour, IZone
    {
        public bool debug;

        [Tooltip("As soon as the animal enters the zone it will activate the zone (fire the Activation Reaction). If False then call the method Zone2.Activate()")]
        public BoolReference automatic = new(true);

        [Tooltip("Disable the Zone after it was used")]
        public BoolReference DisableAfterUsed = new();

        [Tooltip("Layer to detect the Animal")]
        public LayerReference Layer = new(1048576); //Animal Layer

        [SerializeField] private List<Tag> tags;
        public MTags m_Tags;
        public bool tagUpdated = false;

        [Tooltip("Collider for the Zone. If is not set, it will find the first collider attached to this gameobject")]
        [RequiredField] public Collider ZoneCollider;
        public Collider ZCollider => ZoneCollider;

        [Tooltip("Reaction invoked on the entering Animal as soon as it enters the trigger")]
        public Reaction2 EnterReaction;
        [Tooltip("Reaction invoked on the Animal when it fully exits the trigger. Use it to reverse what the Activation Reaction did (e.g. exit a State, reset a Stance)")]
        public Reaction2 ExitReaction;
        [Tooltip("Reaction invoked on the Animal when the Zone activates. This is where the Mode/State/Stance is applied")]
        public Reaction2 ActivationReaction;

        [Tooltip("Global conditions that must be true for the zone to activate. Dynamic Target = the entering Animal")]
        public Conditions.Conditions2 conditions; //MWC: gate activation with reusable Conditions2

        public AnimalEvent OnEnter = new();
        public AnimalEvent OnExit = new();
        public AnimalEvent OnZoneActivation = new();

        /// <summary>Currents Animals inside the zone</summary>
        public HashSet<MAnimal> AnimalsInZone { get; internal set; }

        /// <summary>Currents Animals that have activated the zone</summary>
        public HashSet<MAnimal> AnimalsUsingZone { get; internal set; }

        public MAnimal JustExitAnimal;

        /// <summary>List of all colliders entering the Zone</summary>
        internal HashSet<Collider> m_Colliders = new();

        /// <summary>Keep a Track of all the Reaction Zones on the Scene</summary>
        public static List<Zone2> Zones;

        public List<Tag> Tags { get => tags; set => tags = value; }

        //MWC: Reactions-only zone is never a Mode/State/Stance zone, so Mode.cs and similar
        //IZone consumers will never try to re-activate it. ZoneID is unused here.
        public bool IsMode => false;
        public bool IsState => false;
        public bool IsStance => false;
        public int ZoneID => 0;

        private void Awake()
        {
            Zones ??= new List<Zone2>();
        }

        private void OnEnable()
        {
            if (ZoneCollider == null)
                ZoneCollider = GetComponent<Collider>();    //Get the reference for the collider

            if (ZoneCollider)
            {
                ZoneCollider.isTrigger = true;              //Force Trigger
                ZoneCollider.enabled = true;
            }

            Zones.Add(this);                                //Save the Reaction Zone on the global list

            AnimalsInZone = new();
            AnimalsUsingZone = new();
        }

        private void OnDisable()
        {
            var animals = new List<MAnimal>(AnimalsInZone);

            foreach (var animal in animals)
            {
                ResetStoredAnimal(animal);
                OnExit.Invoke(animal);
                ExitReaction.React(animal);
            }

            if (ZoneCollider)
                ZoneCollider.enabled = false;

            AnimalsInZone = new();      //Clear the Animals in Zone
            AnimalsUsingZone = new();   //Clear the Animals using the Zone
            m_Colliders = new();        //Clear the Colliders
            JustExitAnimal = null;

            Zones.Remove(this);         //Remove the Reaction Zone from the global list
        }

        private void OnValidate()
        {
            if (!tagUpdated && m_Tags != null)
            {
                m_Tags = new MTags(Tags);
                tagUpdated = true;
                MTools.SetDirty(this);
            }
        }

        public bool TrueConditions(Collider other)
        {
            if (!enabled) return false;

            if (Tags != null && Tags.Count > 0)
            {
                if (!other.gameObject.HasMalbersTagInParent(Tags.ToArray())) return false;
            }

            if (ZoneCollider == null) return false;
            if (other == null) return false;                                        //You are CALLING AN ELIMINATED ONE

            if (!MTools.Layer_in_LayerMask(other.gameObject.layer, Layer)) return false;
            if (transform.IsChildOf(other.transform)) return false;                 //Do not Interact with yourself

            return true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (TrueConditions(other))
            {
                MAnimal animal = other.FindComponent<MAnimal>();             //Get the animal on the entering collider

                if (!animal || animal.Sleep || !animal.enabled) return;      //If there's no animal, or is Sleep or disabled do nothing
                if (animal.RB.isKinematic) return;                           //Do not Activate while the animal is kinematic
                if (automatic && animal == JustExitAnimal) return;           //Do not activate the animal that just exit

                if (!m_Colliders.Contains(other))
                    m_Colliders.Add(other);            //if the entering collider is not already on the list add it
                else return;                           //The Collider was already there

                //if the animal is already on the list do nothing
                if (AnimalsInZone.Contains(animal)) return;

                //If the Animal is on another Zone Remove it from the other Zone
                if (animal.InZone && animal.Zone != (IZone)this)
                    animal.Zone.RemoveAnimal(animal);

                animal.Zone = this;                    //Let the animal know it is on a zone
                AnimalsInZone.Add(animal);             //Set a new Animal
                OnEnter.Invoke(animal);
                EnterReaction.React(animal);

                Debugging($"[Enter Animal] -> [{animal.name}]", "yellow");

                if (automatic) ActivateZone(animal);   //MWC: single attempt, no retry coroutine (no conditions/angle to wait for)
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (TrueConditions(other))
            {
                MAnimal animal = other.GetComponentInParent<MAnimal>();
                if (!animal || animal.Sleep || !animal.enabled) return;      //If there's no animal, or is Sleep or disabled do nothing

                if (m_Colliders != null && m_Colliders.Contains(other))
                    m_Colliders.Remove(other);                               //Remove the collider that is exiting the zone

                CheckMissingColliders();

                if (AnimalsInZone.Contains(animal))         //Means the Entering animal still exists on the zone
                {
                    bool NoAnimalColliders = true;

                    foreach (var col in m_Colliders)
                    {
                        if (col.transform.SameHierarchy(animal.transform)) //Check if a Collider is still on the Animal
                        {
                            NoAnimalColliders = false;
                            break;
                        }
                    }
                    if (NoAnimalColliders) RemoveAnimal(animal);
                }
            }
        }

        /// <summary>Activate the Zone on a given Animal: fires the Activation Reaction once.</summary>
        public virtual bool ActivateZone(MAnimal animal)
        {
            if (animal == null) return false;

            //MWC: Global conditions gate the activation (ignored when no conditions are set)
            if (conditions.Valid && !conditions.Evaluate(animal)) return false;

            animal.Zone = this;             //Let the animal know it is on a zone
            AnimalsUsingZone.Add(animal);

            //MWC: fire the Activation Reaction exactly once (fixes the Zone.ReactionsOnly double-fire).
            //If there's no Activation Reaction the zone still counts as activated so events still run.
            var ok = !ActivationReaction.IsValid || ActivationReaction.React(animal);

            if (ok)
            {
                Debugging($"[Zone Activate] <b>[{animal.name}]</b>");
                OnZoneActive(animal);
                return true;
            }
            return false;
        }

        /// <summary>Activate the Zone for every Animal currently inside it (UnityEvent friendly).</summary>
        public virtual void ActivateZone()
        {
            //Copy to avoid mutation while iterating (DisableAfterUsed / ResetStoredAnimal may modify the set)
            var animals = new List<MAnimal>(AnimalsInZone);
            foreach (var animal in animals)
                ActivateZone(animal);
        }

        /// <summary>Activation bookkeeping. MWC: does NOT re-fire the Activation Reaction (already fired in ActivateZone).</summary>
        internal void OnZoneActive(MAnimal animal)
        {
            OnZoneActivation.Invoke(animal);

            if (DisableAfterUsed.Value) enabled = false;
        }

        public virtual void RemoveAnimal(MAnimal animal)
        {
            OnExit.Invoke(animal);              //Invoke On Exit when all the animal's colliders have exited the Zone
            ExitReaction.React(animal);         //React Exit: this is where a Mode/State/Stance gets reversed

            ResetStoredAnimal(animal);

            AnimalsInZone.Remove(animal);
            AnimalsUsingZone.Remove(animal);

            Debugging($"[Exit Animal] -> [{animal.name}]", "yellow");

            if (automatic)
            {
                JustExitAnimal = animal;
                this.Delay_Action(() => JustExitAnimal = null);
            }
        }

        private void CheckMissingColliders()
        {
            m_Colliders.RemoveWhere(x => x == null || x.gameObject == null || !x.enabled || !x.gameObject.scene.IsValid());
        }

        /// <summary>AI / Waypoint arrival hook: activate the zone on the arriving Animal.</summary>
        public void TargetArrived(GameObject go)
        {
            var animal = go.FindComponent<MAnimal>();
            ActivateZone(animal);
        }

        public void ResetAllAnimals()
        {
            var animals = new List<MAnimal>(AnimalsInZone);
            foreach (var animal in animals)
                ResetStoredAnimal(animal);
        }

        public virtual void ResetStoredAnimal(Component animalC)
        {
            var animal = animalC.FindComponent<MAnimal>();
            ResetStoredAnimal(animal);
        }

        public virtual void ResetStoredAnimal(MAnimal animal)
        {
            if (animal == null) return;

            if (animal.Zone != null && animal.Zone == (IZone)this)
                animal.Zone = null;     //Tell the Animal it is no longer on a Zone

            //Remove all the colliders from the animal in case some are still there
            foreach (var item in animal.colliders)
            {
                if (item != null && m_Colliders.Contains(item))
                    m_Colliders.Remove(item);
            }

            if (animal.MainCollider != null && m_Colliders.Contains(animal.MainCollider))
                m_Colliders.Remove(animal.MainCollider);

            AnimalsInZone.Remove(animal);
            AnimalsUsingZone.Remove(animal);
        }

        public void Debugging(string value, string color = "green")
        {
#if UNITY_EDITOR
            if (debug)
                MDebug.Log($"<B>[{name}]</B> → <color={color}><B>{value}</B></color>", this);
#endif
        }

        [HideInInspector] public int Editor_Tabs1 = 0;

#if UNITY_EDITOR
        [ContextMenu("Add On Focused Prefab")]
        private void AddFocusedPrefab()
        {
            MTools.AddOnFocusedPrefab(transform, OnEnter, OnExit);
        }

        private void Reset()
        {
            gameObject.layer = LayerMask.NameToLayer("Ignore Raycast"); //MWC: keep the zone trigger out of raycasts

            if (ZoneCollider == null)
                ZoneCollider = GetComponent<Collider>();

            if (GetComponent<Collider>() == null)
            {
                Debug.LogWarning("There's no Collider on the Zone, Adding a BoxCollider", this);
                ZoneCollider = gameObject.AddComponent<BoxCollider>();
            }

            if (ZoneCollider)
            {
                ZoneCollider.isTrigger = true; //MWC: force the collider to be a trigger
                ZoneCollider.enabled = true;
            }
        }

        private void OnDrawGizmos()
        {
            //MWC: draw the Global conditions gizmos (matches Zone)
            if (UnityEditorInternal.InternalEditorUtility.GetIsInspectorExpanded(this))
                conditions.Gizmos(this);
        }
#endif
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(Zone2)), CanEditMultipleObjects]
    public class Zone2Editor : Editor
    {
        private Zone2 m;
        protected string[] Tabs1 = new string[] { "General", "Events" };

        private SerializedProperty
            EnterReaction, ExitReaction, ActivationReaction, conditions,
            automatic, DisableAfterUsed, debug, Editor_Tabs1,
            OnZoneActivation, OnExit, OnEnter, layer, m_tag, ZoneCollider;

        protected virtual void OnEnable()
        {
            m = (Zone2)target;

            EnterReaction = serializedObject.FindProperty(nameof(Zone2.EnterReaction));
            ExitReaction = serializedObject.FindProperty(nameof(Zone2.ExitReaction));
            ActivationReaction = serializedObject.FindProperty(nameof(Zone2.ActivationReaction));
            conditions = serializedObject.FindProperty(nameof(Zone2.conditions));

            automatic = serializedObject.FindProperty(nameof(Zone2.automatic));
            DisableAfterUsed = serializedObject.FindProperty(nameof(Zone2.DisableAfterUsed));
            layer = serializedObject.FindProperty(nameof(Zone2.Layer));
            m_tag = serializedObject.FindProperty(nameof(Zone2.m_Tags));
            debug = serializedObject.FindProperty(nameof(Zone2.debug));
            ZoneCollider = serializedObject.FindProperty(nameof(Zone2.ZoneCollider));
            Editor_Tabs1 = serializedObject.FindProperty(nameof(Zone2.Editor_Tabs1));

            OnEnter = serializedObject.FindProperty(nameof(Zone2.OnEnter));
            OnExit = serializedObject.FindProperty(nameof(Zone2.OnExit));
            OnZoneActivation = serializedObject.FindProperty(nameof(Zone2.OnZoneActivation));

            if (ZoneCollider.objectReferenceValue == null)
            {
                ZoneCollider.objectReferenceValue = m.GetComponent<Collider>();
                serializedObject.ApplyModifiedProperties();
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            MalbersEditor.DrawDescription("Zone to invoke any Reactions");

            using (new GUILayout.HorizontalScope())
            {
                Editor_Tabs1.intValue = GUILayout.Toolbar(Editor_Tabs1.intValue, Tabs1);
                MalbersEditor.DrawDebugIcon(debug);
            }

            EditorGUI.BeginChangeCheck();

            switch (Editor_Tabs1.intValue)
            {
                case 0: DrawGeneral(); break;
                case 1: DrawEvents(); break;
                default: break;
            }

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(target, "Zone2 Changed");
                EditorUtility.SetDirty(target);
            }

            if (Application.isPlaying && debug.boolValue && m.AnimalsInZone != null)
            {
                using (new EditorGUI.DisabledGroupScope(true))
                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("Debug", EditorStyles.boldLabel);

                    EditorGUILayout.LabelField("Animals in Zone (" + m.AnimalsInZone.Count + ")", EditorStyles.boldLabel);
                    foreach (var item in m.AnimalsInZone)
                        EditorGUILayout.ObjectField(item.name, item, typeof(MAnimal), false);

                    EditorGUILayout.LabelField("Animals Using Zone (" + m.AnimalsUsingZone.Count + ")", EditorStyles.boldLabel);
                    foreach (var item in m.AnimalsUsingZone)
                        EditorGUILayout.ObjectField(item.name, item, typeof(MAnimal), false);

                    EditorGUILayout.LabelField("Colliders in Zone (" + m.m_Colliders.Count + ")", EditorStyles.boldLabel);
                    foreach (var item in m.m_Colliders)
                        EditorGUILayout.ObjectField(item.name, item, typeof(Collider), false);

                    Repaint();
                }
            }
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawGeneral()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(automatic, new GUIContent("Automatic"));
                EditorGUILayout.PropertyField(DisableAfterUsed);
                EditorGUILayout.PropertyField(ZoneCollider, new GUIContent("Trigger"));
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(m_tag,
                    new GUIContent("Tags", "Set this parameter if you want the zone to Interact only with gameObjects with that tag"));
                EditorGUILayout.PropertyField(layer);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(conditions, new GUIContent("Global Conditions"));
                EditorGUI.indentLevel--;
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(ActivationReaction);
                EditorGUILayout.PropertyField(EnterReaction);
                EditorGUILayout.PropertyField(ExitReaction);
                EditorGUI.indentLevel--;
            }
        }

        private void DrawEvents()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(OnEnter, new GUIContent("On Animal Enter Zone"));
                EditorGUILayout.PropertyField(OnExit, new GUIContent("On Animal Exit Zone"));
                EditorGUILayout.PropertyField(OnZoneActivation, new GUIContent("On Zone Active"));
            }
        }
    }
#endif
}
