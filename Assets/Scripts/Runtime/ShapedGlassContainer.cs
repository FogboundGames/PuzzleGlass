using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

namespace PuzzleGlass
{
    public enum GlassShapePreset
    {
        BulbousFlask = 0,
        ClassicBottle = 1,
        Hourglass = 2,
        HeartFlask = 3,
        DiamondCarafe = 4,
        ConicalErlenmeyer = 5,
        DoubleBulb = 6,
        WavyAmphora = 7,
        SCurveFunnel = 8,
        CustomFreeform = 9,
        SquareFlask = 10,        // Hunili Kare Cam Şişe
        RectangleFlask = 11,     // Hunili Dikey Dikdörtgen Cam Şişe
        WideRectangleFlask = 12, // Hunili Yatay Dikdörtgen Cam Şişe
        SquareBox = 13,          // Düz Tabanlı Tam Kare Cam Kutu
        RectangleBox = 14        // Düz Tabanlı Tam Dikdörtgen Cam Kutu
    }

    public enum GlassColorTheme
    {
        SkyBlueGlossy = 0,
        CrystalClear = 1,
        EmeraldGreen = 2,
        RoseQuartz = 3,
        AmethystPurple = 4,
        AmberGold = 5,
        CustomTheme = 6
    }

    [System.Serializable]
    public struct GlassContourPoint
    {
        public float y;
        public float halfWidth;
        public float centerX;
        public float x => centerX;
        [Range(0f, 1f)]
        public float cornerTension;

        public GlassContourPoint(float y, float halfWidth, float centerX = 0f, float cornerTension = 0f)
        {
            this.y = y;
            this.halfWidth = Mathf.Max(0.08f, halfWidth);
            this.centerX = centerX;
            this.cornerTension = Mathf.Clamp01(cornerTension);
        }
    }

    [System.Serializable]
    public class BottleCube
    {
        public GameObject gameObject;
        public PuzzleColor color;
        public int layerOrder;
        public bool isFrozen;
        public float x;
        public float y;
        public float targetX;
        public float targetY;
        public float zOffset;
        public float angle;
        public float rotX;
        public float rotY;
        public float vx;
        public float vy;
        public float vAng;
    }

    [ExecuteAlways]
    public class ShapedGlassContainer : MonoBehaviour
    {
        [Header("Shape Preset & Symmetry")]
        public GlassShapePreset shapePreset = GlassShapePreset.RectangleBox;
        public bool IsFlatBottomBox => shapePreset == GlassShapePreset.SquareBox || shapePreset == GlassShapePreset.RectangleBox;
        public bool symmetricMode = true;
        [Range(4, 24)]
        public int curveSubdivisions = 14;
        [Range(0.5f, 1.8f)]
        public float widthScale = 1.0f;
        [Range(0.6f, 1.6f)]
        public float heightScale = 1.0f;

        [Header("Glass Contour Control Points (Bottom Spout -> Top Neck)")]
        public List<GlassContourPoint> contourPoints = new List<GlassContourPoint>();

        [Header("Glass Compartments & Internal Dividers (Per-Level)")]
        public GlassCompartmentPreset compartmentPreset = GlassCompartmentPreset.None_OrderedPool;
        public List<GlassDividerData> dividers = new List<GlassDividerData>();
        public List<CompartmentColorZone> compartmentZones = new List<CompartmentColorZone>();

        [Header("Glass Color & Material Aesthetics")]
        public GlassColorTheme colorTheme = GlassColorTheme.SkyBlueGlossy;
        public Color glassBodyColor = new Color(0.36f, 0.71f, 0.98f, 0.78f);
        public Color glassCenterGlowColor = new Color(0.76f, 0.91f, 1.00f, 0.20f);
        public Color glassOuterRimColor = new Color(0.17f, 0.49f, 0.85f, 0.92f);
        public Color glassInnerRimColor = new Color(0.96f, 0.99f, 1.00f, 0.95f);
        public Color glassFrontLensColor = new Color(0.95f, 0.99f, 1.00f, 0.00f);
        public Color earHandleColor = new Color(0.92f, 0.96f, 1.00f, 1.00f);

        [Header("Rounded Box Pool Frame Settings")]
        [Range(0.04f, 0.65f)]
        public float boxCornerRadius = 0.22f;
        [Range(0.04f, 0.24f)]
        public float boxFrameThickness = 0.115f;
        public Color boxFrameColor = new Color(0.50f, 0.50f, 0.90f, 1.0f);
        public Color boxFrameHighlightColor = new Color(0.84f, 0.84f, 0.98f, 1.0f);
        public Color boxFrameEdgeShadeColor = new Color(0.38f, 0.36f, 0.80f, 1.0f);
        public Color dropShadowColor = new Color(0.72f, 0.60f, 0.48f, 0.30f);

        [Header("Glass 3D Rim & Shell Settings")]
        [Range(0.015f, 0.090f)]
        public float glassRimRadius = 0.042f;
        [Range(0.025f, 0.120f)]
        public float innerRimInset = 0.064f;
        [Range(0.02f, 0.25f)]
        public float glassShellDepth = 0.08f;
        public bool showShoulderHandles = false;
        public Vector2 shoulderHandleOffset = new Vector2(0f, 0f);
        [Range(0.4f, 1.8f)]
        public float shoulderHandleScale = 1.0f;
        public bool showSpecularReflections = true;
        [Range(0.1f, 1.0f)]
        public float specularIntensity = 0.78f;

        [Header("Particle Fill & Granular Physics")]
        public BottleLayerPattern layerPattern = BottleLayerPattern.VFunnelBands;
        [Range(0.08f, 0.30f)]
        public float particleSize = 0.24f;
        [Range(0.15f, 0.85f)]
        public float particleFillRatio = 0.48f;
        [Range(0, 20)]
        public int floatingBeadCount = 0;
        [Range(1, 8)]
        public int particlesPerMonsterCount = 1;

        [Header("Pixel Art Visual System (Image 2)")]
        public ParticleArrangementMode particleMode = ParticleArrangementMode.PhysicsBalls;
        public PixelArtPreset pixelArtPreset = PixelArtPreset.Frog;
        public bool freezePatternInPlace = true;
        [Range(0.10f, 0.30f)]
        public float gridCellSpacing = 0.165f;
        public float patternVerticalOffset = 0.0f;
        public bool fillEmptyWithColor = false;
        public PuzzleColor emptyCellColor = PuzzleColor.Blue;
        public bool allowCollectAnyColorFromPattern = true;
        public PixelArtPattern customPattern;

        [Header("Legacy / Reference Dimensions")]
        public float spoutBottomY = -1.20f;
        public float spoutTopY = -0.52f;
        public float chamberBottomY = -0.50f;
        public float chamberTopY = 0.90f;
        public float neckBottomY = 0.90f;
        public float neckTopY = 2.65f;
        public float spoutWidth = 0.35f;
        public float chamberWidth = 2.84f;
        public float neckWidth = 0.60f;
        public float glassWallDepth = 0.22f;

        #region Parametric Shape Helpers (Glass Shape Editor)
        public float SpoutWidth
        {
            get => (contourPoints != null && contourPoints.Count > 0) ? contourPoints[0].halfWidth * 2f * widthScale : spoutWidth;
            set
            {
                if (contourPoints == null || contourPoints.Count < 2) return;
                float hw = Mathf.Max(0.06f, value * 0.5f / Mathf.Max(0.1f, widthScale));
                var cp0 = contourPoints[0]; cp0.halfWidth = hw; contourPoints[0] = cp0;
                var cp1 = contourPoints[1]; cp1.halfWidth = hw; contourPoints[1] = cp1;
                RebuildGlassMeshAndSettleParticles();
            }
        }

        public float SpoutLength
        {
            get => (contourPoints != null && contourPoints.Count > 1) ? (contourPoints[1].y - contourPoints[0].y) * heightScale : (spoutTopY - spoutBottomY);
            set
            {
                if (contourPoints == null || contourPoints.Count < 2) return;
                float len = Mathf.Max(0.08f, value / Mathf.Max(0.1f, heightScale));
                var cp0 = contourPoints[0];
                cp0.y = contourPoints[1].y - len;
                contourPoints[0] = cp0;
                RebuildGlassMeshAndSettleParticles();
            }
        }

        public float NeckWidth
        {
            get => (contourPoints != null && contourPoints.Count > 0) ? contourPoints[contourPoints.Count - 1].halfWidth * 2f * widthScale : neckWidth;
            set
            {
                if (contourPoints == null || contourPoints.Count < 2) return;
                int last = contourPoints.Count - 1;
                float hw = Mathf.Max(0.06f, value * 0.5f / Mathf.Max(0.1f, widthScale));
                var cplast = contourPoints[last]; cplast.halfWidth = hw; contourPoints[last] = cplast;
                var cpprev = contourPoints[last - 1]; cpprev.halfWidth = hw; contourPoints[last - 1] = cpprev;
                RebuildGlassMeshAndSettleParticles();
            }
        }

        public float NeckLength
        {
            get => (contourPoints != null && contourPoints.Count > 1) ? (contourPoints[contourPoints.Count - 1].y - contourPoints[contourPoints.Count - 2].y) * heightScale : (neckTopY - neckBottomY);
            set
            {
                if (contourPoints == null || contourPoints.Count < 2) return;
                int last = contourPoints.Count - 1;
                float len = Mathf.Max(0.10f, value / Mathf.Max(0.1f, heightScale));
                var cplast = contourPoints[last];
                cplast.y = contourPoints[last - 1].y + len;
                contourPoints[last] = cplast;
                RebuildGlassMeshAndSettleParticles();
            }
        }

        public float ChamberWaistWidth
        {
            get
            {
                if (contourPoints == null || contourPoints.Count < 3) return chamberWidth;
                int mid = contourPoints.Count / 2;
                return contourPoints[mid].halfWidth * 2f * widthScale;
            }
            set
            {
                if (contourPoints == null || contourPoints.Count < 3) return;
                int mid = contourPoints.Count / 2;
                float hw = Mathf.Max(0.08f, value * 0.5f / Mathf.Max(0.1f, widthScale));
                var cp = contourPoints[mid];
                cp.halfWidth = hw;
                contourPoints[mid] = cp;
                RebuildGlassMeshAndSettleParticles();
            }
        }

        public void SpawnTestSandParticles(int count = 64)
        {
            var pool = BallObjectPool.Instance.OrNull() ?? FindFirstObjectByType<BallObjectPool>();
            if (pool == null)
            {
                var poolObj = new GameObject("BallObjectPool");
                pool = poolObj.AddComponent<BallObjectPool>();
            }
            pool.ClearAllActiveBalls();

            EnsureContourInitialized();
            SyncBoundsFromContour();

            var bcm = BallContainerManager.Instance.OrNull() ?? FindFirstObjectByType<BallContainerManager>();
            if (bcm != null)
            {
                bcm.RebuildColliders();
                bcm.SetSpoutStopperActive(true);
            }

            float minY = chamberBottomY + 0.15f;
            float maxY = chamberTopY - 0.15f;
            float r = Mathf.Clamp(particleSize * 0.5f, 0.045f, 0.10f);

            PuzzleColor[] colors = new PuzzleColor[]
            {
                PuzzleColor.Blue, PuzzleColor.Green, PuzzleColor.Pink, PuzzleColor.Yellow,
                PuzzleColor.Purple, PuzzleColor.Orange, PuzzleColor.Red, PuzzleColor.Cyan
            };

            int spawned = 0;
            int maxAttempts = count * 8;
            int attempts = 0;

            while (spawned < count && attempts < maxAttempts)
            {
                attempts++;
                float y = UnityEngine.Random.Range(minY, maxY);
                GetSafeCubeBoundsAtY(y, out float safeLeft, out float safeRight);
                if (safeRight - safeLeft < r * 2.4f) continue;

                float x = UnityEngine.Random.Range(safeLeft + r * 1.2f, safeRight - r * 1.2f);
                Vector3 worldPos = transform.TransformPoint(new Vector3(x, y, -0.10f));
                PuzzleColor col = colors[spawned % colors.Length];

                pool.SpawnBall(col, worldPos, r);
                spawned++;
            }

            Debug.Log($"<color=#2ED170><b>[Glass Shape Editor]</b></color> {spawned} adet test kumu hazneye dolduruldu!");
        }

        public void ClearTestSandParticles()
        {
            var pool = BallObjectPool.Instance.OrNull() ?? FindFirstObjectByType<BallObjectPool>();
            if (pool != null)
            {
                pool.ClearAllActiveBalls();
            }
            CleanOrphanedChildren();
        }

        public bool IsSpoutGateOpen()
        {
            var bcm = BallContainerManager.Instance.OrNull() ?? FindFirstObjectByType<BallContainerManager>();
            return bcm != null && bcm.bottomSpoutStopper != null && !bcm.bottomSpoutStopper.enabled;
        }

        public void SetSpoutGate(bool open)
        {
            var bcm = BallContainerManager.Instance.OrNull() ?? FindFirstObjectByType<BallContainerManager>();
            if (bcm != null)
            {
                bcm.SetSpoutStopperActive(!open);
            }
            var sand = GetComponent<SandSimulationEngine>();
            if (sand != null)
            {
                sand.isGateOpen = open;
            }
        }

        public void ToggleSpoutGate()
        {
            var bcm = BallContainerManager.Instance.OrNull() ?? FindFirstObjectByType<BallContainerManager>();
            if (bcm != null)
            {
                bool isStopperActive = bcm.bottomSpoutStopper != null && bcm.bottomSpoutStopper.enabled;
                SetSpoutGate(isStopperActive); // if active, open it
                Debug.Log($"<color=#2ED170><b>[Glass Shape Editor]</b></color> Alt çıkış kapağı: {(isStopperActive ? "AÇIK (Kumlar Akabilir)" : "KAPALI")}");
            }
        }
        #endregion

        [Header("Materials")]
        public Material glassMaterial;
        public Material glassRimMaterial;
        public Material glassHighlightMaterial;
        public Material trackFloorMaterial;
        public Material hopperMaterial;

        [Header("Spout Exit Point")]
        public Transform spoutExitPoint;

        private GameObject _trackRootObj;
        private GameObject _glassWallsObj;
        private GameObject _cubesContainer;
        private Dictionary<PuzzleColor, Material> _cubeMaterials = new Dictionary<PuzzleColor, Material>();
        private Mesh _roundedCubeMesh;

        private Material _chamberBackGlassMat;
        private Material _chamberInnerGlowMat;
        private Material _outerBlueRimMat;
        private Material _outerIcyHighlightMat;
        private Material _innerDeepGrooveMat;
        private Material _innerCyanRimMat;
        private Material _frontGlassLensMat;
        private Material _specularStreakMat;
        private Material _earHandleMat;
        private Material _leftEarPillMat;
        private Material _rightEarPillMat;
        private Material _glassFakeShadowMat;
        private Material _dividerOuterRimMat;
        private Material _dividerInnerCoreMat;

        private List<BottleCube> _bottleCubes = new List<BottleCube>();
        private List<Vector3> _splineWaypoints = new List<Vector3>();
        private List<float> _cumulativeLengths = new List<float>();
        private float _totalSplineLength = 0f;
        private float _activeSettleTimer = 0f;

        // Fast 256-sample wall lookup table for any custom contour shape
        private const int LUT_SIZE = 256;
        private float[] _lutLeftX = new float[LUT_SIZE];
        private float[] _lutRightX = new float[LUT_SIZE];
        private float[] _lutSafeLeftX = new float[LUT_SIZE];
        private float[] _lutSafeRightX = new float[LUT_SIZE];
        private float _lutMinY = -1.20f;
        private float _lutMaxY = 2.65f;
        private bool _lutValid = false;

        // Cached generated contour paths for SceneView Gizmos / Editor preview
        [NonSerialized] public Vector3[] cachedLeftOuterPath;
        [NonSerialized] public Vector3[] cachedRightOuterPath;
        [NonSerialized] public Vector3[] cachedLeftInnerPath;
        [NonSerialized] public Vector3[] cachedRightInnerPath;

        private void Awake()
        {
            particlesPerMonsterCount = 1;
            if (IsFlatBottomBox) particleSize = 0.24f;
            else if (particleSize < 0.145f) particleSize = 0.155f;
            patternVerticalOffset = 0f;
            EnsureContourInitialized();
            CleanOrphanedChildren();
            RebuildContainer();
        }

        private void OnEnable()
        {
            particlesPerMonsterCount = 1;
            if (IsFlatBottomBox) particleSize = 0.24f;
            else if (particleSize < 0.145f) particleSize = 0.155f;
            patternVerticalOffset = 0f;
            EnsureContourInitialized();
            CleanOrphanedChildren();
            RebuildContainer();
        }

        public void CleanOrphanedChildren()
        {
            Transform existing = transform.Find("CubesInBottle");
            if (existing != null)
            {
                for (int i = existing.childCount - 1; i >= 0; i--)
                {
                    var child = existing.GetChild(i).gameObject;
                    if (Application.isPlaying) Destroy(child);
                    else DestroyImmediate(child);
                }
            }

            if (_bottleCubes != null)
            {
                _bottleCubes.Clear();
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (IsFlatBottomBox) particleSize = 0.24f;
            else if (particleSize < 0.08f) particleSize = 0.24f;
            EnsureContourInitialized();

            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && gameObject != null)
                {
                    CleanOrphanedChildren();
                    RebuildContainer();
                }
            };
        }
#endif

        private void Update()
        {
            if (!Application.isPlaying || _bottleCubes == null || _bottleCubes.Count == 0) return;

            if (_activeSettleTimer > 0f)
            {
                float dt = Mathf.Min(Time.deltaTime, 0.030f);
                _activeSettleTimer -= dt;

                bool anyMoving = StepGranularPhysics(dt, false);
                SyncCubeTransforms();

                if (anyMoving && _activeSettleTimer < 0.35f)
                {
                    _activeSettleTimer = 0.35f;
                }
            }
        }

        public void EnsureContourInitialized()
        {
            if (contourPoints == null || contourPoints.Count < 3)
            {
                ApplyShapePreset(shapePreset, false);
                return;
            }

            if (IsFlatBottomBox && contourPoints.Count < 4)
            {
                ApplyShapePreset(shapePreset, false);
            }
        }

        public void ValidateBottleDimensions()
        {
            EnsureContourInitialized();
            SyncBoundsFromContour();
        }

        public void SyncBoundsFromContour()
        {
            if (contourPoints == null || contourPoints.Count < 2) return;
            spoutBottomY = contourPoints[0].y * heightScale;
            neckTopY = contourPoints[contourPoints.Count - 1].y * heightScale;
            spoutWidth = contourPoints[0].halfWidth * 2f * widthScale;
            neckWidth = contourPoints[contourPoints.Count - 1].halfWidth * 2f * widthScale;

            float maxHW = 0f;
            for (int i = 0; i < contourPoints.Count; i++)
            {
                if (contourPoints[i].halfWidth > maxHW) maxHW = contourPoints[i].halfWidth;
            }
            chamberWidth = maxHW * 2f * widthScale;
        }

        /// <summary>
        /// Applies one of the 9 built-in glass shape presets and optionally rebuilds the 3D container immediately.
        /// </summary>
        public void ApplyShapePreset(GlassShapePreset preset, bool rebuildImmediate = true)
        {
            shapePreset = preset;
            if (contourPoints == null) contourPoints = new List<GlassContourPoint>();
            contourPoints.Clear();

            widthScale = 1.0f;
            heightScale = 1.0f;

            switch (preset)
            {
                case GlassShapePreset.BulbousFlask:
                default:
                    // Smooth C2 Reference Wide Bulbous Sky-Blue Glass Flask (zero step on shoulders or bottom!)
                    symmetricMode = true;
                    showShoulderHandles = true;
                    contourPoints.Add(new GlassContourPoint(-1.20f, 0.175f, 0f, 0.4f)); // Spout tip
                    contourPoints.Add(new GlassContourPoint(-0.82f, 0.175f, 0f, 0.2f)); // Spout tube
                    contourPoints.Add(new GlassContourPoint(-0.58f, 0.230f, 0f, 0.0f)); // Spout throat flare
                    contourPoints.Add(new GlassContourPoint(-0.48f, 0.620f, 0f, 0.0f)); // Smooth bottom floor curve
                    contourPoints.Add(new GlassContourPoint(-0.36f, 1.120f, 0f, 0.0f)); // Lower belly curve
                    contourPoints.Add(new GlassContourPoint(-0.08f, 1.380f, 0f, 0.0f)); // Lower-mid dome
                    contourPoints.Add(new GlassContourPoint( 0.24f, 1.420f, 0f, 0.0f)); // Wide equator
                    contourPoints.Add(new GlassContourPoint( 0.56f, 1.340f, 0f, 0.0f)); // Upper dome
                    contourPoints.Add(new GlassContourPoint( 0.80f, 1.040f, 0f, 0.0f)); // Upper shoulder curve
                    contourPoints.Add(new GlassContourPoint( 0.92f, 0.560f, 0f, 0.0f)); // Smooth neck base fillet
                    contourPoints.Add(new GlassContourPoint( 1.10f, 0.315f, 0f, 0.0f)); // Neck throat
                    contourPoints.Add(new GlassContourPoint( 1.55f, 0.300f, 0f, 0.2f)); // Upper neck
                    contourPoints.Add(new GlassContourPoint( 2.65f, 0.300f, 0f, 0.4f)); // Top neck rim
                    break;

                case GlassShapePreset.ClassicBottle:
                    // Classic Tall Apothecary / Wine / Potion Bottle
                    symmetricMode = true;
                    showShoulderHandles = false;
                    contourPoints.Add(new GlassContourPoint(-1.20f, 0.180f, 0f, 0.4f));
                    contourPoints.Add(new GlassContourPoint(-0.78f, 0.180f, 0f, 0.2f));
                    contourPoints.Add(new GlassContourPoint(-0.52f, 0.480f, 0f, 0.0f));
                    contourPoints.Add(new GlassContourPoint(-0.30f, 0.920f, 0f, 0.0f));
                    contourPoints.Add(new GlassContourPoint( 0.20f, 0.960f, 0f, 0.0f));
                    contourPoints.Add(new GlassContourPoint( 0.72f, 0.940f, 0f, 0.0f));
                    contourPoints.Add(new GlassContourPoint( 1.05f, 0.680f, 0f, 0.0f));
                    contourPoints.Add(new GlassContourPoint( 1.32f, 0.300f, 0f, 0.0f));
                    contourPoints.Add(new GlassContourPoint( 2.55f, 0.280f, 0f, 0.4f));
                    break;

                case GlassShapePreset.Hourglass:
                    // Symmetric Hourglass with Narrow Pinch Waist
                    symmetricMode = true;
                    showShoulderHandles = true;
                    contourPoints.Add(new GlassContourPoint(-1.20f, 0.175f, 0f, 0.4f));
                    contourPoints.Add(new GlassContourPoint(-0.80f, 0.175f, 0f, 0.2f));
                    contourPoints.Add(new GlassContourPoint(-0.55f, 0.650f, 0f, 0.0f));
                    contourPoints.Add(new GlassContourPoint(-0.22f, 1.240f, 0f, 0.0f));
                    contourPoints.Add(new GlassContourPoint( 0.05f, 0.960f, 0f, 0.0f));
                    contourPoints.Add(new GlassContourPoint( 0.30f, 0.420f, 0f, 0.0f)); // Narrow hourglass waist
                    contourPoints.Add(new GlassContourPoint( 0.58f, 0.980f, 0f, 0.0f));
                    contourPoints.Add(new GlassContourPoint( 0.90f, 1.260f, 0f, 0.0f));
                    contourPoints.Add(new GlassContourPoint( 1.22f, 0.820f, 0f, 0.0f));
                    contourPoints.Add(new GlassContourPoint( 1.45f, 0.320f, 0f, 0.0f));
                    contourPoints.Add(new GlassContourPoint( 2.60f, 0.300f, 0f, 0.4f));
                    break;

                case GlassShapePreset.HeartFlask:
                    // Cute Tapered Heart-Shaped Glass Bottle
                    symmetricMode = true;
                    showShoulderHandles = true;
                    contourPoints.Add(new GlassContourPoint(-1.20f, 0.175f, 0f, 0.4f));
                    contourPoints.Add(new GlassContourPoint(-0.82f, 0.175f, 0f, 0.2f));
                    contourPoints.Add(new GlassContourPoint(-0.55f, 0.360f, 0f, 0.0f));
                    contourPoints.Add(new GlassContourPoint(-0.25f, 0.680f, 0f, 0.0f));
                    contourPoints.Add(new GlassContourPoint( 0.10f, 1.060f, 0f, 0.0f));
                    contourPoints.Add(new GlassContourPoint( 0.48f, 1.380f, 0f, 0.0f));
                    contourPoints.Add(new GlassContourPoint( 0.80f, 1.440f, 0f, 0.0f));
                    contourPoints.Add(new GlassContourPoint( 1.05f, 1.120f, 0f, 0.0f));
                    contourPoints.Add(new GlassContourPoint( 1.18f, 0.340f, 0f, 0.0f));
                    contourPoints.Add(new GlassContourPoint( 2.60f, 0.300f, 0f, 0.4f));
                    break;

                case GlassShapePreset.DiamondCarafe:
                    // 1-to-1 Reference Rounded-Lobe Crystal Carafe (Smooth rounded lobes, sloped shoulders & funnel floor)
                    symmetricMode = true;
                    showShoulderHandles = false;
                    contourPoints.Add(new GlassContourPoint(-1.20f, 0.195f, 0f, 0.45f)); // Bottom spout tip
                    contourPoints.Add(new GlassContourPoint(-0.74f, 0.195f, 0f, 0.25f)); // Vertical spout tube
                    contourPoints.Add(new GlassContourPoint(-0.58f, 0.255f, 0f, 0.05f)); // Smooth spout throat fillet
                    contourPoints.Add(new GlassContourPoint(-0.44f, 0.620f, 0f, 0.12f)); // Lower funnel floor curve
                    contourPoints.Add(new GlassContourPoint(-0.25f, 1.090f, 0f, 0.12f)); // Lower sloped funnel wall
                    contourPoints.Add(new GlassContourPoint(-0.05f, 1.345f, 0f, 0.00f)); // Lower rounded lobe curve
                    contourPoints.Add(new GlassContourPoint( 0.14f, 1.400f, 0f, 0.00f)); // Widest smooth rounded lobe equator
                    contourPoints.Add(new GlassContourPoint( 0.34f, 1.275f, 0f, 0.00f)); // Upper rounded lobe curve
                    contourPoints.Add(new GlassContourPoint( 0.64f, 0.840f, 0f, 0.12f)); // Smooth diagonal upper shoulder
                    contourPoints.Add(new GlassContourPoint( 0.92f, 0.445f, 0f, 0.08f)); // Smooth neck base curve
                    contourPoints.Add(new GlassContourPoint( 1.15f, 0.315f, 0f, 0.15f)); // Neck throat
                    contourPoints.Add(new GlassContourPoint( 1.65f, 0.305f, 0f, 0.35f)); // Vertical upper neck
                    contourPoints.Add(new GlassContourPoint( 2.65f, 0.305f, 0f, 0.45f)); // Top neck rim
                    break;

                case GlassShapePreset.ConicalErlenmeyer:
                    // Conical Chemistry / Alchemy Lab Flask (Wide Base -> Tapered Cone)
                    symmetricMode = true;
                    showShoulderHandles = false;
                    contourPoints.Add(new GlassContourPoint(-1.20f, 0.175f, 0f, 0.4f));
                    contourPoints.Add(new GlassContourPoint(-0.80f, 0.175f, 0f, 0.2f));
                    contourPoints.Add(new GlassContourPoint(-0.55f, 0.650f, 0f, 0.0f));
                    contourPoints.Add(new GlassContourPoint(-0.38f, 1.380f, 0f, 0.1f));
                    contourPoints.Add(new GlassContourPoint(-0.15f, 1.420f, 0f, 0.1f));
                    contourPoints.Add(new GlassContourPoint( 0.40f, 0.950f, 0f, 0.2f));
                    contourPoints.Add(new GlassContourPoint( 0.95f, 0.480f, 0f, 0.2f));
                    contourPoints.Add(new GlassContourPoint( 1.20f, 0.300f, 0f, 0.2f));
                    contourPoints.Add(new GlassContourPoint( 2.60f, 0.300f, 0f, 0.4f));
                    break;

                case GlassShapePreset.DoubleBulb:
                    // Double-Chambered Gourd / Alchemist Bubble Bottle
                    symmetricMode = true;
                    showShoulderHandles = true;
                    contourPoints.Add(new GlassContourPoint(-1.20f, 0.175f, 0f, 0.4f));
                    contourPoints.Add(new GlassContourPoint(-0.82f, 0.175f, 0f, 0.2f));
                    contourPoints.Add(new GlassContourPoint(-0.52f, 0.720f, 0f, 0.0f));
                    contourPoints.Add(new GlassContourPoint(-0.18f, 1.350f, 0f, 0.0f)); // Lower large bulb
                    contourPoints.Add(new GlassContourPoint( 0.18f, 0.950f, 0f, 0.0f));
                    contourPoints.Add(new GlassContourPoint( 0.38f, 0.600f, 0f, 0.0f)); // Middle ring pinch
                    contourPoints.Add(new GlassContourPoint( 0.72f, 1.080f, 0f, 0.0f)); // Upper medium bulb
                    contourPoints.Add(new GlassContourPoint( 1.05f, 0.720f, 0f, 0.0f));
                    contourPoints.Add(new GlassContourPoint( 1.25f, 0.310f, 0f, 0.0f));
                    contourPoints.Add(new GlassContourPoint( 2.60f, 0.300f, 0f, 0.4f));
                    break;

                case GlassShapePreset.WavyAmphora:
                    // Rippled / Wavy Decorative Glass Vase
                    symmetricMode = true;
                    showShoulderHandles = true;
                    contourPoints.Add(new GlassContourPoint(-1.20f, 0.175f, 0f, 0.4f));
                    contourPoints.Add(new GlassContourPoint(-0.80f, 0.175f, 0f, 0.2f));
                    contourPoints.Add(new GlassContourPoint(-0.50f, 0.850f, 0f, 0.0f));
                    contourPoints.Add(new GlassContourPoint(-0.22f, 1.300f, 0f, 0.0f));
                    contourPoints.Add(new GlassContourPoint( 0.06f, 1.040f, 0f, 0.0f));
                    contourPoints.Add(new GlassContourPoint( 0.34f, 1.340f, 0f, 0.0f));
                    contourPoints.Add(new GlassContourPoint( 0.62f, 1.020f, 0f, 0.0f));
                    contourPoints.Add(new GlassContourPoint( 0.88f, 1.220f, 0f, 0.0f));
                    contourPoints.Add(new GlassContourPoint( 1.15f, 0.340f, 0f, 0.0f));
                    contourPoints.Add(new GlassContourPoint( 2.60f, 0.300f, 0f, 0.4f));
                    break;

                case GlassShapePreset.SCurveFunnel:
                    // Asymmetric S-Curve Winding Glass Funnel
                    symmetricMode = false;
                    showShoulderHandles = false;
                    contourPoints.Add(new GlassContourPoint(-1.20f, 0.175f,  0.00f, 0.4f));
                    contourPoints.Add(new GlassContourPoint(-0.80f, 0.185f,  0.00f, 0.2f));
                    contourPoints.Add(new GlassContourPoint(-0.48f, 0.720f, -0.18f, 0.0f));
                    contourPoints.Add(new GlassContourPoint(-0.08f, 1.150f, -0.32f, 0.0f));
                    contourPoints.Add(new GlassContourPoint( 0.35f, 1.120f,  0.28f, 0.0f));
                    contourPoints.Add(new GlassContourPoint( 0.78f, 0.920f,  0.34f, 0.0f));
                    contourPoints.Add(new GlassContourPoint( 1.12f, 0.360f,  0.05f, 0.0f));
                    contourPoints.Add(new GlassContourPoint( 2.60f, 0.300f,  0.00f, 0.4f));
                    break;

                case GlassShapePreset.SquareFlask:
                    // Hunili Kare Cam Şişe (Kare gövde, alt huni ve üst boğaz - Piksel Sanatı için ideal)
                    symmetricMode = true;
                    showShoulderHandles = false;
                    contourPoints.Add(new GlassContourPoint(-1.20f, 0.180f, 0f, 0.40f));
                    contourPoints.Add(new GlassContourPoint(-0.76f, 0.180f, 0f, 0.20f));
                    contourPoints.Add(new GlassContourPoint(-0.56f, 0.260f, 0f, 0.10f));
                    contourPoints.Add(new GlassContourPoint(-0.46f, 1.150f, 0f, 0.90f));
                    contourPoints.Add(new GlassContourPoint(-0.42f, 1.150f, 0f, 1.00f));
                    contourPoints.Add(new GlassContourPoint( 1.70f, 1.150f, 0f, 1.00f));
                    contourPoints.Add(new GlassContourPoint( 1.75f, 1.150f, 0f, 0.90f));
                    contourPoints.Add(new GlassContourPoint( 1.88f, 0.300f, 0f, 0.15f));
                    contourPoints.Add(new GlassContourPoint( 2.65f, 0.300f, 0f, 0.40f));
                    break;

                case GlassShapePreset.RectangleFlask:
                    // Hunili Dikey Dikdörtgen Cam Şişe
                    symmetricMode = true;
                    showShoulderHandles = false;
                    contourPoints.Add(new GlassContourPoint(-1.20f, 0.180f, 0f, 0.40f));
                    contourPoints.Add(new GlassContourPoint(-0.76f, 0.180f, 0f, 0.20f));
                    contourPoints.Add(new GlassContourPoint(-0.56f, 0.240f, 0f, 0.10f));
                    contourPoints.Add(new GlassContourPoint(-0.46f, 0.820f, 0f, 0.90f));
                    contourPoints.Add(new GlassContourPoint(-0.42f, 0.820f, 0f, 1.00f));
                    contourPoints.Add(new GlassContourPoint( 1.90f, 0.820f, 0f, 1.00f));
                    contourPoints.Add(new GlassContourPoint( 1.95f, 0.820f, 0f, 0.90f));
                    contourPoints.Add(new GlassContourPoint( 2.08f, 0.280f, 0f, 0.15f));
                    contourPoints.Add(new GlassContourPoint( 2.65f, 0.280f, 0f, 0.40f));
                    break;

                case GlassShapePreset.WideRectangleFlask:
                    // Hunili Geniş Yatay Dikdörtgen Cam Şişe
                    symmetricMode = true;
                    showShoulderHandles = false;
                    contourPoints.Add(new GlassContourPoint(-1.20f, 0.180f, 0f, 0.40f));
                    contourPoints.Add(new GlassContourPoint(-0.76f, 0.180f, 0f, 0.20f));
                    contourPoints.Add(new GlassContourPoint(-0.56f, 0.280f, 0f, 0.10f));
                    contourPoints.Add(new GlassContourPoint(-0.46f, 1.500f, 0f, 0.90f));
                    contourPoints.Add(new GlassContourPoint(-0.42f, 1.500f, 0f, 1.00f));
                    contourPoints.Add(new GlassContourPoint( 1.30f, 1.500f, 0f, 1.00f));
                    contourPoints.Add(new GlassContourPoint( 1.35f, 1.500f, 0f, 0.90f));
                    contourPoints.Add(new GlassContourPoint( 1.50f, 0.320f, 0f, 0.15f));
                    contourPoints.Add(new GlassContourPoint( 2.65f, 0.320f, 0f, 0.40f));
                    break;

                case GlassShapePreset.SquareBox:
                case GlassShapePreset.RectangleBox:
                    // Reference Image 2: Wide Rounded-Rectangle Periwinkle-Framed Sky-Blue Glass Ball Pool (11*D = 2.64m inner width)
                    symmetricMode = true;
                    showShoulderHandles = false;
                    contourPoints.Add(new GlassContourPoint(-0.56f, 1.320f, 0f, 1.00f));
                    contourPoints.Add(new GlassContourPoint(-0.52f, 1.320f, 0f, 1.00f));
                    contourPoints.Add(new GlassContourPoint( 0.58f, 1.320f, 0f, 1.00f));
                    contourPoints.Add(new GlassContourPoint( 0.62f, 1.320f, 0f, 1.00f));
                    break;
            }

            SyncBoundsFromContour();
            if (rebuildImmediate)
            {
                RebuildContainer();
            }
        }

        /// <summary>
        /// Parametrik olarak çerçeveli yuvarlak-köşeli dikdörtgen/kare havuz camını (Rounded Box Pool) yapılandırır.
        /// </summary>
        public void ConfigureRoundedBoxPool(
            float innerWidth = 2.64f,
            float innerHeight = 1.18f,
            float cornerRadius = 0.22f,
            float frameThickness = 0.115f,
            float bottomY = -0.56f,
            bool rebuildImmediate = true)
        {
            if (contourPoints == null) contourPoints = new List<GlassContourPoint>();
            contourPoints.Clear();

            float clampedW = Mathf.Clamp(innerWidth, 0.80f, 3.40f);
            float clampedH = Mathf.Clamp(innerHeight, 0.60f, 2.80f);
            float hw = clampedW * 0.5f;
            float topY = bottomY + clampedH;

            shapePreset = Mathf.Abs(clampedW - clampedH) < 0.12f
                ? GlassShapePreset.SquareBox
                : GlassShapePreset.RectangleBox;
            symmetricMode = true;
            widthScale = 1.0f;
            heightScale = 1.0f;
            showShoulderHandles = false;
            boxCornerRadius = Mathf.Clamp(cornerRadius, 0.04f, 0.65f);
            boxFrameThickness = Mathf.Clamp(frameThickness, 0.04f, 0.24f);

            contourPoints.Add(new GlassContourPoint(bottomY, hw, 0f, 1.00f));
            contourPoints.Add(new GlassContourPoint(bottomY + 0.04f, hw, 0f, 1.00f));
            contourPoints.Add(new GlassContourPoint(topY - 0.04f, hw, 0f, 1.00f));
            contourPoints.Add(new GlassContourPoint(topY, hw, 0f, 1.00f));

            SyncBoundsFromContour();
            if (rebuildImmediate)
            {
                RebuildContainer();
            }
        }

        public void GetRoundedBoxPoolParameters(out float innerWidth, out float innerHeight, out float bottomY)
        {
            EnsureContourInitialized();
            bottomY = GetChamberFloorY();
            float topY = (contourPoints != null && contourPoints.Count > 0)
                ? contourPoints[contourPoints.Count - 1].y * heightScale
                : bottomY + 1.18f;
            float hw = (contourPoints != null && contourPoints.Count > 0)
                ? contourPoints[0].halfWidth * widthScale
                : 1.32f;

            innerWidth = Mathf.Max(0.60f, hw * 2f);
            innerHeight = Mathf.Max(0.40f, topY - bottomY);
        }

        /// <summary>
        /// Parametrik olarak istenilen genişlik, yükseklik ve köşe yuvarlamasında Kare veya Dikdörtgen cam oluşturur.
        /// </summary>
        public void ConfigureRectangularContainer(
            float width = 2.2f,
            float height = 2.1f,
            float cornerRadius = 0.90f,
            bool hasSpout = true,
            bool hasNeck = true,
            bool rebuildImmediate = true)
        {
            if (contourPoints == null) contourPoints = new List<GlassContourPoint>();
            contourPoints.Clear();
            symmetricMode = true;
            showShoulderHandles = false;
            shapePreset = GlassShapePreset.CustomFreeform;

            float hw = Mathf.Max(0.40f, width * 0.5f);
            float bottomY = -0.45f;
            float topY = bottomY + Mathf.Max(0.6f, height);
            float tension = Mathf.Clamp01(cornerRadius);

            if (hasSpout)
            {
                contourPoints.Add(new GlassContourPoint(-1.20f, 0.180f, 0f, 0.40f));
                contourPoints.Add(new GlassContourPoint(-0.76f, 0.180f, 0f, 0.20f));
                contourPoints.Add(new GlassContourPoint(-0.56f, 0.260f, 0f, 0.10f));
                contourPoints.Add(new GlassContourPoint(bottomY - 0.04f, hw, 0f, tension));
                contourPoints.Add(new GlassContourPoint(bottomY, hw, 0f, 1.0f));
            }
            else
            {
                contourPoints.Add(new GlassContourPoint(bottomY - 0.05f, hw, 0f, 1.0f));
                contourPoints.Add(new GlassContourPoint(bottomY, hw, 0f, 1.0f));
            }

            if (hasNeck)
            {
                contourPoints.Add(new GlassContourPoint(topY, hw, 0f, 1.0f));
                contourPoints.Add(new GlassContourPoint(topY + 0.05f, hw, 0f, tension));
                contourPoints.Add(new GlassContourPoint(topY + 0.18f, 0.30f, 0f, 0.15f));
                contourPoints.Add(new GlassContourPoint(2.65f, 0.30f, 0f, 0.40f));
            }
            else
            {
                contourPoints.Add(new GlassContourPoint(topY, hw, 0f, 1.0f));
                contourPoints.Add(new GlassContourPoint(topY + 0.05f, hw, 0f, 1.0f));
            }

            SyncBoundsFromContour();
            if (rebuildImmediate)
            {
                RebuildContainer();
            }
        }

        public void ApplyColorTheme(GlassColorTheme theme, bool rebuildImmediate = true)
        {
            colorTheme = theme;
            switch (theme)
            {
                case GlassColorTheme.SkyBlueGlossy:
                default:
                    glassBodyColor = new Color(0.36f, 0.71f, 0.98f, 0.78f);
                    glassCenterGlowColor = new Color(0.76f, 0.91f, 1.00f, 0.20f);
                    glassOuterRimColor = new Color(0.17f, 0.49f, 0.85f, 0.92f);
                    glassInnerRimColor = new Color(0.96f, 0.99f, 1.00f, 0.95f);
                    glassFrontLensColor = new Color(0.95f, 0.99f, 1.00f, 0.00f);
                    earHandleColor = new Color(0.96f, 0.98f, 1.00f, 1.00f);
                    break;

                case GlassColorTheme.CrystalClear:
                    glassBodyColor = new Color(0.76f, 0.91f, 1.00f, 0.34f);
                    glassCenterGlowColor = new Color(0.95f, 0.99f, 1.00f, 0.10f);
                    glassOuterRimColor = new Color(0.68f, 0.86f, 0.98f, 0.72f);
                    glassInnerRimColor = new Color(0.96f, 0.99f, 1.00f, 0.52f);
                    glassFrontLensColor = new Color(0.96f, 0.99f, 1.00f, 0.08f);
                    earHandleColor = new Color(0.97f, 0.99f, 1.00f, 1.00f);
                    break;

                case GlassColorTheme.EmeraldGreen:
                    glassBodyColor = new Color(0.20f, 0.86f, 0.72f, 0.46f);
                    glassCenterGlowColor = new Color(0.82f, 0.99f, 0.94f, 0.14f);
                    glassOuterRimColor = new Color(0.18f, 0.80f, 0.66f, 0.80f);
                    glassInnerRimColor = new Color(0.84f, 0.99f, 0.94f, 0.54f);
                    glassFrontLensColor = new Color(0.90f, 1.00f, 0.96f, 0.08f);
                    earHandleColor = new Color(0.94f, 0.99f, 0.97f, 1.00f);
                    break;

                case GlassColorTheme.RoseQuartz:
                    glassBodyColor = new Color(0.98f, 0.50f, 0.78f, 0.48f);
                    glassCenterGlowColor = new Color(1.00f, 0.88f, 0.95f, 0.15f);
                    glassOuterRimColor = new Color(0.95f, 0.44f, 0.74f, 0.80f);
                    glassInnerRimColor = new Color(1.00f, 0.88f, 0.95f, 0.55f);
                    glassFrontLensColor = new Color(1.00f, 0.94f, 0.98f, 0.08f);
                    earHandleColor = new Color(1.00f, 0.96f, 0.98f, 1.00f);
                    break;

                case GlassColorTheme.AmethystPurple:
                    glassBodyColor = new Color(0.66f, 0.48f, 0.98f, 0.48f);
                    glassCenterGlowColor = new Color(0.90f, 0.84f, 1.00f, 0.15f);
                    glassOuterRimColor = new Color(0.60f, 0.42f, 0.95f, 0.80f);
                    glassInnerRimColor = new Color(0.90f, 0.86f, 1.00f, 0.55f);
                    glassFrontLensColor = new Color(0.94f, 0.90f, 1.00f, 0.08f);
                    earHandleColor = new Color(0.96f, 0.94f, 1.00f, 1.00f);
                    break;

                case GlassColorTheme.AmberGold:
                    glassBodyColor = new Color(1.00f, 0.76f, 0.28f, 0.48f);
                    glassCenterGlowColor = new Color(1.00f, 0.95f, 0.82f, 0.15f);
                    glassOuterRimColor = new Color(0.96f, 0.68f, 0.20f, 0.80f);
                    glassInnerRimColor = new Color(1.00f, 0.94f, 0.80f, 0.55f);
                    glassFrontLensColor = new Color(1.00f, 0.97f, 0.88f, 0.08f);
                    earHandleColor = new Color(1.00f, 0.98f, 0.92f, 1.00f);
                    break;
            }

            if (rebuildImmediate)
            {
                RebuildContainer();
            }
        }

        [ContextMenu("Rebuild Container")]
        public void RebuildContainer()
        {
            EnsureContourInitialized();
            SyncBoundsFromContour();

            if (_bottleCubes != null)
            {
                for (int i = 0; i < _bottleCubes.Count; i++)
                {
                    if (_bottleCubes[i]?.gameObject != null)
                    {
                        _bottleCubes[i].gameObject.transform.DOKill();
                    }
                }
                _bottleCubes.Clear();
            }
            else
            {
                _bottleCubes = new List<BottleCube>();
            }

            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                if (Application.isPlaying)
                    Destroy(transform.GetChild(i).gameObject);
                else
                    DestroyImmediate(transform.GetChild(i).gameObject);
            }

            EnsureMaterials();
            BuildBottleContainer();

            // Synchronize airtight physics colliders automatically
            var bcm = GetComponent<BallContainerManager>().OrNull() ?? FindFirstObjectByType<BallContainerManager>();
            if (bcm != null)
            {
                bcm.RebuildColliders();
            }
        }

        /// <summary>
        /// Rebuilds only the 3D glass mesh and walls without destroying/respawning the particles,
        /// then settles existing particles into the updated glass contour! Ideal for fast real-time dragging in Scene View.
        /// </summary>
        public void RebuildGlassMeshAndSettleParticles()
        {
            EnsureContourInitialized();
            SyncBoundsFromContour();
            EnsureMaterials();

            if (_trackRootObj == null || _glassWallsObj == null || _cubesContainer == null)
            {
                RebuildContainer();
                return;
            }

            for (int i = _glassWallsObj.transform.childCount - 1; i >= 0; i--)
            {
                if (Application.isPlaying)
                    Destroy(_glassWallsObj.transform.GetChild(i).gameObject);
                else
                    DestroyImmediate(_glassWallsObj.transform.GetChild(i).gameObject);
            }

            if (spoutExitPoint != null)
            {
                spoutExitPoint.localPosition = new Vector3(
                    contourPoints[0].centerX * widthScale,
                    spoutBottomY - 0.04f,
                    -0.10f);
            }

            BuildSpline();
            BuildFlaskGeometry();
            RecomputeLaminarTargets(true);
            SyncCubeTransforms();

            // Synchronize airtight physics colliders automatically
            var bcm = GetComponent<BallContainerManager>().OrNull() ?? FindFirstObjectByType<BallContainerManager>();
            if (bcm != null)
            {
                bcm.RebuildColliders();
            }
        }

        private Material CreateConfiguredGlassMaterial(string matName, Color col, float smoothness, int renderQueue)
        {
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material m = (glassMaterial != null) ? new Material(glassMaterial) : new Material(litShader);
            m.name = matName;
            m.color = col;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
            if (m.HasProperty("_Color")) m.SetColor("_Color", col);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = renderQueue;
            return m;
        }

        private void ApplyRoundedBoxShaderProperties(Material mat, float boxAspect)
        {
            if (mat == null) return;
            mat.SetFloat("_IsRoundedBoxPool", IsFlatBottomBox ? 1.0f : 0.0f);
            mat.SetFloat("_BoxAspect", boxAspect);
            if (mat.HasProperty("_BoxCornerRadius")) mat.SetFloat("_BoxCornerRadius", Mathf.Clamp(boxCornerRadius, 0.04f, 0.65f));
            if (mat.HasProperty("_FrameThickness")) mat.SetFloat("_FrameThickness", Mathf.Clamp(boxFrameThickness, 0.04f, 0.24f));
            if (mat.HasProperty("_FrameColor")) mat.SetColor("_FrameColor", boxFrameColor);
            if (mat.HasProperty("_FrameHighlight")) mat.SetColor("_FrameHighlight", boxFrameHighlightColor);
            if (mat.HasProperty("_FrameEdgeShade")) mat.SetColor("_FrameEdgeShade", boxFrameEdgeShadeColor);
            if (mat.HasProperty("_FakeShadowColor")) mat.SetColor("_FakeShadowColor", dropShadowColor);
        }

        private void EnsureMaterials()
        {
#if UNITY_EDITOR
            if (glassMaterial == null)
                glassMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Mat_GlassPane.mat");
            if (glassRimMaterial == null)
                glassRimMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Mat_GlassHighlight.mat");
#endif
            Shader chamberShader = Shader.Find("PuzzleGlass/GlassChamberLit");
            Shader shadowShader = Shader.Find("PuzzleGlass/FakeDropShadow");

            // 1. Soft Airy Hypercasual Pastel Fake Drop Shadow Material
            if (shadowShader != null)
            {
                _glassFakeShadowMat = new Material(shadowShader);
                _glassFakeShadowMat.name = "Mat_FlaskFakeDropShadow";
                _glassFakeShadowMat.SetColor("_ShadowColor", new Color(0.76f, 0.62f, 0.48f, 0.19f));
                _glassFakeShadowMat.SetFloat("_FeatherWidth", 0.56f);
                _glassFakeShadowMat.SetFloat("_IsRadial", 0f);
            }
            else
            {
                _glassFakeShadowMat = CreateConfiguredGlassMaterial(
                    "Mat_FlaskFakeDropShadow",
                    new Color(0.76f, 0.62f, 0.48f, 0.16f),
                    0.1f,
                    2970);
            }

            // 2. Crystal-Clear Hypercasual Glass Chamber Interior (airy transparent center + Fresnel tinted edges)
            Color backTint = glassBodyColor;
            backTint.a = Mathf.Clamp(backTint.a * 0.28f, 0.10f, 0.22f);
            _chamberBackGlassMat = CreateConfiguredGlassMaterial(
                "Mat_FlaskSkyBlueInterior",
                backTint,
                0.98f,
                2985);

            float wallThickWorld = Mathf.Clamp(innerRimInset + glassRimRadius * 0.52f, 0.076f, 0.102f);
            float sheenStrength = showSpecularReflections ? Mathf.Clamp(specularIntensity * 1.18f, 0.35f, 1.25f) : 0.0f;

            Color outerCobaltCol = glassOuterRimColor;
            outerCobaltCol.a = Mathf.Clamp(outerCobaltCol.a, 0.85f, 0.96f);

            Color innerGrooveCol = Color.Lerp(glassOuterRimColor, glassBodyColor, 0.28f);
            innerGrooveCol.a = 0.88f;

            Color rimHighlightCol = glassInnerRimColor;
            rimHighlightCol.a = Mathf.Clamp(rimHighlightCol.a, 0.88f, 0.98f);

            if (chamberShader != null)
            {
                _chamberInnerGlowMat = new Material(chamberShader);
                _chamberInnerGlowMat.name = "Mat_FlaskRefractiveChamber";
                Color centerCol = glassCenterGlowColor;
                centerCol.a = Mathf.Clamp(glassCenterGlowColor.a, 0.14f, 0.24f);
                Color edgeCol = glassBodyColor;
                edgeCol.a = Mathf.Clamp(glassBodyColor.a, 0.62f, 0.82f);
                _chamberInnerGlowMat.SetColor("_CenterColor", centerCol);
                _chamberInnerGlowMat.SetColor("_EdgeColor", edgeCol);
                _chamberInnerGlowMat.SetColor("_OuterRimColor", outerCobaltCol);
                _chamberInnerGlowMat.SetColor("_InnerGrooveColor", innerGrooveCol);
                _chamberInnerGlowMat.SetColor("_RimGlowColor", rimHighlightCol);
                _chamberInnerGlowMat.SetFloat("_WallThickness", wallThickWorld);
                _chamberInnerGlowMat.SetFloat("_SheenIntensity", sheenStrength);
                _chamberInnerGlowMat.SetFloat("_IsFrontLens", 0.0f);
                ApplyRoundedBoxShaderProperties(_chamberInnerGlowMat, 1.97975f);
                // Render back glass chamber before 3D balls (Geometry+15 = 2015) so balls inside the pool are never occluded
                _chamberInnerGlowMat.renderQueue = 1998;
            }
            else
            {
                _chamberInnerGlowMat = CreateConfiguredGlassMaterial(
                    "Mat_FlaskCenterGlow",
                    glassCenterGlowColor,
                    0.98f,
                    1998);
            }

            // Internal Glass Compartment Divider Materials (Periwinkle outer frame + Icy sky-blue inner glass bar)
            Color divOuterCol = new Color(0.48f, 0.48f, 0.86f, 1.0f);
            _dividerOuterRimMat = PuzzleColorPalette.CreateToonyHypercasualMaterial(
                divOuterCol,
                new Color(0.58f, 0.60f, 0.80f, 1f),
                "Mat_GlassDividerOuterRim",
                glossyCandy: true,
                indirectIntensity: 0.18f);
            _dividerOuterRimMat.renderQueue = 2018;

            Color divInnerCol = new Color(0.80f, 0.89f, 0.96f, 1.0f);
            _dividerInnerCoreMat = PuzzleColorPalette.CreateToonyHypercasualMaterial(
                divInnerCol,
                new Color(0.68f, 0.74f, 0.86f, 1f),
                "Mat_GlassDividerInnerCore",
                glossyCandy: true,
                indirectIntensity: 0.18f);
            _dividerInnerCoreMat.renderQueue = 2019;

            // 3. Sleek Translucent Hypercasual Glass Border Rim Materials
            Color outerTranslucentRim = glassOuterRimColor;
            outerTranslucentRim.a = Mathf.Clamp(outerTranslucentRim.a, 0.68f, 0.80f);
            _outerBlueRimMat = CreateConfiguredGlassMaterial(
                "Mat_FlaskOuterBlueRim",
                outerTranslucentRim,
                0.98f,
                3008);

            _outerIcyHighlightMat = CreateConfiguredGlassMaterial(
                "Mat_FlaskOuterIcyEdge",
                new Color(0.96f, 0.99f, 1.00f, 0.65f),
                1.00f,
                3014);

            Color deepGrooveCol = Color.Lerp(glassOuterRimColor, new Color(0.24f, 0.62f, 0.96f, 0.45f), 0.50f);
            deepGrooveCol.a = 0.44f;
            _innerDeepGrooveMat = CreateConfiguredGlassMaterial(
                "Mat_FlaskInnerDeepGroove",
                deepGrooveCol,
                0.98f,
                3010);

            Color innerRimCol = glassInnerRimColor;
            innerRimCol.a = Mathf.Clamp(innerRimCol.a, 0.48f, 0.58f);
            _innerCyanRimMat = CreateConfiguredGlassMaterial(
                "Mat_FlaskInnerCyanRim",
                innerRimCol,
                0.99f,
                3015);

            if (chamberShader != null)
            {
                _frontGlassLensMat = new Material(chamberShader);
                _frontGlassLensMat.name = "Mat_FlaskFrontGlassLens";
                Color frontCenter = new Color(0.95f, 0.99f, 1.00f, 0.00f);
                Color frontEdge = glassBodyColor;
                frontEdge.a = Mathf.Clamp(glassBodyColor.a, 0.68f, 0.84f);
                _frontGlassLensMat.SetColor("_CenterColor", frontCenter);
                _frontGlassLensMat.SetColor("_EdgeColor", frontEdge);
                _frontGlassLensMat.SetColor("_OuterRimColor", outerCobaltCol);
                _frontGlassLensMat.SetColor("_InnerGrooveColor", innerGrooveCol);
                _frontGlassLensMat.SetColor("_RimGlowColor", rimHighlightCol);
                _frontGlassLensMat.SetFloat("_WallThickness", wallThickWorld);
                _frontGlassLensMat.SetFloat("_SheenIntensity", sheenStrength);
                _frontGlassLensMat.SetFloat("_IsFrontLens", 1.0f);
                ApplyRoundedBoxShaderProperties(_frontGlassLensMat, 1.97975f);
                _frontGlassLensMat.renderQueue = 3020;
            }
            else
            {
                Color frontLensTint = glassFrontLensColor;
                frontLensTint.a = Mathf.Clamp(frontLensTint.a, 0.05f, 0.09f);
                _frontGlassLensMat = CreateConfiguredGlassMaterial(
                    "Mat_FlaskFrontGlassLens",
                    frontLensTint,
                    0.99f,
                    3020);
            }

            _specularStreakMat = CreateConfiguredGlassMaterial(
                "Mat_FlaskSpecularHighlight",
                new Color(1.00f, 1.00f, 1.00f, Mathf.Clamp(specularIntensity * 0.78f, 0.38f, 0.62f)),
                1.00f,
                3022);

            // 4. Creamy-White Shoulder Cap + Purple (Left) & Golden-Yellow (Right) Pill Inserts (Image 1 1-to-1!)
            Color capWhite = new Color(0.93f, 0.92f, 0.90f, 1.0f);
            _earHandleMat = PuzzleColorPalette.CreateToonyHypercasualMaterial(
                capWhite,
                new Color(0.74f, 0.75f, 0.82f, 1f),
                "Mat_FlaskShoulderCapWhite",
                glossyCandy: false,
                indirectIntensity: 0.16f);

            _leftEarPillMat = PuzzleColorPalette.CreateCandyCubeMaterial(PuzzleColor.Purple, "Mat_LeftEarPurplePill");
            _rightEarPillMat = PuzzleColorPalette.CreateCandyCubeMaterial(PuzzleColor.Yellow, "Mat_RightEarYellowPill");

            foreach (PuzzleColor col in Enum.GetValues(typeof(PuzzleColor)))
            {
                _cubeMaterials[col] = PuzzleColorPalette.CreateCandyCubeMaterial(col, $"Mat_Bead_{col}");
            }

            float clampedBeadSize = Mathf.Clamp(particleSize, 0.08f, 0.22f);
            _roundedCubeMesh = ProceduralMeshUtils.CreateRoundedCube(clampedBeadSize, clampedBeadSize * 0.28f, 4);
        }

        private void BuildBottleContainer()
        {
            _trackRootObj = new GameObject("GlassTrackRoot");
            _trackRootObj.transform.SetParent(transform, false);
            _trackRootObj.transform.localPosition = Vector3.zero;
            _trackRootObj.transform.localRotation = Quaternion.identity;

            _glassWallsObj = new GameObject("GlassWalls");
            _glassWallsObj.transform.SetParent(_trackRootObj.transform, false);

            _cubesContainer = new GameObject("CubesInBottle");
            _cubesContainer.transform.SetParent(_trackRootObj.transform, false);

            float spoutCenterX = (contourPoints != null && contourPoints.Count > 0) ? contourPoints[0].centerX * widthScale : 0f;
            GameObject spoutObj = new GameObject("SpoutExitPoint");
            spoutObj.transform.SetParent(_trackRootObj.transform, false);
            spoutObj.transform.localPosition = new Vector3(spoutCenterX, spoutBottomY - 0.04f, -0.10f);
            spoutExitPoint = spoutObj.transform;

            BuildSpline();
            BuildFlaskGeometry();
            PopulateBottlePhysics();
        }

        public void GetWallBoundsAtY(float y, out float leftX, out float rightX)
        {
            if (!_lutValid)
            {
                GenerateFlaskPaths(out _, out _, out _, out _);
            }

            float t = Mathf.InverseLerp(_lutMinY, _lutMaxY, y) * (LUT_SIZE - 1);
            int idx0 = Mathf.Clamp(Mathf.FloorToInt(t), 0, LUT_SIZE - 1);
            int idx1 = Mathf.Clamp(idx0 + 1, 0, LUT_SIZE - 1);
            float frac = t - idx0;

            leftX = Mathf.Lerp(_lutLeftX[idx0], _lutLeftX[idx1], frac);
            rightX = Mathf.Lerp(_lutRightX[idx0], _lutRightX[idx1], frac);
        }

        /// <summary>
        /// Returns the 2D Minkowski-eroded safe horizontal bounds [safeLeftX, safeRightX] at height y
        /// such that a 3D cube centered at (x, y) never touches or crosses any sloped/curved inner glass wall!
        /// </summary>
        public void GetSafeCubeBoundsAtY(float y, out float safeLeftX, out float safeRightX)
        {
            if (!_lutValid)
            {
                GenerateFlaskPaths(out _, out _, out _, out _);
            }

            float t = Mathf.InverseLerp(_lutMinY, _lutMaxY, y) * (LUT_SIZE - 1);
            int idx0 = Mathf.Clamp(Mathf.FloorToInt(t), 0, LUT_SIZE - 1);
            int idx1 = Mathf.Clamp(idx0 + 1, 0, LUT_SIZE - 1);
            float frac = t - idx0;

            safeLeftX = Mathf.Lerp(_lutSafeLeftX[idx0], _lutSafeLeftX[idx1], frac);
            safeRightX = Mathf.Lerp(_lutSafeRightX[idx0], _lutSafeRightX[idx1], frac);
        }

        public float GetHalfWidthAtY(float y)
        {
            GetWallBoundsAtY(y, out float leftX, out float rightX);
            return Mathf.Max(0.06f, (rightX - leftX) * 0.5f);
        }

        private void BuildFlaskGeometry()
        {
            GenerateFlaskPaths(
                out Vector3[] leftPath,
                out Vector3[] rightPath,
                out Vector3[] leftInnerPath,
                out Vector3[] rightInnerPath);

            cachedLeftOuterPath = leftPath;
            cachedRightOuterPath = rightPath;
            cachedLeftInnerPath = leftInnerPath;
            cachedRightInnerPath = rightInnerPath;

            if (IsFlatBottomBox)
            {
                GetRoundedBoxPoolExtents(out float centerY, out float quadHalfW, out float quadHalfH, out _, out _);
                float boxAspect = quadHalfW / Mathf.Max(0.01f, quadHalfH);

                if (_chamberInnerGlowMat != null)
                {
                    ApplyRoundedBoxShaderProperties(_chamberInnerGlowMat, boxAspect);
                }
                if (_frontGlassLensMat != null)
                {
                    ApplyRoundedBoxShaderProperties(_frontGlassLensMat, boxAspect);
                }

                Mesh boxQuadMesh = CreateRoundedBoxPoolQuadMesh(quadHalfW, quadHalfH, centerY);

                GameObject poolBackObj = new GameObject("FlaskInnerCenterGlow");
                poolBackObj.transform.SetParent(_glassWallsObj.transform, false);
                poolBackObj.transform.localPosition = new Vector3(0f, 0f, 0.08f);
                poolBackObj.AddComponent<MeshFilter>().sharedMesh = boxQuadMesh;
                var poolBackMr = poolBackObj.AddComponent<MeshRenderer>();
                poolBackMr.sharedMaterial = _chamberInnerGlowMat;
                poolBackMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                poolBackMr.receiveShadows = false;

                GameObject poolFrontObj = new GameObject("FlaskFrontGlassLens");
                poolFrontObj.transform.SetParent(_glassWallsObj.transform, false);
                poolFrontObj.transform.localPosition = new Vector3(0f, 0f, -0.24f);
                poolFrontObj.AddComponent<MeshFilter>().sharedMesh = boxQuadMesh;
                var poolFrontMr = poolFrontObj.AddComponent<MeshRenderer>();
                poolFrontMr.sharedMaterial = _frontGlassLensMat;
                poolFrontMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                poolFrontMr.receiveShadows = false;

                BuildGlassDividersGeometry();
                return;
            }

            // 0. Soft Feathered Warm Fake Drop Shadow behind the entire custom glass contour
            BuildGlassFakeDropShadow(leftPath, rightPath);

            // 1. Crystal-Clear Blown-Glass Back Chamber & Fresnel Interior (placed behind the 3D cubes at z = +0.08f)
            Mesh fullVesselRibbonMesh = ProceduralMeshUtils.CreateTrackFloorRibbon(leftPath, rightPath);

            GameObject glowObj = new GameObject("FlaskInnerCenterGlow");
            glowObj.transform.SetParent(_glassWallsObj.transform, false);
            glowObj.transform.localPosition = new Vector3(0f, 0f, 0.08f);
            glowObj.AddComponent<MeshFilter>().sharedMesh = fullVesselRibbonMesh;
            var glowMr = glowObj.AddComponent<MeshRenderer>();
            glowMr.sharedMaterial = _chamberInnerGlowMat;
            glowMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // 2. Continuous Optical Blown-Glass Front Wall, Lobe Crescents, Neck/Spout Streaks & 3D Spout Mouth Ring
            //    (placed in front of the 3D cubes at z = -0.24f with crystal-clear interior so cubes stay 100% vibrant)
            GameObject frontLensObj = new GameObject("FlaskFrontGlassLens");
            frontLensObj.transform.SetParent(_glassWallsObj.transform, false);
            frontLensObj.transform.localPosition = new Vector3(0f, 0f, -0.24f);
            frontLensObj.AddComponent<MeshFilter>().sharedMesh = fullVesselRibbonMesh;
            var frontLensMr = frontLensObj.AddComponent<MeshRenderer>();
            frontLensMr.sharedMaterial = _frontGlassLensMat;
            frontLensMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // 3. Thick 3D Tubular Glass Rims & Perimeter Sidewalls (Outer Blue Glossy Borders matching Reference Image!)
            Material rimMat = _outerBlueRimMat.OrNull() ?? glassRimMaterial;
            float frontZ = -0.24f;
            float backZ = 0.08f;

            // 3a. Left Perimeter Sidewall (Front to Back)
            GameObject leftWallObj = new GameObject("LeftGlassPerimeterWall");
            leftWallObj.transform.SetParent(_glassWallsObj.transform, false);
            leftWallObj.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshUtils.CreatePerimeterWall(leftPath, frontZ, backZ, false);
            var leftWallMr = leftWallObj.AddComponent<MeshRenderer>();
            leftWallMr.sharedMaterial = rimMat;
            leftWallMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // 3b. Right Perimeter Sidewall (Front to Back)
            GameObject rightWallObj = new GameObject("RightGlassPerimeterWall");
            rightWallObj.transform.SetParent(_glassWallsObj.transform, false);
            rightWallObj.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshUtils.CreatePerimeterWall(rightPath, frontZ, backZ, true);
            var rightWallMr = rightWallObj.AddComponent<MeshRenderer>();
            rightWallMr.sharedMaterial = rimMat;
            rightWallMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // 3c. Left Front Rounded Glass Rim Pipe
            GameObject leftFrontRim = new GameObject("LeftFrontGlassRim");
            leftFrontRim.transform.SetParent(_glassWallsObj.transform, false);
            leftFrontRim.transform.localPosition = new Vector3(0f, 0f, frontZ);
            leftFrontRim.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshUtils.CreatePipeMesh(leftPath, glassRimRadius, 10, false);
            var lfmr = leftFrontRim.AddComponent<MeshRenderer>();
            lfmr.sharedMaterial = rimMat;
            lfmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // 3d. Right Front Rounded Glass Rim Pipe
            GameObject rightFrontRim = new GameObject("RightFrontGlassRim");
            rightFrontRim.transform.SetParent(_glassWallsObj.transform, false);
            rightFrontRim.transform.localPosition = new Vector3(0f, 0f, frontZ);
            rightFrontRim.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshUtils.CreatePipeMesh(rightPath, glassRimRadius, 10, false);
            var rfmr = rightFrontRim.AddComponent<MeshRenderer>();
            rfmr.sharedMaterial = rimMat;
            rfmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // 3e. Left Back Rounded Glass Rim Pipe
            GameObject leftBackRim = new GameObject("LeftBackGlassRim");
            leftBackRim.transform.SetParent(_glassWallsObj.transform, false);
            leftBackRim.transform.localPosition = new Vector3(0f, 0f, backZ);
            leftBackRim.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshUtils.CreatePipeMesh(leftPath, glassRimRadius * 0.82f, 8, false);
            var lbmr = leftBackRim.AddComponent<MeshRenderer>();
            lbmr.sharedMaterial = rimMat;
            lbmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // 3f. Right Back Rounded Glass Rim Pipe
            GameObject rightBackRim = new GameObject("RightBackGlassRim");
            rightBackRim.transform.SetParent(_glassWallsObj.transform, false);
            rightBackRim.transform.localPosition = new Vector3(0f, 0f, backZ);
            rightBackRim.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshUtils.CreatePipeMesh(rightPath, glassRimRadius * 0.82f, 8, false);
            var rbmr = rightBackRim.AddComponent<MeshRenderer>();
            rbmr.sharedMaterial = rimMat;
            rbmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // 4. Glossy Specular Shoulder Highlights
            if (showSpecularReflections)
            {
                BuildDomeSpecularReflections(leftInnerPath, rightInnerPath);
            }

            // 5. Optional Creamy-White Shoulder Caps with Colored Pill Buttons (Left=Purple, Right=Yellow)
            if (showShoulderHandles)
            {
                BuildShoulderHandleEar(true, leftPath);
                BuildShoulderHandleEar(false, rightPath);
            }

            // 6. Internal Glass Compartment Dividers
            BuildGlassDividersGeometry();
        }

        private readonly List<Transform> _dividerVisualRoots = new List<Transform>();

        /// <summary>
        /// Builds 3D glossy rounded glass divider bars inside the container.
        /// </summary>
        private void BuildGlassDividersGeometry()
        {
            _dividerVisualRoots.Clear();
            if (dividers == null || dividers.Count == 0 || _glassWallsObj == null) return;

            for (int i = 0; i < dividers.Count; i++)
            {
                var div = dividers[i];
                if (div == null || !div.enabled || div.runtimeIsOpened || div.mechanicType != DividerMechanicType.StaticGlass)
                {
                    _dividerVisualRoots.Add(null);
                    continue;
                }

                float len = Mathf.Max(0.10f, div.length);
                float thick = Mathf.Max(0.035f, div.thickness);
                float cornerR = Mathf.Min(thick * 0.48f, len * 0.25f);

                GameObject divRoot = new GameObject($"GlassDivider_{i + 1}_{div.name}");
                divRoot.transform.SetParent(_glassWallsObj.transform, false);
                divRoot.transform.localPosition = new Vector3(div.center.x, div.center.y, -0.10f);
                divRoot.transform.localRotation = Quaternion.Euler(0f, 0f, div.angleDegrees);
                _dividerVisualRoots.Add(divRoot.transform);

                Material outerMatToUse = _dividerOuterRimMat != null ? _dividerOuterRimMat : _outerBlueRimMat;
                Material innerMatToUse = _dividerInnerCoreMat != null ? _dividerInnerCoreMat : _innerCyanRimMat;

                // Outer glossy frame bar
                Mesh outerBarMesh = ProceduralMeshUtils.CreatePillBadge(len, thick, 0.14f, cornerR);
                divRoot.AddComponent<MeshFilter>().sharedMesh = outerBarMesh;
                var outerMr = divRoot.AddComponent<MeshRenderer>();
                outerMr.sharedMaterial = outerMatToUse;
                outerMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                outerMr.receiveShadows = false;

                // Inner core highlight
                float innerLen = Mathf.Max(0.03f, len - 0.022f);
                float innerThick = Mathf.Max(0.016f, thick - 0.022f);
                float innerCornerR = Mathf.Min(innerThick * 0.48f, innerLen * 0.25f);

                GameObject innerCoreObj = new GameObject("InnerGlassCore");
                innerCoreObj.transform.SetParent(divRoot.transform, false);
                innerCoreObj.transform.localPosition = new Vector3(0f, 0f, -0.008f);
                innerCoreObj.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshUtils.CreatePillBadge(innerLen, innerThick, 0.145f, innerCornerR);
                var innerMr = innerCoreObj.AddComponent<MeshRenderer>();
                innerMr.sharedMaterial = innerMatToUse;
                innerMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                innerMr.receiveShadows = false;
            }
        }

        /// <summary>
        /// Tactile Shake / Tilt mechanic: rocks the glass container left & right while applying lateral + upward
        /// velocity impulses to all resting balls inside the container to unstick wedged balls.
        /// </summary>
        public void ShakeAndTiltContainer(float intensity = 1.0f)
        {
            if (_glassWallsObj != null)
            {
                Transform visualTr = _glassWallsObj.transform;
                visualTr.DOKill(true);
                visualTr.localRotation = Quaternion.identity;
                visualTr.localPosition = Vector3.zero;

                Sequence tiltSeq = DOTween.Sequence();
                tiltSeq.Append(visualTr.DOLocalRotate(new Vector3(0f, 0f, 5.5f * intensity), 0.07f).SetEase(Ease.OutSine));
                tiltSeq.Append(visualTr.DOLocalRotate(new Vector3(0f, 0f, -5.5f * intensity), 0.11f).SetEase(Ease.InOutSine));
                tiltSeq.Append(visualTr.DOLocalRotate(new Vector3(0f, 0f, 3.2f * intensity), 0.09f).SetEase(Ease.InOutSine));
                tiltSeq.Append(visualTr.DOLocalRotate(Vector3.zero, 0.08f).SetEase(Ease.OutBack));
                tiltSeq.Join(visualTr.DOPunchPosition(new Vector3(0.09f * intensity, 0.05f * intensity, 0f), 0.32f, 16, 0.8f));
            }

            if (BallObjectPool.Instance != null && BallObjectPool.Instance.ActiveBalls != null)
            {
                var balls = BallObjectPool.Instance.ActiveBalls;
                for (int i = 0; i < balls.Count; i++)
                {
                    var b = balls[i];
                    if (b == null || b.state != BallState.InContainer || b.rb == null || b.rb.isKinematic)
                        continue;

                    b.rb.WakeUp();
                    float sideDir = (i % 2 == 0) ? 1f : -1f;
                    float vx = (sideDir * UnityEngine.Random.Range(1.35f, 2.35f) + UnityEngine.Random.Range(-0.45f, 0.45f)) * intensity;
                    float vy = UnityEngine.Random.Range(0.75f, 1.55f) * intensity;
                    b.rb.linearVelocity = new Vector3(vx, vy, 0f);
                }
            }
        }

        /// <summary>
        /// Highlights the glass container rim & center glow when Fever Mode (Streak 3+ / 2x Vacuum) is active.
        /// </summary>
        public void SetFeverVisualGlow(bool feverActive)
        {
            if (_outerBlueRimMat != null)
            {
                Color c = feverActive
                    ? Color.Lerp(glassOuterRimColor, new Color(1.0f, 0.78f, 0.18f, 1.0f), 0.65f)
                    : glassOuterRimColor;
                _outerBlueRimMat.color = c;
                if (_outerBlueRimMat.HasProperty("_BaseColor")) _outerBlueRimMat.SetColor("_BaseColor", c);
            }
            if (_chamberInnerGlowMat != null)
            {
                Color g = feverActive
                    ? Color.Lerp(glassCenterGlowColor, new Color(1.0f, 0.90f, 0.48f, 0.56f), 0.55f)
                    : glassCenterGlowColor;
                _chamberInnerGlowMat.color = g;
                if (_chamberInnerGlowMat.HasProperty("_BaseColor")) _chamberInnerGlowMat.SetColor("_BaseColor", g);
            }
        }

        public void ApplyCompartmentPreset(GlassCompartmentPreset preset, bool rebuildImmediate = true)
        {
            compartmentPreset = preset;
            if (dividers == null) dividers = new List<GlassDividerData>();
            if (compartmentZones == null) compartmentZones = new List<CompartmentColorZone>();

            if (preset != GlassCompartmentPreset.Custom)
            {
                BuildCompartmentPresetLists(preset, dividers, compartmentZones);
            }

            if (rebuildImmediate)
            {
                RebuildContainer();
            }
        }

        public static void BuildCompartmentPresetLists(
            GlassCompartmentPreset preset,
            List<GlassDividerData> outDividers,
            List<CompartmentColorZone> outZones)
        {
            if (outDividers == null || outZones == null) return;
            outDividers.Clear();
            outZones.Clear();

            switch (preset)
            {
                case GlassCompartmentPreset.None_OrderedPool:
                default:
                    // Clean 3-layer ordered horizontal pool (Row 0 Blue, Row 1 Yellow, Row 2 Red)
                    break;

                case GlassCompartmentPreset.TwoVertical_LeftRight:
                    // Central vertical glass divider splitting the pool into Left and Right compartments
                    outDividers.Add(new GlassDividerData("Orta Dikey Bölme", new Vector2(0f, -0.02f), 1.04f, 0.085f, 90f, true));
                    outZones.Add(new CompartmentColorZone("Sol Alt",  -1.0f, 0.0f, 0, PuzzleColor.Blue));
                    outZones.Add(new CompartmentColorZone("Sol Orta", -1.0f, 0.0f, 1, PuzzleColor.Yellow));
                    outZones.Add(new CompartmentColorZone("Sol Üst",  -1.0f, 0.0f, 2, PuzzleColor.Red));
                    outZones.Add(new CompartmentColorZone("Sağ Alt",   0.0f, 1.0f, 0, PuzzleColor.Green));
                    outZones.Add(new CompartmentColorZone("Sağ Orta",  0.0f, 1.0f, 1, PuzzleColor.Red));
                    outZones.Add(new CompartmentColorZone("Sağ Üst",   0.0f, 1.0f, 2, PuzzleColor.Blue));
                    break;

                case GlassCompartmentPreset.ThreeVertical_Columns:
                    // Two vertical glass dividers splitting the pool into 3 vertical columns (Left / Center / Right)
                    outDividers.Add(new GlassDividerData("Sol Sütun Bölmesi", new Vector2(-0.44f, -0.02f), 1.04f, 0.080f, 90f, true));
                    outDividers.Add(new GlassDividerData("Sağ Sütun Bölmesi", new Vector2( 0.44f, -0.02f), 1.04f, 0.080f, 90f, true));
                    outZones.Add(new CompartmentColorZone("Sol Sütun",  -1.00f, -0.34f, -1, PuzzleColor.Blue));
                    outZones.Add(new CompartmentColorZone("Orta Sütun", -0.34f,  0.34f, -1, PuzzleColor.Yellow));
                    outZones.Add(new CompartmentColorZone("Sağ Sütun",   0.34f,  1.00f, -1, PuzzleColor.Red));
                    break;

                case GlassCompartmentPreset.InvertedT_BottomRowPlusSplitTop:
                    // Upper vertical divider above Row 0: Bottom row is a full continuous row, upper rows are split Left/Right!
                    outDividers.Add(new GlassDividerData("Üst Ayırıcı Bölme", new Vector2(0f, 0.11f), 0.76f, 0.085f, 90f, true));
                    outZones.Add(new CompartmentColorZone("Alt Tam Sıra",  -1.0f, 1.0f,  0, PuzzleColor.Blue));
                    outZones.Add(new CompartmentColorZone("Üst Sol Bölme", -1.0f, 0.0f, -1, PuzzleColor.Yellow));
                    outZones.Add(new CompartmentColorZone("Üst Sağ Bölme",  0.0f, 1.0f, -1, PuzzleColor.Red));
                    break;

                case GlassCompartmentPreset.TopT_SplitBottomPlusTopRow:
                    // Lower vertical divider splitting Rows 0 & 1, with a full continuous Row 2 across the top
                    outDividers.Add(new GlassDividerData("Alt Ayırıcı Bölme", new Vector2(0f, -0.22f), 0.64f, 0.085f, 90f, true));
                    outZones.Add(new CompartmentColorZone("Alt Sol",      -1.0f, 0.0f, 0, PuzzleColor.Blue));
                    outZones.Add(new CompartmentColorZone("Alt Sağ",       0.0f, 1.0f, 0, PuzzleColor.Green));
                    outZones.Add(new CompartmentColorZone("Orta Sol",     -1.0f, 0.0f, 1, PuzzleColor.Yellow));
                    outZones.Add(new CompartmentColorZone("Orta Sağ",      0.0f, 1.0f, 1, PuzzleColor.Yellow));
                    outZones.Add(new CompartmentColorZone("Üst Tam Sıra", -1.0f, 1.0f, 2, PuzzleColor.Red));
                    break;

                case GlassCompartmentPreset.VFunnel_SlopedChannels:
                    // Angled V-funnel baffles guiding upper balls toward the center channel
                    outDividers.Add(new GlassDividerData("Sol Eğimli Kanal", new Vector2(-0.62f, -0.02f), 0.72f, 0.080f, -26f, true));
                    outDividers.Add(new GlassDividerData("Sağ Eğimli Kanal", new Vector2( 0.62f, -0.02f), 0.72f, 0.080f,  26f, true));
                    outZones.Add(new CompartmentColorZone("Alt Katman",  -1.0f, 1.0f, 0, PuzzleColor.Blue));
                    outZones.Add(new CompartmentColorZone("Orta Katman", -1.0f, 1.0f, 1, PuzzleColor.Yellow));
                    outZones.Add(new CompartmentColorZone("Üst Katman",  -1.0f, 1.0f, 2, PuzzleColor.Red));
                    break;

                case GlassCompartmentPreset.ZigZag_Shelves:
                    // Staggered sloped shelves inside the pool
                    outDividers.Add(new GlassDividerData("Sol Alt Raf", new Vector2(-0.46f, -0.14f), 0.88f, 0.075f, -12f, true));
                    outDividers.Add(new GlassDividerData("Sağ Üst Raf", new Vector2( 0.46f,  0.12f), 0.88f, 0.075f,  12f, true));
                    outZones.Add(new CompartmentColorZone("Alt Katman",  -1.0f, 1.0f, 0, PuzzleColor.Blue));
                    outZones.Add(new CompartmentColorZone("Orta Katman", -1.0f, 1.0f, 1, PuzzleColor.Yellow));
                    outZones.Add(new CompartmentColorZone("Üst Katman",  -1.0f, 1.0f, 2, PuzzleColor.Red));
                    break;
            }
        }

        private static Mesh CreateRoundedBoxPoolQuadMesh(float halfW, float halfH, float centerY)
        {
            Mesh m = new Mesh { name = "RoundedBoxGlassPoolQuad" };
            m.vertices = new Vector3[]
            {
                new Vector3(-halfW, centerY - halfH, 0f),
                new Vector3( halfW, centerY - halfH, 0f),
                new Vector3(-halfW, centerY + halfH, 0f),
                new Vector3( halfW, centerY + halfH, 0f)
            };
            m.uv = new Vector2[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f)
            };
            m.normals = new Vector3[]
            {
                Vector3.back,
                Vector3.back,
                Vector3.back,
                Vector3.back
            };
            m.triangles = new int[] { 0, 2, 1, 2, 3, 1 };
            m.RecalculateBounds();
            return m;
        }

        private void BuildGlassFakeDropShadow(Vector3[] leftPath, Vector3[] rightPath)
        {
            if (leftPath == null || rightPath == null || leftPath.Length < 2) return;

            int n = Mathf.Min(leftPath.Length, rightPath.Length);
            Vector3[] shadowLeft = new Vector3[n];
            Vector3[] shadowRight = new Vector3[n];

            // Warm soft drop shadow offset down-left (-0.11, -0.14) and widened slightly for soft edge feathering
            Vector3 shadowOffset = new Vector3(-0.11f, -0.14f, 0.22f);
            for (int i = 0; i < n; i++)
            {
                shadowLeft[i] = new Vector3(leftPath[i].x - 0.22f, leftPath[i].y, 0f) + shadowOffset;
                shadowRight[i] = new Vector3(rightPath[i].x + 0.14f, rightPath[i].y, 0f) + shadowOffset;
            }

            GameObject shadowObj = new GameObject("FlaskFakeDropShadow");
            shadowObj.transform.SetParent(_glassWallsObj.transform, false);
            Mesh shadowMesh = ProceduralMeshUtils.CreateTrackFloorRibbon(shadowLeft, shadowRight);
            shadowObj.AddComponent<MeshFilter>().sharedMesh = shadowMesh;
            var shadowMr = shadowObj.AddComponent<MeshRenderer>();
            shadowMr.sharedMaterial = _glassFakeShadowMat;
            shadowMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            shadowMr.receiveShadows = false;
        }

        private Vector3[] OffsetPath(Vector3[] src, Vector3 offset)
        {
            Vector3[] res = new Vector3[src.Length];
            for (int i = 0; i < src.Length; i++) res[i] = src[i] + offset;
            return res;
        }

        /// <summary>
        /// Builds the Image-1 Creamy-White Rounded Shoulder Cap with Colored Pill Button Insert
        /// (Purple pill on Left shoulder, Golden-Yellow pill on Right shoulder) + Fake Shadow!
        /// </summary>
        private void BuildShoulderHandleEar(bool isLeft, Vector3[] sideOuterPath)
        {
            float sign = isLeft ? -1f : 1f;

            // Find the widest point in the main chamber and place the shoulder cap on the upper shoulder above it
            int widestIdx = 0;
            float maxAbsX = 0f;
            for (int i = 0; i < sideOuterPath.Length; i++)
            {
                float ax = Mathf.Abs(sideOuterPath[i].x);
                if (ax > maxAbsX)
                {
                    maxAbsX = ax;
                    widestIdx = i;
                }
            }

            // Target a point along the upper shoulder above the equator
            float targetY = sideOuterPath[widestIdx].y + 0.48f * heightScale;
            int shoulderIdx = widestIdx;
            float bestDiff = float.MaxValue;
            for (int i = widestIdx; i < sideOuterPath.Length; i++)
            {
                float d = Mathf.Abs(sideOuterPath[i].y - targetY);
                if (d < bestDiff)
                {
                    bestDiff = d;
                    shoulderIdx = i;
                }
            }

            Vector3 anchor = sideOuterPath[shoulderIdx];
            int prevIdx = Mathf.Max(0, shoulderIdx - 2);
            int nextIdx = Mathf.Min(sideOuterPath.Length - 1, shoulderIdx + 2);
            Vector3 tangent = (sideOuterPath[nextIdx] - sideOuterPath[prevIdx]).normalized;
            float tangentAngle = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg - 90f;

            GameObject earObj = new GameObject(isLeft ? "LeftShoulderCap_PurplePill" : "RightShoulderCap_YellowPill");
            earObj.transform.SetParent(_glassWallsObj.transform, false);
            earObj.transform.localPosition = new Vector3(
                anchor.x + sign * (0.045f + shoulderHandleOffset.x),
                anchor.y + 0.045f + shoulderHandleOffset.y,
                -0.155f);
            earObj.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Clamp(tangentAngle, -42f, 42f));

            float capW = 0.42f * shoulderHandleScale;
            float capH = 0.16f * shoulderHandleScale;
            float capD = 0.06f * shoulderHandleScale;
            float capR = 0.072f * shoulderHandleScale;

            // 1. Creamy-White Outer Pill Cap
            earObj.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshUtils.CreatePillBadge(capW, capH, capD, capR);
            var mr = earObj.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _earHandleMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            // 2. Colored Inner Pill Button Insert (Purple on Left, Golden-Yellow on Right — Image 1 Reference!)
            GameObject innerPillObj = new GameObject(isLeft ? "PurplePillInsert" : "YellowPillInsert");
            innerPillObj.transform.SetParent(earObj.transform, false);
            innerPillObj.transform.localPosition = new Vector3(0f, 0.008f, -0.024f);
            innerPillObj.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshUtils.CreatePillBadge(
                capW * 0.56f,
                capH * 0.46f,
                capD * 0.65f,
                capR * 0.44f);
            var innerMr = innerPillObj.AddComponent<MeshRenderer>();
            innerMr.sharedMaterial = isLeft ? _leftEarPillMat : _rightEarPillMat;
            innerMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            innerMr.receiveShadows = false;
        }

        /// <summary>
        /// Builds the glossy white curved specular reflection crescents automatically hugging the upper-left and upper-right
        /// shoulders of whatever custom glass shape is currently active.
        /// </summary>
        private void BuildDomeSpecularReflections(Vector3[] leftInnerPath, Vector3[] rightInnerPath)
        {
            BuildSideSpecularArc(leftInnerPath, true);
            BuildSideSpecularArc(rightInnerPath, false);
        }

        private void BuildSideSpecularArc(Vector3[] innerPath, bool isLeft)
        {
            if (innerPath == null || innerPath.Length < 10) return;

            // Find the widest point of the chamber on this side
            int widestIdx = 0;
            float maxSpan = float.MinValue;
            for (int i = 0; i < innerPath.Length; i++)
            {
                float span = isLeft ? -innerPath[i].x : innerPath[i].x;
                if (span > maxSpan)
                {
                    maxSpan = span;
                    widestIdx = i;
                }
            }

            int startIdx = Mathf.Clamp(widestIdx + 1, 1, innerPath.Length - 6);
            int endIdx = Mathf.Clamp(widestIdx + innerPath.Length / 5, startIdx + 4, innerPath.Length - 2);

            List<Vector3> arcPts = new List<Vector3>();
            for (int i = startIdx; i <= endIdx; i++)
            {
                Vector3 prev = innerPath[Mathf.Max(0, i - 1)];
                Vector3 next = innerPath[Mathf.Min(innerPath.Length - 1, i + 1)];
                Vector2 tang = new Vector2(next.x - prev.x, next.y - prev.y).normalized;
                Vector2 inNorm = isLeft ? new Vector2(tang.y, -tang.x) : new Vector2(-tang.y, tang.x);

                Vector3 p = innerPath[i] + new Vector3(inNorm.x * 0.075f, inNorm.y * 0.075f, -0.25f);
                arcPts.Add(p);
            }

            if (arcPts.Count >= 3)
            {
                GameObject domeHighlight = new GameObject(isLeft ? "LeftDomeSpecularArc" : "RightDomeSpecularArc");
                domeHighlight.transform.SetParent(_glassWallsObj.transform, false);
                domeHighlight.AddComponent<MeshFilter>().sharedMesh =
                    ProceduralMeshUtils.CreateTaperedPipeMesh(arcPts.ToArray(), 0.044f, 10);
                var mr = domeHighlight.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _specularStreakMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        /// <summary>
        /// Generates the smooth 2D outer and normal-offset inner paths from the user's `contourPoints` list,
        /// and populates the 256-sample fast wall lookup table for granular bead physics.
        /// </summary>
        public void GenerateFlaskPaths(
            out Vector3[] leftPath,
            out Vector3[] rightPath,
            out Vector3[] leftInnerPath,
            out Vector3[] rightInnerPath)
        {
            EnsureContourInitialized();

            // Build scaled control points for left and right walls
            int cpCount = contourPoints.Count;
            Vector2[] leftCP = new Vector2[cpCount];
            Vector2[] rightCP = new Vector2[cpCount];
            float[] tensions = new float[cpCount];

            for (int i = 0; i < cpCount; i++)
            {
                var cp = contourPoints[i];
                float cx = (symmetricMode ? 0f : cp.centerX) * widthScale;
                float hw = Mathf.Max(0.06f, cp.halfWidth * widthScale);
                float y = cp.y * heightScale;

                leftCP[i] = new Vector2(cx - hw, y);
                rightCP[i] = new Vector2(cx + hw, y);
                tensions[i] = Mathf.Clamp01(cp.cornerTension);
            }

            int subdiv = Mathf.Clamp(curveSubdivisions, 4, 24);
            List<Vector2> left2D = InterpolateContourSpline(leftCP, tensions, subdiv);
            List<Vector2> right2D = InterpolateContourSpline(rightCP, tensions, subdiv);

            int n = Mathf.Min(left2D.Count, right2D.Count);
            leftPath = new Vector3[n];
            rightPath = new Vector3[n];
            leftInnerPath = new Vector3[n];
            rightInnerPath = new Vector3[n];

            // Compute true 2D inward normals so the inner cyan rim has uniform perpendicular spacing everywhere!
            for (int i = 0; i < n; i++)
            {
                int iPrev = Mathf.Max(0, i - 1);
                int iNext = Mathf.Min(n - 1, i + 1);

                Vector2 lTang = (left2D[iNext] - left2D[iPrev]).normalized;
                if (lTang.sqrMagnitude < 0.0001f) lTang = Vector2.up;
                // Left path goes bottom-to-top (+y), so inward normal points right (+x, -y)
                Vector2 lInward = new Vector2(lTang.y, -lTang.x).normalized;

                Vector2 rTang = (right2D[iNext] - right2D[iPrev]).normalized;
                if (rTang.sqrMagnitude < 0.0001f) rTang = Vector2.up;
                // Right path goes bottom-to-top (+y), so inward normal points left (-x, +y)
                Vector2 rInward = new Vector2(-rTang.y, rTang.x).normalized;

                float span = right2D[i].x - left2D[i].x;
                float localInset = Mathf.Min(innerRimInset, span * 0.28f);

                Vector2 lInPt = left2D[i] + lInward * localInset;
                Vector2 rInPt = right2D[i] + rInward * localInset;

                leftPath[i] = new Vector3(left2D[i].x, left2D[i].y, 0f);
                rightPath[i] = new Vector3(right2D[i].x, right2D[i].y, 0f);
                leftInnerPath[i] = new Vector3(lInPt.x, lInPt.y, 0f);
                rightInnerPath[i] = new Vector3(rInPt.x, rInPt.y, 0f);
            }

            BuildWallLookupTable(leftPath, rightPath, leftInnerPath, rightInnerPath);
        }

        private List<Vector2> InterpolateContourSpline(Vector2[] pts, float[] tensions, int subdiv)
        {
            List<Vector2> result = new List<Vector2>(pts.Length * subdiv + 1);
            int n = pts.Length;
            if (n < 2)
            {
                if (n == 1) result.Add(pts[0]);
                return result;
            }

            for (int i = 0; i < n - 1; i++)
            {
                Vector2 p0 = pts[Mathf.Max(0, i - 1)];
                Vector2 p1 = pts[i];
                Vector2 p2 = pts[i + 1];
                Vector2 p3 = pts[Mathf.Min(n - 1, i + 2)];

                float segTension = Mathf.Max(tensions[i], tensions[i + 1]);

                for (int s = 0; s < subdiv; s++)
                {
                    float t = (float)s / subdiv;
                    Vector2 smoothPt = EvaluateCatmullRom2D(p0, p1, p2, p3, t);
                    Vector2 linearPt = Vector2.Lerp(p1, p2, t);
                    Vector2 pt = Vector2.Lerp(smoothPt, linearPt, segTension);

                    // Enforce monotonic Y progression so horizontal/vertical lookups never fold back
                    if (result.Count > 0 && pt.y < result[result.Count - 1].y)
                    {
                        pt.y = result[result.Count - 1].y;
                    }
                    result.Add(pt);
                }
            }

            result.Add(pts[n - 1]);
            return result;
        }

        private static Vector2 EvaluateCatmullRom2D(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (
                (2f * p1) +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3
            );
        }

        private void BuildWallLookupTable(
            Vector3[] leftPath,
            Vector3[] rightPath,
            Vector3[] leftInnerPath,
            Vector3[] rightInnerPath)
        {
            if (leftPath == null || leftPath.Length < 2) return;

            _lutMinY = leftPath[0].y;
            _lutMaxY = leftPath[leftPath.Length - 1].y;
            if (_lutMaxY <= _lutMinY + 0.01f) _lutMaxY = _lutMinY + 1.0f;

            int n = leftPath.Length;
            int jL = 0;
            int jR = 0;
            int jIL = 0;
            int jIR = 0;

            float[] rawInnerL = new float[LUT_SIZE];
            float[] rawInnerR = new float[LUT_SIZE];

            for (int k = 0; k < LUT_SIZE; k++)
            {
                float y = Mathf.Lerp(_lutMinY, _lutMaxY, (float)k / (LUT_SIZE - 1));

                while (jL < n - 2 && leftPath[jL + 1].y < y) jL++;
                float tL = Mathf.InverseLerp(leftPath[jL].y, leftPath[jL + 1].y, y);
                _lutLeftX[k] = Mathf.Lerp(leftPath[jL].x, leftPath[jL + 1].x, tL);

                while (jR < n - 2 && rightPath[jR + 1].y < y) jR++;
                float tR = Mathf.InverseLerp(rightPath[jR].y, rightPath[jR + 1].y, y);
                _lutRightX[k] = Mathf.Lerp(rightPath[jR].x, rightPath[jR + 1].x, tR);

                while (jIL < n - 2 && leftInnerPath[jIL + 1].y < y) jIL++;
                float tIL = Mathf.InverseLerp(leftInnerPath[jIL].y, leftInnerPath[jIL + 1].y, y);
                rawInnerL[k] = Mathf.Lerp(leftInnerPath[jIL].x, leftInnerPath[jIL + 1].x, tIL);

                while (jIR < n - 2 && rightInnerPath[jIR + 1].y < y) jIR++;
                float tIR = Mathf.InverseLerp(rightInnerPath[jIR].y, rightInnerPath[jIR + 1].y, y);
                rawInnerR[k] = Mathf.Lerp(rightInnerPath[jIR].x, rightInnerPath[jIR + 1].x, tIR);
            }

            // Full 2D Euclidean Minkowski erosion from the inner glass walls so rotated 3D cubes
            // NEVER penetrate sloped floors, diagonal walls, or curved shoulders at any angle!
            float cubeBoundRadius = particleSize * 0.56f;
            float innerPipeRadius = glassRimRadius * 0.48f;
            float clearanceR = cubeBoundRadius + innerPipeRadius + 0.016f;

            for (int k = 0; k < LUT_SIZE; k++)
            {
                _lutSafeLeftX[k] = rawInnerL[k];
                _lutSafeRightX[k] = rawInnerR[k];
            }

            const int segSubdiv = 8;
            float invRangeY = (LUT_SIZE - 1) / (_lutMaxY - _lutMinY);

            for (int i = 0; i < n - 1; i++)
            {
                Vector3 lA = leftInnerPath[i];
                Vector3 lB = leftInnerPath[i + 1];
                Vector3 rA = rightInnerPath[i];
                Vector3 rB = rightInnerPath[i + 1];

                for (int s = 0; s <= segSubdiv; s++)
                {
                    float u = (float)s / segSubdiv;
                    Vector3 lPt = Vector3.Lerp(lA, lB, u);
                    Vector3 rPt = Vector3.Lerp(rA, rB, u);

                    // Left inner wall point erosion
                    float spanAtL = Mathf.Max(0.04f, rPt.x - lPt.x);
                    float rLocalL = Mathf.Min(clearanceR, Mathf.Max(0.02f, spanAtL * 0.5f - 0.015f));
                    int kMinL = Mathf.Clamp(Mathf.FloorToInt((lPt.y - rLocalL - _lutMinY) * invRangeY), 0, LUT_SIZE - 1);
                    int kMaxL = Mathf.Clamp(Mathf.CeilToInt((lPt.y + rLocalL - _lutMinY) * invRangeY), 0, LUT_SIZE - 1);
                    for (int k = kMinL; k <= kMaxL; k++)
                    {
                        float yk = Mathf.Lerp(_lutMinY, _lutMaxY, (float)k / (LUT_SIZE - 1));
                        float dy = yk - lPt.y;
                        float dySq = dy * dy;
                        if (dySq < rLocalL * rLocalL)
                        {
                            float dx = Mathf.Sqrt(rLocalL * rLocalL - dySq);
                            if (lPt.x + dx > _lutSafeLeftX[k])
                            {
                                _lutSafeLeftX[k] = lPt.x + dx;
                            }
                        }
                    }

                    // Right inner wall point erosion
                    float rLocalR = Mathf.Min(clearanceR, Mathf.Max(0.02f, spanAtL * 0.5f - 0.015f));
                    int kMinR = Mathf.Clamp(Mathf.FloorToInt((rPt.y - rLocalR - _lutMinY) * invRangeY), 0, LUT_SIZE - 1);
                    int kMaxR = Mathf.Clamp(Mathf.CeilToInt((rPt.y + rLocalR - _lutMinY) * invRangeY), 0, LUT_SIZE - 1);
                    for (int k = kMinR; k <= kMaxR; k++)
                    {
                        float yk = Mathf.Lerp(_lutMinY, _lutMaxY, (float)k / (LUT_SIZE - 1));
                        float dy = yk - rPt.y;
                        float dySq = dy * dy;
                        if (dySq < rLocalR * rLocalR)
                        {
                            float dx = Mathf.Sqrt(rLocalR * rLocalR - dySq);
                            if (rPt.x - dx < _lutSafeRightX[k])
                            {
                                _lutSafeRightX[k] = rPt.x - dx;
                            }
                        }
                    }
                }
            }

            for (int k = 0; k < LUT_SIZE; k++)
            {
                if (_lutSafeLeftX[k] > _lutSafeRightX[k])
                {
                    float mid = 0.5f * (rawInnerL[k] + rawInnerR[k]);
                    _lutSafeLeftX[k] = mid - 0.012f;
                    _lutSafeRightX[k] = mid + 0.012f;
                }
            }

            _lutValid = true;
        }

        private void BuildSpline()
        {
            EnsureContourInitialized();
            List<Vector3> keyPoints = new List<Vector3>();
            for (int i = contourPoints.Count - 1; i >= 0; i--)
            {
                keyPoints.Add(new Vector3(contourPoints[i].centerX * widthScale, contourPoints[i].y * heightScale, 0f));
            }

            if (_splineWaypoints == null) _splineWaypoints = new List<Vector3>();
            if (_cumulativeLengths == null) _cumulativeLengths = new List<float>();

            _splineWaypoints.Clear();
            _cumulativeLengths.Clear();

            int n = keyPoints.Count;
            int subdivisions = 8;

            for (int i = 0; i < n - 1; i++)
            {
                Vector3 p0 = keyPoints[Mathf.Max(0, i - 1)];
                Vector3 p1 = keyPoints[i];
                Vector3 p2 = keyPoints[i + 1];
                Vector3 p3 = keyPoints[Mathf.Min(n - 1, i + 2)];

                for (int s = 0; s < subdivisions; s++)
                {
                    float t = (float)s / subdivisions;
                    _splineWaypoints.Add(CatmullRom(p0, p1, p2, p3, t));
                }
            }
            _splineWaypoints.Add(keyPoints[n - 1]);

            _totalSplineLength = 0f;
            _cumulativeLengths.Add(0f);
            for (int i = 1; i < _splineWaypoints.Count; i++)
            {
                _totalSplineLength += Vector3.Distance(_splineWaypoints[i - 1], _splineWaypoints[i]);
                _cumulativeLengths.Add(_totalSplineLength);
            }
        }

        private Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (
                (2f * p1) +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3
            );
        }

        public Vector3 EvaluateSpline(float distance, out Vector3 tangent, out Vector3 normal)
        {
            if (_splineWaypoints == null || _splineWaypoints.Count < 2) BuildSpline();
            distance = Mathf.Clamp(distance, 0f, _totalSplineLength);

            int low = 0;
            int high = _cumulativeLengths.Count - 1;
            while (low <= high)
            {
                int mid = (low + high) / 2;
                if (_cumulativeLengths[mid] < distance) low = mid + 1;
                else high = mid - 1;
            }

            int idx = Mathf.Clamp(low - 1, 0, _splineWaypoints.Count - 2);
            float segStart = _cumulativeLengths[idx];
            float segEnd = _cumulativeLengths[idx + 1];
            float segLen = segEnd - segStart;
            float t = segLen > 0.0001f ? (distance - segStart) / segLen : 0f;

            Vector3 pA = _splineWaypoints[idx];
            Vector3 pB = _splineWaypoints[idx + 1];
            tangent = (pB - pA).normalized;
            normal = new Vector3(-tangent.y, tangent.x, 0f).normalized;
            return Vector3.Lerp(pA, pB, t);
        }

        /// <summary>
        /// Populates any custom or preset glass shape with clean, cohesive, non-mixed color bands (Reference Video style)
        /// matching the active monsters in the scene, or with stunning Pixel Art (Image 2)!
        /// </summary>
        public void PopulateBottlePhysics()
        {
            CleanOrphanedChildren();

            // Disable sand simulation if present to prevent conflicts
            var sand = GetComponent<SandSimulationEngine>();
            if (sand != null)
            {
                sand.ClearAll();
                sand.enabled = false;
            }
            var bridge = GetComponent<SandMonsterFlowBridge>();
            if (bridge != null) bridge.enabled = false;

            // 1. Rebuild Airtight Physics Colliders for this container
            var bcm = GetComponent<BallContainerManager>().OrNull() ?? gameObject.AddComponent<BallContainerManager>();
            bcm.glassContainer = this;
            bcm.wallDepth = Mathf.Max(0.65f, particleSize * 2f + 0.20f);
            bcm.frontPlaneZ = -0.10f - (particleSize * 0.5f + 0.12f);
            bcm.backPlaneZ = -0.10f + (particleSize * 0.5f + 0.12f);
            bcm.RebuildColliders();

            // 2. Clear old physical balls
            if (BallObjectPool.Instance != null)
            {
                BallObjectPool.Instance.ClearAllActiveBalls();
            }

            // 3. Spawn real 3D physical balls via BallPatternGenerator
            var gen = BallPatternGenerator.Instance.OrNull() ?? FindFirstObjectByType<BallPatternGenerator>();
            if (gen == null)
            {
                var gm = PuzzleGlassGameManager.Instance.OrNull() ?? FindFirstObjectByType<PuzzleGlassGameManager>();
                if (gm != null) gen = gm.GetComponent<BallPatternGenerator>().OrNull() ?? gm.gameObject.AddComponent<BallPatternGenerator>();
                else gen = gameObject.AddComponent<BallPatternGenerator>();
            }

            if (IsFlatBottomBox)
            {
                particleSize = 0.265f;
            }

            gen.glassContainer = this;
            gen.ballDiameter = IsFlatBottomBox ? 0.265f : ((particleSize > 0.05f && particleSize <= 0.30f) ? particleSize : 0.265f);
            gen.patternVerticalOffset = patternVerticalOffset;
            gen.initialShapeHoldTime = 0f;

            var gameMgr = PuzzleGlassGameManager.Instance.OrNull() ?? FindFirstObjectByType<PuzzleGlassGameManager>();
            ShapePatternType shape = (gameMgr != null) ? gameMgr.activePatternType : ShapePatternType.GumballMix;

            List<LevelMonsterEntry> goals = null;
            if (gameMgr != null && gameMgr.currentLevel != null && gameMgr.currentLevel.monsters != null)
            {
                goals = gameMgr.currentLevel.monsters;
            }

            var balls = gen.GeneratePattern(shape, goals);

            // 4. Synchronize Carrier Boxes at the bottom staging tray ONLY when no level asset/procedural boxes exist!
            if (gameMgr != null)
            {
                bool hasLevelBoxes = gameMgr.currentLevel != null &&
                                     gameMgr.currentLevel.monsters != null &&
                                     gameMgr.currentLevel.monsters.Count > 0 &&
                                     shape == ShapePatternType.GumballMix;
                if (!hasLevelBoxes)
                {
                    gameMgr.SynchronizeMonstersFromSpawnedBalls(balls);
                }
            }
        }

        public float GetSpoutThroatY()
        {
            if (IsFlatBottomBox)
            {
                return GetChamberFloorY();
            }
            if (contourPoints != null && contourPoints.Count >= 3)
            {
                return contourPoints[1].y * heightScale + 0.04f;
            }
            return spoutBottomY + 0.45f;
        }

        public float GetChamberFloorY()
        {
            if (IsFlatBottomBox)
            {
                if (contourPoints != null && contourPoints.Count > 0)
                {
                    return contourPoints[0].y * heightScale;
                }
                return -0.56f * heightScale;
            }

            if (contourPoints != null && contourPoints.Count >= 4)
            {
                for (int i = 0; i < contourPoints.Count; i++)
                {
                    if (contourPoints[i].y >= -0.60f && contourPoints[i].halfWidth >= 0.38f)
                    {
                        return contourPoints[i].y * heightScale;
                    }
                }
            }

            return -0.46f * heightScale;
        }

        private void GetRoundedBoxPoolExtents(
            out float centerY,
            out float quadHalfW,
            out float quadHalfH,
            out float visibleHalfW,
            out float visibleHalfH)
        {
            float innerFloorY = GetChamberFloorY();
            float innerCeilY = (contourPoints != null && contourPoints.Count > 0)
                ? contourPoints[contourPoints.Count - 1].y * heightScale
                : 0.62f * heightScale;
            centerY = (innerFloorY + innerCeilY) * 0.5f;
            float innerHalfH = Mathf.Max(0.25f, (innerCeilY - innerFloorY) * 0.5f);
            float innerHalfW = (contourPoints != null && contourPoints.Count > 0)
                ? contourPoints[0].halfWidth * widthScale
                : 1.32f * widthScale;

            // Exact alignment with EvaluateRoundedBoxPool SDF
            float ft = Mathf.Clamp(boxFrameThickness, 0.04f, 0.24f);
            quadHalfH = innerHalfH / Mathf.Max(0.30f, 0.86f - ft);
            quadHalfW = (innerHalfW + ft * quadHalfH) / 0.90f;
            visibleHalfW = 0.90f * quadHalfW;
            visibleHalfH = 0.86f * quadHalfH;
        }

        /// <summary>
        /// Returns the world-space bounds of the visible outer glass frame (excluding shader drop-shadow padding).
        /// </summary>
        public bool TryGetVisibleFrameWorldBounds(out float minX, out float maxX, out float minY, out float maxY)
        {
            EnsureContourInitialized();

            if (IsFlatBottomBox)
            {
                GetRoundedBoxPoolExtents(out float centerY, out _, out _, out float visibleHalfW, out float visibleHalfH);
                Vector3 worldMin = transform.TransformPoint(new Vector3(-visibleHalfW, centerY - visibleHalfH, 0f));
                Vector3 worldMax = transform.TransformPoint(new Vector3( visibleHalfW, centerY + visibleHalfH, 0f));
                minX = Mathf.Min(worldMin.x, worldMax.x);
                maxX = Mathf.Max(worldMin.x, worldMax.x);
                minY = Mathf.Min(worldMin.y, worldMax.y);
                maxY = Mathf.Max(worldMin.y, worldMax.y);
                return true;
            }

            Renderer[] rends = GetComponentsInChildren<Renderer>(false);
            bool foundAny = false;
            minX = float.MaxValue;
            maxX = float.MinValue;
            minY = float.MaxValue;
            maxY = float.MinValue;
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] == null || !rends[i].enabled) continue;
                string rName = rends[i].gameObject.name;
                if (rName.Contains("Shadow") || rName.Contains("Ball") || rName.Contains("Sand")) continue;
                Bounds b = rends[i].bounds;
                if (b.min.x < minX) minX = b.min.x;
                if (b.max.x > maxX) maxX = b.max.x;
                if (b.min.y < minY) minY = b.min.y;
                if (b.max.y > maxY) maxY = b.max.y;
                foundAny = true;
            }
            return foundAny;
        }

        public float CalculateChamberCenterY()
        {
            if (contourPoints != null && contourPoints.Count >= 4)
            {
                float maxHw = 0f;
                for (int i = 0; i < contourPoints.Count; i++)
                {
                    if (contourPoints[i].halfWidth > maxHw) maxHw = contourPoints[i].halfWidth;
                }

                float chamberHwThreshold = Mathf.Max(0.40f, maxHw * 0.45f);
                float chamberYMin = float.MaxValue;
                float chamberYMax = float.MinValue;

                for (int i = 0; i < contourPoints.Count; i++)
                {
                    var pt = contourPoints[i];
                    if (pt.halfWidth >= chamberHwThreshold)
                    {
                        if (pt.y < chamberYMin) chamberYMin = pt.y;
                        if (pt.y > chamberYMax) chamberYMax = pt.y;
                    }
                }

                if (chamberYMin < float.MaxValue && chamberYMax > float.MinValue)
                {
                    float mid = (chamberYMin + chamberYMax) * 0.5f;
                    return mid * heightScale + patternVerticalOffset;
                }
            }

            return 0.30f * heightScale + patternVerticalOffset;
        }

        /// <summary>
        /// Camın tek görseli olan kumu kurar: desen modunda piksel görseli, diğer modda canavar renk katmanları.
        /// </summary>
        private void PopulateSandVisual()
        {
            CleanOrphanedChildren();

            var pool = FindFirstObjectByType<BallObjectPool>();
            if (pool != null) pool.ClearAllActiveBalls();

            var engine = GetComponent<SandSimulationEngine>().OrNull() ?? gameObject.AddComponent<SandSimulationEngine>();
            engine.glassContainer = this;

            var gameMgr = PuzzleGlassGameManager.Instance.OrNull() ?? FindFirstObjectByType<PuzzleGlassGameManager>();

            if (particleMode == ParticleArrangementMode.PixelArtVisual)
            {
                PixelArtPattern pattern = PixelArtLibrary.Resolve(pixelArtPreset, customPattern);
                engine.BuildFromPattern(pattern);
                if (gameMgr != null) gameMgr.SynchronizeMonstersForPattern(pattern);
                return;
            }

            var layers = new List<LevelMonsterEntry>();
            if (gameMgr != null && gameMgr.activeMonsters != null)
            {
                for (int i = 0; i < gameMgr.activeMonsters.Count; i++)
                {
                    var monster = gameMgr.activeMonsters[i];
                    if (monster != null) layers.Add(new LevelMonsterEntry(monster.colorType, monster.initialCount));
                }
            }

            if (layers.Count == 0)
            {
                layers.Add(new LevelMonsterEntry(PuzzleColor.Blue, 10));
                layers.Add(new LevelMonsterEntry(PuzzleColor.Yellow, 20));
            }

            engine.BuildFromLayers(layers);
        }

        public void ApplyPixelArtPreset(PixelArtPreset preset, bool rebuildImmediate = true)
        {
            particleMode = ParticleArrangementMode.PixelArtVisual;
            pixelArtPreset = preset;
            freezePatternInPlace = false;
            if (rebuildImmediate)
            {
                RebuildContainer();
            }
        }

        public float EvaluateLayerSortScore(Vector2 p)
        {
            // Keep spout particles at the very bottom first
            if (p.y < spoutBottomY + 0.68f)
            {
                return p.y - 10f;
            }

            switch (layerPattern)
            {
                case BottleLayerPattern.HorizontalWaves:
                    return p.y + Mathf.Sin(p.x * 4.5f) * 0.045f;
                case BottleLayerPattern.LeftRightSplit:
                    return p.y * 0.35f + p.x * 0.65f;
                case BottleLayerPattern.DiagonalBands:
                    return p.y + p.x * 0.38f;
                case BottleLayerPattern.VFunnelBands:
                default:
                    return p.y + Mathf.Abs(p.x) * 0.24f;
            }
        }

        private void SyncCubeTransforms()
        {
            if (_bottleCubes == null) return;
            for (int i = 0; i < _bottleCubes.Count; i++)
            {
                var bc = _bottleCubes[i];
                if (bc.gameObject == null) continue;
                bc.gameObject.transform.localPosition = new Vector3(bc.x, bc.y, -0.10f);
                bc.gameObject.transform.localRotation = Quaternion.Euler(bc.rotX, bc.rotY, bc.angle);
                bc.gameObject.transform.localScale = Vector3.one;
            }
        }

        /// <summary>
        /// Continuous Sub-Stepped Position-Based Dynamics (PBD) Granular Physics Engine:
        /// Simulates real physical 3D cubes inside a glass funnel/bottle under Newtonian gravity:
        /// - Sloped glass wall normal reaction & low-friction tangential sliding along curved/diagonal glass walls
        /// - Asymmetric gravity-ordered cube-cube contact projection (upper cubes roll smoothly around lower cubes' shoulders)
        /// - True 3D angular rolling & bevel facet tumbling proportional to physical displacement
        /// - Cohesive funnel throat flow so active color bands drain cleanly without upper-layer rat-holing
        /// </summary>
        private bool StepGranularPhysics(float dt, bool isInitialSettle)
        {
            if (_bottleCubes == null || _bottleCubes.Count == 0) return false;

            int count = _bottleCubes.Count;

            // In Pixel Art mode with freezePatternInPlace, check if there are any unfrozen cubes (e.g. live dropping)
            if (particleMode == ParticleArrangementMode.PixelArtVisual && freezePatternInPlace)
            {
                bool anyUnfrozen = false;
                for (int i = 0; i < count; i++)
                {
                    if (!_bottleCubes[i].isFrozen) { anyUnfrozen = true; break; }
                }
                if (!anyUnfrozen) return false;
            }

            float beadRadius = particleSize * 0.50f;
            float minDist = particleSize * 1.055f;
            float minDistSq = minDist * minDist;
            float spoutFloorY = spoutBottomY + beadRadius + 0.025f;
            float spoutTopChannelY = spoutBottomY + 0.64f;
            float throatGateY = spoutBottomY + 0.72f;

            int subSteps = isInitialSettle ? 1 : 4;
            float subDt = isInitialSettle ? dt : (dt / subSteps);
            int solverIterations = isInitialSettle ? 5 : 4;
            const float gravity = 28.0f;

            bool anyMoved = false;

            for (int sub = 0; sub < subSteps; sub++)
            {
                // Find the lowest active layerOrder still in the bottle and whether it still has cubes in the chamber
                int minLayerOrder = int.MaxValue;
                for (int i = 0; i < count; i++)
                {
                    if (_bottleCubes[i].layerOrder < minLayerOrder)
                    {
                        minLayerOrder = _bottleCubes[i].layerOrder;
                    }
                }

                bool activeBandInChamber = false;
                for (int i = 0; i < count; i++)
                {
                    var bc = _bottleCubes[i];
                    if (bc.layerOrder == minLayerOrder && bc.y > spoutTopChannelY - 0.04f)
                    {
                        activeBandInChamber = true;
                        break;
                    }
                }

                // 1. Apply Gravity, Sloped Wall Tangential Glide & Funnel Convergence
                for (int i = 0; i < count; i++)
                {
                    var c = _bottleCubes[i];
                    if (c.isFrozen)
                    {
                        c.vx = 0f;
                        c.vy = 0f;
                        c.x = c.targetX;
                        c.y = c.targetY;
                        continue;
                    }

                    c.targetX = c.x; // Store pre-substep X
                    c.targetY = c.y; // Store pre-substep Y

                    c.vy -= gravity * subDt;

                    GetSafeCubeBoundsAtY(c.y, out float minX, out float maxX);
                    float centerLineX = (minX + maxX) * 0.5f;

                    if (c.y < spoutTopChannelY)
                    {
                        // Inside vertical spout tube: smooth centering into a clean single-file column
                        float targetSpoutVx = (centerLineX - c.x) * 18.0f;
                        c.vx = Mathf.Lerp(c.vx, targetSpoutVx, 18.0f * subDt);
                    }
                    else
                    {
                        // Compute local glass wall slopes so cubes resting near sloped walls slide tangentially inward
                        float sampleDy = 0.025f;
                        GetSafeCubeBoundsAtY(c.y + sampleDy, out float minXUp, out float maxXUp);
                        GetSafeCubeBoundsAtY(Mathf.Max(spoutFloorY, c.y - sampleDy), out float minXDn, out float maxXDn);
                        float slopeL = (minXUp - minXDn) / (2f * sampleDy);
                        float slopeR = (maxXUp - maxXDn) / (2f * sampleDy);

                        // Left sloped floor tangential slide (slopeL < 0 when chamber widens upward)
                        if (c.x - minX < particleSize * 0.85f && slopeL < -0.04f)
                        {
                            float sinCos = Mathf.Clamp(-slopeL / (1f + slopeL * slopeL), 0.12f, 0.50f);
                            c.vx += gravity * sinCos * 1.55f * subDt;
                        }
                        // Right sloped floor tangential slide (slopeR > 0 when chamber widens upward)
                        else if (maxX - c.x < particleSize * 0.85f && slopeR > 0.04f)
                        {
                            float sinCos = Mathf.Clamp(slopeR / (1f + slopeR * slopeR), 0.12f, 0.50f);
                            c.vx -= gravity * sinCos * 1.55f * subDt;
                        }

                        // Active draining band converges briskly down the funnel toward the spout throat
                        float distToCenter = centerLineX - c.x;
                        if (!isInitialSettle && c.layerOrder == minLayerOrder && c.y < spoutBottomY + 1.65f)
                        {
                            float pull = Mathf.Clamp(distToCenter * 15.0f, -4.8f, 4.8f);
                            c.vx += pull * subDt;
                        }
                        else if (c.y < spoutBottomY + 1.35f)
                        {
                            float pull = Mathf.Clamp(distToCenter * 6.5f, -2.5f, 2.5f);
                            c.vx += pull * subDt;
                        }
                    }

                    // Light time-step-independent air/glass damping (NO per-substep 0.90 multiplier!)
                    c.vx *= Mathf.Exp(-1.8f * subDt);
                    c.vy *= Mathf.Exp(-0.45f * subDt);

                    // Integrate tentative position
                    c.x += c.vx * subDt;
                    c.y += c.vy * subDt;

                    // Cohesive funnel throat gate: while active-band cubes are still converging from the chamber wings,
                    // prevent upper-layer cubes from rat-holing into the narrow spout ahead of them!
                    if (activeBandInChamber && c.layerOrder > minLayerOrder && c.y < throatGateY)
                    {
                        c.y = throatGateY;
                        if (c.vy < 0f) c.vy = 0f;
                    }
                }

                // 2. Sort bottom-to-top so lower cubes form a stable granular foundation
                _bottleCubes.Sort((a, b) =>
                {
                    float scoreA = a.y + a.layerOrder * 0.015f;
                    float scoreB = b.y + b.layerOrder * 0.015f;
                    return scoreA.CompareTo(scoreB);
                });

                // 3. Multi-Iteration PBD Constraint Projection (Sloped Glass Walls + Cube-Cube Contacts)
                for (int iter = 0; iter < solverIterations; iter++)
                {
                    for (int i = 0; i < count; i++)
                    {
                        var c = _bottleCubes[i];
                        if (c.isFrozen) continue;
                        ProjectOntoSlopedGlassWalls(ref c.x, ref c.y, spoutFloorY);
                    }

                    for (int i = 0; i < count; i++)
                    {
                        var ci = _bottleCubes[i];
                        for (int j = 0; j < i; j++)
                        {
                            var cj = _bottleCubes[j];
                            if (ci.isFrozen && cj.isFrozen) continue;

                            float dy = ci.y - cj.y;
                            if (dy >= minDist || dy <= -minDist) continue;

                            float dx = ci.x - cj.x;
                            if (dx >= minDist || dx <= -minDist) continue;

                            float distSq = dx * dx + dy * dy;
                            if (distSq < minDistSq)
                            {
                                float dist = Mathf.Sqrt(Mathf.Max(0.000001f, distSq));
                                float overlap = minDist - dist;

                                // If balanced nearly vertically in the chamber, nudge slightly so upper cube rolls off the shoulder
                                if (Mathf.Abs(dx) < 0.003f && ci.y > spoutTopChannelY)
                                {
                                    GetSafeCubeBoundsAtY(ci.y, out float lX, out float rX);
                                    float midX = (lX + rX) * 0.5f;
                                    float rollDir = (ci.x < midX - 0.01f) ? 1f : (ci.x > midX + 0.01f ? -1f : ((i % 2 == 0) ? 1f : -1f));
                                    dx = rollDir * 0.008f;
                                    dist = Mathf.Sqrt(dx * dx + dy * dy);
                                }

                                float nx = dx / dist;
                                float ny = Mathf.Max(0.06f, dy / dist);
                                float invLen = 1.0f / Mathf.Sqrt(nx * nx + ny * ny);
                                nx *= invLen;
                                ny *= invLen;

                                // Upper cube rolls up and around the lower cube's shoulder with low friction
                                if (!ci.isFrozen)
                                {
                                    ci.x += nx * overlap * 0.92f;
                                    ci.y += ny * overlap * 0.96f;
                                }

                                // Subtle lateral packing response on lower cube if within the same chamber band
                                if (!cj.isFrozen && cj.y > spoutTopChannelY && cj.layerOrder == ci.layerOrder)
                                {
                                    cj.x -= nx * overlap * 0.12f;
                                    ClampToInnerGlassWalls(ref cj.x, ref cj.y, beadRadius, spoutFloorY);
                                }
                            }
                        }

                        if (!ci.isFrozen && activeBandInChamber && ci.layerOrder > minLayerOrder && ci.y < throatGateY)
                        {
                            ci.y = throatGateY;
                        }
                        if (!ci.isFrozen)
                        {
                            ProjectOntoSlopedGlassWalls(ref ci.x, ref ci.y, spoutFloorY);
                        }
                    }
                }

                // 4. Derive Constrained Physical Velocity & Update 3D Rolling Orientation
                for (int i = 0; i < count; i++)
                {
                    var c = _bottleCubes[i];
                    if (c.isFrozen)
                    {
                        c.vx = 0f;
                        c.vy = 0f;
                        c.x = c.targetX;
                        c.y = c.targetY;
                        continue;
                    }

                    float moveX = c.x - c.targetX;
                    float moveY = c.y - c.targetY;

                    // Prevent any artificial upward pop during live draining
                    if (!isInitialSettle && moveY > 0.0001f)
                    {
                        c.y = Mathf.Lerp(c.targetY, c.y, 0.25f);
                        moveY = c.y - c.targetY;
                    }

                    ClampToInnerGlassWalls(ref c.x, ref c.y, beadRadius, spoutFloorY);
                    moveX = c.x - c.targetX;
                    moveY = c.y - c.targetY;

                    float moveSq = moveX * moveX + moveY * moveY;
                    if (moveSq > 0.0000002f)
                    {
                        anyMoved = true;
                        // True Verlet velocity without per-substep artificial damping!
                        c.vx = Mathf.Clamp(moveX / subDt, -5.2f, 5.2f);
                        c.vy = Mathf.Clamp(moveY / subDt, -6.8f, 0.02f);

                        // Physical 3D rolling rotation on glass walls & neighbor cubes
                        if (!isInitialSettle)
                        {
                            GetSafeCubeBoundsAtY(c.y, out float sMinX, out float sMaxX);
                            float rollRad = -moveX / Mathf.Max(0.04f, beadRadius);

                            // Wall friction torque when sliding down left or right glass slope
                            if (moveY < 0f)
                            {
                                if (c.x - sMinX < particleSize * 0.40f)
                                {
                                    rollRad += (-moveY / beadRadius) * 0.55f;
                                }
                                else if (sMaxX - c.x < particleSize * 0.40f)
                                {
                                    rollRad -= (-moveY / beadRadius) * 0.55f;
                                }
                            }

                            float dAngleZ = rollRad * Mathf.Rad2Deg * 0.95f;
                            c.vAng = Mathf.Lerp(c.vAng, dAngleZ / subDt, 18f * subDt);
                            c.angle += c.vAng * subDt;

                            float moveDist = Mathf.Sqrt(moveSq);
                            float phase = c.angle * Mathf.Deg2Rad;
                            c.rotX = Mathf.Clamp(c.rotX + Mathf.Sin(phase) * moveDist * 70f, -26f, 26f);
                            c.rotY = Mathf.Clamp(c.rotY + Mathf.Cos(phase) * moveDist * 70f, -26f, 26f);
                        }
                    }
                    else
                    {
                        c.vx *= 0.5f;
                        c.vy *= 0.5f;
                        c.vAng *= 0.5f;
                    }

                    c.targetX = c.x;
                    c.targetY = c.y;
                }
            }

            return anyMoved;
        }

        /// <summary>
        /// Projects (x, y) onto the 2D Minkowski-eroded inner glass boundary along the true sloped wall normal,
        /// so cubes resting on diagonal/curved glass floors are supported perpendicular to the glass surface!
        /// </summary>
        private void ProjectOntoSlopedGlassWalls(ref float x, ref float y, float spoutFloorY)
        {
            if (y < spoutFloorY) y = spoutFloorY;
            GetSafeCubeBoundsAtY(y, out float minX, out float maxX);
            if (maxX < minX)
            {
                float mid = (minX + maxX) * 0.5f;
                minX = mid - 0.012f;
                maxX = mid + 0.012f;
            }

            if (x < minX)
            {
                float penX = minX - x;
                float sampleDy = 0.02f;
                GetSafeCubeBoundsAtY(y + sampleDy, out float minXUp, out _);
                GetSafeCubeBoundsAtY(Mathf.Max(spoutFloorY, y - sampleDy), out float minXDn, out _);
                float slopeL = (minXUp - minXDn) / (2f * sampleDy);
                if (slopeL < -0.05f)
                {
                    float lift = Mathf.Clamp(-slopeL / (1f + slopeL * slopeL), 0f, 0.48f);
                    y += penX * lift * 0.70f;
                    GetSafeCubeBoundsAtY(y, out minX, out maxX);
                }
                x = Mathf.Clamp(x, minX, Mathf.Max(minX, maxX));
            }
            else if (x > maxX)
            {
                float penX = x - maxX;
                float sampleDy = 0.02f;
                GetSafeCubeBoundsAtY(y + sampleDy, out _, out float maxXUp);
                GetSafeCubeBoundsAtY(Mathf.Max(spoutFloorY, y - sampleDy), out _, out float maxXDn);
                float slopeR = (maxXUp - maxXDn) / (2f * sampleDy);
                if (slopeR > 0.05f)
                {
                    float lift = Mathf.Clamp(slopeR / (1f + slopeR * slopeR), 0f, 0.48f);
                    y += penX * lift * 0.70f;
                    GetSafeCubeBoundsAtY(y, out minX, out maxX);
                }
                x = Mathf.Clamp(x, Mathf.Min(minX, maxX), maxX);
            }
        }

        /// <summary>
        /// Settles all particles in the container:
        /// - When instantSnap is true (initial spawn or Edit Mode shape change), performs a bottom-to-top laminar pass
        ///   followed by multi-step PBD granular relaxation so cubes rest in a realistic, organic bed with zero overlap.
        /// </summary>
        public void RecomputeLaminarTargets(bool instantSnap)
        {
            if (_bottleCubes == null || _bottleCubes.Count == 0) return;

            // In Pixel Art mode with freezePatternInPlace, do not disturb the exact pixel art grid formation!
            if (particleMode == ParticleArrangementMode.PixelArtVisual && freezePatternInPlace)
            {
                return;
            }

            if (!instantSnap)
            {
                StepGranularPhysics(0.016f, false);
                return;
            }

            // Sort from lowest to highest so lower particles act as a stable foundation
            _bottleCubes.Sort((a, b) => a.targetY.CompareTo(b.targetY));

            float beadRadius = particleSize * 0.50f;
            float minDist = particleSize * 1.055f;
            float minDistSq = minDist * minDist;
            float spoutFloorY = spoutBottomY + beadRadius + 0.025f;
            float stepY = particleSize * 0.22f;
            float diagStepX = particleSize * 0.26f;
            int maxMicroSteps = 16;

            for (int i = 0; i < _bottleCubes.Count; i++)
            {
                var c = _bottleCubes[i];
                float cx = c.targetX;
                float cy = c.targetY;

                // 1. Ensure current position is strictly inside the 2D Minkowski-eroded inner glass walls
                ClampToInnerGlassWalls(ref cx, ref cy, beadRadius, spoutFloorY);

                // 2. If clamping or preset morphing caused an overlap with an already-settled lower cube, lift cleanly first
                if (OverlapsAnySettledLowerCube(i, cx, cy, minDistSq))
                {
                    ResolveOverlapByLifting(i, ref cx, ref cy, minDistSq, spoutFloorY);
                }

                // 3. Step downward / diagonally along the funnel & inner walls into the lowest available pocket
                for (int step = 0; step < maxMicroSteps; step++)
                {
                    float nextY = cy - stepY;
                    if (nextY < spoutFloorY) nextY = spoutFloorY;
                    if (cy - nextY < 0.0005f) break;

                    GetSafeCubeBoundsAtY(nextY, out float lX, out float rX);
                    float centerLineX = (lX + rX) * 0.5f;
                    float dirToCenter = (cx < centerLineX - 0.005f) ? 1f : (cx > centerLineX + 0.005f ? -1f : 0f);

                    float funnelDrift = 0f;
                    if (cy < spoutBottomY + 1.25f && Mathf.Abs(cx - centerLineX) > 0.015f)
                    {
                        funnelDrift = dirToCenter * (stepY * 0.55f);
                    }

                    float wallHugX = cx + funnelDrift;
                    float wallHugY = nextY;
                    ClampToInnerGlassWalls(ref wallHugX, ref wallHugY, beadRadius, spoutFloorY);

                    float[] candXs = new float[]
                    {
                        cx + funnelDrift,
                        cx,
                        cx + dirToCenter * diagStepX,
                        cx - dirToCenter * diagStepX,
                        cx + dirToCenter * (diagStepX * 1.85f),
                        cx + dirToCenter * (diagStepX * 3.10f),
                        wallHugX
                    };

                    bool movedDown = false;
                    for (int k = 0; k < candXs.Length; k++)
                    {
                        float testX = candXs[k];
                        float testY = nextY;

                        if (Mathf.Abs(testX - cx) > particleSize * 1.45f)
                            continue;

                        if (!IsInsideInnerGlassWalls(testX, testY, beadRadius, spoutFloorY))
                            continue;

                        if (!OverlapsAnySettledLowerCube(i, testX, testY, minDistSq))
                        {
                            cx = testX;
                            cy = testY;
                            movedDown = true;
                            break;
                        }
                    }

                    if (!movedDown)
                    {
                        break;
                    }
                }

                c.targetX = cx;
                c.targetY = cy;
                c.x = cx;
                c.y = cy;
                c.vx = 0f;
                c.vy = 0f;
            }

            // 4. Run 16 sub-steps of PBD granular relaxation so the resting bed packs naturally against sloped walls
            for (int r = 0; r < 16; r++)
            {
                StepGranularPhysics(0.012f, true);
            }

            // 5. Final strict non-overlap & safe wall guarantee
            _bottleCubes.Sort((a, b) => a.y.CompareTo(b.y));
            for (int i = 0; i < _bottleCubes.Count; i++)
            {
                var c = _bottleCubes[i];
                float cx = c.x;
                float cy = c.y;
                ClampToInnerGlassWalls(ref cx, ref cy, beadRadius, spoutFloorY);
                if (OverlapsAnySettledLowerCube(i, cx, cy, minDistSq * 0.96f))
                {
                    ResolveOverlapByLifting(i, ref cx, ref cy, minDistSq * 0.96f, spoutFloorY);
                }
                c.x = cx;
                c.y = cy;
                c.targetX = cx;
                c.targetY = cy;
                c.vx = 0f;
                c.vy = 0f;
            }
        }

        private void ResolveOverlapByLifting(int currentIndex, ref float cx, ref float cy, float minDistSq, float spoutFloorY)
        {
            float liftUnit = particleSize * 0.22f;
            float latUnit = particleSize * 0.26f;

            for (int lift = 0; lift <= 80; lift++)
            {
                float testY = Mathf.Max(spoutFloorY, cy + lift * liftUnit);
                GetSafeCubeBoundsAtY(testY, out float minX, out float maxX);
                if (maxX < minX) continue;

                float baseX = Mathf.Clamp(cx, minX, maxX);
                if (!OverlapsAnySettledLowerCube(currentIndex, baseX, testY, minDistSq))
                {
                    cx = baseX;
                    cy = testY;
                    return;
                }

                float span = maxX - minX;
                int maxLatSteps = Mathf.CeilToInt(span / latUnit) + 1;
                for (int s = 1; s <= maxLatSteps; s++)
                {
                    float offset = s * latUnit;

                    float xRight = baseX + offset;
                    if (xRight <= maxX && !OverlapsAnySettledLowerCube(currentIndex, xRight, testY, minDistSq))
                    {
                        cx = xRight;
                        cy = testY;
                        return;
                    }

                    float xLeft = baseX - offset;
                    if (xLeft >= minX && !OverlapsAnySettledLowerCube(currentIndex, xLeft, testY, minDistSq))
                    {
                        cx = xLeft;
                        cy = testY;
                        return;
                    }
                }
            }
        }

        private void ClampToInnerGlassWalls(ref float x, ref float y, float beadRadius, float spoutFloorY)
        {
            if (y < spoutFloorY) y = spoutFloorY;
            GetSafeCubeBoundsAtY(y, out float minX, out float maxX);
            if (maxX < minX)
            {
                float mid = (minX + maxX) * 0.5f;
                minX = mid - 0.012f;
                maxX = mid + 0.012f;
            }
            x = Mathf.Clamp(x, minX, maxX);
        }

        private bool IsInsideInnerGlassWalls(float x, float y, float beadRadius, float spoutFloorY)
        {
            if (y < spoutFloorY - 0.001f) return false;
            GetSafeCubeBoundsAtY(y, out float minX, out float maxX);
            if (maxX < minX)
            {
                float mid = (minX + maxX) * 0.5f;
                minX = mid - 0.015f;
                maxX = mid + 0.015f;
            }
            return x >= minX - 0.0005f && x <= maxX + 0.0005f;
        }

        private bool OverlapsAnySettledLowerCube(int currentIndex, float testX, float testY, float minDistSq)
        {
            // Check ALL already-settled cubes (0 .. currentIndex - 1) without early-exit break,
            // because settled cubes may have dropped into different depths during the pass!
            for (int j = currentIndex - 1; j >= 0; j--)
            {
                var other = _bottleCubes[j];
                float dy = testY - other.targetY;
                if (dy * dy >= minDistSq)
                {
                    continue;
                }
                float dx = testX - other.targetX;
                if (dx * dx + dy * dy < minDistSq)
                {
                    return true;
                }
            }
            return false;
        }

        private GameObject SpawnConveyorCube(Vector3 localPos, PuzzleColor col, float rotX, float rotY, float angleZ)
        {
            EnsureMaterials();

            GameObject cube = new GameObject($"FlaskBead_{col}");
            if (_cubesContainer != null)
                cube.transform.SetParent(_cubesContainer.transform, false);
            else
                cube.transform.SetParent(transform, false);

            cube.transform.localPosition = localPos;
            cube.transform.localRotation = Quaternion.Euler(rotX, rotY, angleZ);

            MeshFilter mf = cube.AddComponent<MeshFilter>();
            mf.sharedMesh = _roundedCubeMesh;

            MeshRenderer mr = cube.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _cubeMaterials[col];
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            return cube;
        }

        public BottleCube GetFrontCube()
        {
            if (_bottleCubes == null || _bottleCubes.Count == 0) return null;

            // In Pixel Art mode with allowCollectAnyColorFromPattern:
            // If any monsters are currently in active slots waiting for cubes, pick the lowest cube matching one of them!
            if (particleMode == ParticleArrangementMode.PixelArtVisual && allowCollectAnyColorFromPattern)
            {
                var mgr = PuzzleGlassGameManager.Instance != null ? PuzzleGlassGameManager.Instance : FindFirstObjectByType<PuzzleGlassGameManager>();
                if (mgr != null && mgr.slotSystem != null && mgr.slotSystem.slots != null)
                {
                    List<PuzzleColor> slottedColors = new List<PuzzleColor>();
                    for (int s = 0; s < mgr.slotSystem.slots.Count; s++)
                    {
                        var slot = mgr.slotSystem.slots[s];
                        if (slot != null && slot.currentMonster != null && slot.currentMonster.remainingCount > 0)
                        {
                            slottedColors.Add(slot.currentMonster.colorType);
                        }
                    }

                    if (slottedColors.Count > 0)
                    {
                        BottleCube bestMatch = null;
                        float bestScore = float.MaxValue;
                        for (int i = 0; i < _bottleCubes.Count; i++)
                        {
                            var bc = _bottleCubes[i];
                            if (bc != null && bc.gameObject != null && slottedColors.Contains(bc.color))
                            {
                                float score = bc.y + Mathf.Abs(bc.x) * 0.12f;
                                if (score < bestScore)
                                {
                                    bestScore = score;
                                    bestMatch = bc;
                                }
                            }
                        }
                        if (bestMatch != null) return bestMatch;
                    }
                }
            }

            BottleCube lowest = null;
            float lowestScore = float.MaxValue;
            for (int i = 0; i < _bottleCubes.Count; i++)
            {
                var bc = _bottleCubes[i];
                if (bc != null && bc.gameObject != null)
                {
                    float score = bc.y + Mathf.Abs(bc.x) * 0.12f;
                    if (score < lowestScore)
                    {
                        lowestScore = score;
                        lowest = bc;
                    }
                }
            }
            return lowest;
        }

        public PuzzleColor? GetFrontCubeColor()
        {
            var front = GetFrontCube();
            return front?.color;
        }

        public bool ConsumeAndLaunchCube(MonsterCharacter targetMonster, PuzzleColor color, Material cubeMat, Mesh cubeMesh)
        {
            if (_bottleCubes == null || _bottleCubes.Count == 0 || _cubesContainer == null)
            {
                RebuildContainer();
            }

            for (int i = _bottleCubes.Count - 1; i >= 0; i--)
            {
                if (_bottleCubes[i].gameObject == null)
                {
                    _bottleCubes.RemoveAt(i);
                }
            }

            if (_bottleCubes.Count == 0) return false;

            // STRICT TIP RULE: Only the single cube closest to the tip/mouth of the bottle can come down!
            // Cubes above it can NEVER pass through the front cube!
            BottleCube targetCube = GetFrontCube();
            if (targetCube == null || targetCube.gameObject == null || targetCube.color != color)
            {
                return false;
            }

            // Ensure the cube has rolled into the spout/throat region before launching it (only in physics mode)
            if (particleMode != ParticleArrangementMode.PixelArtVisual && Application.isPlaying && targetCube.y > spoutBottomY + 0.78f)
            {
                _activeSettleTimer = 1.25f;
                return false;
            }

            targetCube.isFrozen = false;
            float inheritedVy = Mathf.Min(-2.6f, targetCube.vy);
            _bottleCubes.Remove(targetCube);

            // Immediately wake up the column above the vacated spout spot with downward momentum (zero start hesitation!)
            for (int i = 0; i < _bottleCubes.Count; i++)
            {
                var bc = _bottleCubes[i];
                if (bc != null && bc.y < spoutBottomY + 1.35f)
                {
                    bc.vy = Mathf.Min(bc.vy, -1.65f);
                }
            }

            GameObject cubeObj = targetCube.gameObject;
            Vector3 startWorldPos = cubeObj.transform.position;
            Vector3 spoutWorldPos = GetSpoutExitWorldPosition();

            cubeObj.transform.SetParent(null, true);
            cubeObj.transform.localScale = Vector3.one;
            string monsterId = (targetMonster != null) ? targetMonster.monsterId : "Unknown";
            cubeObj.name = $"FlyingBead_{targetCube.color}_To_{monsterId}";

            Material mat = _cubeMaterials.ContainsKey(targetCube.color) ? _cubeMaterials[targetCube.color] : cubeMat;

            FlyingCube flying = cubeObj.GetComponent<FlyingCube>();
            if (flying == null) flying = cubeObj.AddComponent<FlyingCube>();
            flying.LaunchThroughGlassTrack(
                new Vector3[] { startWorldPos, spoutWorldPos },
                spoutWorldPos,
                targetMonster,
                targetCube.color,
                mat,
                cubeMesh,
                1,
                inheritedVy);

            SettleRemainingCubesAnimated();

            return true;
        }

        public void SettleRemainingCubesAnimated()
        {
            if (!gameObject.activeInHierarchy || _bottleCubes == null || _bottleCubes.Count == 0) return;

            if (Application.isPlaying)
            {
                _activeSettleTimer = 1.25f;
            }
            else
            {
                RecomputeLaminarTargets(true);
                SyncCubeTransforms();
            }
        }

        public void AdvanceTrackTrain()
        {
            SettleRemainingCubesAnimated();
        }

        public Vector3 GetSpoutExitWorldPosition()
        {
            if (spoutExitPoint != null)
            {
                return spoutExitPoint.position;
            }
            if (_trackRootObj != null)
            {
                return _trackRootObj.transform.TransformPoint(new Vector3(0f, spoutBottomY - 0.04f, -0.10f));
            }
            return transform.position + new Vector3(0f, -0.85f, -0.70f);
        }

        public void ConsumeCube(PuzzleColor color)
        {
            SettleRemainingCubesAnimated();
        }

        public void PulseSpout()
        {
            if (!gameObject.activeInHierarchy) return;

            if (spoutExitPoint != null)
            {
                spoutExitPoint.DOKill(false);
            }
        }
    }
}
