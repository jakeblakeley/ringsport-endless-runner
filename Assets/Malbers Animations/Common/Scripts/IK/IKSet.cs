using MalbersAnimations.Events;
using MalbersAnimations.Scriptables;
using MalbersAnimations.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;


namespace MalbersAnimations.IK
{
    [System.Serializable]
    public class IKSet
    {
        public StringReference name;

        public string Name => name.Value;


        public bool active = true;

        [Tooltip("Smoothly Enable the IK Set Weight")]
        [Min(0)] public float EnableTime = 0.0f;

        [Tooltip("Smoothly Disable the IK Set Weight")]
        [Min(0)] public float DisableTime = 0.25f;

        [Range(0f, 1f)]
        [Tooltip("Weight of the IK Set")]
        [SerializeField] private float weight = 1f;

        [Tooltip("Use this Targets array to assign IK goals, Targets or Transform References to the IK Processors")]
        public TransformReference[] Targets;


        [Tooltip("Clears all Targets on the Set if the Set gets disabled")]
        public bool ClearTargetsOnDisable = false;

        ////Cache the values of the Targets right after animation and before IK
        //public TransformValues[] CacheTargets { get; set; }

        [Tooltip("Reference for the Aimer Component to get Directions")]
        public Aim aimer;

        [SerializeReference]
        public List<IKProcessor> IKProcesors = new();


        [SerializeReference]//, SubclassSelector]
        public List<WeightProcessor> weightProcessors = new();

        [HideInInspector] public int SelectedIKProcessor; // Inspector Only!!


        [Tooltip("Lerp the Weight of the IK Set. Set the value to zero to ignore lerping")]
        [Min(0)] public float LerpWeight = 5f;

        public FloatEvent OnWeightChanged = new();
        public UnityEvent OnSetEnable = new();
        public UnityEvent OnSetDisable = new();

        public float FinalWeight { get; private set; }

        public int CurrentState { get; private set; }

        public int CurrentStance { get; private set; }

        ///// <summary> Reference for the Animator component </summary>
        //public Animator Animator { get; set; }

        /// <summary>  Reference for Setting the Owner to do Coroutine stuffs</summary>
        public IKManager Owner { get; set; }

        public AnimationCurve EnterLerp = new(new Keyframe(0, 0), new Keyframe(1, 1));
        public AnimationCurve ExitLerp = new(new Keyframe(0, 0), new Keyframe(1, 1));

        /// <summary> Shader Variables between IK Processors</summary>
        public Dictionary<string, object> sharedVars = new();

        public List<IKProcessor> Processors => IKProcesors;

        public float Weight
        {
            get => weight;
            set
            {
                weight = value;
                // Debug.Log($"weight: {weight}"); 
            }
        }

        public virtual void OnEnable(Animator anim, HashSet<int> animParams)
        {
            for (int i = 0; i < weightProcessors.Count; i++)
            {
                weightProcessors[i].OnEnable(this, anim);
            }


            for (int i = 0; i < Processors.Count; i++)
            {
                Processors[i].OnEnable(this, anim, i);
            }
        }

        public virtual void OnDisable(Animator anim, HashSet<int> animParams)
        {
            for (int i = 0; i < weightProcessors.Count; i++)
            {
                weightProcessors[i].OnDisable(this, anim);
            }

            for (int i = 0; i < Processors.Count; i++)
            {
                Processors[i].OnDisable(this, anim, i);
            }
        }


        public virtual void Initialize(IKManager owner)
        {
            Targets ??= new TransformReference[0]; //Make sure the Target list is not Null
            Owner = owner; //Cache the Owner for Coroutines

            if (aimer == null) aimer = owner.GetComponent<Aim>(); //Cache the Aimer Reference

            FinalWeight = 0; //Initialize the Cache Weight on zero

            for (int i = 0; i < Processors.Count; i++)
            {
                var processor = Processors[i];

                if (processor != null)
                {
                    processor.AnimParameterHash = TryAnimParameter(processor.AnimParameter, owner.animatorHashParams); //Store if the anim Param can be found.
                    processor.Start(this, owner.animator, i);
                }
            }
        }


        //Send 0 if the Animator does not contain
        public int TryAnimParameter(string param, HashSet<int> animParams)
        {
            var AnimHash = Animator.StringToHash(param);
            return animParams.Contains(AnimHash) ? AnimHash : 0;
        }

        public void CacheValues(Animator anim)
        {
            if (active)
            {
                for (int i = 0; i < IKProcesors.Count; i++)
                {
                    var processor = Processors[i];
                    processor.CacheValue(this, anim, i);
                }

                //for (int i = 0; i < Targets.Length; i++)
                //{
                //    if (Targets[i] != null && Targets[i].Value != null)
                //        CacheTargets[i] = new TransformValues(Targets[i].Value);
                //}
            }
        }

        public void OnAnimatorIK(Animator anim, float GlobalWeight, float deltaTime, int LayerIndex)
        {
            DoProcessor(anim, GlobalWeight, deltaTime, true, LayerIndex);
        }

        public void LateUpdate(Animator anim, float GlobalWeight, float deltaTime)
        {
            DoProcessor(anim, GlobalWeight, deltaTime, false, 0);
        }

        protected virtual void DoProcessor(Animator anim, float GlobalWeight, float deltaTime, bool OnAnimatorIK, int LayerIndex)
        {
            if (active && anim != null)
            {
                float preWeight = GetWeightProcessor(GlobalWeight, anim);

                for (int i = 0; i < Processors.Count; i++)
                {
                    var processor = Processors[i];

                    if (processor.Active)
                    {
                        var IKProcessorWeight = FinalWeight * processor.Weight;

                        IKProcessorWeight *= processor.GetProcessorAnimWeight(anim); //Process Local Weight

                        if (IKProcessorWeight > 0)
                        {
                            if (FinalWeight > 0.999f) FinalWeight = 1;
                            else if (FinalWeight < 0.001f) FinalWeight = 0;

                            if (OnAnimatorIK)
                                processor.OnAnimatorIK(this, anim, i, IKProcessorWeight, LayerIndex); // Process first the IK Logic after the weight
                            else
                                processor.LateUpdate(this, anim, i, IKProcessorWeight); // Process first the IK Logic after the weight
                        }
                        GetFinalWeight(preWeight, deltaTime);
                    }
                }
            }
        }

        protected virtual float GetWeightProcessor(float GlobalWeight, Animator anim)
        {
            float preWeight = Weight * GlobalWeight;

            if (weightProcessors != null)
            {
                for (int i = 0; i < weightProcessors.Count; i++)
                {
                    if (weightProcessors[i].Active)
                    {
                        var result = weightProcessors[i].Process(this, anim, preWeight);
                        //Invert the weight
                        if (weightProcessors[i].Invert) result = (1 - result);

                        preWeight *= result;

                        if (preWeight <= 0.001f) return 0; //If the weight is too low then return 0 and don't continue processing the IK Processors (this is to avoid unnecessary calculations)
                    }
                }
            }
            return preWeight;
        }

        private void GetFinalWeight(float finalWeight, float deltaTime)
        {
            if (FinalWeight == finalWeight) return; //If the final weight is the same as the current weight then ignore the rest

            //finalWeight *= processor.GetProcessorAnimWeight(anim); //Process the Anim Weight

            FinalWeight = LerpWeight > 0 ? Mathf.Lerp(FinalWeight, finalWeight, deltaTime * LerpWeight) : finalWeight; //Lerp when the LerpWeight is higher than 0

            OnWeightChanged.Invoke(FinalWeight);
        }

        public virtual void Enable(bool value)
        {
            //Clear the Coroutine it one was enabled
            if (C_EnableSmooth != null) Owner.StopCoroutine(C_EnableSmooth);

            C_EnableSmooth = value ? EnableSmooth() : DisableSmooth(); //Set Enable or Disable Smooth Coroutine

            if (Owner.gameObject.activeInHierarchy && Owner.enabled)
                Owner.StartCoroutine(C_EnableSmooth);
        }



        public virtual void SetWeight(bool value)
        {
            if (!active) return; //Ignore if the set is not active

            Weight = value ? 1 : 0;
        }

        private IEnumerator C_EnableSmooth;

        private IEnumerator EnableSmooth()
        {
            var elapsedTime = 0f;
            var startWeight = Weight;
            active = true;

            OnSetEnable.Invoke();

            while (Weight != 1 && /*(EnableTime > 0) &&*/ (elapsedTime <= EnableTime))
            {
                Weight = Mathf.Lerp(startWeight, 1, EnterLerp.Evaluate(elapsedTime / EnableTime));
                elapsedTime += Time.deltaTime;
                yield return null;
            }
            Weight = 1;

            yield return null;
        }


        private IEnumerator DisableSmooth()
        {
            var elapsedTime = 0f;
            var startWeight = Weight;

            while (Weight != 0 && (DisableTime > 0) && (elapsedTime <= DisableTime))
            {
                Weight = Mathf.Lerp(startWeight, 0, ExitLerp.Evaluate(elapsedTime / DisableTime));
                elapsedTime += Time.deltaTime;
                yield return null;
            }

            Weight = 0;
            yield return null;

            if (ClearTargetsOnDisable) //Clear all targets if the IK was disabled
            {
                var length = Targets.Length;
                Targets = new TransformReference[length];
            }

            OnSetDisable.Invoke();
            active = false;
        }

        public virtual void SetTarget(Transform target, int index)
        {
            Targets[index] = target;
        }

        public virtual void ClearTarget(int index)
        {
            Targets[index].Value = null;
        }

        public virtual void ClearAllTargets()
        {
            for (int i = 0; i < Targets.Length; i++)
                Targets[i] = null;
        }

        public virtual void SetTargets(Transform[] newTargets)
        {
            Targets = new TransformReference[newTargets.Length];
            // CacheTargets = new TransformValues[newTargets.Length];

            for (int i = 0; i < Targets.Length; i++)
            {
                if (newTargets[i] != null)
                    Targets[i] = new(newTargets[i]);
            }
        }


        internal void Processor_SetEnable(string processor, bool value)
        {
            for (int i = 0; i < Processors.Count; i++)
            {
                if (Processors[i].name.Contains(processor))
                {
                    Processors[i].Active = value;
                    break;
                }
            }
        }

        internal void Verify(IKManager manager, Animator animator)
        {
            for (int i = 0; i < Processors.Count; i++)
                Processors[i]?.Verify(manager, this, animator, i);
        }

        internal void OnValidate(IKManager iKManager)
        {
            for (int i = 0; i < Processors.Count; i++)
            {
                Processors[i]?.OnValidate(iKManager, this);
            }
        }
    }
}