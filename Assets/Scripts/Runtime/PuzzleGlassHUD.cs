using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace PuzzleGlass
{
    /// <summary>
    /// Clean casual-mobile Canvas HUD for PuzzleGlass:
    /// - Top bar: Sound mute/unmute button, LEVEL X badge with level step arrows, Restart button
    /// - Bottom bar: Salla (Shake) action pill button with remaining charge counter
    /// - Level Clear / Fail feedback cards
    /// All pill backgrounds & crisp icons are generated procedurally with anti-aliased SDFs.
    /// </summary>
    [DefaultExecutionOrder(30)]
    public class PuzzleGlassHUD : MonoBehaviour
    {
        public static PuzzleGlassHUD Instance { get; private set; }

        private Canvas _canvas;
        private Text _levelTitleText;
        private Text _levelSubText;
        private Text _shakeCountText;
        private Button _shakeButton;
        private Image _shakeButtonImg;
        private Image _soundIconImg;
        private GameObject _resultOverlay;
        private Text _resultTitleText;
        private Text _resultBodyText;
        private Button _resultActionButton;
        private Text _resultActionLabel;

        private Sprite _roundedPillSprite;
        private Sprite _roundedCircleSprite;
        private Sprite _iconSoundOn;
        private Sprite _iconSoundOff;
        private Sprite _iconRestart;
        private Sprite _iconShake;
        private Font _uiFont;

        private int _lastShownLevel = -1;
        private int _lastShownShakes = -1;
        private bool _lastMutedState = false;
        private bool _lastCompleted = false;
        private bool _lastFailed = false;
        private int _lastRemainingBalls = -1;

        private static readonly Color ColCardCream    = new Color(1.000f, 0.988f, 0.965f, 0.98f);
        private static readonly Color ColPillWhite    = new Color(0.992f, 0.984f, 0.968f, 0.96f);
        private static readonly Color ColPillShadow   = new Color(0.565f, 0.560f, 0.720f, 0.42f);
        private static readonly Color ColIndigoDeep   = new Color(0.240f, 0.255f, 0.580f, 1.00f);
        private static readonly Color ColPeriwinkle   = new Color(0.416f, 0.451f, 0.925f, 1.00f);
        private static readonly Color ColPeriwinkleDk = new Color(0.285f, 0.314f, 0.745f, 1.00f);
        private static readonly Color ColAmberTop     = new Color(1.000f, 0.678f, 0.196f, 1.00f);
        private static readonly Color ColAmberBot     = new Color(0.845f, 0.486f, 0.060f, 1.00f);
        private static readonly Color ColGreenTop     = new Color(0.290f, 0.808f, 0.365f, 1.00f);
        private static readonly Color ColGreenBot     = new Color(0.180f, 0.595f, 0.240f, 1.00f);
        private static readonly Color ColDisabledBtn  = new Color(0.755f, 0.745f, 0.780f, 0.85f);

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
            if (_iconSoundOn == null)  _iconSoundOn  = CreateSpeakerIconSprite(muted: false);
            if (_iconSoundOff == null) _iconSoundOff = CreateSpeakerIconSprite(muted: true);
            if (_iconRestart == null)  _iconRestart  = CreateRestartIconSprite();
            if (_iconShake == null)    _iconShake    = CreateShakeIconSprite();
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
            BuildBottomBar(canvasObj.transform);
            BuildResultOverlay(canvasObj.transform);
        }

        private void BuildTopBar(Transform root)
        {
            RectTransform topBar = CreateRect("TopBar", root);
            topBar.anchorMin = new Vector2(0f, 1f);
            topBar.anchorMax = new Vector2(1f, 1f);
            topBar.pivot = new Vector2(0.5f, 1f);
            topBar.anchoredPosition = new Vector2(0f, -44f);
            topBar.sizeDelta = new Vector2(0f, 146f);

            // Left: Sound Toggle Button
            Button soundBtn = CreateCircleIconButton("Btn_Sound", topBar, new Vector2(-410f, 4f), 96f, _iconSoundOn, ColIndigoDeep, out _soundIconImg);
            soundBtn.onClick.AddListener(() =>
            {
                PuzzleSoundManager.GetOrCreate().ToggleMute();
                RefreshHud(force: true);
            });

            // Center: Level Pill Badge + subtle Previous / Next level arrows
            CreateMiniStepButton("Btn_PrevLevel", topBar, new Vector2(-235f, 4f), "<", () =>
            {
                PuzzleSoundManager.GetOrCreate().PlayUiClick();
                PuzzleGlassGameManager.Instance?.LoadPreviousLevel();
            });

            RectTransform levelPill = CreateRect("LevelPill", topBar);
            levelPill.anchorMin = new Vector2(0.5f, 0.5f);
            levelPill.anchorMax = new Vector2(0.5f, 0.5f);
            levelPill.anchoredPosition = new Vector2(0f, 4f);
            levelPill.sizeDelta = new Vector2(360f, 108f);

            // Soft 3D lower rim
            RectTransform pillLip = CreateRect("PillLip", levelPill);
            StretchFill(pillLip);
            pillLip.anchoredPosition = new Vector2(0f, -8f);
            Image lipImg = pillLip.gameObject.AddComponent<Image>();
            lipImg.sprite = _roundedPillSprite;
            lipImg.type = Image.Type.Sliced;
            lipImg.color = ColPeriwinkleDk;
            lipImg.raycastTarget = false;

            // Main periwinkle capsule
            RectTransform pillFace = CreateRect("PillFace", levelPill);
            StretchFill(pillFace);
            Image faceImg = pillFace.gameObject.AddComponent<Image>();
            faceImg.sprite = _roundedPillSprite;
            faceImg.type = Image.Type.Sliced;
            faceImg.color = ColPeriwinkle;
            faceImg.raycastTarget = false;

            _levelTitleText = CreateText("LevelTitle", pillFace, "LEVEL 1", 44, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            _levelTitleText.rectTransform.anchorMin = new Vector2(0f, 0.34f);
            _levelTitleText.rectTransform.anchorMax = new Vector2(1f, 0.96f);
            _levelTitleText.rectTransform.offsetMin = Vector2.zero;
            _levelTitleText.rectTransform.offsetMax = Vector2.zero;
            AddSoftTextShadow(_levelTitleText.gameObject, new Color(0.14f, 0.16f, 0.44f, 0.55f), new Vector2(0f, -2.5f));

            _levelSubText = CreateText("LevelSub", pillFace, "KOLAY", 21, FontStyle.Bold, new Color(0.90f, 0.93f, 1f, 0.92f), TextAnchor.MiddleCenter);
            _levelSubText.rectTransform.anchorMin = new Vector2(0f, 0.08f);
            _levelSubText.rectTransform.anchorMax = new Vector2(1f, 0.42f);
            _levelSubText.rectTransform.offsetMin = Vector2.zero;
            _levelSubText.rectTransform.offsetMax = Vector2.zero;

            CreateMiniStepButton("Btn_NextLevel", topBar, new Vector2(235f, 4f), ">", () =>
            {
                PuzzleSoundManager.GetOrCreate().PlayUiClick();
                PuzzleGlassGameManager.Instance?.LoadNextLevel();
            });

            // Right: Restart Button
            Button restartBtn = CreateCircleIconButton("Btn_Restart", topBar, new Vector2(410f, 4f), 96f, _iconRestart, ColIndigoDeep, out _);
            restartBtn.onClick.AddListener(() =>
            {
                PuzzleSoundManager.GetOrCreate().PlayUiClick();
                PuzzleGlassGameManager.Instance?.RestartCurrentPattern();
            });
        }

        private void BuildBottomBar(Transform root)
        {
            RectTransform bottomBar = CreateRect("BottomBar", root);
            bottomBar.anchorMin = new Vector2(0.5f, 0f);
            bottomBar.anchorMax = new Vector2(0.5f, 0f);
            bottomBar.pivot = new Vector2(0.5f, 0f);
            bottomBar.anchoredPosition = new Vector2(0f, 44f);
            bottomBar.sizeDelta = new Vector2(720f, 118f);

            RectTransform btnRect = CreateRect("Btn_Shake", bottomBar);
            btnRect.anchorMin = new Vector2(0.5f, 0.5f);
            btnRect.anchorMax = new Vector2(0.5f, 0.5f);
            btnRect.anchoredPosition = Vector2.zero;
            btnRect.sizeDelta = new Vector2(310f, 94f);

            // 3D bottom lip
            RectTransform lipRect = CreateRect("ShakeLip", btnRect);
            StretchFill(lipRect);
            lipRect.anchoredPosition = new Vector2(0f, -8f);
            Image lipImg = lipRect.gameObject.AddComponent<Image>();
            lipImg.sprite = _roundedPillSprite;
            lipImg.type = Image.Type.Sliced;
            lipImg.color = ColAmberBot;
            lipImg.raycastTarget = false;

            // Main face
            RectTransform faceRect = CreateRect("ShakeFace", btnRect);
            StretchFill(faceRect);
            _shakeButtonImg = faceRect.gameObject.AddComponent<Image>();
            _shakeButtonImg.sprite = _roundedPillSprite;
            _shakeButtonImg.type = Image.Type.Sliced;
            _shakeButtonImg.color = ColAmberTop;

            _shakeButton = btnRect.gameObject.AddComponent<Button>();
            _shakeButton.targetGraphic = _shakeButtonImg;
            _shakeButton.onClick.AddListener(() =>
            {
                PuzzleGlassGameManager.Instance?.TryShakeContainer();
                RefreshHud(force: true);
            });

            // Shake Icon
            RectTransform iconRect = CreateRect("ShakeIcon", faceRect);
            iconRect.anchorMin = new Vector2(0f, 0.5f);
            iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.anchoredPosition = new Vector2(54f, 0f);
            iconRect.sizeDelta = new Vector2(46f, 46f);
            Image iconImg = iconRect.gameObject.AddComponent<Image>();
            iconImg.sprite = _iconShake;
            iconImg.color = Color.white;
            iconImg.raycastTarget = false;

            // "SALLA" Label
            Text label = CreateText("ShakeLabel", faceRect, "SALLA", 36, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            label.rectTransform.anchorMin = new Vector2(0.22f, 0f);
            label.rectTransform.anchorMax = new Vector2(0.76f, 1f);
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
            AddSoftTextShadow(label.gameObject, new Color(0.45f, 0.22f, 0.0f, 0.45f), new Vector2(0f, -2f));

            // Remaining count badge circle
            RectTransform countBadge = CreateRect("CountBadge", faceRect);
            countBadge.anchorMin = new Vector2(1f, 0.5f);
            countBadge.anchorMax = new Vector2(1f, 0.5f);
            countBadge.anchoredPosition = new Vector2(-48f, 0f);
            countBadge.sizeDelta = new Vector2(54f, 54f);
            Image badgeImg = countBadge.gameObject.AddComponent<Image>();
            badgeImg.sprite = _roundedCircleSprite;
            badgeImg.color = new Color(1f, 1f, 1f, 0.96f);
            badgeImg.raycastTarget = false;

            _shakeCountText = CreateText("ShakeCount", countBadge, "3", 30, FontStyle.Bold, ColAmberBot, TextAnchor.MiddleCenter);
            StretchFill(_shakeCountText.rectTransform);
        }

        private void BuildResultOverlay(Transform root)
        {
            _resultOverlay = new GameObject("ResultOverlay");
            _resultOverlay.transform.SetParent(root, false);
            RectTransform ovRect = _resultOverlay.AddComponent<RectTransform>();
            StretchFill(ovRect);

            Image scrim = _resultOverlay.AddComponent<Image>();
            scrim.color = new Color(0.10f, 0.09f, 0.24f, 0.52f);

            RectTransform card = CreateRect("ResultCard", ovRect);
            card.anchorMin = new Vector2(0.5f, 0.5f);
            card.anchorMax = new Vector2(0.5f, 0.5f);
            card.anchoredPosition = Vector2.zero;
            card.sizeDelta = new Vector2(760f, 480f);

            // Outer lip shadow
            RectTransform cardLip = CreateRect("CardLip", card);
            StretchFill(cardLip);
            cardLip.anchoredPosition = new Vector2(0f, -12f);
            Image lipImg = cardLip.gameObject.AddComponent<Image>();
            lipImg.sprite = _roundedPillSprite;
            lipImg.type = Image.Type.Sliced;
            lipImg.color = ColPeriwinkleDk;

            // Card face
            RectTransform cardFace = CreateRect("CardFace", card);
            StretchFill(cardFace);
            Image faceImg = cardFace.gameObject.AddComponent<Image>();
            faceImg.sprite = _roundedPillSprite;
            faceImg.type = Image.Type.Sliced;
            faceImg.color = ColCardCream;

            _resultTitleText = CreateText("ResultTitle", cardFace, "TEBRİKLER!", 52, FontStyle.Bold, ColIndigoDeep, TextAnchor.MiddleCenter);
            _resultTitleText.rectTransform.anchorMin = new Vector2(0.08f, 0.58f);
            _resultTitleText.rectTransform.anchorMax = new Vector2(0.92f, 0.88f);
            _resultTitleText.rectTransform.offsetMin = Vector2.zero;
            _resultTitleText.rectTransform.offsetMax = Vector2.zero;

            _resultBodyText = CreateText("ResultBody", cardFace, "Level Tamamlandı!", 32, FontStyle.Normal, new Color(0.32f, 0.34f, 0.50f, 1f), TextAnchor.MiddleCenter);
            _resultBodyText.rectTransform.anchorMin = new Vector2(0.08f, 0.36f);
            _resultBodyText.rectTransform.anchorMax = new Vector2(0.92f, 0.58f);
            _resultBodyText.rectTransform.offsetMin = Vector2.zero;
            _resultBodyText.rectTransform.offsetMax = Vector2.zero;

            // Action Pill Button
            RectTransform actRect = CreateRect("ResultActionBtn", cardFace);
            actRect.anchorMin = new Vector2(0.5f, 0.16f);
            actRect.anchorMax = new Vector2(0.5f, 0.16f);
            actRect.anchoredPosition = Vector2.zero;
            actRect.sizeDelta = new Vector2(420f, 96f);

            RectTransform actLip = CreateRect("ActLip", actRect);
            StretchFill(actLip);
            actLip.anchoredPosition = new Vector2(0f, -8f);
            Image actLipImg = actLip.gameObject.AddComponent<Image>();
            actLipImg.sprite = _roundedPillSprite;
            actLipImg.type = Image.Type.Sliced;
            actLipImg.color = ColGreenBot;

            RectTransform actFace = CreateRect("ActFace", actRect);
            StretchFill(actFace);
            Image actFaceImg = actFace.gameObject.AddComponent<Image>();
            actFaceImg.sprite = _roundedPillSprite;
            actFaceImg.type = Image.Type.Sliced;
            actFaceImg.color = ColGreenTop;

            _resultActionButton = actRect.gameObject.AddComponent<Button>();
            _resultActionButton.targetGraphic = actFaceImg;

            _resultActionLabel = CreateText("ResultActionLabel", actFace, "SONRAKİ LEVEL", 34, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            StretchFill(_resultActionLabel.rectTransform);
            AddSoftTextShadow(_resultActionLabel.gameObject, new Color(0f, 0.25f, 0.05f, 0.45f), new Vector2(0f, -2f));

            _resultOverlay.SetActive(false);
        }

        private void RefreshHud(bool force)
        {
            var gm = PuzzleGlassGameManager.Instance;
            int levelNum = (gm != null && gm.currentLevel != null) ? gm.currentLevel.levelNumber : 1;
            int shakes = (gm != null) ? Mathf.Max(0, gm.remainingShakes) : 0;
            bool muted = PuzzleSoundManager.Instance != null && PuzzleSoundManager.Instance.IsMuted;
            bool completed = gm != null && gm.isLevelCompleted;
            bool failed = gm != null && gm.isLevelFailed;
            int remainingBalls = (BallObjectPool.Instance != null && BallObjectPool.Instance.ActiveBalls != null)
                ? BallObjectPool.Instance.ActiveBalls.Count
                : 0;

            if (force || levelNum != _lastShownLevel || remainingBalls != _lastRemainingBalls)
            {
                _lastShownLevel = levelNum;
                _lastRemainingBalls = remainingBalls;
                if (_levelTitleText != null) _levelTitleText.text = $"LEVEL {levelNum}";

                if (_levelSubText != null)
                {
                    string diffStr = GetDifficultyTurkish(gm != null && gm.currentLevel != null ? gm.currentLevel.difficulty : LevelDifficulty.Easy);
                    _levelSubText.text = remainingBalls > 0 ? $"{diffStr} • {remainingBalls} TOP" : diffStr;
                }
            }

            if (force || shakes != _lastShownShakes)
            {
                _lastShownShakes = shakes;
                if (_shakeCountText != null) _shakeCountText.text = shakes.ToString();
                if (_shakeButton != null) _shakeButton.interactable = shakes > 0;
                if (_shakeButtonImg != null) _shakeButtonImg.color = shakes > 0 ? ColAmberTop : ColDisabledBtn;
            }

            if (force || muted != _lastMutedState)
            {
                _lastMutedState = muted;
                if (_soundIconImg != null)
                {
                    _soundIconImg.sprite = muted ? _iconSoundOff : _iconSoundOn;
                    _soundIconImg.color = muted ? new Color(0.86f, 0.30f, 0.32f, 1f) : ColIndigoDeep;
                }
            }

            if (force || completed != _lastCompleted || failed != _lastFailed)
            {
                _lastCompleted = completed;
                _lastFailed = failed;

                if (_resultOverlay != null)
                {
                    bool show = completed || failed;
                    _resultOverlay.SetActive(show);

                    if (completed && gm != null)
                    {
                        _resultTitleText.text = "TEBRİKLER!";
                        _resultBodyText.text = $"Level {levelNum} başarıyla tamamlandı!";
                        _resultActionLabel.text = "SONRAKİ LEVEL";
                        if (_resultActionButton != null)
                        {
                            _resultActionButton.onClick.RemoveAllListeners();
                            _resultActionButton.onClick.AddListener(() =>
                            {
                                PuzzleSoundManager.GetOrCreate().PlayUiClick();
                                gm.CancelInvoke(nameof(PuzzleGlassGameManager.LoadNextLevel));
                                gm.LoadNextLevel();
                            });
                        }
                    }
                    else if (failed && gm != null)
                    {
                        _resultTitleText.text = "HAMLE KALMADI";
                        _resultBodyText.text = "Tekrar deneyerek kutuları sırayla açabilirsin.";
                        _resultActionLabel.text = "TEKRAR DENE";
                        if (_resultActionButton != null)
                        {
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

        private static string GetDifficultyTurkish(LevelDifficulty d)
        {
            switch (d)
            {
                case LevelDifficulty.Easy:   return "KOLAY";
                case LevelDifficulty.Medium: return "ORTA";
                case LevelDifficulty.Hard:   return "ZOR";
                case LevelDifficulty.Boss:   return "BOSS";
                default:                     return "KLASİK";
            }
        }

        private Button CreateCircleIconButton(string name, Transform parent, Vector2 pos, float size, Sprite icon, Color iconTint, out Image iconImg)
        {
            RectTransform btnRect = CreateRect(name, parent);
            btnRect.anchorMin = new Vector2(0.5f, 0.5f);
            btnRect.anchorMax = new Vector2(0.5f, 0.5f);
            btnRect.anchoredPosition = pos;
            btnRect.sizeDelta = new Vector2(size, size);

            RectTransform lipRect = CreateRect("Lip", btnRect);
            StretchFill(lipRect);
            lipRect.anchoredPosition = new Vector2(0f, -6f);
            Image lip = lipRect.gameObject.AddComponent<Image>();
            lip.sprite = _roundedCircleSprite;
            lip.color = ColPillShadow;
            lip.raycastTarget = false;

            RectTransform faceRect = CreateRect("Face", btnRect);
            StretchFill(faceRect);
            Image face = faceRect.gameObject.AddComponent<Image>();
            face.sprite = _roundedCircleSprite;
            face.color = ColPillWhite;

            Button btn = btnRect.gameObject.AddComponent<Button>();
            btn.targetGraphic = face;

            RectTransform icoRect = CreateRect("Icon", faceRect);
            icoRect.anchorMin = new Vector2(0.5f, 0.5f);
            icoRect.anchorMax = new Vector2(0.5f, 0.5f);
            icoRect.anchoredPosition = Vector2.zero;
            icoRect.sizeDelta = new Vector2(size * 0.54f, size * 0.54f);
            iconImg = icoRect.gameObject.AddComponent<Image>();
            iconImg.sprite = icon;
            iconImg.color = iconTint;
            iconImg.raycastTarget = false;

            return btn;
        }

        private void CreateMiniStepButton(string name, Transform parent, Vector2 pos, string symbol, UnityEngine.Events.UnityAction onClick)
        {
            RectTransform rect = CreateRect(name, parent);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = pos;
            rect.sizeDelta = new Vector2(62f, 62f);

            Image bg = rect.gameObject.AddComponent<Image>();
            bg.sprite = _roundedCircleSprite;
            bg.color = new Color(1f, 1f, 1f, 0.80f);

            Button btn = rect.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(onClick);

            Text txt = CreateText("Label", rect, symbol, 34, FontStyle.Bold, ColIndigoDeep, TextAnchor.MiddleCenter);
            StretchFill(txt.rectTransform);
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

        // ==================== PROCEDURAL ANTI-ALIASED UI SPRITES ====================

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

                    // Speaker body box on left + trapezoidal cone
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
                    float ang = Mathf.Atan2(v, u) * Mathf.Rad2Deg; // -180..180
                    bool inRing = Mathf.Abs(r - 0.27f) < 0.048f && !(ang > 20f && ang < 95f);

                    // Arrowhead near top-right (ang ~ 20 deg)
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

        private static Sprite CreateShakeIconSprite()
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

                    // Tilted mini cup/tray rounded outline + side shake arcs
                    float ru = u * 0.94f - v * 0.34f;
                    float rv = u * 0.34f + v * 0.94f;
                    float boxD = Mathf.Max(Mathf.Abs(ru) - 0.18f, Mathf.Abs(rv) - 0.15f);
                    bool inCup = Mathf.Abs(boxD) < 0.042f;

                    float dLeft  = Mathf.Abs(Mathf.Sqrt((u + 0.14f) * (u + 0.14f) + v * v) - 0.22f);
                    float dRight = Mathf.Abs(Mathf.Sqrt((u - 0.14f) * (u - 0.14f) + v * v) - 0.22f);
                    bool leftArc  = u < -0.25f && Mathf.Abs(v) < 0.19f && dLeft < 0.032f;
                    bool rightArc = u >  0.25f && Mathf.Abs(v) < 0.19f && dRight < 0.032f;

                    float a = (inCup || leftArc || rightArc) ? 1f : 0f;
                    pixels[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
        }
    }
}
