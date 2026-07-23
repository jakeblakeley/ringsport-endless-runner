using UnityEngine;
using UnityEngine.Playables;

namespace MalbersAnimations.Controller
{
    public class ACGroundAlignerMixer : PlayableBehaviour
    {
        bool m_ShouldInitializeTransform = true;
        Vector3 m_InitialPosition;
        Quaternion m_InitialRotation;
        bool m_InitialIsKinematic; // MWC: store RB kinematic state to restore on clip exit

        MAnimal m_TrackBinding; // MWC: cached reference so OnBehaviourPause can restore state

        void InitializeIfNecessary(MAnimal animal)
        {
            if (m_ShouldInitializeTransform)
            {
                m_InitialPosition = animal.transform.position;
                m_InitialRotation = animal.transform.rotation;
                m_InitialIsKinematic = animal.RB.isKinematic; // MWC: capture before overriding
                animal.RB.isKinematic = false; // MWC: moved from per-frame GroundRayCast; set once at start
                m_ShouldInitializeTransform = false;
            }
        }

        // MWC: restore transform and physics state when the timeline clip ends or graph stops
        public override void OnBehaviourPause(Playable playable, FrameData info)
        {
            if (m_TrackBinding != null && !m_ShouldInitializeTransform)
            {
                m_TrackBinding.transform.SetPositionAndRotation(m_InitialPosition, m_InitialRotation);
                m_TrackBinding.RB.isKinematic = m_InitialIsKinematic;
                m_ShouldInitializeTransform = true;
            }
        }

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            var trackBinding = playerData as MAnimal;
            if (trackBinding == null) return;

            m_TrackBinding = trackBinding;
            InitializeIfNecessary(trackBinding);

            int inputCount = playable.GetInputCount();

            for (int i = 0; i < inputCount; i++)
            {
                float inputWeight = playable.GetInputWeight(i);
                if (inputWeight <= 0f) continue; // MWC: skip clips with no influence

                ScriptPlayable<ACGroundAlignerBehaviour> inputPlayable = (ScriptPlayable<ACGroundAlignerBehaviour>)playable.GetInput(i);
                ACGroundAlignerBehaviour input = inputPlayable.GetBehaviour();

                input.GroundRayCast(trackBinding);
            }
        }
    }
}
