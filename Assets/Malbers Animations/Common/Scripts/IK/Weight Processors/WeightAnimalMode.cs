using System.Collections.Generic;
using UnityEngine;

namespace MalbersAnimations.IK
{
    /// <summary>  Process the weight by checking the Look At Angle of the Animator / </summary>
    [System.Serializable, AddTypeMenu("Animal/Weight Mode")]
    public class WeightAnimalMode : WeightProcessor
    {
        public override string DynamicName
        {
            get
            {
                var hasMode = Modes.Count > 0;

                var result = ExcludeAllModes ? "Exclude All Modes" : "Include All Modes";

                if (hasMode)
                {
                    result = $"{(Modes.include ? "Include" : "Exclude")}: ";

                    foreach (var ID in Modes.IDs)
                    {
                        result += $" {ID.name},";
                    }
                    result = result.TrimEnd(','); // Remove the last comma

                }

                return $"Weight Mode ({result})";
            }
        }


        public bool ExcludeAllModes => Modes.Count == 0 && !Modes.include;
        public bool OnlyModes => Modes.Count == 0 && Modes.include;



        [Tooltip("List of Modes to check the weight." +
            "\nEmpty List + Include it will set the weight to 1 when a mode is playing." +
            "\nEmpty List + Exclude it will set the weight to 0 when a mode is playing.")]
        public IDListCheck<ModeID> Modes = new();

        [Tooltip("Exclude these abilities. Meaning if the Character is NOT on these abilities then the weight is set to 1")]
        public bool excludeAbilities = false;

        [Tooltip("Abilities to check if the animal is on. Weight will be set to 1")]
        public List<int> Abilities = new();

        private ICharacterAction character;

        private float modeWeight = 0;
        public override void OnEnable(IKSet set, Animator anim)
        {
            if (anim.TryGetComponent(out character))
            {
                character.ModeStart += OnModeStart;
                character.ModeEnd += OnModeEnd;
            }
            else
            {
                Active = false;
                Debug.LogWarning("The Mode weight processor requires an Animal Controller. Disabling it");
            }

            modeWeight = OnlyModes ? 0 : 1;
        }

        private void OnModeEnd(int mode, int ability)
        {
            modeWeight = OnlyModes ? 0 : 1;
        }

        private void OnModeStart(int mode, int ability)
        {
            if (ExcludeAllModes)
            {
                modeWeight = 0;
            }
            else if (OnlyModes)
            {
                modeWeight = 1;
            }
            else
            {
                modeWeight = Modes.Check(mode) ? 1 : 0;

                if (Abilities.Count > 0) //Check for abilities too
                {
                    modeWeight *= (Abilities.Contains(ability) ? 1 : 0);
                    if (excludeAbilities) modeWeight = 1 - modeWeight;
                }
            }
        }

        public override void OnDisable(IKSet set, Animator anim)
        {
            if (character != null) character.ModeStart -= OnModeStart;
        }

        public override float Process(IKSet set, Animator animator, float weight) => weight * modeWeight;
    }
}
