using System;
using System.Collections;
using TMPro;
using UnityEngine;
using DG.Tweening;

namespace PuzzleGlass
{
    [ExecuteAlways]
    public class MonsterCharacter : MonoBehaviour
    {
        public const int MaxVisualSockets = 16;

        [Header("Crate Presentation")]
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
        private static Material s_SharedSocketBallMaterial;
        private static Material s_SharedBadgeMaterial;
        private static Material s_SharedShadowMatGrid;
        private static Material s_SharedShadowMatTray;
        private static Material s_SharedLidGlareMat;
        private static Material s_SharedFrostMat;
        private static readonly System.Collections.Generic.Dictionary<PuzzleColor, Material> s_SharedLidWindowMats = new System.Collections.Generic.Dictionary<PuzzleColor, Material>();
        private static readonly System.Collections.Generic.Dictionary<PuzzleColor, Material> s_SharedLidLatchMats = new System.Collections.Generic.Dictionary<PuzzleColor, Material>();
        private static MaterialPropertyBlock s_BoxPropertyBlock;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSharedMaterialCaches()
        {
            s_SharedCarrierBoxMaterial = null;
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
        private int _builtSocketCount = -1;
        private Color _mpbBodyColor = Color.white;
        private Color _mpbBodyDarkColor = Color.gray;
        private Color _mpbOutlineColor = Color.black;
        private Color _mpbHighlightColor = Color.white;
        private Vector4 _mpbLocalSize = new Vector4(1.16f, 1.16f, 0f, 0f);
        private float _mpbRectCount = 1f;
        private Vector4 _mpbRect0 = new Vector4(0f, 0.024f, 0.445f, 0.425f);
        private Vector4 _mpbRect1 = Vector4.zero;
        private float _mpbExitHighlight = 0f;
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

        public Vector4 LockCenterValue => _mpbLockCenter;
        public float LockOpenValue => _mpbLockOpen;
        public float LockAmountValue => _mpbLockAmount;

        public void SetLockWiggle(float v) { _mpbLockWiggle = v; ApplyBoxPropertyBlock(); }
        public void SetLockOpen(float v) { _mpbLockOpen = v; ApplyBoxPropertyBlock(); }
        public void SetLockAmount(float v) { _mpbLockAmount = v; ApplyBoxPropertyBlock(); }
        public void SetLockGlyphScale(float s) { _mpbLockCenter.z = s; ApplyBoxPropertyBlock(); }

        /// <summary>
        /// Returns how many visual sockets this Carrier Box should display (clamped 1..16).
        /// </summary>
        public int GetVisibleSocketCount()
        {
            int raw = (initialCount > 0) ? initialCount : Mathf.Max(1, remainingCount);
            return Mathf.Clamp(raw, 1, MaxVisualSockets);
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

        public bool IsLockedByKey => remainingLockKeys > 0;
        public bool IsFrozenByIce => remainingIceCount > 0;
        public bool IsMovementOrCollectionBlocked => IsLockedByKey || IsFrozenByIce;

        public bool IsActivelyCollecting => (isCollectingInGrid || (assignedSlot != null && !_isMovingToSlot)) && !_isCompleted && remainingCount > 0 && !IsMovementOrCollectionBlocked && gameObject.activeInHierarchy;

        public bool IsMovingToSlot => _isMovingToSlot;

        public void SetMovingToSlot(bool moving)
        {
            _isMovingToSlot = moving;
            if (moving) SetExitClearHighlight(false);
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
        private bool _isCompleted = false;
        public bool IsCompleted => _isCompleted;
        private bool _isMovingToSlot = false;
        private int _subParticleCounter = 0;
        private int _nextIncomingSocketCounter = 0;
        private int _settledSocketCount = 0;
        private float _lastGulpTime = -10f;

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

            EnsureCollider();

            if (NeedsVisualRebuild())
            {
                RebuildMonsterVisuals();
            }
        }

        private void OnEnable()
        {
            EnsureCollider();

            if (NeedsVisualRebuild())
            {
                RebuildMonsterVisuals();
            }
            else
            {
                ApplySocketLayoutToMaterial();
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
                remainingLockKeys = Mathf.Max(0, lockKeyCount);
                remainingIceCount = Mathf.Max(0, iceCount);
                if (NeedsVisualRebuild())
                {
                    RebuildMonsterVisuals();
                }
                else
                {
                    ApplySocketLayoutToMaterial();
                    UpdateBadgeText();
                    RefreshSpecialMechanicVisuals();
                }
            };
        }
#endif

        private bool NeedsVisualRebuild()
        {
            int expectedSockets = GetVisibleSocketCount();
            if (_builtShape != blockShape) return true;
            if (_builtSocketCount != expectedSockets) return true;
            if (transform.childCount == 0 || bodyPivot == null) return true;
            if (transform.Find("BodyPivot/CarrierBoxVisual") == null) return true;
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
            bool effectiveClear = (exitClear || isCollectingInGrid) && !IsMovementOrCollectionBlocked;
            isExitClearInGrid = effectiveClear;
            isUnlockedInStaging = effectiveClear;
            float target = effectiveClear ? 1.0f : 0.0f;
            if (!Mathf.Approximately(_mpbExitHighlight, target))
            {
                _mpbExitHighlight = target;
                ApplyBoxPropertyBlock();
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
            return (slotFitScale.sqrMagnitude > 0.001f) ? slotFitScale : transform.localScale;
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
            transform.DOKill();
            if (bodyPivot != null) bodyPivot.DOKill();
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
            int iceCount)
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
            _keyConsumedVisually = false;
            _subParticleCounter = 0;
            _nextIncomingSocketCounter = 0;
            _settledSocketCount = 0;
            _isCompleted = false;
            monsterId = string.IsNullOrEmpty(id) ? $"Monster_{color}_{Guid.NewGuid().ToString().Substring(0, 4)}" : id;
            EnsureStagingHomeRecorded();
            EnsureCollider();
            RebuildMonsterVisuals();
            UpdateBadgeText();
        }

        [ContextMenu("Rebuild Monster Visuals")]
        public void RebuildMonsterVisuals()
        {
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

            EnsureMaterials();
            BuildCarrierBoxHierarchy();
            UpdateBadgeText();
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
            s_BoxPropertyBlock.SetFloat(CrateModePropId, useCrateVisuals ? 1.0f : 0.0f);
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

            _boxRenderer.SetPropertyBlock(s_BoxPropertyBlock);
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

            // 3. Gray Socket Landing Points & Settled 3D Balls (1..16)
            int expectedSockets = GetVisibleSocketCount();
            _builtSocketCount = expectedSockets;
            CarrierBlockShapeUtility.ComputeSocketLayoutForShape(blockShape, expectedSockets, out Vector2[] socketPositions, out float socketRadius, out _socketBallLocalDiameter);
            ApplyShapeMaterialParams(rect0, rect1, rectCount, quadW, quadH);
            ApplySocketLayoutToMaterial(expectedSockets, socketPositions, socketRadius);

            GameObject socketsRoot = new GameObject("SocketsRoot");
            socketsRoot.transform.SetParent(bodyPivot, false);
            socketsRoot.transform.localPosition = Vector3.zero;
            mouthTransform = socketsRoot.transform;

            socketTransforms = new Transform[expectedSockets];
            socketBalls = new GameObject[expectedSockets];
            Mesh sphereMesh = GetSharedSphereMesh();

            for (int i = 0; i < expectedSockets; i++)
            {
                Vector2 sockPt = socketPositions[i];
                GameObject sockObj = new GameObject($"Socket_{i}");
                sockObj.transform.SetParent(socketsRoot.transform, false);
                sockObj.transform.localPosition = new Vector3(sockPt.x, sockPt.y, -0.10f);
                socketTransforms[i] = sockObj.transform;

                GameObject ballObj = new GameObject($"SettledBall_{i}");
                ballObj.transform.SetParent(sockObj.transform, false);
                ballObj.transform.localPosition = Vector3.zero;
                ballObj.transform.localRotation = Quaternion.Euler(18f, -24f, 0f);
                ballObj.transform.localScale = Vector3.one * _socketBallLocalDiameter;

                var mf = ballObj.AddComponent<MeshFilter>();
                mf.sharedMesh = sphereMesh;

                var mr = ballObj.AddComponent<MeshRenderer>();
                mr.sharedMaterial = socketBallMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                BallObjectPool.ApplyBallPropertyBlock(mr, colorType, variantIndex: 1, wobblePhase: i * 0.65f);

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

            GameObject lidRoot = new GameObject("BoxTransparentLid");
            lidRoot.transform.SetParent(parent, false);
            lidRoot.transform.localPosition = new Vector3(0f, 0f, -0.26f);
            transparentLidTransform = lidRoot.transform;

            // Translucent crystal-tinted acrylic window covering the shape's white cavity (cached per color)
            if (!s_SharedLidWindowMats.TryGetValue(colorType, out Material windowMat) || windowMat == null)
            {
                Color tint = Color.Lerp(GetCarrierFrameColor(colorType), new Color(0.88f, 0.96f, 1.00f, 1f), 0.72f);
                tint.a = 0.36f;
                windowMat = new Material(spriteShader) { name = $"Mat_BoxTransparentLid_{colorType}", color = tint, enableInstancing = true };
                if (windowMat.HasProperty("_BaseColor")) windowMat.SetColor("_BaseColor", tint);
                s_SharedLidWindowMats[colorType] = windowMat;
            }

            CarrierBlockShapeUtility.GetRectParts(blockShape, out Vector4 r0, out Vector4 r1, out int rectCount, out _, out _);
            for (int i = 0; i < rectCount; i++)
            {
                Vector4 r = (i == 0) ? r0 : r1;
                float lw = Mathf.Max(0.20f, (r.z - CarrierBlockShapeUtility.WallInset * 0.9f) * 2f);
                float lh = Mathf.Max(0.20f, (r.w - CarrierBlockShapeUtility.WallInset * 0.9f) * 2f);
                GameObject pane = new GameObject($"LidPane_{i}");
                pane.transform.SetParent(lidRoot.transform, false);
                pane.transform.localPosition = new Vector3(r.x, r.y, 0f);
                pane.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshUtils.CreatePillBadge(lw, lh, 0.03f, Mathf.Min(0.14f, Mathf.Min(lw, lh) * 0.38f));
                var lidMr = pane.AddComponent<MeshRenderer>();
                lidMr.sharedMaterial = windowMat;
                lidMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lidMr.receiveShadows = false;
            }

            // Diagonal white specular reflection glare across the sealed lid (shared across all boxes)
            if (s_SharedLidGlareMat == null)
            {
                s_SharedLidGlareMat = new Material(spriteShader)
                {
                    name = "Mat_LidSpecularGlare_Shared",
                    color = new Color(1f, 1f, 1f, 0.54f),
                    enableInstancing = true
                };
                if (s_SharedLidGlareMat.HasProperty("_BaseColor"))
                    s_SharedLidGlareMat.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.54f));
            }

            GameObject glare1 = new GameObject("LidGlareMain");
            glare1.transform.SetParent(lidRoot.transform, false);
            glare1.transform.localPosition = new Vector3(r0.x - 0.06f, r0.y + 0.04f, -0.02f);
            glare1.transform.localRotation = Quaternion.Euler(0f, 0f, -32f);
            glare1.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshUtils.CreatePillBadge(0.46f, 0.065f, 0.02f, 0.030f);
            var g1Mr = glare1.AddComponent<MeshRenderer>();
            g1Mr.sharedMaterial = s_SharedLidGlareMat;
            g1Mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Top snap-latch clip ("TIK!" lock clasp, cached per color)
            if (!s_SharedLidLatchMats.TryGetValue(colorType, out Material latchMat) || latchMat == null)
            {
                Color latchCol = GetCarrierFrameColor(colorType);
                latchMat = new Material(spriteShader) { name = $"Mat_LidLatch_{colorType}", color = latchCol, enableInstancing = true };
                if (latchMat.HasProperty("_BaseColor")) latchMat.SetColor("_BaseColor", latchCol);
                s_SharedLidLatchMats[colorType] = latchMat;
            }

            GameObject latchObj = new GameObject("LidSnapLatch");
            latchObj.transform.SetParent(lidRoot.transform, false);
            latchObj.transform.localPosition = new Vector3(r0.x, r0.y + r0.w - 0.06f, -0.03f);
            latchObj.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshUtils.CreatePillBadge(0.22f, 0.06f, 0.03f, 0.025f);
            var latchMr = latchObj.AddComponent<MeshRenderer>();
            latchMr.sharedMaterial = latchMat;
            latchMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            lidRoot.SetActive(false);
        }

        private void BuildFakeDropShadow()
        {
            Shader shadowShader = Shader.Find("PuzzleGlass/FakeDropShadow");
            if (shadowShader == null) return;

            CarrierBlockShapeUtility.GetBounds(blockShape, out int w, out int h);
            Vector2Int[] cells = CarrierBlockShapeUtility.GetCells(blockShape);

            // Blocks inside the dark indigo StagingGridBoard need cool deep-indigo contact shadows
            // clipped cleanly per bottom-exposed cell so polyominoes never bleed shadows into empty cells.
            bool inGridBoard = StagingGridBoard.Instance != null && StagingGridBoard.Instance.showTrayVisuals;
            Material shadowMat;
            if (inGridBoard)
            {
                if (s_SharedShadowMatGrid == null)
                {
                    s_SharedShadowMatGrid = new Material(shadowShader) { name = "Mat_MonsterFakeShadow_Grid", enableInstancing = true };
                    s_SharedShadowMatGrid.SetColor("_ShadowColor", new Color(0.05f, 0.04f, 0.16f, 0.54f));
                    s_SharedShadowMatGrid.SetFloat("_FeatherWidth", 0.52f);
                    s_SharedShadowMatGrid.SetFloat("_IsRadial", 1.0f);
                }
                shadowMat = s_SharedShadowMatGrid;
            }
            else
            {
                if (s_SharedShadowMatTray == null)
                {
                    s_SharedShadowMatTray = new Material(shadowShader) { name = "Mat_MonsterFakeShadow_Tray", enableInstancing = true };
                    s_SharedShadowMatTray.SetColor("_ShadowColor", new Color(0.18f, 0.22f, 0.34f, 0.34f));
                    s_SharedShadowMatTray.SetFloat("_FeatherWidth", 0.52f);
                    s_SharedShadowMatTray.SetFloat("_IsRadial", 1.0f);
                }
                shadowMat = s_SharedShadowMatTray;
            }

            for (int i = 0; i < cells.Length; i++)
            {
                Vector2Int c = cells[i];
                bool hasCellBelow = false;
                for (int j = 0; j < cells.Length; j++)
                {
                    if (cells[j].x == c.x && cells[j].y == c.y - 1)
                    {
                        hasCellBelow = true;
                        break;
                    }
                }
                if (hasCellBelow) continue;

                Vector2 cellCenter = CarrierBlockShapeUtility.GetCellLocalCenter(blockShape, c);
                float cx = cellCenter.x;
                float cy = cellCenter.y - 0.45f;

                GameObject shadowQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                shadowQuad.name = $"MonsterFakeDropShadow_{c.x}_{c.y}";
                shadowQuad.transform.SetParent(transform, false);
                shadowQuad.transform.localPosition = new Vector3(cx, cy, 0.14f);
                shadowQuad.transform.localScale = new Vector3(0.82f, 0.20f, 1.0f);
                DestroyCollider(shadowQuad);

                var mr = shadowQuad.GetComponent<MeshRenderer>();
                mr.sharedMaterial = shadowMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
        }

        private void BuildHiddenBadge(Transform parent)
        {
            Vector2 center = GetMechanicAnchorLocal();
            GameObject badge = new GameObject("BellyBadge");
            badge.transform.SetParent(parent, false);
            badge.transform.localPosition = new Vector3(center.x, center.y + 0.01f, -0.26f);
            badgeTransform = badge.transform;

            GameObject textObj = new GameObject("CountText");
            textObj.transform.SetParent(badge.transform, false);
            textObj.transform.localPosition = new Vector3(0, 0, -0.02f);

            counterText = textObj.AddComponent<TextMeshPro>();
            counterText.alignment = TextAlignmentOptions.Center;
            counterText.fontSize = 4.6f;
            counterText.fontStyle = FontStyles.Bold;
            counterText.color = Color.white;
            counterText.text = remainingCount.ToString();
            counterText.rectTransform.sizeDelta = new Vector2(1.1f, 0.7f);
            counterText.sortingOrder = 10;
            ApplyTmpOutline(counterText, new Color(0.06f, 0.08f, 0.14f, 1f), 0.28f);

            badge.SetActive(useCrateVisuals && !IsLockedByKey && !IsFrozenByIce && remainingCount > 0);
        }

        public void UpdateBadgeText()
        {
            if (counterText != null)
            {
                counterText.text = Mathf.Max(0, remainingCount).ToString();
                counterText.color = useCrateVisuals ? Color.white : PuzzleColorPalette.GetTextColor(colorType);
                if (useCrateVisuals)
                {
                    counterText.fontSize = (remainingCount >= 100) ? 3.8f : 4.6f;
                }
            }
            if (badgeTransform != null)
            {
                bool showBadge = useCrateVisuals
                    ? (!IsFrozenByIce && !IsLockedByKey && remainingCount > 0)
                    : (!IsFrozenByIce && (initialCount > MaxVisualSockets) && (remainingCount > 0));
                badgeTransform.gameObject.SetActive(showBadge);
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
            CarrierBlockShapeUtility.ComputeSocketLayoutForShape(blockShape, count, out Vector2[] positions, out float radius, out float ballDiam);
            _socketBallLocalDiameter = ballDiam;
            ApplySocketLayoutToMaterial(count, positions, radius);
        }

        private void ApplySocketLayoutToMaterial(int count, Vector2[] positions, float radius)
        {
            _mpbSocketCount = count;
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

        public int ClaimNextSocketIndex()
        {
            int modCount = Mathf.Max(1, GetSocketModCount());
            int idx = _nextIncomingSocketCounter % modCount;
            _nextIncomingSocketCounter++;
            return idx;
        }

        /// <summary>
        /// Returns the world position of the specified gray socket (0, 1, or 2).
        /// </summary>
        public Vector3 GetSocketWorldPosition(int socketIndex)
        {
            if (useCrateVisuals)
            {
                Vector2 c = GetMechanicAnchorLocal();
                Transform t = bodyPivot != null ? bodyPivot : transform;
                return t.TransformPoint(new Vector3(c.x, c.y + 0.01f, -0.18f));
            }
            if (socketTransforms != null && socketTransforms.Length > 0)
            {
                int clamped = Mathf.Clamp(socketIndex, 0, socketTransforms.Length - 1);
                if (socketTransforms[clamped] != null)
                {
                    return socketTransforms[clamped].position;
                }
            }
            if (mouthTargetPoint != null) return mouthTargetPoint.position;
            return transform.position + new Vector3(0f, 0f, -0.10f);
        }

        /// <summary>
        /// Returns the exact world scale for a ball settled inside one of the gray sockets.
        /// </summary>
        public Vector3 GetSocketBallWorldScale()
        {
            CarrierBlockShapeUtility.ComputeSocketLayoutForShape(blockShape, GetVisibleSocketCount(), out _, out _, out _socketBallLocalDiameter);
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
                if (playSeatEffect && Application.isPlaying)
                {
                    float scaleFactor = (bodyPivot != null) ? bodyPivot.lossyScale.x : transform.lossyScale.x;
                    BlockClearCelebration.PlaySocketSeatRing(GetSocketWorldPosition(socketIndex), PuzzleColorPalette.GetPrimaryColor(colorType), Mathf.Max(0.10f, 0.26f * scaleFactor), 0.72f);
                }
                return;
            }
            if (socketBalls == null || socketBalls.Length == 0) return;
            int idx = Mathf.Clamp(socketIndex, 0, socketBalls.Length - 1);
            GameObject sBall = socketBalls[idx];
            if (sBall == null) return;

            CarrierBlockShapeUtility.ComputeSocketLayoutForShape(blockShape, GetVisibleSocketCount(), out _, out _, out _socketBallLocalDiameter);
            sBall.SetActive(true);
            Transform tr = sBall.transform;
            tr.DOKill();
            tr.localPosition = new Vector3(0f, 0f, -0.024f);
            Vector3 targetLocalScale = Vector3.one * _socketBallLocalDiameter;
            tr.localScale = new Vector3(targetLocalScale.x * 1.22f, targetLocalScale.y * 0.78f, targetLocalScale.z * 1.14f);

            Sequence sockSeq = DOTween.Sequence();
            sockSeq.Append(tr.DOScale(
                new Vector3(targetLocalScale.x * 0.93f, targetLocalScale.y * 1.08f, targetLocalScale.z * 0.96f),
                0.055f).SetEase(Ease.OutQuad));
            sockSeq.Append(tr.DOScale(targetLocalScale, 0.145f).SetEase(Ease.OutBounce));
            sockSeq.Join(tr.DOLocalMoveZ(0f, 0.14f).SetEase(Ease.OutBounce));
            sockSeq.SetLink(sBall);

            if (playSeatEffect && Application.isPlaying)
            {
                float scaleFactor = (bodyPivot != null) ? bodyPivot.lossyScale.x : transform.lossyScale.x;
                float worldRadius = Mathf.Max(0.08f, _socketBallLocalDiameter * 0.65f * scaleFactor);
                BlockClearCelebration.PlaySocketSeatRing(tr.position, PuzzleColorPalette.GetPrimaryColor(colorType), worldRadius);
            }
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
            ActivateAndPulseSocketBall(socketIndex);
            PlaySmoothGulpWave();
            DecrementCounterAndCheckComplete();
        }

        private void PlaySmoothGulpWave()
        {
            if (bodyPivot != null)
            {
                bodyPivot.DOKill(false);
                Sequence gulpSeq = DOTween.Sequence();
                gulpSeq.Append(bodyPivot.DOScale(
                    new Vector3(_originalBodyScale.x * 1.05f, _originalBodyScale.y * 0.94f, _originalBodyScale.z),
                    0.055f).SetEase(Ease.OutSine));
                gulpSeq.Join(bodyPivot.DOLocalMoveY(-0.018f, 0.055f).SetEase(Ease.OutSine));
                gulpSeq.Append(bodyPivot.DOScale(_originalBodyScale, 0.14f).SetEase(Ease.OutBack));
                gulpSeq.Join(bodyPivot.DOLocalMoveY(0f, 0.14f).SetEase(Ease.OutBounce));
            }
        }

        private void DecrementCounterAndCheckComplete()
        {
            remainingCount = Mathf.Max(0, remainingCount - 1);
            UpdateBadgeText();

            OnEatenCube?.Invoke(this);
            if (PuzzleGlassGameManager.Instance != null)
            {
                PuzzleGlassGameManager.Instance.OnCarrierBoxBallConsumed(this);
            }

            if (remainingCount <= 0)
            {
                _isCompleted = true;
                StagingGridBoard.Instance?.MarkGridDirty();
                if (!useCrateVisuals)
                {
                    // Ensure all sockets visibly hold settled balls on completion
                    int sockCount = (socketBalls != null) ? socketBalls.Length : 0;
                    for (int i = 0; i < sockCount; i++)
                    {
                        if (socketBalls[i] != null && !socketBalls[i].activeSelf)
                        {
                            ActivateAndPulseSocketBall(i, playSeatEffect: false);
                        }
                    }
                }
                StartCoroutine(CelebrateAndCompleteRoutine());
            }
        }

        private IEnumerator PlaySocketCompletionWaveRoutine()
        {
            int sockCount = (socketBalls != null) ? socketBalls.Length : 0;
            if (sockCount <= 0) yield break;

            CarrierBlockShapeUtility.ComputeSocketLayoutForShape(blockShape, GetVisibleSocketCount(), out _, out _, out _socketBallLocalDiameter);
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

            // 1. Brief beat for final ball seat + crisp socket shimmer wave
            yield return new WaitForSeconds(0.05f);
            yield return PlaySocketCompletionWaveRoutine();

            // 2. Short anticipation swell in place ("kabarma"), then elastic implosion inward (~0.18s)
            Vector3 startScale = transform.localScale;
            if (bodyPivot != null)
            {
                bodyPivot.DOKill(false);
                yield return bodyPivot.DOScale(_originalBodyScale * 1.07f, 0.065f).SetEase(Ease.OutQuad).WaitForCompletion();
            }
            else
            {
                yield return transform.DOScale(startScale * 1.07f, 0.065f).SetEase(Ease.OutQuad).WaitForCompletion();
            }

            PuzzleSoundManager.Instance?.PlayBoxComplete();

            // Notify listeners & game manager right as the block pops
            if (PuzzleGlassGameManager.Instance != null)
            {
                PuzzleGlassGameManager.Instance.OnCarrierBoxCompleted(this);
            }
            if (assignedSlot != null)
            {
                assignedSlot.DetachMonster();
                assignedSlot = null;
            }
            OnTargetReached?.Invoke(this);

            Vector3 popCenter = (bodyPivot != null) ? bodyPivot.position : transform.position;
            BlockClearCelebration.PlayImplosionPop(popCenter, PuzzleColorPalette.GetPrimaryColor(colorType), 0.92f);

            // Elastic inward implosion directly at current position (never drifts up over the glass bowl)
            transform.DOKill(false);
            Sequence implodeSeq = DOTween.Sequence();
            if (bodyPivot != null)
            {
                implodeSeq.Join(bodyPivot.DOScale(_originalBodyScale * 0.96f, 0.04f).SetEase(Ease.OutQuad));
            }
            implodeSeq.Join(transform.DOScale(Vector3.zero, 0.18f).SetEase(Ease.InBack, 1.15f));
            yield return implodeSeq.WaitForCompletion();

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
                // Anahtar rozeti bloğun sağ üst köşesine asılı durur (top deliklerini kapatmaz)
                Vector2 corner = GetTopRightCornerLocal();
                _keyOverlayRoot.transform.localPosition = new Vector3(corner.x - 0.10f, corner.y - 0.10f, -0.30f);
                _keyOverlayRoot.transform.localScale = Vector3.one * 0.82f;
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
                LockKeyVisuals.PlayUnlock(this, GetLockWorldPosition(), GetCarrierFrameColor(colorType), () =>
                {
                    _unlockAnimPlaying = false;
                    if (_lockOverlayRoot != null) _lockOverlayRoot.SetActive(false);
                    RefreshSpecialMechanicVisuals();
                    StagingGridBoard.Instance?.MarkGridDirty();
                });
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
                LockKeyVisuals.PlayLockWiggle(this);

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

        private GameObject BuildKeyOverlay(Transform parent)
        {
            return LockKeyVisuals.CreateKeyBadge(parent, GetCarrierFrameColor(colorType), new Color(0.97f, 0.97f, 0.98f, 1f),
                GetCarrierDarkColor(colorType), "KeyOverlay");
        }

        /// <summary>Kilidin kendisi shader'da çizilir; burada sadece birden fazla anahtar gerekiyorsa sayı rozeti var.</summary>
        private GameObject BuildLockOverlay(Transform parent, out TextMeshPro countLabel)
        {
            GameObject root = new GameObject("LockOverlay");
            root.transform.SetParent(parent, false);

            GameObject textObj = new GameObject("LockCountText");
            textObj.transform.SetParent(root.transform, false);
            textObj.transform.localPosition = new Vector3(0.17f, -0.17f, -0.05f);

            countLabel = textObj.AddComponent<TextMeshPro>();
            countLabel.alignment = TextAlignmentOptions.Center;
            countLabel.fontSize = 2.8f;
            countLabel.fontStyle = FontStyles.Bold;
            countLabel.color = Color.white;
            countLabel.text = remainingLockKeys.ToString();
            countLabel.rectTransform.sizeDelta = new Vector2(0.5f, 0.5f);
            countLabel.sortingOrder = 12;
            ApplyTmpOutline(countLabel, GetCarrierDarkColor(colorType), 0.30f);
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

            GameObject textObj = new GameObject("IceCountText");
            textObj.transform.SetParent(root.transform, false);
            textObj.transform.localPosition = new Vector3(0f, -0.02f, -0.04f);

            countLabel = textObj.AddComponent<TextMeshPro>();
            countLabel.alignment = TextAlignmentOptions.Center;
            countLabel.fontSize = 3.8f;
            countLabel.fontStyle = FontStyles.Bold;
            countLabel.color = Color.white;
            countLabel.text = remainingIceCount.ToString();
            countLabel.rectTransform.sizeDelta = new Vector2(1.2f, 0.8f);
            countLabel.sortingOrder = 14;
            ApplyTmpOutline(countLabel, new Color(0.07f, 0.31f, 0.52f, 1f), 0.30f);

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
