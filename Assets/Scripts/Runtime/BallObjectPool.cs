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
        public float bounciness = 0.24f;
        public float dynamicFriction = 0.035f;
        public float staticFriction = 0.045f;

        private const int ColorVariantCount = 3;

        private readonly Queue<BallPhysicsController> _availableBalls = new Queue<BallPhysicsController>();
        private readonly List<BallPhysicsController> _activeBalls = new List<BallPhysicsController>();
        private readonly Dictionary<PuzzleColor, Material> _candyMaterials = new Dictionary<PuzzleColor, Material>();
        private readonly Dictionary<PuzzleColor, Material[]> _candyMaterialVariants = new Dictionary<PuzzleColor, Material[]>();

        private static MaterialPropertyBlock s_BallPropertyBlock;
        private static readonly int BaseColorPropId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorPropId = Shader.PropertyToID("_Color");
        private static readonly int ShadowColorPropId = Shader.PropertyToID("_ShadowColor");
        private static readonly int HighlightColorPropId = Shader.PropertyToID("_HighlightColor");
        private static readonly int SmoothnessPropId = Shader.PropertyToID("_Smoothness");
        private static readonly int BevelHighlightPropId = Shader.PropertyToID("_BevelHighlight");
        private static readonly int RimSeparationPropId = Shader.PropertyToID("_RimSeparation");
        private static readonly int WobblePhasePropId = Shader.PropertyToID("_WobblePhase");
        private static readonly int WobbleAmountPropId = Shader.PropertyToID("_WobbleAmount");
        private static readonly int SquashAmountPropId = Shader.PropertyToID("_SquashAmount");
        private static readonly int SquashAxisPropId = Shader.PropertyToID("_SquashAxis");

        private Material _sharedInstancedBallMaterial;
        private PhysicsMaterial _sharedPhysMaterial;
        private Mesh _sharedSphereMesh;
        private GameObject _poolContainer;

        public List<BallPhysicsController> ActiveBalls => _activeBalls;
        public Material SharedInstancedBallMaterial
        {
            get
            {
                EnsureSharedResources();
                return _sharedInstancedBallMaterial;
            }
        }
        public Mesh SharedSphereMesh
        {
            get
            {
                EnsureSharedResources();
                return _sharedSphereMesh;
            }
        }

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
                    bounciness = 0.28f,
                    dynamicFriction = 0.0f,
                    staticFriction = 0.0f,
                    bounceCombine = PhysicsMaterialCombine.Average,
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
                if (Application.isPlaying) Destroy(temp);
                else DestroyImmediate(temp);
            }

            if (_sharedInstancedBallMaterial == null)
            {
                Shader ballShader = Shader.Find("PuzzleGlass/CandyCubeLit")
                    ?? Shader.Find("Universal Render Pipeline/Lit")
                    ?? Shader.Find("Standard");
                _sharedInstancedBallMaterial = new Material(ballShader)
                {
                    name = "Mat_CandyBall_Instanced",
                    enableInstancing = true,
                    renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry + 15
                };
                if (_sharedInstancedBallMaterial.HasProperty(BevelHighlightPropId))
                    _sharedInstancedBallMaterial.SetFloat(BevelHighlightPropId, 0f);
                if (_sharedInstancedBallMaterial.HasProperty(RimSeparationPropId))
                    _sharedInstancedBallMaterial.SetFloat(RimSeparationPropId, 0.46f);
            }
        }

        public static void ComputeBallVariantVisuals(
            PuzzleColor color,
            int variantIndex,
            out Color baseColor,
            out Color shadowColor,
            out Color highlightColor,
            out float smoothness)
        {
            Color raw = PuzzleColorPalette.GetBodyColor(color);
            int v = Mathf.Abs(variantIndex) % ColorVariantCount;
            if (v == 0)
            {
                baseColor = Color.Lerp(raw, Color.black, 0.03f);
                smoothness = 0.44f;
            }
            else if (v == 2)
            {
                baseColor = Color.Lerp(raw, Color.white, 0.025f);
                smoothness = 0.60f;
            }
            else
            {
                baseColor = raw;
                smoothness = 0.52f;
            }
            baseColor.a = raw.a;
            shadowColor = PuzzleColorPalette.GetCubeShadowColor(color);
            highlightColor = PuzzleColorPalette.GetCubeHighlightColor(color);
        }

        public static void ApplyBallPropertyBlock(
            Renderer renderer,
            Color baseColor,
            Color shadowColor,
            Color highlightColor,
            float smoothness,
            float wobblePhase,
            float wobbleAmount,
            float squashAmount,
            Vector3 squashAxis)
        {
            if (renderer == null) return;
            if (s_BallPropertyBlock == null) s_BallPropertyBlock = new MaterialPropertyBlock();

            s_BallPropertyBlock.Clear();
            s_BallPropertyBlock.SetColor(BaseColorPropId, baseColor);
            s_BallPropertyBlock.SetColor(ColorPropId, baseColor);
            s_BallPropertyBlock.SetColor(ShadowColorPropId, shadowColor);
            s_BallPropertyBlock.SetColor(HighlightColorPropId, highlightColor);
            s_BallPropertyBlock.SetFloat(SmoothnessPropId, smoothness);
            s_BallPropertyBlock.SetFloat(BevelHighlightPropId, 0f);
            s_BallPropertyBlock.SetFloat(RimSeparationPropId, 0.46f);
            s_BallPropertyBlock.SetFloat(WobblePhasePropId, wobblePhase);
            s_BallPropertyBlock.SetFloat(WobbleAmountPropId, wobbleAmount);
            s_BallPropertyBlock.SetFloat(SquashAmountPropId, squashAmount);
            s_BallPropertyBlock.SetVector(SquashAxisPropId, new Vector4(squashAxis.x, squashAxis.y, squashAxis.z, 0f));
            renderer.SetPropertyBlock(s_BallPropertyBlock);
        }

        public static void ApplyBallPropertyBlock(Renderer renderer, PuzzleColor color, int variantIndex = 1, float wobblePhase = 0f, float wobbleAmount = 0f)
        {
            ComputeBallVariantVisuals(color, variantIndex, out Color baseCol, out Color shadowCol, out Color highCol, out float smooth);
            ApplyBallPropertyBlock(renderer, baseCol, shadowCol, highCol, smooth, wobblePhase, wobbleAmount, 0f, Vector3.up);
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

            int variantIdx = UnityEngine.Random.Range(0, ColorVariantCount);
            ball.SetupBall(color, r, _sharedPhysMaterial, _sharedInstancedBallMaterial, _sharedSphereMesh, variantIdx);

            _activeBalls.Add(ball);
            return ball;
        }

        public Material GetBallMaterialVariant(PuzzleColor color, int variantIndex)
        {
            EnsureSharedResources();
            return _sharedInstancedBallMaterial;
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
            BallCollectionController.Instance?.MarkBallFieldDirty();
        }

        public void ClearAllActiveBalls()
        {
            if (_poolContainer == null) InitializePool();

            foreach (var ball in FindObjectsByType<BallPhysicsController>(FindObjectsSortMode.None))
            {
                ball.RecycleSelf();
            }
            _activeBalls.Clear();
            BallCollectionController.Instance?.MarkBallFieldDirty();
        }

        public Material GetBallMaterial(PuzzleColor color)
        {
            EnsureSharedResources();
            return _sharedInstancedBallMaterial;
        }
    }
}