using System.Collections.Generic;
using UnityEngine;

namespace PuzzleGlass
{
    [ExecuteAlways]
    public class BallObjectPool : MonoBehaviour
    {
        private const string PoolRootName = "BallObjectPool_Root";

        private static BallObjectPool _instance;
        public static BallObjectPool Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<BallObjectPool>();
                    if (_instance == null)
                    {
                        var poolObj = GameObject.Find("BallObjectPool");
                        if (poolObj == null) poolObj = new GameObject("BallObjectPool");
                        _instance = poolObj.GetComponent<BallObjectPool>().OrNull() ?? poolObj.AddComponent<BallObjectPool>();
                    }
                }
                return _instance;
            }
            private set => _instance = value;
        }

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
            if (ClaimSingleton()) InitializePool();
        }

        private void OnEnable()
        {
            // Domain reload calls OnEnable without Awake and clears the non-serialized pool state
            if (ClaimSingleton()) InitializePool();
        }

        private bool ClaimSingleton()
        {
            if (_instance == null) _instance = this;
            if (_instance == this) return true;

            DestroyDeferred(gameObject);
            return false;
        }

        // Immediate destruction is unsafe while the hierarchy is still being activated
        private static void DestroyDeferred(GameObject target)
        {
            if (Application.isPlaying)
            {
                Destroy(target);
                return;
            }

#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (target == null) return;

                if (Application.isPlaying)
                {
                    Destroy(target);
                    return;
                }

                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(target.scene);
                DestroyImmediate(target);
            };
#endif
        }

        private void AdoptOrCreatePoolRoot()
        {
            _availableBalls.Clear();
            _activeBalls.Clear();

            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = transform.GetChild(i).gameObject;
                if (child.name != PoolRootName) continue;

                if (_poolContainer == null) _poolContainer = child;
                else DestroyDeferred(child);
            }

            if (_poolContainer == null)
            {
                _poolContainer = new GameObject(PoolRootName);
                _poolContainer.transform.SetParent(transform, false);
                return;
            }

            foreach (var ball in _poolContainer.GetComponentsInChildren<BallPhysicsController>(true))
            {
                if (ball.gameObject.activeSelf) _activeBalls.Add(ball);
                else if (_availableBalls.Count < initialPoolSize) _availableBalls.Enqueue(ball);
                else DestroyDeferred(ball.gameObject);
            }
        }

        public void InitializePool()
        {
            if (_poolContainer == null) AdoptOrCreatePoolRoot();

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
            if (_poolContainer == null) InitializePool();
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
            if (_poolContainer == null) InitializePool();

            _activeBalls.Remove(ball);
            if (!_availableBalls.Contains(ball))
            {
                ball.transform.SetParent(_poolContainer.transform, false);
                _availableBalls.Enqueue(ball);
            }
        }

        public void ClearAllActiveBalls()
        {
            if (_poolContainer == null) InitializePool();

            // Scene-wide search so balls saved under old pools or lost after a reload are cleared too
            foreach (var ball in FindObjectsByType<BallPhysicsController>(FindObjectsSortMode.None))
            {
                ball.RecycleSelf();
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

