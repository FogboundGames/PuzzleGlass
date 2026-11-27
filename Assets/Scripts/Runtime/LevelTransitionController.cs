using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace PuzzleGlass
{
    /// <summary>
    /// Level geçişi (useGlassSwapTransition):
    /// 1) Kutlama: boş cam sıkışıp esner, merkezinde kısa parıltı patlaması (BlockClearCelebration) + titreşim.
    /// 2) showLoadingBetweenLevels açıksa: tema perdesi + oyun adı + LOADING barı; bar dolarken sonraki level yüklenir.
    ///    Kapalıysa: eski cam yukarı fırlar, sonraki level anında yüklenir.
    /// 3) Perde kalkarken yeni cam yukarıdan düşer ve inişte sıkışır; kutular sırayla pop yapar, toplar alttan dolar,
    ///    üstteki LEVEL hapı pop yapar (HUD). Her şey yerine oturunca girdi açılır.
    /// Oyun açılışında da (showStartupLoading) aynı perde + bar gösterilir, ilk level aynı giriş animasyonuyla açılır.
    /// Sadece görseller canlandırılır (cam görsel kökü, topların Visual alt objesi, kutu ölçeği); fizik ve oyun mantığı aynıdır.
    /// Eski geçiş (düz perde + LOADING barı + top yağmuru) useGlassSwapTransition kapatılırsa çalışır.
    /// </summary>
    public class LevelTransitionController : MonoBehaviour
    {
        public static LevelTransitionController Instance { get; private set; }

        /// <summary>Geçiş oynarken true. Bu sürede oyuncu girdisi kilitlenir.</summary>
        public static bool IsTransitioning => Instance != null && Instance._isPlaying;

        [Header("Oyun Başlığı ve Tema Rengi")]
        public string gameTitle = "COLOR LOAD";
        public string loadingLabel = "LOADING...";
        [Tooltip("Geçiş ekranı düz arka plan rengi (oyun teması açık lavanta #E8E7F5)")]
        public Color themeBackdropColor = new Color(0.910f, 0.906f, 0.961f, 1f);
        public Color titleColor = new Color(0.240f, 0.255f, 0.580f, 1f);
        public Color barFillColor = new Color(0.416f, 0.451f, 0.925f, 1f);
        public Color barTrackColor = new Color(1.000f, 0.988f, 0.965f, 1f);

        [Header("Zamanlama (saniye)")]
        [Tooltip("Level bittikten sonra geçiş perdesi açılmadan önceki kısa bekleme")]
        public float startDelay = 0.22f;
        [Tooltip("Düz renkli ekranın ve yükleme barının görünme süresi")]
        public float curtainFadeIn = 0.24f;
        [Tooltip("LOADING barının 0 -> 100% dolma süresi")]
        public float loadingBarDuration = 0.85f;
        [Tooltip("Bar %100 olduktan sonra toplar süzülmeye başlamadan önceki kısa bekleme")]
        public float completeHoldDuration = 0.12f;
        [Tooltip("Perde kalkma süresi")]
        public float curtainFadeOut = 0.32f;

        [Header("Süzülen Geçiş Topları")]
        [Tooltip("Ekran genişliğine sığacak sütun sayısı")]
        [Range(5, 14)] public int ballsAcrossScreen = 8;
        [Tooltip("Bar dolunca topların ekranı kaplayıp belirme süresi")]
        public float ballCoverPopDuration = 0.22f;
        [Tooltip("Topların temel aşağı süzülme hızı (dünya birimi/sn)")]
        public float glideSpeed = 8.4f;
        [Tooltip("Yumuşak yatay süzülme salınım genliği")]
        public float swayAmplitude = 0.08f;
        [Tooltip("Kamera önündeki ayrı düzlem")]
        public float ballPlaneZ = -4f;
        public float maxDrainDuration = 2.2f;

        [Header("Cam Geçişi (varsayılan)")]
        public bool useGlassSwapTransition = true;
        [Tooltip("Kutlama (cam sıkışması + parıltı) ile eski camın fırlaması arasındaki süre")]
        public float swapWinBeat = 0.18f;
        public float swapGlassExitDuration = 0.26f;
        [Tooltip("Yeni camın yukarıdan düşüş süresi (inişte sıkışma ayrıca oynar)")]
        public float swapGlassDropDuration = 0.30f;
        [Tooltip("Topların alttan yukarı dolma süresi")]
        public float swapBallFillDuration = 0.42f;
        [Tooltip("Tek bir topun belirme süresi")]
        public float swapBallPopDuration = 0.14f;
        [Tooltip("Kutuların yeni cam düşmeye başladıktan ne kadar sonra pop yapmaya başlayacağı")]
        public float swapBlocksDelay = 0.16f;
        [Tooltip("Kutuların sırayla ızgaraya oturma aralığı")]
        public float swapBlockStagger = 0.028f;
        public float swapBlockPopDuration = 0.2f;
        public Color swapCelebrationColor = new Color(0.80f, 0.78f, 1.00f, 1f);

        [Header("Loading Ekranı")]
        [Tooltip("Level geçişlerinde kutlamadan sonra perde + LOADING barı gösterilsin")]
        public bool showLoadingBetweenLevels = false;
        [Tooltip("Oyun açılırken perde + LOADING barı gösterilsin")]
        public bool showStartupLoading = true;
        [Tooltip("Açılışta barın gerçek ilerlemeye yetişme hızı: bar en erken bu sürede dolar (ilerlemenin önüne geçmez)")]
        public float startupLoadingDuration = 1.1f;

        [Header("UI Üstü Çizim")]
        public int overlaySortingOrder = 30000;

        /// <summary>Geçiş toplarının ayrılmış katmanı (ana kamera çizmez, yalnızca geçiş kamerası çizer).</summary>
        public const int TransitionLayer = 31;

        private struct GlidingBall
        {
            public Transform transform;
            public MeshRenderer renderer;
            public Vector3 basePos;
            public float popDelay;
            public float speed;
            public float accel;
            public float swayFreq;
            public float swayPhase;
            public float swayAmp;
            public Vector3 spinAxis;
            public float spinSpeed;
            public float diameter;
        }

        private readonly List<GameObject> _pool = new List<GameObject>();
        private readonly List<GlidingBall> _active = new List<GlidingBall>();
        private Transform _ballRoot;
        private Mesh _sphereMesh;
        private bool _isPlaying;
        private Coroutine _routine;

        private Camera _overlayCam;
        private RenderTexture _overlayRT;
        private Canvas _overlayCanvas;
        private CanvasGroup _curtainGroup;
        private Image _curtainBg;
        private RectTransform _barFillRect;
        private float _barFullWidth = 580f;
        private Text _titleText;
        private Text _loadingText;
        private LoadingScreenView _loadingView;
        /// <summary>Gerçek yükleme ilerlemesi: tamamlanan yükleme adımı / toplam adım. Bar bunu asla geçmez.</summary>
        private float _loadTarget;
        private RawImage _overlayImage;

        private Sprite _pillSprite;
        private Font _uiFont;
        private bool _warmedUp;
        private bool _warming;

        public static LevelTransitionController GetOrCreate()
        {
            if (Instance != null) return Instance;
            var existing = FindFirstObjectByType<LevelTransitionController>();
            if (existing != null)
            {
                Instance = existing;
                return existing;
            }
            var go = new GameObject("LevelTransitionController");
            return go.AddComponent<LevelTransitionController>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (_overlayRT != null)
            {
                _overlayRT.Release();
                Destroy(_overlayRT);
            }
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Level bittiğinde çağrılır:
        /// Düz tema ekranı + oyun adı + LOADING barı dolar -> sonraki level yüklenir -> toplar aşağı süzülür.
        /// </summary>
        public void PlayNextLevelTransition(List<PuzzleColor> colors)
        {
            if (_isPlaying) return;
            _routine = StartCoroutine(useGlassSwapTransition ? GlassSwapRoutine() : TransitionRoutine(colors));
        }

        /// <summary>
        /// Level başarısız olduğunda çağrılır:
        /// Kısa uyarı anının ardından LOADING yerine "LEVEL FAILED" yazan yükleme perdesi açılır,
        /// aynı level yeniden yüklenir ve cam giriş animasyonuyla tekrar başlar.
        /// </summary>
        public void PlayLevelFailedTransition()
        {
            if (_isPlaying) return;
            _routine = StartCoroutine(LevelFailedRoutine());
        }

        /// <summary>Geçişi anında iptal eder ve tüm geçiş objelerini gizler.</summary>
        public void CancelTransition()
        {
            if (_routine != null) StopCoroutine(_routine);
            _routine = null;
            RecycleAllActive();
            if (_curtainGroup != null) _curtainGroup.alpha = 0f;
            EndOverlay();
            RestoreSwapHidden();
            if (_swapGlass != null) { _swapGlass.DOKill(); _swapGlass.position = _swapGlassTarget; _swapGlass = null; }
            if (_overlayImage != null) _overlayImage.enabled = true;
            _isPlaying = false;
        }

        /// <summary>
        /// İlk geçişte takılma olmaması için geçiş kamerası, canvas, top havuzu ve shader'ları önceden ısıtır.
        /// </summary>
        public void BeginWarmUp()
        {
            if (_warmedUp || _warming || !Application.isPlaying) return;
            if (useGlassSwapTransition) return; // cam geçişi ek kaynak gerektirmez (top havuzu / perde kamerası kurulmaz)
            StartCoroutine(WarmUpRoutine());
        }

        private IEnumerator WarmUpRoutine()
        {
            _warming = true;
            yield return null;
            yield return null;

            var gm = PuzzleGlassGameManager.Instance;
            Camera cam = (gm != null && gm.mainCamera != null) ? gm.mainCamera : Camera.main;
            if (cam == null) { _warming = false; yield break; }

            EnsureResources();
            EnsureOverlay(cam);
            yield return null;

            int targetCount = ComputeFullScreenBallCount(cam);
            const int perFrame = 18;
            while (_pool.Count + _active.Count < targetCount)
            {
                if (_isPlaying) { _warming = false; yield break; }
                int n = Mathf.Min(perFrame, targetCount - (_pool.Count + _active.Count));
                for (int i = 0; i < n; i++)
                {
                    var go = CreateBallObject();
                    go.SetActive(false);
                    _pool.Add(go);
                }
                yield return null;
            }

            if (!_isPlaying && BallObjectPool.Instance != null)
            {
                var colors = (PuzzleColor[])System.Enum.GetValues(typeof(PuzzleColor));
                var shown = new List<GameObject>();
                float d = (cam.orthographicSize * 2f * cam.aspect) / ballsAcrossScreen;
                Vector3 c = cam.transform.position;
                for (int i = 0; i < colors.Length && i < _pool.Count; i++)
                {
                    var go = _pool[i];
                    var mr = go.GetComponent<MeshRenderer>();
                    var mat = BallObjectPool.Instance.SharedInstancedBallMaterial;
                    if (mr != null && mat != null)
                    {
                        mr.sharedMaterial = mat;
                        BallObjectPool.ApplyBallPropertyBlock(mr, colors[i], variantIndex: 1, wobblePhase: i * 0.47f);
                    }
                    go.transform.position = new Vector3(c.x + (i - colors.Length * 0.5f) * d * 0.5f, c.y, ballPlaneZ);
                    go.transform.localScale = Vector3.one * d;
                    go.SetActive(true);
                    shown.Add(go);
                }

                _overlayCam.enabled = true;
                yield return new WaitForEndOfFrame();
                yield return null;
                if (!_isPlaying)
                {
                    _overlayCam.enabled = false;
                    foreach (var go in shown) go.SetActive(false);
                }
            }

            _warmedUp = true;
            _warming = false;
        }

        private IEnumerator TransitionRoutine(List<PuzzleColor> colors)
        {
            _isPlaying = true;

            var gm = PuzzleGlassGameManager.Instance;
            Camera cam = (gm != null && gm.mainCamera != null) ? gm.mainCamera : Camera.main;
            if (cam == null || gm == null)
            {
                _isPlaying = false;
                if (gm != null) gm.LoadNextLevel();
                yield break;
            }

            if (colors == null || colors.Count == 0)
            {
                colors = new List<PuzzleColor>((PuzzleColor[])System.Enum.GetValues(typeof(PuzzleColor)));
            }

            EnsureResources();
            EnsureOverlay(cam);

            int ballCount = ComputeFullScreenBallCount(cam);
            Prewarm(ballCount);

            if (startDelay > 0f) yield return new WaitForSeconds(startDelay);

            // --- 1. Düz tema perdesi + oyun adı + LOADING bar açılır ---
            BeginOverlay(cam, showCurtain: true);
            SetLoadingProgress(0f);

            float fadeInT = 0f;
            float fadeInDur = Mathf.Max(0.05f, curtainFadeIn);
            while (fadeInT < fadeInDur)
            {
                fadeInT += Time.deltaTime;
                if (_curtainGroup != null) _curtainGroup.alpha = Mathf.SmoothStep(0f, 1f, fadeInT / fadeInDur);
                yield return null;
            }
            if (_curtainGroup != null) _curtainGroup.alpha = 1f;

            // --- 2. LOADING bar dolarken perde arkasında sonraki level sessizce yüklenir ---
            bool nextLoaded = false;
            float loadDur = Mathf.Max(0.20f, loadingBarDuration);
            float loadT = 0f;
            while (loadT < loadDur)
            {
                loadT += Time.deltaTime;
                float u = Mathf.Clamp01(loadT / loadDur);
                float eased = u < 0.5f ? 2f * u * u : 1f - Mathf.Pow(-2f * u + 2f, 2f) * 0.5f;
                SetLoadingProgress(eased);

                if (!nextLoaded && u >= 0.42f)
                {
                    nextLoaded = true;
                    gm.CancelInvoke(nameof(PuzzleGlassGameManager.LoadNextLevel));
                    gm.LoadNextLevel();
                }
                yield return null;
            }

            if (!nextLoaded)
            {
                gm.CancelInvoke(nameof(PuzzleGlassGameManager.LoadNextLevel));
                gm.LoadNextLevel();
            }
            SetLoadingProgress(1f);

            if (completeHoldDuration > 0f) yield return new WaitForSeconds(completeHoldDuration);

            // --- 3. Bar %100 olunca düz perdenin üstünde tüm ekranı boşluksuz kaplayan toplar belirir ---
            SpawnFullScreenBlanket(cam, colors);

            float popTotalDur = Mathf.Max(0.10f, ballCoverPopDuration);
            const float singleBallPopDur = 0.12f;
            float popT = 0f;
            while (popT < popTotalDur + singleBallPopDur)
            {
                popT += Time.deltaTime;
                for (int i = 0; i < _active.Count; i++)
                {
                    GlidingBall b = _active[i];
                    if (b.transform == null) continue;
                    float localT = Mathf.Clamp01((popT - b.popDelay) / singleBallPopDur);
                    float s = EaseOutBack(localT);
                    b.transform.localScale = Vector3.one * (b.diameter * s);
                }
                yield return null;
            }
            for (int i = 0; i < _active.Count; i++)
            {
                if (_active[i].transform != null)
                    _active[i].transform.localScale = Vector3.one * _active[i].diameter;
            }

            // Ekran toplarla %100 kaplandıktan sonra alttaki düz perde kalkar (oyun henüz tamamen gizlidir)
            if (_curtainGroup != null)
            {
                _curtainGroup.alpha = 0f;
                _curtainGroup.blocksRaycasts = true;
            }

            // --- 4. Tüm ekranı kaplayan top perdesi aynı anda aşağı doğru süzülerek yeni leveli ortaya çıkarır ---
            float halfH = cam.orthographicSize;
            float killY = cam.transform.position.y - halfH - 1.2f;
            float revealT = 0f;

            while (_active.Count > 0 && revealT < maxDrainDuration)
            {
                float dt = Time.deltaTime;
                revealT += dt;

                if (_curtainGroup != null && revealT > 0.25f)
                {
                    _curtainGroup.blocksRaycasts = false;
                }

                for (int i = _active.Count - 1; i >= 0; i--)
                {
                    GlidingBall b = _active[i];
                    if (b.transform == null)
                    {
                        _active.RemoveAt(i);
                        continue;
                    }

                    b.speed += b.accel * dt;
                    b.basePos.y -= b.speed * dt;

                    float swayOffset = Mathf.Sin(revealT * b.swayFreq + b.swayPhase) * b.swayAmp;
                    b.transform.position = new Vector3(b.basePos.x + swayOffset, b.basePos.y, b.basePos.z);
                    b.transform.Rotate(b.spinAxis, b.spinSpeed * dt, Space.World);
                    _active[i] = b;

                    if (b.basePos.y < killY - b.diameter)
                    {
                        RecycleAt(i);
                    }
                }

                yield return null;
            }

            RecycleAllActive();
            EndOverlay();
            _isPlaying = false;
            _routine = null;
        }

        // ===================================== CAM GEÇİŞİ =====================================

        private struct HiddenVisual
        {
            public Transform tr;
            public Vector3 baseScale;
            public float delay;
        }

        private readonly List<HiddenVisual> _swapBalls = new List<HiddenVisual>();
        private readonly HashSet<Transform> _swapBallSet = new HashSet<Transform>();
        private readonly List<HiddenVisual> _swapBlocks = new List<HiddenVisual>();

        private IEnumerator GlassSwapRoutine()
        {
            _isPlaying = true;
            var gm = PuzzleGlassGameManager.Instance;
            Camera cam = (gm != null && gm.mainCamera != null) ? gm.mainCamera : Camera.main;
            if (gm == null || cam == null)
            {
                _isPlaying = false;
                if (gm != null) gm.LoadNextLevel();
                yield break;
            }
            float screenH = cam.orthographicSize * 2f;

            // 1. Kutlama: boş cam sıkışıp esner + merkezinde kısa parıltı patlaması (kutu dolma efektinin büyüğü)
            Transform oldGlass = GetGlassVisualRoot(gm);
            if (oldGlass != null)
            {
                oldGlass.DOKill(true);
                oldGlass.DOPunchScale(new Vector3(0.07f, -0.06f, 0f), 0.22f, 6, 0.55f).SetLink(oldGlass.gameObject);
                BlockClearCelebration.Play(GetVisualCenter(oldGlass), swapCelebrationColor, 1.8f);
            }
            if (swapWinBeat > 0f) yield return new WaitForSeconds(swapWinBeat);

            if (showLoadingBetweenLevels)
            {
                // 2a. Perde + oyun adı + LOADING barı; bar dolarken perde arkasında sonraki level yüklenir
                yield return CurtainLoadingRoutine(curtainFadeIn, loadingBarDuration, ReloadLevelLoadWork(gm, screenH, advanceToNext: true));
            }
            else
            {
                // 2b. Eski cam yukarı fırlar (hafif geri çekilip); camda kalan top varsa küçülür
                if (oldGlass != null)
                {
                    oldGlass.DOKill(true);
                    oldGlass.DOMoveY(oldGlass.position.y + screenH * 1.1f, swapGlassExitDuration)
                        .SetEase(Ease.InBack, 1.4f).SetLink(oldGlass.gameObject);
                }
                var pool = BallObjectPool.Instance;
                if (pool != null && pool.ActiveBalls != null)
                {
                    foreach (var b in pool.ActiveBalls)
                    {
                        if (b == null || b.visualTransform == null || b.visualTransform == b.transform) continue;
                        b.visualTransform.DOScale(Vector3.zero, swapGlassExitDuration * 0.8f).SetEase(Ease.InBack).SetLink(b.gameObject);
                    }
                }
                yield return new WaitForSeconds(swapGlassExitDuration);
                gm.CancelInvoke(nameof(PuzzleGlassGameManager.LoadNextLevel));
                gm.LoadNextLevel();
                PrepareEntrance(gm, screenH);
            }

            // 3. Perde kalkarken yeni level girer
            yield return EntranceRoutine(fadeCurtainOut: showLoadingBetweenLevels);
            _isPlaying = false;
            _routine = null;
        }

        private IEnumerator LevelFailedRoutine()
        {
            _isPlaying = true;
            var gm = PuzzleGlassGameManager.Instance;
            Camera cam = (gm != null && gm.mainCamera != null) ? gm.mainCamera : Camera.main;
            if (gm == null || cam == null)
            {
                _isPlaying = false;
                if (gm != null) gm.RestartCurrentPattern();
                yield break;
            }
            float screenH = cam.orthographicSize * 2f;

            // Oyuncunun kilitlenme geri bildirimini (sallanma + ses) görebilmesi için kısa bekleme
            yield return new WaitForSeconds(0.55f);
            if (!gm.isLevelFailed)
            {
                _isPlaying = false;
                _routine = null;
                yield break;
            }

            // "LOADING" yerine "LEVEL FAILED" belirten yükleme ekranı açılır, arka planda aynı level yeniden yüklenir
            yield return CurtainLoadingRoutine(
                curtainFadeIn,
                Mathf.Max(0.90f, loadingBarDuration),
                ReloadLevelLoadWork(gm, screenH, advanceToNext: false),
                statusText: "LEVEL FAILED",
                isFailed: true);

            yield return EntranceRoutine(fadeCurtainOut: true);
            _isPlaying = false;
            _routine = null;
        }

        /// <summary>Oyun açılışında: perde + oyun adı + LOADING barı, sonra ilk level giriş animasyonuyla açılır.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartupBoot()
        {
            var gm = PuzzleGlassGameManager.Instance != null ? PuzzleGlassGameManager.Instance : FindFirstObjectByType<PuzzleGlassGameManager>();
            if (gm == null || !gm.useBallPourTransition) return;
            var c = GetOrCreate();
            if (!c.showStartupLoading || !c.useGlassSwapTransition) return;
            c._routine = c.StartCoroutine(c.StartupLoadingRoutine());
        }

        private IEnumerator StartupLoadingRoutine()
        {
            _isPlaying = true;
            // İlk kareden itibaren perde tam kapalı (oyun hiç görünmeden)
            ShowCurtain(1f);
            yield return CurtainLoadingRoutine(0f, startupLoadingDuration, StartupLoadWork());
            yield return EntranceRoutine(fadeCurtainOut: true);
            _isPlaying = false;
            _routine = null;
        }

        /// <summary>Açılıştaki gerçek yükleme adımları (her biri tamamlanınca ilerleme artar).</summary>
        private IEnumerator StartupLoadWork()
        {
            const float steps = 4f;
            // 1) Sahnedeki yöneticilerin Start'ı çalıştı: ilk level (cam, toplar, kutular, slotlar) kuruldu
            yield return null;
            var gm = PuzzleGlassGameManager.Instance;
            Camera cam = (gm != null && gm.mainCamera != null) ? gm.mainCamera : Camera.main;
            if (gm != null && cam != null) PrepareEntrance(gm, cam.orthographicSize * 2f);
            _loadTarget = 1f / steps;
            // 2) İlk kare çizildi: shader'lar, mesh'ler ve dokular GPU'ya yüklendi
            yield return new WaitForEndOfFrame();
            _loadTarget = 2f / steps;
            // 3) Yeni collider'larla ilk fizik adımı tamamlandı
            yield return new WaitForFixedUpdate();
            _loadTarget = 3f / steps;
            // 4) Bir kare daha: geç doğan nesneler (top havuzu, HUD) yerleşti
            yield return null;
            _loadTarget = 1f;
        }

        /// <summary>Level geçişindeki veya fail sonrası yeniden başlatmadaki gerçek yükleme adımları.</summary>
        private IEnumerator ReloadLevelLoadWork(PuzzleGlassGameManager gm, float screenH, bool advanceToNext)
        {
            const float steps = 3f;
            // 1) Hedef level üretildi/yüklendi ve sahnede kuruldu
            gm.CancelInvoke(nameof(PuzzleGlassGameManager.LoadNextLevel));
            if (advanceToNext)
                gm.LoadNextLevel();
            else
                gm.RestartCurrentPattern();
            PrepareEntrance(gm, screenH);
            _loadTarget = 1f / steps;
            // 2) Yeni level'ın ilk karesi çizildi (yeni şekil, mesafe dokusu, malzemeler GPU'da)
            yield return new WaitForEndOfFrame();
            _loadTarget = 2f / steps;
            // 3) Yeni collider'larla ilk fizik adımı tamamlandı
            yield return new WaitForFixedUpdate();
            _loadTarget = 1f;
        }

        /// <summary>
        /// Perdeyi açar (fadeIn &gt; 0 ise yumuşakça), yükleme işini çalıştırır ve barı GERÇEK ilerlemeyle doldurur:
        /// gösterilen değer _loadTarget'a (tamamlanan adım / toplam adım) doğru akıcı ilerler ama onu asla geçmez.
        /// minFillTime yalnızca barın en hızlı dolma süresidir. %100'de kısa tamamlanma animasyonu oynar; perde açık kalır.
        /// </summary>
        private IEnumerator CurtainLoadingRoutine(float fadeIn, float minFillTime, IEnumerator work, string statusText = "LOADING", bool isFailed = false)
        {
            _loadTarget = 0f;
            if (fadeIn > 0f)
            {
                ShowCurtain(0f, statusText, isFailed);
                float ft = 0f;
                while (ft < fadeIn)
                {
                    ft += Time.deltaTime;
                    _curtainGroup.alpha = Mathf.SmoothStep(0f, 1f, ft / fadeIn);
                    yield return null;
                }
            }
            ShowCurtain(1f, statusText, isFailed);
            SetLoadingProgress(0f);

            bool workDone = false;
            StartCoroutine(RunLoadWork(work, () => workDone = true));
            float shown = 0f;
            float speed = 1f / Mathf.Max(0.1f, minFillTime);
            while (!workDone || shown < 0.9999f)
            {
                shown = Mathf.MoveTowards(shown, _loadTarget, Time.deltaTime * speed);
                SetLoadingProgress(shown);
                HideNewBalls(); // perde arkasında doğan toplar da gizli başlasın
                yield return null;
            }
            SetLoadingProgress(1f);
            if (_loadingView != null) yield return _loadingView.PlayComplete();
            else if (completeHoldDuration > 0f) yield return new WaitForSeconds(completeHoldDuration);
        }

        private IEnumerator RunLoadWork(IEnumerator work, System.Action onDone)
        {
            if (work != null) yield return work;
            _loadTarget = 1f;
            onDone();
        }

        /// <summary>Sadece tema perdesi (başlık + bar). Eski geçişin kamera/RenderTexture'ı kurulmaz.</summary>
        private void ShowCurtain(float alpha, string statusText = "LOADING", bool isFailed = false)
        {
            EnsureResources();
            if (_overlayCanvas == null) BuildTransitionCanvas();
            if (_overlayImage != null) _overlayImage.enabled = false;
            _overlayCanvas.sortingOrder = overlaySortingOrder;
            if (_curtainBg != null) _curtainBg.color = themeBackdropColor;
            if (_loadingView != null)
            {
                _loadingView.SetBackdropColor(themeBackdropColor);
                _loadingView.SetStatus(statusText, isFailed);
            }
            if (_titleText != null) { _titleText.text = gameTitle; _titleText.color = titleColor; }
            bool wasHidden = !_overlayCanvas.gameObject.activeSelf || (_curtainGroup != null && _curtainGroup.alpha <= 0.001f);
            if (_curtainGroup != null)
            {
                _curtainGroup.DOKill();
                _curtainGroup.alpha = alpha;
                _curtainGroup.blocksRaycasts = true;
            }
            _overlayCanvas.gameObject.SetActive(true);
            if (wasHidden && _loadingView != null) _loadingView.PlayIntro();
        }

        private void HideCurtain(float duration)
        {
            if (_overlayCanvas == null || !_overlayCanvas.gameObject.activeSelf || _curtainGroup == null) return;
            _curtainGroup.DOKill();
            _curtainGroup.blocksRaycasts = false;
            if (_loadingView != null) _loadingView.PlayOutro(duration);
            var canvasGo = _overlayCanvas.gameObject;
            _curtainGroup.DOFade(0f, duration).SetEase(Ease.OutQuad).OnComplete(() =>
            {
                if (canvasGo != null) canvasGo.SetActive(false);
                if (_overlayImage != null) _overlayImage.enabled = true;
            });
        }

        private Transform _swapGlass;
        private Vector3 _swapGlassTarget;

        /// <summary>Yeni level'ın görsellerini gizli başlatır: cam ekranın üstünde, toplar ve kutular sıfır boyutta.</summary>
        private void PrepareEntrance(PuzzleGlassGameManager gm, float screenH)
        {
            _swapBalls.Clear();
            _swapBallSet.Clear();
            _swapBlocks.Clear();
            _swapGlass = GetGlassVisualRoot(gm);
            _swapGlassTarget = _swapGlass != null ? _swapGlass.position : Vector3.zero;
            if (_swapGlass != null)
            {
                _swapGlass.DOKill(true);
                _swapGlass.position = _swapGlassTarget + Vector3.up * screenH * 0.75f;
            }
            HideNewBalls();
            HideNewBlocks(gm);
        }

        /// <summary>Yeni cam düşer ve inişte sıkışır; kutular pop yapar, toplar alttan dolar. Bitince her şey tam boyutta.</summary>
        private IEnumerator EntranceRoutine(bool fadeCurtainOut)
        {
            if (fadeCurtainOut) HideCurtain(Mathf.Max(0.05f, curtainFadeOut));

            Transform newGlass = _swapGlass;
            if (newGlass != null)
            {
                Sequence drop = DOTween.Sequence().SetLink(newGlass.gameObject);
                drop.Append(newGlass.DOMove(_swapGlassTarget, swapGlassDropDuration).SetEase(Ease.InQuad));
                drop.Append(newGlass.DOPunchScale(new Vector3(0.08f, -0.09f, 0f), 0.24f, 5, 0.6f));
            }
            float t = 0f;
            bool blocksStarted = false;
            while (t < swapGlassDropDuration)
            {
                t += Time.deltaTime;
                HideNewBalls();
                if (!blocksStarted && t >= swapBlocksDelay)
                {
                    blocksStarted = true;
                    PopBlocks();
                }
                yield return null;
            }
            if (!blocksStarted) PopBlocks();
            float blocksEnd = swapBlocksDelay + _swapBlocks.Count * swapBlockStagger + swapBlockPopDuration;

            // Cam yere oturur oturmaz toplar alttan yukarı (sıvı dolar gibi) belirir
            AssignBallFillDelays();
            float fillT = 0f;
            float fillEnd = swapBallFillDuration + swapBallPopDuration;
            while (fillT < fillEnd)
            {
                fillT += Time.deltaTime;
                HideNewBalls(lateDelay: fillT);
                for (int i = 0; i < _swapBalls.Count; i++)
                {
                    var h = _swapBalls[i];
                    if (h.tr == null) continue;
                    float u = Mathf.Clamp01((fillT - h.delay) / Mathf.Max(0.01f, swapBallPopDuration));
                    h.tr.localScale = h.baseScale * EaseOutBack(u);
                }
                yield return null;
            }
            RestoreSwapBalls();

            // Kutuların hepsi oturana kadar girdi kilitli (yarım ölçekte sürükleme/kayıt olmasın)
            float remaining = blocksEnd - (swapGlassDropDuration + fillT);
            if (remaining > 0f) yield return new WaitForSeconds(remaining);
            foreach (var h in _swapBlocks) if (h.tr != null) { h.tr.DOKill(); h.tr.localScale = h.baseScale; }
            _swapBlocks.Clear();
            _swapGlass = null;
        }

        /// <summary>Cam görsellerinin ekrandaki merkezi (kök pivotu camın ortasında olmayabilir).</summary>
        private static Vector3 GetVisualCenter(Transform root)
        {
            var rends = root.GetComponentsInChildren<MeshRenderer>();
            bool any = false;
            Bounds b = new Bounds(root.position, Vector3.zero);
            foreach (var r in rends)
            {
                if (r == null || !r.enabled) continue;
                string n = r.gameObject.name;
                if (n.Contains("Shadow") || n.Contains("Backdrop") || n.Contains("Showcase")) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any ? b.center : root.position;
        }

        private static Transform GetGlassVisualRoot(PuzzleGlassGameManager gm)
        {
            var container = gm != null && gm.glassContainer != null ? gm.glassContainer : FindFirstObjectByType<ShapedGlassContainer>();
            return container != null ? container.GlassVisualRoot : null;
        }

        /// <summary>Henüz işlenmemiş aktif topların görselini gizler (temel ölçeğini saklar).</summary>
        private void HideNewBalls(float lateDelay = -1f)
        {
            var pool = BallObjectPool.Instance;
            if (pool == null || pool.ActiveBalls == null) return;
            foreach (var b in pool.ActiveBalls)
            {
                if (b == null) continue;
                Transform v = b.visualTransform;
                if (v == null || v == b.transform || _swapBallSet.Contains(v)) continue;
                v.DOKill();
                Vector3 baseScale = v.localScale.sqrMagnitude > 1e-6f ? v.localScale : Vector3.one;
                _swapBallSet.Add(v);
                _swapBalls.Add(new HiddenVisual { tr = v, baseScale = baseScale, delay = lateDelay >= 0f ? lateDelay : 0f });
                v.localScale = Vector3.zero;
            }
        }

        /// <summary>Toplara alttan yukarı (sıvı dolar gibi) belirme gecikmesi verir.</summary>
        private void AssignBallFillDelays()
        {
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var h in _swapBalls)
            {
                if (h.tr == null) continue;
                float y = h.tr.position.y;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
            float span = Mathf.Max(0.01f, maxY - minY);
            float window = Mathf.Max(0f, swapBallFillDuration - swapBallPopDuration * 0.5f);
            for (int i = 0; i < _swapBalls.Count; i++)
            {
                var h = _swapBalls[i];
                if (h.tr == null) continue;
                float t01 = (h.tr.position.y - minY) / span;
                float jitter = (Mathf.Abs(Mathf.Sin(i * 12.9898f) * 43758.547f) % 1f) * 0.08f;
                h.delay = Mathf.Clamp01(t01 + jitter) * window;
                _swapBalls[i] = h;
            }
        }

        private void HideNewBlocks(PuzzleGlassGameManager gm)
        {
            if (gm.activeMonsters == null) return;
            // Kutulara slota yakın (üst) sıradan başlayarak oturma sırası ver
            var list = new List<MonsterCharacter>();
            foreach (var m in gm.activeMonsters) if (m != null && m.gameObject.activeInHierarchy) list.Add(m);
            list.Sort((a, b) =>
            {
                int c = b.transform.position.y.CompareTo(a.transform.position.y);
                return c != 0 ? c : a.transform.position.x.CompareTo(b.transform.position.x);
            });
            for (int i = 0; i < list.Count; i++)
            {
                Transform tr = list[i].transform;
                tr.DOKill();
                _swapBlocks.Add(new HiddenVisual { tr = tr, baseScale = tr.localScale, delay = i * swapBlockStagger });
                tr.localScale = Vector3.zero;
            }
        }

        private void PopBlocks()
        {
            foreach (var h in _swapBlocks)
            {
                if (h.tr == null) continue;
                h.tr.DOScale(h.baseScale, swapBlockPopDuration).SetDelay(h.delay).SetEase(Ease.OutBack, 1.6f).SetLink(h.tr.gameObject);
            }
            PuzzleSoundManager.Instance?.PlayBlockSnap();
        }

        private void RestoreSwapBalls()
        {
            foreach (var h in _swapBalls) if (h.tr != null) h.tr.localScale = h.baseScale;
            _swapBalls.Clear();
            _swapBallSet.Clear();
        }

        /// <summary>Geçiş yarıda kesilirse gizlenen her şeyi eski boyutuna döndürür.</summary>
        private void RestoreSwapHidden()
        {
            RestoreSwapBalls();
            foreach (var h in _swapBlocks)
            {
                if (h.tr == null) continue;
                h.tr.DOKill();
                h.tr.localScale = h.baseScale;
            }
            _swapBlocks.Clear();
        }

        private void ComputeFullScreenGridParams(Camera cam, out int colsPlusOne, out int rows, out float colStep, out float rowStep, out float diameter, out float left, out float bottom)
        {
            float halfH = cam.orthographicSize;
            float halfW = halfH * cam.aspect;
            Vector3 camPos = cam.transform.position;
            left = camPos.x - halfW;
            bottom = camPos.y - halfH;

            int cols = Mathf.Max(5, ballsAcrossScreen);
            colStep = (halfW * 2f) / cols;
            // Çap colStep'ten biraz geniş tutulur: komşu toplar arası hiç boşluk kalmaz, tüm ekran kaplanır
            diameter = colStep * 1.24f;
            rowStep = colStep * 0.86f;
            colsPlusOne = cols + 2;
            rows = Mathf.CeilToInt((halfH * 2f + diameter * 1.4f) / rowStep) + 2;
        }

        private int ComputeFullScreenBallCount(Camera cam)
        {
            if (cam == null) return 220;
            ComputeFullScreenGridParams(cam, out int colsPlusOne, out int rows, out _, out _, out _, out _, out _);
            return colsPlusOne * rows;
        }

        private void SpawnFullScreenBlanket(Camera cam, List<PuzzleColor> colors)
        {
            ComputeFullScreenGridParams(cam, out int colsPlusOne, out int rows, out float colStep, out float rowStep, out float diameter, out float left, out float bottom);

            float popWindow = Mathf.Max(0.05f, ballCoverPopDuration);
            float startX = left - colStep * 0.5f;
            float startY = bottom - diameter * 0.25f;

            for (int r = 0; r < rows; r++)
            {
                float rowNorm = (float)r / Mathf.Max(1, rows - 1);
                float stagger = (r % 2 == 1) ? colStep * 0.5f : 0f;
                for (int c = 0; c < colsPlusOne; c++)
                {
                    float x = startX + c * colStep + stagger + Random.Range(-colStep * 0.06f, colStep * 0.06f);
                    float y = startY + r * rowStep + Random.Range(-rowStep * 0.06f, rowStep * 0.06f);
                    float z = ballPlaneZ + (((r + c) & 1) == 0 ? 0f : -0.045f);

                    PuzzleColor col = colors[Random.Range(0, colors.Count)];
                    // Üstten aşağı dalga hâlinde açılış
                    float popDelay = (1f - rowNorm) * popWindow * 0.72f + Random.Range(0f, popWindow * 0.28f);
                    SpawnGlidingBall(new Vector3(x, y, z), diameter, col, popDelay);
                }
            }
        }

        private void SpawnGlidingBall(Vector3 pos, float diameter, PuzzleColor color, float popDelay)
        {
            GameObject go;
            if (_pool.Count > 0)
            {
                go = _pool[_pool.Count - 1];
                _pool.RemoveAt(_pool.Count - 1);
            }
            else
            {
                go = CreateBallObject();
            }

            go.transform.position = pos;
            go.transform.rotation = Random.rotation;
            go.transform.localScale = Vector3.zero;

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null && BallObjectPool.Instance != null)
            {
                Material mat = BallObjectPool.Instance.SharedInstancedBallMaterial;
                if (mat != null)
                {
                    mr.sharedMaterial = mat;
                    BallObjectPool.ApplyBallPropertyBlock(mr, color, variantIndex: 1, wobblePhase: _active.Count * 0.47f);
                }
            }

            go.SetActive(true);
            _active.Add(new GlidingBall
            {
                transform = go.transform,
                renderer = mr,
                basePos = pos,
                popDelay = popDelay,
                speed = glideSpeed * Random.Range(0.92f, 1.12f),
                accel = Random.Range(3.2f, 4.8f),
                swayFreq = Random.Range(2.2f, 3.6f),
                swayPhase = Random.Range(0f, Mathf.PI * 2f),
                swayAmp = swayAmplitude * Random.Range(0.5f, 1.1f),
                spinAxis = Random.onUnitSphere,
                spinSpeed = Random.Range(75f, 180f),
                diameter = diameter
            });
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        private void Prewarm(int count)
        {
            int needed = count - (_pool.Count + _active.Count);
            for (int i = 0; i < needed; i++)
            {
                var go = CreateBallObject();
                go.SetActive(false);
                _pool.Add(go);
            }
        }

        private GameObject CreateBallObject()
        {
            var go = new GameObject("TransitionBall");
            go.layer = TransitionLayer;
            go.transform.SetParent(_ballRoot, false);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = _sphereMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        private void RecycleAt(int index)
        {
            var b = _active[index];
            _active.RemoveAt(index);
            if (b.transform == null) return;
            b.transform.gameObject.SetActive(false);
            _pool.Add(b.transform.gameObject);
        }

        private void RecycleAllActive()
        {
            for (int i = _active.Count - 1; i >= 0; i--) RecycleAt(i);
        }

        private void EnsureResources()
        {
            if (_ballRoot == null)
            {
                var root = new GameObject("TransitionBalls");
                root.transform.SetParent(transform, false);
                _ballRoot = root.transform;
            }

            if (_sphereMesh == null)
            {
                GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                var mf = temp.GetComponent<MeshFilter>();
                if (mf != null) _sphereMesh = mf.sharedMesh;
                Destroy(temp);
            }

            if (_uiFont == null)
            {
                _uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (_uiFont == null) _uiFont = Font.CreateDynamicFontFromOSFont("Arial", 48);
            }

            if (_pillSprite == null)
            {
                _pillSprite = CreateRoundedPillSprite(96, 48, 22);
            }
        }

        private void BeginOverlay(Camera mainCam, bool showCurtain)
        {
            EnsureOverlay(mainCam);
            _overlayCam.enabled = true;
            _overlayCanvas.sortingOrder = overlaySortingOrder;
            _overlayImage.texture = _overlayRT;

            if (_curtainBg != null) _curtainBg.color = themeBackdropColor;
            if (_loadingView != null) _loadingView.SetBackdropColor(themeBackdropColor);
            if (_titleText != null)
            {
                _titleText.text = gameTitle;
                _titleText.color = titleColor;
            }
            if (_loadingText != null) _loadingText.text = loadingLabel;
            if (_curtainGroup != null)
            {
                _curtainGroup.alpha = showCurtain ? 0f : 0f;
                _curtainGroup.blocksRaycasts = showCurtain;
            }

            _overlayCanvas.gameObject.SetActive(true);
        }

        private void EndOverlay()
        {
            if (_overlayCam != null) _overlayCam.enabled = false;
            if (_overlayCanvas != null) _overlayCanvas.gameObject.SetActive(false);
        }

        private void SetLoadingProgress(float progress01)
        {
            if (_loadingView != null) { _loadingView.SetProgress(progress01); return; }
            if (_barFillRect == null) return;
            float p = Mathf.Clamp01(progress01);
            float minW = p > 0.001f ? Mathf.Max(28f, _barFullWidth * p) : 0f;
            _barFillRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Min(_barFullWidth, minW));

            if (_loadingText != null)
            {
                int pct = Mathf.RoundToInt(p * 100f);
                _loadingText.text = $"{loadingLabel}  {pct}%";
            }
        }

        private void EnsureOverlay(Camera mainCam)
        {
            mainCam.cullingMask &= ~(1 << TransitionLayer);

            int w = Mathf.Max(16, Screen.width);
            int h = Mathf.Max(16, Screen.height);
            if (_overlayRT == null || _overlayRT.width != w || _overlayRT.height != h)
            {
                if (_overlayRT != null)
                {
                    _overlayRT.Release();
                    Destroy(_overlayRT);
                }
                _overlayRT = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
                {
                    name = "RT_LevelTransition",
                    antiAliasing = 4
                };
                _overlayRT.Create();
            }

            if (_overlayCam == null)
            {
                var camGo = new GameObject("TransitionCamera");
                camGo.transform.SetParent(transform, false);
                _overlayCam = camGo.AddComponent<Camera>();
                _overlayCam.clearFlags = CameraClearFlags.SolidColor;
                _overlayCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                _overlayCam.cullingMask = 1 << TransitionLayer;
                _overlayCam.allowHDR = false;
                _overlayCam.enabled = false;
            }

            _overlayCam.transform.SetPositionAndRotation(mainCam.transform.position, mainCam.transform.rotation);
            _overlayCam.orthographic = mainCam.orthographic;
            _overlayCam.orthographicSize = mainCam.orthographicSize;
            _overlayCam.fieldOfView = mainCam.fieldOfView;
            _overlayCam.nearClipPlane = mainCam.nearClipPlane;
            _overlayCam.farClipPlane = mainCam.farClipPlane;
            _overlayCam.depth = mainCam.depth - 1;
            _overlayCam.targetTexture = _overlayRT;

            if (_overlayCanvas == null)
            {
                BuildTransitionCanvas();
            }

            _overlayImage.texture = _overlayRT;
        }

        private void BuildTransitionCanvas()
        {
            var canvasGo = new GameObject("TransitionOverlayCanvas");
            canvasGo.transform.SetParent(transform, false);
            _overlayCanvas = canvasGo.AddComponent<Canvas>();
            _overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _overlayCanvas.sortingOrder = overlaySortingOrder;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();

            // 1) Loading ekranı (tek renk arka plan, Color Load logosu, kapsül bar) — LoadingScreenView
            _loadingView = LoadingScreenView.Create(canvasGo.transform, _uiFont, gameTitle, _pillSprite, themeBackdropColor);
            _curtainGroup = _loadingView.Group;
            _curtainBg = null;
            _titleText = null;
            _loadingText = null;
            _barFillRect = null;

            // 2) En üstte şeffaf arka planlı 3D süzülen top katmanı
            var imgGo = new GameObject("TransitionBallsOverlay");
            imgGo.transform.SetParent(canvasGo.transform, false);
            _overlayImage = imgGo.AddComponent<RawImage>();
            StretchFill(_overlayImage.rectTransform);
            _overlayImage.raycastTarget = false;

            canvasGo.SetActive(false);
        }

        private static void StretchFill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static Sprite CreateRoundedPillSprite(int width, int height, int radius)
        {
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            Color32[] pixels = new Color32[width * height];
            float r = Mathf.Min(radius, Mathf.Min(width, height) * 0.5f);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float px = x + 0.5f;
                    float py = y + 0.5f;
                    float dx = Mathf.Max(0f, Mathf.Abs(px - width * 0.5f) - (width * 0.5f - r));
                    float dy = Mathf.Max(0f, Mathf.Abs(py - height * 0.5f) - (height * 0.5f - r));
                    float dist = Mathf.Sqrt(dx * dx + dy * dy) - r;
                    float a = Mathf.Clamp01(0.5f - dist);
                    pixels[y * width + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            Vector4 border = new Vector4(radius, radius, radius, radius);
            return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        }
    }
}
