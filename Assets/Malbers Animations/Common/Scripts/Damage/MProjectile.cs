using MalbersAnimations.Scriptables;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using MalbersAnimations.Controller;
using UnityEngine.Pool;
using MalbersAnimations.Events;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.Weapons
{
    public enum ProjectileRotation { None, FollowTrajectory, Random, Axis };

    /// <summary>  Pool - Added DisableOnImpact for ProjectilePool </summary>

    //MWC: removed StickOnHitSurface; StickOnSurface keeps the bone-accurate closest-transform path (kept enum order so existing serialized values stay valid)
    public enum ImpactBehaviour { None, StickOnSurface, DestroyOnImpact, ActivateRigidBody, DisableOnImpact };
    /// <summary> Pool - End </summary>

    //MWC: Magic Projectile - Projectile movement type (Flying = original trajectory; Rolling = rigidbody physics rolling on the ground)
    public enum ProjectileType { FlyingProjectile, RollingProjectile };

    //MWC: Magic Projectile - When a Rolling Projectile should deactivate
    public enum DeactivateProjectile { Life, MaxDistance };


    [AddComponentMenu("Malbers/Damage/Projectile")]
    [SelectionBase]
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/secondary-components/mdamager/mprojectile")]
    public class MProjectile : MDamager, IProjectile
    {
        /// <summary>  Pool - Added Tooltip to explain impact Behavior new Option  </summary>  
        [Tooltip("Setting to Destroy On Impact \nWill not use ProjectilePool!")]
        /// <summary>  Pool - End </summary>  
        /// 
        public ImpactBehaviour impactBehaviour = ImpactBehaviour.None;
        public ProjectileRotation rotation = ProjectileRotation.None;

        //MWC: Magic Projectile - Rolling Projectile fields (ported from the Magic Projectile package) -------------
        [Tooltip("Flying Projectile for default behaviour, \nRolling Projectile for a projectile that rolls on the ground")]
        public ProjectileType projectileType = ProjectileType.FlyingProjectile;
        [Tooltip("Remove projectile when Lifetime or MaxDistance reached. \nUsed only for Rolling Projectile")]
        public DeactivateProjectile deactivateProjectile = DeactivateProjectile.Life;

        [Tooltip("Maximal distance projectile will travel. \nUsed only for Rolling Projectile")]
        public FloatReference rollingProjectileMaxDistance = new(20);
        [Tooltip("Destroy projectile on MaxDistance reached. Set to False to destroy after LifeImpact. \nUsed only for Rolling Projectile")]
        public BoolReference rollingProjectileDestroyOnMaxDistance = new(false);
        [Tooltip("Destroy projectile on Life time reached. Set to False to destroy after LifeImpact. \nUsed only for Rolling Projectile")]
        public BoolReference rollingProjectileDestroyOnLife = new(false);

        [SerializeField, Tooltip("Rolling projectile Ground Layer mask. \nUsed only for Rolling Projectile")]
        private LayerReference m_rollingProjectileGroundMask = new(1);
        public LayerMask RollingProjectileGroundMask { get => m_rollingProjectileGroundMask.Value; set => m_rollingProjectileGroundMask.Value = value; }

        [Tooltip("Rolling projectile only on ground, no bouncing. \nUsed only for Rolling Projectile")]
        public BoolReference rollingProjectileRollOnGroundOnly = new(false);
        [Tooltip("Rolling projectile adds additional distance from AimOrigin in the Character forward direction by the DistanceFromGround amount. \nUsed only for Rolling Projectile")]
        public BoolReference rollingProjectileDistanceFromAimOrigin = new(false);
        [Tooltip("Rolling projectile maximum angle inclination when moving UP the slope. Set to 0 disables this check. \nUsed only for Rolling Projectile")]
        public FloatReference rollingProjectileMaxUpSlopeAngle = new(30);
        [Tooltip("Rolling projectile maximum angle inclination when moving DOWN the slope. Set to 0 disables this check. \nUsed only for Rolling Projectile")]
        public FloatReference rollingProjectileMaxDownSlopeAngle = new(0);
        [Tooltip("Rolling projectile maximum deflection angle. Set to 0 disables this check. \nUsed only for Rolling Projectile")]
        public FloatReference rollingProjectileMaxDeflectionAngle = new(30);

        private float projectileDistanceFromGround = 0;
        private float TraveledDistance = 0;
        private float TraveledTime = 0;
        private bool rollingProjectileMainColliderHit = false;
        private bool rollingProjectileMainColliderHitWithTag = false;
        //MWC: Magic Projectile - End Rolling Projectile fields -------------------------------------------------

        [Tooltip("Rotation amount around trajectory axis when the projectile is set to Follow Trajectory")]
        public float TrajectoryRoll = 0;
        public Vector3 TrajectoryRollOffset;

        public float Penetration = 0.1f;

        [SerializeField, Tooltip("Keep Projectile Damage and Layer Values, The Throwable-Shootable component will not override the Damage Values")]
        protected BoolReference m_KeepDamageValues = new(false);

        [SerializeField, Tooltip("Gravity applied to the projectile, if gravity is zero the projectile will go straight. If the Projectile is thrown by a Projectile Thrower." +
            "It will inherit the gravity from it")]
        protected Vector3Reference gravity = new(Physics.gravity);

        [SerializeField, Tooltip("Apply Gravity after certain distance is reached")]
        private FloatReference m_AfterDistance = new(0f);
        public float AfterDistance { get => m_AfterDistance.Value; set => m_AfterDistance.Value = value; }

        [Tooltip("Life of the Projectile on the air, if it has not touch anything on this time it will destroy it self")]
        public FloatReference Life = new(10f);
        [Tooltip("Life of the Projectile After Impact. If the projectile is not destroyed on impact, then wait this time to do it. (0 -> Ignores it) ")]
        public FloatReference LifeImpact = new(0f);

        [Tooltip("Multiplier of the Force to Apply to the object the projectile impact ")]
        public FloatReference PushMultiplier = new(1);

        [Tooltip("Torque for the rotation of the projectile")]
        public FloatReference torque = new(50f);
        [Tooltip("Axis Torque for the rotation of the projectile")]
        public Vector3 torqueAxis = Vector3.up;

        [Tooltip("Offset to position the projectile when is Instantiated on the weapon. E.g. (Arrow in the Bow) ")]
        public Vector3 m_PosOffset;

        [Tooltip("Offset to rotation the projectile when is Instantiated on the weapon. E.g. (Arrow in the Bow) ")]
        public Vector3 m_RotOffset;

        [Tooltip("Offset to scale the projectile when is Instantiated on the weapon. E.g. (Arrow in the Bow) ")]
        public Vector3 m_ScaleOffset;

        [Tooltip("Use Spherecast to predict the trajectory")]
        public bool useRadius = false;
        [Tooltip("Radius of the projectile to cast a ray to find targets better")]
        public FloatReference Radius = new(0.01f);

        public UnityEvent OnFire = new();                       //Send the transform to the event

        //MWC: Magic Projectile - events used by Rolling Projectile and Magic Particle Triggers ------------------
        [Tooltip("Triggered when the projectile main collider hits a Tagged gameObject (Rolling Projectile)")]
        public TransformEvent OnHitTag = new();
        [Tooltip("Triggered when a particle (not the main collider) hits a gameObject; sends the hit position")]
        public Vector3Event OnHitPositionWithoutImpact = new();
        [Tooltip("Triggered when a particle (not the main collider) hits a gameObject; sends the hit transform")]
        public TransformEvent OnHitTransformWithoutImpact = new();
        [Tooltip("Triggered when a Rolling Projectile reaches max travel distance (True); deflection or slope angle sends False")]
        public BoolEvent OnReachedMaxDistance = new();
        //MWC: Magic Projectile - End events -------------------------------------------------------------------

        [Tooltip("Reference for the Projectile Rigidbody")]
        public Rigidbody rb;
        [Tooltip("Reference for the Projectile collider")]
        public Collider m_collider;

        [Tooltip("Reference for the trail renderer")]
        public TrailRenderer m_trail;

        public float DragOnImpact = 1;

        protected Vector3 Prev_pos;

        protected Transform T;

        #region Properties
        /// <summary>Initial Velocity (Direction * Power) </summary>
        public Vector3 Velocity { get; set; }

        /// <summary>Has the projectile impacted with something</summary>
        public bool HasImpacted { get; set; }

        /// <summary>Do Fly Raycast</summary>
        protected bool doRayCast;

        /// <summary>Is the Projectile Flying</summary>
        public bool IsFlying { get; set; }


        public Vector3 Gravity { get => gravity.Value; set => gravity.Value = value; }
        public bool KeepValues { get => m_KeepDamageValues.Value; set => m_KeepDamageValues.Value = value; }
        public Vector3 TargetHitPosition { get; set; }
        public bool FollowTrajectory => rotation == ProjectileRotation.FollowTrajectory;
        public bool DestroyOnImpact => impactBehaviour == ImpactBehaviour.DestroyOnImpact;

        public Vector3 PosOffset { get => m_PosOffset; set => m_PosOffset = value; }
        public Vector3 RotOffset { get => m_RotOffset; set => m_RotOffset = value; }
        // public Vector3 ScaleOffset { get => m_ScaleOffset; set => m_ScaleOffset = value; }
        #endregion

        public RayCastHitEvent OnRayCastHit = new();

        /// <summary> Pool - End  </summary>  
        public IObjectPool<GameObject> Pool { get; set; }
        [HideInInspector] public int Editor_Tabs1;

        private bool colliderInitialEnable;

        protected virtual void Awake()
        {
            T = transform;
            if (!rb) TryGetComponent(out rb);
            if (!m_collider)
            {
                m_collider = GetComponentInChildren<Collider>();
                colliderInitialEnable = m_collider ? m_collider.enabled : false;
            }

            m_audio = gameObject.GetOrAddComponent<AudioSource>(); //Gets|Add the AudioSource
            m_audio.spatialBlend = 1;
            m_audio.maxDistance = 50;

            //Create a Link to Stick on Surface 
            if (impactBehaviour == ImpactBehaviour.StickOnSurface) //MWC: removed StickOnHitSurface branch
            {
                LinkStickOnSurface = new GameObject($"Link [{name}]");
                LinkStickOnSurface.transform.SetParent(transform);
                LinkStickOnSurface.transform.ResetLocal();
            }

            //MWC: Magic Projectile - default the Rolling Projectile ground mask to "Default" if nothing is set
            if (RollingProjectileGroundMask == 0)
                RollingProjectileGroundMask = LayerMask.GetMask("Default");
        }


        protected virtual void OnDisable()
        {
            StopAllCoroutines();
            CancelInvoke();

            if (m_collider && impactBehaviour != ImpactBehaviour.ActivateRigidBody)
                m_collider.enabled = colliderInitialEnable; //The pool might enable or disable the collider
        }

        /// <summary> Initialize the Projectile main references and parameters</summary>
        protected virtual void Initialize()
        {
            HasImpacted = false;
            if (Life > 0f)
                this.Delay_Action(Life, () => DestroyProjectile());
        }

        /// <summary>
        /// Returns the last calculated normalized direction of the projectile. (updated while flying)
        /// </summary>
        public Vector3 NormalizedDirection { get; set; }
        public MShootable Thrower { get; internal set; }

        /// <summary> Prepare the Projectile for firing </summary>
        public virtual void Prepare(GameObject Owner, Vector3 Gravity,
            Vector3 ProjectileVelocity, LayerMask HitLayer, QueryTriggerInteraction triggerInteraction, IObjectPool<GameObject> thisProjectilePool)
        {
            if (!KeepValues)
            {
                this.Layer = HitLayer;
                this.TriggerInteraction = triggerInteraction;
            }

            this.UserGo = Owner;
            this.Gravity = Gravity;
            this.Velocity = ProjectileVelocity;
            this.MinForce = this.MaxForce = Velocity.magnitude; //CustomPatch: removed extra redundant magnitude calculation
            /// <summary> Pool - Set thisProjectilePool  </summary>
            this.Pool = thisProjectilePool;
            /// <summary> Pool - End </summary>  
            Debugging("Projectile Prepared", this);
        }

        public virtual void Fire(Vector3 ProjectileVelocity)
        {
            this.Velocity = ProjectileVelocity;
            this.MinForce = this.MaxForce = Velocity.magnitude; //CustomPatch: removed extra redundant magnitude calculation
            Fire();
        }

        public virtual void Fire()
        {
            Initialize();

            gameObject.SetActive(true); //Just to make sure is working  
            Active = true;

            if (Velocity == Vector3.zero) //Hack when the Velocity is not set
            {
                Velocity = T.forward;
                this.MaxForce = 1;
                this.MinForce = 1;
            }

            doRayCast = true;

            if (m_collider && rb)
            {
                //MWC: Magic Projectile - Rolling enables its collider immediately so it can roll on the ground; Flying keeps the small delay so it doesn't collide with the thrower
                if (projectileType == ProjectileType.RollingProjectile)
                    EnableCollider(0);
                else
                    EnableCollider(0.1f); //Don't enable it right away so it does not collide with the thrower

                doRayCast = m_collider.isTrigger;
            }

            if (rb)
            {
                EnableRigidBody();
                rb.linearVelocity = Vector3.zero; //Reset the velocity IMPORTANT!

                if (rotation == ProjectileRotation.Random)
                {
                    rb.AddTorque(new Vector3(Random.value, Random.value, Random.value).normalized * torque, ForceMode.Acceleration);
                }
                else if (rotation == ProjectileRotation.Axis)
                {
                    rb.AddTorque(torqueAxis * torque.Value, ForceMode.Impulse);
                }
                //  Debug.Log("RIGID BODY Gravity");

                //MWC: Magic Projectile - Rolling launches using the projectile's own ForceMode (so friction can slow it); Flying keeps the original hardcoded VelocityChange
                if (projectileType == ProjectileType.RollingProjectile)
                {
                    rb.constraints = RigidbodyConstraints.None;
                    if (rotation == ProjectileRotation.None) rb.freezeRotation = true;
                    rb.AddForce(Velocity, forceMode);
                }
                else
                {
                    rb.AddForce(Velocity, ForceMode.VelocityChange);
                }
            }

            //MWC: Magic Projectile - pick the movement coroutine based on the projectile type
            if (projectileType == ProjectileType.RollingProjectile)
                StartCoroutine(RollingProjectile()); //Rigidbody physics rolling is done here.
            else
                StartCoroutine(FlyingProjectile()); //Trajectory movement is done here.


            OnFire.Invoke();

            //if (TryGetComponent<ICollectable>(out var pickable))
            //{
            //    pickable.Drop(); //if the Projectile is a pickable then drop it?
            //}

            Debugging("Projectile Fired", this);
        }

        public void EnableCollider(float time) => this.Delay_Action(time, Enable_Collider); //MWC: replaced reflection-based Invoke with the project's coroutine-based Delay_Action

        protected virtual void Enable_Collider()
        {
            if (m_collider && !m_collider.isTrigger)
                m_collider.enabled = true;
        }

        /// <summary> / Destroy Projectile on Life Time End  </summary>
        protected virtual void DestroyProjectile()
        {
            HasImpacted = false;
            if (impactBehaviour == ImpactBehaviour.DestroyOnImpact)
            {
                Debugging($"Life time elapsed [{Life}]. Destroy Projectile", null);
                Destroy(gameObject);
                if (LinkStickOnSurface) Destroy(LinkStickOnSurface);
            }
            /// <summary> Pool - Leave option to destroy and for everything else use Pool  </summary>
            else
            {
                Debugging($"Life time elapsed [{LifeImpact.Value}].[Projectile returned to the Pool]", null);

                //If the Projectile is sticking on the surface then remove the parent
                if (LinkStickOnSurface)
                {
                    transform.parent = null;
                    LinkStickOnSurface.transform.SetParent(transform);
                }

                if (m_trail != null) m_trail.Clear(); //Clear the Trail when the Projectile is Destroyed or Returned to the Pool

                StopAllCoroutines();

                Pool?.Release(gameObject);
            }
        }

        public virtual void Pool_Release(GameObject gameObject)
        {
            if (Pool != null)
            {
                Pool.Release(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        public virtual void ReturnToPool() => Pool.Release(gameObject);

        public virtual GameObject Pool_Get() => Pool.Get();


        protected virtual void OnCollisionEnter(UnityEngine.Collision other)
        {
            if (rb && rb.isKinematic) return;
            if (HasImpacted) return; //Do not check new Collisions
            if (IsInvalid(other.collider)) return;
            if (!enabled) return;

            if (Prev_pos == Vector3.zero) Prev_pos = T.position;

            //MWC: Magic Projectile - Rolling flags whether the main-collider hit was a Tagged target so ProjectileImpact routes OnHitTag vs OnHit (IsInvalid above already validated tags/owner/layer)
            if (projectileType == ProjectileType.RollingProjectile)
            {
                if (Tags != null && Tags.Length > 0) rollingProjectileMainColliderHitWithTag = true;
                else rollingProjectileMainColliderHit = true;
            }

            ProjectileImpact(other.rigidbody, other.collider, other.contacts[0].point, (other.collider.bounds.center - m_collider.transform.position).normalized, true);

        }

        protected virtual void OnTriggerEnter(Collider other)
        {
            if (HasImpacted) return; //Do not check new Collisions
            if (IsInvalid(other)) return;
            if (!enabled) return;

            if (Prev_pos == Vector3.zero) Prev_pos = T.position;

            //MWC: Magic Projectile - Rolling flags whether the main-collider hit was a Tagged target so ProjectileImpact routes OnHitTag vs OnHit (IsInvalid above already validated tags/owner/layer)
            if (projectileType == ProjectileType.RollingProjectile)
            {
                if (Tags != null && Tags.Length > 0) rollingProjectileMainColliderHitWithTag = true;
                else rollingProjectileMainColliderHit = true;
            }

            ProjectileImpact(other.attachedRigidbody, other, Prev_pos, (other.bounds.center - m_collider.transform.position).normalized, true);
        }

        private readonly WaitForFixedUpdate waitForFixedUpdate = new();

        /// <summary> Logic Applied when the projectile is flying</summary>
        protected virtual IEnumerator FlyingProjectile()
        {
            Vector3 start = transform.position;
            Prev_pos = start;
            //MWC: launch rotation used as the spin base for the Rigidbody Axis path (it spins around its own axis, not the trajectory)
            Quaternion startRot = transform.rotation;
            float deltatime = Time.fixedDeltaTime;

            Direction = Velocity.normalized; //

            int step = 1;

            Vector3 RotationAround = Vector3.zero;
            //MWC: accumulated spin angle (deg) used by the Axis rotation to tumble like a thrown axe while following the trajectory
            float spinAngle = 0;
            if (rotation == ProjectileRotation.Random)
                RotationAround = new Vector3(Random.value, Random.value, Random.value).normalized;
            else if (rotation == ProjectileRotation.Axis)
            {
                RotationAround = torqueAxis.normalized;

            }
            float TraveledDistance = 0;
            int NoGravityStep = 0;

            while (!HasImpacted && enabled)
            {
                var time = deltatime * step;
                var GravityTime = deltatime * (step - NoGravityStep);

                Vector3 next_pos = (start + Velocity * time) + (GravityTime * GravityTime * Gravity / 2);

                if (!rb)
                {
                    transform.position = Prev_pos; //If there's no Rigid body move the Projectile!!

                    if (rotation == ProjectileRotation.Random)
                    {
                        transform.Rotate(RotationAround, torque * deltatime, Space.World);
                        Debug.Log($"Random : ");
                    }
                    else if (rotation == ProjectileRotation.Axis)
                    {
                        //MWC: Axis rotation - face the trajectory, then spin around torqueAxis in self space (thrown-axe tumble)
                        //Base orientation aligned with the flight direction
                        Quaternion lookRot = Quaternion.LookRotation(Direction, Vector3.up);

                        //Accumulate the spin (torque = degrees per second around torqueAxis)
                        spinAngle += torque * deltatime;

                        //Right-multiply so the spin axis stays relative to the projectile's local frame
                        transform.rotation = lookRot * Quaternion.AngleAxis(spinAngle, RotationAround);
                    }
                }
                else
                {
                    // rb.velocity = Direction;
                    rb.MovePosition(Prev_pos);

                    //MWC: Axis rotation for the Rigidbody path - spin around torqueAxis in self space at 'torque' deg/sec.
                    //Driven manually so it stays consistent and does NOT follow the trajectory.
                    if (rotation == ProjectileRotation.Axis)
                    {
                        spinAngle += torque * deltatime;
                        rb.MoveRotation(startRot * Quaternion.AngleAxis(spinAngle, RotationAround));
                    }
                }

                Direction = (next_pos - Prev_pos);



                Debug.DrawLine(Prev_pos, next_pos, Color.yellow);
                if (Radius > 0)
                {
                    MDebug.DrawWireSphere(Prev_pos, Color.yellow, Radius);
                    MDebug.DrawWireSphere(next_pos, Color.yellow, Radius);
                }

                var Length = Vector3.Distance(next_pos, Prev_pos);

                if (Physics.SphereCast(Prev_pos, Radius, Direction, out RaycastHit hit, Length, Layer, triggerInteraction))
                {
                    if (!IsInvalid(hit.collider))
                    {
                        yield return waitForFixedUpdate;

                        OnRayCastHit.Invoke(hit);

                        ProjectileImpact(hit.rigidbody, hit.collider, hit.point, hit.normal);
                        yield break;
                    }
                }

                if (FollowTrajectory) //The Projectile will rotate towards de Direction
                {
                    transform.rotation = Quaternion.LookRotation(Direction, transform.up);

                    //Rotate around an axis while following a trajectory
                    if (TrajectoryRoll != 0)
                    {
                        transform.Rotate(Direction, TrajectoryRoll * deltatime, Space.World);
                    }
                }


                //Check if the gravity can be applied after distance
                if (TraveledDistance < AfterDistance)
                {
                    TraveledDistance += Direction.magnitude;
                    NoGravityStep++;
                }



                Prev_pos = next_pos;
                step++;

                yield return waitForFixedUpdate;
            }
            yield return null;
        }

        //MWC: Magic Projectile - Rolling Projectile movement (ported from the Magic Projectile package, adapted to the latest API: linearVelocity, Delay_Action, UserGo, cached T)
        protected virtual IEnumerator RollingProjectile()
        {
            if (rb == null)
            {
                Debug.LogError($"[{name}] - Rolling Projectile requires a RigidBody", this);
                yield break;
            }

            Vector3 start = T.position; // Set start position
            projectileDistanceFromGround = 0; // Set distance from ground

            RaycastHit raycastHit;

            if (rollingProjectileRollOnGroundOnly == true)
            {
                // RayCast for Ground Layers
                Physics.Raycast(T.position, Vector3.down, out raycastHit, Mathf.Infinity, RollingProjectileGroundMask, QueryTriggerInteraction.Ignore);

                // If there is a raycast result set the start position on the ground using the collider size
                if (raycastHit.collider != null && m_collider != null)
                {
                    if (m_collider is BoxCollider box)
                        projectileDistanceFromGround = box.size.y * T.localScale.y;

                    if (m_collider is SphereCollider sphere)
                        projectileDistanceFromGround = sphere.radius * T.localScale.y;

                    start = raycastHit.point + new Vector3(0, projectileDistanceFromGround, 0);
                }
            }
            else // If no raycast set the start position as the initial position
            {
                start = T.position;
            }

            if (rollingProjectileDistanceFromAimOrigin == true && UserGo != null)
            {
                start += UserGo.transform.forward * projectileDistanceFromGround;
            }

            // Place projectile at the start position
            T.position = start;

            TraveledDistance = 0; // Set Travel Distance
            TraveledTime = 0; // Set Travel Time

            Quaternion initialRotation = UserGo != null ? UserGo.transform.rotation : T.rotation; // Initial rotation of the projectile
            Vector3 prev_velocity = rb != null ? rb.linearVelocity : Velocity; // Initial velocity for the projectile

            // While loop that runs in FixedUpdate
            while (!HasImpacted && enabled)
            {
                if (deactivateProjectile == DeactivateProjectile.MaxDistance)
                {
                    TraveledDistance = Vector3.Distance(T.position, start); // Calculate Travel Distance

                    // If Travel Distance larger than Max Distance kill Projectile
                    if (TraveledDistance > rollingProjectileMaxDistance)
                    {
                        Debugging($"<color=yellow> <b>[Projectile reached maximum distance] </b> [{TraveledDistance}]. Stop projectile and destroy after LifeImpact. </color>", this);
                        StopRollingProjectile();
                        OnReachedMaxDistance.Invoke(true);

                        if (rollingProjectileDestroyOnMaxDistance == true) DestroyProjectile();
                        else this.Delay_Action(LifeImpact, DestroyProjectile);

                        yield break;
                    }
                }

                if (deactivateProjectile == DeactivateProjectile.Life)
                {
                    TraveledTime += Time.fixedDeltaTime; // Calculate Travel Time

                    if (TraveledTime > Life.Value) // If Travel Time larger than Life kill Projectile
                    {
                        Debugging($"<color=yellow> <b>[Projectile Life expired] </b> [{TraveledTime}]. Destroy projectile.</color>", this);
                        StopRollingProjectile();
                        OnReachedMaxDistance.Invoke(false);

                        if (rollingProjectileDestroyOnLife == true) DestroyProjectile();
                        else this.Delay_Action(LifeImpact, DestroyProjectile);

                        yield break;
                    }
                }

                // Projectile stopped
                if (rb.linearVelocity.sqrMagnitude < 0.00001f && TraveledDistance > 1)
                {
                    Debugging($"<color=yellow> <b>[Projectile has stopped at </b> [{TraveledDistance}]. Stop projectile and destroy after LifeImpact. </color>", this);
                    StopRollingProjectile();
                    OnReachedMaxDistance.Invoke(false);
                    this.Delay_Action(LifeImpact, DestroyProjectile);
                    yield break;
                }

                if (rb.linearVelocity.sqrMagnitude > 0.001f && prev_velocity.sqrMagnitude > 0.001f)
                {
                    float currentDeflection = Vector3.Angle(Vector3.ProjectOnPlane(prev_velocity, Vector3.up), Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up));

                    // If deflection is larger than MaxDeflection
                    if (rollingProjectileMaxDeflectionAngle != 0 && currentDeflection > rollingProjectileMaxDeflectionAngle && currentDeflection < 180)
                    {
                        Debugging($"<color=yellow> <b>[Projectile reached Max Deflection {rollingProjectileMaxDeflectionAngle.Value}] at </b> [{currentDeflection}]. Stop projectile and destroy after LifeImpact. </color>", this);
                        StopRollingProjectile();
                        OnReachedMaxDistance.Invoke(false);
                        this.Delay_Action(LifeImpact, DestroyProjectile);
                        yield break;
                    }
                }

                if (rotation == ProjectileRotation.None) // If there should be no rotation
                {
                    T.rotation = initialRotation; // Set Projectile rotation to the initial one
                }

                if (rotation == ProjectileRotation.FollowTrajectory)
                {
                    T.rotation = Quaternion.LookRotation(rb.linearVelocity.normalized, T.up);
                    if (TrajectoryRoll != 0) T.Rotate(rb.linearVelocity.normalized, TrajectoryRoll * Time.fixedDeltaTime, Space.World);
                }

                // Keep applying the launch force (Impulse is a one-shot launch so it is skipped here)
                if (forceMode != ForceMode.Impulse)
                    rb.AddForce(Velocity, forceMode);

                if (rollingProjectileRollOnGroundOnly == true) // If the Projectile must stay on the ground
                {
                    // Cast below the Projectile
                    Physics.Raycast(T.position, Vector3.down, out raycastHit, 512, RollingProjectileGroundMask, QueryTriggerInteraction.Ignore);

                    if (raycastHit.collider != null && projectileDistanceFromGround != 0)
                    {
                        if (raycastHit.distance > projectileDistanceFromGround)
                        {
                            // Snap the Projectile (with its collider) to the ground surface
                            T.position = raycastHit.point + new Vector3(0, projectileDistanceFromGround, 0);
                        }

                        float groundAngle = Vector3.SignedAngle(Vector3.up, raycastHit.normal, T.right); // Calculate ground angle ([-] up slope, [+] down slope)

                        if (rollingProjectileMaxUpSlopeAngle != 0 && groundAngle < -rollingProjectileMaxUpSlopeAngle) // Check UpSlope angle
                        {
                            Debugging($"<color=yellow> <b>[Projectile on slope larger than] </b> [-{rollingProjectileMaxUpSlopeAngle.Value}]. Stop projectile and destroy after LifeImpact. </color>", this);
                            StopRollingProjectile();
                            OnReachedMaxDistance.Invoke(false);
                            this.Delay_Action(LifeImpact, DestroyProjectile);
                            yield break;
                        }

                        if (rollingProjectileMaxDownSlopeAngle != 0 && groundAngle > rollingProjectileMaxDownSlopeAngle) // Check DownSlope angle
                        {
                            Debugging($"<color=yellow> <b>[Projectile on slope larger than] </b> [{rollingProjectileMaxDownSlopeAngle.Value}]. Stop projectile and destroy after LifeImpact. </color>", this);
                            StopRollingProjectile();
                            OnReachedMaxDistance.Invoke(false);
                            this.Delay_Action(LifeImpact, DestroyProjectile);
                            yield break;
                        }
                    }
                }

                Prev_pos = T.position;
                prev_velocity = rb.linearVelocity;
                yield return waitForFixedUpdate;
            }

            Debugging($"<color=yellow> <b>[Rolling Projectile Impacted HasImpact==true.</b></color>", this);
            yield return waitForFixedUpdate;
        }

        //MWC: Magic Projectile - shared "stop the rolling projectile in place" used by RollingProjectile() end conditions
        private void StopRollingProjectile()
        {
            DisableRigidBody();
            if (rb) rb.constraints = RigidbodyConstraints.FreezeAll;
            ColliderEnable(false);
        }

        private void ColliderEnable(bool value)
        {
            if (m_collider)
            {
                m_collider.enabled = value;
                Debug.Log("m_collider.enabled = " + m_collider.enabled);
            }
        }




        //MWC: Magic Projectile - added defaulted 'hasImpacted'. true = real collision/trajectory hit (original behaviour). false = damage caused by a magic particle cast (MMagicParticleTriggers): apply damage/effects but keep the projectile travelling.
        public virtual void ProjectileImpact(Rigidbody targetRB, Collider collider, Vector3 hitPosition, Vector3 normal, bool hasImpacted = true)
        {
            if (!Active) return;

            Debugging($"<color=yellow><b>Projectile Impact</b>{collider.name} [hasImpacted: {hasImpacted}]</color>", this);  //Debug
            bool attackMissed = false; //CustomPatch: added flag to keep track of missed attack state in this method for the end result

            //CustomPatch: added try-finally block to call the hit events at the correct time after adjusting final projectile hit position
            Transform colliderTransform = null;
            try
            {
                this.HitPosition = hitPosition; //Store the Hit position of the Projectile

                //MWC: Magic Projectile - only a real impact latches HasImpacted and stops the movement coroutine. Particle damage leaves the projectile flying/rolling.
                if (hasImpacted)
                {
                    HasImpacted = true;
                    StopAllCoroutines();
                }

                if (MissAttack())
                {
                    attackMissed = true;
                    //MWC: Magic Projectile - a real impact that misses destroys the projectile (original behaviour); a particle miss just skips this particle's damage
                    if (hasImpacted)
                    {
                        Debugging("Destroy Projectile Missed", this);
                        Destroy(gameObject);
                    }
                    return;
                }

                //if there's no collider OR the projectile collider is a trigger
                //MWC: Magic Projectile - only freeze/disable the rigidbody on a real impact (not on particle damage, which must keep moving)
                if (hasImpacted && (!m_collider || m_collider.isTrigger))
                {
                    DisableRigidBody();
                    if (rb) rb.constraints = RigidbodyConstraints.FreezeAll;
                }

                TryInteract(collider.gameObject);

                var damagee = collider.GetComponentInParent<IMDamage>();                      //Get the Animal on the Other collider
                //Store the Last Collider that the animal hit
                if (damagee != null) { damagee.HitCollider = collider; }

                TryDamage(damagee, statModifier);

                // TryPhysics(targetRB, collider, Direction, Force);
                //Add a force to the Target RigidBody
                //CustomPatch: fix: removed ? from "targetRB" => avoid using ? operator on Unity objects
                if (targetRB != null)
                    targetRB.AddForceAtPosition(PushMultiplier * Velocity.magnitude * NormalizedDirection, hitPosition, forceMode); //CustomPatch: optimization: used cached NormalizedDirection

                //CustomPatch: cache collider transform to avoid repeated native engine calls below
                colliderTransform = collider.transform;

                var hasAnimator = collider.gameObject.GetComponentInParent<Animator>();
                var RootBone = colliderTransform;
                if (hasAnimator != null) { RootBone = hasAnimator.avatarRoot; }

                //CustomPatch: replaced with cached collider transform
                var ClosestTransform = colliderTransform; //If the collider is a MeshCollider then use the same transform (To avoid errors with the ClosestPoint

                //MWC: removed StickOnHitSurface gate so StickOnSurface always runs the bone-accurate closest-transform search
                if (!collider.isTrigger && collider is not MeshCollider && collider is not TerrainCollider && hasAnimator)
                {
                    ClosestTransform = MTools.GetClosestTransform(hitPosition, RootBone, Layer);

                    //Meaning it found a nearest transform
                    if (ClosestTransform != colliderTransform) //CustomPatch: use cached transform
                    {
                        var colTransform = ClosestTransform.GetComponent<Collider>();

                        if (colTransform != null && !colTransform.isTrigger && colTransform is not MeshCollider)
                        {
                            hitPosition = colTransform.ClosestPoint(hitPosition);
                        }
                        else
                        {
                            //find the closes point in the upper bone or the lower bone
                            var MainPos = ClosestTransform.position;

                            //find the parent bone
                            var parentPoint = ClosestTransform.parent != null ? ClosestTransform.parent.position : MainPos;

                            //find the child bone
                            var ChildPoint = ClosestTransform.childCount > 0 ? ClosestTransform.GetChild(0).position : MainPos;

                            var P1 = MTools.ClosestPointOnLine(hitPosition, ChildPoint, MainPos);
                            var P2 = MTools.ClosestPointOnLine(hitPosition, parentPoint, MainPos);

                            var Dist1 = Vector3.Distance(P1, MainPos);
                            var Dist2 = Vector3.Distance(P2, MainPos);

                            hitPosition = Dist1 < Dist2 ? P1 : P2;
                        }
                    }
                }

                TryHitEffectProjectile(hitPosition, normal, ClosestTransform, damagee);

                //MWC: Magic Projectile - impact-only behaviour (stick/destroy/activate-rb + life-impact). Particle damage skips all of this so the projectile keeps travelling and emitting.
                if (hasImpacted)
                {
                    switch (impactBehaviour)
                    {
                        case ImpactBehaviour.None:
                            transform.position = hitPosition;
                            MDebug.DrawWireSphere(hitPosition, Color.red, 0.1f, 2);
                            break;
                        case ImpactBehaviour.StickOnSurface:
                            Stick_On_Surface(ClosestTransform, hitPosition);
                            break;
                        case ImpactBehaviour.DestroyOnImpact:
                            Debugging("DestroyOnImpact", this);
                            Destroy(gameObject);
                            return;
                        case ImpactBehaviour.ActivateRigidBody:
                            EnableRigidBody();
                            Enable_Collider();
                            if (rb) rb.linearDamping = DragOnImpact;
                            Debugging("Activate Rigid Body", this);
                            break;
                        /// <summary> Pool - Added DisableOnImpact Option  </summary>
                        case ImpactBehaviour.DisableOnImpact:
                            Pool.Release(this.gameObject);
                            Debugging("DisableOnImpact [Return to the Pool]", this);
                            return;
                            /// <summary>  Pool - End  </summary>
                    }

                    //In case the projectile lives in the scene it needs to be destroyed after life impact has elapsed (Destroy or sent back to the pool)
                    if (LifeImpact > 0)
                    {
                        //Debug.Log("DO LIFE IMPACT WITH DEST");
                        this.Delay_Action(LifeImpact, DestroyProjectile); //MWC: replaced reflection-based Invoke with the project's coroutine-based Delay_Action
                    }
                    //MWC: Magic Projectile - Rolling stays Active after a main-collider impact so its already-emitted magic particles keep dealing damage; Flying disables as before
                    else if (projectileType == ProjectileType.FlyingProjectile)
                    {
                        Active = false; //Disable the projectile it has already impacted with something
                    }
                }
            }
            finally
            {
                //  moved hit events at the end of the execution of this method to make sure they're called with the latest up-to-date post-processed impact information (penetration adjustment, projectile re-parenting, hit position adjustments, etc)
                if (!attackMissed)
                {
                    //MWC: Magic Projectile - route the hit events by impact kind
                    if (hasImpacted)
                    {
                        //MWC: a Rolling Projectile main-collider hit on a Tagged target routes through OnHitTag instead of OnHit
                        if (rollingProjectileMainColliderHitWithTag)
                        {
                            OnHitTag.Invoke(colliderTransform);
                            rollingProjectileMainColliderHitWithTag = false;
                        }
                        else
                        {
                            if (Thrower != null) Thrower.OnHit.Invoke(colliderTransform);
                            OnHit.Invoke(colliderTransform);
                        }
                        rollingProjectileMainColliderHit = false;
                        OnHitPosition.Invoke(hitPosition);
                    }
                    else
                    {
                        //MWC: particle (non-impact) damage drives the "without impact" events for VFX hooks
                        OnHitPositionWithoutImpact.Invoke(hitPosition);
                        OnHitTransformWithoutImpact.Invoke(colliderTransform);
                    }
                }
            }
        }

        protected virtual void EnableRigidBody()
        {
            if (rb)
            {
                rb.useGravity = true;
                rb.isKinematic = false;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.constraints = RigidbodyConstraints.None;
            }
        }

        protected virtual void DisableRigidBody()
        {
            if (rb)
            {
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                rb.useGravity = false;
                rb.isKinematic = true;
            }
        }

        public void PrepareDamage(StatModifier modifier, float CriticalChance, float CriticalMultiplier, StatElement element)
        {
            if (!KeepValues)
            {
                statModifier = new StatModifier(modifier);
                this.CriticalChance = CriticalChance;
                this.CriticalMultiplier = CriticalMultiplier;
                this.element = element;
            }
        }

        protected virtual void Stick_On_Surface(Transform colliderTransform, Vector3 HitPosition) //CustomPatch: renamed the previous "collider" param to avoid confusion (good practices creates an easier to maintain code base ;) )
        {
            Debugging($"Stick on Surface [{colliderTransform.name}]", this);
            MDebug.DrawWireSphere(HitPosition, Color.red, 0.05f);


            LinkStickOnSurface.transform.parent = null; //remove the Parent from the link stick
            transform.SetParentScaleFixer(colliderTransform, HitPosition, LinkStickOnSurface);
            DisableRigidBody();

            T.position += Direction.normalized * Penetration; //Put the Projectile a bit deeper in the collider

            if (rb != null)
            {
                rb.position = T.position;
                rb.rotation = T.rotation;
            }
        }

        private GameObject LinkStickOnSurface;

        protected virtual void TryHitEffectProjectile(Vector3 HitPosition, Vector3 Normal, Transform hitTransform, IMDamage damagee)
        {

            var hitEffectGameObj = HitEffect;
            //  var hitSound = this.hitSound; Debug.Log($"hitSound {hitSound.Value.name}");

            //Find Hit Effects and Sounds
            if (damagee != null && hitEffects != null)
            {
                var eff = hitEffects.Get(damagee.Surface);

                if (eff != null)
                {
                    if (eff.effect.Value != null) hitEffectGameObj = eff.effect.Value;     //Use the Effect from the List

                    //if (eff.sound != null) hitSound = eff.sound;                    //use the sound form the list
                }
            }

            if (hitEffectGameObj != null)
            {
                var HitRotation = Quaternion.FromToRotation(Vector3.up, Normal);

                if (debug) MDebug.DrawWireSphere(HitPosition, Color.red, 0.05f, 1);

                Debugging($"<color=yellow> <b>[HitEffect] </b> [{hitEffectGameObj.name}] , {HitPosition} </color>", this);  //Debug

                if (hitEffectGameObj.IsPrefab())
                {
                    var instance = Instantiate(hitEffectGameObj, HitPosition, HitRotation);

                    var HasHlp = instance.transform.SetParentScaleFixer(hitTransform, HitPosition); //Fix the Scale issue


                    //Reset the gameobject visibility 
                    CheckHitEffect(instance);

                    if (DestroyHitEffect > 0f)
                    {
                        Destroy(instance, DestroyHitEffect);
                        if (HasHlp) Destroy(HasHlp.gameObject, DestroyHitEffect);
                    }
                }
                else
                {
                    hitEffectGameObj.transform.SetPositionAndRotation(HitPosition, HitRotation);
                    CheckHitEffect(hitEffectGameObj);
                }
                //CustomPatch: disabled redundant SetActive call. Already handled in above CheckHitEffect(HitEffect) calls + introduces a bug where if the HitEffect is an actual prefab this call would call activate directly on the prefab asset and modify its state
                //HitEffect.SetActive(true);
            }

            if (m_audio != null)
            {
                /// <summary> Pool - Added audio for DisableOnImpact  </summary>
                if (impactBehaviour == ImpactBehaviour.DestroyOnImpact || impactBehaviour == ImpactBehaviour.DisableOnImpact)
                /// <summary>  Pool - End  </summary>
                {
                    if (hitEffectGameObj)
                    {
                        if (HitEffect.TryGetComponent<AudioSource>(out var audio) && audio.isActiveAndEnabled)
                        {
                            audio.resource = hitSound.Value;
                            audio.spatialBlend = 1;
                            audio.Play();
                        }
                    }
                }
                else
                {
                    PlaySound(hitSound.Value);
                }
            }
        }

#if UNITY_EDITOR
        protected override void Reset()
        {
            base.Reset();
            rb = GetComponent<Rigidbody>();
            m_collider = GetComponentInChildren<Collider>();


            m_audio = GetComponent<AudioSource>(); //Gets the Weapon Source

            if (!m_audio) m_audio = gameObject.AddComponent<AudioSource>(); //Create an AudioSource if there's no Audio Source on the weapon

            m_audio.spatialBlend = 1;
            m_audio.maxDistance = 50;


            // Make the object and all the children to be in the Ignore Raycast Layer
            int ignoreLayer = LayerMask.NameToLayer("Item");
            if (ignoreLayer == -1) ignoreLayer = 2; // Fallback to Unity's default index for Ignore Raycast

            var transforms = GetComponentsInChildren<Transform>(true);
            foreach (var t in transforms)
            {
                if (t != null && t.gameObject != null)
                    t.gameObject.layer = ignoreLayer;
            }
        }

        protected void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow + Color.red;
            // Gizmos.DrawSphere(transform.position, Radius);
            Gizmos.DrawWireSphere(transform.position, Radius);
        }
#endif
    }



    /// ----------------------------------------
    /// EDITOR
    /// ----------------------------------------

    #region Inspector


#if UNITY_EDITOR
    [CustomEditor(typeof(MProjectile))]
    public class MProjectileEditor : MDamagerEd
    {
        private SerializedProperty gravity, Penetration, DragOnImpact, PushMultiplier, TrajectoryRollOffset
     , Editor_Tabs1, KeepDamageValues, Radius, m_AfterDistance, OnRayCastHit, Life, LifeImpact,
        OnFire, impactBehaviour, rotation, torque, torqueAxis, m_PosOffset, m_RotOffset, rb, m_collider, m_trail, TrajectoryRoll,
         projectileType, deactivateProjectile, rollingProjectileMaxDistance,
            rollingProjectileDestroyOnMaxDistance, rollingProjectileDestroyOnLife, m_rollingProjectileGroundMask,
            rollingProjectileRollOnGroundOnly, rollingProjectileDistanceFromAimOrigin,
            rollingProjectileMaxUpSlopeAngle, rollingProjectileMaxDownSlopeAngle, rollingProjectileMaxDeflectionAngle,
            OnHitTag, OnHitPositionWithoutImpact, OnHitTransformWithoutImpact, OnReachedMaxDistance;

        protected string[] Tabs1 = new string[] { "General", "Damage", "Physics", "Events" };
        private MProjectile M;

        //MWC: Magic Projectile - tooltips mirroring the original package editor
        private readonly string[] projectileTypeTooltip = new string[]
        {
             "Default Projectile used for bullets, arrows, etc.",
             "Projectile that is a rigidbody and uses physics to roll on the ground",
        };
        private readonly string[] deactivateProjectileTooltip = new string[]
        {
             "Deactivate Projectile when Life is reached",
             "Deactivate Projectile when MaxDistance is reached",
        };

        private readonly string[] rotationTooltip = new string[] {
             "No Rotation is applied to the projectile while flying",
             "The projectile will follow its trajectory while flying",
             "The projectile will inherit the rotation it had before it was fired",
             "The projectile will rotate randomly while flying",
             "The projectile will rotate around an axis (world relative)"};

        protected override void OnEnable()
        {
            FindBaseProperties();
            M = (MProjectile)target;

            gravity = serializedObject.FindProperty("gravity");

            OnFire = serializedObject.FindProperty("OnFire");
            Radius = serializedObject.FindProperty("Radius");


            Life = serializedObject.FindProperty("Life");
            LifeImpact = serializedObject.FindProperty("LifeImpact");
            impactBehaviour = serializedObject.FindProperty("impactBehaviour");
            rotation = serializedObject.FindProperty("rotation");

            Penetration = serializedObject.FindProperty("Penetration");
            DragOnImpact = serializedObject.FindProperty("DragOnImpact");
            PushMultiplier = serializedObject.FindProperty("PushMultiplier");

            m_PosOffset = serializedObject.FindProperty("m_PosOffset");
            m_RotOffset = serializedObject.FindProperty("m_RotOffset");
            KeepDamageValues = serializedObject.FindProperty("m_KeepDamageValues");
            m_AfterDistance = serializedObject.FindProperty("m_AfterDistance");

            torque = serializedObject.FindProperty("torque");
            TrajectoryRoll = serializedObject.FindProperty("TrajectoryRoll");
            TrajectoryRollOffset = serializedObject.FindProperty("TrajectoryRollOffset");
            torqueAxis = serializedObject.FindProperty("torqueAxis");
            //  InstantiateOnImpact = serializedObject.FindProperty("InstantiateOnImpact");
            Editor_Tabs1 = serializedObject.FindProperty("Editor_Tabs1");
            rb = serializedObject.FindProperty("rb");
            m_trail = serializedObject.FindProperty("m_trail");
            m_collider = serializedObject.FindProperty("m_collider");

            //  sphereCastZOffset = serializedObject.FindProperty("sphereCastZOffset"); //CustomPatch: added sphereCastZOffset property
            OnRayCastHit = serializedObject.FindProperty("OnRayCastHit");

            //MWC: Magic Projectile - Rolling Projectile fields + new events
            projectileType = serializedObject.FindProperty("projectileType");
            deactivateProjectile = serializedObject.FindProperty("deactivateProjectile");
            rollingProjectileMaxDistance = serializedObject.FindProperty("rollingProjectileMaxDistance");
            rollingProjectileDestroyOnMaxDistance = serializedObject.FindProperty("rollingProjectileDestroyOnMaxDistance");
            rollingProjectileDestroyOnLife = serializedObject.FindProperty("rollingProjectileDestroyOnLife");
            m_rollingProjectileGroundMask = serializedObject.FindProperty("m_rollingProjectileGroundMask");
            rollingProjectileRollOnGroundOnly = serializedObject.FindProperty("rollingProjectileRollOnGroundOnly");
            rollingProjectileDistanceFromAimOrigin = serializedObject.FindProperty("rollingProjectileDistanceFromAimOrigin");
            rollingProjectileMaxUpSlopeAngle = serializedObject.FindProperty("rollingProjectileMaxUpSlopeAngle");
            rollingProjectileMaxDownSlopeAngle = serializedObject.FindProperty("rollingProjectileMaxDownSlopeAngle");
            rollingProjectileMaxDeflectionAngle = serializedObject.FindProperty("rollingProjectileMaxDeflectionAngle");
            OnHitTag = serializedObject.FindProperty("OnHitTag");
            OnHitPositionWithoutImpact = serializedObject.FindProperty("OnHitPositionWithoutImpact");
            OnHitTransformWithoutImpact = serializedObject.FindProperty("OnHitTransformWithoutImpact");
            OnReachedMaxDistance = serializedObject.FindProperty("OnReachedMaxDistance");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawDescription("Logic for Projectiles. When is fired by a Thrower component, use the method Prepare() to transfer all the properties from the thrower");

            Editor_Tabs1.intValue = GUILayout.Toolbar(Editor_Tabs1.intValue, Tabs1);

            int Selection = Editor_Tabs1.intValue;
            if (Selection == 0) DrawGeneral();
            else if (Selection == 1) DrawDamage();
            else if (Selection == 2) DrawExtras();
            else if (Selection == 3) DrawEvents();
            // EditorGUILayout.PropertyField(debug);

            // EditorGUILayout.PropertyField(sphereCastZOffset);

            serializedObject.ApplyModifiedProperties();
        }

        protected void DrawExtras()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawPhysics(false);
                EditorGUILayout.PropertyField(gravity);
                EditorGUILayout.PropertyField(PushMultiplier);
                EditorGUILayout.PropertyField(m_AfterDistance);

                //MWC: Magic Projectile - Rolling Projectile section (Flying shows nothing extra)
                EditorGUILayout.PropertyField(projectileType, new GUIContent("Projectile Type", projectileTypeTooltip[projectileType.intValue]));

                if ((ProjectileType)projectileType.intValue == ProjectileType.RollingProjectile)
                {
                    EditorGUILayout.PropertyField(deactivateProjectile, new GUIContent("Deactivate Projectile", deactivateProjectileTooltip[deactivateProjectile.intValue]));

                    switch ((DeactivateProjectile)deactivateProjectile.intValue)
                    {
                        case DeactivateProjectile.Life:
                            EditorGUILayout.PropertyField(rollingProjectileDestroyOnLife);
                            break;
                        case DeactivateProjectile.MaxDistance:
                            EditorGUILayout.PropertyField(rollingProjectileMaxDistance);
                            EditorGUILayout.PropertyField(rollingProjectileDestroyOnMaxDistance);
                            break;
                    }

                    EditorGUILayout.PropertyField(m_rollingProjectileGroundMask, new GUIContent("Ground Mask"));
                    EditorGUILayout.PropertyField(rollingProjectileRollOnGroundOnly);
                    EditorGUILayout.PropertyField(rollingProjectileDistanceFromAimOrigin);
                    EditorGUILayout.PropertyField(rollingProjectileMaxUpSlopeAngle);
                    EditorGUILayout.PropertyField(rollingProjectileMaxDownSlopeAngle);
                    EditorGUILayout.PropertyField(rollingProjectileMaxDeflectionAngle);
                    EditorGUILayout.Space();
                    EditorGUILayout.PropertyField(OnHitTag);
                    EditorGUILayout.PropertyField(OnHitPositionWithoutImpact);
                    EditorGUILayout.PropertyField(OnHitTransformWithoutImpact);
                    EditorGUILayout.PropertyField(OnReachedMaxDistance);
                }
            }

            DrawMisc();
        }

        protected void DrawDamage()
        {
            EditorGUILayout.PropertyField(KeepDamageValues, new GUIContent("Keep Values"));
            if (!M.KeepValues)
            {
                EditorGUILayout.HelpBox("If the Projectile is thrown by a Throwable, the Stat will be set by the Throwable. [E.g. The Arrow will get the Damage from the bow]", MessageType.Info);
            }
            else
            {
                DrawStatModifier();
                DrawCriticalDamage();
            }

            DrawMisc();
        }

        protected override void DrawGeneral(bool drawbox = true)
        {
            base.DrawGeneral(drawbox);

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Life.isExpanded = MalbersEditor.Foldout(Life.isExpanded, "Projectile Life");

                if (Life.isExpanded)
                {
                    EditorGUILayout.PropertyField(Life);
                    EditorGUILayout.PropertyField(LifeImpact);
                    EditorGUILayout.PropertyField(Radius);
                }

                m_PosOffset.isExpanded = MalbersEditor.Foldout(m_PosOffset.isExpanded, "Offsets");

                if (m_PosOffset.isExpanded)
                {
                    EditorGUILayout.PropertyField(m_PosOffset, new GUIContent("Position"));
                    EditorGUILayout.PropertyField(m_RotOffset, new GUIContent("Rotation"));
                }

                rotation.isExpanded = MalbersEditor.Foldout(rotation.isExpanded, "Rotation Behaviour");

                if (rotation.isExpanded)
                {
                    EditorGUILayout.PropertyField(rotation, new GUIContent("Rotation", rotationTooltip[rotation.intValue]));

                    var rot = (ProjectileRotation)rotation.intValue;

                    switch (rot)
                    {
                        case ProjectileRotation.None:
                            break;
                        case ProjectileRotation.FollowTrajectory:
                            EditorGUILayout.PropertyField(TrajectoryRoll);
                            EditorGUILayout.PropertyField(TrajectoryRollOffset);
                            break;
                        case ProjectileRotation.Random:
                            EditorGUILayout.PropertyField(torque);
                            break;
                        case ProjectileRotation.Axis:
                            EditorGUILayout.PropertyField(torque);
                            EditorGUILayout.PropertyField(torqueAxis);
                            break;
                        default:
                            break;
                    }
                }

                impactBehaviour.isExpanded = MalbersEditor.Foldout(impactBehaviour.isExpanded, "On Impact");

                if (impactBehaviour.isExpanded)
                {
                    EditorGUILayout.PropertyField(impactBehaviour);
                    //MWC: removed StickOnHitSurface check
                    if (impactBehaviour.intValue == (int)ImpactBehaviour.StickOnSurface)
                        EditorGUILayout.PropertyField(Penetration);
                    if (impactBehaviour.intValue == (int)ImpactBehaviour.ActivateRigidBody) //CustomPatch: changed hard-coded value to enum 
                        EditorGUILayout.PropertyField(DragOnImpact);
                }

                rb.isExpanded = MalbersEditor.Foldout(rb.isExpanded, "References");

                if (rb.isExpanded)
                {
                    EditorGUILayout.PropertyField(rb, new GUIContent("Rigid Body"));
                    EditorGUILayout.PropertyField(m_collider, new GUIContent("Collider"));
                    EditorGUILayout.PropertyField(m_trail);
                }
            }

        }

        protected override void DrawCustomEvents()
        {
            EditorGUILayout.PropertyField(OnFire);
            EditorGUILayout.PropertyField(OnRayCastHit);
        }
    }
#endif

    #endregion
}