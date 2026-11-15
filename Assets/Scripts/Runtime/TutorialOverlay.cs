using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PuzzleGlass
{
    /// <summary>
    /// Öğretici katman:
    ///  1. El ipucu: levelde tutorialHint açıksa bir parmak, belirtilen bloğun üstünden hedef konuma kayar
    ///     ("Bloğu buraya sürükle"). Oyuncu o bloğu hareket ettirince ya da blok toplamaya başlayınca kaybolur.
    ///  2. Mekanik tanıtımı: buz, kilit ya da anahtar bir levelde İLK kez görüldüğünde kısa bir kart çıkar
    ///     (dokununca ya da birkaç saniye sonra kapanır; bir kez gösterilir, PlayerPrefs'te tutulur).
    /// Kendi canvas'ı vardır (HUD'un üstünde, level geçişinin altında).
    /// </summary>
    public class TutorialOverlay : MonoBehaviour
    {
        public static TutorialOverlay Instance { get; private set; }

        public const string PrefIce = "PG_Intro_Ice";
        public const string PrefLock = "PG_Intro_Lock";
        private const float CycleSeconds = 2.2f;
        private const float CardSeconds = 6f;

        private Canvas _canvas;
        private RectTransform _root;
        private RectTransform _finger, _ring;
        private Image _fingerImg, _ringImg;
        private RectTransform _bubble;
        private CanvasGroup _handGroup;
        private Sprite _circle;
        private Font _font;

        private MonsterCharacter _hintBlock;
        private Vector2Int _hintFrom, _hintTarget;
        private bool _hintActive;
        private float _hintTime;

        private readonly Queue<(string title, string body)> _cards = new Queue<(string, string)>();
        private GameObject _cardObj;
        private float _cardTime;

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
            if (lvl.tutorialHint && blocks != null && lvl.tutorialBlockIndex >= 0 && lvl.monsters != null && lvl.tutorialBlockIndex < lvl.monsters.Count)
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
                    _hintActive = true;
                    _hintTime = -0.8f; // yükleme sonrası kısa bekleme
                }
            }

            // 2) Mekanik tanıtımları (ilk görüldüklerinde bir kez)
            bool hasIce = false, hasLock = false, hasKey = false;
            if (lvl.monsters != null)
                foreach (var m in lvl.monsters)
                {
                    if (m == null) continue;
                    if (m.iceCount > 0) hasIce = true;
                    if (m.lockKeyCount > 0) hasLock = true;
                    if (m.hasKey) hasKey = true;
                }
            if (hasIce && PlayerPrefs.GetInt(PrefIce, 0) == 0)
            {
                string how = lvl.iceMeltMode == IceMeltMode.PerBallCollected ? "a ball is collected" : "a box is filled";
                _cards.Enqueue(("FROZEN BLOCK", $"A frozen block can't move or collect balls. Its number goes down each time {how}. At zero, the ice melts."));
                PlayerPrefs.SetInt(PrefIce, 1);
            }
            if ((hasLock || hasKey) && PlayerPrefs.GetInt(PrefLock, 0) == 0)
            {
                _cards.Enqueue(("LOCK & KEY", "A locked block stays shut until boxes with keys are filled and bring their keys. Fill the key boxes first."));
                PlayerPrefs.SetInt(PrefLock, 1);
            }
            if (_cards.Count > 0) PlayerPrefs.Save();
            ShowNextCard();
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
                || _hintBlock.gridAnchor != _hintFrom || _hintBlock.isCollectingInGrid || LevelTransitionController.IsTransitioning)
            {
                StopHint();
                return;
            }
            // Oyuncu bu bloğu sürüklüyorsa ipucu kaybolur
            var drag = MonsterDragController.Instance;
            if (drag != null && drag.IsBlockCurrentlyDragged(_hintBlock)) { StopHint(); return; }

            _hintTime += Time.unscaledDeltaTime;
            float t = Mathf.Repeat(Mathf.Max(0f, _hintTime), CycleSeconds);

            Vector3 fromW = grid.GetWorldPositionForAnchor(_hintFrom, _hintBlock.blockShape);
            Vector3 toW = grid.GetWorldPositionForAnchor(_hintTarget, _hintBlock.blockShape);
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
            _finger.anchoredPosition = pos + new Vector2(18f, -26f); // parmak ucu hedefin biraz sağ altında
            _finger.localScale = Vector3.one * Mathf.Lerp(1.12f, 0.92f, press);
            _ring.anchoredPosition = pos;
            float ringScale = Mathf.Lerp(0.6f, 1.25f, press);
            _ring.localScale = Vector3.one * ringScale;
            _ringImg.color = new Color(1f, 1f, 1f, 0.55f * press);
            _bubble.anchoredPosition = Vector2.Lerp(from, to, 0.5f) + new Vector2(0f, 120f);
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
            var (title, body) = _cards.Dequeue();

            _cardObj = new GameObject("IntroCard", typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)_cardObj.transform;
            rt.SetParent(_root, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -230f);
            rt.sizeDelta = new Vector2(860f, 300f);
            var img = _cardObj.GetComponent<Image>();
            img.sprite = _circle;
            img.type = Image.Type.Sliced;
            img.color = new Color(0.27f, 0.24f, 0.62f, 0.96f);
            _cardObj.GetComponent<Button>().onClick.AddListener(CloseCard);

            AddText(rt, title, 46, FontStyle.Bold, new Color(1f, 0.86f, 0.42f), new Vector2(0f, -36f), new Vector2(800f, 60f));
            AddText(rt, body, 34, FontStyle.Normal, Color.white, new Vector2(0f, -104f), new Vector2(800f, 150f));
            AddText(rt, "tap to continue", 26, FontStyle.Italic, new Color(1f, 1f, 1f, 0.6f), new Vector2(0f, -250f), new Vector2(800f, 40f));
            _cardTime = 0f;
        }

        private void UpdateCard()
        {
            if (_cardObj == null) return;
            _cardTime += Time.unscaledDeltaTime;
            if (_cardTime >= CardSeconds) CloseCard();
        }

        private void CloseCard()
        {
            if (_cardObj != null) Destroy(_cardObj);
            _cardObj = null;
            ShowNextCard();
        }

        private void ClearCards()
        {
            _cards.Clear();
            if (_cardObj != null) Destroy(_cardObj);
            _cardObj = null;
        }

        // ==================== Kurulum ====================

        private void BuildCanvas()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _circle = CreateCircleSprite(128);

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

            _ring = MakeImage(handRt, "Ring", 150f, new Color(1f, 1f, 1f, 0f), out _ringImg);
            _finger = MakeImage(handRt, "Finger", 86f, new Color(1f, 1f, 1f, 0.95f), out _fingerImg);
            var shadow = _finger.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.2f, 0.15f, 0.45f, 0.45f);
            shadow.effectDistance = new Vector2(4f, -6f);
            // Ortalama ekran merkezli konumlar için anchor ortada
            foreach (var r in new[] { _ring, _finger }) { r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f); }

            var bubbleGo = new GameObject("Bubble", typeof(RectTransform), typeof(Image));
            bubbleGo.transform.SetParent(handRt, false);
            _bubble = (RectTransform)bubbleGo.transform;
            _bubble.anchorMin = _bubble.anchorMax = new Vector2(0.5f, 0.5f);
            _bubble.sizeDelta = new Vector2(520f, 92f);
            var bImg = bubbleGo.GetComponent<Image>();
            bImg.sprite = _circle;
            bImg.type = Image.Type.Sliced;
            bImg.color = new Color(0.27f, 0.24f, 0.62f, 0.92f);
            bImg.raycastTarget = false;
            var label = AddText(_bubble, "Drag the block here", 38, FontStyle.Bold, Color.white, Vector2.zero, _bubble.sizeDelta);
            ((RectTransform)label.transform).anchorMin = ((RectTransform)label.transform).anchorMax = new Vector2(0.5f, 0.5f);
            ((RectTransform)label.transform).pivot = new Vector2(0.5f, 0.5f);
            label.raycastTarget = false;
        }

        private RectTransform MakeImage(RectTransform parent, string name, float size, Color color, out Image img)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(size, size);
            img = go.GetComponent<Image>();
            img.sprite = _circle;
            img.color = color;
            img.raycastTarget = false;
            return rt;
        }

        private Text AddText(RectTransform parent, string text, int size, FontStyle style, Color color, Vector2 pos, Vector2 box)
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
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>Yumuşak kenarlı daire (9-dilim için kenar payı ayarlı; kartlarda yuvarlak köşe olarak da kullanılır).</summary>
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
    }
}
