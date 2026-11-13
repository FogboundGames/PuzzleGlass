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

        private float _socketBallLocalDiameter = 0.188f;

        private static readonly int LocalSizePropId = Shader.PropertyToID("_LocalSize");
        private static readonly int RectCountPropId = Shader.PropertyToID("_RectCount");
        private static readonly int Rect0PropId = Shader.PropertyToID("_Rect0");
        private static readonly int Rect1PropId = Shader.PropertyToID("_Rect1");
        private static readonly int WallInsetPropId = Shader.PropertyToID("_WallInset");
        private static readonly int CornerRadPropId = Shader.PropertyToID("_CornerRad");
        private static readonly int InnerRadPropId = Shader.PropertyToID("_InnerRad");
        private static readonly int OutlineThicknessPropId = Shader.PropertyToID("_OutlineThickness");
        private static readonly int LipOffsetPropId = Shader.PropertyToID("_LipOffset");
        private static readonly int ExitHighlightPropId = Shader.PropertyToID("_ExitHighlight");
        private static readonly int SocketCountPropId = Shader.PropertyToID("_SocketCount");
        private static readonly int SocketRadiusPropId = Shader.PropertyToID("_SocketRadius");
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

        public bool IsActivelyCollecting => (isCollectingInGrid || assignedSlot != null) && !_isCompleted && remainingCount > 0 && gameObject.activeInHierarchy;

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
                if (NeedsVisualRebuild())
                {
                    RebuildMonsterVisuals();
                }
                else
                {
                    ApplySocketLayoutToMaterial();
                    UpdateBadgeText();
                }
            };
        }
#endif

        private bool NeedsVisualRebuild()
        {
            int expectedSockets = GetVisibleSocketCount();
            if (_builtShape != blockShape) return true;
            if (transform.childCount == 0 || bodyPivot == null) return true;
            if (transform.Find("BodyPivot/CarrierBoxVisual") == null) return true;
            if (socketTransforms == null || socketTransforms.Length != expectedSockets || socketTransforms[0] == null) return true;
            if (socketBalls == null || socketBalls.Length != expectedSockets || socketBalls[0] == null) return true;
            if (bodyMaterial == null) return true;
            if (bodyMaterial.HasProperty(SocketCountPropId) && Mathf.RoundToInt(bodyMaterial.GetFloat(SocketCountPropId)) != expectedSockets) return true;
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
            isExitClearInGrid = exitClear;
            isUnlockedInStaging = exitClear;
            if (bodyMaterial != null && bodyMaterial.HasProperty(ExitHighlightPropId))
            {
                bodyMaterial.SetFloat(ExitHighlightPropId, exitClear ? 1.0f : 0.0f);
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
            colorType = color;
            blockShape = shape;
            initialCount = Mathf.Clamp(count, 1, MaxVisualSockets);
            remainingCount = initialCount;
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

            _nextIncomingSocketCounter = 0;
            _settledSocketCount = 0;
            _builtShape = blockShape;

            EnsureMaterials();
            BuildCarrierBoxHierarchy();
            UpdateBadgeText();
        }

        private void EnsureMaterials()
        {
            Shader boxShader = Shader.Find("PuzzleGlass/CarrierBox")
                ?? Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Sprites/Default");

            Color baseCol = GetCarrierFrameColor(colorType);
            Color darkCol = GetCarrierDarkColor(colorType);
            Color outlineCol = GetCarrierOutlineColor(colorType);
            Color highlightCol = Color.Lerp(baseCol, Color.white, 0.28f);

            bodyMaterial = new Material(boxShader) { name = $"Mat_CarrierBox_{colorType}" };
            bodyMaterial.color = Color.white;
            if (bodyMaterial.HasProperty("_BodyColor")) bodyMaterial.SetColor("_BodyColor", baseCol);
            if (bodyMaterial.HasProperty("_BodyDarkColor")) bodyMaterial.SetColor("_BodyDarkColor", darkCol);
            if (bodyMaterial.HasProperty("_OutlineColor")) bodyMaterial.SetColor("_OutlineColor", outlineCol);
            if (bodyMaterial.HasProperty("_HighlightColor")) bodyMaterial.SetColor("_HighlightColor", highlightCol);
            if (bodyMaterial.HasProperty("_PlateColor")) bodyMaterial.SetColor("_PlateColor", new Color(0.97f, 0.97f, 0.98f, 1.0f));
            if (bodyMaterial.HasProperty("_PlateShadowColor")) bodyMaterial.SetColor("_PlateShadowColor", new Color(0.84f, 0.86f, 0.90f, 1.0f));
            if (bodyMaterial.HasProperty("_SocketColor")) bodyMaterial.SetColor("_SocketColor", new Color(0.60f, 0.61f, 0.65f, 1.0f));
            if (bodyMaterial.HasProperty("_SocketShadowColor")) bodyMaterial.SetColor("_SocketShadowColor", new Color(0.46f, 0.47f, 0.52f, 1.0f));
            ApplySocketLayoutToMaterial();

            socketBallMaterial = PuzzleColorPalette.CreateCandyCubeMaterial(colorType, $"Mat_SocketBall_{colorType}");

            Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            badgeMaterial = new Material(litShader) { name = "Mat_BellyBadge" };
            badgeMaterial.color = Color.white;
            if (badgeMaterial.HasProperty("_BaseColor")) badgeMaterial.SetColor("_BaseColor", Color.white);
        }

        private static Color GetCarrierFrameColor(PuzzleColor color)
        {
            switch (color)
            {
                case PuzzleColor.Blue:   return new Color(0.06f, 0.56f, 0.94f, 1f); // Reference image vibrant sky-azure blue
                case PuzzleColor.Yellow: return new Color(0.98f, 0.78f, 0.12f, 1f); // Sunny gold yellow
                case PuzzleColor.Red:    return new Color(0.96f, 0.31f, 0.28f, 1f); // Vibrant coral-crimson red
                case PuzzleColor.Green:  return new Color(0.28f, 0.80f, 0.26f, 1f); // Fresh apple green
                case PuzzleColor.Purple: return new Color(0.58f, 0.36f, 0.94f, 1f); // Rich violet
                case PuzzleColor.Orange: return new Color(0.98f, 0.54f, 0.14f, 1f); // Bright tangerine
                case PuzzleColor.Pink:   return new Color(0.96f, 0.34f, 0.66f, 1f); // Bubblegum pink
                case PuzzleColor.Cyan:   return new Color(0.14f, 0.82f, 0.82f, 1f); // Turquoise cyan
                default: return PuzzleColorPalette.GetBodyColor(color);
            }
        }

        private static Color GetCarrierDarkColor(PuzzleColor color)
        {
            Color c = GetCarrierFrameColor(color);
            return new Color(c.r * 0.74f, c.g * 0.76f, c.b * 0.82f, 1f);
        }

        private static Color GetCarrierOutlineColor(PuzzleColor color)
        {
            Color c = GetCarrierFrameColor(color);
            return new Color(c.r * 0.50f, c.g * 0.54f, c.b * 0.62f, 1f);
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

            // 3. Gray Socket Landing Points & Settled 3D Balls (1..16)
            int expectedSockets = GetVisibleSocketCount();
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

            // Translucent crystal-tinted acrylic window covering the shape's white cavity
            Color tint = Color.Lerp(GetCarrierFrameColor(colorType), new Color(0.88f, 0.96f, 1.00f, 1f), 0.72f);
            tint.a = 0.36f;
            Material windowMat = new Material(spriteShader) { name = "Mat_BoxTransparentLid", color = tint };
            if (windowMat.HasProperty("_BaseColor")) windowMat.SetColor("_BaseColor", tint);

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

            // Diagonal white specular reflection glare across the sealed lid
            Material glareMat = new Material(spriteShader)
            {
                name = "Mat_LidSpecularGlare",
                color = new Color(1f, 1f, 1f, 0.54f)
            };
            if (glareMat.HasProperty("_BaseColor")) glareMat.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.54f));

            GameObject glare1 = new GameObject("LidGlareMain");
            glare1.transform.SetParent(lidRoot.transform, false);
            glare1.transform.localPosition = new Vector3(r0.x - 0.06f, r0.y + 0.04f, -0.02f);
            glare1.transform.localRotation = Quaternion.Euler(0f, 0f, -32f);
            glare1.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshUtils.CreatePillBadge(0.46f, 0.065f, 0.02f, 0.030f);
            var g1Mr = glare1.AddComponent<MeshRenderer>();
            g1Mr.sharedMaterial = glareMat;
            g1Mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Top snap-latch clip ("TIK!" lock clasp)
            Color latchCol = GetCarrierFrameColor(colorType);
            Material latchMat = new Material(spriteShader) { name = "Mat_LidLatch", color = latchCol };
            if (latchMat.HasProperty("_BaseColor")) latchMat.SetColor("_BaseColor", latchCol);

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
            bool inGridBoard = StagingGridBoard.Instance != null || FindFirstObjectByType<StagingGridBoard>() != null;
            Color shadowColor = inGridBoard
                ? new Color(0.05f, 0.04f, 0.16f, 0.54f)
                : new Color(0.75f, 0.59f, 0.44f, 0.36f);

            Material shadowMat = new Material(shadowShader) { name = "Mat_MonsterFakeShadow" };
            shadowMat.SetColor("_ShadowColor", shadowColor);
            shadowMat.SetFloat("_FeatherWidth", 0.52f);
            shadowMat.SetFloat("_IsRadial", 1.0f);

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
            GameObject badge = new GameObject("BellyBadge");
            badge.transform.SetParent(parent, false);
            badge.transform.localPosition = new Vector3(0, -0.34f, -0.36f);
            badgeTransform = badge.transform;

            GameObject textObj = new GameObject("CountText");
            textObj.transform.SetParent(badge.transform, false);
            textObj.transform.localPosition = new Vector3(0, 0, -0.045f);

            counterText = textObj.AddComponent<TextMeshPro>();
            counterText.alignment = TextAlignmentOptions.Center;
            counterText.fontSize = 2.6f;
            counterText.fontStyle = FontStyles.Bold;
            counterText.color = PuzzleColorPalette.GetTextColor(colorType);
            counterText.text = remainingCount.ToString();
            counterText.rectTransform.sizeDelta = new Vector2(1.2f, 0.6f);

            // Hidden so the carrier box cleanly shows the 3 gray sockets just like the reference image
            badge.SetActive(false);
        }

        public void UpdateBadgeText()
        {
            if (counterText != null)
            {
                counterText.text = Mathf.Max(0, remainingCount).ToString();
                counterText.color = PuzzleColorPalette.GetTextColor(colorType);
            }
            if (badgeTransform != null)
            {
                // Up to MaxVisualSockets (16), the gray sockets inside the Carrier Box display exact capacity.
                bool showBadge = (initialCount > MaxVisualSockets) && (remainingCount > 0);
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
            if (bodyMaterial == null) return;
            if (bodyMaterial.HasProperty(LocalSizePropId)) bodyMaterial.SetVector(LocalSizePropId, new Vector4(quadW, quadH, 0f, 0f));
            if (bodyMaterial.HasProperty(RectCountPropId)) bodyMaterial.SetFloat(RectCountPropId, rectCount);
            if (bodyMaterial.HasProperty(Rect0PropId)) bodyMaterial.SetVector(Rect0PropId, rect0);
            if (bodyMaterial.HasProperty(Rect1PropId)) bodyMaterial.SetVector(Rect1PropId, rect1);
            if (bodyMaterial.HasProperty(WallInsetPropId)) bodyMaterial.SetFloat(WallInsetPropId, CarrierBlockShapeUtility.WallInset);
            if (bodyMaterial.HasProperty(CornerRadPropId)) bodyMaterial.SetFloat(CornerRadPropId, CarrierBlockShapeUtility.OuterCornerRadius);
            if (bodyMaterial.HasProperty(InnerRadPropId)) bodyMaterial.SetFloat(InnerRadPropId, CarrierBlockShapeUtility.InnerCornerRadius);
            if (bodyMaterial.HasProperty(OutlineThicknessPropId)) bodyMaterial.SetFloat(OutlineThicknessPropId, CarrierBlockShapeUtility.OutlineThickness);
            if (bodyMaterial.HasProperty(LipOffsetPropId)) bodyMaterial.SetFloat(LipOffsetPropId, CarrierBlockShapeUtility.LipDepthOffset);
            if (bodyMaterial.HasProperty(ExitHighlightPropId)) bodyMaterial.SetFloat(ExitHighlightPropId, isExitClearInGrid ? 1f : 0f);
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
            if (bodyMaterial == null) return;
            if (bodyMaterial.HasProperty(SocketCountPropId)) bodyMaterial.SetFloat(SocketCountPropId, count);
            if (bodyMaterial.HasProperty(SocketRadiusPropId)) bodyMaterial.SetFloat(SocketRadiusPropId, radius);
            for (int p = 0; p < SockPairPropIds.Length; p++)
            {
                if (!bodyMaterial.HasProperty(SockPairPropIds[p])) continue;
                int pIdx = p * 2;
                Vector2 a = (positions != null && pIdx < positions.Length) ? positions[pIdx] : Vector2.zero;
                Vector2 b = (positions != null && pIdx + 1 < positions.Length) ? positions[pIdx + 1] : Vector2.zero;
                bodyMaterial.SetVector(SockPairPropIds[p], new Vector4(a.x, a.y, b.x, b.y));
            }
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

        private void ActivateAndPulseSocketBall(int socketIndex)
        {
            if (socketBalls == null || socketBalls.Length == 0) return;
            int idx = Mathf.Clamp(socketIndex, 0, socketBalls.Length - 1);
            GameObject sBall = socketBalls[idx];
            if (sBall == null) return;

            CarrierBlockShapeUtility.ComputeSocketLayoutForShape(blockShape, GetVisibleSocketCount(), out _, out _, out _socketBallLocalDiameter);
            sBall.SetActive(true);
            Transform tr = sBall.transform;
            tr.DOKill();
            Vector3 targetLocalScale = Vector3.one * _socketBallLocalDiameter;
            tr.localScale = targetLocalScale * 1.18f;
            tr.DOScale(targetLocalScale, 0.14f).SetEase(Ease.OutBack);
        }

        // ==================== ANIMATIONS ====================

        public void StartIdleAndBlink()
        {
            StopIdleAndBlink();

            if (bodyPivot != null)
            {
                bodyPivot.localScale = _originalBodyScale;
                _idleTween = bodyPivot.DOScale(
                    new Vector3(_originalBodyScale.x * 0.98f, _originalBodyScale.y * 1.02f, _originalBodyScale.z),
                    0.85f)
                    .SetEase(Ease.InOutSine)
                    .SetLoops(-1, LoopType.Yoyo);
            }
        }

        public void StopIdleAndBlink()
        {
            if (_idleTween != null && _idleTween.IsActive()) _idleTween.Kill();
            if (_blinkSeq != null && _blinkSeq.IsActive()) _blinkSeq.Kill();
            if (bodyPivot != null) bodyPivot.localScale = _originalBodyScale;
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
                    new Vector3(_originalBodyScale.x * 1.05f, _originalBodyScale.y * 0.95f, _originalBodyScale.z),
                    0.06f).SetEase(Ease.OutSine));
                gulpSeq.Append(bodyPivot.DOScale(_originalBodyScale, 0.12f).SetEase(Ease.OutBack));
            }
        }

        private void DecrementCounterAndCheckComplete()
        {
            remainingCount = Mathf.Max(0, remainingCount - 1);
            UpdateBadgeText();

            OnEatenCube?.Invoke(this);

            if (remainingCount <= 0)
            {
                _isCompleted = true;
                // Ensure all sockets visibly hold settled balls on completion
                int sockCount = (socketBalls != null) ? socketBalls.Length : 0;
                for (int i = 0; i < sockCount; i++)
                {
                    if (socketBalls[i] != null && !socketBalls[i].activeSelf)
                    {
                        ActivateAndPulseSocketBall(i);
                    }
                }
                StartCoroutine(CelebrateAndCompleteRoutine());
            }
        }

        private IEnumerator CelebrateAndCompleteRoutine()
        {
            StopIdleAndBlink();

            // 1. Brief pause so the 3rd ball settles into its gray socket
            yield return new WaitForSeconds(0.14f);

            // 2. Transparent Lid Snap-Shut ("TIK!" Gachapon / Bento Package Seal)
            PuzzleSoundManager.Instance?.PlayBoxComplete();
            if (transparentLidTransform == null && bodyPivot != null)
            {
                transparentLidTransform = bodyPivot.Find("BoxTransparentLid");
            }

            if (transparentLidTransform != null)
            {
                transparentLidTransform.gameObject.SetActive(true);
                transparentLidTransform.DOKill();
                transparentLidTransform.localPosition = new Vector3(0f, 0.28f, -0.26f);
                transparentLidTransform.localScale = new Vector3(0.94f, 0.14f, 1f);

                Sequence lidSnapSeq = DOTween.Sequence();
                lidSnapSeq.Join(transparentLidTransform.DOLocalMove(new Vector3(0f, 0f, -0.26f), 0.14f).SetEase(Ease.OutBack));
                lidSnapSeq.Join(transparentLidTransform.DOScale(Vector3.one, 0.14f).SetEase(Ease.OutBack));
                if (bodyPivot != null)
                {
                    // Mechanical "TIK!" lock squash-punch when the lid latches shut
                    lidSnapSeq.Insert(0.11f, bodyPivot.DOScale(
                        new Vector3(_originalBodyScale.x * 1.09f, _originalBodyScale.y * 0.91f, _originalBodyScale.z),
                        0.05f).SetEase(Ease.OutQuad));
                    lidSnapSeq.Insert(0.16f, bodyPivot.DOScale(_originalBodyScale, 0.10f).SetEase(Ease.OutBack));
                }
                yield return lidSnapSeq.WaitForCompletion();
            }

            // 3. Notify Game Manager (increments Combo Streak / triggers 2x Vacuum Fever Mode & unlocks matching Color Gates!)
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

            // Brief pause so the player admires the sealed transparent package before it lifts away by its handle
            yield return new WaitForSeconds(0.16f);

            Vector3 origPos = transform.position;
            float sideDir = (origPos.x >= 0f) ? 0.24f : -0.24f;

            // 4. Lifted up by its top handle and carried away!
            Sequence exitSeq = DOTween.Sequence();
            if (bodyPivot != null)
            {
                exitSeq.Join(bodyPivot.DOLocalRotate(new Vector3(0f, 0f, -sideDir * 35f), 0.26f).SetEase(Ease.InOutSine));
            }
            exitSeq.Join(transform.DOMove(origPos + new Vector3(sideDir, 0.65f, 0f), 0.26f).SetEase(Ease.InBack));
            exitSeq.Join(transform.DOScale(Vector3.zero, 0.26f).SetEase(Ease.InBack));
            yield return exitSeq.WaitForCompletion();

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
    }
}
