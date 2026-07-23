using MalbersAnimations.Reactions;
using MalbersAnimations.Scriptables;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace MalbersAnimations
{
    public struct DamageData
    {
        /// <summary> Who is doing the damage </summary>
        public GameObject Damager;
        /// <summary>Who received the damage </summary>
        public GameObject Damagee;
        /// <summary> Stat Modifier with the updated final stat values</summary>
        public StatModifier stat;

        /// <summary> Direction of the Damage</summary>
        public Vector3 Direction;

        /// <summary> Position of the Damage Contact</summary>
        public Vector3 HitPosition;

        /// <summary> Final value who modified the Stat</summary>
        public readonly float Damage => stat.modify != StatOption.None ? stat.Value : 0f;

        /// <summary>Store if the Damage was Critical</summary>
        public bool critical;

        /// <summary>Store if the Damage was Pure</summary>
        public bool Pure;

        /// <summary>Was Miss</summary>
        public bool Missed;

        /// <summary>Store if the damage was  </summary>
        public ElementMultiplier Element;

        public Reaction2 reaction;

        public DamageData
            (Vector3 direction, GameObject damager, GameObject damagee, StatModifier stat, ElementMultiplier element, Reaction2 reaction, bool critical, bool pure, bool missed)
        {
            Damager = damager;
            Damagee = damagee;
            this.stat = new StatModifier(stat);
            this.critical = critical;
            Element = element;
            Direction = direction;
            this.reaction = reaction;
            Missed = missed;
            HitPosition = Vector3.zero; // Default value for HitPosition
            Pure = pure;
        }

        public DamageData
           (Vector3 direction, Vector3 hitPosition, GameObject damager, GameObject damagee,
            StatModifier stat, bool critical, ElementMultiplier element, Reaction2 reaction, bool missed)
        {
            Damager = damager;
            Damagee = damagee;
            this.stat = new StatModifier(stat);
            this.critical = critical;
            Element = element;
            Direction = direction;
            this.reaction = reaction;
            Missed = missed;
            HitPosition = hitPosition;
            Pure = false;
        }

        public DamageData(StatID stat, float value)
        {
            Damager = null;
            Damagee = null;
            this.stat = new StatModifier(stat, value);
            Element = new();
            Direction = Vector3.zero;
            HitPosition = Vector3.zero; // Default value for HitPosition
            reaction = new Reaction2();
            critical = false;
            Missed = false;
            Pure = false;
        }

        public void ClearData()
        {
            Damager = null;
            Damagee = null;
            stat = new StatModifier(null, 0);
            Element = new ElementMultiplier(null);
            Direction = Vector3.zero;
            HitPosition = Vector3.zero;
            reaction = new Reaction2();
            critical = false;
            Missed = false;
            Pure = false;
        }
    }


    [System.Serializable]
    public struct MDamageableProfile
    {
        [Tooltip("Name of the Profile. This is used for Setting a New Damageable Profile. E.g. When the Animal is blocking or Parrying")]
        public string name;

        [Tooltip("Type of surface the Damageable is. (Flesh, Metal, Wood,etc)")]
        public SurfaceID surface;

        [Tooltip("Multiplier for the Stat modifier Value. Use this to increase or decrease the final value of the Stat")]
        public FloatReference multiplier;

        [Tooltip("When Enabled the animal will rotate towards the Damage direction")]
        public BoolReference AlignToDamage;

        public BoolReference ignoreDamagerReaction;

        [Tooltip("Reaction to apply to itself when the damage is done")]
        public Reaction2 damageReaction;

        [Tooltip(" Reaction to apply to itself when it receives a critical damage")]
        public Reaction2 criticalReaction;

        [Tooltip(" Reaction to apply to the one doing the damage")]
        public Reaction2 damagerReaction;

        [Tooltip("Elements that affect the MDamageable")]
        public List<ElementMultiplier> elements;

        public bool Events;
        [Hide("Events")]
        public UnityEvent OnProfileEnter;
        [Hide("Events")]
        public UnityEvent OnProfileExit;

        public readonly float Multiplier => multiplier.Value;

        public MDamageableProfile(string Name, SurfaceID surface,
            Reaction2 reaction, Reaction2 criticalReaction, Reaction2 DamagerReaction, BoolReference ignoreDamagerReaction,
            FloatReference multiplier, BoolReference AlignToDamage, List<ElementMultiplier> elements)
        {
            this.name = Name;
            this.surface = surface;
            this.damageReaction = reaction;
            this.damagerReaction = DamagerReaction;
            this.multiplier = multiplier;
            this.criticalReaction = criticalReaction;
            this.AlignToDamage = AlignToDamage;
            this.elements = elements;
            this.ignoreDamagerReaction = ignoreDamagerReaction;
            OnProfileEnter = new();
            OnProfileExit = new();
            Events = false;
        }

        public MDamageableProfile(MDamageableProfile newP)
        {
            name = newP.name;
            surface = newP.surface;
            damageReaction = newP.damageReaction;
            damagerReaction = newP.damagerReaction;
            multiplier = newP.multiplier;
            criticalReaction = newP.criticalReaction;
            AlignToDamage = newP.AlignToDamage;
            elements = new(newP.elements);
            ignoreDamagerReaction = newP.ignoreDamagerReaction;
            OnProfileEnter = newP.OnProfileEnter;
            OnProfileExit = newP.OnProfileExit;
            Events = false;
        }
    }
}