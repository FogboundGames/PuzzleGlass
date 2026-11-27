using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace PuzzleGlass
{
    /// <summary>
    /// Clean full-screen celebratory "New Feature" progression overlay:
    /// - Translucent vibrant blue overlay so the cleared game board remains softly visible behind
    /// - Rotating radial sunburst, lively confetti shower, and large 3D mechanic silhouette-fill
    /// - Percentage sits centered inside the mechanic box visual using exact box-counter typography
    /// - Exact box-style TextMeshProUGUI typography ("New Feature" + percentage)
    /// - Tap anywhere after the reveal to continue to the next level
    /// </summary>
    public class FeatureUnlockShowcaseView : MonoBehaviour
    {
        private static readonly int RevealProgressId = Shader.PropertyToID("_RevealProgress");
        private static readonly int ContentBoundsId = Shader.PropertyToID("_ContentBounds");
        private static readonly int SilhouetteColorId = Shader.PropertyToID("_SilhouetteColor");

        private static readonly Color ColBackdropBlue = new Color(0.14f, 0.42f, 0.86f, 0.72f);
        private static readonly Color ColUnlockedGold = new Color(1.00f, 0.82f, 0.22f, 1f);
        private static readonly Color ColStepPending = new Color(0.08f, 0.18f, 0.42f, 0.62f);
        private static readonly Color ColStepFilled = new Color(0.56f, 0.96f, 1.00f, 1f);
        private static readonly Color ColStepNewGold = new Color(1.00f, 0.88f, 0.36f, 1f);

        private static readonly Color[] ConfettiPalette =
        {
            new Color(1.00f, 0.82f, 0.20f, 1f),
            new Color(0.28f, 0.90f, 0.42f, 1f),
            new Color(0.26f, 0.82f, 1.00f, 1f),
            new Color(1.00f, 0.38f, 0.56f, 1f),
            new Color(0.76f, 0.48f, 1.00f, 1f)
        };

        private struct ConfettiParticle
        {
            public RectTransform rt;
            public Image img;
            public Vector2 vel;
            public float rotSpeed;
            public float flutterPhase;
            public float flutterFreq;
            public float life;
            public float maxLife;
            public bool ambient;
            public bool isStar;
            public Vector2 baseSize;
        }

        private struct SparkleFx
        {
            public RectTransform rt;
            public Image img;
            public float phase;
            public float freq;
            public float baseScale;
            public float rotSpeed;
            public Color baseColor;
        }

        private Image _backdropImg;
        private Button _tapAnywhereBtn;
        private RectTransform _sunburstRt;
        private Image _sunburstImg;
        private RectTransform _sunburstSecondaryRt;
        private Image _sunburstSecondaryImg;
        private RectTransform _glowHaloRt;
        private Image _glowHaloImg;
        private RectTransform _glowCoreRt;
        private Image _glowCoreImg;
        private RectTransform _backRingRt;
        private Image _backRingImg;
        private RectTransform _confettiLayer;
        private RectTransform _celebrationLayer;
        private RawImage _mechanicPreview;
        private Material _revealMat;
        private TextMeshProUGUI _titleLabel;
        private TextMeshProUGUI _subtitleLabel;
        private TextMeshProUGUI _percentLabel;
        private RectTransform _stepRowRt;
        private Sprite _stepRoundedSprite;
        private readonly List<Image> _stepDots = new List<Image>();
        private TextMeshProUGUI _tapHintLabel;
        private RectTransform _unlockedBadgeRt;
        private TextMeshProUGUI _unlockedBadgeText;
        private Image _flashOverlay;

        private readonly List<ConfettiParticle> _confetti = new List<ConfettiParticle>();
        private readonly List<SparkleFx> _sparkles = new List<SparkleFx>();
        private readonly List<GameObject> _tempRings = new List<GameObject>();
        private Sequence _showSeq;
        private Sequence _unlockSeq;
        private System.Action _onNextClicked;
        private bool _canTapToContinue;
        private bool _nextConsumed;
        private bool _isUnlockedIdle;
        private int _shownPct = -1;
        private int _lastTickSoundPct = -100;
        private float _lastGlintSpawnTime = -10f;
        private float _ambientConfettiTimer;
        private float _glowAnimTime;
        private float _unlockIdleTime;

        public static FeatureUnlockShowcaseView Create(Transform parent, Font font, PuzzleGlassUITheme theme, Sprite roundedSprite)
        {
            var go = new GameObject("FeatureUnlockFullScreen", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<FeatureUnlockShowcaseView>();
            view.Build(roundedSprite);
            return view;
        }

        private void Build(Sprite roundedSprite)
        {
            var root = (RectTransform)transform;
            StretchFill(root);
            _stepRoundedSprite = roundedSprite;

            // 1. Semi-transparent vibrant royal-blue backdrop (game board softly visible behind) + tap-anywhere Button
            _backdropImg = gameObject.AddComponent<Image>();
            _backdropImg.color = ColBackdropBlue;
            _backdropImg.raycastTarget = true;

            _tapAnywhereBtn = gameObject.AddComponent<Button>();
            _tapAnywhereBtn.transition = Selectable.Transition.None;
            _tapAnywhereBtn.onClick.AddListener(HandleTapAnywhere);

            // 2. Central rotating radial sunburst + counter-rotating warm rays + balanced halo + subtle frosted ring
            var raysObj = new GameObject("SunburstRays", typeof(RectTransform), typeof(Image));
            raysObj.transform.SetParent(root, false);
            _sunburstRt = (RectTransform)raysObj.transform;
            _sunburstRt.anchorMin = _sunburstRt.anchorMax = new Vector2(0.5f, 0.54f);
            _sunburstRt.anchoredPosition = Vector2.zero;
            _sunburstRt.sizeDelta = new Vector2(1620f, 1620f);
            _sunburstImg = raysObj.GetComponent<Image>();
            _sunburstImg.sprite = MechanicFeaturePreviewRenderer.GetOrCreateSunburstSprite();
            _sunburstImg.color = new Color(0.88f, 0.97f, 1.00f, 0.72f);
            _sunburstImg.raycastTarget = false;

            var rays2Obj = new GameObject("SunburstRaysSecondary", typeof(RectTransform), typeof(Image));
            rays2Obj.transform.SetParent(root, false);
            _sunburstSecondaryRt = (RectTransform)rays2Obj.transform;
            _sunburstSecondaryRt.anchorMin = _sunburstSecondaryRt.anchorMax = new Vector2(0.5f, 0.54f);
            _sunburstSecondaryRt.anchoredPosition = Vector2.zero;
            _sunburstSecondaryRt.sizeDelta = new Vector2(1320f, 1320f);
            _sunburstSecondaryRt.localRotation = Quaternion.Euler(0f, 0f, 12.8f);
            _sunburstSecondaryImg = rays2Obj.GetComponent<Image>();
            _sunburstSecondaryImg.sprite = _sunburstImg.sprite;
            _sunburstSecondaryImg.color = new Color(1.00f, 0.96f, 0.80f, 0.36f);
            _sunburstSecondaryImg.raycastTarget = false;

            Sprite glowSpr = MechanicFeaturePreviewRenderer.GetOrCreateRadialGlowSprite();

            var haloObj = new GameObject("BehindGlowHalo", typeof(RectTransform), typeof(Image));
            haloObj.transform.SetParent(root, false);
            _glowHaloRt = (RectTransform)haloObj.transform;
            _glowHaloRt.anchorMin = _glowHaloRt.anchorMax = new Vector2(0.5f, 0.54f);
            _glowHaloRt.anchoredPosition = Vector2.zero;
            _glowHaloRt.sizeDelta = new Vector2(860f, 860f);
            _glowHaloImg = haloObj.GetComponent<Image>();
            _glowHaloImg.sprite = glowSpr;
            _glowHaloImg.color = new Color(0.56f, 0.90f, 1.00f, 0.68f);
            _glowHaloImg.raycastTarget = false;

            var coreObj = new GameObject("BehindGlowCore", typeof(RectTransform), typeof(Image));
            coreObj.transform.SetParent(root, false);
            _glowCoreRt = (RectTransform)coreObj.transform;
            _glowCoreRt.anchorMin = _glowCoreRt.anchorMax = new Vector2(0.5f, 0.54f);
            _glowCoreRt.anchoredPosition = Vector2.zero;
            _glowCoreRt.sizeDelta = new Vector2(540f, 540f);
            _glowCoreImg = coreObj.GetComponent<Image>();
            _glowCoreImg.sprite = glowSpr;
            _glowCoreImg.color = new Color(0.95f, 0.99f, 1.00f, 0.70f);
            _glowCoreImg.raycastTarget = false;

            var backRingObj = new GameObject("MechanicBackRing", typeof(RectTransform), typeof(Image));
            backRingObj.transform.SetParent(root, false);
            _backRingRt = (RectTransform)backRingObj.transform;
            _backRingRt.anchorMin = _backRingRt.anchorMax = new Vector2(0.5f, 0.54f);
            _backRingRt.anchoredPosition = Vector2.zero;
            _backRingRt.sizeDelta = new Vector2(476f, 476f);
            _backRingImg = backRingObj.GetComponent<Image>();
            _backRingImg.sprite = MechanicFeaturePreviewRenderer.GetOrCreateCelebrationRingSprite();
            _backRingImg.color = new Color(0.76f, 0.95f, 1.00f, 0.36f);
            _backRingImg.raycastTarget = false;

            BuildBehindSparkles(root);

            // 3. Confetti particle layer
            _confettiLayer = CreateRect("ConfettiLayer", root);
            StretchFill(_confettiLayer);

            // 4. Large hero mechanic preview (bottom-to-top silhouette fill)
            var previewObj = new GameObject("MechanicPreview", typeof(RectTransform), typeof(RawImage));
            previewObj.transform.SetParent(root, false);
            var pRt = (RectTransform)previewObj.transform;
            pRt.anchorMin = pRt.anchorMax = new Vector2(0.5f, 0.54f);
            pRt.anchoredPosition = Vector2.zero;
            pRt.sizeDelta = new Vector2(380f, 380f);
            _mechanicPreview = previewObj.GetComponent<RawImage>();
            _mechanicPreview.raycastTarget = false;

            Shader revealShader = Resources.Load<Shader>("Shaders/PuzzleFeatureRevealUI")
                ?? Shader.Find("PuzzleGlass/FeatureRevealUI");
            if (revealShader != null)
            {
                _revealMat = new Material(revealShader) { name = "Mat_FeatureRevealUI_Inst" };
                _revealMat.SetColor(SilhouetteColorId, new Color(0.06f, 0.11f, 0.26f, 1f));
                _mechanicPreview.material = _revealMat;
            }

            // 5. Percentage centered right inside the mechanic box visual (Level 1 HUD typography)
            _percentLabel = CreateBoxStyleTmpText("FeaturePercent", pRt, "0%", 82,
                new Vector2(0.5f, 0.5f), new Vector2(340f, 120f), charSpacing: 2.5f);
            _percentLabel.rectTransform.anchoredPosition = new Vector2(0f, 6f);

            // 6. "New Feature" label + goal subtitle + segmented step bar + "Tap to Continue"
            _titleLabel = CreateBoxStyleTmpText("NewFeatureTitle", root, "New Feature", 58,
                new Vector2(0.5f, 0.365f), new Vector2(760f, 84f), charSpacing: 3f);

            _subtitleLabel = CreateBoxStyleTmpText("FeatureSubtitle", root, "2 Levels Left", 30,
                new Vector2(0.5f, 0.305f), new Vector2(720f, 48f), charSpacing: 2.5f);

            _stepRowRt = CreateRect("StepDotsRow", root);
            _stepRowRt.anchorMin = _stepRowRt.anchorMax = new Vector2(0.5f, 0.248f);
            _stepRowRt.anchoredPosition = Vector2.zero;
            _stepRowRt.sizeDelta = new Vector2(480f, 28f);

            _tapHintLabel = CreateBoxStyleTmpText("TapToContinueHint", root, "Tap to Continue", 28,
                new Vector2(0.5f, 0.135f), new Vector2(560f, 44f), charSpacing: 2.5f,
                textColor: new Color(1f, 1f, 1f, 0f));

            // 7. "UNLOCKED!" golden pill banner (only visible when reaching 100%)
            var badgeObj = new GameObject("UnlockedBadge", typeof(RectTransform), typeof(Image));
            badgeObj.transform.SetParent(root, false);
            _unlockedBadgeRt = (RectTransform)badgeObj.transform;
            _unlockedBadgeRt.anchorMin = _unlockedBadgeRt.anchorMax = new Vector2(0.5f, 0.705f);
            _unlockedBadgeRt.anchoredPosition = Vector2.zero;
            _unlockedBadgeRt.sizeDelta = new Vector2(580f, 70f);
            var badgeImg = badgeObj.GetComponent<Image>();
            badgeImg.sprite = roundedSprite;
            badgeImg.type = Image.Type.Sliced;
            badgeImg.color = ColUnlockedGold;
            badgeImg.raycastTarget = false;
            var badgeSh = badgeObj.AddComponent<Shadow>();
            badgeSh.effectColor = new Color(0.05f, 0.08f, 0.22f, 0.50f);
            badgeSh.effectDistance = new Vector2(0f, -4f);

            _unlockedBadgeText = CreateBoxStyleTmpText("UnlockedText", _unlockedBadgeRt, "UNLOCKED!", 32,
                new Vector2(0.5f, 0.5f), new Vector2(540f, 62f), charSpacing: 2.5f);
            _unlockedBadgeRt.gameObject.SetActive(false);

            // 8. Foreground celebration layer (shockwave rings + 100% firework confetti/stars)
            _celebrationLayer = CreateRect("CelebrationLayer", root);
            StretchFill(_celebrationLayer);

            // 9. Screen flash layer for 100% unlock burst
            var flashObj = new GameObject("UnlockFlash", typeof(RectTransform), typeof(Image));
            flashObj.transform.SetParent(root, false);
            StretchFill((RectTransform)flashObj.transform);
            _flashOverlay = flashObj.GetComponent<Image>();
            _flashOverlay.color = new Color(1f, 0.96f, 0.85f, 0f);
            _flashOverlay.raycastTarget = false;

            gameObject.SetActive(false);
        }

        private void HandleTapAnywhere()
        {
            if (!_canTapToContinue || _nextConsumed || LevelTransitionController.IsTransitioning) return;
            _nextConsumed = true;
            PuzzleSoundManager.GetOrCreate().PlayUiClick();
            var cb = _onNextClicked;
            Hide();
            cb?.Invoke();
        }

        public void Show(int completedLevelNumber, FeatureProgressInfo info, System.Action onNextClicked)
        {
            _showSeq?.Kill();
            _unlockSeq?.Kill();
            _unlockSeq = null;
            ClearConfetti();
            _onNextClicked = onNextClicked;
            _canTapToContinue = false;
            _nextConsumed = false;
            _isUnlockedIdle = false;
            _unlockIdleTime = 0f;
            _ambientConfettiTimer = 0.25f;
            _lastTickSoundPct = info.prevPercent;
            _lastGlintSpawnTime = -10f;
            gameObject.SetActive(true);

            var snapshot = MechanicFeaturePreviewRenderer.GetOrCapturePreview(info.kind, info.sampleEntry);
            if (_mechanicPreview != null)
            {
                _mechanicPreview.texture = snapshot.texture;
                _mechanicPreview.rectTransform.DOKill(true);
                _mechanicPreview.rectTransform.anchoredPosition = Vector2.zero;
                _mechanicPreview.rectTransform.localRotation = Quaternion.identity;
            }

            if (_revealMat != null)
            {
                _revealMat.SetVector(ContentBoundsId, new Vector4(snapshot.contentVBounds.x, snapshot.contentVBounds.y, 0f, 0f));
                _revealMat.SetFloat(RevealProgressId, Mathf.Clamp01(info.prevPercent / 100f));
            }

            if (_titleLabel != null) _titleLabel.text = "New Feature";

            int levelsLeft = Mathf.Max(0, info.unlockLevel - completedLevelNumber - 1);
            if (_subtitleLabel != null)
            {
                if (info.isJustUnlocked || levelsLeft <= 0)
                    _subtitleLabel.text = "Ready to Play!";
                else
                    _subtitleLabel.text = levelsLeft == 1 ? "1 Level Left" : $"{levelsLeft} Levels Left";
            }

            int totalSteps = Mathf.Max(1, info.totalSteps);
            int targetStep = Mathf.Clamp(info.currentStep, 0, totalSteps);
            int prevStep = Mathf.Clamp(targetStep - 1, 0, totalSteps);
            RebuildStepDots(totalSteps, prevStep);

            if (_tapHintLabel != null)
            {
                _tapHintLabel.DOKill(true);
                Color hintCol = _tapHintLabel.color;
                hintCol.a = 0f;
                _tapHintLabel.color = hintCol;
                _tapHintLabel.rectTransform.localScale = Vector3.one;
            }

            if (_unlockedBadgeRt != null)
            {
                _unlockedBadgeRt.gameObject.SetActive(false);
                _unlockedBadgeRt.DOKill(true);
            }
            if (_flashOverlay != null)
            {
                _flashOverlay.DOKill(true);
                _flashOverlay.color = new Color(1f, 0.96f, 0.85f, 0f);
            }

            _shownPct = -1;
            SetDisplayedPercent(info.prevPercent);

            Color bgTarget = ColBackdropBlue;
            _backdropImg.color = new Color(bgTarget.r, bgTarget.g, bgTarget.b, 0f);
            _glowAnimTime = 0f;
            _sunburstRt.localScale = Vector3.one * 0.35f;
            if (_sunburstSecondaryRt != null) _sunburstSecondaryRt.localScale = Vector3.one * 0.35f;
            if (_glowHaloRt != null) _glowHaloRt.localScale = Vector3.one * 0.35f;
            if (_glowCoreRt != null) _glowCoreRt.localScale = Vector3.one * 0.35f;
            if (_backRingRt != null) _backRingRt.localScale = Vector3.one * 0.35f;
            if (_sunburstImg != null) _sunburstImg.color = new Color(0.88f, 0.97f, 1.00f, 0.72f);
            if (_sunburstSecondaryImg != null) _sunburstSecondaryImg.color = new Color(1.00f, 0.96f, 0.80f, 0.36f);
            if (_glowHaloImg != null) _glowHaloImg.color = new Color(0.56f, 0.90f, 1.00f, 0.68f);
            if (_glowCoreImg != null) _glowCoreImg.color = new Color(0.95f, 0.99f, 1.00f, 0.70f);
            if (_backRingImg != null) _backRingImg.color = new Color(0.76f, 0.95f, 1.00f, 0.36f);
            _mechanicPreview.rectTransform.localScale = Vector3.one * 0.35f;
            _titleLabel.rectTransform.localScale = Vector3.one * 0.7f;
            if (_subtitleLabel != null) _subtitleLabel.rectTransform.localScale = Vector3.one * 0.7f;
            _percentLabel.rectTransform.localScale = Vector3.one;

            float from01 = Mathf.Clamp01(info.prevPercent / 100f);
            float to01 = Mathf.Clamp01(info.newPercent / 100f);

            Sequence seq = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);

            // 1. Backdrop fade + sunburst & halo & hero mechanic pop + confetti burst
            seq.Append(_backdropImg.DOFade(bgTarget.a, 0.22f).SetEase(Ease.OutQuad));
            seq.Join(_sunburstRt.DOScale(1f, 0.42f).SetEase(Ease.OutBack));
            if (_sunburstSecondaryRt != null) seq.Join(_sunburstSecondaryRt.DOScale(1f, 0.42f).SetEase(Ease.OutBack));
            if (_glowHaloRt != null) seq.Join(_glowHaloRt.DOScale(1f, 0.40f).SetEase(Ease.OutBack));
            if (_glowCoreRt != null) seq.Join(_glowCoreRt.DOScale(1f, 0.38f).SetEase(Ease.OutBack));
            if (_backRingRt != null) seq.Join(_backRingRt.DOScale(1f, 0.40f).SetEase(Ease.OutBack));
            seq.Insert(0.08f, _mechanicPreview.rectTransform.DOScale(1f, 0.38f).SetEase(Ease.OutBack));
            seq.Insert(0.12f, _titleLabel.rectTransform.DOScale(1f, 0.30f).SetEase(Ease.OutBack));
            if (_subtitleLabel != null) seq.Insert(0.16f, _subtitleLabel.rectTransform.DOScale(1f, 0.28f).SetEase(Ease.OutBack));
            seq.InsertCallback(0.20f, () => SpawnConfettiBurst(24));

            // 2. Smooth bottom-to-top fill animation + percentage counter roll + rising glints & audio ticks
            seq.AppendInterval(0.08f);
            seq.Append(DOTween.To(() => from01, v =>
            {
                from01 = v;
                if (_revealMat != null) _revealMat.SetFloat(RevealProgressId, v);
                int pct = Mathf.RoundToInt(Mathf.Lerp(info.prevPercent, info.newPercent,
                    Mathf.InverseLerp(info.prevPercent / 100f, Mathf.Max(info.prevPercent / 100f + 0.0001f, to01), v)));
                if (to01 <= info.prevPercent / 100f) pct = info.newPercent;
                SetDisplayedPercent(pct);

                if (pct - _lastTickSoundPct >= 5 && pct < info.newPercent)
                {
                    _lastTickSoundPct = pct;
                    PuzzleSoundManager.Instance?.PlayBallTap(0.28f);
                }

                float now = Time.unscaledTime;
                if (now - _lastGlintSpawnTime >= 0.11f && to01 > info.prevPercent / 100f + 0.01f)
                {
                    _lastGlintSpawnTime = now;
                    SpawnFillLineGlint(v, snapshot.contentVBounds);
                }
            }, to01, 0.80f).SetEase(Ease.OutCubic));

            // 3. Completion punch, step dot fill, shockwave & unlock celebration
            seq.AppendCallback(() =>
            {
                SetDisplayedPercent(info.newPercent);
                if (_revealMat != null) _revealMat.SetFloat(RevealProgressId, to01);

                _percentLabel.rectTransform.DOKill(true);
                _percentLabel.rectTransform.localScale = Vector3.one;
                _percentLabel.rectTransform.DOPunchScale(Vector3.one * 0.30f, 0.32f, 5, 0.6f).SetUpdate(true);

                if (_mechanicPreview != null)
                {
                    _mechanicPreview.rectTransform.DOKill(true);
                    _mechanicPreview.rectTransform.localScale = Vector3.one;
                    _mechanicPreview.rectTransform.DOPunchScale(Vector3.one * 0.11f, 0.30f, 5, 0.55f).SetUpdate(true);
                }

                AnimateStepDotEarned(targetStep - 1, info.isJustUnlocked);
                SpawnShockwaveRing(new Color(0.75f, 0.96f, 1.00f, 0.72f), 340f, 760f, 0.44f, 0f);
                BlockClearCelebration.VibrateSlotPlace();

                _canTapToContinue = true;
                if (_tapHintLabel != null)
                {
                    _tapHintLabel.DOFade(0.92f, 0.32f).SetEase(Ease.OutQuad).SetUpdate(true);
                }

                if (info.isJustUnlocked)
                {
                    BlockClearCelebration.VibrateBoxClear();
                    PlayUnlockCelebration(info.featureTitle);
                }
                else
                {
                    PuzzleSoundManager.Instance?.PlayLidLatch();
                }
            });

            _showSeq = seq;
        }

        public void Hide()
        {
            _showSeq?.Kill();
            _showSeq = null;
            _unlockSeq?.Kill();
            _unlockSeq = null;
            _isUnlockedIdle = false;
            _canTapToContinue = false;
            ClearConfetti();
            gameObject.SetActive(false);
        }

        private void PlayUnlockCelebration(string featureTitle)
        {
            PuzzleSoundManager.Instance?.PlayUnlock();
            _unlockSeq?.Kill();
            _isUnlockedIdle = false;
            _unlockIdleTime = 0f;

            if (_mechanicPreview != null)
            {
                var rt = _mechanicPreview.rectTransform;
                rt.DOKill(true);
                rt.localScale = Vector3.one;
                rt.localRotation = Quaternion.identity;

                Sequence pop = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
                pop.Append(rt.DOScale(new Vector3(1.16f, 0.88f, 1f), 0.08f).SetEase(Ease.OutSine));
                pop.Append(rt.DOScale(new Vector3(0.95f, 1.18f, 1f), 0.14f).SetEase(Ease.OutQuad));
                pop.Join(rt.DOLocalRotate(new Vector3(0f, 0f, 5.5f), 0.14f).SetEase(Ease.OutQuad));
                pop.Append(rt.DOScale(1.09f, 0.14f).SetEase(Ease.OutBack));
                pop.Join(rt.DOLocalRotate(new Vector3(0f, 0f, -4.5f), 0.14f).SetEase(Ease.InOutSine));
                pop.Append(rt.DOScale(1.0f, 0.16f).SetEase(Ease.InOutSine));
                pop.Join(rt.DOLocalRotate(Vector3.zero, 0.16f).SetEase(Ease.OutSine));
                pop.InsertCallback(0.24f, () =>
                {
                    SpawnCenterFireworkBurst(28);
                    SpawnConfettiBurst(20, celebratory: true);
                });
                pop.OnComplete(() =>
                {
                    _isUnlockedIdle = true;
                    _unlockIdleTime = 0f;
                });
                _unlockSeq = pop;
            }
            else
            {
                _isUnlockedIdle = true;
            }

            if (_glowHaloRt != null)
            {
                _glowHaloRt.DOKill(true);
                _glowHaloRt.localScale = Vector3.one;
                _glowHaloRt.DOPunchScale(Vector3.one * 0.34f, 0.52f, 4, 0.5f).SetUpdate(true);
            }
            if (_glowCoreRt != null)
            {
                _glowCoreRt.DOKill(true);
                _glowCoreRt.localScale = Vector3.one;
                _glowCoreRt.DOPunchScale(Vector3.one * 0.38f, 0.52f, 4, 0.5f).SetUpdate(true);
            }
            if (_sunburstSecondaryImg != null)
            {
                _sunburstSecondaryImg.color = new Color(1.00f, 0.90f, 0.46f, 0.72f);
            }
            if (_flashOverlay != null)
            {
                _flashOverlay.color = new Color(1f, 0.96f, 0.85f, 0.58f);
                _flashOverlay.DOFade(0f, 0.50f).SetEase(Ease.OutQuad).SetUpdate(true);
            }
            if (_unlockedBadgeRt != null)
            {
                if (_unlockedBadgeText != null) _unlockedBadgeText.text = $"{featureTitle} UNLOCKED!";
                _unlockedBadgeRt.gameObject.SetActive(true);
                _unlockedBadgeRt.localScale = Vector3.one * 0.35f;
                _unlockedBadgeRt.DOScale(1f, 0.38f).SetEase(Ease.OutBack).SetUpdate(true);
            }

            SpawnShockwaveRing(new Color(1f, 0.97f, 0.86f, 0.90f), 260f, 1180f, 0.55f, 0f);
            SpawnShockwaveRing(new Color(1f, 0.85f, 0.28f, 0.82f), 220f, 960f, 0.62f, 0.08f);
            SpawnCenterFireworkBurst(42);
            SpawnConfettiBurst(28, celebratory: true);
        }

        private void SpawnShockwaveRing(Color color, float startSize, float endSize, float duration, float delay)
        {
            RectTransform parent = _celebrationLayer != null ? _celebrationLayer : _confettiLayer;
            if (parent == null) return;

            var go = new GameObject("UnlockShockwaveRing", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.54f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(startSize, startSize);

            var img = go.GetComponent<Image>();
            img.sprite = MechanicFeaturePreviewRenderer.GetOrCreateCelebrationRingSprite();
            img.color = new Color(color.r, color.g, color.b, 0f);
            img.raycastTarget = false;
            _tempRings.Add(go);

            Sequence ringSeq = DOTween.Sequence().SetUpdate(true).SetLink(go);
            if (delay > 0f) ringSeq.AppendInterval(delay);
            ringSeq.AppendCallback(() => { if (img != null) img.color = color; });
            ringSeq.Append(rt.DOSizeDelta(new Vector2(endSize, endSize), duration).SetEase(Ease.OutCubic));
            ringSeq.Join(img.DOFade(0f, duration).SetEase(Ease.InQuad));
            ringSeq.OnComplete(() =>
            {
                _tempRings.Remove(go);
                if (go != null) Destroy(go);
            });
        }

        private void SpawnCenterFireworkBurst(int count)
        {
            RectTransform parent = _celebrationLayer != null ? _celebrationLayer : _confettiLayer;
            if (parent == null) return;

            Sprite confettiSpr = MechanicFeaturePreviewRenderer.GetOrCreateConfettiSprite();
            Sprite starSpr = MechanicFeaturePreviewRenderer.GetOrCreateSparkleSprite();

            for (int i = 0; i < count; i++)
            {
                bool isStar = (i % 4 == 0);
                var go = new GameObject(isStar ? "FireworkStar" : "FireworkConfetti", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(parent, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.54f);

                float ang = (i / (float)count) * Mathf.PI * 2f + Random.Range(-0.18f, 0.18f);
                Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                rt.anchoredPosition = dir * Random.Range(24f, 95f);

                float size = isStar ? Random.Range(34f, 56f) : Random.Range(26f, 44f);
                Vector2 sz = isStar ? new Vector2(size, size) : new Vector2(size, size * 0.56f);
                rt.sizeDelta = sz;
                rt.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

                var img = go.GetComponent<Image>();
                img.sprite = isStar ? starSpr : confettiSpr;
                img.color = isStar
                    ? (Random.value < 0.55f ? new Color(1f, 0.96f, 0.72f, 1f) : Color.white)
                    : ConfettiPalette[Random.Range(0, ConfettiPalette.Length)];
                img.raycastTarget = false;

                float spd = Random.Range(420f, 1020f);
                Vector2 vel = dir * spd + new Vector2(0f, Random.Range(140f, 340f));

                _confetti.Add(new ConfettiParticle
                {
                    rt = rt,
                    img = img,
                    vel = vel,
                    rotSpeed = Random.Range(-480f, 480f),
                    flutterPhase = Random.Range(0f, Mathf.PI * 2f),
                    flutterFreq = Random.Range(5f, 10f),
                    life = 0f,
                    maxLife = Random.Range(1.4f, 2.2f),
                    ambient = false,
                    isStar = isStar,
                    baseSize = sz
                });
            }
        }

        private void SpawnConfettiBurst(int countPerSide, bool celebratory = false)
        {
            RectTransform parent = (celebratory && _celebrationLayer != null) ? _celebrationLayer : _confettiLayer;
            if (parent == null) return;
            Sprite spr = MechanicFeaturePreviewRenderer.GetOrCreateConfettiSprite();
            for (int side = -1; side <= 1; side += 2)
            {
                Vector2 origin = celebratory ? new Vector2(side * 360f, -220f) : new Vector2(side * 380f, -140f);
                for (int i = 0; i < countPerSide; i++)
                {
                    var go = new GameObject("Confetti", typeof(RectTransform), typeof(Image));
                    go.transform.SetParent(parent, false);
                    var rt = (RectTransform)go.transform;
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = origin + new Vector2(Random.Range(-40f, 40f), Random.Range(-50f, 50f));
                    float size = celebratory ? Random.Range(26f, 44f) : Random.Range(22f, 36f);
                    Vector2 sz = new Vector2(size, size * 0.58f);
                    rt.sizeDelta = sz;
                    rt.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

                    var img = go.GetComponent<Image>();
                    img.sprite = spr;
                    img.color = ConfettiPalette[Random.Range(0, ConfettiPalette.Length)];
                    img.raycastTarget = false;

                    float speedX = -side * Random.Range(200f, 640f);
                    float speedY = celebratory ? Random.Range(740f, 1360f) : Random.Range(560f, 1140f);
                    _confetti.Add(new ConfettiParticle
                    {
                        rt = rt,
                        img = img,
                        vel = new Vector2(speedX, speedY),
                        rotSpeed = Random.Range(-420f, 420f),
                        flutterPhase = Random.Range(0f, Mathf.PI * 2f),
                        flutterFreq = Random.Range(5f, 9f),
                        life = 0f,
                        maxLife = Random.Range(1.7f, 2.6f),
                        ambient = false,
                        isStar = false,
                        baseSize = sz
                    });
                }
            }
        }

        private void SpawnAmbientConfetti(int count)
        {
            if (_confettiLayer == null) return;
            Sprite spr = MechanicFeaturePreviewRenderer.GetOrCreateConfettiSprite();
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("AmbientConfetti", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(_confettiLayer, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(Random.Range(-490f, 490f), Random.Range(620f, 840f));
                float size = Random.Range(20f, 34f);
                Vector2 sz = new Vector2(size, size * 0.58f);
                rt.sizeDelta = sz;
                rt.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

                var img = go.GetComponent<Image>();
                img.sprite = spr;
                img.color = ConfettiPalette[Random.Range(0, ConfettiPalette.Length)];
                img.raycastTarget = false;

                _confetti.Add(new ConfettiParticle
                {
                    rt = rt,
                    img = img,
                    vel = new Vector2(Random.Range(-75f, 75f), -Random.Range(170f, 300f)),
                    rotSpeed = Random.Range(-240f, 240f),
                    flutterPhase = Random.Range(0f, Mathf.PI * 2f),
                    flutterFreq = Random.Range(4f, 7f),
                    life = 0f,
                    maxLife = Random.Range(2.4f, 3.5f),
                    ambient = true,
                    isStar = false,
                    baseSize = sz
                });
            }
        }

        private void RebuildStepDots(int totalSteps, int initiallyFilledCount)
        {
            if (_stepRowRt == null) return;
            for (int i = _stepRowRt.childCount - 1; i >= 0; i--)
            {
                Destroy(_stepRowRt.GetChild(i).gameObject);
            }
            _stepDots.Clear();

            if (totalSteps <= 1)
            {
                _stepRowRt.gameObject.SetActive(false);
                return;
            }
            _stepRowRt.gameObject.SetActive(true);

            int count = Mathf.Min(totalSteps, 8);
            float spacing = 14f;
            float maxRowWidth = 420f;
            float pillW = Mathf.Clamp((maxRowWidth - (count - 1) * spacing) / count, 28f, 76f);
            float pillH = 14f;
            float totalW = count * pillW + (count - 1) * spacing;
            float startX = -totalW * 0.5f + pillW * 0.5f;

            for (int i = 0; i < count; i++)
            {
                var dotGo = new GameObject($"StepPill_{i}", typeof(RectTransform), typeof(Image));
                dotGo.transform.SetParent(_stepRowRt, false);
                var rt = (RectTransform)dotGo.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(startX + i * (pillW + spacing), 0f);
                rt.sizeDelta = new Vector2(pillW, pillH);

                var img = dotGo.GetComponent<Image>();
                if (_stepRoundedSprite != null)
                {
                    img.sprite = _stepRoundedSprite;
                    img.type = Image.Type.Sliced;
                }
                img.color = (i < initiallyFilledCount) ? ColStepFilled : ColStepPending;
                img.raycastTarget = false;

                var sh = dotGo.AddComponent<Shadow>();
                sh.effectColor = new Color(0.03f, 0.06f, 0.18f, 0.45f);
                sh.effectDistance = new Vector2(0f, -2f);

                _stepDots.Add(img);
            }
        }

        private void AnimateStepDotEarned(int stepIndex, bool isUnlockStep)
        {
            if (stepIndex < 0 || stepIndex >= _stepDots.Count) return;
            Image dotImg = _stepDots[stepIndex];
            if (dotImg == null) return;

            Color targetCol = isUnlockStep ? ColStepNewGold : ColStepFilled;
            dotImg.DOKill(true);
            dotImg.DOColor(targetCol, 0.22f).SetEase(Ease.OutQuad).SetUpdate(true);
            dotImg.rectTransform.DOKill(true);
            dotImg.rectTransform.localScale = Vector3.one;
            dotImg.rectTransform.DOPunchScale(new Vector3(0.28f, 0.45f, 0f), 0.34f, 5, 0.55f).SetUpdate(true);
        }

        private void SpawnFillLineGlint(float progress01, Vector2 contentVBounds)
        {
            RectTransform parent = _celebrationLayer != null ? _celebrationLayer : _confettiLayer;
            if (parent == null || _mechanicPreview == null) return;

            Sprite starSpr = MechanicFeaturePreviewRenderer.GetOrCreateSparkleSprite();
            float normY = Mathf.Lerp(contentVBounds.x, contentVBounds.y, Mathf.Clamp01(progress01)) - 0.5f;
            float boxSize = _mechanicPreview.rectTransform.sizeDelta.x;
            float yOffset = normY * boxSize;
            float xOffset = Random.Range(-boxSize * 0.30f, boxSize * 0.30f);

            var go = new GameObject("FillGlint", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.54f);
            rt.anchoredPosition = new Vector2(xOffset, yOffset);
            float sz = Random.Range(22f, 36f);
            rt.sizeDelta = new Vector2(sz, sz);

            var img = go.GetComponent<Image>();
            img.sprite = starSpr;
            img.color = Random.value < 0.5f ? Color.white : new Color(0.68f, 0.96f, 1.00f, 1f);
            img.raycastTarget = false;

            _confetti.Add(new ConfettiParticle
            {
                rt = rt,
                img = img,
                vel = new Vector2(Random.Range(-45f, 45f), Random.Range(140f, 240f)),
                rotSpeed = Random.Range(-220f, 220f),
                flutterPhase = Random.Range(0f, Mathf.PI * 2f),
                flutterFreq = 7f,
                life = 0f,
                maxLife = Random.Range(0.45f, 0.68f),
                ambient = true,
                isStar = true,
                baseSize = new Vector2(sz, sz)
            });
        }

        private void BuildBehindSparkles(RectTransform root)
        {
            _sparkles.Clear();
            Sprite starSpr = MechanicFeaturePreviewRenderer.GetOrCreateSparkleSprite();
            Vector2[] offsets =
            {
                new Vector2(-265f, 205f),
                new Vector2(270f, 195f),
                new Vector2(-295f, -30f),
                new Vector2(295f, -15f),
                new Vector2(-215f, -230f),
                new Vector2(225f, -220f),
                new Vector2(0f, 285f),
                new Vector2(-135f, 255f),
                new Vector2(145f, 250f)
            };
            float[] sizes = { 76f, 68f, 62f, 72f, 64f, 70f, 82f, 52f, 54f };
            for (int i = 0; i < offsets.Length; i++)
            {
                var go = new GameObject($"BehindSparkle_{i}", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(root, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.54f);
                rt.anchoredPosition = offsets[i];
                rt.sizeDelta = new Vector2(sizes[i], sizes[i]);

                var img = go.GetComponent<Image>();
                img.sprite = starSpr;
                Color baseCol = (i % 2 == 0)
                    ? new Color(1.00f, 0.99f, 0.90f, 0.95f)
                    : new Color(0.76f, 0.96f, 1.00f, 0.95f);
                img.color = baseCol;
                img.raycastTarget = false;

                _sparkles.Add(new SparkleFx
                {
                    rt = rt,
                    img = img,
                    phase = i * 0.78f,
                    freq = 2.6f + (i % 3) * 0.65f,
                    baseScale = 1f,
                    rotSpeed = (i % 2 == 0 ? 18f : -18f),
                    baseColor = baseCol
                });
            }
        }

        private void ClearConfetti()
        {
            for (int i = _confetti.Count - 1; i >= 0; i--)
            {
                if (_confetti[i].rt != null) Destroy(_confetti[i].rt.gameObject);
            }
            _confetti.Clear();
            for (int i = _tempRings.Count - 1; i >= 0; i--)
            {
                if (_tempRings[i] != null) Destroy(_tempRings[i]);
            }
            _tempRings.Clear();
        }

        private void SetDisplayedPercent(int pct)
        {
            pct = Mathf.Clamp(pct, 0, 100);
            if (pct == _shownPct) return;
            _shownPct = pct;
            if (_percentLabel != null) _percentLabel.text = $"{pct}%";
        }

        private void Update()
        {
            if (!gameObject.activeInHierarchy) return;
            float dt = Time.unscaledDeltaTime;
            _glowAnimTime += dt;

            if (_sunburstRt != null)
            {
                _sunburstRt.Rotate(0f, 0f, (_isUnlockedIdle ? -22f : -14f) * dt);
            }
            if (_sunburstSecondaryRt != null)
            {
                _sunburstSecondaryRt.Rotate(0f, 0f, (_isUnlockedIdle ? 14f : 9f) * dt);
            }
            if (_glowHaloRt != null && !DOTween.IsTweening(_glowHaloRt))
            {
                float pulse = 1f + (_isUnlockedIdle ? 0.08f : 0.055f) * Mathf.Sin(_glowAnimTime * 2.8f);
                _glowHaloRt.localScale = Vector3.one * pulse;
            }
            if (_glowCoreRt != null && !DOTween.IsTweening(_glowCoreRt))
            {
                float corePulse = 1f + (_isUnlockedIdle ? 0.065f : 0.045f) * Mathf.Sin(_glowAnimTime * 3.4f + 0.8f);
                _glowCoreRt.localScale = Vector3.one * corePulse;
            }
            if (_backRingRt != null && !DOTween.IsTweening(_backRingRt))
            {
                float ringPulse = 1f + (_isUnlockedIdle ? 0.05f : 0.03f) * Mathf.Sin(_glowAnimTime * 2.4f + 0.4f);
                _backRingRt.localScale = Vector3.one * ringPulse;
            }
            if (_mechanicPreview != null && _canTapToContinue && !DOTween.IsTweening(_mechanicPreview.rectTransform))
            {
                float floatY = Mathf.Sin(_glowAnimTime * 2.4f) * 7f;
                _mechanicPreview.rectTransform.anchoredPosition = new Vector2(0f, floatY);
            }
            if (_isUnlockedIdle && _mechanicPreview != null)
            {
                _unlockIdleTime += dt;
                float breathe = 1.025f + 0.045f * Mathf.Sin(_unlockIdleTime * 3.1f);
                float sway = 2.0f * Mathf.Sin(_unlockIdleTime * 2.1f);
                _mechanicPreview.rectTransform.localScale = Vector3.one * breathe;
                _mechanicPreview.rectTransform.localRotation = Quaternion.Euler(0f, 0f, sway);
            }
            if (_isUnlockedIdle && _unlockedBadgeRt != null && _unlockedBadgeRt.gameObject.activeSelf && !DOTween.IsTweening(_unlockedBadgeRt))
            {
                float badgePulse = 1f + 0.032f * Mathf.Sin(_unlockIdleTime * 3.1f + 0.6f);
                _unlockedBadgeRt.localScale = Vector3.one * badgePulse;
            }
            if (_canTapToContinue && _tapHintLabel != null && !DOTween.IsTweening(_tapHintLabel))
            {
                float hintWave = 0.5f + 0.5f * Mathf.Sin(_glowAnimTime * 3.0f);
                Color hc = _tapHintLabel.color;
                hc.a = Mathf.Lerp(0.50f, 0.96f, hintWave);
                _tapHintLabel.color = hc;
                _tapHintLabel.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.985f, 1.025f, hintWave);
            }
            for (int i = 0; i < _sparkles.Count; i++)
            {
                var sp = _sparkles[i];
                if (sp.rt == null || sp.img == null) continue;
                float wave = 0.5f + 0.5f * Mathf.Sin(_glowAnimTime * sp.freq + sp.phase);
                float eased = wave * wave;
                sp.rt.localScale = Vector3.one * (sp.baseScale * (0.35f + 0.75f * eased));
                sp.rt.Rotate(0f, 0f, sp.rotSpeed * dt);
                Color col = sp.baseColor;
                col.a = 0.20f + 0.80f * eased;
                sp.img.color = col;
            }

            _ambientConfettiTimer -= dt;
            if (_ambientConfettiTimer <= 0f)
            {
                _ambientConfettiTimer = _isUnlockedIdle ? 0.14f : 0.24f;
                if (_confetti.Count < (_isUnlockedIdle ? 110 : 80)) SpawnAmbientConfetti(_isUnlockedIdle ? 3 : 2);
            }

            for (int i = _confetti.Count - 1; i >= 0; i--)
            {
                var c = _confetti[i];
                if (c.rt == null) { _confetti.RemoveAt(i); continue; }
                c.life += dt;
                if (c.life >= c.maxLife)
                {
                    Destroy(c.rt.gameObject);
                    _confetti.RemoveAt(i);
                    continue;
                }

                if (!c.ambient)
                {
                    c.vel.y -= 860f * dt;
                    c.vel.x *= Mathf.Exp(-1.5f * dt);
                }

                float sway = Mathf.Sin(c.life * c.flutterFreq + c.flutterPhase) * (c.isStar ? 24f : 52f);
                c.rt.anchoredPosition += new Vector2(c.vel.x + sway, c.vel.y) * dt;
                c.rt.Rotate(0f, 0f, c.rotSpeed * dt);
                if (c.isStar)
                {
                    float sPulse = 0.72f + 0.36f * Mathf.Abs(Mathf.Sin(c.life * c.flutterFreq + c.flutterPhase));
                    c.rt.localScale = Vector3.one * sPulse;
                }
                else
                {
                    float flipScale = 0.40f + 0.60f * Mathf.Abs(Mathf.Cos(c.life * c.flutterFreq + c.flutterPhase));
                    c.rt.localScale = new Vector3(1f, flipScale, 1f);
                }

                float fade = 1f - Mathf.SmoothStep(0.72f, 1.0f, c.life / c.maxLife);
                Color col = c.img.color;
                col.a = fade;
                c.img.color = col;
                _confetti[i] = c;
            }
        }

        private void OnDestroy()
        {
            _showSeq?.Kill();
            _unlockSeq?.Kill();
            ClearConfetti();
            if (_revealMat != null) Destroy(_revealMat);
        }

        /// <summary>
        /// Creates TextMeshProUGUI matching the HUD "Level 1" title styling:
        /// rounded bold dilate (0.6), deep purple-indigo crisp outline (0.24), subtle underlay shadow,
        /// and white-to-soft-lilac vertical gradient.
        /// </summary>
        private static TextMeshProUGUI CreateBoxStyleTmpText(
            string name,
            Transform parent,
            string content,
            float fontSize,
            Vector2 anchor,
            Vector2 box,
            float charSpacing = 3f,
            Color? textColor = null,
            bool applyOutlineShadow = true)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = box;

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = content;
            tmp.fontSize = fontSize;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = textColor ?? Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.extraPadding = true;
            tmp.characterSpacing = charSpacing;
            tmp.raycastTarget = false;

            if (applyOutlineShadow)
            {
                tmp.enableVertexGradient = true;
                tmp.colorGradient = new VertexGradient(
                    Color.white,
                    Color.white,
                    new Color(0.86f, 0.82f, 1f, 1f),
                    new Color(0.86f, 0.82f, 1f, 1f));
            }

            if (applyOutlineShadow && tmp.fontSharedMaterial != null)
            {
                var outline = new Color(0.20f, 0.10f, 0.46f, 1f);
                var mat = new Material(tmp.fontSharedMaterial) { name = $"{tmp.fontSharedMaterial.name}_{name}" };
                if (mat.HasProperty("_FaceDilate")) mat.SetFloat("_FaceDilate", 0.6f);
                mat.EnableKeyword("OUTLINE_ON");
                if (mat.HasProperty("_OutlineColor")) mat.SetColor("_OutlineColor", outline);
                if (mat.HasProperty("_OutlineWidth")) mat.SetFloat("_OutlineWidth", 0.24f);
                if (mat.HasProperty("_OutlineSoftness")) mat.SetFloat("_OutlineSoftness", 0f);
                if (mat.HasProperty("_UnderlayColor"))
                {
                    mat.EnableKeyword("UNDERLAY_ON");
                    mat.SetColor("_UnderlayColor", new Color(outline.r * 0.6f, outline.g * 0.6f, outline.b * 0.6f, 0.72f));
                    mat.SetFloat("_UnderlayOffsetX", 0f);
                    mat.SetFloat("_UnderlayOffsetY", -0.9f);
                    mat.SetFloat("_UnderlayDilate", 0.5f);
                    mat.SetFloat("_UnderlaySoftness", 0.1f);
                }
                tmp.fontSharedMaterial = mat;
                tmp.UpdateMeshPadding();
            }
            return tmp;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void StretchFill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
