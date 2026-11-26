using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace PuzzleGlass
{
    /// <summary>
    /// Öğretici katman (Violet Theme UI assetleri + pastel lavanta teması):
    ///  1. Level 1 spotlight tutorial: yalnızca hedef (en baştaki mor) kutu aydınlık/tıklanabilir kalır, diğer tüm alanlar
    ///     karartılır ve kilitlenir; kutu slota yerleşince karartma akıcı biçimde kalkar ve oyun devam eder.
    ///  2. El ipucu: ilk levellerde oyuncu ilerleyemezse camdaki toplara uyan bir bloğu parmakla işaret eder.
    ///  3. Mekanik tanıtımı: buz, kilit & anahtar ve içi dolu kutu bir levelde İLK kez görüldüğünde
    ///     kısa bir tanıtım kartı çıkar.
    /// Kendi canvas'ı vardır (HUD'un üstünde, level geçişinin altında).
    /// </summary>
    public class TutorialOverlay : MonoBehaviour
    {
        public static TutorialOverlay Instance { get; private set; }

        public const string PrefIce = "PG_Intro_Ice";
        public const string PrefLock = "PG_Intro_Lock";
        public const string PrefFilledBox = "PG_Intro_FilledBox";
        public const string PrefSlotWarning = "PG_Intro_SlotWarning";
        private const float CycleSeconds = 2.2f;
        private const float TapCycleSeconds = 1.25f;
        /// <summary>Kart açıldıktan sonra bu süre dolmadan dokunuş kartı kapatmaz (yanlışlıkla geçmeyi önler).</summary>
        private const float CardMinSeconds = 0.6f;
        private const float IdleHintSeconds = 6f;

        private enum IntroMechanic
        {
            Ice,
            Lock,
            FilledBox
        }

        private PuzzleGlassUITheme _theme;
        private Canvas _canvas;
        private RectTransform _root;
        private RectTransform _finger, _ring;
        private Image _fingerImg, _ringImg;
        private CanvasGroup _handGroup;
        private Sprite _circle;
        private Sprite _ringSprite;
        private Font _font;

        private MonsterCharacter _hintBlock;
        private Vector2Int _hintFrom, _hintTarget;
        private bool _hintActive;
        private bool _hintToSlot;
        private float _hintTime;

        private GameObject _spotlightRoot;
        private Image _spotlightDimImg;
        private Material _spotlightMat;
        private RectTransform _blockTop, _blockBottom, _blockLeft, _blockRight;
        private Image _blockTopImg, _blockBottomImg, _blockLeftImg, _blockRightImg;
        private MonsterCharacter _spotlightTarget;
        private bool _spotlightActive;
        private bool _spotlightShown;
        private bool _pauseGlassBallsForTutorial;
        private float _spotlightAlpha;
        private Tween _spotlightTween;

        private struct IntroCard
        {
            public IntroMechanic kind;
            public string title, body, prefKey;
            public MonsterCharacter target;
        }

        private readonly Queue<IntroCard> _cards = new Queue<IntroCard>();
        private GameObject _cardObj;
        private float _cardTime;
        private IntroCard _currentCard;

        private PuzzleGlassLevelData _level;
        private List<MonsterCharacter> _blocks;
        private float _idleTime;
        private int _lastProgress = -1;

        private static readonly Color ColIndigoDeep = new Color(0.240f, 0.255f, 0.580f, 1.00f);
        private static readonly Color ColCardBody   = new Color(0.270f, 0.285f, 0.540f, 1.00f);
        private static readonly Color ColVioletFallback = new Color(0.420f, 0.340f, 0.880f, 0.96f);
        private static readonly Color ColSpotlightDim = new Color(0.06f, 0.05f, 0.16f, 0.72f);

        private static readonly int ShaderCanvasSizeId = Shader.PropertyToID("_CanvasSize");
        private static readonly int ShaderHoleRectId = Shader.PropertyToID("_HoleRect");
        private static readonly int ShaderHoleParamsId = Shader.PropertyToID("_HoleParams");

        public bool IsSpotlightTutorialActive => _spotlightActive;
        public bool AreGlassBallsPausedForTutorial => _pauseGlassBallsForTutorial;

        public bool CanInteractWithBlock(MonsterCharacter block)
        {
            if (!_spotlightActive) return true;
            if (LevelTransitionController.IsTransitioning) return false;
            return block != null && block == _spotlightTarget && !block.IsMovingToSlot && block.assignedSlot == null;
        }

        /// <summary>
        /// Level yüklenmeye başlarken çağrılır: Level 1'de toplar fizikle doğal yerleşir ama
        /// spotlight kutusu slota konana kadar bant dönmez ve toplama başlamaz.
        /// </summary>
        public void OnLevelLoading(PuzzleGlassLevelData lvl)
        {
            StopHint();
            ClearCards();
            ClearSpotlight();
            _level = lvl;
            _blocks = null;
            _pauseGlassBallsForTutorial = lvl != null && lvl.tutorial == LevelTutorial.FirstLevel;
            if (!_pauseGlassBallsForTutorial)
            {
                ReleaseGlassBalls();
            }
        }

        public void NotifySpotlightBlockPlaced(MonsterCharacter block)
        {
            if (!_spotlightActive) return;
            if (block != null && block == _spotlightTarget)
            {
                EndSpotlightTutorial();
            }
        }

        public static TutorialOverlay GetOrCreate()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("TutorialOverlay");
            return go.AddComponent<TutorialOverlay>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            BuildCanvas();
        }

        private void OnDestroy()
        {
            _pauseGlassBallsForTutorial = false;
            _spotlightTween?.Kill();
            if (_spotlightMat != null) Destroy(_spotlightMat);
            if (Instance == this) Instance = null;
        }

        /// <summary>Level yüklendikten (bloklar oluştuktan) sonra çağrılır.</summary>
        public void Begin(PuzzleGlassLevelData lvl, List<MonsterCharacter> blocks)
        {
            StopHint();
            ClearCards();
            ClearSpotlight();
            _level = lvl;
            _blocks = blocks;
            _idleTime = 0f;
            _lastProgress = -1;
            // Öğretici level numarasına değil level tasarımındaki seçime bağlı (PuzzleGlassLevelData.tutorial).
            // Eskiden burada "level 1 değilse çık" vardı; bu yüzden buz/kilit/dolu kutu kartları hiç görünmüyordu.
            if (lvl == null)
            {
                _pauseGlassBallsForTutorial = false;
                ReleaseGlassBalls();
                return;
            }

            // 1) İlk level: spotlight tanıtımı veya el ipucu
            if (lvl.tutorial == LevelTutorial.FirstLevel && blocks != null && SlotSystem.IsActive)
            {
                // Tasarımcı ipucu kutusunu seçtiyse o (gönderilebilirse), yoksa eski kural: mor kutu, sonra otomatik
                var pick = (lvl.tutorialBlockIndex >= 0 ? PickDesignerHintBlock(lvl, blocks) : null)
                           ?? PickLevel1PurpleSpotlightBlock(blocks) ?? PickSlotHintBlock(lvl, blocks);
                if (pick != null)
                {
                    _spotlightTarget = pick;
                    _spotlightActive = true;
                    _spotlightShown = false;
                    _spotlightAlpha = 0f;
                    _pauseGlassBallsForTutorial = true;
                    StartSlotHint(pick);
                    _hintTime = -0.25f;
                }
                else
                {
                    _pauseGlassBallsForTutorial = false;
                    ReleaseGlassBalls();
                }
            }
            else
            {
                _pauseGlassBallsForTutorial = false;
                ReleaseGlassBalls();
                if (lvl.tutorialHint && blocks != null && SlotSystem.IsActive)
                {
                    _hintBlock = PickSlotHintBlock(lvl, blocks);
                    if (_hintBlock != null)
                    {
                        StartSlotHint(_hintBlock);
                        _hintTime = -0.8f;
                    }
                }
                else if (lvl.tutorialHint && blocks != null && lvl.tutorialBlockIndex >= 0 && lvl.monsters != null && lvl.tutorialBlockIndex < lvl.monsters.Count)
                {
                    var entry = lvl.monsters[lvl.tutorialBlockIndex];
                    foreach (var b in blocks)
                    {
                        if (b != null && b.gridAnchor == lvl.tutorialFromAnchor && b.colorType == entry.color && b.blockShape == entry.blockShape)
                        {
                            _hintBlock = b;
                            break;
                        }
                    }
                    if (_hintBlock != null && lvl.tutorialTargetAnchor != lvl.tutorialFromAnchor)
                    {
                        _hintFrom = lvl.tutorialFromAnchor;
                        _hintTarget = lvl.tutorialTargetAnchor;
                        _hintToSlot = false;
                        _hintActive = true;
                        _hintTime = -0.8f;
                    }
                }
            }

            // 2) Mekanik tanıtım kartı: level tasarımında seçilen (oyuncu başına bir kez)
            bool hasIce = lvl.tutorial == LevelTutorial.FrozenBlock;
            bool hasLock = lvl.tutorial == LevelTutorial.LockAndKey, hasKey = false;
            bool hasFilled = lvl.tutorial == LevelTutorial.FilledBox;
            // "Görüldü" kaydı kart kapanınca yapılır (CloseCard): okunmadan level yeniden başlarsa kart tekrar çıkar
            if (hasIce && PlayerPrefs.GetInt(PrefIce, 0) == 0)
            {
                string how = lvl.iceMeltMode == IceMeltMode.PerBallCollected ? "a ball is collected" : "a box is filled";
                _cards.Enqueue(new IntroCard
                {
                    kind = IntroMechanic.Ice, prefKey = PrefIce, title = "FROZEN BLOCK",
                    body = $"A frozen block can't be sent up yet. Its counter drops each time {how}. At zero, the ice melts.",
                    target = FindMechanicBlock(blocks, b => b.iceCount > 0)
                });
            }
            if ((hasLock || hasKey) && PlayerPrefs.GetInt(PrefLock, 0) == 0)
            {
                _cards.Enqueue(new IntroCard
                {
                    kind = IntroMechanic.Lock, prefKey = PrefLock, title = "LOCK & KEY",
                    body = "A locked block stays shut until boxes carrying keys are filled and bring their keys. Fill key boxes first!",
                    target = FindMechanicBlock(blocks, b => b.lockKeyCount > 0) ?? FindMechanicBlock(blocks, b => b.hasKey)
                });
            }
            if (hasFilled && PlayerPrefs.GetInt(PrefFilledBox, 0) == 0)
            {
                _cards.Enqueue(new IntroCard
                {
                    kind = IntroMechanic.FilledBox, prefKey = PrefFilledBox, title = "FILLED BOX",
                    body = "This box starts full! Send it up to pour its balls into the glass, then it stays to collect its own color.",
                    target = FindMechanicBlock(blocks, b => b.isFilledBox)
                });
            }
            ShowNextCard();
        }

        private static MonsterCharacter FindMechanicBlock(List<MonsterCharacter> blocks, System.Func<MonsterCharacter, bool> match)
        {
            if (blocks == null) return null;
            foreach (var b in blocks)
                if (b != null && !b.IsCompleted && b.gameObject.activeInHierarchy && match(b)) return b;
            return null;
        }

        /// <summary>Slota gönderilebilir (tamamlanmamış, kilitsiz/buzsuz, çıkışı açık, tepside) blok mu?</summary>
        private static bool IsSendable(MonsterCharacter b, List<MonsterCharacter> blocks)
        {
            var grid = StagingGridBoard.Instance;
            return b != null && !b.IsCompleted && b.assignedSlot == null && !b.IsMovementOrCollectionBlocked
                   && b.gameObject.activeInHierarchy
                   && (grid == null || grid.IsBlockExitClearToSlots(b, blocks));
        }

        private static MonsterCharacter PickLevel1PurpleSpotlightBlock(List<MonsterCharacter> blocks)
        {
            if (blocks == null) return null;
            MonsterCharacter best = null;
            foreach (var b in blocks)
            {
                if (!IsSendable(b, blocks) || b.colorType != PuzzleColor.Purple) continue;
                if (best == null
                    || b.gridAnchor.y > best.gridAnchor.y
                    || (b.gridAnchor.y == best.gridAnchor.y && b.gridAnchor.x < best.gridAnchor.x))
                {
                    best = b;
                }
            }
            return best;
        }

        /// <summary>Level tasarımında seçilen ipucu kutusu (indeks + konum + renk eşleşmeli ve gönderilebilir olmalı).</summary>
        private static MonsterCharacter PickDesignerHintBlock(PuzzleGlassLevelData lvl, List<MonsterCharacter> blocks)
        {
            if (lvl == null || blocks == null || lvl.monsters == null) return null;
            if (lvl.tutorialBlockIndex < 0 || lvl.tutorialBlockIndex >= lvl.monsters.Count) return null;
            var entry = lvl.monsters[lvl.tutorialBlockIndex];
            // Kutunun güncel konumu (seçimden sonra kutu taşınmış olabilir; kayıtlı tutorialFromAnchor eskiyebilir)
            foreach (var b in blocks)
                if (IsSendable(b, blocks) && b.gridAnchor == entry.gridAnchor && b.colorType == entry.color) return b;
            return null;
        }

        private static MonsterCharacter PickSlotHintBlock(PuzzleGlassLevelData lvl, List<MonsterCharacter> blocks)
        {
            var grid = StagingGridBoard.Instance;
            System.Func<MonsterCharacter, bool> clear = b =>
                b != null && !b.IsCompleted && b.assignedSlot == null && !b.IsMovementOrCollectionBlocked
                && (grid == null || grid.IsBlockExitClearToSlots(b, blocks));

            if (lvl.tutorialBlockIndex >= 0 && lvl.monsters != null && lvl.tutorialBlockIndex < lvl.monsters.Count)
            {
                var entry = lvl.monsters[lvl.tutorialBlockIndex];
                foreach (var b in blocks)
                    if (clear(b) && b.gridAnchor == lvl.tutorialFromAnchor && b.colorType == entry.color) return b;
            }
            if (lvl.bottomToTopGroups != null && lvl.bottomToTopGroups.Count > 0 && lvl.bottomToTopGroups[0] != null)
            {
                var first = lvl.bottomToTopGroups[0].color;
                foreach (var b in blocks)
                    if (clear(b) && b.colorType == first) return b;
            }
            foreach (var b in blocks)
                if (clear(b)) return b;
            return null;
        }

        public void StopHint()
        {
            _hintActive = false;
            _hintBlock = null;
            if (_handGroup != null) _handGroup.alpha = 0f;
        }

        private void Update()
        {
            UpdateSpotlight();
            if (_cardObj != null)
            {
                // Kart açıkken el gösterilmez (rastgele bir bloğu işaretlemesi kafa karıştırıyordu);
                // el ipucu kart kapandıktan sonra başlar.
                UpdateCard();
                if (_handGroup != null && !_hintActive) _handGroup.alpha = 0f;
            }
            else
            {
                UpdateIdleHint();
                UpdateHint();
            }
        }

        // ==================== El ipucu ====================

        private void StartSlotHint(MonsterCharacter block)
        {
            _hintBlock = block;
            _hintFrom = block.gridAnchor;
            _hintToSlot = true;
            _hintActive = true;
            _hintTime = -0.4f;
        }

        /// <summary>
        /// İlk levellerde oyuncu IdleHintSeconds boyunca ilerleme kaydetmezse (hiçbir blok top toplamadı, dokunmadı)
        /// camdaki toplara en çok uyan gönderilebilir bloğu el ile gösterir.
        /// </summary>
        private void UpdateIdleHint()
        {
            var gm = PuzzleGlassGameManager.Instance;
            if (_spotlightActive || _hintActive || _level == null || _level.tutorial != LevelTutorial.FirstLevel || _blocks == null || !SlotSystem.IsActive
                || (gm != null && (gm.isLevelCompleted || gm.isLevelFailed)) || LevelTransitionController.IsTransitioning
                || PointerPressedThisFrame())
            {
                _idleTime = 0f;
                return;
            }

            int progress = 0;
            foreach (var b in _blocks) if (b != null) progress += Mathf.Max(0, b.remainingCount);
            if (progress != _lastProgress)
            {
                _lastProgress = progress;
                _idleTime = 0f;
                return;
            }

            _idleTime += Time.unscaledDeltaTime;
            if (_idleTime < IdleHintSeconds) return;
            _idleTime = 0f;
            if (SlotSystem.Instance.GetFirstEmptySlot() == null) return;
            var pick = PickIdleHintBlock();
            if (pick != null) StartSlotHint(pick);
        }

        /// <summary>Camdaki toplarla en çok eşleşen (slottaki aynı renk blokların ihtiyacı düşülmüş) gönderilebilir blok.</summary>
        private MonsterCharacter PickIdleHintBlock()
        {
            var pool = BallObjectPool.Instance;
            if (pool == null || pool.ActiveBalls == null) return null;
            var score = new Dictionary<PuzzleColor, int>();
            foreach (var ball in pool.ActiveBalls)
            {
                if (ball == null || !ball.gameObject.activeInHierarchy) continue;
                score.TryGetValue(ball.colorType, out int v);
                score[ball.colorType] = v + 1;
            }
            foreach (var b in _blocks)
            {
                if (b == null || b.IsCompleted || b.assignedSlot == null || b.isFilledBox) continue;
                if (score.TryGetValue(b.colorType, out int v)) score[b.colorType] = v - Mathf.Max(0, b.remainingCount);
            }

            MonsterCharacter best = null;
            int bestScore = 0;
            foreach (var b in _blocks)
            {
                if (!IsSendable(b, _blocks)) continue;
                // Dolu kutu her zaman işe yarar (toplarını cama döker); renk eşleşmesi yoksa ona yönlendir
                int s = b.isFilledBox ? 1 : (score.TryGetValue(b.colorType, out int v) ? v : 0);
                if (s > bestScore) { bestScore = s; best = b; }
            }
            return best;
        }

        private static bool PointerPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            if (Pointer.current != null && Pointer.current.press.wasPressedThisFrame) return true;
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame) return true;
            return false;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonDown(0) || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began);
#else
            return false;
#endif
        }

        // ==================== Level 1 Spotlight ====================

        private void UpdateSpotlight()
        {
            if (_spotlightRoot == null) return;
            if (!_spotlightActive) return;

            var gm = PuzzleGlassGameManager.Instance;
            if (_spotlightTarget == null || _spotlightTarget.IsCompleted || !_spotlightTarget.gameObject.activeInHierarchy
                || _spotlightTarget.assignedSlot != null || (gm != null && (gm.isLevelCompleted || gm.isLevelFailed)))
            {
                EndSpotlightTutorial();
                return;
            }

            if (LevelTransitionController.IsTransitioning)
            {
                if (_spotlightRoot.activeSelf) _spotlightRoot.SetActive(false);
                if (_handGroup != null) _handGroup.alpha = 0f;
                return;
            }

            if (!_spotlightRoot.activeSelf) _spotlightRoot.SetActive(true);

            if (_spotlightTarget.IsMovingToSlot)
            {
                if (_hintActive) StopHint();
            }
            else if (!_hintActive)
            {
                var drag = MonsterDragController.Instance;
                if (drag == null || !drag.IsBlockCurrentlyDragged(_spotlightTarget))
                {
                    StartSlotHint(_spotlightTarget);
                    _hintTime = -0.1f;
                }
            }

            var cam = Camera.main;
            if (cam == null || !TryGetTargetCanvasRect(_spotlightTarget, cam, out Vector2 holeCenter, out Vector2 holeSize))
                return;

            UpdateSpotlightVisualAndBlockers(holeCenter, holeSize);

            if (!_spotlightShown)
            {
                _spotlightShown = true;
                _spotlightTween?.Kill();
                _spotlightAlpha = 0f;
                _spotlightTween = DOTween.To(() => _spotlightAlpha, v => _spotlightAlpha = v, 1f, 0.22f)
                    .SetEase(Ease.OutQuad).SetUpdate(true);
                SetBlockersActive(true);
            }
        }

        private void UpdateSpotlightVisualAndBlockers(Vector2 holeCenter, Vector2 holeSize)
        {
            float halfW = holeSize.x * 0.5f;
            float halfH = holeSize.y * 0.5f;
            float xMin = holeCenter.x - halfW;
            float xMax = holeCenter.x + halfW;
            float yMin = holeCenter.y - halfH;
            float yMax = holeCenter.y + halfH;

            float canvasHalfW = Mathf.Max(600f, _root.rect.width * 0.5f + 120f);
            float canvasHalfH = Mathf.Max(1000f, _root.rect.height * 0.5f + 120f);

            SetRectBounds(_blockTop,    -canvasHalfW, yMax,         canvasHalfW, canvasHalfH);
            SetRectBounds(_blockBottom, -canvasHalfW, -canvasHalfH, canvasHalfW, yMin);
            SetRectBounds(_blockLeft,   -canvasHalfW, yMin,         xMin,        yMax);
            SetRectBounds(_blockRight,  xMax,         yMin,         canvasHalfW, yMax);

            if (_spotlightMat != null && _spotlightDimImg != null)
            {
                _spotlightDimImg.enabled = true;
                _spotlightDimImg.color = new Color(1f, 1f, 1f, _spotlightAlpha);
                _spotlightMat.SetVector(ShaderCanvasSizeId, new Vector4(_root.rect.width, _root.rect.height, 0f, 0f));
                _spotlightMat.SetVector(ShaderHoleRectId, new Vector4(holeCenter.x, holeCenter.y, halfW, halfH));
                _spotlightMat.SetVector(ShaderHoleParamsId, new Vector4(34f, 14f, 0f, 0f));
            }
            else
            {
                if (_spotlightDimImg != null) _spotlightDimImg.enabled = false;
                Color fallbackCol = new Color(ColSpotlightDim.r, ColSpotlightDim.g, ColSpotlightDim.b, ColSpotlightDim.a * _spotlightAlpha);
                if (_blockTopImg != null) _blockTopImg.color = fallbackCol;
                if (_blockBottomImg != null) _blockBottomImg.color = fallbackCol;
                if (_blockLeftImg != null) _blockLeftImg.color = fallbackCol;
                if (_blockRightImg != null) _blockRightImg.color = fallbackCol;
            }
        }

        private void SetBlockersActive(bool active)
        {
            if (_blockTopImg != null) _blockTopImg.raycastTarget = active;
            if (_blockBottomImg != null) _blockBottomImg.raycastTarget = active;
            if (_blockLeftImg != null) _blockLeftImg.raycastTarget = active;
            if (_blockRightImg != null) _blockRightImg.raycastTarget = active;
        }

        private void EndSpotlightTutorial()
        {
            _spotlightActive = false;
            _spotlightTarget = null;
            _pauseGlassBallsForTutorial = false;
            ReleaseGlassBalls();
            StopHint();
            SetBlockersActive(false);

            if (_spotlightRoot == null || !_spotlightRoot.activeSelf)
            {
                ClearSpotlight();
                return;
            }

            _spotlightTween?.Kill();
            _spotlightTween = DOTween.To(() => _spotlightAlpha, v =>
                {
                    _spotlightAlpha = v;
                    if (_spotlightDimImg != null && _spotlightDimImg.enabled)
                    {
                        _spotlightDimImg.color = new Color(1f, 1f, 1f, _spotlightAlpha);
                    }
                    else
                    {
                        Color fallbackCol = new Color(ColSpotlightDim.r, ColSpotlightDim.g, ColSpotlightDim.b, ColSpotlightDim.a * _spotlightAlpha);
                        if (_blockTopImg != null) _blockTopImg.color = fallbackCol;
                        if (_blockBottomImg != null) _blockBottomImg.color = fallbackCol;
                        if (_blockLeftImg != null) _blockLeftImg.color = fallbackCol;
                        if (_blockRightImg != null) _blockRightImg.color = fallbackCol;
                    }
                }, 0f, 0.22f)
                .SetEase(Ease.OutQuad)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    if (!_spotlightActive && _spotlightRoot != null) _spotlightRoot.SetActive(false);
                });
        }

        private void ClearSpotlight()
        {
            _spotlightTween?.Kill();
            _spotlightTween = null;
            _spotlightActive = false;
            _spotlightShown = false;
            _spotlightTarget = null;
            _spotlightAlpha = 0f;
            SetBlockersActive(false);
            if (_spotlightRoot != null) _spotlightRoot.SetActive(false);
        }

        private static void ReleaseGlassBalls()
        {
            if (!Application.isPlaying) return;
            if (BallObjectPool.Instance != null && BallObjectPool.Instance.ActiveBalls != null)
            {
                var active = BallObjectPool.Instance.ActiveBalls;
                for (int i = 0; i < active.Count; i++)
                {
                    var ball = active[i];
                    if (ball != null && ball.state == BallState.InContainer)
                    {
                        ball.ReleaseToPhysics();
                    }
                }
            }
            BallCollectionController.Instance?.MarkBallFieldDirty();
        }

        private bool TryGetTargetCanvasRect(MonsterCharacter target, Camera cam, out Vector2 center, out Vector2 size)
        {
            center = Vector2.zero;
            size = new Vector2(200f, 200f);
            if (target == null || cam == null || _root == null) return false;

            CarrierBlockShapeUtility.GetRectParts(target.blockShape, out Vector4 r0, out Vector4 r1, out int rectCount, out _, out _);
            float minX = r0.x - r0.z;
            float maxX = r0.x + r0.z;
            float minY = r0.y - r0.w - CarrierBlockShapeUtility.LipDepthOffset;
            float maxY = r0.y + r0.w;
            if (rectCount > 1)
            {
                minX = Mathf.Min(minX, r1.x - r1.z);
                maxX = Mathf.Max(maxX, r1.x + r1.z);
                minY = Mathf.Min(minY, r1.y - r1.w - CarrierBlockShapeUtility.LipDepthOffset);
                maxY = Mathf.Max(maxY, r1.y + r1.w);
            }

            Vector3 wMin = target.transform.TransformPoint(new Vector3(minX, minY, 0f));
            Vector3 wMax = target.transform.TransformPoint(new Vector3(maxX, maxY, 0f));
            Vector2 cMin = WorldToCanvas(cam, wMin);
            Vector2 cMax = WorldToCanvas(cam, wMax);

            const float pad = 16f;
            center = (cMin + cMax) * 0.5f;
            size = new Vector2(Mathf.Abs(cMax.x - cMin.x) + pad * 2f, Mathf.Abs(cMax.y - cMin.y) + pad * 2f);
            return true;
        }

        private static void SetRectBounds(RectTransform rt, float xMin, float yMin, float xMax, float yMax)
        {
            if (rt == null) return;
            rt.anchoredPosition = new Vector2((xMin + xMax) * 0.5f, (yMin + yMax) * 0.5f);
            rt.sizeDelta = new Vector2(Mathf.Max(0f, xMax - xMin), Mathf.Max(0f, yMax - yMin));
        }

        private void UpdateHint()
        {
            if (!_hintActive) return;
            var gm = PuzzleGlassGameManager.Instance;
            var grid = StagingGridBoard.Instance;
            var cam = Camera.main;
            if (_hintBlock == null || grid == null || cam == null || (gm != null && (gm.isLevelCompleted || gm.isLevelFailed))
                || _hintBlock.gridAnchor != _hintFrom || _hintBlock.isCollectingInGrid || _hintBlock.assignedSlot != null
                || _hintBlock.IsMovingToSlot || LevelTransitionController.IsTransitioning)
            {
                StopHint();
                return;
            }

            var drag = MonsterDragController.Instance;
            if (drag != null && drag.IsBlockCurrentlyDragged(_hintBlock)) { StopHint(); return; }

            _hintTime += Time.unscaledDeltaTime;
            Vector3 fromW = grid.GetWorldPositionForAnchor(_hintFrom, _hintBlock.blockShape);
            Vector2 from = WorldToCanvas(cam, fromW);
            bool hasHandSprite = _theme != null && _theme.iconHandClick != null;

            if (_hintToSlot)
            {
                var slot = SlotSystem.IsActive ? SlotSystem.Instance.GetFirstEmptySlot() : null;
                if (slot == null) { StopHint(); return; }

                if (_hintTime < 0f)
                {
                    _handGroup.alpha = 0f;
                    return;
                }

                float fadeIn = Mathf.Clamp01(_hintTime / 0.22f);
                float cycle = Mathf.Repeat(_hintTime, TapCycleSeconds) / TapCycleSeconds;
                float press = Mathf.Sin(Mathf.Clamp01(cycle / 0.55f) * Mathf.PI);
                float ringT = Mathf.Clamp01(cycle / 0.78f);

                _handGroup.alpha = fadeIn;
                Vector2 tapOffset = hasHandSprite ? new Vector2(18f, -20f) : new Vector2(34f, -44f);
                _finger.anchoredPosition = from + tapOffset + new Vector2(0f, -6f * press);
                _finger.localScale = Vector3.one * Mathf.Lerp(1.06f, 0.88f, press);
                _finger.localRotation = hasHandSprite ? Quaternion.Euler(0f, 0f, Mathf.Lerp(-10f, -20f, press)) : Quaternion.identity;

                _ring.anchoredPosition = from;
                _ring.localScale = Vector3.one * Mathf.Lerp(0.75f, 1.38f, ringT);
                _ringImg.color = new Color(1f, 0.94f, 0.55f, (1f - ringT) * 0.85f * fadeIn);
                return;
            }

            float t = Mathf.Repeat(Mathf.Max(0f, _hintTime), CycleSeconds);
            Vector3 toW = grid.GetWorldPositionForAnchor(_hintTarget, _hintBlock.blockShape);
            Vector2 to = WorldToCanvas(cam, toW);

            // 0-0.35 belir + bas, 0.35-1.45 sürükle, 1.45-1.85 bırak + sön, kalan bekle
            float alpha, move, dragPress;
            if (_hintTime < 0f) { alpha = 0f; move = 0f; dragPress = 0f; }
            else if (t < 0.35f) { alpha = t / 0.35f; move = 0f; dragPress = t / 0.35f; }
            else if (t < 1.45f) { alpha = 1f; move = Mathf.SmoothStep(0f, 1f, (t - 0.35f) / 1.10f); dragPress = 1f; }
            else if (t < 1.85f) { alpha = 1f - (t - 1.45f) / 0.40f; move = 1f; dragPress = 1f - (t - 1.45f) / 0.40f; }
            else { alpha = 0f; move = 1f; dragPress = 0f; }

            _handGroup.alpha = alpha;
            Vector2 pos = Vector2.Lerp(from, to, move);

            _finger.anchoredPosition = hasHandSprite ? pos : (pos + new Vector2(18f, -26f));
            _finger.localScale = Vector3.one * Mathf.Lerp(1.08f, 0.90f, dragPress);
            _finger.localRotation = hasHandSprite ? Quaternion.Euler(0f, 0f, Mathf.Lerp(-8f, -18f, dragPress)) : Quaternion.identity;

            _ring.anchoredPosition = pos;
            float ringScale = Mathf.Lerp(0.55f, 1.28f, dragPress);
            _ring.localScale = Vector3.one * ringScale;
            _ringImg.color = new Color(0.54f, 0.46f, 1.00f, 0.72f * dragPress);
        }

        private Vector2 WorldToCanvas(Camera cam, Vector3 world)
        {
            Vector2 screen = cam.WorldToScreenPoint(world);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, null, out Vector2 local);
            return local;
        }

        // ==================== Mekanik kartları ====================

        private void ShowNextCard()
        {
            if (_cardObj != null || _cards.Count == 0) return;
            _currentCard = _cards.Dequeue();
            var kind = _currentCard.kind;
            string title = _currentCard.title, body = _currentCard.body;

            // Tam ekran hafif karartma: kart açıkken bloklara dokunulmaz (MonsterDragController UI üstündeki dokunuşu yok sayar),
            // ekranın herhangi bir yerine dokunmak kartı kapatır
            _cardObj = new GameObject("IntroCardLayer", typeof(RectTransform), typeof(Image), typeof(Button));
            var layerRt = (RectTransform)_cardObj.transform;
            layerRt.SetParent(_root, false);
            layerRt.anchorMin = Vector2.zero; layerRt.anchorMax = Vector2.one;
            layerRt.offsetMin = layerRt.offsetMax = Vector2.zero;
            var dimImg = _cardObj.GetComponent<Image>();
            dimImg.color = new Color(0.08f, 0.05f, 0.22f, 0f);
            dimImg.DOFade(0.32f, 0.25f).SetUpdate(true);
            var layerBtn = _cardObj.GetComponent<Button>();
            layerBtn.transition = Selectable.Transition.None;
            layerBtn.onClick.AddListener(CloseCard);
            // El/halka karartmanın üstünde kalsın (gösterilen blok görünür olsun)
            if (_handGroup != null) _handGroup.transform.SetAsLastSibling();

            var cardGo = new GameObject("IntroCard", typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)cardGo.transform;
            rt.SetParent(layerRt, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -214f);
            rt.sizeDelta = new Vector2(880f, 360f);

            Sprite panelSprite = _theme != null ? _theme.whitePanelViolet : null;
            Sprite pillSprite = (_theme != null && _theme.roundedWhite80 != null) ? _theme.roundedWhite80 : _circle;

            // Alt yumuşak gölge
            var shGo = new GameObject("CardShadow", typeof(RectTransform), typeof(Image));
            shGo.transform.SetParent(rt, false);
            var shRt = (RectTransform)shGo.transform;
            shRt.anchorMin = Vector2.zero; shRt.anchorMax = Vector2.one;
            shRt.offsetMin = new Vector2(6f, -12f); shRt.offsetMax = new Vector2(-6f, -12f);
            var shImg = shGo.GetComponent<Image>();
            shImg.sprite = pillSprite;
            shImg.type = Image.Type.Sliced;
            shImg.color = new Color(0.12f, 0.10f, 0.34f, 0.34f);
            shImg.raycastTarget = false;

            // Kart yüzeyi
            var faceGo = new GameObject("CardFace", typeof(RectTransform), typeof(Image));
            faceGo.transform.SetParent(rt, false);
            var faceRt = (RectTransform)faceGo.transform;
            faceRt.anchorMin = Vector2.zero; faceRt.anchorMax = Vector2.one;
            faceRt.offsetMin = faceRt.offsetMax = Vector2.zero;

            var rootImg = cardGo.GetComponent<Image>();
            rootImg.color = new Color(1f, 1f, 1f, 0f); // tıklama alanı şeffaf, görsel child'da

            var faceImg = faceGo.GetComponent<Image>();
            if (panelSprite != null)
            {
                faceImg.sprite = panelSprite;
                faceImg.type = Image.Type.Sliced;
                faceImg.pixelsPerUnitMultiplier = 2.05f; // ~95px üst mor başlık şeridi
                faceImg.color = Color.white;
            }
            else
            {
                faceImg.sprite = _circle;
                faceImg.type = Image.Type.Sliced;
                faceImg.color = ColVioletFallback;
            }
            faceImg.raycastTarget = false;

            cardGo.GetComponent<Button>().onClick.AddListener(CloseCard);

            // Başlık (mor üst şeridin içinde)
            Text titleTxt = AddText(
                faceRt, title, 40, FontStyle.Bold, Color.white,
                new Vector2(-16f, -18f), new Vector2(720f, 64f), TextAnchor.MiddleCenter);
            var titleShadow = titleTxt.gameObject.AddComponent<Shadow>();
            titleShadow.effectColor = new Color(0.14f, 0.08f, 0.38f, 0.50f);
            titleShadow.effectDistance = new Vector2(0f, -2.5f);

            // Sağ üst X kapat ikonu
            if (_theme != null && _theme.iconClose != null)
            {
                var closeGo = new GameObject("CloseHint", typeof(RectTransform), typeof(Image));
                closeGo.transform.SetParent(faceRt, false);
                var cRt = (RectTransform)closeGo.transform;
                cRt.anchorMin = cRt.anchorMax = new Vector2(1f, 1f);
                cRt.anchoredPosition = new Vector2(-46f, -46f);
                cRt.sizeDelta = new Vector2(34f, 34f);
                var cImg = closeGo.GetComponent<Image>();
                cImg.sprite = _theme.iconClose;
                cImg.preserveAspect = true;
                cImg.color = new Color(1f, 1f, 1f, 0.88f);
                cImg.raycastTarget = false;
            }

            // Sol mekanik ikon rozeti
            GetMechanicVisual(kind, out Sprite mechIcon, out Color badgeColor, out bool tintIconWhite);
            var badgeGo = new GameObject("MechanicBadge", typeof(RectTransform), typeof(Image));
            badgeGo.transform.SetParent(faceRt, false);
            var bRt = (RectTransform)badgeGo.transform;
            bRt.anchorMin = bRt.anchorMax = new Vector2(0f, 0.5f);
            bRt.anchoredPosition = new Vector2(106f, -34f);
            bRt.sizeDelta = new Vector2(126f, 126f);
            var bImg = badgeGo.GetComponent<Image>();
            bImg.sprite = pillSprite;
            bImg.type = Image.Type.Sliced;
            bImg.color = badgeColor;
            bImg.raycastTarget = false;

            var bShadow = badgeGo.AddComponent<Shadow>();
            bShadow.effectColor = new Color(0.14f, 0.12f, 0.36f, 0.25f);
            bShadow.effectDistance = new Vector2(0f, -5f);

            if (mechIcon != null)
            {
                var mGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                mGo.transform.SetParent(bRt, false);
                var mRt = (RectTransform)mGo.transform;
                mRt.anchorMin = mRt.anchorMax = new Vector2(0.5f, 0.5f);
                mRt.anchoredPosition = Vector2.zero;
                mRt.sizeDelta = new Vector2(78f, 78f);
                var mImg = mGo.GetComponent<Image>();
                mImg.sprite = mechIcon;
                mImg.preserveAspect = true;
                mImg.color = tintIconWhite ? Color.white : Color.white;
                mImg.raycastTarget = false;
            }

            // Açıklama metni (sağda)
            Color bodyColor = panelSprite != null ? ColCardBody : Color.white;
            Text bodyTxt = AddText(
                faceRt, body, 28, FontStyle.Bold, bodyColor,
                new Vector2(84f, -112f), new Vector2(620f, 168f), TextAnchor.MiddleLeft);
            bodyTxt.lineSpacing = 1.12f;

            // Alt devam ipucu
            Color tapColor = panelSprite != null ? new Color(0.45f, 0.46f, 0.70f, 0.88f) : new Color(1f, 1f, 1f, 0.65f);
            AddText(faceRt, "TAP TO CONTINUE", 22, FontStyle.Bold, tapColor, new Vector2(0f, -304f), new Vector2(780f, 36f), TextAnchor.MiddleCenter);

            rt.localScale = Vector3.one * 0.86f;
            rt.DOScale(1f, 0.26f).SetEase(Ease.OutBack).SetUpdate(true);

            _cardTime = 0f;
        }

        private void GetMechanicVisual(IntroMechanic kind, out Sprite sprite, out Color badgeColor, out bool tintWhite)
        {
            tintWhite = true;
            if (_theme == null)
            {
                sprite = null;
                badgeColor = ColVioletFallback;
                return;
            }

            switch (kind)
            {
                case IntroMechanic.Ice:
                    sprite = _theme.iconSnow;
                    badgeColor = new Color(0.28f, 0.73f, 0.98f, 1f);
                    break;
                case IntroMechanic.Lock:
                    sprite = _theme.coloredGoldLock != null ? _theme.coloredGoldLock : _theme.iconLock;
                    badgeColor = new Color(0.95f, 0.72f, 0.20f, 1f);
                    tintWhite = _theme.coloredGoldLock == null;
                    break;
                case IntroMechanic.FilledBox:
                default:
                    sprite = _theme.iconGiftBox != null ? _theme.iconGiftBox : _theme.coloredBox;
                    badgeColor = new Color(0.48f, 0.36f, 0.96f, 1f);
                    break;
            }
        }

        private void UpdateCard()
        {
            if (_cardObj == null) return;
            _cardTime += Time.unscaledDeltaTime;
            // Kendiliğinden kapanmaz: oyuncu okuyup dokununca kapanır
        }

        private void CloseCard()
        {
            if (_cardObj == null || _cardTime < CardMinSeconds) return;
            if (!string.IsNullOrEmpty(_currentCard.prefKey))
            {
                PlayerPrefs.SetInt(_currentCard.prefKey, 1);
                PlayerPrefs.Save();
            }
            DestroyCardObject();
            ShowNextCard();
        }

        private void ClearCards()
        {
            _cards.Clear();
            DestroyCardObject();
        }

        private void DestroyCardObject()
        {
            if (_cardObj != null)
            {
                foreach (var t in _cardObj.GetComponentsInChildren<Transform>(true)) t.DOKill(true);
                var img = _cardObj.GetComponent<Image>();
                if (img != null) img.DOKill();
                Destroy(_cardObj);
            }
            _cardObj = null;
            _currentCard = default;
            if (_handGroup != null && !_hintActive) _handGroup.alpha = 0f;
        }

        // ==================== Kurulum ====================

        private void BuildCanvas()
        {
            _theme = PuzzleGlassUITheme.Load();
            _font = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI Semibold", "Segoe UI", "Arial" }, 40);
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _circle = CreateCircleSprite(128);
            _ringSprite = CreateRingSprite(128);

            var canvasGo = new GameObject("TutorialCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 60; // HUD (50) üstü, level geçişi (30000) altı
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            _root = (RectTransform)canvasGo.transform;

            BuildSpotlight();

            // El ipucu grubu (tıklamaları engellemez)
            var hand = new GameObject("Hand", typeof(RectTransform), typeof(CanvasGroup));
            hand.transform.SetParent(_root, false);
            var handRt = (RectTransform)hand.transform;
            handRt.anchorMin = Vector2.zero; handRt.anchorMax = Vector2.one;
            handRt.offsetMin = handRt.offsetMax = Vector2.zero;
            _handGroup = hand.GetComponent<CanvasGroup>();
            _handGroup.blocksRaycasts = false;
            _handGroup.interactable = false;
            _handGroup.alpha = 0f;

            _ring = MakeImage(handRt, "Ring", 160f, new Color(0.54f, 0.46f, 1f, 0f), _ringSprite, out _ringImg);

            bool hasHandSprite = _theme != null && _theme.iconHandClick != null;
            _finger = MakeImage(
                handRt,
                "Finger",
                hasHandSprite ? 128f : 86f,
                new Color(1f, 1f, 1f, 0.98f),
                hasHandSprite ? _theme.iconHandClick : _circle,
                out _fingerImg);
            _fingerImg.preserveAspect = true;
            if (hasHandSprite)
            {
                // Touch.png işaret parmağının ucu spritenın üst solunda (0.34, 0.95)
                _finger.pivot = new Vector2(0.34f, 0.95f);
            }
            var shadow = _finger.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.16f, 0.12f, 0.42f, 0.48f);
            shadow.effectDistance = new Vector2(4f, -8f);

            foreach (var r in new[] { _ring, _finger }) { r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f); }
        }

        private void BuildSpotlight()
        {
            _spotlightRoot = new GameObject("SpotlightLayer", typeof(RectTransform));
            _spotlightRoot.transform.SetParent(_root, false);
            var spRt = (RectTransform)_spotlightRoot.transform;
            spRt.anchorMin = Vector2.zero;
            spRt.anchorMax = Vector2.one;
            spRt.offsetMin = spRt.offsetMax = Vector2.zero;

            var dimGo = new GameObject("SpotlightDim", typeof(RectTransform), typeof(Image));
            dimGo.transform.SetParent(spRt, false);
            var dimRt = (RectTransform)dimGo.transform;
            dimRt.anchorMin = Vector2.zero;
            dimRt.anchorMax = Vector2.one;
            dimRt.offsetMin = dimRt.offsetMax = Vector2.zero;
            _spotlightDimImg = dimGo.GetComponent<Image>();
            _spotlightDimImg.raycastTarget = false;

            Material baseMat = Resources.Load<Material>("TutorialSpotlightMat");
            if (baseMat != null)
            {
                _spotlightMat = new Material(baseMat);
            }
            else
            {
                Shader sh = Shader.Find("PuzzleGlass/TutorialSpotlight");
                if (sh != null) _spotlightMat = new Material(sh);
            }
            if (_spotlightMat != null)
            {
                _spotlightMat.color = ColSpotlightDim;
                _spotlightDimImg.material = _spotlightMat;
                _spotlightDimImg.color = new Color(1f, 1f, 1f, 0f);
            }
            else
            {
                _spotlightDimImg.enabled = false;
            }

            _blockTop    = MakeSpotlightBlocker(spRt, "BlockTop",    out _blockTopImg);
            _blockBottom = MakeSpotlightBlocker(spRt, "BlockBottom", out _blockBottomImg);
            _blockLeft   = MakeSpotlightBlocker(spRt, "BlockLeft",   out _blockLeftImg);
            _blockRight  = MakeSpotlightBlocker(spRt, "BlockRight",  out _blockRightImg);

            _spotlightRoot.SetActive(false);
        }

        private static RectTransform MakeSpotlightBlocker(RectTransform parent, string name, out Image img)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            img = go.GetComponent<Image>();
            img.color = Color.clear;
            img.raycastTarget = false;
            return rt;
        }

        private RectTransform MakeImage(RectTransform parent, string name, float size, Color color, Sprite sprite, out Image img)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(size, size);
            img = go.GetComponent<Image>();
            img.sprite = sprite != null ? sprite : _circle;
            img.color = color;
            img.raycastTarget = false;
            return rt;
        }

        private Text AddText(RectTransform parent, string text, int size, FontStyle style, Color color, Vector2 pos, Vector2 box, TextAnchor anchor)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = box;
            var t = go.GetComponent<Text>();
            t.font = _font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        private static Sprite CreateCircleSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            float r = size * 0.5f;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                    byte a = (byte)(Mathf.Clamp01(r - d) * 255f);
                    px[y * size + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px);
            tex.Apply();
            float border = size * 0.45f;
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        private static Sprite CreateRingSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            float c = size * 0.5f;
            float outerR = size * 0.46f;
            float innerR = size * 0.34f;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(c, c));
                    float outerAlpha = Mathf.Clamp01(outerR - d);
                    float innerAlpha = Mathf.Clamp01(d - innerR);
                    byte a = (byte)(Mathf.Min(outerAlpha, innerAlpha) * 255f);
                    px[y * size + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
