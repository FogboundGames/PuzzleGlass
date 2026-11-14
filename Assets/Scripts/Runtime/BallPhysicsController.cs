using System;
using UnityEngine;
using DG.Tweening;

namespace PuzzleGlass
{
    public enum BallState
    {
        InContainer,
        AttractingToExit,
        FlyingToMonster,
        Pooled
    }

    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public class BallPhysicsController : MonoBehaviour
    {
        public PuzzleColor colorType;
        public BallState state = BallState.InContainer;
        public Rigidbody rb;
        public SphereCollider sphereCollider;
        public MeshRenderer meshRenderer;
        public MeshFilter meshFilter;

        [Header("Physical Settings")]
        public float ballRadius = 0.12f;
        public float defaultMass = 0.32f;
        public float defaultDrag = 0.45f;
        public float defaultAngularDrag = 0.65f;
        [Tooltip("Top havuzu toplarının tok ve hızlı düşmesi için ek yerçekimi ivmesi")]
        public float extraGravity = 14.0f;
        [Tooltip("Sadece tünelleme/patlama koruması. Normal harekete karışmaz.")]
        public float maxSafetySpeed = 12.0f;

        [Header("Ordered Ball Pool Layer Info")]
        [Tooltip("0 = Bottom-most layer, 1 = Second layer from bottom, 2 = Third layer, etc.")]
        public int layerIndex = 0;
        public float homeWorldX = 0f;
        public bool hasHomeX = false;
        public float containerFloorWorldY = -10f;
        public float rowStepWorldY = 0.20f;

        private MonsterCharacter _targetMonster;
        private Action<BallPhysicsController> _onEatenCallback;
        private Vector3 _originalScale = Vector3.one;
        private Tweener _flightTween;
        private Sequence _scaleSeq;
        private float _attractionStartTime = -1f;

        public float AttractionTime => (_attractionStartTime > 0f) ? (Time.time - _attractionStartTime) : 0f;
        public MonsterCharacter TargetMonster => _targetMonster;

        private void Awake()
        {
            EnsureComponents();
        }

        public void EnsureComponents()
        {
            if (rb == null) rb = GetComponent<Rigidbody>();
            if (sphereCollider == null) sphereCollider = GetComponent<SphereCollider>();
            if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();
            if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();

            if (rb != null)
            {
                rb.mass = defaultMass;
                rb.linearDamping = defaultDrag;
                rb.angularDamping = defaultAngularDrag;
                rb.useGravity = true;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                // Lock strictly to the Z = -0.10f plane while allowing free X, Y movement and 3D rolling rotation!
                rb.constraints = RigidbodyConstraints.FreezePositionZ;
            }

            if (sphereCollider != null)
            {
                // 0.496f radius lets 11 balls sit wall-to-wall without horizontal jamming while rolling smoothly into pockets
                sphereCollider.radius = 0.496f;
            }
        }

        public void ConfigurePoolLayer(int layerIdx, float worldX, float floorWorldY, float stepWorldY)
        {
            layerIndex = layerIdx;
            homeWorldX = worldX;
            // Free 2D X-Y Newtonian ball-pit physics so balls roll naturally into gaps when lower balls are collected!
            hasHomeX = false;
            containerFloorWorldY = floorWorldY;
            rowStepWorldY = stepWorldY;
        }

        private void FixedUpdate()
        {
            if (state != BallState.InContainer || rb == null || rb.isKinematic) return;

            Vector3 pos = transform.position;

            // Extra downward gravity gives balls a crisp, tactile marble weight in the ball pool
            if (extraGravity > 0.01f)
            {
                rb.AddForce(Vector3.down * extraGravity, ForceMode.Acceleration);
            }

            // Gerçek fizik: hız artık elle kırpılmıyor ve sıfırlanmıyor (sekme, yuvarlanma, çökme serbest).
            // Sadece tünelleme/patlama koruması için yüksek bir üst sınır var; yatışma Unity'nin sleep sistemine bırakıldı.
            Vector3 vel = rb.linearVelocity;
            vel.z = 0f;
            if (vel.sqrMagnitude > maxSafetySpeed * maxSafetySpeed)
            {
                vel = vel.normalized * maxSafetySpeed;
            }
            rb.linearVelocity = vel;

            // Görsel yuvarlanma: yatay harekete göre dönüş (düşük sürtünmede kaymak yerine yuvarlanıyormuş gibi görünsün)
            float r = Mathf.Max(0.05f, ballRadius);
            Vector3 targetAngVel = new Vector3(0f, 0f, -vel.x / r);
            rb.angularVelocity = Vector3.Lerp(rb.angularVelocity, targetAngVel, 10f * Time.fixedDeltaTime);

            if (Mathf.Abs(pos.z - (-0.10f)) > 0.001f)
            {
                pos.z = -0.10f;
                transform.position = pos;
            }
        }

        /// <summary>
        /// Configures the ball with its visual sphere mesh, candy material, physics material, and color ID.
        /// </summary>
        public void SetupBall(
            PuzzleColor color,
            float radius,
            PhysicsMaterial physMaterial,
            Material ballMaterial,
            Mesh sphereMesh)
        {
            EnsureComponents();

            colorType = color;
            ballRadius = Mathf.Max(0.04f, radius);
            state = BallState.InContainer;
            _attractionStartTime = -1f;
            layerIndex = 0;
            hasHomeX = false;

            if (meshFilter != null && sphereMesh != null)
            {
                meshFilter.sharedMesh = sphereMesh;
            }

            if (meshRenderer != null && ballMaterial != null)
            {
                meshRenderer.sharedMaterial = ballMaterial;
                meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;
            }

            float diameter = ballRadius * 2.0f;
            _originalScale = new Vector3(diameter, diameter, diameter);
            transform.localScale = _originalScale;

            if (sphereCollider != null)
            {
                sphereCollider.radius = 0.496f;
                sphereCollider.sharedMaterial = physMaterial;
                sphereCollider.enabled = true;
                sphereCollider.isTrigger = false;
            }

            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.constraints = RigidbodyConstraints.FreezePositionZ;
            }
        }

        public void MakeKinematic()
        {
            if (rb == null) rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
            }
        }

        public void ReleaseToPhysics()
        {
            if (state != BallState.InContainer) return;
            if (rb == null) rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.constraints = RigidbodyConstraints.FreezePositionZ;
                rb.WakeUp();
            }
        }

        /// <summary>
        /// If the target monster is removed from its slot before the ball exits the container,
        /// returns the ball back to normal container physics so it drops back onto the pool floor.
        /// </summary>
        public void CancelAttractionAndReturnToContainer()
        {
            if (state != BallState.AttractingToExit) return;

            state = BallState.InContainer;
            _attractionStartTime = -1f;
            _targetMonster = null;

            Vector3 p = transform.position;
            p.z = -0.10f;
            transform.position = p;
            transform.localScale = _originalScale;

            if (sphereCollider != null)
            {
                sphereCollider.enabled = true;
                sphereCollider.isTrigger = false;
                if (BallContainerManager.Instance != null && BallContainerManager.Instance.bottomSpoutStopper != null)
                {
                    Physics.IgnoreCollision(sphereCollider, BallContainerManager.Instance.bottomSpoutStopper, false);
                }
            }

            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.constraints = RigidbodyConstraints.FreezePositionZ;
                rb.linearVelocity = new Vector3(0f, -1.2f, 0f);
                rb.WakeUp();
            }
        }

        /// <summary>
        /// Begins the smooth attraction flow toward the bottom exit.
        /// </summary>
        public void BeginAttraction(MonsterCharacter targetMonster, Vector3 exitWorldPos)
        {
            state = BallState.AttractingToExit;
            _attractionStartTime = Time.time;
            _targetMonster = targetMonster;

            Vector3 vacatedPos = transform.position;

            // Çıkan top trigger olur: en alttan dümdüz aşağı indiği için diğer topları itmez, ama camın hemen
            // altındaki kutunun collider'ına da takılmaz. (Katı yapınca kutuya takılıp ~1 sn gecikme oluyordu.)
            if (sphereCollider != null)
            {
                sphereCollider.isTrigger = true;
            }

            // Bring slightly forward in Z so it visibly pops in front of resting balls
            Vector3 p = transform.position;
            p.z = -0.16f;
            transform.position = p;

            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = false;
                rb.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY;

                Vector3 toExit = exitWorldPos - transform.position;
                toExit.z = 0f;
                if (toExit.sqrMagnitude > 0.001f)
                {
                    rb.linearVelocity = toExit.normalized * 2.6f;
                }
                rb.WakeUp();
            }

            // Wake up remaining balls in container and nudge upper neighbors so they cascade into the vacated gap!
            if (BallObjectPool.Instance != null)
            {
                var activeList = BallObjectPool.Instance.ActiveBalls;
                float wakeRangeX = ballRadius * 4.8f;
                for (int i = 0; i < activeList.Count; i++)
                {
                    var other = activeList[i];
                    if (other != null && other != this && other.state == BallState.InContainer && other.rb != null)
                    {
                        if (other.rb.isKinematic) continue;
                        other.rb.WakeUp();

                        Vector3 op = other.transform.position;
                        float dx = vacatedPos.x - op.x;
                        float dy = op.y - vacatedPos.y;
                        if (dy >= -ballRadius * 0.4f && dy <= ballRadius * 5.0f && Mathf.Abs(dx) <= wakeRangeX)
                        {
                            float inwardVx = Mathf.Clamp(dx * 1.8f, -0.55f, 0.55f);
                            other.rb.linearVelocity += new Vector3(inwardVx, -0.45f, 0f);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Applies controlled attraction physics toward the bottom exit while preserving 2D container containment.
        /// </summary>
        public void ApplyAttractionToExit(Vector3 exitWorldPos, float pullForce = 24f, float maxSpeed = 3.4f)
        {
            if (state == BallState.FlyingToMonster || state == BallState.Pooled || rb == null) return;

            if (state != BallState.AttractingToExit)
            {
                state = BallState.AttractingToExit;
                _attractionStartTime = Time.time;
            }

            if (_attractionStartTime <= 0f)
            {
                _attractionStartTime = Time.time;
            }

            if (sphereCollider != null && !sphereCollider.isTrigger)
            {
                sphereCollider.isTrigger = true;
            }

            Vector3 myPos = transform.position;
            Vector3 toExit = exitWorldPos - myPos;
            toExit.z = 0f;

            float dist = toExit.magnitude;
            if (dist > 0.001f)
            {
                Vector3 dir = toExit / dist;
                Vector3 targetVel = dir * maxSpeed;

                rb.linearVelocity = Vector3.MoveTowards(rb.linearVelocity, targetVel, pullForce * Time.fixedDeltaTime);
            }
        }

        /// <summary>
        /// Smooth ballistic flight from the container exit directly into the target carrier box's gray socket recess!
        /// </summary>
        public void LaunchToMonsterMouth(
            Vector3 spoutExitPos,
            MonsterCharacter targetMonster,
            Action<BallPhysicsController> onEatenCallback)
        {
            if (state == BallState.FlyingToMonster || state == BallState.Pooled) return;

            state = BallState.FlyingToMonster;
            _targetMonster = targetMonster;
            _onEatenCallback = onEatenCallback;

            StopAndMakeKinematic();

            if (sphereCollider != null)
            {
                sphereCollider.enabled = false;
                sphereCollider.isTrigger = false;
            }

            transform.DOKill();

            Vector3 startPos = transform.position;
            startPos.z = -0.22f;
            transform.position = startPos;

            int socketIndex = (targetMonster != null) ? targetMonster.ClaimNextSocketIndex() : 0;
            Vector3 socketPos = (targetMonster != null)
                ? targetMonster.GetSocketWorldPosition(socketIndex)
                : spoutExitPos;
            Vector3 settledScale = (targetMonster != null)
                ? targetMonster.GetSocketBallWorldScale()
                : _originalScale * 0.58f;

            if (targetMonster != null)
            {
                targetMonster.OnBallIncoming(this);
            }

            float duration = Mathf.Clamp(Vector3.Distance(startPos, socketPos) * 0.19f, 0.26f, 0.40f);
            if (PuzzleGlassGameManager.Instance != null && PuzzleGlassGameManager.Instance.IsFeverActive)
            {
                duration *= 0.52f; // 2x Vacuum speed in Fever Mode!
            }

            // Tactile pop-scale + parabolic jump arc directly into the gray circular socket (without shrinking to zero!)
            Sequence flightSeq = DOTween.Sequence();
            flightSeq.Join(transform.DOJump(socketPos, jumpPower: 0.32f, numJumps: 1, duration: duration).SetEase(Ease.Linear));
            flightSeq.Join(transform.DORotate(new Vector3(180f, 0f, 90f), duration, RotateMode.FastBeyond360));
            flightSeq.Insert(0f, transform.DOScale(_originalScale * 1.10f, duration * 0.35f).SetEase(Ease.OutQuad));
            flightSeq.Insert(duration * 0.35f, transform.DOScale(settledScale, duration * 0.65f).SetEase(Ease.InOutQuad));

            flightSeq.OnComplete(() =>
            {
                if (_targetMonster != null)
                {
                    _targetMonster.FeedBallIntoSocket(this, socketIndex);
                }
                _onEatenCallback?.Invoke(this);
                RecycleSelf();
            });
        }

        public void RecycleSelf()
        {
            transform.DOKill();
            state = BallState.Pooled;
            StopAndMakeKinematic();
            if (sphereCollider != null)
            {
                sphereCollider.enabled = false;
                sphereCollider.isTrigger = false;
            }

            gameObject.SetActive(false);
            if (BallObjectPool.Instance != null)
            {
                BallObjectPool.Instance.ReturnBall(this);
            }
        }

        private void StopAndMakeKinematic()
        {
            if (rb == null || rb.isKinematic) return;

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        private void OnDisable()
        {
            transform.DOKill();
        }

        private void OnDestroy()
        {
            transform.DOKill();
        }
    }
}
