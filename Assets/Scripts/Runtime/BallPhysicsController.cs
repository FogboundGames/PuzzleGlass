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
        public float ballRadius = 0.135f;
        public float defaultMass = 0.25f;
        public float defaultDrag = 0.35f;
        public float defaultAngularDrag = 0.70f;

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
                rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                // Lock strictly to the Z = -0.10f plane so balls stay sealed in front of back glass and behind front glass
                rb.constraints = RigidbodyConstraints.FreezePositionZ;
            }

            if (sphereCollider != null)
            {
                // Primitive sphere mesh radius is 0.5f. Since transform.localScale = (diameter, diameter, diameter),
                // world collider radius is 0.5f * (2 * ballRadius) = ballRadius!
                sphereCollider.radius = 0.5f;
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
                sphereCollider.radius = 0.5f;
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
        /// Begins the smooth attraction flow toward the spout exit.
        /// </summary>
        public void BeginAttraction(MonsterCharacter targetMonster, Vector3 exitWorldPos)
        {
            state = BallState.AttractingToExit;
            _attractionStartTime = Time.time;
            _targetMonster = targetMonster;

            if (sphereCollider != null)
            {
                // Become trigger so this ball glides smoothly through resting balls and past the stopper
                sphereCollider.isTrigger = true;
            }

            // Bring slightly forward in Z so it smoothly and visibly glides in front of resting balls
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
                    rb.linearVelocity = toExit.normalized * 2.2f;
                }
                rb.WakeUp();
            }

            // Wake up remaining balls in container so they naturally tumble down and fill the freed space
            if (BallObjectPool.Instance != null)
            {
                var activeList = BallObjectPool.Instance.ActiveBalls;
                for (int i = 0; i < activeList.Count; i++)
                {
                    var other = activeList[i];
                    if (other != null && other != this && other.state == BallState.InContainer && other.rb != null)
                    {
                        other.rb.WakeUp();
                    }
                }
            }
        }

        /// <summary>
        /// Applies controlled attraction physics toward the bottom exit while preserving 2D container containment.
        /// </summary>
        public void ApplyAttractionToExit(Vector3 exitWorldPos, float pullForce = 16f, float maxSpeed = 2.4f)
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
            toExit.z = 0f; // strictly 2D attraction

            float dist = toExit.magnitude;
            if (dist > 0.001f)
            {
                Vector3 dir = toExit / dist;
                Vector3 targetVel = dir * maxSpeed;

                rb.linearVelocity = Vector3.MoveTowards(rb.linearVelocity, targetVel, pullForce * Time.fixedDeltaTime);

                // Spout centering: When approaching or inside the spout chute, center X
                ShapedGlassContainer container = BallContainerManager.Instance != null ? BallContainerManager.Instance.glassContainer : null;
                float throatY = (container != null) ? container.GetSpoutThroatY() : -0.72f;
                float worldThroatY = (container != null) ? container.transform.TransformPoint(new Vector3(0f, throatY, 0f)).y : throatY;

                if (myPos.y <= worldThroatY + 0.35f)
                {
                    float targetX = Mathf.MoveTowards(myPos.x, exitWorldPos.x, 3.2f * Time.fixedDeltaTime);
                    Vector3 p = transform.position;
                    p.x = targetX;
                    p.z = -0.16f;
                    transform.position = p;
                }
            }
        }

        /// <summary>
        /// Smooth ballistic flight from the spout exit directly into the target monster's open mouth!
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

            // Transition from dynamic physics to silky smooth ballistic flight
            StopAndMakeKinematic();

            if (sphereCollider != null)
            {
                sphereCollider.enabled = false;
                sphereCollider.isTrigger = false;
            }

            transform.DOKill();

            Vector3 startPos = transform.position;
            startPos.z = -0.18f;
            transform.position = startPos;

            Vector3 mouthPos = (targetMonster != null && targetMonster.mouthTargetPoint != null)
                ? targetMonster.mouthTargetPoint.position
                : (targetMonster != null ? targetMonster.transform.position + new Vector3(0f, 0.04f, -0.15f) : spoutExitPos);

            // Notify monster that a delicious ball is incoming so it opens its mouth in anticipation!
            if (targetMonster != null)
            {
                targetMonster.OnBallIncoming(this);
            }

            float duration = Mathf.Clamp(Vector3.Distance(startPos, mouthPos) * 0.22f, 0.34f, 0.48f);

            // Parabolic jump arc with subtle organic rolling
            Sequence flightSeq = DOTween.Sequence();
            flightSeq.Join(transform.DOJump(mouthPos, jumpPower: 0.32f, numJumps: 1, duration: duration).SetEase(Ease.Linear));
            flightSeq.Join(transform.DORotate(new Vector3(180f, 0f, 90f), duration, RotateMode.FastBeyond360));

            // Rapid absorption shrink directly at the mouth threshold
            flightSeq.Insert(duration * 0.72f, transform.DOScale(Vector3.zero, duration * 0.28f).SetEase(Ease.InBack));

            flightSeq.OnComplete(() =>
            {
                if (_targetMonster != null)
                {
                    _targetMonster.FeedBall(this);
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