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
        public int initialPoolSize = 140;
        public float defaultBallRadius = 0.0825f;

        [Header("Physics Material Settings")]
        public float bounciness = 0.22f;
        public float dynamicFriction = 0.06f;
        public float staticFriction = 0.08f;

        private const int ColorVariantCount = 3;

        private readonly Queue<BallPhysicsController> _availableBalls = new Queue<BallPhysicsController>();
        private readonly List<BallPhysicsController> _activeBalls = new List<BallPhysicsController>();
        private readonly Dictionary<PuzzleColor, Material> _candyMaterials = new Dictionary<PuzzleColor, Material>();
        private readonly Dictionary<PuzzleColor, Material[]> _candyMaterialVariants = new Dictionary<PuzzleColor, Material[]>();

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
            if (ClaimSingleton()) InitializePool();
        }

        private bool ClaimSingleton()
        {
            if (_instance == null) _instance = this;
            if (_instance == this) return true;

            DestroyDeferred(gameObject);
            return false;
        }

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
                    frictionCombine = PhysicsMaterialCombine.Minimum
                };
            }

            if (_sharedSphereMesh == null)
            {
                // Create a clean primitive sphere mesh once to share across all balls
                GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                var mf = temp.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    _sharedSphereMesh = mf.sharedMesh;
                }
                DestroyImmediate(temp);
            }

            // Ensure subtle material variants per color in PuzzleColor enum (satin / standard / pearlescent)
            var colors = (PuzzleColor[])System.Enum.GetValues(typeof(PuzzleColor));
            for (int i = 0; i < colors.Length; i++)
            {
                var c = colors[i];
                if (!_candyMaterialVariants.TryGetValue(c, out var variants) || variants == null || variants.Length != ColorVariantCount || variants[0] == null)
                {
                    variants = new Material[ColorVariantCount];
                    for (int v = 0; v < ColorVariantCount; v++)
                    {
                        variants[v] = CreateBallVariantMaterial(c, v);
                    }
                    _candyMaterialVariants[c] = variants;
                    _candyMaterials[c] = variants[1];
                }
            }
        }

        private static Material CreateBallVariantMaterial(PuzzleColor color, int variantIndex)
        {
            Material m = PuzzleColorPalette.CreateCandyBallMaterial(color, $"Mat_Ball_{color}_v{variantIndex}");
            if (m == null || variantIndex == 1) return m;

            Color baseColor = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;
            Color adjustedColor = (variantIndex == 0)
                ? Color.Lerp(baseColor, Color.black, 0.03f)
                : Color.Lerp(baseColor, Color.white, 0.025f);
            adjustedColor.a = baseColor.a;

            m.color = adjustedColor;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", adjustedColor);
            if (m.HasProperty("_Color")) m.SetColor("_Color", adjustedColor);

            float smoothness = (variantIndex == 0) ? 0.44f : 0.60f;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);

            return m;
        }

        private BallPhysicsController CreateNewBallInstance()
        {
            GameObject obj = new GameObject("PuzzleBall_Instance");
            obj.transform.SetParent(_poolContainer.transform, false);

            // Decoupled visual child lets squash/stretch & micro size variation animate smoothly without disturbing physics contacts
            GameObject visualObj = new GameObject("Visual");
            visualObj.transform.SetParent(obj.transform, false);

            var mf = visualObj.AddComponent<MeshFilter>();
            mf.sharedMesh = _sharedSphereMesh;

            var mr = visualObj.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            var sc = obj.AddComponent<SphereCollider>();
            sc.radius = 0.5f; // PrimitiveType.Sphere mesh has radius 0.5
            sc.sharedMaterial = _sharedPhysMaterial;

            var rb = obj.AddComponent<Rigidbody>();
            rb.mass = 0.25f;
            rb.linearDamping = 0.35f;
            rb.angularDamping = 0.70f;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.constraints = RigidbodyConstraints.FreezePositionZ;

            var controller = obj.AddComponent<BallPhysicsController>();
            controller.ballRadius = defaultBallRadius;
            controller.rb = rb;
            controller.sphereCollider = sc;
            controller.visualTransform = visualObj.transform;
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
            ball.transform.rotation = UnityEngine.Random.rotation;
            ball.gameObject.SetActive(true);

            Material mat = GetBallMaterialVariant(color, UnityEngine.Random.Range(0, ColorVariantCount));
            ball.SetupBall(color, r, _sharedPhysMaterial, mat, _sharedSphereMesh);

            _activeBalls.Add(ball);
            return ball;
        }

        public Material GetBallMaterialVariant(PuzzleColor color, int variantIndex)
        {
            EnsureSharedResources();
            if (_candyMaterialVariants.TryGetValue(color, out var variants) && variants != null && variants.Length > 0)
            {
                int idx = Mathf.Abs(variantIndex) % variants.Length;
                return variants[idx] ?? variants[0];
            }
            return GetBallMaterial(color);
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