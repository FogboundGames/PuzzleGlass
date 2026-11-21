using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DG.Tweening;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace PuzzleGlass
{
    /// <summary>
    /// Clean casual-mobile Canvas HUD for PuzzleGlass styled with the Violet Theme UI pack
    /// harmonized to the game's pastel lavender/indigo palette:
    /// - Top bar: Sound toggle, LEVEL X badge with dev step arrows, Moves pill, Restart button
    /// - Level Clear / Fail feedback cards with Violet Theme panels, icons & tactile buttons
    /// </summary>
    [DefaultExecutionOrder(30)]
    public class PuzzleGlassHUD : MonoBehaviour
    {
        public static PuzzleGlassHUD Instance { get; private set; }

        [Header("UI Theme (Optional Override)")]
        [SerializeField] private PuzzleGlassUITheme uiTheme;

        private PuzzleGlassUITheme _theme;
        private Canvas _canvas;
        private Text _levelTitleText;
        private Text _levelSubText;
        private Image _soundIconImg;

        private GameObject _resultOverlay;
        private RectTransform _resultCardRect;
        private Text _resultTitleText;
        private Text _resultBodyText;
        private GameObject _winStarsGroup;
        private GameObject _failBadgeGroup;
        private Button _resultActionButton;
        private Image _resultActionFaceImg;
        private Image _resultActionIconImg;
        private Text _resultActionLabel;

        private Sprite _roundedPillSprite;
        private Sprite _roundedCircleSprite;
        private Sprite _iconSoundOn;
        private Sprite _iconSoundOff;
        private Sprite _iconRestart;
        private Font _uiFont;

        private int _lastShownLevel = -1;
        private bool _lastMutedState = false;
        private bool _lastCompleted = false;
        private bool _lastFailed = false;
        private bool _wasOverlayVisible = false;

        private static readonly Color ColCardCream    = new Color(1.000f, 0.992f, 0.980f, 0.99f);
        private static readonly Color ColPillWhite    = new Color(0.992f, 0.988f, 0.998f, 0.98f);
        private static readonly Color ColPillShadow   = new Color(0.565f, 0.560f, 0.720f, 0.42f);
        private static readonly Color ColSoftDrop     = new Color(0.140f, 0.110f, 0.360f, 0.28f);
        private static readonly Color ColIndigoDeep   = new Color(0.240f, 0.255f, 0.580f, 1.00f);
        private static readonly Color ColPeriwinkle   = new Color(0.416f, 0.451f, 0.925f, 1.00f);
        private static readonly Color ColPeriwinkleDk = new Color(0.285f, 0.314f, 0.745f, 1.00f);
        private static readonly Color ColGreenTop     = new Color(0.290f, 0.808f, 0.365f, 1.00f);
        private static readonly Color ColSubtleViolet = new Color(0.935f, 0.920f, 1.000f, 0.95f);
        private static readonly Color ColBodySlate    = new Color(0.300f, 0.315f, 0.500f, 1.00f);
        private static readonly Color ColFailCoral    = new Color(0.920f, 0.300f, 0.360f, 1.00f);

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            if (!Application.isPlaying) return;
            EnsureEventSystem();
            EnsureSpritesAndFont();
            BuildHudCanvas();
            RefreshHud(force: true);
        }

        private void Update()
        {
            if (!Application.isPlaying || _canvas == null) return;
            RefreshHud(force: false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void EnsureEventSystem()
        {
            EventSystem es = FindFirstObjectByType<EventSystem>();
            if (es == null)
            {
                GameObject esObj = new GameObject("EventSystem");
                es = esObj.AddComponent<EventSystem>();
            }
#if ENABLE_INPUT_SYSTEM
            if (es.GetComponent<InputSystemUIInputModule>() == null)
            {
                var legacy = es.GetComponent<StandaloneInputModule>();
                if (legacy != null) Destroy(legacy);
                es.gameObject.AddComponent<InputSystemUIInputModule>();
            }
#else
            if (es.GetComponent<StandaloneInputModule>() == null)
            {
                es.gameObject.AddComponent<StandaloneInputModule>();
            }
#endif
        }

        private void EnsureSpritesAndFont()
        {
            if (_theme == null)
            {
                _theme = uiTheme != null ? uiTheme : PuzzleGlassUITheme.Load();
            }

            if (_uiFont == null)
            {
                _uiFont = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI Semibold", "Segoe UI", "Arial" }, 40);
                if (_uiFont == null) _uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (_uiFont == null) _uiFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            if (_roundedPillSprite == null)
            {
                _roundedPillSprite = CreateRoundedRectSprite(96, 96, 30f, new Vector4(34, 34, 34, 34));
            }
            if (_roundedCircleSprite == null)
            {
                _roundedCircleSprite = CreateRoundedRectSprite(96, 96, 47f, Vector4.zero);
            }
            if (_iconSoundOn == null)  _iconSoundOn  = (_theme != null && _theme.iconSoundOn != null)  ? _theme.iconSoundOn  : CreateSpeakerIconSprite(muted: false);
            if (_iconSoundOff == null) _iconSoundOff = (_theme != null && _theme.iconSoundOff != null) ? _theme.iconSoundOff : CreateSpeakerIconSprite(muted: true);
            if (_iconRestart == null)  _iconRestart  = (_theme != null && _theme.iconRestart != null)  ? _theme.iconRestart  : CreateRestartIconSprite();
        }

        private void BuildHudCanvas()
        {
            if (_canvas != null) return;

            GameObject canvasObj = new GameObject("PuzzleGlassHUD_Canvas");
            canvasObj.transform.SetParent(transform, false);

            _canvas = canvasObj.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 50;

            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            canvasObj.AddComponent<GraphicRaycaster>();

            BuildTopBar(canvasObj.transform);
            BuildResultOverlay(canvasObj.transform);
        }

        private void BuildTopBar(Transform root)
        {
            RectTransform topBar = CreateRect("TopBar", root);
            topBar.anchorMin = new Vector2(0f, 1f);
            topBar.anchorMax = new Vector2(1f, 1f);
            topBar.pivot = new Vector2(0.5f, 1f);
            topBar.anchoredPosition = new Vector2(0f, -64f);
            topBar.sizeDelta = new Vector2(0f, 110f);

            Sprite sqViolet = _theme != null ? _theme.smallSquareViolet : null;
            Sprite sqBlue = _theme != null ? _theme.smallSquareBlue : null;
            Sprite shadowSpr = (_theme != null && _theme.roundedWhite80 != null) ? _theme.roundedWhite80 : _roundedPillSprite;
            bool usingVioletSquare = sqViolet != null;

            // Left: Sound Toggle Button
            Button soundBtn = CreateSquareIconButton(
                "Btn_Sound", topBar, new Vector2(-416f, 0f), 74f,
                sqViolet, shadowSpr, _iconSoundOn,
                usingVioletSquare ? Color.white : ColIndigoDeep,
                out _soundIconImg);
            soundBtn.onClick.AddListener(() =>
            {
                PunchButton(soundBtn.transform);
                PuzzleSoundManager.GetOrCreate().ToggleMute();
                RefreshHud(force: true);
            });

            // (< ve > level atlama butonları sadece test içindir: editörde ve Development Build'de görünür)
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Sprite leftIcon = _theme != null ? _theme.iconLeft : null;
            Sprite rightIcon = _theme != null ? _theme.iconRight : null;
            CreateMiniStepButton("Btn_PrevLevel", topBar, new Vector2(-198f, 0f), sqBlue, shadowSpr, leftIcon, "<", () =>
            {
                PuzzleSoundManager.GetOrCreate().PlayUiClick();
                PuzzleGlassGameManager.Instance?.LoadPreviousLevel();
            });
#endif

            // Center: Level Pill Banner
            RectTransform levelPill = CreateRect("LevelPill", topBar);
            levelPill.anchorMin = new Vector2(0.5f, 0.5f);
            levelPill.anchorMax = new Vector2(0.5f, 0.5f);
            levelPill.anchoredPosition = new Vector2(0f, 0f);
            levelPill.sizeDelta = new Vector2(280f, 80f);

            Sprite levelBg = _theme != null ? _theme.buttonViolet : null;
            if (levelBg != null)
            {
                RectTransform dropRt = CreateRect("PillShadow", levelPill);
                StretchFill(dropRt);
                dropRt.anchoredPosition = new Vector2(0f, -7f);
                Image dropImg = dropRt.gameObject.AddComponent<Image>();
                dropImg.sprite = shadowSpr;
                dropImg.type = Image.Type.Sliced;
                dropImg.color = ColSoftDrop;
                dropImg.raycastTarget = false;

                RectTransform faceRt = CreateRect("PillFace", levelPill);
                StretchFill(faceRt);
                Image faceImg = faceRt.gameObject.AddComponent<Image>();
                faceImg.sprite = levelBg;
                faceImg.type = Image.Type.Sliced;
                faceImg.pixelsPerUnitMultiplier = 1.40f;
                faceImg.color = Color.white;
                faceImg.raycastTarget = false;
            }
            else
            {
                RectTransform pillLip = CreateRect("PillLip", levelPill);
                StretchFill(pillLip);
                pillLip.anchoredPosition = new Vector2(0f, -6f);
                Image lipImg = pillLip.gameObject.AddComponent<Image>();
                lipImg.sprite = _roundedPillSprite;
                lipImg.type = Image.Type.Sliced;
                lipImg.color = ColPeriwinkleDk;
                lipImg.raycastTarget = false;

                RectTransform pillFace = CreateRect("PillFace", levelPill);
                StretchFill(pillFace);
                Image faceImg = pillFace.gameObject.AddComponent<Image>();
                faceImg.sprite = _roundedPillSprite;
                faceImg.type = Image.Type.Sliced;
                faceImg.color = ColPeriwinkle;
                faceImg.raycastTarget = false;
            }

            _levelTitleText = CreateText("LevelTitle", levelPill, "LEVEL 1", 32, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            _levelTitleText.rectTransform.anchorMin = new Vector2(0.04f, 0.36f);
            _levelTitleText.rectTransform.anchorMax = new Vector2(0.96f, 0.92f);
            _levelTitleText.rectTransform.offsetMin = Vector2.zero;
            _levelTitleText.rectTransform.offsetMax = Vector2.zero;
            AddSoftTextShadow(_levelTitleText.gameObject, new Color(0.14f, 0.10f, 0.36f, 0.58f), new Vector2(0f, -2f));

            _levelSubText = CreateText("LevelSub", levelPill, "EASY", 16, FontStyle.Bold, ColSubtleViolet, TextAnchor.MiddleCenter);
            _levelSubText.rectTransform.anchorMin = new Vector2(0.04f, 0.12f);
            _levelSubText.rectTransform.anchorMax = new Vector2(0.96f, 0.42f);
            _levelSubText.rectTransform.offsetMin = Vector2.zero;
            _levelSubText.rectTransform.offsetMax = Vector2.zero;
            AddSoftTextShadow(_levelSubText.gameObject, new Color(0.14f, 0.10f, 0.36f, 0.40f), new Vector2(0f, -1.5f));

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CreateMiniStepButton("Btn_NextLevel", topBar, new Vector2(198f, 0f), sqBlue, shadowSpr, rightIcon, ">", () =>
            {
                PuzzleSoundManager.GetOrCreate().PlayUiClick();
                PuzzleGlassGameManager.Instance?.LoadNextLevel();
            });
#endif

            // Right: Restart Button
            Button restartBtn = CreateSquareIconButton(
                "Btn_Restart", topBar, new Vector2(416f, 0f), 74f,
                sqViolet, shadowSpr, _iconRestart,
                usingVioletSquare ? Color.white : ColIndigoDeep,
                out _);
            restartBtn.onClick.AddListener(() =>
            {
                PunchButton(restartBtn.transform);
                PuzzleSoundManager.GetOrCreate().PlayUiClick();
                PuzzleGlassGameManager.Instance?.RestartCurrentPattern();
            });
        }

        private void BuildResultOverlay(Transform root)
        {
            _resultOverlay = new GameObject("ResultOverlay");
            _resultOverlay.transform.SetParent(root, false);
            RectTransform ovRect = _resultOverlay.AddComponent<RectTransform>();
            StretchFill(ovRect);

            Image scrim = _resultOverlay.AddComponent<Image>();
            scrim.color = new Color(0.11f, 0.09f, 0.25f, 0.56f);

            _resultCardRect = CreateRect("ResultCard", ovRect);
            _resultCardRect.anchorMin = new Vector2(0.5f, 0.5f);
            _resultCardRect.anchorMax = new Vector2(0.5f, 0.5f);
            _resultCardRect.anchoredPosition = Vector2.zero;
            _resultCardRect.sizeDelta = new Vector2(780f, 560f);

            Sprite panelSprite = _theme != null ? _theme.whitePanelViolet : null;
            Sprite pillBg = (_theme != null && _theme.roundedWhite80 != null) ? _theme.roundedWhite80 : _roundedPillSprite;

            // Outer soft shadow
            RectTransform cardShadow = CreateRect("CardShadow", _resultCardRect);
            StretchFill(cardShadow);
            cardShadow.anchoredPosition = new Vector2(0f, -14f);
            Image shadowImg = cardShadow.gameObject.AddComponent<Image>();
            shadowImg.sprite = pillBg;
            shadowImg.type = Image.Type.Sliced;
            shadowImg.color = new Color(0.08f, 0.06f, 0.22f, 0.36f);
            shadowImg.raycastTarget = false;

            // Card face (Violet Style 2 White Panel: mor başlık şeridi + temiz beyaz gövde)
            RectTransform cardFace = CreateRect("CardFace", _resultCardRect);
            StretchFill(cardFace);
            Image faceImg = cardFace.gameObject.AddComponent<Image>();
            if (panelSprite != null)
            {
                faceImg.sprite = panelSprite;
                faceImg.type = Image.Type.Sliced;
                faceImg.pixelsPerUnitMultiplier = 1.75f; // ~111px header şeridi (780x560 kartta dengeli oran)
                faceImg.color = Color.white;
            }
            else
            {
                faceImg.sprite = _roundedPillSprite;
                faceImg.type = Image.Type.Sliced;
                faceImg.color = ColCardCream;
            }

            // Üst Dekoratif Yıldızlar (Win)
            _winStarsGroup = CreateWinStarsHeader(cardFace);

            // Üst Uyarı Rozeti (Fail)
            _failBadgeGroup = CreateFailBadgeHeader(cardFace, pillBg);

            // Kart Başlığı (mor header şeridinin içine oturur)
            Color titleColor = panelSprite != null ? Color.white : ColIndigoDeep;
            _resultTitleText = CreateText("ResultTitle", cardFace, "WELL DONE!", 44, FontStyle.Bold, titleColor, TextAnchor.MiddleCenter);
            _resultTitleText.rectTransform.anchorMin = new Vector2(0.08f, 0.80f);
            _resultTitleText.rectTransform.anchorMax = new Vector2(0.92f, 0.97f);
            _resultTitleText.rectTransform.offsetMin = Vector2.zero;
            _resultTitleText.rectTransform.offsetMax = Vector2.zero;
            if (panelSprite != null)
            {
                AddSoftTextShadow(_resultTitleText.gameObject, new Color(0.15f, 0.08f, 0.38f, 0.55f), new Vector2(0f, -3f));
            }

            // Kart Açıklama Metni
            _resultBodyText = CreateText("ResultBody", cardFace, "Level Complete!", 32, FontStyle.Bold, ColBodySlate, TextAnchor.MiddleCenter);
            _resultBodyText.rectTransform.anchorMin = new Vector2(0.10f, 0.40f);
            _resultBodyText.rectTransform.anchorMax = new Vector2(0.90f, 0.72f);
            _resultBodyText.rectTransform.offsetMin = Vector2.zero;
            _resultBodyText.rectTransform.offsetMax = Vector2.zero;
            _resultBodyText.horizontalOverflow = HorizontalWrapMode.Wrap;

            // Aksiyon Butonu (NEXT LEVEL / TRY AGAIN)
            RectTransform actRect = CreateRect("ResultActionBtn", cardFace);
            actRect.anchorMin = new Vector2(0.5f, 0.18f);
            actRect.anchorMax = new Vector2(0.5f, 0.18f);
            actRect.anchoredPosition = Vector2.zero;
            actRect.sizeDelta = new Vector2(352f, 86f);

            RectTransform actShadow = CreateRect("ActShadow", actRect);
            StretchFill(actShadow);
            actShadow.anchoredPosition = new Vector2(0f, -6f);
            Image actShadowImg = actShadow.gameObject.AddComponent<Image>();
            actShadowImg.sprite = pillBg;
            actShadowImg.type = Image.Type.Sliced;
            actShadowImg.color = ColSoftDrop;
            actShadowImg.raycastTarget = false;

            RectTransform actFace = CreateRect("ActFace", actRect);
            StretchFill(actFace);
            _resultActionFaceImg = actFace.gameObject.AddComponent<Image>();
            Sprite btnGreen = _theme != null ? _theme.buttonGreen : null;
            _resultActionFaceImg.sprite = btnGreen != null ? btnGreen : _roundedPillSprite;
            _resultActionFaceImg.type = Image.Type.Sliced;
            _resultActionFaceImg.pixelsPerUnitMultiplier = btnGreen != null ? 1.32f : 1f;
            _resultActionFaceImg.color = btnGreen != null ? Color.white : ColGreenTop;

            _resultActionButton = actRect.gameObject.AddComponent<Button>();
            _resultActionButton.targetGraphic = _resultActionFaceImg;

            _resultActionLabel = CreateText("ResultActionLabel", actFace, "NEXT LEVEL", 30, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            _resultActionLabel.rectTransform.anchorMin = new Vector2(0.08f, 0.08f);
            _resultActionLabel.rectTransform.anchorMax = new Vector2(0.80f, 0.96f);
            _resultActionLabel.rectTransform.offsetMin = Vector2.zero;
            _resultActionLabel.rectTransform.offsetMax = Vector2.zero;
            AddSoftTextShadow(_resultActionLabel.gameObject, new Color(0f, 0.15f, 0.05f, 0.48f), new Vector2(0f, -2.5f));

            RectTransform actIcoRt = CreateRect("ActIcon", actFace);
            actIcoRt.anchorMin = new Vector2(0.84f, 0.52f);
            actIcoRt.anchorMax = new Vector2(0.84f, 0.52f);
            actIcoRt.anchoredPosition = Vector2.zero;
            actIcoRt.sizeDelta = new Vector2(32f, 32f);
            _resultActionIconImg = actIcoRt.gameObject.AddComponent<Image>();
            _resultActionIconImg.preserveAspect = true;
            _resultActionIconImg.color = Color.white;
            _resultActionIconImg.raycastTarget = false;
            Sprite rightIco = _theme != null ? _theme.iconRight : null;
            _resultActionIconImg.sprite = rightIco;
            _resultActionIconImg.enabled = rightIco != null;

            _resultOverlay.SetActive(false);
        }

        private GameObject CreateWinStarsHeader(Transform cardFace)
        {
            GameObject group = new GameObject("WinStars");
            group.transform.SetParent(cardFace, false);
            RectTransform rt = group.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, 48f);
            rt.sizeDelta = new Vector2(320f, 100f);

            Sprite starSpr = _theme != null ? (_theme.coloredStar != null ? _theme.coloredStar : _theme.iconStar) : null;
            if (starSpr == null)
            {
                group.SetActive(false);
                return group;
            }

            CreateHeaderStar(rt, "StarLeft", starSpr, new Vector2(-88f, -12f), 74f, 15f);
            CreateHeaderStar(rt, "StarCenter", starSpr, new Vector2(0f, 8f), 94f, 0f);
            CreateHeaderStar(rt, "StarRight", starSpr, new Vector2(88f, -12f), 74f, -15f);
            return group;
        }

        private static void CreateHeaderStar(Transform parent, string name, Sprite spr, Vector2 pos, float size, float rotZ)
        {
            RectTransform sRt = CreateRect(name, parent);
            sRt.anchorMin = sRt.anchorMax = new Vector2(0.5f, 0.5f);
            sRt.anchoredPosition = pos;
            sRt.sizeDelta = new Vector2(size, size);
            sRt.localRotation = Quaternion.Euler(0f, 0f, rotZ);

            Image img = sRt.gameObject.AddComponent<Image>();
            img.sprite = spr;
            img.preserveAspect = true;
            img.raycastTarget = false;

            Shadow sh = sRt.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0.14f, 0.08f, 0.34f, 0.40f);
            sh.effectDistance = new Vector2(0f, -5f);
        }

        private GameObject CreateFailBadgeHeader(Transform cardFace, Sprite pillBg)
        {
            GameObject group = new GameObject("FailBadge");
            group.transform.SetParent(cardFace, false);
            RectTransform rt = group.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, 42f);
            rt.sizeDelta = new Vector2(92f, 92f);

            Image bg = group.AddComponent<Image>();
            bg.sprite = pillBg;
            bg.type = Image.Type.Sliced;
            bg.color = ColFailCoral;
            bg.raycastTarget = false;

            Shadow sh = group.AddComponent<Shadow>();
            sh.effectColor = new Color(0.14f, 0.08f, 0.34f, 0.42f);
            sh.effectDistance = new Vector2(0f, -5f);

            Sprite warnSpr = _theme != null ? _theme.iconWarning : null;
            if (warnSpr != null)
            {
                RectTransform icoRt = CreateRect("WarnIcon", rt);
                icoRt.anchorMin = icoRt.anchorMax = new Vector2(0.5f, 0.5f);
                icoRt.anchoredPosition = Vector2.zero;
                icoRt.sizeDelta = new Vector2(54f, 54f);
                Image icoImg = icoRt.gameObject.AddComponent<Image>();
                icoImg.sprite = warnSpr;
                icoImg.preserveAspect = true;
                icoImg.color = Color.white;
                icoImg.raycastTarget = false;
            }
            else
            {
                group.SetActive(false);
            }
            return group;
        }

        private void RefreshHud(bool force)
        {
            var gm = PuzzleGlassGameManager.Instance;
            int levelNum = (gm != null && gm.currentLevel != null) ? gm.currentLevel.levelNumber : 1;
            bool muted = PuzzleSoundManager.Instance != null && PuzzleSoundManager.Instance.IsMuted;
            bool completed = gm != null && gm.isLevelCompleted;
            bool failed = gm != null && gm.isLevelFailed;

            if (force || levelNum != _lastShownLevel)
            {
                // Level değişince üstteki LEVEL hapı kısa bir pop yapar (geçiş ekranı yerine tek level geri bildirimi)
                bool levelChanged = _lastShownLevel >= 0 && levelNum != _lastShownLevel;
                if (levelChanged && Application.isPlaying && _levelTitleText != null && _levelTitleText.transform.parent != null)
                {
                    Transform pill = _levelTitleText.transform.parent;
                    pill.DOKill(true);
                    pill.localScale = Vector3.one;
                    pill.DOPunchScale(new Vector3(0.18f, 0.18f, 0f), 0.34f, 6, 0.6f).SetLink(pill.gameObject);
                }
                _lastShownLevel = levelNum;
                if (_levelTitleText != null) _levelTitleText.text = $"LEVEL {levelNum}";

                if (_levelSubText != null)
                {
                    string diffStr = GetDifficultyLabel(gm != null && gm.currentLevel != null ? gm.currentLevel.difficulty : LevelDifficulty.Easy);
                    _levelSubText.text = diffStr;
                }
            }

            if (force || muted != _lastMutedState)
            {
                _lastMutedState = muted;
                if (_soundIconImg != null)
                {
                    _soundIconImg.sprite = muted ? _iconSoundOff : _iconSoundOn;
                    bool overViolet = _theme != null && _theme.smallSquareViolet != null;
                    _soundIconImg.color = muted
                        ? (overViolet ? new Color(1f, 0.72f, 0.76f, 1f) : new Color(0.86f, 0.30f, 0.32f, 1f))
                        : (overViolet ? Color.white : ColIndigoDeep);
                }
            }

            if (force || completed != _lastCompleted || failed != _lastFailed)
            {
                _lastCompleted = completed;
                _lastFailed = failed;

                if (_resultOverlay != null)
                {
                    // Top dökülmeli geçiş açıkken "Tebrikler" paneli gösterilmez; geçişin kendisi kutlama
                    bool pourTransition = gm != null && gm.useBallPourTransition;
                    bool show = (completed && !pourTransition) || failed;
                    _resultOverlay.SetActive(show);

                    if (show && !_wasOverlayVisible && _resultCardRect != null)
                    {
                        _resultCardRect.DOKill(true);
                        _resultCardRect.localScale = Vector3.one * 0.82f;
                        _resultCardRect.DOScale(1f, 0.30f).SetEase(Ease.OutBack).SetUpdate(true);
                    }
                    _wasOverlayVisible = show;

                    if (completed && gm != null)
                    {
                        if (_winStarsGroup != null) _winStarsGroup.SetActive(_theme != null && (_theme.coloredStar != null || _theme.iconStar != null));
                        if (_failBadgeGroup != null) _failBadgeGroup.SetActive(false);

                        _resultTitleText.text = "WELL DONE!";
                        _resultBodyText.text = $"Level {levelNum} complete!";

                        _resultActionLabel.text = "NEXT LEVEL";
                        ApplyActionButtonTheme(isWin: true);

                        if (_resultActionButton != null)
                        {
                            _resultActionButton.gameObject.SetActive(!gm.useBallPourTransition);
                            _resultActionButton.onClick.RemoveAllListeners();
                            _resultActionButton.onClick.AddListener(() =>
                            {
                                PuzzleSoundManager.GetOrCreate().PlayUiClick();
                                if (LevelTransitionController.IsTransitioning) return;
                                gm.CancelInvoke(nameof(PuzzleGlassGameManager.LoadNextLevel));
                                gm.LoadNextLevel();
                            });
                        }
                    }
                    else if (failed && gm != null)
                    {
                        if (_winStarsGroup != null) _winStarsGroup.SetActive(false);
                        if (_failBadgeGroup != null) _failBadgeGroup.SetActive(_theme != null && _theme.iconWarning != null);

                        _resultTitleText.text = "NO MOVES LEFT";
                        _resultBodyText.text = "Try again and place the boxes in a better order.";

                        _resultActionLabel.text = "TRY AGAIN";
                        ApplyActionButtonTheme(isWin: false);

                        if (_resultActionButton != null)
                        {
                            _resultActionButton.gameObject.SetActive(true);
                            _resultActionButton.onClick.RemoveAllListeners();
                            _resultActionButton.onClick.AddListener(() =>
                            {
                                PuzzleSoundManager.GetOrCreate().PlayUiClick();
                                gm.RestartCurrentPattern();
                            });
                        }
                    }
                }
            }
        }

        private void ApplyActionButtonTheme(bool isWin)
        {
            if (_resultActionFaceImg != null)
            {
                Sprite target = isWin
                    ? (_theme != null ? _theme.buttonGreen : null)
                    : (_theme != null ? (_theme.buttonViolet != null ? _theme.buttonViolet : _theme.buttonBlue) : null);

                if (target != null)
                {
                    _resultActionFaceImg.sprite = target;
                    _resultActionFaceImg.pixelsPerUnitMultiplier = 1.32f;
                    _resultActionFaceImg.color = Color.white;
                }
                else
                {
                    _resultActionFaceImg.sprite = _roundedPillSprite;
                    _resultActionFaceImg.pixelsPerUnitMultiplier = 1f;
                    _resultActionFaceImg.color = isWin ? ColGreenTop : ColPeriwinkle;
                }
            }

            if (_resultActionIconImg != null && _theme != null)
            {
                Sprite ico = isWin ? _theme.iconRight : _theme.iconRestart;
                _resultActionIconImg.sprite = ico;
                _resultActionIconImg.enabled = ico != null;
            }
        }

        private static string GetDifficultyLabel(LevelDifficulty d)
        {
            switch (d)
            {
                case LevelDifficulty.Easy:   return "EASY";
                case LevelDifficulty.Medium: return "MEDIUM";
                case LevelDifficulty.Hard:   return "HARD";
                case LevelDifficulty.Boss:   return "BOSS";
                default:                     return "CLASSIC";
            }
        }

        private static void PunchButton(Transform target)
        {
            if (target == null) return;
            target.DOKill(true);
            target.DOPunchScale(Vector3.one * -0.10f, 0.16f, 6, 0.5f).SetUpdate(true);
        }

        private Button CreateSquareIconButton(
            string name,
            Transform parent,
            Vector2 pos,
            float size,
            Sprite squareBg,
            Sprite shadowSpr,
            Sprite icon,
            Color iconTint,
            out Image iconImg)
        {
            RectTransform btnRect = CreateRect(name, parent);
            btnRect.anchorMin = new Vector2(0.5f, 0.5f);
            btnRect.anchorMax = new Vector2(0.5f, 0.5f);
            btnRect.anchoredPosition = pos;
            btnRect.sizeDelta = new Vector2(size, size);

            RectTransform lipRect = CreateRect("Shadow", btnRect);
            StretchFill(lipRect);
            lipRect.anchoredPosition = new Vector2(0f, -6f);
            Image lip = lipRect.gameObject.AddComponent<Image>();
            lip.sprite = squareBg != null ? shadowSpr : _roundedCircleSprite;
            lip.type = squareBg != null ? Image.Type.Sliced : Image.Type.Simple;
            lip.color = squareBg != null ? ColSoftDrop : ColPillShadow;
            lip.raycastTarget = false;

            RectTransform faceRect = CreateRect("Face", btnRect);
            StretchFill(faceRect);
            Image face = faceRect.gameObject.AddComponent<Image>();
            face.sprite = squareBg != null ? squareBg : _roundedCircleSprite;
            face.type = squareBg != null ? Image.Type.Sliced : Image.Type.Simple;
            face.pixelsPerUnitMultiplier = squareBg != null ? 1.45f : 1f;
            face.color = squareBg != null ? Color.white : ColPillWhite;

            Button btn = btnRect.gameObject.AddComponent<Button>();
            btn.targetGraphic = face;

            RectTransform icoRect = CreateRect("Icon", faceRect);
            icoRect.anchorMin = new Vector2(0.5f, 0.52f);
            icoRect.anchorMax = new Vector2(0.5f, 0.52f);
            icoRect.anchoredPosition = Vector2.zero;
            icoRect.sizeDelta = new Vector2(size * 0.46f, size * 0.46f);
            iconImg = icoRect.gameObject.AddComponent<Image>();
            iconImg.sprite = icon;
            iconImg.preserveAspect = true;
            iconImg.color = iconTint;
            iconImg.raycastTarget = false;

            return btn;
        }

        private void CreateMiniStepButton(
            string name,
            Transform parent,
            Vector2 pos,
            Sprite squareBg,
            Sprite shadowSpr,
            Sprite iconSprite,
            string fallbackSymbol,
            UnityEngine.Events.UnityAction onClick)
        {
            RectTransform rect = CreateRect(name, parent);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = pos;
            rect.sizeDelta = new Vector2(50f, 50f);

            if (squareBg != null)
            {
                RectTransform shRt = CreateRect("Shadow", rect);
                StretchFill(shRt);
                shRt.anchoredPosition = new Vector2(0f, -4f);
                Image shImg = shRt.gameObject.AddComponent<Image>();
                shImg.sprite = shadowSpr;
                shImg.type = Image.Type.Sliced;
                shImg.color = ColSoftDrop;
                shImg.raycastTarget = false;
            }

            Image bg = rect.gameObject.AddComponent<Image>();
            bg.sprite = squareBg != null ? squareBg : _roundedCircleSprite;
            bg.type = squareBg != null ? Image.Type.Sliced : Image.Type.Simple;
            bg.pixelsPerUnitMultiplier = squareBg != null ? 1.65f : 1f;
            bg.color = squareBg != null ? Color.white : new Color(1f, 1f, 1f, 0.85f);

            Button btn = rect.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() =>
            {
                PunchButton(rect);
                onClick?.Invoke();
            });

            if (iconSprite != null)
            {
                RectTransform icoRt = CreateRect("Icon", rect);
                icoRt.anchorMin = icoRt.anchorMax = new Vector2(0.5f, 0.53f);
                icoRt.anchoredPosition = Vector2.zero;
                icoRt.sizeDelta = new Vector2(20f, 20f);
                Image icoImg = icoRt.gameObject.AddComponent<Image>();
                icoImg.sprite = iconSprite;
                icoImg.preserveAspect = true;
                icoImg.color = squareBg != null ? Color.white : ColIndigoDeep;
                icoImg.raycastTarget = false;
            }
            else
            {
                Text txt = CreateText("Label", rect, fallbackSymbol, 26, FontStyle.Bold, ColIndigoDeep, TextAnchor.MiddleCenter);
                StretchFill(txt.rectTransform);
            }
        }

        private Text CreateText(string name, Transform parent, string content, int fontSize, FontStyle style, Color col, TextAnchor anchor)
        {
            RectTransform rect = CreateRect(name, parent);
            Text txt = rect.gameObject.AddComponent<Text>();
            txt.font = _uiFont;
            txt.text = content;
            txt.fontSize = fontSize;
            txt.fontStyle = style;
            txt.color = col;
            txt.alignment = anchor;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            txt.raycastTarget = false;
            return txt;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.AddComponent<RectTransform>();
        }

        private static void StretchFill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void AddSoftTextShadow(GameObject go, Color col, Vector2 dist)
        {
            Shadow sh = go.AddComponent<Shadow>();
            sh.effectColor = col;
            sh.effectDistance = dist;
        }

        // ==================== PROCEDURAL FALLBACK SPRITES ====================

        private static Sprite CreateRoundedRectSprite(int w, int h, float radius, Vector4 border)
        {
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color32[] pixels = new Color32[w * h];
            float halfW = w * 0.5f - 1.5f;
            float halfH = h * 0.5f - 1.5f;
            float r = Mathf.Min(radius, Mathf.Min(halfW, halfH));

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float px = Mathf.Abs(x + 0.5f - w * 0.5f) - (halfW - r);
                    float py = Mathf.Abs(y + 0.5f - h * 0.5f) - (halfH - r);
                    float qx = Mathf.Max(px, 0f);
                    float qy = Mathf.Max(py, 0f);
                    float dist = Mathf.Sqrt(qx * qx + qy * qy) + Mathf.Min(Mathf.Max(px, py), 0f) - r;
                    float alpha = Mathf.Clamp01(0.5f - dist);
                    byte a = (byte)Mathf.RoundToInt(alpha * 255f);
                    pixels[y * w + x] = new Color32(255, 255, 255, a);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        }

        private static Sprite CreateSpeakerIconSprite(bool muted)
        {
            const int S = 64;
            Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            Color32[] pixels = new Color32[S * S];

            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S;
                    float v = (y + 0.5f) / S;

                    bool inBox = u >= 0.16f && u <= 0.30f && v >= 0.38f && v <= 0.62f;
                    float coneHalfH = Mathf.Lerp(0.12f, 0.28f, Mathf.Clamp01((u - 0.28f) / 0.22f));
                    bool inCone = u >= 0.28f && u <= 0.50f && Mathf.Abs(v - 0.50f) <= coneHalfH;

                    float alpha = (inBox || inCone) ? 1f : 0f;

                    if (!muted)
                    {
                        float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.48f, 0.50f));
                        bool rightHalf = u > 0.54f && Mathf.Abs(v - 0.50f) < (u - 0.44f) * 1.1f;
                        if (rightHalf && (Mathf.Abs(d - 0.18f) < 0.032f || Mathf.Abs(d - 0.31f) < 0.032f))
                            alpha = 1f;
                    }
                    else
                    {
                        if (u > 0.56f && u < 0.88f && v > 0.34f && v < 0.66f)
                        {
                            float dx = Mathf.Abs((u - 0.72f) - (v - 0.50f));
                            float dy = Mathf.Abs((u - 0.72f) + (v - 0.50f));
                            if (dx < 0.034f || dy < 0.034f) alpha = 1f;
                        }
                    }

                    pixels[y * S + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
        }

        private static Sprite CreateRestartIconSprite()
        {
            const int S = 64;
            Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            Color32[] pixels = new Color32[S * S];

            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S - 0.5f;
                    float v = (y + 0.5f) / S - 0.5f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float ang = Mathf.Atan2(v, u) * Mathf.Rad2Deg;
                    bool inRing = Mathf.Abs(r - 0.27f) < 0.048f && !(ang > 20f && ang < 95f);

                    float au = u - 0.24f;
                    float av = v - 0.14f;
                    bool inArrow = au > -0.14f && au < 0.07f && av > -0.06f && av < 0.15f && (av - au * 0.4f) < 0.14f;

                    float a = (inRing || inArrow) ? 1f : 0f;
                    pixels[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
        }
    }
}
