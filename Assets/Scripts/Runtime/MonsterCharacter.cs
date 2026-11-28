using System;
using System.Collections;
using TMPro;
using UnityEngine;
using DG.Tweening;

namespace PuzzleGlass
{
    /// <summary>Blok (taşıyıcı kutu) görünümü.</summary>
    public enum BlockVisualStyle
    {
        PuzzleWithSockets = 0, // Puzzle parçası: beyaz plaka, toplarla dolan gri delikler
        PuzzleWithNumber = 1,  // Boşluklu kare kutu + ortada kalan top sayısı (varsayılan)
        Crate = 2              // Kasa görünümü
    }

    [ExecuteAlways]
    public class MonsterCharacter : MonoBehaviour
    {
        public const int MaxVisualSockets = 16;
        public const int MaxHollowPitBalls = 30;

        [Tooltip("Bloğun görünümü. Oyunda GameManager'daki 'Block Visual Style' ayarından gelir.")]
        public BlockVisualStyle visualStyle = BlockVisualStyle.PuzzleWithNumber;

        [SerializeField, HideInInspector] private int _hollowStyleMigrated = 0;
        private BlockVisualStyle _builtVisualStyle = (BlockVisualStyle)(-1);

        /// <summary>Görünümü ayarlar (görselleri yeniden kurmaz; ardından RebuildMonsterVisuals ya da Initialize çağrılmalı).</summary>
        public void SetVisualStyle(BlockVisualStyle style)
        {
            _hollowStyleMigrated = 2;
            visualStyle = style;
            // useCrateVisuals = "sayaçlı" davranış: toplar ortaya uçar, ortada kalan sayı yazar (delik yok)
            useCrateVisuals = style != BlockVisualStyle.PuzzleWithSockets;
        }

        private void EnsureHollowVisualStyleSync()
        {
            if (_hollowStyleMigrated < 2)
            {
                _hollowStyleMigrated = 2;
                visualStyle = BlockVisualStyle.PuzzleWithNumber;
            }
            if (PuzzleGlassGameManager.Instance != null)
            {
                PuzzleGlassGameManager.Instance.EnsureHollowBlockStyleMigrated();
                visualStyle = PuzzleGlassGameManager.Instance.blockVisualStyle;
            }
            useCrateVisuals = visualStyle != BlockVisualStyle.PuzzleWithSockets;
        }

        [Header("Crate Presentation")]
        [Tooltip("Açık: sayaçlı görünüm (ortada büyük sayı). Kapalı: eski puzzle parçası görünümü " +
                 "(beyaz plaka + renkli çerçeve + toplarla dolan gri delikler). Oyunda GameManager'daki ayar kullanılır.")]
        public bool useCrateVisuals = true;

        private float _socketBallLocalDiameter = 0.188f;

        private static readonly int BodyColorPropId = Shader.PropertyToID("_BodyColor");
        private static readonly int BodyDarkColorPropId = Shader.PropertyToID("_BodyDarkColor");
        private static readonly int OutlineColorPropId = Shader.PropertyToID("_OutlineColor");
        private static readonly int HighlightColorPropId = Shader.PropertyToID("_HighlightColor");
        private static readonly int LocalSizePropId = Shader.PropertyToID("_LocalSize");
        private static readonly int RectCountPropId = Shader.PropertyToID("_RectCount");
        private static readonly int Rect0PropId = Shader.PropertyToID("_Rect0");
        private static readonly int Rect1PropId = Shader.PropertyToID("_Rect1");
        private static readonly int WallInsetPropId = Shader.PropertyToID("_WallInset");
        private static readonly int CornerRadPropId = Shader.PropertyToID("_CornerRad");
        private static readonly int InnerRadPropId = Shader.PropertyToID("_InnerRad");
        private static readonly int OutlineThicknessPropId = Shader.PropertyToID("_OutlineThickness");
        private static readonly int LipOffsetPropId = Shader.PropertyToID("_LipOffset");
        private static readonly int CrateModePropId = Shader.PropertyToID("_CrateMode");
        private static readonly int ExitHighlightPropId = Shader.PropertyToID("_ExitHighlight");
        private static readonly int DragHighlightPropId = Shader.PropertyToID("_DragHighlight");
        private static readonly int IceAmountPropId = Shader.PropertyToID("_IceAmount");
        private static readonly int LockAmountPropId = Shader.PropertyToID("_LockAmount");
        private static readonly int LockOpenPropId = Shader.PropertyToID("_LockOpen");
        private static readonly int LockWigglePropId = Shader.PropertyToID("_LockWiggle");
        private static readonly int LockCenterPropId = Shader.PropertyToID("_LockCenter");
        private static readonly int SocketCountPropId = Shader.PropertyToID("_SocketCount");
        private static readonly int SocketRadiusPropId = Shader.PropertyToID("_SocketRadius");
        private static readonly int WobblePhasePropId = Shader.PropertyToID("_WobblePhase");
        private static readonly int WobbleAmountPropId = Shader.PropertyToID("_WobbleAmount");
        private static readonly int SquashAmountPropId = Shader.PropertyToID("_SquashAmount");
        private static readonly int FillAmountPropId = Shader.PropertyToID("_FillAmount");
        private static readonly int FillWavePropId = Shader.PropertyToID("_FillWave");
        private static readonly int[] SockPairPropIds = new int[]
        {
            Shader.PropertyToID("_SockPair0"),
            Shader.PropertyToID("_SockPair1"),
            Shader.PropertyToID("_SockPair2"),
            Shader.PropertyToID("_SockPair3"),
            Shader.PropertyToID("_SockPair4"),
            Shader.PropertyToID("_SockPair5"),
            Shader.PropertyToID("_SockPair6"),
            Shader.PropertyToID("_SockPair7")
        };

        private static Material s_SharedCarrierBoxMaterial;
        private static Material s_SharedCarrierBoxFrontRimMaterial;
        private static Material s_SharedSocketBallMaterial;
        private static Material s_SharedBadgeMaterial;
        private static Material s_SharedShadowMatGrid;
        private static Material s_SharedShadowMatTray;
        private static Material s_SharedLidGlareMat;
        private static Material s_SharedFrostMat;
        private static readonly System.Collections.Generic.Dictionary<PuzzleColor, Material> s_SharedLidWindowMats = new System.Collections.Generic.Dictionary<PuzzleColor, Material>();
        private static readonly System.Collections.Generic.Dictionary<PuzzleColor, Material> s_SharedLidLatchMats = new System.Collections.Generic.Dictionary<PuzzleColor, Material>();
        private static MaterialPropertyBlock s_BoxPropertyBlock;
        private static MaterialPropertyBlock s_ShadowPropertyBlock;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSharedMaterialCaches()
        {
            s_SharedCarrierBoxMaterial = null;
            s_SharedCarrierBoxFrontRimMaterial = null;
            s_SharedSocketBallMaterial = null;
            s_SharedBadgeMaterial = null;
            s_SharedShadowMatGrid = null;
            s_SharedShadowMatTray = null;
            s_SharedLidGlareMat = null;
            s_SharedFrostMat = null;
            s_SharedLidWindowMats.Clear();
            s_SharedLidLatchMats.Clear();
        }

        private MeshRenderer _boxRenderer;
        private MeshRenderer _frontRimRenderer;
        private MeshRenderer _shadowRenderer;
        private int _builtSocketCount = -1;
        private Color _mpbBodyColor = Color.white;
        private Color _mpbBodyDarkColor = Color.gray;
        private Color _mpbOutlineColor = Color.black;
        private Color _mpbHighlightColor = Color.white;
        private Vector4 _mpbLocalSize = new Vector4(1.16f, 1.16f, 0f, 0f);
        private Vector4 _mpbShadowLocalSize = new Vector4(1.72f, 1.84f, 0f, 0f);
        private float _mpbRectCount = 1f;
        private Vector4 _mpbRect0 = new Vector4(0f, 0.024f, 0.445f, 0.425f);
        private Vector4 _mpbRect1 = Vector4.zero;
        private float _mpbExitHighlight = 1f;
        private float _mpbDragHighlight = 0f;
        private float _mpbIceAmount = 0f;
        private float _mpbLockAmount = 0f;
        private float _mpbLockOpen = 0f;
        private float _mpbLockWiggle = 0f;
        private Vector4 _mpbLockCenter = new Vector4(0f, 0f, 1.3f, 0f);
        private float _mpbSocketCount = 3f;
        private float _mpbSocketRadius = 0.108f;
        private readonly Vector4[] _mpbSockPairs = new Vector4[8];
        private float _mpbWobblePhase = 0f;
        private float _mpbWobbleAmount = 0f;
        private float _mpbSquashAmount = 0f;
        private float _mpbFillAmount = 0f;
        private float _mpbFillWave = 0f;

        public Vector4 LockCenterValue => _mpbLockCenter;
        public float LockOpenValue => _mpbLockOpen;
        public float LockAmountValue => _mpbLockAmount;
        public bool IsHollowPitBox => IsHollowPitStyle;

        public Vector3 GetPitPlungeWorldTarget(Vector3 baseSocketPos)
        {
            Transform t = bodyPivot != null ? bodyPivot : transform;
            float boxWorldZ = t.position.z;
            float boxScale = Mathf.Max(0.25f, t.lossyScale.x);
            return new Vector3(
                baseSocketPos.x,
                baseSocketPos.y - 0.045f * boxScale,
                boxWorldZ - 0.065f * boxScale);
        }

        public void SetLockWiggle(float v) { _mpbLockWiggle = v; ApplyBoxPropertyBlock(); }
        public void SetLockOpen(float v) { _mpbLockOpen = v; ApplyBoxPropertyBlock(); }
        public void SetLockAmount(float v) { _mpbLockAmount = v; ApplyBoxPropertyBlock(); }
        public void SetLockGlyphScale(float s) { _mpbLockCenter.z = s; ApplyBoxPropertyBlock(); }

        /// <summary>
        /// Returns how many visual sockets/balls this Carrier Box should display
        /// (1..30 for hollow pit boxes, 1..16 for legacy socket plates).
        /// </summary>
        public int GetVisibleSocketCount()
        {
            int raw = (initialCount > 0) ? initialCount : Mathf.Max(1, remainingCount);
            int cap = IsHollowPitStyle ? MaxHollowPitBalls : MaxVisualSockets;
            return Mathf.Clamp(raw, 1, cap);
        }

        private bool IsHollowPitStyle => visualStyle == BlockVisualStyle.PuzzleWithNumber;
        private bool ShowsCavityBalls => visualStyle != BlockVisualStyle.Crate;

        private void ComputeActiveBallLayout(int count, out Vector2[] positions, out float socketRadius, out float ballDiameter)
        {
            if (IsHollowPitStyle)
            {
                CarrierBlockShapeUtility.ComputeHollowPitBallLayout(blockShape, count, out positions, out ballDiameter);
                if (count >= 14) ballDiameter *= 0.90f;
                else if (count >= 6) ballDiameter *= 0.94f;
                socketRadius = ballDiameter * 0.5f;
                return;
            }
            CarrierBlockShapeUtility.ComputeSocketLayoutForShape(blockShape, count, out positions, out socketRadius, out ballDiameter);
        }

        private int GetSocketModCount()
        {
            return (socketTransforms != null && socketTransforms.Length > 0)
                ? socketTransforms.Length
                : GetVisibleSocketCount();
        }

        private static Vector2[] BuildSocketRows(int[] rowCounts, float xStep, float[] rowYs)
        {
            int total = 0;
            for (int r = 0; r < rowCounts.Length; r++) total += rowCounts[r];
            Vector2[] pts = new Vector2[total];
            int idx = 0;
            for (int r = 0; r < rowCounts.Length; r++)
            {
                int cols = rowCounts[r];
                float y = rowYs[r];
                float startX = -0.5f * (cols - 1) * xStep;
                for (int c = 0; c < cols; c++)
                {
                    pts[idx++] = new Vector2(startX + c * xStep, y);
                }
            }
            return pts;
        }

        /// <summary>
        /// Computes normalized socket coordinates (in shader box space), socket hole radius,
        /// and settled 3D ball local diameter inside the white front plate for 1..16 sockets.
        /// </summary>
        public static void ComputeSocketLayout(int count, out Vector2[] positions, out float socketRadius, out float ballLocalDiameter)
        {
            int c = Mathf.Clamp(count, 1, MaxVisualSockets);
            switch (c)
            {
                case 1:
                    positions = new Vector2[] { new Vector2(0f, -0.04f) };
                    socketRadius = 0.190f;
                    break;
                case 2:
                    positions = new Vector2[] { new Vector2(-0.22f, -0.04f), new Vector2(0.22f, -0.04f) };
                    socketRadius = 0.155f;
                    break;
                case 3:
                    positions = new Vector2[]
                    {
                        new Vector2(-0.23f, -0.175f),
                        new Vector2( 0.23f, -0.175f),
                        new Vector2( 0.00f,  0.140f)
                    };
                    socketRadius = 0.150f;
                    break;
                case 4:
                    positions = BuildSocketRows(new[] { 2, 2 }, 0.390f, new[] { -0.190f, 0.110f });
                    socketRadius = 0.125f;
                    break;
                case 5:
                    positions = BuildSocketRows(new[] { 3, 2 }, 0.270f, new[] { -0.175f, 0.100f });
                    socketRadius = 0.112f;
                    break;
                case 6:
                    positions = BuildSocketRows(new[] { 3, 3 }, 0.270f, new[] { -0.175f, 0.095f });
                    socketRadius = 0.110f;
                    break;
                case 7:
                    positions = BuildSocketRows(new[] { 4, 3 }, 0.215f, new[] { -0.165f, 0.085f });
                    socketRadius = 0.088f;
                    break;
                case 8:
                    positions = BuildSocketRows(new[] { 4, 4 }, 0.215f, new[] { -0.165f, 0.085f });
                    socketRadius = 0.088f;
                    break;
                case 9:
                    positions = BuildSocketRows(new[] { 3, 3, 3 }, 0.240f, new[] { -0.235f, -0.045f, 0.145f });
                    socketRadius = 0.080f;
                    break;
                case 10:
                    positions = BuildSocketRows(new[] { 3, 4, 3 }, 0.214f, new[] { -0.235f, -0.045f, 0.145f });
                    socketRadius = 0.080f;
                    break;
                case 11:
                    positions = BuildSocketRows(new[] { 4, 4, 3 }, 0.214f, new[] { -0.235f, -0.045f, 0.145f });
                    socketRadius = 0.078f;
                    break;
                case 12:
                    positions = BuildSocketRows(new[] { 4, 4, 4 }, 0.214f, new[] { -0.235f, -0.045f, 0.145f });
                    socketRadius = 0.078f;
                    break;
                case 13:
                    positions = BuildSocketRows(new[] { 4, 4, 3, 2 }, 0.200f, new[] { -0.280f, -0.125f, 0.030f, 0.185f });
                    socketRadius = 0.064f;
                    break;
                case 14:
                    positions = BuildSocketRows(new[] { 4, 4, 3, 3 }, 0.200f, new[] { -0.280f, -0.125f, 0.030f, 0.185f });
                    socketRadius = 0.064f;
                    break;
                case 15:
                    positions = BuildSocketRows(new[] { 4, 4, 4, 3 }, 0.200f, new[] { -0.280f, -0.125f, 0.030f, 0.185f });
                    socketRadius = 0.064f;
                    break;
                default:
                    positions = BuildSocketRows(new[] { 4, 4, 4, 4 }, 0.200f, new[] { -0.280f, -0.125f, 0.030f, 0.185f });
                    socketRadius = 0.064f;
                    break;
            }

            ballLocalDiameter = socketRadius * 1.26f * 0.92f;
        }

        private static Mesh _sharedSphereMesh;

        [Header("Carrier Box Identity")]
        public string monsterId;
        public PuzzleColor colorType = PuzzleColor.Blue;
        public CarrierBlockShape blockShape = CarrierBlockShape.Rect1x1;
        public int remainingCount = 10;
        public int initialCount = 10;
        public SlotPlatform assignedSlot;

        [Header("Staging Home & Grid State")]
        public Vector3 stagingHomePosition;
        public Vector3 stagingHomeScale = Vector3.one;
        public Vector3 slotFitScale = Vector3.one * 0.68f;
        public bool hasStagingHome = false;
        public int stagingColumn = 0;
        public int stagingRow = 0;
        public Vector2Int gridAnchor = Vector2Int.zero;
        public bool isUnlockedInStaging = true;
        public bool isExitClearInGrid = true;
        public bool isCollectingInGrid = false;
        public bool armedAtTopEdge = false;
        private CarrierBlockShape _builtShape = (CarrierBlockShape)(-1);

        [Header("Special Mechanics (Key / Lock / Ice)")]
        public bool hasKey = false;
        public int lockKeyCount = 0;
        public int remainingLockKeys = 0;
        public int iceCount = 0;
        public int remainingIceCount = 0;

        [Header("Filled Supply Box (Starts Full, Empties Into Glass)")]
        public bool isFilledBox = false;
        public PuzzleColor filledBallColor = PuzzleColor.Blue;

        public bool IsLockedByKey => remainingLockKeys > 0;
        public bool IsFrozenByIce => remainingIceCount > 0;
        public bool IsMovementOrCollectionBlocked => IsLockedByKey || IsFrozenByIce;

        public bool IsActivelyCollecting => !isFilledBox && (isCollectingInGrid || (assignedSlot != null && !_isMovingToSlot)) && !_isCompleted && remainingCount > 0 && !IsMovementOrCollectionBlocked && gameObject.activeInHierarchy;
        public bool IsSupplyBoxActive => isFilledBox && assignedSlot != null && !_isMovingToSlot && !_isCompleted && remainingCount > 0 && !IsMovementOrCollectionBlocked && gameObject.activeInHierarchy;

        public bool IsMovingToSlot => _isMovingToSlot;

        public Vector3 OriginalBodyScale => _originalBodyScale;

        public void SetSlotFlightGlow(float amount)
        {
            float clamped = Mathf.Clamp(amount, 0f, 2f);
            if (!Mathf.Approximately(_mpbDragHighlight, clamped))
            {
                _mpbDragHighlight = clamped;
                ApplyBoxPropertyBlock();
            }
        }

        public void PlaySlotLandingSquash()
        {
            if (bodyPivot == null || !Application.isPlaying) return;
            bodyPivot.DOKill(false);
            bodyPivot.localPosition = Vector3.zero;
            bodyPivot.localRotation = Quaternion.identity;
            Vector3 baseS = _originalBodyScale.sqrMagnitude > 0.001f ? _originalBodyScale : Vector3.one;

            Sequence s = DOTween.Sequence();
            s.Append(bodyPivot.DOScale(new Vector3(baseS.x * 1.13f, baseS.y * 0.85f, baseS.z * 1.04f), 0.055f).SetEase(Ease.OutQuad));
            s.Join(bodyPivot.DOLocalMoveY(-0.020f, 0.055f).SetEase(Ease.OutQuad));
            s.Append(bodyPivot.DOScale(new Vector3(baseS.x * 0.96f, baseS.y * 1.06f, baseS.z), 0.075f).SetEase(Ease.OutSine));
            s.Join(bodyPivot.DOLocalMoveY(0.010f, 0.075f).SetEase(Ease.OutSine));
            s.Append(bodyPivot.DOScale(baseS, 0.085f).SetEase(Ease.OutCubic));
            s.Join(bodyPivot.DOLocalMoveY(0f, 0.085f).SetEase(Ease.OutCubic));
            s.SetLink(gameObject, LinkBehaviour.KillOnDisable);
        }

        public void SetMovingToSlot(bool moving)
        {
            _isMovingToSlot = moving;
            if (moving)
            {
                SetExitClearHighlight(true);
                _hasPitMotionHistory = false;
            }
            else
            {
                if (_mpbDragHighlight > 0.001f &&
                    (MonsterDragController.Instance == null || !MonsterDragController.Instance.IsBlockCurrentlyDragged(this)))
                {
                    _mpbDragHighlight = 0f;
                    ApplyBoxPropertyBlock();
                }
                if (IsHollowPitStyle && ShowsCavityBalls)
                {
                    JostlePitBalls(new Vector2(UnityEngine.Random.Range(-0.24f, 0.24f), 0.78f), wakeAll: true);
                }
            }
            StagingGridBoard.Instance?.MarkGridDirty();
        }

        [Header("Visual Parts")]
        public Transform bodyPivot;
        public Transform mouthTransform;
        public Transform mouthTargetPoint;
        public Transform badgeTransform;
        public TextMeshPro counterText;
        public Transform eyeLeft;
        public Transform eyeRight;
        public Transform[] socketTransforms = new Transform[3];
        public GameObject[] socketBalls = new GameObject[3];
        public Transform transparentLidTransform;
        private Transform _lidGlareTransform;
        private Transform _lidGlareSubTransform;
        private Transform _lidLatchTransform;
        private Vector3 _lidClosedLocalPos = new Vector3(0f, 0f, -0.26f);
        private Vector3 _lidGlareRestLocalPos;
        private Vector3 _lidGlareSubRestLocalPos;

        private struct PitBallSim
        {
            public Vector2 pos;
            public Vector2 vel;
            public Vector2 target;
            public float angle;
            public float angularVel;
            public float impactSquash;
            public float sleepTimer;
            public bool settled;
            public bool active;
        }
        private PitBallSim[] _pitSim;
        private Vector2[] _pitTargets;
        private Vector4 _pitRect0;
        private Vector4 _pitRect1;
        private int _pitRectCount = 1;
        private bool _pitAnyActive = false;
        private Vector2 _lastPitWorldPos;
        private Vector2 _lastPitWorldVel;
        private bool _hasPitMotionHistory = false;
        private float _pitNextSpawnHintX = 0f;
        private bool _hasPitSpawnHint = false;
        private float _lastPitImpactVfxTime = -10f;

        [Header("Materials (Auto-Assigned)")]
        public Material bodyMaterial;
        public Material mouthCavityMaterial;
        public Material toothMaterial;
        public Material eyeWhiteMaterial;
        public Material pupilMaterial;
        public Material blushMaterial;
        public Material badgeMaterial;
        public Material socketBallMaterial;

        private Vector3 _originalBodyScale = Vector3.one;
        private Vector3 _originalBadgeScale = Vector3.one;
        private Tweener _idleTween;
        private Sequence _blinkSeq;
        private Tweener _fillAmountTween;
        private Sequence _nearCompleteSeq;

        /// <summary>Kutuda bu kadar (veya daha az) top kalınca her topta "neredeyse doldu" nabzı atar.</summary>
        public static int NearCompleteThreshold = 3;
        private Tweener _fillWaveTween;
        private Tweener _gulpGlowTween;
        private bool _isCompleted = false;
        public bool IsCompleted => _isCompleted;
        private bool _isMovingToSlot = false;
        private int _subParticleCounter = 0;
        private int _nextIncomingSocketCounter = 0;
        private int _settledSocketCount = 0;
        private float _lastGulpTime = -10f;
        private float _lastLandingPopVfxTime = -10f;

        private GameObject _keyOverlayRoot;
        private GameObject _lockOverlayRoot;
        private TextMeshPro _lockCountText;
        private bool _unlockAnimPlaying;
        private GameObject _iceOverlayRoot;
        private TextMeshPro _iceCountText;
        private bool _keyConsumedVisually = false;

        public event Action<MonsterCharacter> OnEatenCube;
        public event Action<MonsterCharacter> OnTargetReached;

        private void Awake()
        {
            if (string.IsNullOrEmpty(monsterId))
            {
                monsterId = $"Monster_{colorType}_{Guid.NewGuid().ToString().Substring(0, 4)}";
            }

            EnsureHollowVisualStyleSync();
            EnsureCollider();

            if (NeedsVisualRebuild())
            {
                RebuildMonsterVisuals();
            }
        }

        private void OnEnable()
        {
            EnsureHollowVisualStyleSync();
            EnsureCollider();

            if (NeedsVisualRebuild())
            {
                RebuildMonsterVisuals();
            }
            else
            {
                ApplySocketLayoutToMaterial();
                UpdateBadgeText();
            }

            if (Application.isPlaying)
            {
                StartIdleAndBlink();
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying) return;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null || Application.isPlaying) return;
                EnsureHollowVisualStyleSync();
                remainingLockKeys = Mathf.Max(0, lockKeyCount);
                remainingIceCount = Mathf.Max(0, iceCount);
                if (NeedsVisualRebuild())
                {
                    RebuildMonsterVisuals();
                }
                else
                {
                    RefreshSocketBallColors();
                    ApplySocketLayoutToMaterial();
                    UpdateBadgeText();
                    SyncFillProgressImmediate();
                    RefreshSpecialMechanicVisuals();
                }
            };
        }
#endif

        private bool NeedsVisualRebuild()
        {
            int expectedSockets = GetVisibleSocketCount();
            if (_builtShape != blockShape) return true;
            if (_builtVisualStyle != visualStyle) return true;
            if (_builtSocketCount != expectedSockets) return true;
            if (transform.childCount == 0 || bodyPivot == null) return true;
            if (transform.Find("BodyPivot/CarrierBoxVisual") == null) return true;
            if (transform.Find("BodyPivot/CarrierBoxFakeShadow") == null) return true;
            if (socketTransforms == null || socketTransforms.Length != expectedSockets || socketTransforms[0] == null) return true;
            if (socketBalls == null || socketBalls.Length != expectedSockets || socketBalls[0] == null) return true;
            if (bodyMaterial == null) return true;
            return false;
        }

        private void EnsureCollider()
        {
            CarrierBlockShapeUtility.GetRectParts(blockShape, out Vector4 r0, out Vector4 r1, out int rectCount, out _, out _);
            BoxCollider[] existing = GetComponents<BoxCollider>();

            for (int i = existing.Length; i < rectCount; i++)
            {
                gameObject.AddComponent<BoxCollider>();
            }
            existing = GetComponents<BoxCollider>();

            for (int i = 0; i < existing.Length; i++)
            {
                if (i >= rectCount)
                {
                    if (Application.isPlaying) Destroy(existing[i]);
                    else DestroyImmediate(existing[i]);
                    continue;
                }
                Vector4 r = (i == 0) ? r0 : r1;
                existing[i].enabled = true;
                existing[i].center = new Vector3(r.x, r.y, 0f);
                existing[i].size = new Vector3(r.z * 2f + 0.08f, r.w * 2f + 0.08f, 3.0f);
            }
        }

        public void SetStagingHome(Vector3 homePos, Vector3 homeScale)
        {
            stagingHomePosition = homePos;
            stagingHomeScale = homeScale;
            hasStagingHome = true;
        }

        public void SetGridState(
            Vector2Int anchor,
            Vector3 worldPos,
            Vector3 gridScale,
            Vector3 targetSlotFitScale,
            bool exitClear,
            bool animateMove = false)
        {
            gridAnchor = anchor;
            stagingColumn = anchor.x;
            stagingRow = anchor.y;
            stagingHomePosition = worldPos;
            stagingHomeScale = gridScale;
            slotFitScale = targetSlotFitScale;
            hasStagingHome = true;
            SetExitClearHighlight(exitClear);
            StagingGridBoard.Instance?.MarkGridDirty();

            if (animateMove && Application.isPlaying)
            {
                transform.DOKill(false);
                Sequence seq = DOTween.Sequence();
                seq.Join(transform.DOMove(worldPos, 0.16f).SetEase(Ease.OutCubic));
                seq.Join(transform.DOScale(gridScale, 0.16f).SetEase(Ease.OutBack));
            }
            else
            {
                transform.position = worldPos;
                transform.localScale = gridScale;
            }
        }

        public void SetExitClearHighlight(bool exitClear)
        {
            bool effectiveClear = (exitClear || isCollectingInGrid || assignedSlot != null || _isMovingToSlot) && !IsMovementOrCollectionBlocked;
            isExitClearInGrid = effectiveClear;
            isUnlockedInStaging = effectiveClear;
            float target = effectiveClear ? 1.0f : 0.0f;
            if (!Mathf.Approximately(_mpbExitHighlight, target))
            {
                _mpbExitHighlight = target;
                ApplyBoxPropertyBlock();
            }
            UpdateCounterTextPlayableTint(effectiveClear);
        }

        private void UpdateCounterTextPlayableTint(bool playable)
        {
            if (counterText == null) return;
            if (useCrateVisuals)
            {
                counterText.color = playable ? Color.white : new Color(0.86f, 0.87f, 0.90f, 0.92f);
            }
            else
            {
                Color baseTxt = PuzzleColorPalette.GetTextColor(colorType);
                if (!playable)
                {
                    float lum = baseTxt.r * 0.299f + baseTxt.g * 0.587f + baseTxt.b * 0.114f;
                    baseTxt = Color.Lerp(new Color(lum, lum, lum, 0.92f), baseTxt, 0.58f);
                }
                counterText.color = baseTxt;
            }
        }

        public void SetDragHighlight(bool isDragging)
        {
            float target = isDragging ? 1.0f : 0.0f;
            if (!Mathf.Approximately(_mpbDragHighlight, target))
            {
                _mpbDragHighlight = target;
                ApplyBoxPropertyBlock();
            }
        }

        public Vector3 GetSlotFitScale()
        {
            if (slotFitScale.sqrMagnitude <= 0.001f) return transform.localScale;
            // Slot sırası ekrana sığmak için küçüldüyse slottaki blok da aynı oranda küçülür
            float rowFit = SlotSystem.IsActive ? SlotSystem.Instance.RowFitScale : 1f;
            return slotFitScale * rowFit;
        }

        public void SetStagingQueueState(
            int col,
            int row,
            bool unlocked,
            Vector3 newHomePos,
            Vector3 baseScale,
            bool animateMove = false)
        {
            SetGridState(new Vector2Int(col, row), newHomePos, baseScale, slotFitScale, unlocked, animateMove);
        }

        private void ApplyStagingVisualState(bool unlocked)
        {
            SetExitClearHighlight(unlocked);
        }

        public void EnsureStagingHomeRecorded()
        {
            if (!hasStagingHome && assignedSlot == null)
            {
                stagingHomePosition = transform.position;
                stagingHomeScale = transform.localScale;
                hasStagingHome = true;
            }
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            KillAllTweens();
        }

        private void OnDestroy()
        {
            StopAllCoroutines();
            KillAllTweens();
        }

        public void KillAllTweens()
        {
            DOTween.Kill(this);
            transform.DOKill();
            _mpbDragHighlight = 0f;
            if (_shadowRenderer != null) _shadowRenderer.enabled = true;
            if (bodyPivot != null)
            {
                bodyPivot.DOKill();
                bodyPivot.localPosition = Vector3.zero;
                bodyPivot.localRotation = Quaternion.identity;
                bodyPivot.localScale = _originalBodyScale;
            }
            if (transparentLidTransform != null)
            {
                transparentLidTransform.DOKill();
                transparentLidTransform.localPosition = _lidClosedLocalPos;
                transparentLidTransform.localRotation = Quaternion.identity;
                transparentLidTransform.localScale = Vector3.one;
                transparentLidTransform.gameObject.SetActive(false);
            }
            if (_lidGlareTransform != null) _lidGlareTransform.DOKill();
            if (_lidGlareSubTransform != null) _lidGlareSubTransform.DOKill();
            if (_lidLatchTransform != null) _lidLatchTransform.DOKill();
            if (badgeTransform != null) badgeTransform.DOKill();
            if (counterText != null) counterText.transform.DOKill();
            if (_keyOverlayRoot != null) _keyOverlayRoot.transform.DOKill();
            if (_lockOverlayRoot != null) _lockOverlayRoot.transform.DOKill();
            if (_iceOverlayRoot != null) _iceOverlayRoot.transform.DOKill();
            if (socketBalls != null)
            {
                for (int i = 0; i < socketBalls.Length; i++)
                {
                    if (socketBalls[i] != null) socketBalls[i].transform.DOKill();
                }
            }
            if (_idleTween != null && _idleTween.IsActive()) _idleTween.Kill();
            if (_blinkSeq != null && _blinkSeq.IsActive()) _blinkSeq.Kill();
            if (_fillAmountTween != null && _fillAmountTween.IsActive()) _fillAmountTween.Kill();
            if (_fillWaveTween != null && _fillWaveTween.IsActive()) _fillWaveTween.Kill();
            if (_gulpGlowTween != null && _gulpGlowTween.IsActive()) _gulpGlowTween.Kill();
            _fillAmountTween = null;
            _fillWaveTween = null;
            _gulpGlowTween = null;
        }

        public void Initialize(PuzzleColor color, int count, string id = null, CarrierBlockShape shape = CarrierBlockShape.Rect1x1)
        {
            Initialize(color, count, id, shape, hasKey: false, lockKeyCount: 0, iceCount: 0);
        }

        public void Initialize(
            PuzzleColor color,
            int count,
            string id,
            CarrierBlockShape shape,
            bool hasKey,
            int lockKeyCount,
            int iceCount,
            bool isFilledBox = false,
            PuzzleColor filledBallColor = PuzzleColor.Blue)
        {
            colorType = color;
            blockShape = shape;
            initialCount = Mathf.Max(1, count);
            remainingCount = initialCount;
            this.hasKey = hasKey;
            this.lockKeyCount = Mathf.Max(0, lockKeyCount);
            this.remainingLockKeys = this.lockKeyCount;
            this.iceCount = Mathf.Max(0, iceCount);
            this.remainingIceCount = this.iceCount;
            this.isFilledBox = isFilledBox;
            this.filledBallColor = isFilledBox ? filledBallColor : color;
            _keyConsumedVisually = false;
            _subParticleCounter = 0;
            _nextIncomingSocketCounter = 0;
            _settledSocketCount = 0;
            _isCompleted = false;
            _fillAmountTween?.Kill();
            _fillWaveTween?.Kill();
            _mpbFillAmount = this.isFilledBox ? 1f : 0f;
            _mpbFillWave = 0f;
            monsterId = string.IsNullOrEmpty(id) ? $"Monster_{color}_{Guid.NewGuid().ToString().Substring(0, 4)}" : id;
            EnsureStagingHomeRecorded();
            EnsureCollider();
            RebuildMonsterVisuals();
            UpdateBadgeText();
        }

        [ContextMenu("Rebuild Monster Visuals")]
        public void RebuildMonsterVisuals()
        {
            EnsureHollowVisualStyleSync();
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                if (Application.isPlaying)
                    Destroy(transform.GetChild(i).gameObject);
                else
                    DestroyImmediate(transform.GetChild(i).gameObject);
            }

            _keyOverlayRoot = null;
            _lockOverlayRoot = null;
            _lockCountText = null;
            _unlockAnimPlaying = false;
            _iceOverlayRoot = null;
            _iceCountText = null;
            _nextIncomingSocketCounter = 0;
            _settledSocketCount = 0;
            _builtShape = blockShape;
            _builtVisualStyle = visualStyle;
            _fillAmountTween?.Kill();
            _fillWaveTween?.Kill();
            _mpbFillAmount = GetCurrentFillProgress();
            _mpbFillWave = 0f;

            EnsureMaterials();
            BuildCarrierBoxHierarchy();
            UpdateBadgeText();
            if (isFilledBox)
            {
                SyncFillProgressImmediate();
            }
        }

        private static Material GetSharedCarrierBoxMaterial()
        {
            if (s_SharedCarrierBoxMaterial != null) return s_SharedCarrierBoxMaterial;

            Shader boxShader = Shader.Find("PuzzleGlass/CarrierBox")
                ?? Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Sprites/Default");

            s_SharedCarrierBoxMaterial = new Material(boxShader)
            {
                name = "Mat_CarrierBox_Instanced",
                enableInstancing = true,
                color = Color.white
            };
            if (s_SharedCarrierBoxMaterial.HasProperty("_PlateColor"))
                s_SharedCarrierBoxMaterial.SetColor("_PlateColor", new Color(0.95f, 0.95f, 0.97f, 1.0f));
            if (s_SharedCarrierBoxMaterial.HasProperty("_PlateShadowColor"))
                s_SharedCarrierBoxMaterial.SetColor("_PlateShadowColor", new Color(0.74f, 0.77f, 0.84f, 1.0f));
            if (s_SharedCarrierBoxMaterial.HasProperty("_SocketColor"))
                s_SharedCarrierBoxMaterial.SetColor("_SocketColor", new Color(0.56f, 0.58f, 0.63f, 1.0f));
            if (s_SharedCarrierBoxMaterial.HasProperty("_SocketShadowColor"))
                s_SharedCarrierBoxMaterial.SetColor("_SocketShadowColor", new Color(0.40f, 0.42f, 0.49f, 1.0f));
            if (s_SharedCarrierBoxMaterial.HasProperty(WallInsetPropId))
                s_SharedCarrierBoxMaterial.SetFloat(WallInsetPropId, CarrierBlockShapeUtility.WallInset);
            if (s_SharedCarrierBoxMaterial.HasProperty(CornerRadPropId))
                s_SharedCarrierBoxMaterial.SetFloat(CornerRadPropId, CarrierBlockShapeUtility.OuterCornerRadius);
            if (s_SharedCarrierBoxMaterial.HasProperty(InnerRadPropId))
                s_SharedCarrierBoxMaterial.SetFloat(InnerRadPropId, CarrierBlockShapeUtility.InnerCornerRadius);
            if (s_SharedCarrierBoxMaterial.HasProperty(OutlineThicknessPropId))
                s_SharedCarrierBoxMaterial.SetFloat(OutlineThicknessPropId, CarrierBlockShapeUtility.OutlineThickness);
            if (s_SharedCarrierBoxMaterial.HasProperty(LipOffsetPropId))
                s_SharedCarrierBoxMaterial.SetFloat(LipOffsetPropId, CarrierBlockShapeUtility.LipDepthOffset);
            if (s_SharedCarrierBoxMaterial.HasProperty(CrateModePropId))
                s_SharedCarrierBoxMaterial.SetFloat(CrateModePropId, 1.0f);
            return s_SharedCarrierBoxMaterial;
        }

        private static Material GetSharedCarrierBoxFrontRimMaterial()
        {
            if (s_SharedCarrierBoxFrontRimMaterial != null) return s_SharedCarrierBoxFrontRimMaterial;

            Material baseMat = GetSharedCarrierBoxMaterial();
            s_SharedCarrierBoxFrontRimMaterial = new Material(baseMat)
            {
                name = "Mat_CarrierBoxFrontRim_Instanced",
                enableInstancing = true,
                renderQueue = 2996
            };
            if (s_SharedCarrierBoxFrontRimMaterial.HasProperty("_FrontRimOnly"))
                s_SharedCarrierBoxFrontRimMaterial.SetFloat("_FrontRimOnly", 1.0f);
            return s_SharedCarrierBoxFrontRimMaterial;
        }

        private static Material GetSharedSocketBallMaterial()
        {
            if (BallObjectPool.Instance != null)
            {
                return BallObjectPool.Instance.SharedInstancedBallMaterial;
            }
            if (s_SharedSocketBallMaterial != null) return s_SharedSocketBallMaterial;

            Shader ballShader = Shader.Find("PuzzleGlass/CandyCubeLit")
                ?? Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard");
            s_SharedSocketBallMaterial = new Material(ballShader)
            {
                name = "Mat_SocketBall_Instanced",
                enableInstancing = true,
                renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry + 15
            };
            return s_SharedSocketBallMaterial;
        }

        private void EnsureMaterials()
        {
            Color baseCol = GetCarrierFrameColor(colorType);
            Color darkCol = GetCarrierDarkColor(colorType);
            Color outlineCol = GetCarrierOutlineColor(colorType);
            Color highlightCol = Color.Lerp(baseCol, new Color(0.99f, 0.99f, 1.0f, 1f), 0.34f);

            bodyMaterial = GetSharedCarrierBoxMaterial();
            _mpbBodyColor = baseCol;
            _mpbBodyDarkColor = darkCol;
            _mpbOutlineColor = outlineCol;
            _mpbHighlightColor = highlightCol;
            _mpbWobblePhase = ((int)colorType * 1.37f + Mathf.Abs(GetInstanceID()) * 0.19f) % (Mathf.PI * 2f);
            ApplySocketLayoutToMaterial();

            socketBallMaterial = GetSharedSocketBallMaterial();

            if (s_SharedBadgeMaterial == null)
            {
                s_SharedBadgeMaterial = PuzzleColorPalette.CreateToonyHypercasualMaterial(
                    new Color(0.92f, 0.93f, 0.96f, 1f),
                    new Color(0.74f, 0.77f, 0.86f, 1f),
                    "Mat_BellyBadge_Shared",
                    glossyCandy: false,
                    indirectIntensity: 0.16f);
            }
            badgeMaterial = s_SharedBadgeMaterial;
        }

        private void ApplyBoxPropertyBlock()
        {
            if (_boxRenderer == null && bodyPivot != null)
            {
                Transform vis = bodyPivot.Find("CarrierBoxVisual");
                if (vis != null) _boxRenderer = vis.GetComponent<MeshRenderer>();
            }
            if (_boxRenderer == null) return;

            if (s_BoxPropertyBlock == null) s_BoxPropertyBlock = new MaterialPropertyBlock();

            s_BoxPropertyBlock.Clear();
            s_BoxPropertyBlock.SetColor(BodyColorPropId, _mpbBodyColor);
            s_BoxPropertyBlock.SetColor(BodyDarkColorPropId, _mpbBodyDarkColor);
            s_BoxPropertyBlock.SetColor(OutlineColorPropId, _mpbOutlineColor);
            s_BoxPropertyBlock.SetColor(HighlightColorPropId, _mpbHighlightColor);
            s_BoxPropertyBlock.SetVector(LocalSizePropId, _mpbLocalSize);
            s_BoxPropertyBlock.SetFloat(RectCountPropId, _mpbRectCount);
            s_BoxPropertyBlock.SetVector(Rect0PropId, _mpbRect0);
            s_BoxPropertyBlock.SetVector(Rect1PropId, _mpbRect1);
            s_BoxPropertyBlock.SetFloat(LipOffsetPropId, CarrierBlockShapeUtility.LipDepthOffset);
            s_BoxPropertyBlock.SetFloat(CrateModePropId, visualStyle == BlockVisualStyle.Crate ? 1.0f : 0.0f);
            s_BoxPropertyBlock.SetFloat(ExitHighlightPropId, _mpbExitHighlight);
            s_BoxPropertyBlock.SetFloat(DragHighlightPropId, _mpbDragHighlight);
            s_BoxPropertyBlock.SetFloat(IceAmountPropId, _mpbIceAmount);
            s_BoxPropertyBlock.SetFloat(LockAmountPropId, _mpbLockAmount);
            s_BoxPropertyBlock.SetFloat(LockOpenPropId, _mpbLockOpen);
            s_BoxPropertyBlock.SetFloat(LockWigglePropId, _mpbLockWiggle);
            s_BoxPropertyBlock.SetVector(LockCenterPropId, _mpbLockCenter);
            s_BoxPropertyBlock.SetFloat(SocketCountPropId, _mpbSocketCount);
            s_BoxPropertyBlock.SetFloat(SocketRadiusPropId, _mpbSocketRadius);
            for (int p = 0; p < SockPairPropIds.Length; p++)
            {
                s_BoxPropertyBlock.SetVector(SockPairPropIds[p], _mpbSockPairs[p]);
            }
            s_BoxPropertyBlock.SetFloat(WobblePhasePropId, _mpbWobblePhase);
            s_BoxPropertyBlock.SetFloat(WobbleAmountPropId, _mpbWobbleAmount);
            s_BoxPropertyBlock.SetFloat(SquashAmountPropId, _mpbSquashAmount);
            s_BoxPropertyBlock.SetFloat(FillAmountPropId, _mpbFillAmount);
            s_BoxPropertyBlock.SetFloat(FillWavePropId, _mpbFillWave);

            _boxRenderer.SetPropertyBlock(s_BoxPropertyBlock);

            if (_frontRimRenderer == null && bodyPivot != null)
            {
                Transform rimVis = bodyPivot.Find("CarrierBoxFrontLip");
                if (rimVis != null) _frontRimRenderer = rimVis.GetComponent<MeshRenderer>();
            }
            if (_frontRimRenderer != null)
            {
                _frontRimRenderer.SetPropertyBlock(s_BoxPropertyBlock);
            }

            ApplyShadowPropertyBlock();
        }

        private void ApplyShadowPropertyBlock()
        {
            if (_shadowRenderer == null && bodyPivot != null)
            {
                Transform sh = bodyPivot.Find("CarrierBoxFakeShadow");
                if (sh != null) _shadowRenderer = sh.GetComponent<MeshRenderer>();
            }
            if (_shadowRenderer == null) return;

            if (s_ShadowPropertyBlock == null) s_ShadowPropertyBlock = new MaterialPropertyBlock();

            s_ShadowPropertyBlock.Clear();
            s_ShadowPropertyBlock.SetVector(LocalSizePropId, _mpbShadowLocalSize);
            s_ShadowPropertyBlock.SetFloat(RectCountPropId, _mpbRectCount);
            s_ShadowPropertyBlock.SetVector(Rect0PropId, _mpbRect0);
            s_ShadowPropertyBlock.SetVector(Rect1PropId, _mpbRect1);
            s_ShadowPropertyBlock.SetFloat(CornerRadPropId, CarrierBlockShapeUtility.OuterCornerRadius);
            s_ShadowPropertyBlock.SetFloat(LipOffsetPropId, CarrierBlockShapeUtility.LipDepthOffset);
            _shadowRenderer.SetPropertyBlock(s_ShadowPropertyBlock);
        }

        private static Color GetCarrierFrameColor(PuzzleColor color)
        {
            return PuzzleColorPalette.GetBodyColor(color);
        }

        private static Color GetCarrierDarkColor(PuzzleColor color)
        {
            Color c = GetCarrierFrameColor(color);
            Color tint = PuzzleColorPalette.GetTcp2ShadowTint(color);
            return new Color(c.r * Mathf.Lerp(tint.r, 0.78f, 0.35f), c.g * Mathf.Lerp(tint.g, 0.78f, 0.35f), c.b * Mathf.Lerp(tint.b, 0.84f, 0.35f), 1f);
        }

        private static Color GetCarrierOutlineColor(PuzzleColor color)
        {
            Color c = GetCarrierFrameColor(color);
            return new Color(c.r * 0.42f, c.g * 0.44f, c.b * 0.54f, 1f);
        }

        private static Mesh GetSharedSphereMesh()
        {
            if (_sharedSphereMesh == null)
            {
                GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                var mf = temp.GetComponent<MeshFilter>();
                if (mf != null) _sharedSphereMesh = mf.sharedMesh;
                if (Application.isPlaying) Destroy(temp);
                else DestroyImmediate(temp);
            }
            return _sharedSphereMesh;
        }

        private void BuildCarrierBoxHierarchy()
        {
            // 1. Root Pivot (for squash & stretch animations)
            GameObject pivotObj = new GameObject("BodyPivot");
            pivotObj.transform.SetParent(transform, false);
            bodyPivot = pivotObj.transform;

            // 2. Carrier Box Visual Quad (renders the 3D rounded polyomino box, white inner plate & gray sockets)
            CarrierBlockShapeUtility.GetRectParts(blockShape, out Vector4 rect0, out Vector4 rect1, out int rectCount, out float quadW, out float quadH);

            GameObject boxQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            boxQuad.name = "CarrierBoxVisual";
            boxQuad.transform.SetParent(bodyPivot, false);
            boxQuad.transform.localPosition = Vector3.zero;
            boxQuad.transform.localScale = new Vector3(quadW, quadH, 1.0f);
            DestroyCollider(boxQuad);

            var boxMr = boxQuad.GetComponent<MeshRenderer>();
            boxMr.sharedMaterial = bodyMaterial;
            boxMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            boxMr.receiveShadows = false;
            _boxRenderer = boxMr;
            _frontRimRenderer = null;

            if (IsHollowPitStyle)
            {
                GameObject rimQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                rimQuad.name = "CarrierBoxFrontLip";
                rimQuad.transform.SetParent(bodyPivot, false);
                rimQuad.transform.localPosition = new Vector3(0f, 0f, -0.14f);
                rimQuad.transform.localScale = new Vector3(quadW, quadH, 1.0f);
                DestroyCollider(rimQuad);

                var rimMr = rimQuad.GetComponent<MeshRenderer>();
                rimMr.sharedMaterial = GetSharedCarrierBoxFrontRimMaterial();
                rimMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rimMr.receiveShadows = false;
                _frontRimRenderer = rimMr;
            }

            // 3. Gray Socket Landing Points & Settled 3D Balls (1..16)
            int expectedSockets = GetVisibleSocketCount();
            _builtSocketCount = expectedSockets;
            ComputeActiveBallLayout(expectedSockets, out Vector2[] socketPositions, out float socketRadius, out _socketBallLocalDiameter);
            ApplyShapeMaterialParams(rect0, rect1, rectCount, quadW, quadH);
            ApplySocketLayoutToMaterial(expectedSockets, socketPositions, socketRadius);

            GameObject socketsRoot = new GameObject("SocketsRoot");
            socketsRoot.transform.SetParent(bodyPivot, false);
            socketsRoot.transform.localPosition = Vector3.zero;
            mouthTransform = socketsRoot.transform;

            socketTransforms = new Transform[expectedSockets];
            socketBalls = new GameObject[expectedSockets];
            _pitSim = new PitBallSim[expectedSockets];
            _pitTargets = socketPositions;
            _pitRect0 = rect0;
            _pitRect1 = rect1;
            _pitRectCount = rectCount;
            _pitAnyActive = false;
            Mesh sphereMesh = GetSharedSphereMesh();
            float socketLocalZ = IsHollowPitStyle ? -0.055f : -0.10f;

            for (int i = 0; i < expectedSockets; i++)
            {
                float depth01 = (expectedSockets > 1) ? (i / (float)(expectedSockets - 1)) : 1f;
                float itemLocalZ = IsHollowPitStyle ? Mathf.Lerp(-0.032f, -0.078f, depth01) : socketLocalZ;
                float itemDepthScale = IsHollowPitStyle ? Mathf.Lerp(0.88f, 1.00f, depth01) : 1f;

                Vector2 sockPt = socketPositions[i];
                GameObject sockObj = new GameObject($"Socket_{i}");
                sockObj.transform.SetParent(socketsRoot.transform, false);
                sockObj.transform.localPosition = new Vector3(sockPt.x, sockPt.y, itemLocalZ);
                socketTransforms[i] = sockObj.transform;

                GameObject ballObj = new GameObject($"SettledBall_{i}");
                ballObj.transform.SetParent(sockObj.transform, false);
                ballObj.transform.localPosition = Vector3.zero;
                ballObj.transform.localRotation = Quaternion.Euler(18f, -24f, 0f);
                ballObj.transform.localScale = Vector3.one * (_socketBallLocalDiameter * itemDepthScale);

                var mf = ballObj.AddComponent<MeshFilter>();
                mf.sharedMesh = sphereMesh;

                var mr = ballObj.AddComponent<MeshRenderer>();
                mr.sharedMaterial = socketBallMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                PuzzleColor ballCol = isFilledBox ? filledBallColor : colorType;
                if (IsHollowPitStyle)
                {
                    BallObjectPool.ComputeBallVariantVisuals(ballCol, 1, out Color bCol, out Color sCol, out Color hCol, out float sm);
                    float dim = Mathf.Lerp(0.24f, 0f, depth01);
                    bCol = Color.Lerp(bCol, sCol, dim);
                    BallObjectPool.ApplyBallPropertyBlock(mr, bCol, sCol, hCol, sm, i * 0.65f, 0f, 0f, Vector3.up);
                }
                else
                {
                    BallObjectPool.ApplyBallPropertyBlock(mr, ballCol, variantIndex: 1, wobblePhase: i * 0.65f);
                }

                ballObj.SetActive(false);
                socketBalls[i] = ballObj;
            }

            // Default fallback target point
            GameObject mouthTarget = new GameObject("MouthTargetPoint");
            mouthTarget.transform.SetParent(bodyPivot, false);
            mouthTarget.transform.localPosition = new Vector3(0f, -0.02f, -0.10f);
            mouthTargetPoint = mouthTarget.transform;

            // 4. Glossy Transparent Snap-Lid (seals the 3 balls inside on completion!)
            BuildTransparentPackageLid(bodyPivot);

            // Hidden counter badge for compatibility
            BuildHiddenBadge(bodyPivot);

            // Soft Warm Fake Contact Drop Shadow underneath the carrier box
            BuildFakeDropShadow();

            // Key / Lock / Ice overlays (built on demand)
            RefreshSpecialMechanicVisuals();

            _originalBodyScale = bodyPivot.localScale;
            if (badgeTransform != null) _originalBadgeScale = badgeTransform.localScale;
        }

        private void BuildTransparentPackageLid(Transform parent)
        {
            Shader spriteShader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");

            CarrierBlockShapeUtility.GetRectParts(blockShape, out Vector4 r0, out Vector4 r1, out int rectCount, out _, out _);
            float maxTopY = r0.y + r0.w;
            if (rectCount > 1) maxTopY = Mathf.Max(maxTopY, r1.y + r1.w);
            float hingeY = maxTopY - CarrierBlockShapeUtility.WallInset * 0.9f;

            // Pivot at top inner rim so the lid swings shut downward from the top hinge
            GameObject lidRoot = new GameObject("BoxTransparentLid");
            lidRoot.transform.SetParent(parent, false);
            _lidClosedLocalPos = new Vector3(0f, hingeY, -0.26f);
            lidRoot.transform.localPosition = _lidClosedLocalPos;
            transparentLidTransform = lidRoot.transform;

            // Translucent crystal-tinted acrylic window covering the shape's cavity (cached per color)
            if (!s_SharedLidWindowMats.TryGetValue(colorType, out Material windowMat) || windowMat == null)
            {
                Color tint = Color.Lerp(GetCarrierFrameColor(colorType), new Color(0.92f, 0.98f, 1.00f, 1f), 0.74f);
                tint.a = 0.36f;
                windowMat = new Material(spriteShader) { name = $"Mat_BoxTransparentLid_{colorType}", color = tint, enableInstancing = true, renderQueue = 3018 };
                if (windowMat.HasProperty("_BaseColor")) windowMat.SetColor("_BaseColor", tint);
                s_SharedLidWindowMats[colorType] = windowMat;
            }

            // Diagonal white specular reflection glare & crisp rim trim (shared across all boxes)
            if (s_SharedLidGlareMat == null)
            {
                s_SharedLidGlareMat = new Material(spriteShader)
                {
                    name = "Mat_LidSpecularGlare_Shared",
                    color = new Color(1f, 1f, 1f, 0.68f),
                    enableInstancing = true,
                    renderQueue = 3020
                };
                if (s_SharedLidGlareMat.HasProperty("_BaseColor"))
                    s_SharedLidGlareMat.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.68f));
            }

            for (int i = 0; i < rectCount; i++)
            {
                Vector4 r = (i == 0) ? r0 : r1;
                float lw = Mathf.Max(0.20f, (r.z - CarrierBlockShapeUtility.WallInset * 0.9f) * 2f);
                float lh = Mathf.Max(0.20f, (r.w - CarrierBlockShapeUtility.WallInset * 0.9f) * 2f);
                float localCenterY = r.y - hingeY;

                GameObject pane = new GameObject($"LidPane_{i}");
                pane.transform.SetParent(lidRoot.transform, false);
                pane.transform.localPosition = new Vector3(r.x, localCenterY, 0f);
                pane.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshUtils.CreatePillBadge(lw, lh, 0.03f, Mathf.Min(0.14f, Mathf.Min(lw, lh) * 0.38f));
                var lidMr = pane.AddComponent<MeshRenderer>();
                lidMr.sharedMaterial = windowMat;
                lidMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lidMr.receiveShadows = false;

                // Crisp pearlescent top & bottom lid edge rails so the swinging flap silhouette reads clearly in 3D
                const float railThick = 0.022f;
                float railLen = Mathf.Max(0.14f, lw - 0.06f);
                Mesh hRailMesh = ProceduralMeshUtils.CreatePillBadge(railLen, railThick, 0.01f, railThick * 0.45f);
                for (int edge = 0; edge < 2; edge++)
                {
                    float ey = localCenterY + (edge == 0 ? (lh * 0.5f - railThick * 0.5f) : (-lh * 0.5f + railThick * 0.5f));
                    GameObject rail = new GameObject($"LidRailH_{i}_{edge}");
                    rail.transform.SetParent(lidRoot.transform, false);
                    rail.transform.localPosition = new Vector3(r.x, ey, -0.012f);
                    rail.AddComponent<MeshFilter>().sharedMesh = hRailMesh;
                    var rMr = rail.AddComponent<MeshRenderer>();
                    rMr.sharedMaterial = s_SharedLidGlareMat;
                    rMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    rMr.receiveShadows = false;
                }
            }

            GameObject glare1 = new GameObject("LidGlareMain");
            glare1.transform.SetParent(lidRoot.transform, false);
            glare1.transform.localPosition = new Vector3(r0.x - 0.06f, (r0.y + 0.04f) - hingeY, -0.02f);
            glare1.transform.localRotation = Quaternion.Euler(0f, 0f, -32f);
            glare1.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshUtils.CreatePillBadge(0.46f, 0.068f, 0.02f, 0.032f);
            var g1Mr = glare1.AddComponent<MeshRenderer>();
            g1Mr.sharedMaterial = s_SharedLidGlareMat;
            g1Mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _lidGlareTransform = glare1.transform;
            _lidGlareRestLocalPos = glare1.transform.localPosition;

            GameObject glare2 = new GameObject("LidGlareSub");
            glare2.transform.SetParent(lidRoot.transform, false);
            glare2.transform.localPosition = new Vector3(r0.x + 0.06f, (r0.y - 0.07f) - hingeY, -0.02f);
            glare2.transform.localRotation = Quaternion.Euler(0f, 0f, -32f);
            glare2.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshUtils.CreatePillBadge(0.32f, 0.034f, 0.015f, 0.016f);
            var g2Mr = glare2.AddComponent<MeshRenderer>();
            g2Mr.sharedMaterial = s_SharedLidGlareMat;
            g2Mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _lidGlareSubTransform = glare2.transform;
            _lidGlareSubRestLocalPos = glare2.transform.localPosition;

            // Bottom snap-latch clasp ("TIK!" lock clip opposite top hinge, cached per color)
            if (!s_SharedLidLatchMats.TryGetValue(colorType, out Material latchMat) || latchMat == null)
            {
                Color latchCol = Color.Lerp(GetCarrierFrameColor(colorType), Color.white, 0.22f);
                latchMat = new Material(spriteShader) { name = $"Mat_LidLatch_{colorType}", color = latchCol, enableInstancing = true, renderQueue = 3022 };
                if (latchMat.HasProperty("_BaseColor")) latchMat.SetColor("_BaseColor", latchCol);
                s_SharedLidLatchMats[colorType] = latchMat;
            }

            float bottomRimY = (r0.y - r0.w + CarrierBlockShapeUtility.WallInset * 0.9f + 0.02f) - hingeY;
            GameObject latchObj = new GameObject("LidSnapLatch");
            latchObj.transform.SetParent(lidRoot.transform, false);
            latchObj.transform.localPosition = new Vector3(r0.x, bottomRimY, -0.03f);
            latchObj.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshUtils.CreatePillBadge(0.24f, 0.068f, 0.03f, 0.028f);
            var latchMr = latchObj.AddComponent<MeshRenderer>();
            latchMr.sharedMaterial = latchMat;
            latchMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _lidLatchTransform = latchObj.transform;

            lidRoot.SetActive(false);
        }

        private const float ShadowPadX = 0.56f;
        private const float ShadowPadY = 0.68f;

        private void BuildFakeDropShadow()
        {
            Shader shadowShader = Shader.Find("PuzzleGlass/FakeDropShadow");
            if (shadowShader == null) return;

            CarrierBlockShapeUtility.GetRectParts(blockShape, out _, out _, out _, out float quadW, out float quadH);
            float shadowW = quadW + ShadowPadX;
            float shadowH = quadH + ShadowPadY;
            _mpbShadowLocalSize = new Vector4(shadowW, shadowH, 0f, 0f);

            // Polyomino silhouette-matched 2.5D fake drop shadow rendered behind every box (Transparent-7 = 2993)
            bool inGridBoard = StagingGridBoard.Instance != null && StagingGridBoard.Instance.showTrayVisuals;
            Material shadowMat;
            if (inGridBoard)
            {
                if (s_SharedShadowMatGrid == null)
                {
                    s_SharedShadowMatGrid = new Material(shadowShader)
                    {
                        name = "Mat_MonsterFakeShadow_Grid",
                        enableInstancing = true,
                        renderQueue = 2993
                    };
                    s_SharedShadowMatGrid.SetColor("_ShadowColor", new Color(0.05f, 0.04f, 0.16f, 0.58f));
                    s_SharedShadowMatGrid.SetFloat("_IsRadial", 2.0f);
                }
                shadowMat = s_SharedShadowMatGrid;
            }
            else
            {
                if (s_SharedShadowMatTray == null)
                {
                    s_SharedShadowMatTray = new Material(shadowShader)
                    {
                        name = "Mat_MonsterFakeShadow_Tray",
                        enableInstancing = true,
                        renderQueue = 2993
                    };
                    s_SharedShadowMatTray.SetColor("_ShadowColor", new Color(0.11f, 0.13f, 0.27f, 0.44f));
                    s_SharedShadowMatTray.SetFloat("_IsRadial", 2.0f);
                }
                shadowMat = s_SharedShadowMatTray;
            }

            Transform shadowParent = bodyPivot != null ? bodyPivot : transform;
            GameObject shadowQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            shadowQuad.name = "CarrierBoxFakeShadow";
            shadowQuad.transform.SetParent(shadowParent, false);
            shadowQuad.transform.localPosition = new Vector3(0f, 0f, 0.12f);
            shadowQuad.transform.localScale = new Vector3(shadowW, shadowH, 1.0f);
            DestroyCollider(shadowQuad);

            var mr = shadowQuad.GetComponent<MeshRenderer>();
            mr.sharedMaterial = shadowMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _shadowRenderer = mr;

            ApplyShadowPropertyBlock();
        }

        private void BuildHiddenBadge(Transform parent)
        {
            Vector2 center = GetMechanicAnchorLocal();
            float yOffset = (visualStyle == BlockVisualStyle.PuzzleWithNumber) ? -0.032f : 0.01f;
            GameObject badge = new GameObject("BellyBadge");
            badge.transform.SetParent(parent, false);
            badge.transform.localPosition = new Vector3(center.x, center.y + yOffset, -0.26f);
            badgeTransform = badge.transform;

            GameObject textObj = new GameObject("CountText");
            textObj.transform.SetParent(badge.transform, false);
            textObj.transform.localPosition = new Vector3(0, 0, -0.02f);

            counterText = textObj.AddComponent<TextMeshPro>();
            counterText.alignment = TextAlignmentOptions.Center;
            counterText.fontSize = (visualStyle == BlockVisualStyle.PuzzleWithNumber) ? 3.8f : 4.6f;
            counterText.fontStyle = FontStyles.Bold;
            counterText.color = Color.white;
            counterText.text = remainingCount.ToString();
            counterText.rectTransform.sizeDelta = new Vector2(1.1f, 0.7f);
            counterText.sortingOrder = 10;
            // Tok beyaz sayı + ince koyu kontur + aşağı düşen koyu gölge (referans rozet stili)
            counterText.color = Color.white;
            counterText.extraPadding = true;
            counterText.characterSpacing = -4f;
            ApplyBoxNumberStyle(counterText, new Color(0.04f, 0.05f, 0.10f, 1f));

            badge.SetActive(useCrateVisuals && !IsLockedByKey && !IsFrozenByIce && remainingCount > 0);
        }

        public void UpdateBadgeText()
        {
            if (counterText != null)
            {
                counterText.text = Mathf.Max(0, remainingCount).ToString();
                UpdateCounterTextPlayableTint(isExitClearInGrid || assignedSlot != null || _isMovingToSlot);
                if (visualStyle == BlockVisualStyle.PuzzleWithNumber)
                {
                    counterText.fontSize = (remainingCount >= 100) ? 3.1f : 3.8f;
                }
                else if (useCrateVisuals)
                {
                    counterText.fontSize = (remainingCount >= 100) ? 3.8f : 4.6f;
                }
            }
            if (badgeTransform != null)
            {
                Vector2 center = GetMechanicAnchorLocal();
                float yOffset = (visualStyle == BlockVisualStyle.PuzzleWithNumber) ? -0.032f : 0.01f;
                badgeTransform.localPosition = new Vector3(center.x, center.y + yOffset, -0.26f);

                bool showBadge = useCrateVisuals
                    ? (!IsFrozenByIce && !IsLockedByKey && remainingCount > 0)
                    : (!IsFrozenByIce && (initialCount > MaxVisualSockets) && (remainingCount > 0));
                badgeTransform.gameObject.SetActive(showBadge);
            }

            if (!Application.isPlaying)
            {
                SyncFillProgressImmediate();
            }
        }

        private void DestroyCollider(GameObject obj)
        {
            Collider col = obj.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }
        }

        // ==================== SOCKET TARGETING & SETTLING ====================

        /// <summary>
        /// Claims the next gray socket index (0 = Bottom-Left, 1 = Bottom-Right, 2 = Top-Center)
        /// for a ball launching toward this carrier box.
        /// </summary>
        private void ApplyShapeMaterialParams(Vector4 rect0, Vector4 rect1, int rectCount, float quadW, float quadH)
        {
            _mpbLocalSize = new Vector4(quadW, quadH, 0f, 0f);
            _mpbShadowLocalSize = new Vector4(quadW + ShadowPadX, quadH + ShadowPadY, 0f, 0f);
            _mpbRectCount = rectCount;
            _mpbRect0 = rect0;
            _mpbRect1 = rect1;
            _mpbExitHighlight = ((isExitClearInGrid || isCollectingInGrid) && !IsMovementOrCollectionBlocked) ? 1f : 0f;
            _mpbDragHighlight = 0f;
            _mpbIceAmount = IsFrozenByIce ? 1f : 0f;
        }

        private void ApplySocketLayoutToMaterial()
        {
            int count = GetVisibleSocketCount();
            CarrierBlockShapeUtility.GetRectParts(blockShape, out Vector4 r0, out Vector4 r1, out int rc, out float qw, out float qh);
            ApplyShapeMaterialParams(r0, r1, rc, qw, qh);
            ComputeActiveBallLayout(count, out Vector2[] positions, out float radius, out _socketBallLocalDiameter);
            ApplySocketLayoutToMaterial(count, positions, radius);
        }

        private void ApplySocketLayoutToMaterial(int count, Vector2[] positions, float radius)
        {
            // Sayılı puzzle görünümünde plaka deliksiz çizilir
            _mpbSocketCount = visualStyle == BlockVisualStyle.PuzzleWithNumber ? 0 : count;
            _mpbSocketRadius = radius;
            for (int p = 0; p < SockPairPropIds.Length; p++)
            {
                int pIdx = p * 2;
                Vector2 a = (positions != null && pIdx < positions.Length) ? positions[pIdx] : Vector2.zero;
                Vector2 b = (positions != null && pIdx + 1 < positions.Length) ? positions[pIdx + 1] : Vector2.zero;
                _mpbSockPairs[p] = new Vector4(a.x, a.y, b.x, b.y);
            }
            ApplyBoxPropertyBlock();
        }

        public int ClaimNextSocketIndex() => ClaimNextSocketIndex(out _);

        public int ClaimNextSocketIndex(out bool isFinalBall)
        {
            int targetTotal = Mathf.Max(1, initialCount);
            isFinalBall = (_nextIncomingSocketCounter + 1 >= targetTotal) || (remainingCount <= 1);
            int modCount = Mathf.Max(1, GetSocketModCount());
            int idx;
            if (IsHollowPitStyle && initialCount > modCount && initialCount > 0)
            {
                idx = Mathf.Clamp(Mathf.FloorToInt((_nextIncomingSocketCounter / (float)initialCount) * modCount), 0, modCount - 1);
            }
            else
            {
                idx = _nextIncomingSocketCounter % modCount;
            }
            _nextIncomingSocketCounter++;
            return idx;
        }

        /// <summary>
        /// Returns the world position of the specified socket or pit entry point.
        /// </summary>
        public Vector3 GetSocketWorldPosition(int socketIndex)
        {
            if (IsHollowPitStyle)
            {
                Transform t = bodyPivot != null ? bodyPivot : transform;
                ComputeActiveBallLayout(GetVisibleSocketCount(), out _, out _, out _socketBallLocalDiameter);
                float r = Mathf.Max(0.025f, _socketBallLocalDiameter * 0.5f);
                Vector2 topEntry = GetPitDropEntryLocal(socketIndex, GetPitTargetLocal(socketIndex), r);
                return t.TransformPoint(new Vector3(topEntry.x, topEntry.y, -0.085f));
            }
            if (ShowsCavityBalls && socketTransforms != null && socketTransforms.Length > 0)
            {
                int clamped = Mathf.Clamp(socketIndex, 0, socketTransforms.Length - 1);
                if (socketTransforms[clamped] != null)
                {
                    return socketTransforms[clamped].position;
                }
            }
            if (useCrateVisuals)
            {
                Vector2 c = GetMechanicAnchorLocal();
                float yOffset = (visualStyle == BlockVisualStyle.PuzzleWithNumber) ? -0.032f : 0.01f;
                Transform t = bodyPivot != null ? bodyPivot : transform;
                return t.TransformPoint(new Vector3(c.x, c.y + yOffset, -0.18f));
            }
            if (mouthTargetPoint != null) return mouthTargetPoint.position;
            return transform.position + new Vector3(0f, 0f, -0.10f);
        }

        /// <summary>
        /// Returns the exact world scale for a ball settled inside one of the gray sockets or hollow pit.
        /// </summary>
        public Vector3 GetSocketBallWorldScale()
        {
            ComputeActiveBallLayout(GetVisibleSocketCount(), out _, out _, out _socketBallLocalDiameter);
            Vector3 lossy = (bodyPivot != null) ? bodyPivot.lossyScale : transform.lossyScale;
            return lossy * _socketBallLocalDiameter;
        }

        private void ActivateAndPulseSocketBall(int socketIndex, bool playSeatEffect = true)
        {
            if (useCrateVisuals)
            {
                if (counterText != null && Application.isPlaying)
                {
                    counterText.transform.DOKill(true);
                    counterText.transform.DOPunchScale(Vector3.one * 0.24f, 0.14f, 5, 0.5f);
                }
                if (!ShowsCavityBalls)
                {
                    if (playSeatEffect && Application.isPlaying)
                    {
                        float scaleFactor = (bodyPivot != null) ? bodyPivot.lossyScale.x : transform.lossyScale.x;
                        BlockClearCelebration.PlaySocketSeatRing(GetSocketWorldPosition(socketIndex), PuzzleColorPalette.GetPrimaryColor(colorType), Mathf.Max(0.10f, 0.26f * scaleFactor), 0.72f);
                    }
                    return;
                }
            }
            if (socketBalls == null || socketBalls.Length == 0) return;
            int idx = Mathf.Clamp(socketIndex, 0, socketBalls.Length - 1);
            GameObject sBall = socketBalls[idx];
            if (sBall == null) return;

            ComputeActiveBallLayout(GetVisibleSocketCount(), out _, out _, out _socketBallLocalDiameter);
            sBall.SetActive(true);
            Transform tr = sBall.transform;
            tr.DOKill();
            Vector3 targetLocalScale = Vector3.one * _socketBallLocalDiameter;

            if (IsHollowPitStyle)
            {
                EnsurePitLayoutCached();
                float r = Mathf.Max(0.025f, _socketBallLocalDiameter * 0.5f);
                Vector2 target = GetPitTargetLocal(idx);
                Vector2 entry = GetPitDropEntryLocal(idx, target, r);
                if (_hasPitSpawnHint)
                {
                    entry.x = ClampXInsidePitTopRect(entry.y, _pitNextSpawnHintX, r);
                    _hasPitSpawnHint = false;
                }

                float frac = (((idx + _settledSocketCount) * 0.618034f) % 1f) - 0.5f;
                float vx = Application.isPlaying ? (frac * 0.85f + (Mathf.Abs(frac) < 0.08f ? 0.18f : 0f)) : 0f;
                float vy = Application.isPlaying ? -3.15f : 0f;

                _pitSim[idx] = new PitBallSim
                {
                    pos = Application.isPlaying ? entry : target,
                    vel = new Vector2(vx, vy),
                    target = target,
                    angle = (idx * 0.75f),
                    angularVel = Application.isPlaying ? (-vx / r) : 0f,
                    impactSquash = Application.isPlaying ? 0.08f : 0f,
                    sleepTimer = 0f,
                    settled = !Application.isPlaying,
                    active = true
                };
                _pitAnyActive = Application.isPlaying;

                float depth01 = (_pitSim.Length > 1) ? (idx / (float)(_pitSim.Length - 1)) : 1f;
                float depthZ = Mathf.Lerp(-0.032f, -0.078f, depth01);
                float depthScale = Mathf.Lerp(0.88f, 1.00f, depth01);

                if (socketTransforms != null && idx < socketTransforms.Length && socketTransforms[idx] != null)
                {
                    Vector2 initialPos = Application.isPlaying ? entry : target;
                    socketTransforms[idx].localPosition = new Vector3(initialPos.x, initialPos.y, depthZ);
                }

                tr.localPosition = Vector3.zero;
                tr.localRotation = Quaternion.Euler(18f, -24f, _pitSim[idx].angle * Mathf.Rad2Deg);
                tr.localScale = targetLocalScale * depthScale;
            }
            else
            {
                tr.localPosition = new Vector3(0f, 0f, -0.024f);
                tr.localScale = new Vector3(targetLocalScale.x * 1.22f, targetLocalScale.y * 0.78f, targetLocalScale.z * 1.14f);

                Sequence sockSeq = DOTween.Sequence();
                sockSeq.Append(tr.DOScale(
                    new Vector3(targetLocalScale.x * 0.93f, targetLocalScale.y * 1.08f, targetLocalScale.z * 0.96f),
                    0.055f).SetEase(Ease.OutQuad));
                sockSeq.Append(tr.DOScale(targetLocalScale, 0.145f).SetEase(Ease.OutBounce));
                sockSeq.Join(tr.DOLocalMoveZ(0f, 0.14f).SetEase(Ease.OutBounce));
                sockSeq.SetLink(sBall);
            }

            if (playSeatEffect && Application.isPlaying)
            {
                float scaleFactor = (bodyPivot != null) ? bodyPivot.lossyScale.x : transform.lossyScale.x;
                float worldRadius = Mathf.Max(0.08f, _socketBallLocalDiameter * 0.65f * scaleFactor);
                Vector3 ringPos = IsHollowPitStyle && socketTransforms != null && idx < socketTransforms.Length && socketTransforms[idx] != null
                    ? socketTransforms[idx].position
                    : tr.position;
                BlockClearCelebration.PlaySocketSeatRing(ringPos, PuzzleColorPalette.GetPrimaryColor(colorType), worldRadius);
            }
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying || !IsHollowPitStyle || _pitSim == null) return;

            float dt = Mathf.Min(Time.deltaTime, 0.035f);
            if (dt <= 0.0001f) return;

            Transform motionT = bodyPivot != null ? bodyPivot : transform;
            Vector2 worldPos = motionT.position;
            if (_hasPitMotionHistory)
            {
                Vector2 disp = worldPos - _lastPitWorldPos;
                if (disp.sqrMagnitude > 1.2f)
                {
                    _lastPitWorldVel = Vector2.zero;
                }
                else
                {
                    Vector2 curVel = disp / dt;
                    Vector2 accel = (curVel - _lastPitWorldVel) / dt;
                    _lastPitWorldVel = curVel;

                    float invScale = 1f / Mathf.Max(0.15f, motionT.lossyScale.x);
                    Vector2 localAccel = accel * invScale;
                    if (!_isCompleted && localAccel.sqrMagnitude > 6.5f)
                    {
                        Vector2 inertiaKick = Vector2.ClampMagnitude(-localAccel * (dt * 0.24f), 1.45f);
                        JostlePitBalls(inertiaKick, wakeAll: true);
                    }
                }
            }
            else
            {
                _lastPitWorldVel = Vector2.zero;
                _hasPitMotionHistory = true;
            }
            _lastPitWorldPos = worldPos;

            if (_pitAnyActive)
            {
                // Kare süresi kırpılırsa (oyun sonu 2x hızlanma ya da düşük FPS) kutudaki toplar oyunun gerisinde
                // kalıp "inmiyor" gibi görünüyordu. Süreyi kırpmak yerine kararlı (≤0.035 sn) alt adımlara böl.
                float total = Mathf.Min(Time.deltaTime, 0.105f);
                int steps = Mathf.Clamp(Mathf.CeilToInt(total / 0.035f), 1, 3);
                float stepDt = total / steps;
                for (int s = 0; s < steps && _pitAnyActive; s++)
                {
                    StepHollowPitPhysics(stepDt);
                }
            }
        }

        private void EnsurePitLayoutCached()
        {
            int count = (socketBalls != null && socketBalls.Length > 0) ? socketBalls.Length : GetVisibleSocketCount();
            if (_pitSim == null || _pitSim.Length != count)
            {
                _pitSim = new PitBallSim[count];
            }
            if (_pitTargets == null || _pitTargets.Length != count)
            {
                CarrierBlockShapeUtility.GetRectParts(blockShape, out _pitRect0, out _pitRect1, out _pitRectCount, out _, out _);
                ComputeActiveBallLayout(count, out _pitTargets, out _, out _socketBallLocalDiameter);
            }
            else if (_pitRect0 == Vector4.zero)
            {
                CarrierBlockShapeUtility.GetRectParts(blockShape, out _pitRect0, out _pitRect1, out _pitRectCount, out _, out _);
            }
        }

        private Vector2 GetPitTargetLocal(int ballIdx)
        {
            EnsurePitLayoutCached();
            if (_pitTargets != null && _pitTargets.Length > 0)
            {
                int idx = Mathf.Clamp(ballIdx, 0, _pitTargets.Length - 1);
                return _pitTargets[idx];
            }
            return new Vector2(_pitRect0.x, _pitRect0.y - 0.03f);
        }

        private Vector4 SelectPitRectForPoint(Vector2 pt)
        {
            if (_pitRect0 == Vector4.zero)
            {
                CarrierBlockShapeUtility.GetRectParts(blockShape, out _pitRect0, out _pitRect1, out _pitRectCount, out _, out _);
            }
            if (_pitRectCount > 1 && Mathf.Abs(pt.x - _pitRect1.x) <= _pitRect1.z && Mathf.Abs(pt.y - _pitRect1.y) <= _pitRect1.w)
            {
                return _pitRect1;
            }
            return _pitRect0;
        }

        private float ClampXInsidePitTopRect(float entryY, float desiredX, float radius)
        {
            Vector4 rect = SelectPitRectForPoint(new Vector2(desiredX, entryY));
            const float pitInset = 0.118f;
            float usableHalfW = Mathf.Max(0.02f, rect.z - pitInset - radius - 0.012f);
            return Mathf.Clamp(desiredX, rect.x - usableHalfW, rect.x + usableHalfW);
        }

        private Vector2 GetPitDropEntryLocal(int ballIdx, Vector2 target, float radius)
        {
            Vector4 rect = SelectPitRectForPoint(target);
            const float pitInset = 0.118f;
            const float topOverhang = 0.068f;
            const float wallPad = 0.002f;

            float pitTopY = rect.y + rect.w - (pitInset + topOverhang + radius + wallPad + 0.006f);
            float usableHalfW = Mathf.Max(0.02f, (rect.z - pitInset - radius - wallPad) * 0.62f);
            float frac = (((ballIdx + _settledSocketCount) * 0.618034f) % 1f) - 0.5f;
            float entryX = rect.x + frac * 2f * usableHalfW;
            Vector2 candidate = new Vector2(entryX, pitTopY);

            float d = CarrierBlockShapeUtility.SampleHollowPitSdfAndNormal(candidate, _pitRect0, _pitRect1, _pitRectCount, radius, out Vector2 n);
            if (d > -0.002f)
            {
                candidate -= n * (d + 0.003f);
            }
            return candidate;
        }

        public void JostlePitBalls(Vector2 impulse, bool wakeAll)
        {
            if (_pitSim == null || !IsHollowPitStyle) return;
            for (int i = 0; i < _pitSim.Length; i++)
            {
                if (!_pitSim[i].active) continue;
                if (!wakeAll && _pitSim[i].settled) continue;

                ref PitBallSim sim = ref _pitSim[i];
                float jitterX = ((i % 2 == 0) ? 1f : -1f) * 0.08f;
                sim.vel += new Vector2(impulse.x + jitterX, impulse.y);
                sim.settled = false;
                sim.sleepTimer = 0f;
                _pitAnyActive = true;
            }
        }

        private void ApplyPitImpactAtLocal(Vector2 localPos, float radius, float downwardImpulse)
        {
            if (_pitSim == null || !IsHollowPitStyle) return;
            float affectRadius = Mathf.Max(0.18f, radius * 2.4f);
            float affectRadiusSq = affectRadius * affectRadius;
            for (int i = 0; i < _pitSim.Length; i++)
            {
                if (!_pitSim[i].active) continue;
                ref PitBallSim sim = ref _pitSim[i];
                Vector2 diff = sim.pos - localPos;
                float distSq = diff.sqrMagnitude;
                if (distSq <= affectRadiusSq || sim.pos.y >= _pitRect0.y)
                {
                    float pushX = (Mathf.Abs(diff.x) < 0.01f) ? ((i % 2 == 0) ? 0.25f : -0.25f) : Mathf.Sign(diff.x) * 0.35f;
                    sim.vel += new Vector2(pushX, downwardImpulse * 0.55f);
                    sim.impactSquash = Mathf.Max(sim.impactSquash, 0.14f);
                    sim.settled = false;
                    sim.sleepTimer = 0f;
                    _pitAnyActive = true;
                }
            }
        }

        private void StepHollowPitPhysics(float dt)
        {
            if (dt <= 0.0001f || _pitSim == null) return;

            const int subSteps = 4;
            const float gravity = 19.0f;
            const float wallRestitution = 0.34f;
            const float ballRestitution = 0.30f;

            float subDt = dt / subSteps;
            float ballRadius = Mathf.Max(0.025f, _socketBallLocalDiameter * 0.5f);
            float minDist = ballRadius * 2.0f;
            float minDistSq = minDist * minDist;
            Vector3 baseBallScale = Vector3.one * _socketBallLocalDiameter;

            for (int step = 0; step < subSteps; step++)
            {
                for (int i = 0; i < _pitSim.Length; i++)
                {
                    if (!_pitSim[i].active || _pitSim[i].settled) continue;
                    ref PitBallSim sim = ref _pitSim[i];
                    sim.vel.y -= gravity * subDt;
                    sim.vel *= 0.996f;
                    sim.angularVel *= 0.985f;
                    sim.pos += sim.vel * subDt;
                    sim.angle += sim.angularVel * subDt;
                }

                // Ball-to-ball elastic & rolling collisions
                for (int i = 0; i < _pitSim.Length; i++)
                {
                    if (!_pitSim[i].active) continue;
                    for (int j = i + 1; j < _pitSim.Length; j++)
                    {
                        if (!_pitSim[j].active) continue;
                        if (_pitSim[i].settled && _pitSim[j].settled) continue;

                        ref PitBallSim a = ref _pitSim[i];
                        ref PitBallSim b = ref _pitSim[j];

                        Vector2 delta = b.pos - a.pos;
                        float distSq = delta.sqrMagnitude;
                        if (distSq >= minDistSq) continue;

                        Vector2 normal;
                        float dist;
                        if (distSq < 1e-8f)
                        {
                            float dir = ((i + j) % 2 == 0) ? 0.12f : -0.12f;
                            normal = new Vector2(dir, 1f).normalized;
                            dist = 0.001f;
                        }
                        else
                        {
                            dist = Mathf.Sqrt(distSq);
                            normal = delta / dist;
                            // Break pure vertical stacking symmetry so balls roll naturally into side crevices
                            if (Mathf.Abs(normal.x) < 0.015f && (a.vel.sqrMagnitude > 0.04f || b.vel.sqrMagnitude > 0.04f))
                            {
                                float side = ((i + j) % 2 == 0) ? 0.03f : -0.03f;
                                normal = new Vector2(side, normal.y).normalized;
                            }
                        }

                        float overlap = minDist - dist;
                        Vector2 relVel = b.vel - a.vel;
                        float vn = Vector2.Dot(relVel, normal);

                        // Wake resting balls when struck briskly or pushed firmly
                        if (a.settled && (-vn > 0.22f || overlap > 0.006f))
                        {
                            a.settled = false;
                            a.sleepTimer = 0f;
                        }
                        if (b.settled && (-vn > 0.22f || overlap > 0.006f))
                        {
                            b.settled = false;
                            b.sleepTimer = 0f;
                        }

                        if (!a.settled && !b.settled)
                        {
                            Vector2 corr = normal * (overlap * 0.5f);
                            a.pos -= corr;
                            b.pos += corr;
                        }
                        else if (!a.settled)
                        {
                            a.pos -= normal * (overlap * 0.92f);
                        }
                        else if (!b.settled)
                        {
                            b.pos += normal * (overlap * 0.92f);
                        }

                        if (vn < 0f)
                        {
                            float e = (-vn > 0.50f) ? ballRestitution : 0f;
                            float jNormal = -(1f + e) * vn * 0.5f;
                            Vector2 impulse = normal * jNormal;

                            Vector2 tangent = new Vector2(-normal.y, normal.x);
                            float vt = Vector2.Dot(relVel, tangent);
                            Vector2 tImpulse = tangent * (-vt * 0.14f);

                            if (!a.settled)
                            {
                                a.vel -= impulse + tImpulse;
                                a.angularVel += vt / (ballRadius * 2.5f);
                                if (-vn > 1.35f) a.impactSquash = Mathf.Max(a.impactSquash, Mathf.Min(0.18f, -vn * 0.055f));
                            }
                            if (!b.settled)
                            {
                                b.vel += impulse + tImpulse;
                                b.angularVel -= vt / (ballRadius * 2.5f);
                                if (-vn > 1.35f) b.impactSquash = Mathf.Max(b.impactSquash, Mathf.Min(0.18f, -vn * 0.055f));
                            }
                            if (-vn > 1.30f && Time.unscaledTime - _lastPitImpactVfxTime >= 0.055f && bodyPivot != null)
                            {
                                _lastPitImpactVfxTime = Time.unscaledTime;
                                Vector2 hitLocal = 0.5f * (a.pos + b.pos);
                                Vector3 hitWorld = bodyPivot.TransformPoint(new Vector3(hitLocal.x, hitLocal.y, -0.08f));
                                float worldDiam = _socketBallLocalDiameter * bodyPivot.lossyScale.x;
                                BlockClearCelebration.PlayCavityTapEffect(hitWorld, PuzzleColorPalette.GetPrimaryColor(colorType), worldDiam);
                            }
                        }
                    }
                }

                // Pit floor, walls, ceiling & curved corner SDF collision
                for (int i = 0; i < _pitSim.Length; i++)
                {
                    if (!_pitSim[i].active || _pitSim[i].settled) continue;
                    ref PitBallSim sim = ref _pitSim[i];

                    float d = CarrierBlockShapeUtility.SampleHollowPitSdfAndNormal(
                        sim.pos, _pitRect0, _pitRect1, _pitRectCount, ballRadius, out Vector2 wallOutNormal);

                    if (d > 0f)
                    {
                        sim.pos -= wallOutNormal * d;
                        float vn = Vector2.Dot(sim.vel, wallOutNormal);
                        if (vn > 0f)
                        {
                            float e = (vn > 0.48f) ? wallRestitution : 0f;
                            sim.vel -= wallOutNormal * ((1f + e) * vn);

                            Vector2 tangent = new Vector2(-wallOutNormal.y, wallOutNormal.x);
                            float vt = Vector2.Dot(sim.vel, tangent);
                            sim.angularVel = -vt / ballRadius;
                            sim.vel -= tangent * (vt * 0.045f);

                            if (vn > 1.25f)
                            {
                                sim.impactSquash = Mathf.Max(sim.impactSquash, Mathf.Min(0.22f, vn * 0.065f));
                                if (Time.unscaledTime - _lastPitImpactVfxTime >= 0.055f && bodyPivot != null)
                                {
                                    _lastPitImpactVfxTime = Time.unscaledTime;
                                    Vector2 hitLocal = sim.pos + wallOutNormal * (ballRadius * 0.8f);
                                    Vector3 hitWorld = bodyPivot.TransformPoint(new Vector3(hitLocal.x, hitLocal.y, -0.08f));
                                    float worldDiam = _socketBallLocalDiameter * bodyPivot.lossyScale.x;
                                    BlockClearCelebration.PlayCavityTapEffect(hitWorld, PuzzleColorPalette.GetPrimaryColor(colorType), worldDiam);
                                }
                            }
                        }
                    }
                }
            }

            bool anyMoving = false;
            Vector3 rollSpinAxis = new Vector3(0.22f, 0.35f, 0.91f).normalized;
            Quaternion baseTilt = Quaternion.Euler(18f, -24f, 0f);
            for (int i = 0; i < _pitSim.Length; i++)
            {
                if (!_pitSim[i].active) continue;
                ref PitBallSim sim = ref _pitSim[i];

                if (sim.impactSquash > 0.001f)
                {
                    sim.impactSquash = Mathf.MoveTowards(sim.impactSquash, 0f, dt * 1.45f);
                    anyMoving = true;
                }

                if (!sim.settled)
                {
                    if (sim.vel.sqrMagnitude < 0.014f && Mathf.Abs(sim.angularVel) < 0.95f)
                    {
                        sim.sleepTimer += dt;
                        if (sim.sleepTimer >= 0.20f)
                        {
                            sim.vel = Vector2.zero;
                            sim.angularVel = 0f;
                            sim.settled = true;
                        }
                        else
                        {
                            anyMoving = true;
                        }
                    }
                    else
                    {
                        sim.sleepTimer = 0f;
                        anyMoving = true;
                    }
                }

                float depth01 = (_pitSim.Length > 1) ? (i / (float)(_pitSim.Length - 1)) : 1f;
                float depthZ = Mathf.Lerp(-0.032f, -0.078f, depth01);
                float depthScale = Mathf.Lerp(0.88f, 1.00f, depth01);

                if (socketTransforms != null && i < socketTransforms.Length && socketTransforms[i] != null)
                {
                    socketTransforms[i].localPosition = new Vector3(sim.pos.x, sim.pos.y, depthZ);
                }
                if (socketBalls != null && i < socketBalls.Length && socketBalls[i] != null)
                {
                    Transform bTr = socketBalls[i].transform;
                    bTr.localRotation = baseTilt * Quaternion.AngleAxis(sim.angle * Mathf.Rad2Deg, rollSpinAxis);
                    float sq = sim.impactSquash;
                    float fallStretch = sim.settled ? 0f : Mathf.Clamp((sim.vel.magnitude - 0.95f) * 0.042f, 0f, 0.13f);
                    bTr.localScale = new Vector3(
                        baseBallScale.x * depthScale * (1f + sq - fallStretch * 0.55f),
                        baseBallScale.y * depthScale * (1f - sq * 0.68f + fallStretch),
                        baseBallScale.z * depthScale * (1f + sq * 0.35f - fallStretch * 0.25f));
                }
            }

            _pitAnyActive = anyMoving;
        }

        // ==================== ANIMATIONS ====================

        public void StartIdleAndBlink()
        {
            StopIdleAndBlink();

            if (bodyPivot != null)
            {
                bodyPivot.localScale = _originalBodyScale;
            }
            _mpbWobbleAmount = 0.0f;
            ApplyBoxPropertyBlock();
        }

        public void StopIdleAndBlink()
        {
            if (_idleTween != null && _idleTween.IsActive()) _idleTween.Kill();
            if (_blinkSeq != null && _blinkSeq.IsActive()) _blinkSeq.Kill();
            _mpbWobbleAmount = 0.0f;
            ApplyBoxPropertyBlock();
            if (bodyPivot != null)
            {
                bodyPivot.localScale = _originalBodyScale;
                bodyPivot.localPosition = Vector3.zero;
            }
        }

        /// <summary>
        /// Kutunun dolmasına kalan tanecik sayısı.
        /// </summary>
        public int GetGrainsNeeded(int grainsPerCount)
        {
            return Mathf.Max(0, remainingCount * grainsPerCount - _subParticleCounter);
        }

        public void FeedParticle(int particlesPerCount)
        {
            if (_isCompleted) return;

            int ratio = Mathf.Max(1, particlesPerCount);
            _subParticleCounter++;

            if (Time.time - _lastGulpTime >= 0.14f)
            {
                _lastGulpTime = Time.time;
                PlaySmoothGulpWave();
            }

            if (_subParticleCounter >= ratio)
            {
                _subParticleCounter = 0;
                int sockIdx = _settledSocketCount % Mathf.Max(1, GetSocketModCount());
                _settledSocketCount++;
                ActivateAndPulseSocketBall(sockIdx);
                DecrementCounterAndCheckComplete();
            }
        }

        public void FeedCube()
        {
            if (_isCompleted) return;
            _subParticleCounter = 0;
            int sockIdx = _settledSocketCount % Mathf.Max(1, GetSocketModCount());
            _settledSocketCount++;
            ActivateAndPulseSocketBall(sockIdx);
            PlaySmoothGulpWave();
            DecrementCounterAndCheckComplete();
        }

        public void OnBallIncoming(BallPhysicsController ball)
        {
            if (_isCompleted) return;
        }

        public void FeedBall(BallPhysicsController ball)
        {
            int sockIdx = _settledSocketCount % Mathf.Max(1, GetSocketModCount());
            FeedBallIntoSocket(ball, sockIdx);
        }

        public void FeedBallIntoSocket(BallPhysicsController ball, int socketIndex)
        {
            if (_isCompleted) return;

            _settledSocketCount++;
            if (IsHollowPitStyle && socketBalls != null && socketBalls.Length > 0)
            {
                _hasPitSpawnHint = false;
                Vector2 incomingLocal = Vector2.zero;
                if (ball != null && bodyPivot != null)
                {
                    Vector3 loc = bodyPivot.InverseTransformPoint(ball.transform.position);
                    incomingLocal = new Vector2(loc.x, loc.y);
                    _pitNextSpawnHintX = loc.x;
                    _hasPitSpawnHint = true;
                }

                int total = Mathf.Max(1, initialCount);
                int targetActive = Mathf.Clamp(
                    Mathf.RoundToInt((_settledSocketCount / (float)total) * socketBalls.Length),
                    1,
                    socketBalls.Length);
                int lastActivated = targetActive - 1;
                bool activatedAny = false;
                for (int i = 0; i < targetActive; i++)
                {
                    if (socketBalls[i] != null && !socketBalls[i].activeSelf)
                    {
                        ActivateAndPulseSocketBall(i, playSeatEffect: (i == lastActivated));
                        activatedAny = true;
                    }
                }
                if (!activatedAny)
                {
                    if (_hasPitSpawnHint)
                    {
                        ApplyPitImpactAtLocal(incomingLocal, Mathf.Max(0.05f, _socketBallLocalDiameter), -1.05f);
                        _hasPitSpawnHint = false;
                    }
                    if (counterText != null && Application.isPlaying)
                    {
                        counterText.transform.DOKill(true);
                        counterText.transform.DOPunchScale(Vector3.one * 0.24f, 0.14f, 5, 0.5f);
                    }
                }
                _hasPitSpawnHint = false;
            }
            else
            {
                ActivateAndPulseSocketBall(socketIndex);
                if (counterText != null && Application.isPlaying && !useCrateVisuals)
                {
                    counterText.transform.DOKill(true);
                    counterText.transform.localScale = Vector3.one;
                    counterText.transform.DOPunchScale(Vector3.one * 0.24f, 0.14f, 5, 0.5f);
                }
            }

            bool isFinalBall = !isFilledBox && remainingCount <= 1;

            if (Application.isPlaying && (isFinalBall || Time.unscaledTime - _lastLandingPopVfxTime >= 0.045f))
            {
                _lastLandingPopVfxTime = Time.unscaledTime;
                Vector3 popPos = (ball != null)
                    ? ball.transform.position
                    : GetSocketWorldPosition(socketIndex);
                float boxScale = (bodyPivot != null) ? bodyPivot.lossyScale.x : transform.lossyScale.x;
                Color boxCol = PuzzleColorPalette.GetPrimaryColor(colorType);
                BlockClearCelebration.PlayBoxBallLandingPop(popPos, boxCol, isFinalBall ? boxScale * 1.35f : boxScale);
                if (isFinalBall)
                {
                    BlockClearCelebration.PlaySocketSeatRing(popPos, Color.Lerp(boxCol, Color.white, 0.6f), Mathf.Max(0.14f, 0.28f * boxScale), 0.90f);
                }
            }

            if (isFinalBall && IsHollowPitStyle && ShowsCavityBalls && Application.isPlaying)
            {
                TriggerPitCompletionRipple();
            }

            PlaySmoothGulpWave();
            DecrementCounterAndCheckComplete();
        }

        private void TriggerPitCompletionRipple()
        {
            if (_pitSim == null) return;
            JostlePitBalls(new Vector2(0f, 1.35f), wakeAll: true);
            for (int i = 0; i < _pitSim.Length; i++)
            {
                if (_pitSim[i].active)
                {
                    _pitSim[i].impactSquash = Mathf.Max(_pitSim[i].impactSquash, 0.20f);
                }
            }
        }

        private void PlaySmoothGulpWave()
        {
            if (bodyPivot != null)
            {
                bodyPivot.DOKill(false);
                bodyPivot.localPosition = Vector3.zero;
                bodyPivot.localRotation = Quaternion.identity;
                bodyPivot.localScale = _originalBodyScale;

                Sequence gulpSeq = DOTween.Sequence();
                gulpSeq.Append(bodyPivot.DOScale(
                    new Vector3(_originalBodyScale.x * 1.07f, _originalBodyScale.y * 0.92f, _originalBodyScale.z),
                    0.05f).SetEase(Ease.OutSine));
                gulpSeq.Join(bodyPivot.DOLocalMoveY(-0.020f, 0.05f).SetEase(Ease.OutSine));
                gulpSeq.Append(bodyPivot.DOScale(
                    new Vector3(_originalBodyScale.x * 0.98f, _originalBodyScale.y * 1.03f, _originalBodyScale.z),
                    0.06f).SetEase(Ease.OutSine));
                gulpSeq.Join(bodyPivot.DOLocalMoveY(0.008f, 0.06f).SetEase(Ease.OutSine));
                gulpSeq.Append(bodyPivot.DOScale(_originalBodyScale, 0.08f).SetEase(Ease.OutCubic));
                gulpSeq.Join(bodyPivot.DOLocalMoveY(0f, 0.08f).SetEase(Ease.OutCubic));
                gulpSeq.SetLink(gameObject, LinkBehaviour.KillOnDisable);
            }

            if (Application.isPlaying && remainingCount > 1)
            {
                if (_gulpGlowTween != null && _gulpGlowTween.IsActive()) _gulpGlowTween.Kill();
                _mpbDragHighlight = Mathf.Max(_mpbDragHighlight, 0.48f);
                ApplyBoxPropertyBlock();
                _gulpGlowTween = DOTween.To(
                    () => _mpbDragHighlight,
                    v => { _mpbDragHighlight = v; ApplyBoxPropertyBlock(); },
                    0f,
                    0.16f).SetEase(Ease.OutQuad).SetTarget(this).SetLink(gameObject, LinkBehaviour.KillOnDisable);
            }
        }

        private void RefreshSocketBallColors()
        {
            if (socketBalls == null) return;
            PuzzleColor ballCol = isFilledBox ? filledBallColor : colorType;
            for (int i = 0; i < socketBalls.Length; i++)
            {
                if (socketBalls[i] == null) continue;
                var mr = socketBalls[i].GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    BallObjectPool.ApplyBallPropertyBlock(mr, ballCol, variantIndex: 1, wobblePhase: i * 0.65f);
                }
            }
        }

        private float GetCurrentFillProgress()
        {
            if (initialCount <= 0) return 0f;
            if (isFilledBox) return Mathf.Clamp01(Mathf.Max(0, remainingCount) / (float)initialCount);
            return Mathf.Clamp01((initialCount - Mathf.Max(0, remainingCount)) / (float)initialCount);
        }

        private void SyncFillProgressImmediate()
        {
            _mpbFillAmount = GetCurrentFillProgress();
            _mpbFillWave = 0f;
            ApplyBoxPropertyBlock();

            if ((!Application.isPlaying || isFilledBox) && ShowsCavityBalls && socketBalls != null && socketBalls.Length > 0)
            {
                int targetActive = Mathf.Clamp(Mathf.RoundToInt(_mpbFillAmount * socketBalls.Length), 0, socketBalls.Length);
                if (IsHollowPitStyle)
                {
                    EnsurePitLayoutCached();
                    for (int i = 0; i < socketBalls.Length; i++)
                    {
                        bool act = i < targetActive;
                        if (socketBalls[i] != null) socketBalls[i].SetActive(act);
                        Vector2 tPos = GetPitTargetLocal(i);
                        _pitSim[i] = new PitBallSim
                        {
                            pos = tPos,
                            vel = Vector2.zero,
                            target = tPos,
                            settled = act,
                            active = act
                        };
                        if (act && socketTransforms != null && i < socketTransforms.Length && socketTransforms[i] != null)
                        {
                            socketTransforms[i].localPosition = new Vector3(tPos.x, tPos.y, -0.055f);
                        }
                    }
                    _pitAnyActive = false;
                }
                else
                {
                    for (int i = 0; i < socketBalls.Length; i++)
                    {
                        if (socketBalls[i] != null) socketBalls[i].SetActive(i < targetActive);
                    }
                }
            }
        }

        /// <summary>
        /// Dolu kutudan bir topu eksiltir, kutu içindeki görsel top yığınından en üsttekini çıkarır ve başlangıç noktasını döndürür.
        /// </summary>
        public Vector3 PopOneFilledSupplyBallVisual(out Vector3 startWorldScale)
        {
            startWorldScale = GetSocketBallWorldScale();
            Vector3 startPos = (bodyPivot != null ? bodyPivot.position : transform.position) + new Vector3(0f, 0.04f, -0.14f);

            if (ShowsCavityBalls && socketBalls != null && socketBalls.Length > 0)
            {
                int activeCount = 0;
                int topIdx = -1;
                float bestY = float.MinValue;
                for (int i = 0; i < socketBalls.Length; i++)
                {
                    var sb = socketBalls[i];
                    if (sb == null || !sb.activeSelf) continue;
                    activeCount++;
                    float y = sb.transform.position.y;
                    if (y > bestY)
                    {
                        bestY = y;
                        topIdx = i;
                    }
                }

                if (topIdx >= 0)
                {
                    startPos = socketBalls[topIdx].transform.position;
                }

                int nextRem = Mathf.Max(0, remainingCount - 1);
                int targetActiveAfter = nextRem <= 0
                    ? 0
                    : Mathf.Clamp(Mathf.CeilToInt((nextRem / (float)Mathf.Max(1, initialCount)) * socketBalls.Length), 1, socketBalls.Length);

                while (activeCount > targetActiveAfter)
                {
                    int removeIdx = -1;
                    float highestY = float.MinValue;
                    for (int i = 0; i < socketBalls.Length; i++)
                    {
                        var sb = socketBalls[i];
                        if (sb == null || !sb.activeSelf) continue;
                        float y = sb.transform.position.y;
                        if (y > highestY)
                        {
                            highestY = y;
                            removeIdx = i;
                        }
                    }
                    if (removeIdx < 0) break;

                    socketBalls[removeIdx].SetActive(false);
                    if (_pitSim != null && removeIdx < _pitSim.Length)
                    {
                        _pitSim[removeIdx].active = false;
                    }
                    activeCount--;
                }

                if (activeCount <= 0)
                {
                    _pitAnyActive = false;
                }
                else if (IsHollowPitStyle)
                {
                    // Removing upper balls lets the remaining pile collapse and settle naturally
                    JostlePitBalls(new Vector2(0f, -0.18f), wakeAll: true);
                }
            }

            PlaySmoothGulpWave();
            if (isFilledBox)
            {
                remainingCount = Mathf.Max(0, remainingCount - 1);
                UpdateBadgeText();
                AnimateCollectionFillProgress();
                if (remainingCount <= 0)
                {
                    TransitionFilledSupplyToCollector();
                }
            }
            else
            {
                DecrementCounterAndCheckComplete();
            }
            return startPos;
        }

        private void TransitionFilledSupplyToCollector()
        {
            isFilledBox = false;
            remainingCount = Mathf.Max(1, initialCount);
            _subParticleCounter = 0;
            _nextIncomingSocketCounter = 0;
            _settledSocketCount = 0;
            if (socketBalls != null)
            {
                for (int i = 0; i < socketBalls.Length; i++)
                {
                    if (socketBalls[i] != null) socketBalls[i].SetActive(false);
                    if (_pitSim != null && i < _pitSim.Length) _pitSim[i].active = false;
                }
            }
            _pitAnyActive = false;
            RefreshSocketBallColors();
            UpdateBadgeText();
            _fillAmountTween?.Kill();
            _mpbFillAmount = 0f;
            _mpbFillWave = 0f;
            ApplyBoxPropertyBlock();
            PlaySmoothGulpWave();
            BallCollectionController.Instance?.OnMonsterPlacedInSlot(this);
        }

        private void AnimateCollectionFillProgress()
        {
            float targetFill = GetCurrentFillProgress();
            if (!Application.isPlaying)
            {
                SyncFillProgressImmediate();
                return;
            }

            _fillAmountTween?.Kill();
            float fillDuration = (remainingCount <= 0) ? 0.11f : 0.22f;
            _fillAmountTween = DOTween.To(
                    () => _mpbFillAmount,
                    x =>
                    {
                        _mpbFillAmount = x;
                        ApplyBoxPropertyBlock();
                    },
                    targetFill,
                    fillDuration)
                .SetEase(Ease.OutCubic)
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy);

            _fillWaveTween?.Kill();
            _mpbFillWave = 1f;
            ApplyBoxPropertyBlock();
            _fillWaveTween = DOTween.To(
                    () => _mpbFillWave,
                    x =>
                    {
                        _mpbFillWave = x;
                        ApplyBoxPropertyBlock();
                    },
                    0f,
                    0.40f)
                .SetEase(Ease.OutQuad)
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy);
        }

        /// <summary>
        /// Son birkaç topta "neredeyse doldu" beklentisi: kutu kısa bir parıltıyla nabız atar, sayaç daha güçlü zıplar.
        /// Kalan top azaldıkça nabız biraz güçlenir; kapanma anı daha tatmin edici olsun diye.
        /// </summary>
        /// <summary>Izgarada, çıkışı açık olup şu an slota konabilecek kutu mu?</summary>
        public bool IsPlayableInGrid =>
            assignedSlot == null && !_isMovingToSlot && !_isCompleted && !isCollectingInGrid
            && isExitClearInGrid && !IsMovementOrCollectionBlocked && gameObject.activeInHierarchy;

        private static void PlayReadyHopOnPlayableGridBoxes(MonsterCharacter except)
        {
            var gm = PuzzleGlassGameManager.Instance;
            if (gm == null || gm.activeMonsters == null) return;
            int n = 0;
            for (int i = 0; i < gm.activeMonsters.Count; i++)
            {
                var m = gm.activeMonsters[i];
                if (m == null || m == except || !m.IsPlayableInGrid) continue;
                m.PlayReadyHop(0.38f + n * 0.05f); // slot nabzının hemen ardından, soldan sağa hafif dalga
                n++;
            }
        }

        /// <summary>Oynanabilir kutunun kısa "beni seç" zıplaması (gövde pivotunda; ızgara konumuna dokunmaz).</summary>
        public void PlayReadyHop(float delay)
        {
            if (!Application.isPlaying || bodyPivot == null) return;
            DOVirtual.DelayedCall(delay, () =>
            {
                if (this == null || bodyPivot == null || !IsPlayableInGrid) return;
                bodyPivot.DOKill(true);
                bodyPivot.localPosition = new Vector3(bodyPivot.localPosition.x, 0f, bodyPivot.localPosition.z);
                bodyPivot.localScale = _originalBodyScale;
                Sequence hop = DOTween.Sequence();
                hop.Append(bodyPivot.DOLocalMoveY(0.07f, 0.11f).SetEase(Ease.OutQuad));
                hop.Join(bodyPivot.DOScale(new Vector3(_originalBodyScale.x * 0.96f, _originalBodyScale.y * 1.05f, _originalBodyScale.z), 0.11f));
                hop.Append(bodyPivot.DOLocalMoveY(0f, 0.16f).SetEase(Ease.OutBounce));
                hop.Join(bodyPivot.DOScale(_originalBodyScale, 0.16f).SetEase(Ease.OutBack));
            }, ignoreTimeScale: false);
        }

        private void PlayNearCompletePulse()
        {
            float urgency = 1f - (remainingCount - 1f) / Mathf.Max(1f, NearCompleteThreshold); // son topta 1
            float peak = Mathf.Lerp(0.45f, 0.85f, urgency);

            if (_nearCompleteSeq != null && _nearCompleteSeq.IsActive()) _nearCompleteSeq.Kill();
            _nearCompleteSeq = DOTween.Sequence();
            _nearCompleteSeq.Append(DOTween.To(() => _mpbDragHighlight, v => { _mpbDragHighlight = v; ApplyBoxPropertyBlock(); }, peak, 0.06f).SetEase(Ease.OutQuad));
            _nearCompleteSeq.Append(DOTween.To(() => _mpbDragHighlight, v => { _mpbDragHighlight = v; ApplyBoxPropertyBlock(); }, 0f, 0.22f).SetEase(Ease.InQuad));

            if (counterText != null)
            {
                counterText.transform.DOKill(true);
                counterText.transform.DOPunchScale(Vector3.one * Mathf.Lerp(0.32f, 0.46f, urgency), 0.20f, 6, 0.6f);
            }
        }

        private void DecrementCounterAndCheckComplete()
        {
            remainingCount = Mathf.Max(0, remainingCount - 1);
            UpdateBadgeText();
            AnimateCollectionFillProgress();

            if (!isFilledBox && Application.isPlaying)
            {
                // Her top: çok kısa, hafif dokunsal tık (hızlı akışta seyreltilir)
                BlockClearCelebration.VibrateTick();
                if (remainingCount > 0 && remainingCount <= NearCompleteThreshold) PlayNearCompletePulse();
            }

            OnEatenCube?.Invoke(this);
            if (PuzzleGlassGameManager.Instance != null)
            {
                PuzzleGlassGameManager.Instance.OnCarrierBoxBallConsumed(this);
            }

            if (remainingCount <= 0)
            {
                // Beklenti nabzı tamamlanma parıltısıyla aynı değeri sürer: çakışmasın
                if (_nearCompleteSeq != null && _nearCompleteSeq.IsActive()) _nearCompleteSeq.Kill();
                _isCompleted = true;
                StagingGridBoard.Instance?.MarkGridDirty();
                // Yapışık küme koşulu: bu renkte kutu tamamlandı
                if (!isFilledBox) BallClusters.NotifyBoxCompleted(colorType);

                if (!isFilledBox && Application.isPlaying && badgeTransform != null && counterText != null)
                {
                    badgeTransform.gameObject.SetActive(true);
                    counterText.text = "0";
                    counterText.transform.DOKill(true);
                    counterText.transform.localScale = Vector3.one;
                    counterText.transform.DOPunchScale(Vector3.one * 0.50f, 0.22f, 6, 0.55f);
                }

                if (ShowsCavityBalls && !isFilledBox)
                {
                    // Ensure all sockets / pit balls visibly hold settled balls on completion
                    int sockCount = (socketBalls != null) ? socketBalls.Length : 0;
                    for (int i = 0; i < sockCount; i++)
                    {
                        if (socketBalls[i] != null && !socketBalls[i].activeSelf)
                        {
                            ActivateAndPulseSocketBall(i, playSeatEffect: false);
                        }
                    }
                }
                else if (isFilledBox && socketBalls != null)
                {
                    for (int i = 0; i < socketBalls.Length; i++)
                    {
                        if (socketBalls[i] != null) socketBalls[i].SetActive(false);
                        if (_pitSim != null && i < _pitSim.Length) _pitSim[i].active = false;
                    }
                    _pitAnyActive = false;
                }
                StartCoroutine(CelebrateAndCompleteRoutine());
            }
        }

        private IEnumerator PlaySocketCompletionWaveRoutine()
        {
            int sockCount = (socketBalls != null) ? socketBalls.Length : 0;
            if (sockCount <= 0) yield break;

            ComputeActiveBallLayout(GetVisibleSocketCount(), out _, out _, out _socketBallLocalDiameter);
            Vector3 baseScale = Vector3.one * _socketBallLocalDiameter;
            float scaleFactor = (bodyPivot != null) ? bodyPivot.lossyScale.x : transform.lossyScale.x;
            float ringRadius = Mathf.Max(0.07f, _socketBallLocalDiameter * 0.56f * scaleFactor);
            Color glintCol = Color.Lerp(PuzzleColorPalette.GetPrimaryColor(colorType), Color.white, 0.55f);

            float stepDelay = Mathf.Clamp(0.11f / sockCount, 0.012f, 0.026f);
            for (int i = 0; i < sockCount; i++)
            {
                GameObject sBall = socketBalls[i];
                if (sBall == null || !sBall.activeSelf) continue;

                Transform tr = sBall.transform;
                tr.DOKill();
                tr.localScale = baseScale;

                Sequence popSeq = DOTween.Sequence();
                popSeq.Append(tr.DOScale(baseScale * 1.20f, 0.045f).SetEase(Ease.OutQuad));
                popSeq.Join(tr.DOLocalMoveZ(-0.020f, 0.045f).SetEase(Ease.OutQuad));
                popSeq.Append(tr.DOScale(baseScale, 0.075f).SetEase(Ease.OutBack));
                popSeq.Join(tr.DOLocalMoveZ(0f, 0.075f).SetEase(Ease.OutQuad));
                popSeq.SetLink(sBall);

                if (Application.isPlaying)
                {
                    BlockClearCelebration.PlaySocketSeatRing(tr.position, glintCol, ringRadius, 0.52f);
                }

                yield return new WaitForSeconds(stepDelay);
            }

            yield return new WaitForSeconds(0.035f);
        }

        private IEnumerator CelebrateAndCompleteRoutine()
        {
            StopIdleAndBlink();
            if (_gulpGlowTween != null && _gulpGlowTween.IsActive()) _gulpGlowTween.Kill();
            _gulpGlowTween = null;

            if (transparentLidTransform != null && transparentLidTransform.gameObject.activeSelf)
            {
                transparentLidTransform.gameObject.SetActive(false);
            }

            // Smooth patlama animasyonu: aniden yok olmadan önce yumuşakça küçülür, gerilerek büyür, kısa bir tepe anından sonra patlar
            if (_shadowRenderer != null) _shadowRenderer.enabled = false;

            if (ShowsCavityBalls && !isFilledBox && !IsHollowPitStyle)
            {
                StartCoroutine(PlaySocketCompletionWaveRoutine());
            }

            Transform animTarget = bodyPivot != null ? bodyPivot : transform;
            Vector3 baseS = bodyPivot != null ? _originalBodyScale : transform.localScale;
            animTarget.DOKill(false);

            const float shrinkDur = 0.19f;
            const float swellDur = 0.23f;
            const float peakHoldDur = 0.045f;

            Sequence popSeq = DOTween.Sequence();
            popSeq.Append(animTarget.DOScale(new Vector3(baseS.x * 0.80f, baseS.y * 0.76f, baseS.z * 0.88f), shrinkDur).SetEase(Ease.InOutQuad));
            if (bodyPivot != null)
            {
                popSeq.Join(bodyPivot.DOLocalMove(new Vector3(0f, -0.024f, 0f), shrinkDur).SetEase(Ease.InOutQuad));
                popSeq.Join(bodyPivot.DOLocalRotate(Vector3.zero, shrinkDur * 0.65f).SetEase(Ease.OutSine));
            }
            popSeq.Join(DOTween.To(() => _mpbDragHighlight, v => { _mpbDragHighlight = v; ApplyBoxPropertyBlock(); }, 0.55f, shrinkDur).SetEase(Ease.InOutSine));

            popSeq.Append(animTarget.DOScale(new Vector3(baseS.x * 1.30f, baseS.y * 1.26f, baseS.z * 1.16f), swellDur).SetEase(Ease.InOutSine));
            if (bodyPivot != null)
            {
                popSeq.Join(bodyPivot.DOLocalMoveY(0.030f, swellDur).SetEase(Ease.InOutSine));
            }
            popSeq.Join(DOTween.To(() => _mpbDragHighlight, v => { _mpbDragHighlight = v; ApplyBoxPropertyBlock(); }, 1.85f, swellDur).SetEase(Ease.InQuad));

            popSeq.Append(animTarget.DOScale(new Vector3(baseS.x * 1.34f, baseS.y * 1.30f, baseS.z * 1.18f), peakHoldDur).SetEase(Ease.OutQuad));
            popSeq.SetLink(gameObject, LinkBehaviour.KillOnDisable);
            yield return popSeq.WaitForCompletion();

            PuzzleSoundManager.Instance?.PlayBoxComplete();

            if (PuzzleGlassGameManager.Instance != null)
            {
                PuzzleGlassGameManager.Instance.OnCarrierBoxCompleted(this);
            }
            if (assignedSlot != null)
            {
                SlotPlatform freedSlot = assignedSlot;
                assignedSlot.DetachMonster();
                assignedSlot = null;
                freedSlot.PlayFreedPulse(0.24f);
                PlayReadyHopOnPlayableGridBoxes(this);
            }
            OnTargetReached?.Invoke(this);

            _pitAnyActive = false;
            Vector3 popCenter = (bodyPivot != null) ? bodyPivot.position : transform.position;
            int scatterCount = Mathf.Clamp(initialCount > 0 ? initialCount : 14, 14, 22);
            BlockClearCelebration.PlayBalloonPop(popCenter, PuzzleColorPalette.GetPrimaryColor(colorType), colorType, 1.20f, scatterCount);

            transform.DOKill(false);
            if (bodyPivot != null) bodyPivot.DOKill(false);

            _mpbDragHighlight = 0f;
            ApplyBoxPropertyBlock();
            if (_shadowRenderer != null) _shadowRenderer.enabled = true;
            if (transparentLidTransform != null)
            {
                transparentLidTransform.localPosition = _lidClosedLocalPos;
                transparentLidTransform.localRotation = Quaternion.identity;
                transparentLidTransform.localScale = Vector3.one;
                transparentLidTransform.gameObject.SetActive(false);
            }
            if (bodyPivot != null)
            {
                bodyPivot.localPosition = Vector3.zero;
                bodyPivot.localRotation = Quaternion.identity;
                bodyPivot.localScale = _originalBodyScale;
            }

            if (badgeTransform != null)
            {
                badgeTransform.gameObject.SetActive(false);
            }

            isCollectingInGrid = false;
            gameObject.SetActive(false);

            if (PuzzleGlassGameManager.Instance != null)
            {
                PuzzleGlassGameManager.Instance.OnGridBlockCompleted(this);
            }

            if (BallCollectionController.Instance != null)
            {
                BallCollectionController.Instance.CheckLevelCompletion();
            }
        }

        /// <summary>
        /// Direct in-grid collection (no upper slots):
        /// When this block sits at the top edge of the grid ("en üst taraf") AND the glass bottom currently exposes
        /// at least one matching ball, activates collection so matching balls fly straight from the glass into its sockets!
        /// </summary>
        public bool TryStartInGridCollection(bool playDenialFeedback = true)
        {
            if (_isMovingToSlot || _isCompleted || remainingCount <= 0 || !gameObject.activeInHierarchy) return false;
            if (IsMovementOrCollectionBlocked)
            {
                if (playDenialFeedback) PlayBlockedMechanicFeedback();
                return false;
            }

            if (isCollectingInGrid)
            {
                if (playDenialFeedback) PlayWaitingForBallsFeedback();
                return true;
            }

            if (StagingGridBoard.Instance != null)
            {
                bool atTopExit = StagingGridBoard.Instance.IsBlockAtTopExit(this);
                SetExitClearHighlight(atTopExit);
                if (!atTopExit)
                {
                    if (playDenialFeedback) PlayLockedInSlotFeedback();
                    return false;
                }
            }

            var collector = BallCollectionController.Instance.OrNull() ?? FindFirstObjectByType<BallCollectionController>();
            if (collector == null || !collector.HasReachableBottomBallForColor(colorType, this))
            {
                if (playDenialFeedback)
                {
                    transform.DOKill(true);
                    transform.DOShakePosition(0.26f, strength: new Vector3(0.09f, 0f, 0f), vibrato: 16, randomness: 90, snapping: false, fadeOut: true);
                }
                return false;
            }

            isCollectingInGrid = true;
            transform.DOKill(true);
            transform.DOPunchScale(Vector3.one * 0.08f, 0.20f, 4, 0.6f);

            collector.ActivateGridCollector(this);

            if (SandMonsterFlowBridge.Instance != null)
            {
                SandMonsterFlowBridge.Instance.NotifyMonsterPlacedInSlot(this);
            }

            if (PuzzleGlassGameManager.Instance != null)
            {
                PuzzleGlassGameManager.Instance.OnStagingBoxPlacedInSlot(this);
            }

            return true;
        }

        public void PauseInGridCollection()
        {
            if (!isCollectingInGrid) return;
            isCollectingInGrid = false;
            var collector = BallCollectionController.Instance.OrNull() ?? FindFirstObjectByType<BallCollectionController>();
            if (collector != null)
            {
                collector.OnMonsterRemovedFromSlot(this);
            }
        }

        public bool TryHopToNextAvailableSlot()
        {
            return TryStartInGridCollection(playDenialFeedback: true);
        }

        public void PlayLockedInSlotFeedback()
        {
            if (_isCompleted || bodyPivot == null) return;
            bodyPivot.DOKill(false);
            bodyPivot.localRotation = Quaternion.identity;
            Sequence s = DOTween.Sequence();
            s.Append(bodyPivot.DOLocalRotate(new Vector3(0f, 0f, 6f), 0.06f).SetEase(Ease.OutSine));
            s.Append(bodyPivot.DOLocalRotate(new Vector3(0f, 0f, -6f), 0.08f).SetEase(Ease.InOutSine));
            s.Append(bodyPivot.DOLocalRotate(Vector3.zero, 0.06f).SetEase(Ease.OutSine));
        }

        /// <summary>
        /// Returns an unslotted staging box back to its staging tray position (when dropped outside an empty slot).
        /// Note: Once assigned to a slot, a box is permanently locked in that slot until completed.
        /// </summary>
        public void ReturnToStagingHome(Action onComplete = null)
        {
            if (_isCompleted) return;
            if (assignedSlot != null)
            {
                // Never detach a box that has already been placed in a slot
                onComplete?.Invoke();
                return;
            }

            EnsureStagingHomeRecorded();
            StopAllCoroutines();
            _isMovingToSlot = false;

            Vector3 targetHomePos = hasStagingHome ? stagingHomePosition : new Vector3(transform.position.x, -1.98f, 0f);
            Vector3 targetHomeScale = (hasStagingHome && stagingHomeScale.sqrMagnitude > 0.01f) ? stagingHomeScale : Vector3.one * 0.64f;

            transform.DOKill(false);
            if (bodyPivot != null) bodyPivot.localRotation = Quaternion.identity;

            Sequence returnSeq = DOTween.Sequence();
            returnSeq.Join(transform.DOJump(targetHomePos, jumpPower: 0.36f, numJumps: 1, duration: 0.24f).SetEase(Ease.OutQuad));
            returnSeq.Join(transform.DOScale(targetHomeScale, 0.22f).SetEase(Ease.OutBack));
            returnSeq.OnComplete(() =>
            {
                transform.position = targetHomePos;
                transform.localScale = targetHomeScale;
                if (Application.isPlaying)
                {
                    StartIdleAndBlink();
                }
                onComplete?.Invoke();
            });
        }

        /// <summary>
        /// Subtle tilt feedback when placed in a slot where its balls are currently blocked above other balls.
        /// </summary>
        public void PlayWaitingForBallsFeedback()
        {
            if (_isCompleted || bodyPivot == null) return;
            PuzzleSoundManager.Instance?.PlayBlocked();
            if (PuzzleGlassGameManager.Instance != null)
            {
                PuzzleGlassGameManager.Instance.OnCarrierBoxBlockedInSlot(this);
            }
            bodyPivot.DOKill(false);
            Sequence s = DOTween.Sequence();
            s.Append(bodyPivot.DOLocalRotate(new Vector3(0f, 0f, 8f), 0.10f).SetEase(Ease.OutSine));
            s.Append(bodyPivot.DOLocalRotate(new Vector3(0f, 0f, -8f), 0.14f).SetEase(Ease.InOutSine));
            s.Append(bodyPivot.DOLocalRotate(Vector3.zero, 0.10f).SetEase(Ease.OutSine));
        }

        private IEnumerator HopToSlotRoutine(SlotPlatform slot)
        {
            _isMovingToSlot = true;
            Vector3 startScale = transform.localScale;
            Vector3 targetScale = GetSlotFitScale();
            Vector3 targetPos = slot.GetPlacementPosition();

            // 1. Smooth anticipation cushion
            yield return transform.DOScale(new Vector3(startScale.x * 1.06f, startScale.y * 0.92f, startScale.z * 1.06f), 0.06f)
                .SetEase(Ease.OutSine).WaitForCompletion();

            // 2. Parabolic arc into the slot while scaling proportionally to fit inside 1 slot
            Sequence leapSeq = DOTween.Sequence();
            leapSeq.Join(transform.DOJump(targetPos, jumpPower: 0.45f, numJumps: 1, duration: 0.26f).SetEase(Ease.OutCubic));
            leapSeq.Join(transform.DOScale(targetScale, 0.24f).SetEase(Ease.OutBack));
            yield return leapSeq.WaitForCompletion();

            transform.position = targetPos;

            // 3. Attach to slot (locked in place!) & start collecting bottom-most available balls
            slot.AttachMonster(this);
            BlockClearCelebration.VibrateSlotPlace();

            if (BallCollectionController.Instance != null)
            {
                BallCollectionController.Instance.OnMonsterPlacedInSlot(this);
            }

            if (SandMonsterFlowBridge.Instance != null)
            {
                SandMonsterFlowBridge.Instance.NotifyMonsterPlacedInSlot(this);
            }

            CubeDispenser dispenser = FindFirstObjectByType<CubeDispenser>();
            if (dispenser != null)
            {
                dispenser.RegisterMonsterOrder(this);
                dispenser.StartFeedingSlottedMonsters();
            }

            // 4. Gentle landing settle
            Sequence settleSeq = DOTween.Sequence();
            settleSeq.Append(transform.DOScale(new Vector3(targetScale.x * 1.06f, targetScale.y * 0.94f, targetScale.z * 1.06f), 0.07f).SetEase(Ease.OutSine));
            settleSeq.Append(transform.DOScale(targetScale, 0.12f).SetEase(Ease.OutCubic));
            yield return settleSeq.WaitForCompletion();

            transform.localScale = targetScale;
            _isMovingToSlot = false;
        }

        // ==================== SPECIAL MECHANICS (KEY / LOCK / ICE) ====================

        private Vector2 GetMechanicAnchorLocal()
        {
            CarrierBlockShapeUtility.GetRectParts(blockShape, out Vector4 r0, out _, out _, out _, out _);
            return new Vector2(r0.x, r0.y);
        }

        /// <summary>Bloğun en üst sıradaki en sağ hücresinin sağ üst köşesi (yerel). Anahtar rozeti buraya asılır.</summary>
        private Vector2 GetTopRightCornerLocal()
        {
            var cells = CarrierBlockShapeUtility.GetCells(blockShape);
            CarrierBlockShapeUtility.GetBounds(blockShape, out int w, out int h);
            Vector2Int best = cells.Length > 0 ? cells[0] : Vector2Int.zero;
            foreach (var c in cells)
                if (c.y > best.y || (c.y == best.y && c.x > best.x)) best = c;
            // GetRectParts ile aynı yerel uzay: hücre merkezi = (x + 0.5 - w/2, y + 0.5 - h/2 + 0.024)
            return new Vector2(best.x + 1f - w * 0.5f - 0.055f, best.y + 1f - h * 0.5f + 0.024f - 0.075f);
        }

        public Vector3 GetKeyWorldPosition()
        {
            if (_keyOverlayRoot != null) return _keyOverlayRoot.transform.position;
            Vector2 c = GetMechanicAnchorLocal();
            return transform.TransformPoint(new Vector3(c.x, c.y, -0.28f));
        }

        public Vector3 GetLockWorldPosition()
        {
            Vector2 c = GetMechanicAnchorLocal();
            Transform t = bodyPivot != null ? bodyPivot : transform;
            return t.TransformPoint(new Vector3(c.x, c.y, -0.30f));
        }

        /// <summary>Uçan anahtarın renkleri: beyaz gövde (plaka rengi), bloğun renginde kenar.</summary>
        public Color KeyFillColor => new Color(0.97f, 0.97f, 0.98f, 1f);
        public Color KeyEdgeColor => GetCarrierFrameColor(colorType);

        /// <summary>Asma kilidin anahtar deliğinin dünya konumu (anahtar buraya girer).</summary>
        public Vector3 GetKeyholeWorldPosition()
        {
            Vector2 c = GetMechanicAnchorLocal() + LockKeyVisuals.KeyholeOffset * LockKeyVisuals.GlyphScale;
            Transform t = bodyPivot != null ? bodyPivot : transform;
            return t.TransformPoint(new Vector3(c.x, c.y, -0.30f));
        }

        /// <summary>Kilitli bloğa dokunulunca anahtarlı bloğun rozeti zıplar (hangi bloğu doldurman gerektiğini gösterir).</summary>
        public void PlayKeyHint()
        {
            if (!hasKey || _keyConsumedVisually || _isCompleted || _keyOverlayRoot == null || !_keyOverlayRoot.activeInHierarchy) return;
            LockKeyVisuals.PlayKeyHint(_keyOverlayRoot);
            if (bodyPivot != null)
            {
                bodyPivot.DOKill(true);
                bodyPivot.DOPunchPosition(new Vector3(0f, 0.10f, 0f), 0.35f, 4, 0.5f);
            }
        }

        public void HideKeyForFlight()
        {
            _keyConsumedVisually = true;
            if (_keyOverlayRoot != null) _keyOverlayRoot.SetActive(false);
        }

        public void RefreshSpecialMechanicVisuals()
        {
            _mpbIceAmount = IsFrozenByIce ? 1.0f : 0.0f;
            // Kilitli blok: plaka kendi renginde kapakla kapanır, ortasında beyaz kilit (açılırken animasyon yönetir)
            if (!_unlockAnimPlaying)
            {
                _mpbLockAmount = IsLockedByKey ? 1.0f : 0.0f;
                Vector2 lc = GetMechanicAnchorLocal();
                _mpbLockCenter = new Vector4(lc.x, lc.y, LockKeyVisuals.GlyphScale, 0f);
                _mpbLockOpen = 0f;
                _mpbLockWiggle = 0f;
            }
            bool effectiveExit = (isExitClearInGrid || isCollectingInGrid) && !IsMovementOrCollectionBlocked;
            _mpbExitHighlight = effectiveExit ? 1.0f : 0.0f;
            ApplyBoxPropertyBlock();

            if (bodyPivot == null) return;

            bool showKey = hasKey && !_keyConsumedVisually;
            bool showLock = remainingLockKeys > 0;
            bool showIce = remainingIceCount > 0;

            Vector2 center = GetMechanicAnchorLocal();

            if (showKey)
            {
                if (_keyOverlayRoot == null) _keyOverlayRoot = BuildKeyOverlay(bodyPivot);
                // Anahtar ikonu bloğun sağ üst köşesine net şekilde oturur (sayıyı kapatmaz)
                Vector2 corner = GetTopRightCornerLocal();
                _keyOverlayRoot.transform.localPosition = new Vector3(corner.x - 0.06f, corner.y - 0.05f, -0.32f);
                _keyOverlayRoot.transform.localScale = Vector3.one;
                _keyOverlayRoot.SetActive(true);
            }
            else if (_keyOverlayRoot != null)
            {
                _keyOverlayRoot.SetActive(false);
            }

            if (showLock)
            {
                if (_lockOverlayRoot == null) _lockOverlayRoot = BuildLockOverlay(bodyPivot, out _lockCountText);
                _lockOverlayRoot.transform.localPosition = new Vector3(center.x, center.y, -0.31f);
                _lockOverlayRoot.transform.localScale = Vector3.one;
                Transform lockSpriteT = _lockOverlayRoot.transform.Find("LockSprite");
                if (lockSpriteT != null && !_unlockAnimPlaying)
                {
                    Sprite lockSp = LockKeyVisuals.GetLockSprite();
                    lockSpriteT.localPosition = new Vector3(0f, 0.01f, -0.02f);
                    lockSpriteT.localRotation = Quaternion.identity;
                    if (lockSp != null) lockSpriteT.localScale = LockKeyVisuals.GetIconScale(lockSp, 0.44f);
                }
                _lockOverlayRoot.SetActive(true);
                if (_lockCountText != null)
                {
                    _lockCountText.text = remainingLockKeys.ToString();
                    _lockCountText.gameObject.SetActive(lockKeyCount > 1);
                }
            }
            else if (_lockOverlayRoot != null && !_unlockAnimPlaying)
            {
                _lockOverlayRoot.SetActive(false);
            }

            if (showIce)
            {
                if (_iceOverlayRoot == null) _iceOverlayRoot = BuildIceOverlay(bodyPivot, out _iceCountText);
                Vector2 iceOffset = showLock ? new Vector2(-0.14f, -0.10f) : Vector2.zero;
                _iceOverlayRoot.transform.localPosition = new Vector3(center.x + iceOffset.x, center.y + iceOffset.y, -0.33f);
                _iceOverlayRoot.SetActive(true);
                if (_iceCountText != null) _iceCountText.text = remainingIceCount.ToString();
            }
            else if (_iceOverlayRoot != null)
            {
                _iceOverlayRoot.SetActive(false);
            }

            UpdateBadgeText();
        }

        /// <summary>
        /// Reduces the remaining key lock counter. Returns true if the block just became unlocked by keys.
        /// </summary>
        public bool DecrementLockKey(int amount = 1)
        {
            if (remainingLockKeys <= 0) return false;
            remainingLockKeys = Mathf.Max(0, remainingLockKeys - Mathf.Max(1, amount));

            if (remainingLockKeys > 0)
            {
                if (_lockCountText != null) _lockCountText.text = remainingLockKeys.ToString();
                if (_lockOverlayRoot != null && Application.isPlaying)
                {
                    _lockOverlayRoot.transform.DOKill(true);
                    _lockOverlayRoot.transform.DOPunchScale(Vector3.one * 0.22f, 0.22f, 6, 0.5f);
                }
                return false;
            }

            // Açıldı! Halka kalkar, kapak açılır, plaka ve delikler görünür
            if (_lockCountText != null) _lockCountText.gameObject.SetActive(false);
            if (Application.isPlaying)
            {
                _unlockAnimPlaying = true;
                Transform lockVisual = _lockOverlayRoot != null ? _lockOverlayRoot.transform.Find("LockSprite") : null;
                LockKeyVisuals.PlayUnlock(this, GetLockWorldPosition(), GetCarrierFrameColor(colorType), () =>
                {
                    _unlockAnimPlaying = false;
                    if (_lockOverlayRoot != null) _lockOverlayRoot.SetActive(false);
                    RefreshSpecialMechanicVisuals();
                    StagingGridBoard.Instance?.MarkGridDirty();
                }, lockVisual);
            }
            else if (_lockOverlayRoot != null)
            {
                _lockOverlayRoot.SetActive(false);
            }

            if (bodyPivot != null && Application.isPlaying)
            {
                bodyPivot.DOKill(true);
                bodyPivot.DOPunchScale(Vector3.one * 0.08f, 0.22f, 4, 0.6f);
            }

            RefreshSpecialMechanicVisuals();
            return !IsMovementOrCollectionBlocked;
        }

        /// <summary>
        /// Reduces the remaining ice melt counter. Returns true if the ice just melted completely.
        /// </summary>
        public bool DecrementIce(int amount = 1)
        {
            if (remainingIceCount <= 0) return false;
            remainingIceCount = Mathf.Max(0, remainingIceCount - Mathf.Max(1, amount));

            if (remainingIceCount > 0)
            {
                if (_iceCountText != null) _iceCountText.text = remainingIceCount.ToString();
                if (_iceOverlayRoot != null && Application.isPlaying)
                {
                    _iceOverlayRoot.transform.DOKill(true);
                    _iceOverlayRoot.transform.DOPunchScale(Vector3.one * 0.14f, 0.16f, 4, 0.5f);
                }
                return false;
            }

            // Ice melted! Smooth ice shatter & reveal underlying color
            if (Application.isPlaying)
            {
                DOTween.To(
                    () => _mpbIceAmount,
                    v => { _mpbIceAmount = v; ApplyBoxPropertyBlock(); },
                    0f,
                    0.18f);
            }
            else
            {
                _mpbIceAmount = 0f;
                ApplyBoxPropertyBlock();
            }

            if (_iceOverlayRoot != null && Application.isPlaying)
            {
                GameObject iceObj = _iceOverlayRoot;
                iceObj.transform.DOKill();
                Sequence seq = DOTween.Sequence();
                seq.Append(iceObj.transform.DOScale(Vector3.one * 1.25f, 0.08f).SetEase(Ease.OutQuad));
                seq.Append(iceObj.transform.DOScale(Vector3.zero, 0.14f).SetEase(Ease.InBack));
                seq.OnComplete(() =>
                {
                    if (iceObj != null) iceObj.SetActive(false);
                });
            }
            else if (_iceOverlayRoot != null)
            {
                _iceOverlayRoot.SetActive(false);
            }

            if (bodyPivot != null && Application.isPlaying)
            {
                bodyPivot.DOKill(true);
                bodyPivot.DOPunchScale(Vector3.one * 0.09f, 0.24f, 5, 0.6f);
            }

            UpdateBadgeText();
            return !IsMovementOrCollectionBlocked;
        }

        public void PlayBlockedMechanicFeedback()
        {
            if (_isCompleted) return;
            PuzzleSoundManager.Instance?.PlayBlocked();

            transform.DOKill(true);
            transform.DOShakePosition(0.24f, strength: new Vector3(0.065f, 0f, 0f), vibrato: 14, randomness: 70f, snapping: false, fadeOut: true);

            if (IsLockedByKey && _lockOverlayRoot != null)
            {
                Transform lockVisual = _lockOverlayRoot.transform.Find("LockSprite");
                LockKeyVisuals.PlayLockWiggle(this, lockVisual);

                // İpucu: anahtarı taşıyan bloklar zıplar
                var gm = PuzzleGlassGameManager.Instance;
                if (gm != null && gm.activeMonsters != null)
                    foreach (var m in gm.activeMonsters)
                        if (m != null && m != this) m.PlayKeyHint();
            }
            else if (IsFrozenByIce && _iceOverlayRoot != null)
            {
                _iceOverlayRoot.transform.DOKill(true);
                _iceOverlayRoot.transform.DOPunchScale(Vector3.one * 0.18f, 0.20f, 5, 0.6f);
            }
        }

        private static Material CreateMechanicMaterial(Color color, string matName, bool glossy = true)
        {
            return PuzzleColorPalette.CreateToonyHypercasualMaterial(
                color,
                Color.Lerp(color, new Color(0.22f, 0.16f, 0.10f, 1f), 0.35f),
                matName,
                glossyCandy: glossy,
                indirectIntensity: 0.20f);
        }

        private static void ApplyTmpOutline(TextMeshPro tmp, Color outlineColor, float outlineWidth)
        {
            if (tmp == null) return;
            if (tmp.fontSharedMaterial != null)
            {
                Material inst = new Material(tmp.fontSharedMaterial)
                {
                    name = $"{tmp.fontSharedMaterial.name}_MechanicInst"
                };
                if (inst.HasProperty("_OutlineColor")) inst.SetColor("_OutlineColor", outlineColor);
                if (inst.HasProperty("_OutlineWidth")) inst.SetFloat("_OutlineWidth", outlineWidth);
                tmp.fontSharedMaterial = inst;
            }
        }

        private static void ApplyBoxNumberStyle(TextMeshPro tmp, Color outlineColor)
        {
            if (tmp == null || tmp.fontSharedMaterial == null) return;
            Material inst = new Material(tmp.fontSharedMaterial)
            {
                name = $"{tmp.fontSharedMaterial.name}_BoxNumberInst"
            };
            if (inst.HasProperty("_FaceDilate")) inst.SetFloat("_FaceDilate", 0.28f);
            if (inst.HasProperty("_OutlineColor")) inst.SetColor("_OutlineColor", outlineColor);
            if (inst.HasProperty("_OutlineWidth")) inst.SetFloat("_OutlineWidth", 0.16f);
            if (inst.HasProperty("_OutlineSoftness")) inst.SetFloat("_OutlineSoftness", 0f);
            if (inst.HasProperty("_UnderlayColor"))
            {
                inst.EnableKeyword("UNDERLAY_ON");
                inst.SetColor("_UnderlayColor", new Color(outlineColor.r, outlineColor.g, outlineColor.b, 0.85f));
                inst.SetFloat("_UnderlayOffsetX", 0f);
                inst.SetFloat("_UnderlayOffsetY", -0.75f);
                inst.SetFloat("_UnderlayDilate", 0.45f);
                inst.SetFloat("_UnderlaySoftness", 0.05f);
            }
            tmp.fontSharedMaterial = inst;
        }

        private GameObject BuildKeyOverlay(Transform parent)
        {
            return LockKeyVisuals.CreateKeyBadge(parent, GetCarrierFrameColor(colorType), new Color(0.97f, 0.97f, 0.98f, 1f),
                GetCarrierDarkColor(colorType), "KeyOverlay");
        }

        /// <summary>Kapak shader'da çizilir; üstünde Silver Lock ikonu ve çoklu anahtar sayacı yer alır.</summary>
        private GameObject BuildLockOverlay(Transform parent, out TextMeshPro countLabel)
        {
            GameObject root = new GameObject("LockOverlay");
            root.transform.SetParent(parent, false);

            Sprite lockSprite = LockKeyVisuals.GetLockSprite();
            if (lockSprite != null)
            {
                LockKeyVisuals.CreateSpriteIcon("LockSprite", root.transform, lockSprite, 0.44f, new Vector3(0f, 0.01f, -0.02f), 15);
            }

            GameObject textObj = new GameObject("LockCountText");
            textObj.transform.SetParent(root.transform, false);
            textObj.transform.localPosition = new Vector3(0.17f, -0.17f, -0.05f);

            countLabel = textObj.AddComponent<TextMeshPro>();
            if (counterText != null && counterText.font != null)
            {
                countLabel.font = counterText.font;
            }
            countLabel.alignment = TextAlignmentOptions.Center;
            countLabel.fontSize = 2.9f;
            countLabel.fontStyle = FontStyles.Bold;
            countLabel.color = Color.white;
            countLabel.text = remainingLockKeys.ToString();
            countLabel.rectTransform.sizeDelta = new Vector2(0.5f, 0.5f);
            countLabel.sortingOrder = 16;
            countLabel.extraPadding = true;
            countLabel.characterSpacing = -4f;
            ApplyBoxNumberStyle(countLabel, new Color(0.04f, 0.05f, 0.10f, 1f));
            textObj.SetActive(lockKeyCount > 1);

            return root;
        }

        private GameObject BuildIceOverlay(Transform parent, out TextMeshPro countLabel)
        {
            GameObject root = new GameObject("IceOverlay");
            root.transform.SetParent(parent, false);

            if (s_SharedFrostMat == null)
            {
                s_SharedFrostMat = CreateMechanicMaterial(new Color(0.88f, 0.98f, 1.00f, 1f), "Mat_IceFrostCrest_Shared", glossy: true);
            }
            AddMeshPart("FrostGlintLeft", root.transform, new Vector3(-0.22f, 0.20f, 0f), ProceduralMeshUtils.CreatePillBadge(0.18f, 0.048f, 0.02f, 0.022f), s_SharedFrostMat);
            AddMeshPart("FrostGlintRight", root.transform, new Vector3(0.20f, 0.18f, 0f), ProceduralMeshUtils.CreatePillBadge(0.12f, 0.042f, 0.02f, 0.020f), s_SharedFrostMat);

            float yOffset = (visualStyle == BlockVisualStyle.PuzzleWithNumber) ? -0.032f : 0.01f;
            GameObject textObj = new GameObject("IceCountText");
            textObj.transform.SetParent(root.transform, false);
            textObj.transform.localPosition = new Vector3(0f, yOffset, -0.04f);

            countLabel = textObj.AddComponent<TextMeshPro>();
            if (counterText != null && counterText.font != null)
            {
                countLabel.font = counterText.font;
            }
            countLabel.alignment = TextAlignmentOptions.Center;
            float baseSize = (visualStyle == BlockVisualStyle.PuzzleWithNumber) ? 3.8f : 4.6f;
            countLabel.fontSize = (remainingIceCount >= 100) ? (baseSize - 0.7f) : baseSize;
            countLabel.fontStyle = FontStyles.Bold;
            countLabel.color = Color.white;
            countLabel.text = remainingIceCount.ToString();
            countLabel.rectTransform.sizeDelta = new Vector2(1.1f, 0.7f);
            countLabel.sortingOrder = 14;
            countLabel.extraPadding = true;
            countLabel.characterSpacing = -4f;
            ApplyBoxNumberStyle(countLabel, new Color(0.04f, 0.05f, 0.10f, 1f));

            return root;
        }

        private static void AddMeshPart(string name, Transform parent, Vector3 localPos, Mesh mesh, Material mat)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = localPos;
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = obj.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }
    }
}
