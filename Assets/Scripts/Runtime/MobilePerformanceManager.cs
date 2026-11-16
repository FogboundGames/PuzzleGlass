using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PuzzleGlass
{
    /// <summary>
    /// Mobil cihazlarda varsayılan 30 FPS kısıtlamasını kaldırır ve kare süresini kararlı 60 FPS'te tutar.
    /// - vSyncCount = 0, Application.targetFrameRate = 60
    /// - Incremental GC kare başına zaman dilimi = 2 ms (saliselik stutter önleme)
    /// - URP SRP Batcher doğrulaması ve düşük dokunma gecikmesi (maxQueuedFrames = 2)
    /// - İsteğe bağlı dinamik renderScale ve FPS sayacı
    /// </summary>
    [AddComponentMenu("PuzzleGlass/Mobile Performance Manager")]
    public class MobilePerformanceManager : MonoBehaviour
    {
        private static MobilePerformanceManager s_Instance;
        public static MobilePerformanceManager Instance => s_Instance;

        [Header("Hedef Kare Hızı (FPS)")]
        [SerializeField, Range(30, 120)] private int m_MaxTargetFps = 60;
        [SerializeField, Range(30, 60)] private int m_MinTargetFps = 45;

        [Header("Dinamik Performans & Çözünürlük Ölçekleyici")]
        [Tooltip("FPS düşerse URP renderScale'i hafifçe kısarak 45+ FPS'i korur (render buffer yeniden tahsisini önlemek için varsayılan kapalıdır).")]
        [SerializeField] private bool m_EnableAdaptiveResolution = false;
        [SerializeField, Range(0.70f, 1.0f)] private float m_MinRenderScale = 0.82f;

        [Header("Hata Ayıklama")]
        [SerializeField] private bool m_ShowFpsCounter = false;

        public int MaxTargetFps
        {
            get => m_MaxTargetFps;
            set
            {
                m_MaxTargetFps = Mathf.Clamp(value, 30, 120);
                ApplyTargetFrameRate();
            }
        }

        public int MinTargetFps
        {
            get => m_MinTargetFps;
            set => m_MinTargetFps = Mathf.Clamp(value, 30, m_MaxTargetFps);
        }

        public bool ShowFpsCounter
        {
            get => m_ShowFpsCounter;
            set => m_ShowFpsCounter = value;
        }

        public float CurrentFps { get; private set; } = 60f;

        private UniversalRenderPipelineAsset m_ActiveUrpAsset;
        private float m_OriginalRenderScale = 1f;
        private float m_CurrentRenderScale = 1f;
        private float m_FpsAccumulator = 0f;
        private int m_FpsFrames = 0;
        private float m_FpsTimeLeft = 0.5f;
        private GUIStyle m_LabelStyle;
        private GUIStyle m_BoxStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoInitialize()
        {
            EnsureInstance();
        }

        public static MobilePerformanceManager EnsureInstance()
        {
            if (s_Instance != null) return s_Instance;

            s_Instance = FindFirstObjectByType<MobilePerformanceManager>();
            if (s_Instance != null)
            {
                DontDestroyOnLoad(s_Instance.gameObject);
                s_Instance.InitializePerformance();
                return s_Instance;
            }

            GameObject go = new GameObject("[MobilePerformanceManager]");
            s_Instance = go.AddComponent<MobilePerformanceManager>();
            DontDestroyOnLoad(go);
            s_Instance.InitializePerformance();
            return s_Instance;
        }

        private void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            s_Instance = this;
            DontDestroyOnLoad(gameObject);
            InitializePerformance();
        }

        private void Start()
        {
            ApplyTargetFrameRate();
        }

        public void InitializePerformance()
        {
            CacheUrpAsset();
            ApplyTargetFrameRate();

            if (m_ActiveUrpAsset != null && !m_ActiveUrpAsset.useSRPBatcher)
            {
                m_ActiveUrpAsset.useSRPBatcher = true;
            }

            // Incremental GC: kare başına GC süresini 2 ms ile sınırla (60 FPS = 16.6 ms bütçe)
            if (UnityEngine.Scripting.GarbageCollector.isIncremental)
            {
                UnityEngine.Scripting.GarbageCollector.incrementalTimeSliceNanoseconds = 2_000_000;
            }
        }

        private void CacheUrpAsset()
        {
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                m_ActiveUrpAsset = urp;
                m_OriginalRenderScale = urp.renderScale;
                m_CurrentRenderScale = urp.renderScale;
            }
            else if (QualitySettings.renderPipeline is UniversalRenderPipelineAsset qUrp)
            {
                m_ActiveUrpAsset = qUrp;
                m_OriginalRenderScale = qUrp.renderScale;
                m_CurrentRenderScale = qUrp.renderScale;
            }
        }

        public void ApplyTargetFrameRate()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = m_MaxTargetFps;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            QualitySettings.maxQueuedFrames = 2;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            m_FpsTimeLeft -= dt;
            m_FpsAccumulator += 1f / Mathf.Max(1e-4f, dt);
            m_FpsFrames++;

            if (m_FpsTimeLeft <= 0f)
            {
                CurrentFps = m_FpsAccumulator / m_FpsFrames;
                m_FpsTimeLeft = 0.5f;
                m_FpsAccumulator = 0f;
                m_FpsFrames = 0;

                if (m_EnableAdaptiveResolution && m_ActiveUrpAsset != null)
                {
                    AdjustAdaptiveResolution();
                }
            }
        }

        private void AdjustAdaptiveResolution()
        {
            if (m_ActiveUrpAsset == null) return;

            if (CurrentFps < m_MinTargetFps + 3f)
            {
                if (m_CurrentRenderScale > m_MinRenderScale)
                {
                    m_CurrentRenderScale = Mathf.Max(m_MinRenderScale, m_CurrentRenderScale - 0.03f);
                    m_ActiveUrpAsset.renderScale = m_CurrentRenderScale;
                }
            }
            else if (CurrentFps >= m_MaxTargetFps - 2f)
            {
                if (m_CurrentRenderScale < m_OriginalRenderScale)
                {
                    m_CurrentRenderScale = Mathf.Min(m_OriginalRenderScale, m_CurrentRenderScale + 0.02f);
                    m_ActiveUrpAsset.renderScale = m_CurrentRenderScale;
                }
            }
        }

        private void OnDisable()
        {
            if (m_ActiveUrpAsset != null)
            {
                m_ActiveUrpAsset.renderScale = m_OriginalRenderScale;
            }
        }

        private void OnDestroy()
        {
            if (m_ActiveUrpAsset != null)
            {
                m_ActiveUrpAsset.renderScale = m_OriginalRenderScale;
            }
            if (s_Instance == this) s_Instance = null;
        }

        private void OnGUI()
        {
            if (!m_ShowFpsCounter) return;

            int fps = Mathf.RoundToInt(CurrentFps);
            Color textColor = fps >= 50 ? Color.green : (fps >= 42 ? Color.yellow : Color.red);

            if (m_LabelStyle == null)
            {
                m_LabelStyle = new GUIStyle
                {
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.UpperLeft
                };
            }
            m_LabelStyle.fontSize = Mathf.Max(24, Screen.height / 45);
            m_LabelStyle.normal.textColor = textColor;

            if (m_BoxStyle == null)
            {
                m_BoxStyle = new GUIStyle(GUI.skin.box);
            }

            GUI.Box(new Rect(20, 20, 180, 50), GUIContent.none, m_BoxStyle);
            GUI.Label(new Rect(30, 25, 160, 40), $"{fps} FPS", m_LabelStyle);
        }
    }
}
