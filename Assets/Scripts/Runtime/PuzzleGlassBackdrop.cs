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

        [Header("Palette")]
        public Color topColor  = new Color(0.905f, 0.910f, 0.965f, 1f); // Soft pastel lavender mist
        public Color midColor  = new Color(0.950f, 0.922f, 0.885f, 1f); // Warm ivory cream
        public Color botColor  = new Color(0.920f, 0.862f, 0.798f, 1f); // Warm pastel peach-cream
        public Color glowColor = new Color(0.975f, 0.960f, 0.938f, 1f);
        public Color dotColor  = new Color(0.810f, 0.790f, 0.840f, 1f);
        [Range(0f, 0.20f)]
        public float dotStrength = 0.055f;

        private Transform _quadTransform;
        private Material _backdropMaterial;
        private float _lastOrthoSize = -1f;
        private float _lastAspect = -1f;

        private static readonly int TopColorId    = Shader.PropertyToID("_TopColor");
        private static readonly int MidColorId    = Shader.PropertyToID("_MidColor");
        private static readonly int BotColorId    = Shader.PropertyToID("_BotColor");
        private static readonly int GlowColorId   = Shader.PropertyToID("_GlowColor");
        private static readonly int DotColorId    = Shader.PropertyToID("_DotColor");
        private static readonly int DotStrengthId = Shader.PropertyToID("_DotStrength");
        private static readonly int AspectId      = Shader.PropertyToID("_Aspect");

        private void OnEnable()
        {
            Instance = this;
            EnsureBackdropQuad();
            SyncWithCamera(force: true);
        }

        private void LateUpdate()
        {
            SyncWithCamera(force: false);
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

            Shader shader = Shader.Find("PuzzleGlass/SoftBackdrop") ?? Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null && (_backdropMaterial == null || _backdropMaterial.shader != shader))
            {
                _backdropMaterial = new Material(shader) { name = "Mat_PuzzleGlassBackdrop" };
            }

            if (_backdropMaterial != null)
            {
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

        public void SyncWithCamera(bool force)
        {
            Camera cam = Camera.main;
            if (cam == null) cam = FindFirstObjectByType<Camera>();
            if (cam == null) return;

            EnsureBackdropQuad();
            if (_quadTransform == null) return;

            float ortho = cam.orthographic ? cam.orthographicSize : 4.2f;
            float aspect = Mathf.Max(0.25f, cam.aspect);

            if (!force && Mathf.Approximately(ortho, _lastOrthoSize) && Mathf.Approximately(aspect, _lastAspect))
            {
                // Still keep XY aligned to camera center in case camera moved
                Vector3 cPos = cam.transform.position;
                _quadTransform.position = new Vector3(cPos.x, cPos.y, 8.0f);
                return;
            }

            _lastOrthoSize = ortho;
            _lastAspect = aspect;

            float h = ortho * 2.15f;
            float w = h * aspect;

            Vector3 camPos = cam.transform.position;
            _quadTransform.position = new Vector3(camPos.x, camPos.y, 8.0f);
            _quadTransform.rotation = Quaternion.identity;
            _quadTransform.localScale = new Vector3(w, h, 1f);

            ApplyMaterialProperties(aspect);
        }

        private void ApplyMaterialProperties(float aspect)
        {
            if (_backdropMaterial == null) return;
            if (_backdropMaterial.HasProperty(TopColorId)) _backdropMaterial.SetColor(TopColorId, topColor);
            if (_backdropMaterial.HasProperty(MidColorId)) _backdropMaterial.SetColor(MidColorId, midColor);
            if (_backdropMaterial.HasProperty(BotColorId)) _backdropMaterial.SetColor(BotColorId, botColor);
            if (_backdropMaterial.HasProperty(GlowColorId)) _backdropMaterial.SetColor(GlowColorId, glowColor);
            if (_backdropMaterial.HasProperty(DotColorId)) _backdropMaterial.SetColor(DotColorId, dotColor);
            if (_backdropMaterial.HasProperty(DotStrengthId)) _backdropMaterial.SetFloat(DotStrengthId, dotStrength);
            if (_backdropMaterial.HasProperty(AspectId)) _backdropMaterial.SetFloat(AspectId, aspect);
        }
    }
}
