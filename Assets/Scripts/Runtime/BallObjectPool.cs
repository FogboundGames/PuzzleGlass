using System.Collections.Generic;
using UnityEngine;

namespace PuzzleGlass
{
    public class BallObjectPool : MonoBehaviour
    {
        public static BallObjectPool Instance { get; private set; }

        [Header("Pool Settings")]
        public int initialPoolSize = 120;
        public float defaultBallRadius = 0.082f;

        [Header("Physics Material Settings")]
        public float bounciness = 0.22f;
        public float dynamicFriction = 0.24f;
        public float staticFriction = 0.32f;

        private readonly Queue<BallPhysicsController> _availableBalls = new Queue<BallPhysicsController>();
        private readonly List<BallPhysicsController> _activeBalls = new List<BallPhysicsController>();
        private readonly Dictionary<PuzzleColor, Material> _candyMaterials = new Dictionary<PuzzleColor, Material>();

        private PhysicsMaterial _sharedPhysMaterial;
        private Mesh _sharedSphereMesh;
        private GameObject _poolContainer;

        public List<BallPhysicsController> ActiveBalls => _activeBalls;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            InitializePool();
        }

        private void OnEnable()
        {
            if (Instance == null) Instance = this;
        }

        public void InitializePool()
        {
            if (_poolContainer == null)
            {
                _poolContainer = new GameObject("BallObjectPool_Root");
                _poolContainer.transform.SetParent(transform, false);
            }

            EnsureSharedResources();

            // Pre-warm pool
            while (_availableBalls.Count + _activeBalls.Count < initialPoolSize)
            {
                var ball = CreateNewBallInstance();
                ball.gameObject.SetActive(false);
                _availableBalls.Enqueue(ball);
            }
        }

        private void EnsureSharedResources()
        {
            if (_sharedPhysMaterial == null)
            {
                _sharedPhysMaterial = new PhysicsMaterial("Mat_BallPhysical")
                {
                    bounciness = bounciness,
                    dynamicFriction = dynamicFriction,
                    staticFriction = staticFriction,
                    bounceCombine = PhysicsMaterialCombine.Minimum,
                    frictionCombine = PhysicsMaterialCombine.Average
                };
            }

            if (_sharedSphereMesh == null)
            {
                // Create a clean primitive sphere mesh once to share across all 100+ balls
                GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                var mf = temp.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    _sharedSphereMesh = mf.sharedMesh;
                }
                DestroyImmediate(temp);
            }

            // Ensure materials for all colors in PuzzleColor enum
            var colors = (PuzzleColor[])System.Enum.GetValues(typeof(PuzzleColor));
            for (int i = 0; i < colors.Length; i++)
            {
                var c = colors[i];
                if (!_candyMaterials.ContainsKey(c) || _candyMaterials[c] == null)
                {
                    _candyMaterials[c] = PuzzleColorPalette.CreateCandyCubeMaterial(c, $"Mat_Ball_{c}");
                }
            }
        }

        private BallPhysicsController CreateNewBallInstance()
        {
            GameObject obj = new GameObject("PuzzleBall_Instance");
            obj.transform.SetParent(_poolContainer.transform, false);

            var mf = obj.AddComponent<MeshFilter>();
            mf.sharedMesh = _sharedSphereMesh;

            var mr = obj.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            var sc = obj.AddComponent<SphereCollider>();
            sc.radius = defaultBallRadius;
            sc.sharedMaterial = _sharedPhysMaterial;

            var rb = obj.AddComponent<Rigidbody>();
            rb.mass = 0.20f;
            rb.linearDamping = 0.40f;
            rb.angularDamping = 0.80f;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.constraints = RigidbodyConstraints.FreezePositionZ;

            var controller = obj.AddComponent<BallPhysicsController>();
            controller.ballRadius = defaultBallRadius;
            controller.rb = rb;
            controller.sphereCollider = sc;
            controller.meshRenderer = mr;
            controller.meshFilter = mf;

            return controller;
        }

        public BallPhysicsController SpawnBall(PuzzleColor color, Vector3 position, float radius = -1f)
        {
            EnsureSharedResources();

            float r = (radius > 0.02f) ? radius : defaultBallRadius;

            BallPhysicsController ball;
            if (_availableBalls.Count > 0)
            {
                ball = _availableBalls.Dequeue();
            }
            else
            {
                ball = CreateNewBallInstance();
            }

            ball.transform.SetParent(_poolContainer.transform, false);
            ball.transform.position = position;
            ball.transform.rotation = Quaternion.identity;
            ball.gameObject.SetActive(true);

            Material mat = _candyMaterials.ContainsKey(color) ? _candyMaterials[color] : null;
            ball.SetupBall(color, r, _sharedPhysMaterial, mat, _sharedSphereMesh);

            _activeBalls.Add(ball);
            return ball;
        }

        public void ReturnBall(BallPhysicsController ball)
        {
            if (ball == null) return;

            _activeBalls.Remove(ball);
            if (!_availableBalls.Contains(ball))
            {
                ball.transform.SetParent(_poolContainer.transform, false);
                _availableBalls.Enqueue(ball);
            }
        }

        public void ClearAllActiveBalls()
        {
            for (int i = _activeBalls.Count - 1; i >= 0; i--)
            {
                var ball = _activeBalls[i];
                if (ball != null)
                {
                    ball.RecycleSelf();
                }
            }
            _activeBalls.Clear();
        }

        public Material GetBallMaterial(PuzzleColor color)
        {
            EnsureSharedResources();
            return _candyMaterials.ContainsKey(color) ? _candyMaterials[color] : null;
        }
    }
}

