using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace MalbersAnimations.Controller
{
    // MWC: removed [TrackBindingType] — that attribute is only valid on TrackAsset, not PlayableAsset
    public class ACGroundAlignerClip : PlayableAsset, ITimelineClipAsset
    {
        // MWC: Blending allows smooth weight fade when clips overlap or at clip edges
        public ClipCaps clipCaps => ClipCaps.Blending;

        public float Offset = 0;
        public float Distance = 2f;
        public bool HasHipPivot = true;

        public ExposedReference<MAnimal> animalEndLocation;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<ACGroundAlignerBehaviour>.Create(graph);
            var behaviour = playable.GetBehaviour();

            behaviour.distance = Distance;
            behaviour.HasHipPivot = HasHipPivot;
            behaviour.Offset = Offset;
            behaviour.EndLocation = animalEndLocation.Resolve(graph.GetResolver());

            return playable;
        }
    }
}
