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
        public float ballRadius = 0.08f;
        public float defaultMass = 0.20f;
        public float defaultDrag = 0.40f;
        public float defaultAngularDrag = 0.80f;

        private MonsterCharacter _targetMonster;
        private Action<BallPhysicsController> _onEatenCallback;
        private Vector3 _originalScale = Vector3.one;
        private Tweener _flightTween;
        private Sequence _scaleSeq;

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
                sphereCollider.radius = ballRadius;
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

            if (sphereCollider != null)
            {
                sphereCollider.radius = ballRadius;
                sphereCollider.sharedMaterial = physMaterial;
                sphereCollider.enabled = true;
                sphereCollider.isTrigger = false;
            }

            float diameter = ballRadius * 2.0f;
            _originalScale = new Vector3(diameter, diameter, diameter);
            transform.localScale = _originalScale;

            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.constraints = RigidbodyConstraints.FreezePositionZ;
            }
        }

        /// <summary>
        /// Applies controlled attraction physics toward the bottom exit while preserving collisions with nearby balls.
        /// </summary>
        public void ApplyAttractionToExit(Vector3 exitWorldPos, float pullForce = 22f, float maxSpeed = 4.2f)
        {
            if (state == BallState.FlyingToMonster || state == BallState.Pooled || rb == null) return;

            state = BallState.AttractingToExit;

            Vector3 toExit = exitWorldPos - transform.position;
            toExit.z = 0f; // 2D plane

            float dist = toExit.magnitude;
            if (dist > 0.001f)
            {
                Vector3 dir = toExit / dist;
                // Controlled physical attraction: pull downward toward exit while allowing rolling over neighboring balls
                Vector3 force = dir * pullForce;
                rb.AddForce(force, ForceMode.Force);

                // Clamp horizontal and downward velocity so balls don't shoot violently
                Vector3 vel = rb.linearVelocity;
                if (vel.magnitude > maxSpeed)
                {
                    rb.linearVelocity = Vector3.ClampMagnitude(vel, maxSpeed);
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
            }

            transform.DOKill();

            Vector3 startPos = transform.position;
            Vector3 mouthPos = (targetMonster != null && targetMonster.mouthTargetPoint != null)
                ? targetMonster.mouthTargetPoint.position
                : (targetMonster != null ? targetMonster.transform.position + new Vector3(0f, 0.15f, -0.2f) : spoutExitPos);

            // Slightly bring forward in Z for unobstructed sightline to mouth
            startPos.z = -0.35f;
            mouthPos.z = -0.35f;
            transform.position = startPos;

            // Notify monster that a delicious ball is incoming so it opens its mouth in anticipation!
            if (targetMonster != null)
            {
                targetMonster.OnBallIncoming(this);
            }

            float duration = Mathf.Clamp(Vector3.Distance(startPos, mouthPos) * 0.16f, 0.22f, 0.42f);

            // Parabolic jump arc with subtle organic rolling
            Sequence flightSeq = DOTween.Sequence();
            flightSeq.Join(transform.DOJump(mouthPos, jumpPower: 0.35f, numJumps: 1, duration: duration).SetEase(Ease.InQuad));
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

            // Velocity can only be written while the body is still dynamic
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

