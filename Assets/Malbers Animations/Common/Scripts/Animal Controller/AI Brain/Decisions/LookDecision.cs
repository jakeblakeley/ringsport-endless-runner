using MalbersAnimations.Reactions;
using MalbersAnimations.Scriptables;
using System.Collections.Generic;
using UnityEngine;


#if UNITY_EDITOR
#endif


namespace MalbersAnimations.Controller.AI
{
    public enum LookFor { MainAnimalPlayer, MalbersTag, UnityTag, Zones, GameObject, ClosestWayPoint, CurrentTarget, TransformVar, GameObjectVar, RuntimeGameobjectSet, LastDamager }


    [CreateAssetMenu(menuName = "Malbers Animations/Pluggable AI/Decision/Look", order = -101)]
    public class LookDecision : MAIDecision
    {
        public override string DisplayName => "General/Look";

        [Range(0, 1)]
        /// <summary>Angle of Vision of the Animal</summary>
        [Tooltip("Shorten the Look Ray to not found the ground by mistake")]
        public float LookMultiplier = 0.9f;

        /// <summary>Range for Looking forward and Finding something</summary>
        [Space, Tooltip("Range for Looking forward and Finding something")]

        public bool UseBlackBoardRange = false;
        [Hide(nameof(UseBlackBoardRange), true)]
        public FloatReference LookRange = new(15);
        [Hide(nameof(UseBlackBoardRange)), Tooltip("Blackboard variable for 'Look Range'. Make sure is a Float variable")]
        public string LookRangeBlackboard = "LookRange";

        public bool UseBlackBoardAngle = false;
        /// <summary>Angle of Vision of the Animal</summary>
        [Tooltip("Angle of Vision of the Animal")]
        [Hide(nameof(UseBlackBoardAngle), true), Range(0, 360)]
        public float LookAngle = 120;

        [Hide(nameof(UseBlackBoardAngle)), Tooltip("Blackboard variable for 'Look Angle'. Make sure is a Float variable")]
        public string LookAngleBlackboard = "LookAngle";


        [Tooltip("Layers that can block the Animal Eyes")]
        public LayerReference ObstacleLayer = new(1);
        [Space]
        /// <summary>What to look for?? </summary>
        [Space, Tooltip("What to look for??")]
        public LookFor lookFor = LookFor.MainAnimalPlayer;
        [Tooltip("Look for this Unity Tag on an Object")]
        [Hide(nameof(lookFor), (int)LookFor.UnityTag)]
        public string UnityTag = string.Empty;
        [Tooltip("Look for an Specific GameObject by its name")]
        [Hide(nameof(lookFor), (int)LookFor.GameObject)]
        public string GameObjectName = string.Empty;

        [RequiredField, Tooltip("Transform Reference value. This value should be set by a Transform Hook Component")]
        [Hide(nameof(lookFor), (int)LookFor.TransformVar)]
        public TransformVar transform;
        [RequiredField, Tooltip("GameObject Reference value. This value should be set by a GameObject Hook Component")]
        [Hide(nameof(lookFor), (int)LookFor.GameObjectVar)]
        public GameObjectVar gameObject;

        [RequiredField, Tooltip("GameObjectSet. Search for all  GameObjects Set in the Set")]
        [Hide(nameof(lookFor), (int)LookFor.RuntimeGameobjectSet)]
        public RuntimeGameObjects gameObjectSet;

        /// <summary>Type of Zone we want to find</summary>
        [Tooltip("Type of Zone we want to find")]
        [Hide(nameof(lookFor), (int)LookFor.Zones)]
        public ZoneType zoneType;

        [Tooltip("Search for all zones")]
        [Hide(nameof(lookFor), (int)LookFor.Zones)]
        public bool AllZones = true;
        /// <summary>ID value of the Zone we want to find</summary>
        [Tooltip("ID value of the Zone we want to find")]
        [Hide(nameof(lookFor), (int)LookFor.Zones)]
        public int ZoneID = -1;

        [Tooltip("Mode Zone Index")]
        [Hide(nameof(lookFor), (int)LookFor.Zones)]
        public int ZoneModeAbility = -1;

        /// <summary>Custom Tags you want to find (Old Compatability</summary>
        [Tooltip("Custom Tags you want to find")]
        [HideInInspector] public Tag[] tags;

        [Tooltip("Custom Tags you want to find")]
        [Hide(nameof(lookFor), (int)LookFor.MalbersTag)]
        public MTags m_Tags;


        [Space(20), Tooltip("If the what we are looking for is found then Assign it as a new Target")]
        public bool AssignTarget = true;
        [Tooltip("If the what we are looking for is found then also start moving")]
        public bool MoveToTarget = true;
        [Tooltip("Select randomly one of the potential targets, not the closest one found")]
        public bool ChooseRandomly = false;



        private void OnValidate()
        {
            UpgradeToMTag();
        }


        [Tooltip("If the Look decision is achieved then do a reaction on self")]
        public Reaction2 ReactionOnSelf;

        [Tooltip("If the Look decision is achieved then do a reaction on the Target")]
        public Reaction2 ReactionOnTarget;


        public Color debugColor = new(0, 0, 0.7f, 0.3f);

        void Reset() => Description = "The Animal will look for an Object using a cone view";

        public void UpgradeToMTag()
        {
            if (m_Tags == null || m_Tags.Length == 0 && (tags != null && tags.Length > 0))
            {
                m_Tags = new MTags(tags);
                tags = null;
                MTools.SetDirty(this);
            }
        }


        public override bool Decide(MAnimalBrain brain, int index) => Look_For(brain, false, index);
        public override void FinishDecision(MAnimalBrain brain, int index)
        {
            UpgradeToMTag(); //Remove later in newer updates, this is just to upgrade the old Tags array to the new MTags Scriptable Object version 1.5.2


            Look_For(brain, AssignTarget, index); //This will assign the Target in case its true
        }
        public override void PrepareDecision(MAnimalBrain brain, int index)
        {
            switch (lookFor)
            {
                case LookFor.MalbersTag:

                    if (m_Tags == null || m_Tags.tags == null || m_Tags.tags.Count == 0)
                    {
                        Debug.LogWarning($"There's no Tags to look for in the Look Decision [{name}]: " + brain.name, this);
                        return;
                    }
                    List<GameObject> gTags = new();

                    foreach (var t in Tags.TagsHolders)
                    {
                        if (t.gameObject.HasMalbersTag(m_Tags))
                            gTags.Add(t.gameObject);
                    }

                    if (gTags.Count > 0) brain.DecisionsVars[index].gameobjects = gTags.ToArray();

                    break;

                case LookFor.UnityTag:
                    if (string.IsNullOrEmpty(UnityTag)) return;
                    brain.DecisionsVars[index].gameobjects = GameObject.FindGameObjectsWithTag(UnityTag);
                    break;
                case LookFor.RuntimeGameobjectSet:
                    if (gameObjectSet == null || gameObjectSet.Count == 0) return;
                    brain.DecisionsVars[index].gameobjects = gameObjectSet.Items.ToArray();
                    break;

                default:
                    break;
            }

            StoreColliders(brain, index);
        }

        /// <summary> Store all renderers found on the GameObjects  </summary>
        private void StoreColliders(MAnimalBrain brain, int index)
        {
            if (brain.DecisionsVars[index].gameobjects != null && brain.DecisionsVars[index].gameobjects.Length > 0)
            {
                var colliders = new List<Collider>();

                for (int i = 0; i < brain.DecisionsVars[index].gameobjects.Length; i++)
                {
                    var AllColliders = brain.DecisionsVars[index].gameobjects[i].GetComponentsInChildren<Collider>();

                    foreach (var c in AllColliders)
                    {
                        if (c.isTrigger) continue;
                        if (MTools.Layer_in_LayerMask(c.gameObject.layer, ObstacleLayer.Value)) continue;
                        if (c.transform.SameHierarchy(brain.Animal.transform)) continue; //Avoid adding its own colliders

                        colliders.Add(c); //Save only good Colliders
                    }
                }
                brain.DecisionsVars[index].AddComponents(colliders.ToArray());
            }
        }


        /// <summary>  Looks for a gameobject according to the Look For type.</summary>
        private bool Look_For(MAnimalBrain brain, bool assign, int index)
        {
            return lookFor switch
            {
                LookFor.MainAnimalPlayer => LookForAnimalPlayer(brain, assign),
                LookFor.MalbersTag => LookForMalbersTags(brain, assign, index),
                LookFor.UnityTag => LookForUnityTags(brain, assign, index),
                LookFor.Zones => LookForZones(brain, assign),
                LookFor.GameObject => LookForGameObjectByName(brain, assign),
                LookFor.ClosestWayPoint => LookForClosestWaypoint(brain, assign),
                LookFor.CurrentTarget => LookForTarget(brain, assign),
                LookFor.TransformVar => LookForTransformVar(brain, assign),
                LookFor.GameObjectVar => LookForGoVar(brain, assign),
                LookFor.RuntimeGameobjectSet => LookForGoSet(brain, assign, index),
                LookFor.LastDamager => LookForGoLastDamager(brain, assign, index),
                _ => false,
            };
        }

        private bool LookForGoLastDamager(MAnimalBrain brain, bool assign, int index)
        {
            if (brain.Animal.TryGetComponent<MDamageable>(out var damageable))
            {
                var lastDamager = damageable.LastDamage.Damager;
                if (lastDamager != null)
                {
                    AssignMoveTarget(brain, lastDamager.transform, assign);
                    var Center = brain.TargetAnimal ? brain.TargetAnimal.Center : lastDamager.transform.position;
                    return IsInFieldOfView(brain, Center, out _);
                }
            }
            return false;
        }

        public bool LookForTarget(MAnimalBrain brain, bool assign)
        {
            if (brain.Target == null) return false;

            AssignMoveTarget(brain, brain.Target, assign);
            var Center = brain.TargetAnimal ? brain.TargetAnimal.Center : brain.Target.position;
            return IsInFieldOfView(brain, Center, out _);
        }

        public bool LookForTransformVar(MAnimalBrain brain, bool assign)
        {
            if (transform == null || transform.Value == null) return false;

            AssignMoveTarget(brain, transform.Value, assign);

            var Center =
                transform.Value == brain.Target && !brain.AIControl.IsAITarget.IsUnityRefNull() ? //: corrected null check of unity object interface type
                brain.AIControl.IsAITarget.GetCenterY() :
                transform.Value.position;

            return IsInFieldOfView(brain, Center, out _);
        }

        public bool LookForGoVar(MAnimalBrain brain, bool assign)
        {
            if (gameObject == null && gameObject.Value && !gameObject.Value.IsPrefab()) return false;

            AssignMoveTarget(brain, gameObject.Value.transform, assign);

            var Center =
               gameObject.Value.transform == brain.Target && !brain.AIControl.IsAITarget.IsUnityRefNull() ? //: corrected null check of unity object interface type
               brain.AIControl.IsAITarget.GetCenterY() :
               gameObject.Value.transform.position;

            return IsInFieldOfView(brain, Center, out _);
        }

        private bool IsInFieldOfView(MAnimalBrain brain, Vector3 Center, out float Distance)
        {
            var Direction_to_Target = (Center - brain.Eyes.position); //Put the Sight a bit higher

            //Important, otherwise it will find the ground for Objects to close to it. Also Apply the Scale
            Distance = Vector3.Distance(Center, brain.Eyes.position) * LookMultiplier; //CustomPatch: TODO: this can be optimized by using squared distance (soon...)


            var LookAngle = UseBlackBoardAngle ? brain.LocalVars.GetFloat(LookAngleBlackboard) : this.LookAngle;
            var LookRange = UseBlackBoardRange ? brain.LocalVars.GetFloat(LookRangeBlackboard) : this.LookRange.Value;


            if (LookAngle == 0 || LookRange <= 0) return true; //Means the Field of view can be ignored

            if (Distance < LookRange * brain.Animal.ScaleFactor) //Check if whe are inside the Look Radius
            {
                Vector3 EyesForward = Vector3.ProjectOnPlane(brain.Eyes.forward, brain.Animal.UpVector);

                var angle = Vector3.Angle(Direction_to_Target, EyesForward); //CustomPatch: TODO: can significantly be optimized by using dot product checking instead of complex Sqrt and ACos based angle calculations (will get back to this)

                if (angle < (LookAngle * 0.5f)) //CustomPatch: minor but easy optimization: multiply cheaper than divide (by around 4x - 10x CPU cycles)
                {
                    //Need a RayCast to see if there's no obstacle in front of the Animal OBSTACLE LAYER
                    if (Physics.Raycast(brain.Eyes.position, Direction_to_Target, out RaycastHit hit, Distance, ObstacleLayer, QueryTriggerInteraction.Ignore))
                    {
                        if (brain.debug)
                        {
                            MDebug.DrawRay(brain.Eyes.position, Direction_to_Target * LookMultiplier, Color.green, interval);
                            MDebug.DrawLine(hit.point, Center, Color.red, interval);
                            MDebug.DrawWireSphere(Center, Color.red, interval);
                            MDebug.DrawCircle(hit.point, hit.normal, 0.1f, Color.red, true, interval);
                        }

                        return false; //Meaning there's something between the Eyes of the Animal and the Target
                    }
                    else
                    {
                        if (brain.debug)
                        {
                            MDebug.DrawRay(brain.Eyes.position, Direction_to_Target, Color.green, interval);
                            MDebug.DrawWireSphere(Center, Color.green, interval);
                        }
                        return true;
                    }
                }

                return false;
            }
            //  Debug.Log($"False (NOT IN Distance {Distance} > RANGE) {LookRange.Value}" );
            return false;
        }

        private void AssignMoveTarget(MAnimalBrain brain, Transform target, bool assign)
        {
            if (assign && AssignTarget)
            {
                brain.AIControl.SetTarget(target, MoveToTarget);
            }
        }

        public bool LookForZones(MAnimalBrain brain, bool assign)
        {
            var zones = Zone.Zones;
            if (zones == null || zones.Count == 0) return false;  //There's no zone around here

            float minDistance = float.PositiveInfinity;

            Zone FoundZone = null;

            foreach (var zone in zones)
            {
                if (AllZones ||
                    (zone && zone.zoneType == zoneType) &&                      //Check the same Zone Types
                    ZoneID == -1 ||                                             //Check First if its Any Zone
                    zone.ZoneID == ZoneID ||                                    //Check Zone has the same ID
                    zone.zoneType != ZoneType.Mode ||                           //Check if its not a Zone Mode
                    zone.zoneType == ZoneType.Mode && ZoneModeAbility == -1 ||  //Check if it's a Zone Mode but the Ability its any
                    zone.ModeAbilityIndex == ZoneModeAbility)                   //Check if it's a Zone Mode AND Ability Match

                    if (IsInFieldOfView(brain, zone.ZoneCollider.bounds.center, out float Distance) && Distance < minDistance)
                    {
                        minDistance = Distance;
                        FoundZone = zone;
                    }
            }

            if (FoundZone)
            {
                AssignMoveTarget(brain, FoundZone.transform, assign);

                ReactionOnSelf.React(brain.Animal); //React to the Look Decision Achieved
                ReactionOnTarget.React(FoundZone);

                return true;
            }
            return false;
        }

        public bool LookForMalbersTags(MAnimalBrain brain, bool assign, int index)
        {
            if (Tags.TagsHolders == null || m_Tags == null || m_Tags.Length == 0) return false;

            float minDistance = float.MaxValue;
            Transform Closest = null;

            var filteredTags = Tags.GameObjectbyTag(m_Tags);
            if (filteredTags == null)
                return false;

            if (ChooseRandomly)
            {
                while (filteredTags.Count != 0)
                {
                    int newIndex = Random.Range(0, filteredTags.Count);
                    var t = filteredTags[newIndex].transform;

                    if (t != null)
                    {
                        if (IsInFieldOfView(brain, t.position, out _))
                        {
                            AssignMoveTarget(brain, t, assign);
                            return true;
                        }
                    }
                    filteredTags.RemoveAt(newIndex);
                }
            }
            else
            {
                for (int i = 0; i < filteredTags.Count; i++)
                {
                    var t = filteredTags[i].transform;

                    if (t != null)
                    {
                        if (IsInFieldOfView(brain, t.position, out float Distance))
                        {
                            if (Distance < minDistance)
                            {
                                minDistance = Distance;
                                Closest = t;
                            }
                        }
                    }
                }
            }

            if (Closest)
            {
                AssignMoveTarget(brain, Closest, assign);
                return true;
            }
            return false;
        }

        public bool LookForUnityTags(MAnimalBrain brain, bool assign, int index)
        {
            if (string.IsNullOrEmpty(UnityTag)) return false;

            if (ChooseRandomly)
                return ChooseRandomObject(brain, assign, index);
            return ClosestGameObject(brain, assign, index);
        }

        public bool LookForGoSet(MAnimalBrain brain, bool assign, int index)
        {
            if (gameObjectSet == null || gameObjectSet.Count == 0) return false;

            if (ChooseRandomly)
                return ChooseRandomObject(brain, assign, index);
            return ClosestGameObject(brain, assign, index);
        }



        private bool ClosestGameObject(MAnimalBrain brain, bool assign, int index)
        {
            var All = brain.DecisionsVars[index].gameobjects; //catch all the saved gameobjects

            if (All == null || All.Length == 0) return false;

            float minDistance = float.MaxValue;

            GameObject ClosestGameObject = null;

            for (int i = 0; i < All.Length; i++)
            {
                var go = All[i];

                if (go != null)
                {
                    var Center = go.transform.position;// + new Vector3(0, brain.Animal.Height, 0); //In case there's no height use the animal Default

                    if (brain.DecisionsVars[index].Components != null && brain.DecisionsVars[index].Components.Length > 0)
                    {
                        var bounds = Vector3.zero;
                        int total = 0;
                        foreach (var c in brain.DecisionsVars[index].Components)
                        {
                            if (c != null && c is Collider && c.transform.SameHierarchy(go.transform))
                            {
                                bounds += (c as Collider).bounds.center;
                                total++;
                            }
                        }
                        bounds /= total;

                        if (bounds != Vector3.zero) Center = bounds;
                    }


                    if (IsInFieldOfView(brain, Center, out float Distance))
                    {
                        if (Distance < minDistance)
                        {
                            minDistance = Distance;
                            ClosestGameObject = go;
                        }
                    }
                }
            }

            if (ClosestGameObject)
            {
                AssignMoveTarget(brain, ClosestGameObject.transform, assign);
                return true;
            }
            return false;
        }

        public bool ChooseRandomObject(MAnimalBrain brain, bool assign, int index)
        {
            var All = new List<GameObject>();
            if (brain.DecisionsVars[index].gameobjects != null)
                All.AddRange(brain.DecisionsVars[index].gameobjects); //catch all the saved gameobjects with a tag
            if (All.Count == 0) return false;

            while (All.Count != 0)
            {
                int newIndex = Random.Range(0, All.Count);
                if (All[newIndex] != null)
                {
                    var Center = All[newIndex].transform.position + new Vector3(0, brain.Animal.Height, 0);

                    var renderer = brain.DecisionsVars[index].Components[newIndex];

                    if (renderer != null && renderer is Renderer rendererComponent)
                        Center = rendererComponent.bounds.center; //CustomPatch: optimization: removed redundant Renderer cast

                    if (IsInFieldOfView(brain, Center, out float Distance))
                    {
                        AssignMoveTarget(brain, All[newIndex].transform, assign);
                        return true;
                    }
                }
                All.RemoveAt(newIndex);
            }

            return false;
        }


        public bool LookForGameObjectByName(MAnimalBrain brain, bool assign)
        {
            if (string.IsNullOrEmpty(GameObjectName)) return false;

            var gameObject = GameObject.Find(GameObjectName);

            if (gameObject)
            {
                AssignMoveTarget(brain, gameObject.transform, assign);
                return IsInFieldOfView(brain, gameObject.transform.position, out _);   //Find if is inside the Field of view
            }
            return false;
        }

        public bool LookForClosestWaypoint(MAnimalBrain brain, bool assign)
        {
            var allWaypoints = MWayPoint.WayPoints;
            if (allWaypoints == null || allWaypoints.Count == 0) return false;  //There's no waypoints  around here

            float minDistance = float.MaxValue;

            MWayPoint closestWayPoint = null;

            foreach (var way in allWaypoints)
            {
                var center = way.GetCenterY();
                if (IsInFieldOfView(brain, center, out float Distance))
                {
                    if (Distance < minDistance)
                    {
                        minDistance = Distance;
                        closestWayPoint = way;
                    }
                }
            }

            if (closestWayPoint)
            {
                AssignMoveTarget(brain, closestWayPoint.transform, assign);
                return true; //Find if is inside the Field of view
            }
            return false;
        }

        private bool LookForAnimalPlayer(MAnimalBrain brain, bool assign)
        {
            if (MAnimal.MainAnimal == null || MAnimal.MainAnimal.ActiveStateID == StateEnum.Death) return false; //Means the animal is death or Disable
            if (MAnimal.MainAnimal == brain.Animal) { Debug.LogError("AI Animal is set as MainAnimal. Fix it!", brain.Animal); return false; }

            AssignMoveTarget(brain, MAnimal.MainAnimal.transform, assign);
            return IsInFieldOfView(brain, MAnimal.MainAnimal.Center, out _);
        }


#if UNITY_EDITOR

        public override void DrawGizmos(MAnimalBrain brain)
        {
            var Eyes = brain.Eyes;

            var scale = brain.Animal ? brain.Animal.ScaleFactor : brain.transform.root.localScale.y;

            if (Eyes != null)
            {
                Color c = debugColor;
                c.a = 1f;

                var LookAngle = this.LookAngle;
                var LookRange = this.LookRange.Value;

                if (UseBlackBoardAngle || UseBlackBoardRange)
                {
                    var ILocalVars = brain.LocalVars;

                    if (ILocalVars != null)
                    {
                        if (UseBlackBoardAngle) LookAngle = ILocalVars.GetVarEditor<float>(LookAngleBlackboard);
                        if (UseBlackBoardRange) LookRange = ILocalVars.GetVarEditor<float>(LookRangeBlackboard);
                    }
                }

                if (LookAngle == 0f || LookRange == 0f) return; //Means the Field of view can be ignored

                Vector3 EyesForward = Vector3.ProjectOnPlane(brain.Eyes.forward, Vector3.up);

                Vector3 rotatedForward = Quaternion.Euler(0, -LookAngle * 0.5f, 0) * EyesForward;
                UnityEditor.Handles.color = c;
                UnityEditor.Handles.DrawWireArc(Eyes.position, Vector3.up, rotatedForward, LookAngle, LookRange * scale);
                UnityEditor.Handles.color = debugColor;
                UnityEditor.Handles.DrawSolidArc(Eyes.position, Vector3.up, rotatedForward, LookAngle, LookRange * scale);
            }
        }
#endif
    }
}
