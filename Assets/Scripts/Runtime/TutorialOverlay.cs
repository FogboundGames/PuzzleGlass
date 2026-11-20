using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace PuzzleGlass
{
    /// <summary>
    /// Öğretici katman (Violet Theme UI assetleri + pastel lavanta teması):
    ///  1. El ipucu: levelde tutorialHint açıksa Violet el ikonu ve dokunma halkası ile bloğu işaret eder.
    ///  2. Mekanik tanıtımı: buz, kilit & anahtar ve içi dolu kutu bir levelde İLK kez görüldüğünde
    ///     Violet panel ve ikon rozetli kısa bir tanıtım kartı çıkar.
    /// Kendi canvas'ı vardır (HUD'un üstünde, level geçişinin altında).
    /// </summary>
    public class TutorialOverlay : MonoBehaviour
    {
        public static TutorialOverlay Instance { get; private set; }

        public const string PrefIce = "PG_Intro_Ice";
        public const string PrefLock = "PG_Intro_Lock";
        public const string PrefFilledBox = "PG_Intro_FilledBox";
        private const float CycleSeconds = 2.2f;
        private const float CardSeconds = 7.5f;

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
        private RectTransform _bubble;
        private Image _bubbleIconImg;
        private CanvasGroup _handGroup;
        private Sprite _circle;
        private Sprite _ringSprite;
        private Font _font;

        private MonsterCharacter _hintBlock;
        private Vector2Int _hintFrom, _hintTarget;
        private bool _hintActive;
        private bool _hintToSlot;
        private Text _hintLabel;
        private float _hintTime;

        private readonly Queue<(IntroMechanic kind, string title, string body)> _cards = new Queue<(IntroMechanic, string, string)>();
        private GameObject _cardObj;
        private float _cardTime;

        private static readonly Color ColIndigoDeep = new Color(0.240f, 0.255f, 0.580f, 1.00f);
        private static readonly Color ColCardBody   = new Color(0.270f, 0.285f, 0.540f, 1.00f);
        private static readonly Color ColVioletFallback = new Color(0.420f, 0.340f, 0.880f, 0.96f);

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
            if (Instance == this) Instance = null;
        }

        /// <summary>Level yüklendikten (bloklar oluştuktan) sonra çağrılır.</summary>
        public void Begin(PuzzleGlassLevelData lvl, List<MonsterCharacter> blocks)
        {
            StopHint();
            ClearCards();
            if (lvl == null) return;

            // 1) El ipucu
            if (lvl.tutorialHint && blocks != null && SlotSystem.IsActive)
            {
                _hintBlock = PickSlotHintBlock(lvl, blocks);
                if (_hintBlock != null)
                {
                    _hintFrom = _hintBlock.gridAnchor;
                    _hintToSlot = true;
                    _hintActive = true;
                    _hintTime = -0.8f;
                    if (_hintLabel != null) _hintLabel.text = "Tap to send it up";
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
                    if (_hintLabel != null) _hintLabel.text = "Drag the block here";
                }
            }

            // 2) Mekanik tanıtımları (ilk görüldüklerinde bir kez)
            bool hasIce = false, hasLock = false, hasKey = false, hasFilled = false;
            if (lvl.monsters != null)
            {
                foreach (var m in lvl.monsters)
                {
                    if (m == null) continue;
                    if (m.iceCount > 0) hasIce = true;
                    if (m.lockKeyCount > 0) hasLock = true;
                    if (m.hasKey) hasKey = true;
                    if (m.isFilledBox) hasFilled = true;
                }
            }
            if (hasIce && PlayerPrefs.GetInt(PrefIce, 0) == 0)
            {
                string how = lvl.iceMeltMode == IceMeltMode.PerBallCollected ? "a ball is collected" : "a box is filled";
                _cards.Enqueue((IntroMechanic.Ice, "FROZEN BLOCK", $"A frozen block can't move or collect balls. Its counter drops each time {how}. At zero, the ice melts."));
                PlayerPrefs.SetInt(PrefIce, 1);
            }
            if ((hasLock || hasKey) && PlayerPrefs.GetInt(PrefLock, 0) == 0)
            {
                _cards.Enqueue((IntroMechanic.Lock, "LOCK & KEY", "A locked block stays shut until boxes carrying keys are filled and bring their keys. Fill key boxes first!"));
                PlayerPrefs.SetInt(PrefLock, 1);
            }
            if (hasFilled && PlayerPrefs.GetInt(PrefFilledBox, 0) == 0)
            {
                _cards.Enqueue((IntroMechanic.FilledBox, "FILLED BOX", "A filled box starts full! Send it up to pour its balls into the glass, then it stays to collect matching balls."));
                PlayerPrefs.SetInt(PrefFilledBox, 1);
            }
            if (_cards.Count > 0) PlayerPrefs.Save();
            ShowNextCard();
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
            UpdateHint();
            UpdateCard();
        }

        // ==================== El ipucu ====================

        private void UpdateHint()
        {
            if (!_hintActive) return;
            var gm = PuzzleGlassGameManager.Instance;
            var grid = StagingGridBoard.Instance;
            var cam = Camera.main;
            if (_hintBlock == null || grid == null || cam == null || (gm != null && (gm.isLevelCompleted || gm.isLevelFailed))
                || _hintBlock.gridAnchor != _hintFrom || _hintBlock.isCollectingInGrid || _hintBlock.assignedSlot != null
                || LevelTransitionController.IsTransitioning)
            {
                StopHint();
                return;
            }

            var drag = MonsterDragController.Instance;
            if (drag != null && drag.IsBlockCurrentlyDragged(_hintBlock)) { StopHint(); return; }

            _hintTime += Time.unscaledDeltaTime;
            float t = Mathf.Repeat(Mathf.Max(0f, _hintTime), CycleSeconds);

            Vector3 fromW = grid.GetWorldPositionForAnchor(_hintFrom, _hintBlock.blockShape);
            Vector3 toW;
            if (_hintToSlot)
            {
                var slot = SlotSystem.IsActive ? SlotSystem.Instance.GetFirstEmptySlot() : null;
                if (slot == null) { StopHint(); return; }
                toW = slot.transform.position;
            }
            else
            {
                toW = grid.GetWorldPositionForAnchor(_hintTarget, _hintBlock.blockShape);
            }
            Vector2 from = WorldToCanvas(cam, fromW), to = WorldToCanvas(cam, toW);

            // 0-0.35 belir + bas, 0.35-1.45 sürükle, 1.45-1.85 bırak + sön, kalan bekle
            float alpha, move, press;
            if (_hintTime < 0f) { alpha = 0f; move = 0f; press = 0f; }
            else if (t < 0.35f) { alpha = t / 0.35f; move = 0f; press = t / 0.35f; }
            else if (t < 1.45f) { alpha = 1f; move = Mathf.SmoothStep(0f, 1f, (t - 0.35f) / 1.10f); press = 1f; }
            else if (t < 1.85f) { alpha = 1f - (t - 1.45f) / 0.40f; move = 1f; press = 1f - (t - 1.45f) / 0.40f; }
            else { alpha = 0f; move = 1f; press = 0f; }

            _handGroup.alpha = alpha;
            Vector2 pos = Vector2.Lerp(from, to, move);

            bool hasHandSprite = _theme != null && _theme.iconHandClick != null;
            _finger.anchoredPosition = hasHandSprite ? pos : (pos + new Vector2(18f, -26f));
            _finger.localScale = Vector3.one * Mathf.Lerp(1.08f, 0.90f, press);
            _finger.localRotation = hasHandSprite ? Quaternion.Euler(0f, 0f, Mathf.Lerp(-8f, -18f, press)) : Quaternion.identity;

            _ring.anchoredPosition = pos;
            float ringScale = Mathf.Lerp(0.55f, 1.28f, press);
            _ring.localScale = Vector3.one * ringScale;
            _ringImg.color = new Color(0.54f, 0.46f, 1.00f, 0.72f * press);

            Vector2 bubblePos;
            if (_hintToSlot)
            {
                Vector3 trayBottomW = grid.GetWorldPositionForAnchor(new Vector2Int(_hintFrom.x, 0), _hintBlock.blockShape)
                                      - new Vector3(0f, grid.cellSize * 0.5f + 0.34f, 0f);
                bubblePos = new Vector2(0f, WorldToCanvas(cam, trayBottomW).y);
            }
            else
            {
                bubblePos = Vector2.Lerp(from, to, 0.5f) + new Vector2(0f, 128f);
            }
            float halfW = _root.rect.width * 0.5f - _bubble.sizeDelta.x * 0.5f - 16f;
            if (halfW > 0f) bubblePos.x = Mathf.Clamp(bubblePos.x, -halfW, halfW);
            _bubble.anchoredPosition = bubblePos;
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
            var (kind, title, body) = _cards.Dequeue();

            _cardObj = new GameObject("IntroCard", typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)_cardObj.transform;
            rt.SetParent(_root, false);
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

            var rootImg = _cardObj.GetComponent<Image>();
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

            _cardObj.GetComponent<Button>().onClick.AddListener(CloseCard);

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
            if (_cardTime >= CardSeconds) CloseCard();
        }

        private void CloseCard()
        {
            if (_cardObj != null)
            {
                _cardObj.transform.DOKill(true);
                Destroy(_cardObj);
            }
            _cardObj = null;
            ShowNextCard();
        }

        private void ClearCards()
        {
            _cards.Clear();
            if (_cardObj != null)
            {
                _cardObj.transform.DOKill(true);
                Destroy(_cardObj);
            }
            _cardObj = null;
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
                // İşaret parmağının ucu spritenın üst sol-ortasında (0.42, 0.92)
                _finger.pivot = new Vector2(0.42f, 0.92f);
            }
            var shadow = _finger.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.16f, 0.12f, 0.42f, 0.48f);
            shadow.effectDistance = new Vector2(4f, -8f);

            foreach (var r in new[] { _ring, _finger }) { r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f); }

            // İpucu balonu
            var bubbleGo = new GameObject("Bubble", typeof(RectTransform), typeof(Image));
            bubbleGo.transform.SetParent(handRt, false);
            _bubble = (RectTransform)bubbleGo.transform;
            _bubble.anchorMin = _bubble.anchorMax = new Vector2(0.5f, 0.5f);
            _bubble.sizeDelta = new Vector2(540f, 96f);

            Sprite bubbleSpr = _theme != null && _theme.buttonViolet != null ? _theme.buttonViolet : _circle;
            var bImg = bubbleGo.GetComponent<Image>();
            bImg.sprite = bubbleSpr;
            bImg.type = Image.Type.Sliced;
            bImg.pixelsPerUnitMultiplier = bubbleSpr != _circle ? 1.15f : 1f;
            bImg.color = bubbleSpr != _circle ? Color.white : ColVioletFallback;
            bImg.raycastTarget = false;

            var bShadow = bubbleGo.AddComponent<Shadow>();
            bShadow.effectColor = new Color(0.14f, 0.10f, 0.36f, 0.34f);
            bShadow.effectDistance = new Vector2(0f, -6f);

            Sprite infoIco = _theme != null ? (_theme.iconInfo != null ? _theme.iconInfo : _theme.iconHandClick) : null;
            if (infoIco != null)
            {
                var icoGo = new GameObject("BubbleIcon", typeof(RectTransform), typeof(Image));
                icoGo.transform.SetParent(_bubble, false);
                var iRt = (RectTransform)icoGo.transform;
                iRt.anchorMin = iRt.anchorMax = new Vector2(0f, 0.54f);
                iRt.anchoredPosition = new Vector2(48f, 0f);
                iRt.sizeDelta = new Vector2(40f, 40f);
                _bubbleIconImg = icoGo.GetComponent<Image>();
                _bubbleIconImg.sprite = infoIco;
                _bubbleIconImg.preserveAspect = true;
                _bubbleIconImg.color = Color.white;
                _bubbleIconImg.raycastTarget = false;
            }

            _hintLabel = AddText(_bubble, "Drag the block here", 35, FontStyle.Bold, Color.white, new Vector2(infoIco != null ? 18f : 0f, 4f), new Vector2(440f, 76f), TextAnchor.MiddleCenter);
            var lblRt = (RectTransform)_hintLabel.transform;
            lblRt.anchorMin = lblRt.anchorMax = new Vector2(0.5f, 0.5f);
            lblRt.pivot = new Vector2(0.5f, 0.5f);
            _hintLabel.raycastTarget = false;
            var lShadow = _hintLabel.gameObject.AddComponent<Shadow>();
            lShadow.effectColor = new Color(0.14f, 0.08f, 0.38f, 0.52f);
            lShadow.effectDistance = new Vector2(0f, -2f);
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
