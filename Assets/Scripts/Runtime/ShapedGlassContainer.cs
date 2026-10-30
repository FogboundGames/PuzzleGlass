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
        CustomFreeform = 9
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
        public GlassShapePreset shapePreset = GlassShapePreset.DiamondCarafe;
        public bool symmetricMode = true;
        [Range(4, 24)]
        public int curveSubdivisions = 14;
        [Range(0.5f, 1.8f)]
        public float widthScale = 1.0f;
        [Range(0.6f, 1.6f)]
        public float heightScale = 1.0f;

        [Header("Glass Contour Control Points (Bottom Spout -> Top Neck)")]
        public List<GlassContourPoint> contourPoints = new List<GlassContourPoint>();

        [Header("Glass Color & Material Aesthetics")]
        public GlassColorTheme colorTheme = GlassColorTheme.SkyBlueGlossy;
        public Color glassBodyColor = new Color(0.36f, 0.71f, 0.98f, 0.78f);
        public Color glassCenterGlowColor = new Color(0.76f, 0.91f, 1.00f, 0.20f);
        public Color glassOuterRimColor = new Color(0.17f, 0.49f, 0.85f, 0.92f);
        public Color glassInnerRimColor = new Color(0.96f, 0.99f, 1.00f, 0.95f);
        public Color glassFrontLensColor = new Color(0.95f, 0.99f, 1.00f, 0.00f);
        public Color earHandleColor = new Color(0.92f, 0.96f, 1.00f, 1.00f);

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
        [Range(0.08f, 0.22f)]
        public float particleSize = 0.155f;
        [Range(0.15f, 0.85f)]
        public float particleFillRatio = 0.48f;
        [Range(0, 20)]
        public int floatingBeadCount = 0;
        [Range(1, 8)]
        public int particlesPerMonsterCount = 1;

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
            if (particleSize < 0.145f) particleSize = 0.155f;
            EnsureContourInitialized();
            RebuildContainer();
        }

        private void OnEnable()
        {
            particlesPerMonsterCount = 1;
            if (particleSize < 0.145f) particleSize = 0.155f;
            EnsureContourInitialized();
            if (_bottleCubes == null || _bottleCubes.Count == 0 || _cubesContainer == null)
            {
                RebuildContainer();
            }
        }

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
            }
        }

        public void ValidateBottleDimensions()
        {
            EnsureContourInitialized();
            SyncBoundsFromContour();
        }

        private void SyncBoundsFromContour()
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

        private void EnsureMaterials()
        {
#if UNITY_EDITOR
            if (glassMaterial == null)
                glassMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Mat_GlassPane.mat");
            if (glassRimMaterial == null)
                glassRimMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Mat_GlassHighlight.mat");
#endif
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
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
                _chamberInnerGlowMat.renderQueue = 2990;
            }
            else
            {
                _chamberInnerGlowMat = CreateConfiguredGlassMaterial(
                    "Mat_FlaskCenterGlow",
                    glassCenterGlowColor,
                    0.98f,
                    2992);
            }

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
            _earHandleMat = new Material(litShader);
            _earHandleMat.name = "Mat_FlaskShoulderCapWhite";
            Color capWhite = new Color(0.98f, 0.96f, 0.93f, 1.0f);
            _earHandleMat.color = capWhite;
            if (_earHandleMat.HasProperty("_BaseColor")) _earHandleMat.SetColor("_BaseColor", capWhite);
            if (_earHandleMat.HasProperty("_Smoothness")) _earHandleMat.SetFloat("_Smoothness", 0.78f);

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

            // 3. Optional Creamy-White Shoulder Caps with Colored Pill Buttons (Left=Purple, Right=Yellow)
            if (showShoulderHandles)
            {
                BuildShoulderHandleEar(true, leftPath);
                BuildShoulderHandleEar(false, rightPath);
            }
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
        /// matching the active monsters in the scene!
        /// </summary>
        public void PopulateBottlePhysics()
        {
            if (_bottleCubes == null) _bottleCubes = new List<BottleCube>();
            for (int i = 0; i < _bottleCubes.Count; i++)
            {
                if (_bottleCubes[i]?.gameObject != null)
                {
                    if (Application.isPlaying) Destroy(_bottleCubes[i].gameObject);
                    else DestroyImmediate(_bottleCubes[i].gameObject);
                }
            }
            _bottleCubes.Clear();

            int ratio = Mathf.Max(1, particlesPerMonsterCount);
            List<(PuzzleColor color, int count)> colorBands = new List<(PuzzleColor, int)>();

            var manager = PuzzleGlassGameManager.Instance != null
                ? PuzzleGlassGameManager.Instance
                : FindFirstObjectByType<PuzzleGlassGameManager>();

            if (manager != null && manager.activeMonsters != null && manager.activeMonsters.Count > 0)
            {
                for (int i = 0; i < manager.activeMonsters.Count; i++)
                {
                    var m = manager.activeMonsters[i];
                    if (m != null)
                    {
                        int baseCount = (m.initialCount > 0) ? m.initialCount : Mathf.Max(1, m.remainingCount);
                        colorBands.Add((m.colorType, baseCount * ratio));
                    }
                }
            }

            if (colorBands.Count == 0)
            {
                colorBands.Add((PuzzleColor.Blue, 10));
                colorBands.Add((PuzzleColor.Yellow, 20));
            }

            int totalMonsterCubes = 0;
            for (int i = 0; i < colorBands.Count; i++) totalMonsterCubes += colorBands[i].count;

            // If there are only a few monster cubes (e.g., 10 Blue + 20 Yellow = 30),
            // add upper remaining color bands so that after the 10 or 20 cubes are collected, the rest stay in the bottle!
            if (totalMonsterCubes <= 35)
            {
                colorBands.Add((PuzzleColor.Red, 15));
                colorBands.Add((PuzzleColor.Purple, 15));
            }

            int totalNeeded = 0;
            for (int i = 0; i < colorBands.Count; i++) totalNeeded += colorBands[i].count;

            float colSpacing = Mathf.Clamp(particleSize * 1.09f, 0.10f, 0.24f);
            float rowSpacing = Mathf.Clamp(particleSize * 0.96f, 0.09f, 0.21f);
            float beadRadius = particleSize * 0.50f;

            // Generate staggered honeycomb grid slots strictly inside the 2D Minkowski-eroded safe glass walls
            List<Vector2> candidateSlots = new List<Vector2>(totalNeeded + 64);

            // 1. Spout vertical channel slots
            for (float y = spoutBottomY + beadRadius + 0.03f; y < spoutBottomY + 0.65f; y += rowSpacing * 1.02f)
            {
                GetSafeCubeBoundsAtY(y, out float minX, out float maxX);
                float midX = (minX + maxX) * 0.5f;
                int rowIdx = candidateSlots.Count;
                float xOff = Mathf.Clamp((rowIdx % 2 == 0) ? -0.012f : 0.012f, minX - midX, maxX - midX);
                candidateSlots.Add(new Vector2(midX + xOff, y));
            }

            // 2. Main chamber honeycomb grid slots using true 2D safe wall clearance
            float startChamberY = spoutBottomY + 0.68f;
            float maxChamberY = neckTopY - 0.12f;
            int rIdx = 0;
            for (float y = startChamberY; y <= maxChamberY && candidateSlots.Count < totalNeeded + 60; y += rowSpacing)
            {
                GetSafeCubeBoundsAtY(y, out float minX, out float maxX);
                if (maxX <= minX + 0.01f) continue;

                float stagger = (rIdx % 2 == 1) ? colSpacing * 0.5f : 0f;
                for (float x = minX + stagger; x <= maxX; x += colSpacing)
                {
                    candidateSlots.Add(new Vector2(x, y));
                }
                rIdx++;
            }

            // Sort candidate slots according to the chosen layerPattern (VFunnelBands, HorizontalWaves, LeftRightSplit, DiagonalBands)
            candidateSlots.Sort((a, b) =>
            {
                float scoreA = EvaluateLayerSortScore(a);
                float scoreB = EvaluateLayerSortScore(b);
                return scoreA.CompareTo(scoreB);
            });

            int slotCursor = 0;
            for (int b = 0; b < colorBands.Count; b++)
            {
                PuzzleColor bandColor = colorBands[b].color;
                int bandCount = colorBands[b].count;

                for (int k = 0; k < bandCount; k++)
                {
                    Vector2 pos;
                    if (slotCursor < candidateSlots.Count)
                    {
                        pos = candidateSlots[slotCursor];
                    }
                    else
                    {
                        float extraY = startChamberY + (slotCursor * 0.025f);
                        GetSafeCubeBoundsAtY(extraY, out float sMinX, out float sMaxX);
                        pos = new Vector2((sMinX + sMaxX) * 0.5f, extraY);
                    }
                    slotCursor++;

                    // Deterministic, organic 3D tumble angles so each cube shows beveled 3D facets like Image 2
                    float seed = (slotCursor * 1.6180339f);
                    float rx = Mathf.Sin(seed * 3.1f) * 20f;
                    float ry = Mathf.Cos(seed * 2.3f) * 22f;
                    float rz = Mathf.Sin(seed * 4.7f) * 38f;

                    _bottleCubes.Add(new BottleCube
                    {
                        color = bandColor,
                        layerOrder = b,
                        isFrozen = false,
                        x = pos.x,
                        y = pos.y,
                        targetX = pos.x,
                        targetY = pos.y,
                        rotX = rx,
                        rotY = ry,
                        angle = rz
                    });
                }
            }

            // Settle initial resting positions cleanly from bottom to top without any upward bounce or color mixing
            RecomputeLaminarTargets(true);

            // Instantiate GameObjects
            for (int i = 0; i < _bottleCubes.Count; i++)
            {
                var bc = _bottleCubes[i];
                Vector3 localPos = new Vector3(bc.x, bc.y, -0.10f);
                bc.gameObject = SpawnConveyorCube(localPos, bc.color, bc.rotX, bc.rotY, bc.angle);
            }
        }

        private float EvaluateLayerSortScore(Vector2 p)
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
                        ProjectOntoSlopedGlassWalls(ref c.x, ref c.y, spoutFloorY);
                    }

                    for (int i = 0; i < count; i++)
                    {
                        var ci = _bottleCubes[i];
                        for (int j = 0; j < i; j++)
                        {
                            var cj = _bottleCubes[j];
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
                                ci.x += nx * overlap * 0.92f;
                                ci.y += ny * overlap * 0.96f;

                                // Subtle lateral packing response on lower cube if within the same chamber band
                                if (cj.y > spoutTopChannelY && cj.layerOrder == ci.layerOrder)
                                {
                                    cj.x -= nx * overlap * 0.12f;
                                    ClampToInnerGlassWalls(ref cj.x, ref cj.y, beadRadius, spoutFloorY);
                                }
                            }
                        }

                        if (activeBandInChamber && ci.layerOrder > minLayerOrder && ci.y < throatGateY)
                        {
                            ci.y = throatGateY;
                        }
                        ProjectOntoSlopedGlassWalls(ref ci.x, ref ci.y, spoutFloorY);
                    }
                }

                // 4. Derive Constrained Physical Velocity & Update 3D Rolling Orientation
                for (int i = 0; i < count; i++)
                {
                    var c = _bottleCubes[i];
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

            // Ensure the cube has rolled into the spout/throat region before launching it
            if (Application.isPlaying && targetCube.y > spoutBottomY + 0.78f)
            {
                _activeSettleTimer = 1.25f;
                return false;
            }

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
