using UnityEngine;
using UnityEngine.Playables;

namespace MalbersAnimations.Controller
{
    public class ACGroundAlignerBehaviour : PlayableBehaviour
    {
        public float distance;
        public bool HasHipPivot;
        public MAnimal EndLocation; // NOTE: MWC - EndLocation is resolved by the Clip but not yet consumed; incomplete feature

        public float Offset;

        public void GroundRayCast(MAnimal animal)
        {
            var hit_Chest = new RaycastHit() { normal = Vector3.zero };
            var hit_Hip = new RaycastHit();
            hit_Chest.distance = hit_Hip.distance = animal.Height;

            bool FrontRay = false;

            // MWC: use local variable — modifying the field directly caused distance to grow unboundedly each frame
            float scaledDistance = distance * animal.ScaleFactor;

            Vector3 MainPoint = animal.Main_Pivot_Point;

#if UNITY_EDITOR
            MDebug.DrawWireSphere(animal.t.position, Color.black, 0.05f * animal.ScaleFactor, 1);
#endif

            if (Physics.Raycast(MainPoint, -animal.Up, out hit_Chest, scaledDistance, animal.GroundLayer, QueryTriggerInteraction.Ignore))
            {
                FrontRay = true;

                // MWC: moved inside the block where they're actually used
                Vector3 SlopeNormal = hit_Chest.normal;
                Vector3 SlopeDirection = Vector3.ProjectOnPlane(animal.Gravity, SlopeNormal).normalized;

#if UNITY_EDITOR
                MDebug.DrawRay(MainPoint, -animal.Up * scaledDistance, Color.blue, 0.2f);
                MDebug.DrawRay(MainPoint - animal.Up * hit_Chest.distance, 0.2f * animal.ScaleFactor * SlopeDirection, Color.red, 0.2f);
                MDebug.DrawWireSphere(MainPoint - animal.Up * hit_Chest.distance, Color.green, 0.1f * animal.ScaleFactor);
                MDebug.Draw_Arrow(hit_Chest.point, SlopeDirection * 0.5f, Color.black, 0, 0.1f);
#endif
            }

            bool MainRay;

            if (animal.Has_Pivot_Hip)
            {
                var hipPoint = animal.Pivot_Hip.World(animal.t);

                if (Physics.Raycast(hipPoint, -animal.Up, out hit_Hip, scaledDistance, animal.GroundLayer, QueryTriggerInteraction.Ignore))
                {
                    MainRay = true;
                    if (!FrontRay) hit_Chest = hit_Hip;
                }
                else
                {
                    MainRay = false;
                    if (FrontRay) hit_Hip = hit_Chest;
                }
            }
            else
            {
                MainRay = FrontRay;
                hit_Hip = hit_Chest;
            }

            // MWC: guard against zero-vector SurfaceNormal and NaN rotation when no ray hits terrain
            if (!FrontRay && !MainRay) return;

            Vector3 direction = (hit_Chest.point - hit_Hip.point).normalized;
            Vector3 Side = Vector3.Cross(animal.UpVector, direction).normalized;
            Vector3 SurfaceNormal = Vector3.Cross(direction, Side).normalized;

            if (!MainRay && FrontRay)
            {
                SurfaceNormal = hit_Chest.normal;
            }

            var rot = AlignRotation(animal, SurfaceNormal);
            var pos = AlignPosition(animal, hit_Hip.distance);

            animal.t.SetPositionAndRotation(pos, rot);
        }

        public virtual Quaternion AlignRotation(MAnimal animal, Vector3 alignNormal)
        {
            Quaternion AlignRot = Quaternion.FromToRotation(animal.Up, alignNormal) * animal.Rotation;
            return AlignRot;
        }

        // MWC: changed from internal to public virtual for consistency with AlignRotation
        public virtual Vector3 AlignPosition(MAnimal animal, float dist)
        {
            float difference = (animal.Height + Offset) - dist;
            Vector3 align = animal.Rotation * new Vector3(0, difference, 0);
            return animal.Position + align;
        }
    }
}
