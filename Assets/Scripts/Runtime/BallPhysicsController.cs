using System;
using UnityEngine;
using DG.Tweening;

namespace PuzzleGlass
{
    public enum BallState
    {
        InContainer,
        InFeedTube,
        AttractingToExit,
        FlyingToMonster,
        Pooled,
        FlyingIntoContainer
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
        public Transform visualTransform;

        [Header("Physical Settings")]
        public float ballRadius = 0.052f;
        public float defaultMass = 0.75f;
        public float defaultDrag = 0.20f;
        public float defaultAngularDrag = 0.42f;
        [Tooltip("Top havuzu toplarının akıcı ve doyurucu düşmesi için ek yerçekimi ivmesi")]
        public float extraGravity = 9.5f;
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
        private Vector3 _visualBaseScale = Vector3.one;
        private Tweener _flightTween;
        private Sequence _scaleSeq;
        private Sequence _visualSeq;
        private float _attractionStartTime = -1f;
        private float _spawnTime = -10f;
        private float _nextCollisionSquashTime = -10f;
        private bool _hasPlayedArrivalSquash = false;
        private TrailRenderer _flightTrail;

        // Cached per-instance MPB visuals (Pillar 1 + Pillar 2: 0 DOTween sequences on balls, 1 instanced draw call)
        private Color _mpbBaseColor = Color.white;
        private Color _mpbShadowColor = Color.gray;
        private Color _mpbHighlightColor = Color.white;
        private float _mpbSmoothness = 0.52f;
        private float _wobblePhase = 0f;
        private float _wobbleAmount = 0f;
        private float _squashTimer = 0f;
        private float _squashDuration = 0f;
        private float _squashAmp = 0f;
        private Vector3 _squashAxisLocal = Vector3.up;
        private bool _isGpuSquashing = false;
        private bool _isTrackMode = false;
        // Pist kuyruğu durumu (TrackCrowdQueue sürer): top kinematiktir, konumunu kuyruk belirler
        [System.NonSerialized] public bool trackQueueDriven;
        [System.NonSerialized] public bool trackQueuePosValid;
        [System.NonSerialized] public Vector2 trackQueueLocalPos;
        [System.NonSerialized] public float trackQueueLocalZ;
        [System.NonSerialized] public float trackQueueSpeed;
        /// <summary>Boşluğun etrafında dönen banttaki yuva indeksi (-1: bantta değil, kuyrukta bekliyor).</summary>
        [System.NonSerialized] public int trackLoopSlot = -1;
        /// <summary>Pist fiziği: topun cam yerel düzlemdeki hızı (yay takibi + çarpışmalar).</summary>
        [System.NonSerialized] public Vector2 trackVel;
        public bool IsGpuSquashing => _isGpuSquashing;
        public bool IsTrackMode => _isTrackMode;

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

            if (visualTransform == null)
            {
                Transform childVisual = transform.Find("Visual");
                if (childVisual != null)
                {
                    visualTransform = childVisual;
                }
            }

            if (meshRenderer == null)
            {
                meshRenderer = (visualTransform != null ? visualTransform.GetComponent<MeshRenderer>() : null)
                    ?? GetComponentInChildren<MeshRenderer>();
            }
            if (meshFilter == null)
            {
                meshFilter = (visualTransform != null ? visualTransform.GetComponent<MeshFilter>() : null)
                    ?? GetComponentInChildren<MeshFilter>();
            }
            if (visualTransform == null)
            {
                visualTransform = (meshRenderer != null) ? meshRenderer.transform : transform;
            }

            if (rb != null)
            {
                if (defaultDrag >= 0.39f) defaultDrag = 0.20f;
                if (defaultAngularDrag >= 0.60f) defaultAngularDrag = 0.42f;
                if (defaultMass < 0.70f) defaultMass = 0.75f;
                if (Mathf.Approximately(extraGravity, 14f)) extraGravity = 9.5f;

                rb.mass = defaultMass;
                rb.linearDamping = 1.05f;
                rb.angularDamping = 1.15f;
                rb.useGravity = false;
                rb.sleepThreshold = 0.0f;
                rb.maxDepenetrationVelocity = 2.4f;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                // Lock strictly to the Z = -0.10f plane while allowing free X, Y movement and 3D rolling rotation!
                rb.constraints = RigidbodyConstraints.FreezePositionZ;
            }

            if (sphereCollider != null)
            {
                // 0.478f collider yarıçapı topların gerçek yüzey temasıyla çarpışıp doğal yön değiştirmesini,
                // daralan alanlarda birbirini sıkıştırarak boşluklara yerleşmesini sağlar.
                sphereCollider.radius = 0.478f;
            }

            EnsureFlightTrail();
        }

        private void EnsureFlightTrail()
        {
            if (!Application.isPlaying) return;
            if (_flightTrail != null) return;
            _flightTrail = GetComponent<TrailRenderer>();
            if (_flightTrail == null)
            {
                _flightTrail = gameObject.AddComponent<TrailRenderer>();
            }
            _flightTrail.time = 0.13f;
            _flightTrail.minVertexDistance = 0.025f;
            _flightTrail.numCapVertices = 3;
            _flightTrail.numCornerVertices = 2;
            _flightTrail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _flightTrail.receiveShadows = false;
            _flightTrail.alignment = LineAlignment.TransformZ;
            _flightTrail.sortingOrder = 18;
            if (BlockClearCelebration.SpriteMaterial != null)
            {
                _flightTrail.sharedMaterial = BlockClearCelebration.SpriteMaterial;
            }
            _flightTrail.widthCurve = new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(0.55f, 0.42f),
                new Keyframe(1f, 0f));
            _flightTrail.emitting = false;
            // Uçmayan topta iz renderer'ı tamamen kapalı: camdaki 100+ topun boş izi her kare güncellenmesin
            _flightTrail.enabled = false;
        }

        private void SetFlightTrailActive(bool active)
        {
            if (!Application.isPlaying) return;
            EnsureFlightTrail();
            if (_flightTrail == null) return;

            if (!active)
            {
                _flightTrail.emitting = false;
                _flightTrail.Clear();
                _flightTrail.enabled = false;
                return;
            }
            _flightTrail.enabled = true;

            if (BlockClearCelebration.SpriteMaterial != null && _flightTrail.sharedMaterial == null)
            {
                _flightTrail.sharedMaterial = BlockClearCelebration.SpriteMaterial;
            }
            float headW = Mathf.Max(0.05f, ballRadius * 1.18f);
            _flightTrail.widthMultiplier = headW;
            _flightTrail.time = (PuzzleGlassGameManager.Instance != null && PuzzleGlassGameManager.Instance.IsFeverActive) ? 0.10f : 0.135f;

            Color c = Color.Lerp(PuzzleColorPalette.GetPrimaryColor(colorType), Color.white, 0.45f);
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(c, 0f), new GradientColorKey(Color.Lerp(c, Color.white, 0.35f), 1f) },
                new[] { new GradientAlphaKey(0.46f, 0f), new GradientAlphaKey(0.16f, 0.55f), new GradientAlphaKey(0f, 1f) });
            _flightTrail.colorGradient = grad;
            _flightTrail.Clear();
            _flightTrail.emitting = true;
        }

        public void ConfigureForTrackMode(bool isTrack)
        {
            _isTrackMode = isTrack;
            if (sphereCollider != null)
            {
                sphereCollider.radius = 0.478f;
            }
            if (rb != null)
            {
                rb.useGravity = false;
                rb.linearDamping = 0.88f;
                rb.angularDamping = 1.05f;
                rb.sleepThreshold = 0.0f;
                if (!rb.isKinematic) rb.WakeUp();
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

        /// <summary>
        /// Centralized physics step driven by BallCollectionController (replaces 140+ per-ball FixedUpdate() calls).
        /// Skips sleeping balls and avoids waking settled rigidbodies unnecessarily.
        /// </summary>
        public void StepBallPhysics(float fixedDt)
        {
            if (state != BallState.InContainer || rb == null || rb.isKinematic) return;
            // Pist topları TrackCrowdQueue'nun sürüşüyle ve PhysX ile hareket eder; havuz salınımı/yerçekimi uygulanmaz
            // (ayrıca uyuyan topu burada uyandırmayız ki dengeye gelen top dinlenebilsin).
            if (_isTrackMode && trackQueueDriven) return;
            if (rb.IsSleeping()) rb.WakeUp();

            Vector3 pos = rb.position;
            Vector3 vel = rb.linearVelocity;
            float speedSq = vel.x * vel.x + vel.y * vel.y;

            // 1. Z-plane & floor containment (keep balls strictly on 2D board plane and above container floor)
            if (Mathf.Abs(pos.z - (-0.10f)) > 0.015f)
            {
                pos.z = -0.10f;
                rb.position = pos;
            }
            if (containerFloorWorldY > -9f)
            {
                float minWorldY = containerFloorWorldY + ballRadius * 0.72f;
                if (pos.y < minWorldY)
                {
                    pos.y = minWorldY;
                    rb.position = pos;
                    if (vel.y < 0f)
                    {
                        vel.y = 0f;
                        rb.linearVelocity = vel;
                    }
                }
            }
            if (Mathf.Abs(vel.z) > 0.0001f)
            {
                vel.z = 0f;
                rb.linearVelocity = vel;
            }

            // 2. Hafif yatay mikro salınım (sıkışan topların birbirinin yanından kayıp boşluklara yerleşmesini destekler;
            //    yukarı yönlü salınım yok ki toplar birbirinden bağımsız havaya zıplamasın)
            float t = Time.time;
            float p1 = _wobblePhase;
            float p2 = _wobblePhase * 1.73f;
            float ax = Mathf.Sin(t * 4.6f + p1) + 0.65f * Mathf.Cos(t * 7.4f - p2);
            Vector2 agitateAccel = new Vector2(ax * 0.45f, 0f);

            // 3. Tutarlı aşağı (-Y) yerçekimi ve ağırlık/momentum akışı
            {
                float downSpeed = Mathf.Max(0f, -vel.y);
                float slideBoost = Mathf.Clamp01((downSpeed - 0.08f) / 0.45f);
                float downAccel = Mathf.Lerp(12.0f, 17.0f, slideBoost);
                Vector2 totalAccel = Vector2.down * downAccel + agitateAccel;
                rb.AddForce(new Vector3(totalAccel.x, totalAccel.y, 0f), ForceMode.Acceleration);
            }

            // 4. Yumuşak hız sınırı (momentum ve kontrollü sekmeyi kesmeyecek doğal tavan)
            const float maxContainerSpeed = 2.65f;
            if (speedSq > maxContainerSpeed * maxContainerSpeed)
            {
                float invSpd = maxContainerSpeed / Mathf.Sqrt(speedSq);
                vel.x *= invSpd;
                vel.y *= invSpd;
                rb.linearVelocity = vel;
                speedSq = maxContainerSpeed * maxContainerSpeed;
            }

            // 5. Sürekli 3D yuvarlanma ve canlı dönüş (hiçbir zaman tamamen donmaz)
            float r = Mathf.Max(0.025f, ballRadius);
            float spinMul = (PuzzleGlassGameManager.Instance != null && PuzzleGlassGameManager.Instance.IsEndgameFastForward) ? 2.0f : 1.0f;
            Vector3 continuousSpin = new Vector3(
                Mathf.Cos(t * 3.4f * spinMul + p1) * 1.4f,
                Mathf.Sin(t * 3.9f * spinMul + p2) * 1.4f,
                Mathf.Sin(t * 2.8f * spinMul + p1) * 0.7f) * spinMul;
            Vector3 targetAngVel = new Vector3(-vel.y / r, vel.x / r, 0f) * 0.85f + continuousSpin;
            rb.angularVelocity = Vector3.Lerp(rb.angularVelocity, targetAngVel, 12f * fixedDt);
        }

        public void StepGpuVisualSquash(float dt)
        {
            if (!_isGpuSquashing || meshRenderer == null) return;

            _squashTimer += dt;
            float duration = Mathf.Max(0.05f, _squashDuration);
            float t = _squashTimer / duration;
            if (t >= 1f)
            {
                _isGpuSquashing = false;
                FlushPropertyBlock(0f);
                return;
            }

            // Smooth damped squash -> rebound -> settle curve on GPU
            float amount;
            if (t < 0.25f)
            {
                amount = Mathf.Lerp(0f, -_squashAmp, t / 0.25f);
            }
            else if (t < 0.60f)
            {
                float u = (t - 0.25f) / 0.35f;
                amount = Mathf.Lerp(-_squashAmp, _squashAmp * 0.34f, u);
            }
            else
            {
                float u = (t - 0.60f) / 0.40f;
                amount = Mathf.Lerp(_squashAmp * 0.34f, 0f, u);
            }

            FlushPropertyBlock(amount);
        }

        private void FlushPropertyBlock(float squashAmount)
        {
            BallObjectPool.ApplyBallPropertyBlock(
                meshRenderer,
                _mpbBaseColor,
                _mpbShadowColor,
                _mpbHighlightColor,
                _mpbSmoothness,
                _wobblePhase,
                _wobbleAmount,
                squashAmount,
                _squashAxisLocal);
        }

        /// <summary>
        /// Configures the ball with its visual sphere mesh, candy material, physics material, and color ID.
        /// </summary>
        public void SetupBall(
            PuzzleColor color,
            float radius,
            PhysicsMaterial physMaterial,
            Material ballMaterial,
            Mesh sphereMesh,
            int variantIndex = 1)
        {
            EnsureComponents();

            colorType = color;
            ballRadius = Mathf.Max(0.025f, radius);
            state = BallState.InContainer;
            _attractionStartTime = -1f;
            _isTrackMode = false;
            ResetTrackQueueState(false);
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

            BallObjectPool.ComputeBallVariantVisuals(
                color,
                variantIndex,
                out _mpbBaseColor,
                out _mpbShadowColor,
                out _mpbHighlightColor,
                out _mpbSmoothness);
            _wobblePhase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            _wobbleAmount = 0.55f;

            float diameter = ballRadius * 2.0f;
            _originalScale = new Vector3(diameter, diameter, diameter);
            transform.localScale = _originalScale;

            _visualSeq?.Kill();
            _visualSeq = null;
            _spawnTime = Time.time;
            _nextCollisionSquashTime = Time.time + 0.08f;
            _hasPlayedArrivalSquash = false;

            if (visualTransform != null && visualTransform != transform)
            {
                // Subtle tactile size variety (±4%) decoupled from collider bounds so rows never wedge
                float sizeJitter = UnityEngine.Random.Range(0.958f, 1.038f);
                _visualBaseScale = Vector3.one * sizeJitter;
                visualTransform.localScale = _visualBaseScale;
            }
            else
            {
                _visualBaseScale = Vector3.one;
            }

            if (Application.isPlaying)
            {
                // Lightweight GPU pop-in without allocating DOTween sequences
                _squashAxisLocal = Vector3.up;
                _squashAmp = 0.14f;
                _squashDuration = 0.17f;
                _squashTimer = 0f;
                _isGpuSquashing = true;
                BallCollectionController.Instance?.RegisterSquashingBall(this);
                FlushPropertyBlock(-0.12f);
            }
            else
            {
                _isGpuSquashing = false;
                FlushPropertyBlock(0f);
            }

            if (sphereCollider != null)
            {
                sphereCollider.radius = 0.478f;
                sphereCollider.sharedMaterial = physMaterial;
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
                rb.useGravity = false;
                rb.linearDamping = 0.88f;
                rb.angularDamping = 1.05f;
                rb.sleepThreshold = 0.0f;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.constraints = RigidbodyConstraints.FreezePositionZ;
            }

            SetFlightTrailActive(false);
        }

        public void MakeKinematic()
        {
            if (rb == null) rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
            }
            ResetTrackQueueState(false);
        }

        private void ResetTrackQueueState(bool driven)
        {
            trackQueueDriven = driven;
            trackQueuePosValid = false;
            trackQueueSpeed = 0f;
            trackLoopSlot = -1;
            trackVel = Vector2.zero;
        }

        /// <summary>Pist modunda topu kinematik yapıp kuyruğa (TrackCrowdQueue) teslim eder.</summary>
        private void JoinTrackQueue()
        {
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = !TrackCrowdQueue.UseRigidbodyPhysics;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
            }
            ResetTrackQueueState(true);
            // Gerçek fizik modunda pist topu dinamik Rigidbody'dir (çarpışma, yuvarlanma, momentum, uyku PhysX'te)
            if (TrackCrowdQueue.UseRigidbodyPhysics) TrackCrowdQueue.ConfigureDynamicTrackBall(this);
        }

        public void ReleaseToPhysics()
        {
            if (state != BallState.InContainer) return;
            if (rb == null) rb = GetComponent<Rigidbody>();
            if (_isTrackMode)
            {
                JoinTrackQueue();
                return;
            }
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = false;
                rb.linearDamping = 0.88f;
                rb.angularDamping = 1.05f;
                rb.sleepThreshold = 0.0f;
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

            SetFlightTrailActive(false);

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

            if (_isTrackMode)
            {
                JoinTrackQueue();
            }
            else if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = false;
                rb.linearDamping = 0.88f;
                rb.angularDamping = 1.05f;
                rb.sleepThreshold = 0.0f;
                rb.constraints = RigidbodyConstraints.FreezePositionZ;
                rb.linearVelocity = new Vector3(0f, -1.2f, 0f);
                rb.WakeUp();
            }

            BallCollectionController.Instance?.MarkBallFieldDirty();
        }

        public void SetAttractionTarget(MonsterCharacter targetMonster)
        {
            _targetMonster = targetMonster;
        }

        /// <summary>
        /// Begins the smooth attraction flow toward the bottom exit.
        /// </summary>
        public void BeginAttraction(MonsterCharacter targetMonster, Vector3 exitWorldPos)
        {
            state = BallState.AttractingToExit;
            _attractionStartTime = Time.time;
            _targetMonster = targetMonster;
            ResetTrackQueueState(false);

            BallCollectionController.Instance?.MarkBallFieldDirty();

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
                    rb.linearVelocity = toExit.normalized * 1.55f;
                }
                rb.WakeUp();
            }

            SetFlightTrailActive(true);

            // Wake up remaining balls in container and nudge upper neighbors so they cascade into the vacated gap!
            if (BallObjectPool.Instance != null)
            {
                var activeList = BallObjectPool.Instance.ActiveBalls;
                bool flatPool = BallContainerManager.Instance != null
                    && BallContainerManager.Instance.glassContainer != null
                    && BallContainerManager.Instance.glassContainer.IsFlatBottomBox;
                float wakeRangeX = ballRadius * (flatPool ? 3.0f : 4.8f);
                float nudgeCap = flatPool ? 0.18f : 0.45f;
                float nudgeDown = flatPool ? -0.12f : -0.24f;
                for (int i = 0; i < activeList.Count; i++)
                {
                    var other = activeList[i];
                    if (other != null && other != this && other.state == BallState.InContainer && other.rb != null)
                    {
                        Vector3 op = other.transform.position;
                        float dx = vacatedPos.x - op.x;
                        float dy = op.y - vacatedPos.y;

                        if (_isTrackMode)
                        {
                            // Pist modunda komşu toplara ani hız darbesi vurma ki mevcut diziliş/katmanlar sarsılıp karışmasın;
                            // sadece yakın komşuyu uyandır, doğal yerçekimi ve bant akışı boşluğu sakince doldursun.
                            float distSq = dx * dx + dy * dy;
                            float maxWakeDist = ballRadius * 3.6f;
                            if (dy >= -ballRadius * 0.25f && distSq <= maxWakeDist * maxWakeDist && distSq > 1e-5f)
                            {
                                if (!other.rb.isKinematic) other.rb.WakeUp();
                                float dist = Mathf.Sqrt(distSq);
                                if (dist <= ballRadius * 2.8f && Time.time >= other._nextCollisionSquashTime)
                                {
                                    other._nextCollisionSquashTime = Time.time + 0.14f;
                                    Vector2 toGap = new Vector2(dx / dist, Mathf.Min(0f, -dy / dist));
                                    other.PlayVisualSquashStretch(0.032f, new Vector3(toGap.x, toGap.y, 0f), isArrival: false);
                                }
                            }
                            continue;
                        }

                        if (other.rb.isKinematic) continue;
                        other.rb.WakeUp();

                        if (dy >= -ballRadius * 0.4f && dy <= ballRadius * 5.2f && Mathf.Abs(dx) <= wakeRangeX)
                        {
                            float inwardVx = Mathf.Clamp(dx * 1.45f, -nudgeCap, nudgeCap);
                            other.rb.linearVelocity += new Vector3(inwardVx, nudgeDown, 0f);

                            if (dy <= ballRadius * 3.0f && Mathf.Abs(dx) <= ballRadius * 2.6f && Time.time >= other._nextCollisionSquashTime)
                            {
                                other._nextCollisionSquashTime = Time.time + 0.14f;
                                Vector3 slideDir = new Vector3(inwardVx, -1f, 0f).normalized;
                                other.PlayVisualSquashStretch(0.036f, slideDir, isArrival: false);
                            }
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
            if (state == BallState.FlyingToMonster || state == BallState.FlyingIntoContainer || state == BallState.Pooled || rb == null) return;

            if (state != BallState.AttractingToExit)
            {
                state = BallState.AttractingToExit;
                _attractionStartTime = Time.time;

                if (visualTransform != null && visualTransform != transform && Application.isPlaying)
                {
                    Vector3 pullDir = exitWorldPos - transform.position;
                    pullDir.z = 0f;
                    if (pullDir.sqrMagnitude > 0.0001f)
                    {
                        PlayVisualSquashStretch(-0.075f, pullDir.normalized, isArrival: false);
                    }
                }
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
            if (state == BallState.FlyingToMonster || state == BallState.FlyingIntoContainer || state == BallState.Pooled) return;

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

            float duration = Mathf.Clamp(Vector3.Distance(startPos, socketPos) * 0.28f, 0.40f, 0.62f);
            if (PuzzleGlassGameManager.Instance != null && PuzzleGlassGameManager.Instance.IsFeverActive)
            {
                duration *= 0.60f;
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

        /// <summary>
        /// Topu seçili slota uzanan cam çıkış kanalının içinden kaydırarak kanal ağzından doğrudan blok yuvasına bırakır.
        /// </summary>
        public void LaunchThroughChannelToMonsterMouth(
            ShapedGlassContainer container,
            int channelIndex,
            MonsterCharacter targetMonster,
            Action<BallPhysicsController> onEatenCallback)
        {
            if (state == BallState.FlyingToMonster || state == BallState.FlyingIntoContainer || state == BallState.Pooled) return;

            Vector3[] channelPts = (container != null) ? container.GetChannelWorldPathPoints(channelIndex, reverse: false, steps: 7) : null;
            if (channelPts == null || channelPts.Length < 2)
            {
                LaunchToMonsterMouth(transform.position, targetMonster, onEatenCallback);
                return;
            }

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
            startPos.z = -0.16f;
            transform.position = startPos;

            int socketIndex = (targetMonster != null) ? targetMonster.ClaimNextSocketIndex() : 0;
            Vector3 mouthPos = channelPts[channelPts.Length - 1];
            Vector3 socketPos = (targetMonster != null)
                ? targetMonster.GetSocketWorldPosition(socketIndex)
                : mouthPos;
            Vector3 settledScale = (targetMonster != null)
                ? targetMonster.GetSocketBallWorldScale()
                : _originalScale * 0.58f;

            if (targetMonster != null)
            {
                targetMonster.OnBallIncoming(this);
            }

            // Kanal girişinden ağıza kadar olan yol + ağızdan bloğun yuvasına kısa iniş
            var pathPts = new Vector3[channelPts.Length];
            pathPts[0] = Vector3.Lerp(startPos, channelPts[0], 0.5f);
            float pathLen = Vector3.Distance(startPos, pathPts[0]);
            for (int k = 1; k < channelPts.Length; k++)
            {
                pathPts[k] = channelPts[k];
                pathLen += Vector3.Distance(pathPts[k - 1], pathPts[k]);
            }

            bool fast = PuzzleGlassGameManager.Instance != null &&
                        (PuzzleGlassGameManager.Instance.IsFeverActive || PuzzleGlassGameManager.Instance.IsEndgameFastForward);
            float channelDur = Mathf.Clamp(pathLen * 0.21f, 0.18f, 0.34f);
            float dropDur = Mathf.Clamp(Vector3.Distance(mouthPos, socketPos) * 0.24f, 0.12f, 0.22f);
            if (fast)
            {
                channelDur *= 0.60f;
                dropDur *= 0.60f;
            }

            Sequence flightSeq = DOTween.Sequence();
            flightSeq.Append(transform.DOPath(pathPts, channelDur, PathType.CatmullRom).SetEase(Ease.InQuad));
            flightSeq.Join(transform.DORotate(new Vector3(260f, 0f, 45f), channelDur, RotateMode.FastBeyond360));
            flightSeq.Join(transform.DOScale(_originalScale * 1.02f, channelDur).SetEase(Ease.OutQuad));

            Vector3 dropTarget = new Vector3(socketPos.x, socketPos.y, -0.22f);
            flightSeq.Append(transform.DOJump(dropTarget, jumpPower: 0.06f, numJumps: 1, duration: dropDur).SetEase(Ease.OutQuad));
            flightSeq.Join(transform.DORotate(new Vector3(120f, 0f, 45f), dropDur, RotateMode.FastBeyond360));
            flightSeq.Join(transform.DOScale(settledScale, dropDur).SetEase(Ease.InOutQuad));

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

        /// <summary>
        /// Dolu kutudan çıkan bir topu slot üzerindeki kutudan alt giriş ağzına getirip camın içine sokar.
        /// </summary>
        public void LaunchIntoContainer(
            Vector3 startWorldPos,
            Vector3 startWorldScale,
            ShapedGlassContainer container,
            Vector3 mouthWorldPos,
            Vector3 targetWorldPos,
            bool isTrackMode,
            Vector2 trackFlowDir,
            Vector3[] reverseChannelWaypoints = null)
        {
            if (state == BallState.Pooled) return;

            state = BallState.FlyingIntoContainer;
            _targetMonster = null;
            _onEatenCallback = null;

            ConfigureForTrackMode(isTrackMode);
            StopAndMakeKinematic();

            if (sphereCollider != null)
            {
                sphereCollider.enabled = false;
                sphereCollider.isTrigger = false;
            }

            transform.DOKill();
            transform.position = new Vector3(startWorldPos.x, startWorldPos.y, -0.22f);
            transform.localScale = startWorldScale;
            SetFlightTrailActive(true);

            Vector3 entryPos = new Vector3(mouthWorldPos.x, mouthWorldPos.y - 0.045f, -0.14f);
            Vector3 throatPos = new Vector3(mouthWorldPos.x, mouthWorldPos.y + 0.025f, -0.10f);
            Vector3 landPos = new Vector3(targetWorldPos.x, targetWorldPos.y, -0.10f);

            float leg1Dist = Vector3.Distance(transform.position, entryPos);
            float leg1Dur = Mathf.Clamp(leg1Dist * 0.20f, 0.14f, 0.26f);

            Vector3[] upPath;
            float leg2Dist;
            if (reverseChannelWaypoints != null && reverseChannelWaypoints.Length >= 2)
            {
                upPath = new Vector3[reverseChannelWaypoints.Length + 1];
                leg2Dist = Vector3.Distance(entryPos, reverseChannelWaypoints[0]);
                for (int i = 0; i < reverseChannelWaypoints.Length; i++)
                {
                    upPath[i] = reverseChannelWaypoints[i];
                    if (i > 0) leg2Dist += Vector3.Distance(reverseChannelWaypoints[i - 1], reverseChannelWaypoints[i]);
                }
                upPath[reverseChannelWaypoints.Length] = landPos;
                leg2Dist += Vector3.Distance(reverseChannelWaypoints[reverseChannelWaypoints.Length - 1], landPos);
            }
            else
            {
                upPath = new[] { throatPos, landPos };
                leg2Dist = Vector3.Distance(entryPos, landPos);
            }

            float leg2Dur = Mathf.Clamp(leg2Dist * 0.28f, 0.10f, 0.34f);
            if (PuzzleGlassGameManager.Instance != null && PuzzleGlassGameManager.Instance.IsFeverActive)
            {
                leg1Dur *= 0.65f;
                leg2Dur *= 0.65f;
            }

            // 1. Aşama: Kutudan alt giriş ağzının önüne uç
            // 2. Aşama: Doğrudan giriş boğazından / kanalından yukarı geçip camın içine yerleş
            Sequence flightSeq = DOTween.Sequence();
            flightSeq.Append(transform.DOJump(entryPos, jumpPower: 0.09f, numJumps: 1, duration: leg1Dur).SetEase(Ease.InOutQuad));
            flightSeq.Join(transform.DORotate(new Vector3(140f, 0f, 60f), leg1Dur, RotateMode.FastBeyond360));
            flightSeq.Join(transform.DOScale(_originalScale * 0.96f, leg1Dur).SetEase(Ease.OutQuad));
            flightSeq.Append(transform.DOPath(upPath, leg2Dur, PathType.CatmullRom).SetEase(Ease.OutQuad));
            flightSeq.Join(transform.DORotate(new Vector3(220f, 0f, 120f), leg2Dur, RotateMode.FastBeyond360));
            flightSeq.Join(transform.DOScale(_originalScale, leg2Dur).SetEase(Ease.OutQuad));
            flightSeq.SetLink(gameObject, LinkBehaviour.KillOnDisable);

            flightSeq.OnComplete(() =>
            {
                if (state != BallState.FlyingIntoContainer || !gameObject.activeInHierarchy) return;

                SetFlightTrailActive(false);
                state = BallState.InContainer;
                transform.position = landPos;
                transform.localScale = _originalScale;

                if (sphereCollider != null)
                {
                    sphereCollider.enabled = true;
                    sphereCollider.isTrigger = false;
                }

                ReleaseToPhysics();

                if (rb != null && !rb.isKinematic)
                {
                    if (isTrackMode)
                    {
                        Vector2 flow2 = trackFlowDir.sqrMagnitude > 0.001f ? trackFlowDir.normalized : Vector2.right;
                        Vector3 worldFlow = container != null
                            ? container.transform.TransformDirection(new Vector3(flow2.x, flow2.y, 0f))
                            : new Vector3(flow2.x, flow2.y, 0f);
                        rb.linearVelocity = worldFlow * TrackCrowdQueue.BeltSpeed;
                    }
                    else
                    {
                        rb.linearVelocity = new Vector3(UnityEngine.Random.Range(-0.12f, 0.12f), -0.35f, 0f);
                    }
                }

                BallCollectionController.Instance?.MarkBallFieldDirty();
            });
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (state != BallState.InContainer || !Application.isPlaying) return;
            if (meshRenderer == null) return;
            if (Time.time < _spawnTime + 0.045f) return;

            BallCollectionController.Instance?.MarkBallFieldDirty();

            float relSpeed = collision.relativeVelocity.magnitude;
            Vector3 normal = (collision.contactCount > 0) ? collision.GetContact(0).normal : Vector3.up;

            // Arrival squash & stretch on first settling impact inside the glass container
            if (!_hasPlayedArrivalSquash && relSpeed >= 0.65f)
            {
                _hasPlayedArrivalSquash = true;
                _nextCollisionSquashTime = Time.time + 0.09f;
                float arrivalAmp = Mathf.Clamp(0.068f + relSpeed * 0.018f, 0.078f, 0.155f);
                PlayVisualSquashStretch(arrivalAmp, normal, isArrival: true);
                PuzzleSoundManager.Instance?.PlayBallTap(Mathf.Clamp01((relSpeed - 0.50f) / 3.8f));
                return;
            }

            if (relSpeed < 0.52f || Time.time < _nextCollisionSquashTime) return;
            _nextCollisionSquashTime = Time.time + 0.075f;

            float amp = Mathf.Clamp(0.036f + (relSpeed - 0.52f) * 0.018f, 0.036f, 0.105f);
            PlayVisualSquashStretch(amp, normal, isArrival: false);

            PuzzleSoundManager.Instance?.PlayBallTap(Mathf.Clamp01((relSpeed - 0.45f) / 3.9f));
        }

        /// <summary>Pistte başka bir topa/duvara çarpınca kısa ezilme (sık tekrarlanmasın diye bekleme süreli).</summary>
        public void PlayTrackBump(Vector3 worldAxis, float amp)
        {
            if (!Application.isPlaying || Time.time < _nextCollisionSquashTime) return;
            _nextCollisionSquashTime = Time.time + 0.14f;
            PlayVisualSquashStretch(amp, worldAxis, isArrival: false);
        }

        private void PlayVisualSquashStretch(float amp, Vector3 worldAxis, bool isArrival)
        {
            if (meshRenderer == null || !Application.isPlaying) return;

            Vector3 localAxis = transform.InverseTransformDirection(worldAxis);
            if (localAxis.sqrMagnitude < 0.0001f) localAxis = Vector3.up;
            localAxis.Normalize();

            _squashAxisLocal = localAxis;
            _squashAmp = Mathf.Clamp(Mathf.Abs(amp) * 1.38f, 0.042f, 0.23f) * (amp < 0f ? -1f : 1f);
            _squashDuration = isArrival ? 0.24f : 0.19f;
            _squashTimer = 0f;
            _isGpuSquashing = true;

            BallCollectionController.Instance?.RegisterSquashingBall(this);
        }

        private void KillAllTweens()
        {
            transform.DOKill();
            _flightTween?.Kill();
            _flightTween = null;
            _scaleSeq?.Kill();
            _scaleSeq = null;
            _visualSeq?.Kill();
            _visualSeq = null;
            if (_isGpuSquashing)
            {
                _isGpuSquashing = false;
                FlushPropertyBlock(0f);
            }
            if (visualTransform != null && visualTransform != transform)
            {
                visualTransform.localScale = Vector3.one;
            }
        }

        public void RecycleSelf()
        {
            SetFlightTrailActive(false);
            KillAllTweens();
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
            KillAllTweens();
        }

        private void OnDestroy()
        {
            KillAllTweens();
        }
    }
}
