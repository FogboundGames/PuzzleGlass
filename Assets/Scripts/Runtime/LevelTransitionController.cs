using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PuzzleGlass
{
    /// <summary>
    /// Level geçişi: ekrana yukarıdan renkli toplar dökülür ve ekranı kapatır, ekran kapalıyken
    /// sonraki level yüklenir, sonra zemin açılır ve toplar aşağı düşerek yeni leveli ortaya çıkarır.
    ///
    /// Geçiş topları oyunun BallObjectPool'unu KULLANMAZ: kendi havuzları vardır, BallPhysicsController
    /// taşımazlar ve kamera önündeki ayrı bir Z düzleminde (z = ballPlaneZ) düşerler. Böylece level
    /// yüklenirken silinmezler, oyun mantığına (toplanabilir toplar, level bitti kontrolü) karışmazlar
    /// ve "Ignore Raycast" katmanında oldukları için dokunmaları engellemezler.
    /// </summary>
    public class LevelTransitionController : MonoBehaviour
    {
        public static LevelTransitionController Instance { get; private set; }

        /// <summary>Geçiş oynarken true. Bu sürede oyuncu girdisi kilitlenir.</summary>
        public static bool IsTransitioning => Instance != null && Instance._isPlaying;

        [Header("Toplar")]
        [Tooltip("Ekran genişliğine kaç top sığsın (top boyutunu belirler)")]
        [Range(3, 14)] public int ballsAcrossScreen = 6;
        [Tooltip("Yığının ekranın üstünü kesin aşması için fazladan top satırı")]
        [Range(0, 8)] public int extraRows = 3;
        [Tooltip("Rastgele dökülme sıkı dizilimden daha gevşek yığılır; bunu telafi eden çarpan")]
        [Range(1.0f, 1.6f)] public float looseStackMultiplier = 1.20f;
        [Range(60, 400)] public int maxBalls = 260;

        [Header("Arka Perde (toplar arası boşlukları kapatır)")]
        [Tooltip("Toplar ekranı kapladıktan sonra arkalarında düz bir perde açılır; level değişimi aralardan görünmez")]
        public bool fillGapsWithBackdrop = true;
        [Tooltip("Perde rengi (alfa = 0 ise kameranın arka plan rengi kullanılır)")]
        public Color backdropColor = new Color(0f, 0f, 0f, 0f);
        public float backdropFadeIn = 0.30f;
        public float backdropFadeOut = 0.25f;
        [Tooltip("Kamera önündeki ayrı fizik düzlemi (oyunun collider'larına değmesin)")]
        public float ballPlaneZ = -4f;

        [Header("Fizik")]
        [Tooltip("Ek yerçekimi: dökülmenin tok ve hızlı hissettirmesi için")]
        public float extraGravity = 22f;
        public float bounciness = 0.25f;
        public float friction = 0.20f;

        [Header("Zamanlama (saniye)")]
        [Tooltip("Level bittikten sonra dökülme başlamadan önceki kısa bekleme")]
        public float startDelay = 0.35f;
        [Tooltip("Dökülmenin ne kadar dağınık/uzun olacağı: aynı sütundaki toplar arası en fazla ek boşluk (çap katı)")]
        [Range(0.05f, 2.0f)] public float pourSpread = 0.9f;
        [Tooltip("Tüm toplar bu hızın altına inince yığın oturmuş sayılır")]
        public float settleSpeed = 0.35f;
        [Tooltip("Yığının oturmasını en fazla bu kadar bekle")]
        public float maxSettleWait = 3.0f;
        [Tooltip("Yığın oturup perde açıldıktan sonra level yüklenmeden önceki kısa bekleme")]
        public float settleDuration = 0.10f;
        [Tooltip("Yeni level yüklendikten sonra zemin açılmadan önceki bekleme")]
        public float holdAfterLoad = 0.15f;
        [Tooltip("Toplar ekrandan çıkmazsa geçişi en geç bu sürede bitir")]
        public float maxDrainDuration = 2.5f;

        private readonly List<Rigidbody> _pool = new List<Rigidbody>();
        private readonly List<Rigidbody> _active = new List<Rigidbody>();
        private Transform _ballRoot;
        private BoxCollider _floor;
        private BoxCollider _leftWall;
        private BoxCollider _rightWall;
        private PhysicsMaterial _physMaterial;
        private Mesh _sphereMesh;
        private bool _isPlaying;
        private Coroutine _routine;

        /// <summary>
        /// Geçiş objelerinin katmanı (isimsiz, boş katman). Ana kamera bu katmanı çizmez; geçiş kamerası
        /// sadece bunu çizer ve sonucu en üstteki overlay canvas'ta gösterir -> toplar tüm UI'ın üstünde.
        /// </summary>
        public const int TransitionLayer = 31;

        [Header("UI Üstü Çizim")]
        [Tooltip("Geçiş canvas'ının sıralaması: tüm UI canvas'larından büyük olmalı")]
        public int overlaySortingOrder = 30000;

        private Camera _overlayCam;
        private RenderTexture _overlayRT;
        private Canvas _overlayCanvas;
        private RawImage _overlayImage;

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
        /// Level bittiğinde çağrılır: top dökülmesi -> sonraki level yüklenir -> toplar düşüp gider.
        /// Geçiş zaten oynuyorsa ikinci çağrı yok sayılır.
        /// </summary>
        public void PlayNextLevelTransition(List<PuzzleColor> colors)
        {
            if (_isPlaying) return;
            _routine = StartCoroutine(TransitionRoutine(colors));
        }

        private IEnumerator TransitionRoutine(List<PuzzleColor> colors)
        {
            _isPlaying = true;

            var gm = PuzzleGlassGameManager.Instance;
            Camera cam = (gm != null && gm.mainCamera != null) ? gm.mainCamera : Camera.main;
            if (cam == null || gm == null)
            {
                // Kamera yoksa geçiş yapamayız: eski davranışa düş
                _isPlaying = false;
                if (gm != null) gm.LoadNextLevel();
                yield break;
            }

            if (colors == null || colors.Count == 0)
            {
                colors = new List<PuzzleColor>((PuzzleColor[])System.Enum.GetValues(typeof(PuzzleColor)));
            }

            EnsureResources();

            // --- Ekran sınırları (ortografik kamera) ---
            float halfH = cam.orthographicSize;
            float halfW = halfH * cam.aspect;
            Vector3 camPos = cam.transform.position;
            float left = camPos.x - halfW;
            float right = camPos.x + halfW;
            float bottom = camPos.y - halfH;
            float top = camPos.y + halfH;

            float diameter = (halfW * 2f) / ballsAcrossScreen;
            float radius = diameter * 0.5f;
            int count = ComputeBallCount(cam);

            BuildCage(left, right, bottom, halfH);
            SetupBackdrop(cam, camPos, halfW, halfH);
            BeginOverlay(cam);

            // Topları önceden oluştur: dökülme karesinde yüzlerce obje yaratmak takılma yapıyordu ve
            // büyük deltaTime yüzünden toplar iç içe doğup birbirini fırlatıyordu (ilk geçişte yarım dolma).
            Prewarm(count);

            if (startDelay > 0f) yield return new WaitForSeconds(startDelay);

            // --- 1. Dökülme ---
            // Toplar ekran üstünde sütunlar hâlinde, aralarında rastgele boşluklarla üst üste dizilir.
            // Sütun aralığı = çap, aynı sütunda aralık >= çap: hiçbir top doğarken diğeriyle çakışmaz.
            // Hepsi aynı anda serbest bırakılır; rastgele boşluklar sayesinde dökülme gibi görünür.
            int cols = ballsAcrossScreen;
            float[] colY = new float[cols];
            for (int ci = 0; ci < cols; ci++) colY[ci] = top + radius + Random.Range(0f, diameter * 1.5f);

            for (int i = 0; i < count; i++)
            {
                int ci = i % cols;
                float x = left + radius + ci * diameter;
                float y = colY[ci];
                colY[ci] += diameter * Random.Range(1.02f, 1.0f + pourSpread);
                PuzzleColor c = colors[Random.Range(0, colors.Count)];
                SpawnBall(new Vector3(x, y, ballPlaneZ), diameter, c);
            }

            // --- 2. Yığın gerçekten oturana kadar bekle (en geç maxSettleWait) ---
            float waited = 0f;
            float settleSpeedSq = settleSpeed * settleSpeed;
            while (waited < maxSettleWait)
            {
                waited += Time.deltaTime;
                bool anyFast = false;
                for (int i = 0; i < _active.Count; i++)
                {
                    var rb = _active[i];
                    if (rb == null) continue;
                    if (rb.linearVelocity.sqrMagnitude > settleSpeedSq) { anyFast = true; break; }
                }
                // Hareket bittiyse (ya da çok az kaldıysa) çık
                if (!anyFast && waited > 0.3f) break;
                yield return null;
            }

            // Toplar arası boşlukları kapatan perde
            yield return FadeBackdrop(1f, backdropFadeIn);
            if (settleDuration > 0f) yield return new WaitForSeconds(settleDuration);

            // --- 3. Ekran kapalıyken sonraki leveli yükle ---
            gm.CancelInvoke(nameof(PuzzleGlassGameManager.LoadNextLevel));
            gm.LoadNextLevel();

            if (holdAfterLoad > 0f) yield return new WaitForSeconds(holdAfterLoad);

            // --- 4. Zemini aç, toplar aşağı düşsün ---
            if (_floor != null) _floor.enabled = false;
            for (int i = 0; i < _active.Count; i++)
            {
                var rb = _active[i];
                if (rb == null) continue;
                rb.WakeUp();
                rb.linearVelocity += new Vector3(Random.Range(-0.4f, 0.4f), Random.Range(-1.5f, -0.3f), 0f);
            }
            StartCoroutine(FadeBackdrop(0f, backdropFadeOut));

            float drainT = 0f;
            float killY = bottom - diameter * 2f;
            while (_active.Count > 0 && drainT < maxDrainDuration)
            {
                drainT += Time.deltaTime;
                for (int i = _active.Count - 1; i >= 0; i--)
                {
                    var rb = _active[i];
                    if (rb == null || rb.position.y < killY)
                    {
                        RecycleAt(i);
                    }
                }
                yield return null;
            }

            // Süre dolduysa kalanları da topla
            for (int i = _active.Count - 1; i >= 0; i--) RecycleAt(i);

            if (_floor != null) _floor.enabled = true;
            EndOverlay();
            _isPlaying = false;
            _routine = null;
        }

        private void FixedUpdate()
        {
            if (!_isPlaying || extraGravity <= 0.01f) return;
            for (int i = 0; i < _active.Count; i++)
            {
                var rb = _active[i];
                if (rb != null && !rb.isKinematic)
                {
                    rb.AddForce(Vector3.down * extraGravity, ForceMode.Acceleration);
                }
            }
        }

        /// <summary>Geçişi anında iptal eder ve tüm geçiş toplarını gizler (ör. sahne değişimi).</summary>
        public void CancelTransition()
        {
            if (_routine != null) StopCoroutine(_routine);
            _routine = null;
            for (int i = _active.Count - 1; i >= 0; i--) RecycleAt(i);
            if (_floor != null) _floor.enabled = true;
            if (_backdrop != null) _backdrop.gameObject.SetActive(false);
            EndOverlay();
            _isPlaying = false;
        }

        // ==================== UI Üstü Çizim (ayrı kamera -> RenderTexture -> en üst canvas) ====================

        /// <summary>Ekranı alttan üste dolduracak top sayısı (altıgen dizilim + fazladan satırlar + gevşek yığılma payı).</summary>
        private int ComputeBallCount(Camera cam)
        {
            float halfH = cam.orthographicSize;
            float halfW = halfH * cam.aspect;
            float diameter = (halfW * 2f) / ballsAcrossScreen;
            int rows = Mathf.CeilToInt((halfH * 2f) / (diameter * 0.866f)) + extraRows;
            return Mathf.Min(maxBalls, Mathf.CeilToInt(rows * ballsAcrossScreen * looseStackMultiplier));
        }

        // ==================== Önceden hazırlık (takılmasın diye) ====================

        private bool _warmedUp;
        private bool _warming;

        /// <summary>
        /// Geçişin ağır kısımlarını (top objeleri, render texture, kamera, canvas, shader derlemesi) level başında
        /// birkaç kareye yayarak hazırlar. Yoksa son top girdiği anda hepsi aynı karede yapılıp oyun donuyordu.
        /// </summary>
        public void BeginWarmUp()
        {
            if (_warmedUp || _warming || !Application.isPlaying) return;
            StartCoroutine(WarmUpRoutine());
        }

        private IEnumerator WarmUpRoutine()
        {
            _warming = true;
            // Level yüklemesi bitsin
            yield return null;
            yield return null;

            var gm = PuzzleGlassGameManager.Instance;
            Camera cam = (gm != null && gm.mainCamera != null) ? gm.mainCamera : Camera.main;
            if (cam == null) { _warming = false; yield break; }

            EnsureResources();
            EnsureOverlay(cam);
            yield return null;

            // Topları karelere yayarak oluştur
            int count = ComputeBallCount(cam);
            const int perFrame = 20;
            while (_pool.Count + _active.Count < count)
            {
                if (_isPlaying) { _warming = false; yield break; } // geçiş başladıysa kalanını o halleder
                int n = Mathf.Min(perFrame, count - (_pool.Count + _active.Count));
                for (int i = 0; i < n; i++)
                {
                    var rb = CreateBall();
                    rb.isKinematic = true;
                    rb.gameObject.SetActive(false);
                    _pool.Add(rb);
                }
                yield return null;
            }

            // Perde sprite'ı
            float halfH = cam.orthographicSize;
            SetupBackdrop(cam, cam.transform.position, halfH * cam.aspect, halfH);
            if (_backdrop != null) _backdrop.gameObject.SetActive(false);

            // Shader ısınması: her renkten bir topu geçiş kamerasıyla bir kez görünmeden çiz
            // (canvas kapalı olduğu için ekranda bir şey görünmez; ilk geçişteki shader derleme takılmasını önler)
            if (!_isPlaying && BallObjectPool.Instance != null)
            {
                var colors = (PuzzleColor[])System.Enum.GetValues(typeof(PuzzleColor));
                var shown = new List<Rigidbody>();
                float d = (halfH * cam.aspect * 2f) / ballsAcrossScreen;
                Vector3 c = cam.transform.position;
                for (int i = 0; i < colors.Length && i < _pool.Count; i++)
                {
                    var rb = _pool[i];
                    var mr = rb.GetComponent<MeshRenderer>();
                    var mat = BallObjectPool.Instance.GetBallMaterial(colors[i]);
                    if (mr != null && mat != null) mr.sharedMaterial = mat;
                    rb.transform.position = new Vector3(c.x + (i - colors.Length * 0.5f) * d * 0.5f, c.y, ballPlaneZ);
                    rb.transform.localScale = Vector3.one * d;
                    rb.gameObject.SetActive(true);
                    shown.Add(rb);
                }
                _overlayCam.enabled = true;
                yield return new WaitForEndOfFrame();
                yield return null;
                if (!_isPlaying)
                {
                    _overlayCam.enabled = false;
                    foreach (var rb in shown) rb.gameObject.SetActive(false);
                }
            }

            _warmedUp = true;
            _warming = false;
        }

        private void BeginOverlay(Camera mainCam)
        {
            EnsureOverlay(mainCam);
            _overlayCam.enabled = true;
            _overlayCanvas.sortingOrder = overlaySortingOrder;
            _overlayImage.texture = _overlayRT;
            _overlayCanvas.gameObject.SetActive(true);
        }

        /// <summary>Render texture, geçiş kamerası ve en üst canvas'ı oluşturur (kapalı hâlde).</summary>
        private void EnsureOverlay(Camera mainCam)
        {
            // Ana kamera geçiş katmanını çizmesin (yoksa toplar iki kez görünür)
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
                _overlayCam.backgroundColor = new Color(0f, 0f, 0f, 0f); // şeffaf: sadece toplar ve perde görünsün
                _overlayCam.cullingMask = 1 << TransitionLayer;
                _overlayCam.allowHDR = false;
                _overlayCam.enabled = false; // sadece geçişte (ve ısınmada) çizer
            }

            // Ana kamerayla birebir aynı kadraj
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
                var canvasGo = new GameObject("TransitionOverlayCanvas");
                canvasGo.transform.SetParent(transform, false);
                _overlayCanvas = canvasGo.AddComponent<Canvas>();
                _overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                // Geçiş sırasında alttaki UI butonlarına basılmasın
                canvasGo.AddComponent<GraphicRaycaster>();

                var imgGo = new GameObject("TransitionImage");
                imgGo.transform.SetParent(canvasGo.transform, false);
                _overlayImage = imgGo.AddComponent<RawImage>();
                var rt = _overlayImage.rectTransform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                _overlayImage.raycastTarget = true;
                canvasGo.SetActive(false); // geçiş başlayana kadar kapalı (UI'ı engellemesin)
            }

            _overlayImage.texture = _overlayRT;
        }

        private void EndOverlay()
        {
            if (_overlayCam != null) _overlayCam.enabled = false;
            if (_overlayCanvas != null) _overlayCanvas.gameObject.SetActive(false);
        }

        // ==================== Arka Perde ====================

        private SpriteRenderer _backdrop;
        private Color _backdropBaseColor;

        /// <summary>Topların hemen arkasına, ekranı tamamen kaplayan düz renkli bir perde koyar (başta görünmez).</summary>
        private void SetupBackdrop(Camera cam, Vector3 camPos, float halfW, float halfH)
        {
            if (!fillGapsWithBackdrop) return;

            if (_backdrop == null)
            {
                var go = new GameObject("TransitionBackdrop");
                go.layer = TransitionLayer;
                go.transform.SetParent(transform, false);
                _backdrop = go.AddComponent<SpriteRenderer>();
                var tex = Texture2D.whiteTexture;
                _backdrop.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width);
            }

            _backdropBaseColor = backdropColor.a > 0.01f ? backdropColor : cam.backgroundColor;
            _backdropBaseColor.a = 1f;

            // Topların hemen arkasında (kameradan daha uzak), oyunun önünde
            _backdrop.transform.position = new Vector3(camPos.x, camPos.y, ballPlaneZ + 1f);
            _backdrop.transform.localScale = new Vector3(halfW * 2f + 0.5f, halfH * 2f + 0.5f, 1f);
            SetBackdropAlpha(0f);
            _backdrop.gameObject.SetActive(true);
        }

        private IEnumerator FadeBackdrop(float targetAlpha, float duration)
        {
            if (_backdrop == null || !fillGapsWithBackdrop) yield break;

            float start = _backdrop.color.a;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                SetBackdropAlpha(Mathf.Lerp(start, targetAlpha, t / duration));
                yield return null;
            }
            SetBackdropAlpha(targetAlpha);
            if (targetAlpha <= 0.001f) _backdrop.gameObject.SetActive(false);
        }

        private void SetBackdropAlpha(float a)
        {
            if (_backdrop == null) return;
            Color c = _backdropBaseColor;
            c.a = a;
            _backdrop.color = c;
        }

        // ==================== Yardımcılar ====================

        private void EnsureResources()
        {
            if (_ballRoot == null)
            {
                var root = new GameObject("TransitionBalls");
                root.transform.SetParent(transform, false);
                _ballRoot = root.transform;
            }

            if (_physMaterial == null)
            {
                _physMaterial = new PhysicsMaterial("Mat_TransitionBall")
                {
                    bounciness = bounciness,
                    dynamicFriction = friction,
                    staticFriction = friction,
                    bounceCombine = PhysicsMaterialCombine.Average,
                    frictionCombine = PhysicsMaterialCombine.Average
                };
            }

            if (_sphereMesh == null)
            {
                GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                var mf = temp.GetComponent<MeshFilter>();
                if (mf != null) _sphereMesh = mf.sharedMesh;
                Destroy(temp);
            }
        }

        /// <summary>Ekranın altına zemin, iki yanına duvar kurar (geçiş düzleminde).</summary>
        private void BuildCage(float left, float right, float bottom, float halfH)
        {
            const float thickness = 1f;
            const float depth = 2f;
            float width = right - left;
            float wallH = halfH * 40f; // ekran üstünde yüksek sütunlarda doğan toplar da duvarların arasında kalsın

            if (_floor == null) _floor = CreateCageCollider("TransitionFloor");
            _floor.enabled = true;
            _floor.transform.position = new Vector3((left + right) * 0.5f, bottom - thickness * 0.5f, ballPlaneZ);
            _floor.size = new Vector3(width + thickness * 2f, thickness, depth);

            if (_leftWall == null) _leftWall = CreateCageCollider("TransitionWallL");
            _leftWall.transform.position = new Vector3(left - thickness * 0.5f, bottom + wallH * 0.5f - halfH, ballPlaneZ);
            _leftWall.size = new Vector3(thickness, wallH, depth);

            if (_rightWall == null) _rightWall = CreateCageCollider("TransitionWallR");
            _rightWall.transform.position = new Vector3(right + thickness * 0.5f, bottom + wallH * 0.5f - halfH, ballPlaneZ);
            _rightWall.size = new Vector3(thickness, wallH, depth);
        }

        private BoxCollider CreateCageCollider(string name)
        {
            var go = new GameObject(name);
            go.layer = TransitionLayer;
            go.transform.SetParent(transform, false);
            var col = go.AddComponent<BoxCollider>();
            col.sharedMaterial = _physMaterial;
            return col;
        }

        /// <summary>Havuzda en az 'count' top hazır olsun (pasif hâlde).</summary>
        private void Prewarm(int count)
        {
            int needed = count - (_pool.Count + _active.Count);
            for (int i = 0; i < needed; i++)
            {
                var rb = CreateBall();
                rb.isKinematic = true;
                rb.gameObject.SetActive(false);
                _pool.Add(rb);
            }
        }

        private void SpawnBall(Vector3 pos, float diameter, PuzzleColor color)
        {
            Rigidbody rb;
            if (_pool.Count > 0)
            {
                rb = _pool[_pool.Count - 1];
                _pool.RemoveAt(_pool.Count - 1);
            }
            else
            {
                rb = CreateBall();
            }

            var go = rb.gameObject;
            go.transform.position = pos;
            go.transform.rotation = Random.rotation;
            go.transform.localScale = Vector3.one * diameter;

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null && BallObjectPool.Instance != null)
            {
                Material mat = BallObjectPool.Instance.GetBallMaterial(color);
                if (mat != null) mr.sharedMaterial = mat;
            }

            go.SetActive(true);
            rb.isKinematic = false;
            rb.linearVelocity = new Vector3(Random.Range(-0.15f, 0.15f), Random.Range(-1.5f, -0.5f), 0f);
            rb.angularVelocity = Random.insideUnitSphere * 6f;
            _active.Add(rb);
        }

        private Rigidbody CreateBall()
        {
            var go = new GameObject("TransitionBall");
            go.layer = TransitionLayer;
            go.transform.SetParent(_ballRoot, false);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = _sphereMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            var sc = go.AddComponent<SphereCollider>();
            sc.radius = 0.5f;
            sc.sharedMaterial = _physMaterial;

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.3f;
            rb.linearDamping = 0.1f;
            rb.angularDamping = 0.5f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.constraints = RigidbodyConstraints.FreezePositionZ;
            return rb;
        }

        private void RecycleAt(int index)
        {
            var rb = _active[index];
            _active.RemoveAt(index);
            if (rb == null) return;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.gameObject.SetActive(false);
            _pool.Add(rb);
        }
    }
}
