using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PuzzleGlass
{
    /// <summary>
    /// Level geçişi:
    /// 1) Temaya uygun düz renkli (#E8E7F5) geçiş perdesi açılır; üzerinde oyun adı ve "LOADING" barı dolar.
    /// 2) Perde kapalıyken sonraki level arka planda yüklenir.
    /// 3) Bar dolunca perde kalkar ve renkli 3D toplar ekranın üstünden aşağı doğru yumuşakça süzülerek yeni leveli ortaya çıkarır.
    /// </summary>
    public class LevelTransitionController : MonoBehaviour
    {
        public static LevelTransitionController Instance { get; private set; }

        /// <summary>Geçiş oynarken true. Bu sürede oyuncu girdisi kilitlenir.</summary>
        public static bool IsTransitioning => Instance != null && Instance._isPlaying;

        [Header("Oyun Başlığı ve Tema Rengi")]
        public string gameTitle = "PUZZLE GLASS";
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
            _routine = StartCoroutine(TransitionRoutine(colors));
        }

        /// <summary>Geçişi anında iptal eder ve tüm geçiş objelerini gizler.</summary>
        public void CancelTransition()
        {
            if (_routine != null) StopCoroutine(_routine);
            _routine = null;
            RecycleAllActive();
            if (_curtainGroup != null) _curtainGroup.alpha = 0f;
            EndOverlay();
            _isPlaying = false;
        }

        /// <summary>
        /// İlk geçişte takılma olmaması için geçiş kamerası, canvas, top havuzu ve shader'ları önceden ısıtır.
        /// </summary>
        public void BeginWarmUp()
        {
            if (_warmedUp || _warming || !Application.isPlaying) return;
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

            // 1) Düz renkli tema perdesi + Başlık + Loading bar
            var curtainGo = new GameObject("ThemeCurtain");
            curtainGo.transform.SetParent(canvasGo.transform, false);
            var curtainRT = curtainGo.AddComponent<RectTransform>();
            StretchFill(curtainRT);
            _curtainBg = curtainGo.AddComponent<Image>();
            _curtainBg.color = themeBackdropColor;
            _curtainBg.raycastTarget = true;
            _curtainGroup = curtainGo.AddComponent<CanvasGroup>();
            _curtainGroup.alpha = 0f;

            var centerGo = new GameObject("CenterContent");
            centerGo.transform.SetParent(curtainGo.transform, false);
            var centerRT = centerGo.AddComponent<RectTransform>();
            centerRT.anchorMin = new Vector2(0.5f, 0.5f);
            centerRT.anchorMax = new Vector2(0.5f, 0.5f);
            centerRT.sizeDelta = new Vector2(860f, 360f);
            centerRT.anchoredPosition = new Vector2(0f, 30f);

            // Oyun İsmi ("PUZZLE GLASS")
            var titleGo = new GameObject("GameTitle");
            titleGo.transform.SetParent(centerGo.transform, false);
            var titleRT = titleGo.AddComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0.5f, 1f);
            titleRT.anchorMax = new Vector2(0.5f, 1f);
            titleRT.sizeDelta = new Vector2(840f, 120f);
            titleRT.anchoredPosition = new Vector2(0f, -68f);
            _titleText = titleGo.AddComponent<Text>();
            _titleText.font = _uiFont;
            _titleText.text = gameTitle;
            _titleText.fontSize = 90;
            _titleText.fontStyle = FontStyle.Bold;
            _titleText.color = titleColor;
            _titleText.alignment = TextAnchor.MiddleCenter;
            _titleText.raycastTarget = false;
            var titleShadow = titleGo.AddComponent<Shadow>();
            titleShadow.effectColor = new Color(barFillColor.r, barFillColor.g, barFillColor.b, 0.25f);
            titleShadow.effectDistance = new Vector2(0f, -5f);

            // "LOADING..." yazısı
            var loadTxtGo = new GameObject("LoadingLabel");
            loadTxtGo.transform.SetParent(centerGo.transform, false);
            var loadTxtRT = loadTxtGo.AddComponent<RectTransform>();
            loadTxtRT.anchorMin = new Vector2(0.5f, 0.5f);
            loadTxtRT.anchorMax = new Vector2(0.5f, 0.5f);
            loadTxtRT.sizeDelta = new Vector2(600f, 52f);
            loadTxtRT.anchoredPosition = new Vector2(0f, -24f);
            _loadingText = loadTxtGo.AddComponent<Text>();
            _loadingText.font = _uiFont;
            _loadingText.text = loadingLabel;
            _loadingText.fontSize = 30;
            _loadingText.fontStyle = FontStyle.Bold;
            _loadingText.color = Color.Lerp(titleColor, barFillColor, 0.45f);
            _loadingText.alignment = TextAnchor.MiddleCenter;
            _loadingText.raycastTarget = false;

            // Yuvarlak Loading Bar arka gölge
            const float barW = 600f;
            const float barH = 42f;
            const float pad = 6f;
            _barFullWidth = barW - pad * 2f;

            var barShadowGo = new GameObject("BarShadow");
            barShadowGo.transform.SetParent(centerGo.transform, false);
            var barShadowRT = barShadowGo.AddComponent<RectTransform>();
            barShadowRT.anchorMin = new Vector2(0.5f, 0.5f);
            barShadowRT.anchorMax = new Vector2(0.5f, 0.5f);
            barShadowRT.sizeDelta = new Vector2(barW + 6f, barH + 6f);
            barShadowRT.anchoredPosition = new Vector2(0f, -86f);
            var barShadowImg = barShadowGo.AddComponent<Image>();
            barShadowImg.sprite = _pillSprite;
            barShadowImg.type = Image.Type.Sliced;
            barShadowImg.color = new Color(0.52f, 0.51f, 0.74f, 0.30f);
            barShadowImg.raycastTarget = false;

            // Track (krem beyaz yuvarlak çerçeve)
            var trackGo = new GameObject("LoadingBarTrack");
            trackGo.transform.SetParent(centerGo.transform, false);
            var trackRT = trackGo.AddComponent<RectTransform>();
            trackRT.anchorMin = new Vector2(0.5f, 0.5f);
            trackRT.anchorMax = new Vector2(0.5f, 0.5f);
            trackRT.sizeDelta = new Vector2(barW, barH);
            trackRT.anchoredPosition = new Vector2(0f, -82f);
            var trackImg = trackGo.AddComponent<Image>();
            trackImg.sprite = _pillSprite;
            trackImg.type = Image.Type.Sliced;
            trackImg.color = barTrackColor;
            trackImg.raycastTarget = false;

            // Fill bar (periwinkle tema mavisi + yuvarlak uçlu dolgu)
            var fillGo = new GameObject("LoadingBarFill");
            fillGo.transform.SetParent(trackGo.transform, false);
            _barFillRect = fillGo.AddComponent<RectTransform>();
            _barFillRect.anchorMin = new Vector2(0f, 0.5f);
            _barFillRect.anchorMax = new Vector2(0f, 0.5f);
            _barFillRect.pivot = new Vector2(0f, 0.5f);
            _barFillRect.anchoredPosition = new Vector2(pad, 0f);
            _barFillRect.sizeDelta = new Vector2(0f, barH - pad * 2f);
            var fillImg = fillGo.AddComponent<Image>();
            fillImg.sprite = _pillSprite;
            fillImg.type = Image.Type.Sliced;
            fillImg.color = barFillColor;
            fillImg.raycastTarget = false;

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
