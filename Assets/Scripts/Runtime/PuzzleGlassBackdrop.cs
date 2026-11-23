using UnityEngine;

namespace PuzzleGlass
{
    /// <summary>
    /// Renders a soft casual-puzzle gradient + subtle dot-motif backdrop quad behind the scene camera.
    /// Automatically stays fitted to the orthographic camera aspect ratio in both Edit and Play modes.
    /// </summary>
    [ExecuteAlways]
    public class PuzzleGlassBackdrop : MonoBehaviour
    {
        public static PuzzleGlassBackdrop Instance { get; private set; }

        private const string DefaultBackdropResourcePath = "Textures/StudioBackdrop";

        [Header("Image Backdrop")]
        public bool useTextureBackdrop = false;
        public Texture2D backdropTexture;
        public Color backdropTint = Color.white;

        [Header("Pastel Lavender Studio Palette")]
        public Color topColor  = new Color(0.505f, 0.508f, 0.625f, 1f);
        public Color midColor  = new Color(0.550f, 0.550f, 0.660f, 1f);
        public Color botColor  = new Color(0.875f, 0.878f, 0.945f, 1f);
        public Color glowColor = new Color(0.610f, 0.612f, 0.735f, 1f);
        public Color dotColor  = new Color(0.520f, 0.525f, 0.640f, 1f);
        [Range(0f, 0.20f)]
        public float dotStrength = 0.0f;
        [Range(0f, 1.0f)]
        public float floatingStrength = 0.0f;

        [Header("Stepped Lower Ground")]
        public bool enableSteppedSplitGround = true;
        public Color lowerTopColor = new Color(0.928f, 0.930f, 0.970f, 1f);
        public Color lowerBotColor = new Color(0.875f, 0.878f, 0.945f, 1f);
        public Color lowerRimColor = new Color(0.990f, 0.992f, 1.000f, 1f);

        [SerializeField, HideInInspector] private int visualThemeVersion = 0;

        private Transform _quadTransform;
        private Material _backdropMaterial;
        private float _lastOrthoSize = -1f;
        private float _lastAspect = -1f;
        private Vector3 _lastCamPos = new Vector3(float.NaN, float.NaN, float.NaN);
        private float _quadWorldWidth = 5f;
        private float _quadWorldHeight = 9f;
        private Vector4 _lastSplitDockParams = new Vector4(float.NaN, float.NaN, float.NaN, float.NaN);
        private float _lastSplitEnabled = -1f;

        private static readonly int MainTexId           = Shader.PropertyToID("_MainTex");
        private static readonly int TintColorId         = Shader.PropertyToID("_TintColor");
        private static readonly int UseTextureId        = Shader.PropertyToID("_UseTexture");
        private static readonly int TexAspectId         = Shader.PropertyToID("_TexAspect");
        private static readonly int TopColorId          = Shader.PropertyToID("_TopColor");
        private static readonly int MidColorId          = Shader.PropertyToID("_MidColor");
        private static readonly int BotColorId          = Shader.PropertyToID("_BotColor");
        private static readonly int GlowColorId         = Shader.PropertyToID("_GlowColor");
        private static readonly int DotColorId          = Shader.PropertyToID("_DotColor");
        private static readonly int DotStrengthId       = Shader.PropertyToID("_DotStrength");
        private static readonly int FloatingStrengthId  = Shader.PropertyToID("_FloatingStrength");
        private static readonly int AspectId            = Shader.PropertyToID("_Aspect");
        private static readonly int LowerTopColorId     = Shader.PropertyToID("_LowerTopColor");
        private static readonly int LowerBotColorId     = Shader.PropertyToID("_LowerBotColor");
        private static readonly int LowerRimColorId     = Shader.PropertyToID("_LowerRimColor");
        private static readonly int BackdropWorldRectId = Shader.PropertyToID("_BackdropWorldRect");
        private static readonly int SplitDockParamsId   = Shader.PropertyToID("_SplitDockParams");
        private static readonly int SplitEnabledId      = Shader.PropertyToID("_SplitEnabled");

        private void OnEnable()
        {
            Instance = this;
            MigrateThemeIfNeeded();
            EnsureBackdropTexture();
            EnsureBackdropQuad();
            SyncWithCamera(force: true);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (isActiveAndEnabled)
            {
                EnsureBackdropTexture();
                ApplyMaterialProperties(_lastAspect > 0f ? _lastAspect : 0.5625f);
            }
        }
#endif

        private void EnsureBackdropTexture()
        {
            if (useTextureBackdrop && backdropTexture == null)
            {
                backdropTexture = Resources.Load<Texture2D>(DefaultBackdropResourcePath);
            }
        }

        private void MigrateThemeIfNeeded()
        {
            // v8: Gri-Lavanta Üst Duvar + Slotları Saran Kademeli (___/‾‾‾\___) Pastel Beyaz-Lila Alt Zemin
            if (visualThemeVersion >= 8) return;
            visualThemeVersion = 8;
            ApplyPastelLavenderStudioPreset();
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorUtility.SetDirty(this);
            }
#endif
        }

        public void ApplyPastelLavenderStudioPreset()
        {
            useTextureBackdrop = false;
            backdropTint = Color.white;
            topColor  = new Color(0.505f, 0.508f, 0.625f, 1f);
            midColor  = new Color(0.550f, 0.550f, 0.660f, 1f);
            botColor  = new Color(0.875f, 0.878f, 0.945f, 1f);
            glowColor = new Color(0.610f, 0.612f, 0.735f, 1f);
            dotColor  = new Color(0.520f, 0.525f, 0.640f, 1f);
            dotStrength = 0.0f;
            floatingStrength = 0.0f;
            enableSteppedSplitGround = true;
            lowerTopColor = new Color(0.928f, 0.930f, 0.970f, 1f);
            lowerBotColor = new Color(0.875f, 0.878f, 0.945f, 1f);
            lowerRimColor = new Color(0.990f, 0.992f, 1.000f, 1f);
        }

        private void LateUpdate()
        {
            SyncWithCamera(force: false);
            UpdateSplitDockUniforms(force: false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void EnsureBackdropQuad()
        {
            if (_quadTransform == null)
            {
                Transform existing = transform.Find("BackdropQuad");
                if (existing != null)
                {
                    _quadTransform = existing;
                }
                else
                {
                    GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    quad.name = "BackdropQuad";
                    quad.transform.SetParent(transform, false);
                    Collider col = quad.GetComponent<Collider>();
                    if (col != null)
                    {
                        if (Application.isPlaying) Destroy(col);
                        else DestroyImmediate(col);
                    }
                    _quadTransform = quad.transform;
                }
            }

            // Materyal yalnızca bir kez oluşturulur; her karede Shader.Find + GetComponent +
            // HasProperty/Set* çağrıları yapmak (LateUpdate her karede çalışıyor) gereksiz CPU maliyetiydi.
            if (_backdropMaterial == null)
            {
                EnsureBackdropTexture();

                Shader shader = Shader.Find("PuzzleGlass/SoftBackdrop") ?? Shader.Find("Universal Render Pipeline/Unlit");
                if (shader != null)
                {
                    _backdropMaterial = new Material(shader) { name = "Mat_PuzzleGlassBackdrop" };
                    ApplyMaterialProperties(_lastAspect > 0f ? _lastAspect : 0.5625f);

                    var mr = _quadTransform.GetComponent<MeshRenderer>();
                    if (mr != null)
                    {
                        mr.sharedMaterial = _backdropMaterial;
                        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        mr.receiveShadows = false;
                    }
                }
            }
        }

        public void SyncWithCamera(bool force)
        {
            Camera cam = Camera.main;
            if (cam == null) cam = FindFirstObjectByType<Camera>();
            if (cam == null) return;

            EnsureBackdropQuad();
            if (_quadTransform == null) return;

            float ortho = cam.orthographic ? cam.orthographicSize : 4.2f;
            float aspect = Mathf.Max(0.25f, cam.aspect);

            Vector3 camPos = cam.transform.position;
            if (!force && Mathf.Approximately(ortho, _lastOrthoSize) && Mathf.Approximately(aspect, _lastAspect))
            {
                if (!Mathf.Approximately(camPos.x, _lastCamPos.x) || !Mathf.Approximately(camPos.y, _lastCamPos.y))
                {
                    _lastCamPos = camPos;
                    _quadTransform.position = new Vector3(camPos.x, camPos.y, 8.0f);
                    if (_backdropMaterial != null && _backdropMaterial.HasProperty(BackdropWorldRectId))
                    {
                        _backdropMaterial.SetVector(BackdropWorldRectId, new Vector4(camPos.x, camPos.y, _quadWorldWidth, _quadWorldHeight));
                    }
                }
                return;
            }

            _lastOrthoSize = ortho;
            _lastAspect = aspect;
            _lastCamPos = camPos;

            float h = ortho * 2.15f;
            float w = h * aspect;
            _quadWorldWidth = w;
            _quadWorldHeight = h;

            _quadTransform.position = new Vector3(camPos.x, camPos.y, 8.0f);
            _quadTransform.rotation = Quaternion.identity;
            _quadTransform.localScale = new Vector3(w, h, 1f);

            ApplyMaterialProperties(aspect);
        }

        private void ApplyMaterialProperties(float aspect)
        {
            if (_backdropMaterial == null) return;
            bool hasTex = useTextureBackdrop && backdropTexture != null;
            if (_backdropMaterial.HasProperty(MainTexId)) _backdropMaterial.SetTexture(MainTexId, backdropTexture);
            if (_backdropMaterial.HasProperty(TintColorId)) _backdropMaterial.SetColor(TintColorId, backdropTint);
            if (_backdropMaterial.HasProperty(UseTextureId)) _backdropMaterial.SetFloat(UseTextureId, hasTex ? 1f : 0f);
            if (_backdropMaterial.HasProperty(TexAspectId) && backdropTexture != null && backdropTexture.height > 0)
            {
                _backdropMaterial.SetFloat(TexAspectId, (float)backdropTexture.width / backdropTexture.height);
            }
            if (_backdropMaterial.HasProperty(TopColorId)) _backdropMaterial.SetColor(TopColorId, topColor);
            if (_backdropMaterial.HasProperty(MidColorId)) _backdropMaterial.SetColor(MidColorId, midColor);
            if (_backdropMaterial.HasProperty(BotColorId)) _backdropMaterial.SetColor(BotColorId, botColor);
            if (_backdropMaterial.HasProperty(GlowColorId)) _backdropMaterial.SetColor(GlowColorId, glowColor);
            if (_backdropMaterial.HasProperty(DotColorId)) _backdropMaterial.SetColor(DotColorId, dotColor);
            if (_backdropMaterial.HasProperty(DotStrengthId)) _backdropMaterial.SetFloat(DotStrengthId, dotStrength);
            if (_backdropMaterial.HasProperty(FloatingStrengthId)) _backdropMaterial.SetFloat(FloatingStrengthId, floatingStrength);
            if (_backdropMaterial.HasProperty(AspectId)) _backdropMaterial.SetFloat(AspectId, aspect);
            if (_backdropMaterial.HasProperty(LowerTopColorId)) _backdropMaterial.SetColor(LowerTopColorId, lowerTopColor);
            if (_backdropMaterial.HasProperty(LowerBotColorId)) _backdropMaterial.SetColor(LowerBotColorId, lowerBotColor);
            if (_backdropMaterial.HasProperty(LowerRimColorId)) _backdropMaterial.SetColor(LowerRimColorId, lowerRimColor);
            if (_backdropMaterial.HasProperty(BackdropWorldRectId))
            {
                float cx = float.IsNaN(_lastCamPos.x) ? 0f : _lastCamPos.x;
                float cy = float.IsNaN(_lastCamPos.y) ? 0.3f : _lastCamPos.y;
                _backdropMaterial.SetVector(BackdropWorldRectId, new Vector4(cx, cy, _quadWorldWidth, _quadWorldHeight));
            }
            UpdateSplitDockUniforms(force: true);
        }

        private void UpdateSplitDockUniforms(bool force)
        {
            if (_backdropMaterial == null) return;

            float enabled = 0f;
            Vector4 splitParams = Vector4.zero;
            if (enableSteppedSplitGround)
            {
                SlotSystem slotSys = SlotSystem.Instance != null ? SlotSystem.Instance : FindFirstObjectByType<SlotSystem>();
                if (slotSys != null && slotSys.TryGetRaisedBayWorldBounds(out float cx, out float bayHalfW, out float bayTopY, out float wingY))
                {
                    enabled = 1f;
                    splitParams = new Vector4(cx, bayTopY, wingY, bayHalfW);
                }
            }

            if (!force && Mathf.Approximately(enabled, _lastSplitEnabled) && splitParams == _lastSplitDockParams)
            {
                return;
            }

            _lastSplitEnabled = enabled;
            _lastSplitDockParams = splitParams;
            if (_backdropMaterial.HasProperty(SplitEnabledId)) _backdropMaterial.SetFloat(SplitEnabledId, enabled);
            if (_backdropMaterial.HasProperty(SplitDockParamsId)) _backdropMaterial.SetVector(SplitDockParamsId, splitParams);
        }
    }
}
