using System;
using UnityEngine;
using UnityEngine.Audio;

namespace MalbersAnimations.Reactions
{
    [System.Serializable]

    [AddTypeMenu("Unity/Sound [Audio]")]
    public class SoundReaction : Reaction
    {
        public override string DynamicName
        {
            get
            {
                if (resource == null) return base.DynamicName;
                return $"Audio Reaction Play ({resource.name}). [Vol [{volume.minValue:F2} - {volume.maxValue:F2}]]";
            }
        }

        public override Type ReactionType => typeof(AudioSource);

        public AudioResource resource;
        [MinMaxRange(0, 1)] public RangedFloat volume = new(1, 1);
        [MinMaxRange(-3, 3)] public RangedFloat pitch = new(1, 1);
        [Range(0, 1)] public float spatialBlend = 0f;
        public bool loop = false;

        protected override bool _TryReact(Component component)
        {
            var audio = (component as AudioSource);
            audio.resource = resource;
            audio.volume = volume.RandomValue;
            audio.pitch = pitch.RandomValue;
            audio.spatialBlend = spatialBlend;
            audio.loop = loop;

            audio.Play();
            return true;
        }
    }
}
