using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PuzzleGlass
{
    /// <summary>
    /// Cam pist / çıkış kanalları / alt kavanoz quad'ları "PuzzleGlass/GlassChamberLit" ile her karede piksel başına
    /// ağır bir hesap yapıyordu (telefonda ölçüm: bu shader kapatılınca 42 → 59 FPS). Shader'ın çıktısı zamana bağlı
    /// değil, yalnızca UV ve level kurulurken ayarlanan malzeme değerlerine bağlı. Bu yüzden her quad bir kez,
    /// ekranda kapladığı piksel boyutunda bir dokuya çizilir ve renderer basit bir doku shader'ına geçirilir.
    /// Karışım modu ve render sırası orijinal malzemeyle aynı kalır, görüntü değişmez.
    ///
    /// Level yeniden kurulunca yeni renderer'lar oluştuğu için periyodik taramada yakalanıp yeniden pişirilir.
    /// <see cref="Enabled"/> false yapılırsa her şey orijinal canlı shader'a döner.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public class GlassChamberBakeCache : MonoBehaviour
    {
        public static bool Enabled = true;

        private const string ChamberShaderName = "PuzzleGlass/GlassChamberLit";
        private const string SlotUiShaderName = "PuzzleGlass/SlotSquircleUI";
        private const string BakedShaderName = "PuzzleGlass/BakedGlassQuad";
        private const float ScanInterval = 0.25f;
        private const int MaxBakeSize = 2048;
        private const int MinBakeSize = 32;
        // Ekrandaki boyut bu orandan fazla değişirse (kamera yakınlaşması vb.) yeniden pişir
        private const float ResizeTolerance = 0.15f;

        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int UseTrackSdfId = Shader.PropertyToID("_UseTrackSDF");
        private static readonly int UseExitChanSdfId = Shader.PropertyToID("_UseExitChanSDF");
        private static readonly int SlotModeId = Shader.PropertyToID("_SlotMode");
        private static readonly string[] BakeableModes = { "_IsTrackChannel", "_IsExitChannels", "_IsRoundedBoxPool" };
        private static readonly string[] NonBakeableModes = { "_IsCollar", "_IsFeedTube", "_TrackBake" };

        private class Entry
        {
            public MeshRenderer renderer;
            public Material original;
            public Material baked;
            public RenderTexture rt;
        }

        private static GlassChamberBakeCache s_Instance;

        private readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();
        private readonly List<int> _removeBuffer = new List<int>();
        private Shader _chamberShader;
        private Shader _slotUiShader;
        private Shader _bakedShader;
        private float _nextScan;
        private bool _wasEnabled = true;
        private bool _scanDirty = true;
        private Camera _cachedCam;
        private int _lastScreenW = -1;
        private int _lastScreenH = -1;
        private float _lastRenderScale = -1f;
        private float _lastOrthoSize = -1f;

        /// <summary>Yeni cam/kavanoz/dock quad'ları kurulduğunda bir sonraki karede tek seferlik taramayı tetikler.</summary>
        public static void MarkDirty()
        {
            if (s_Instance != null) s_Instance._scanDirty = true;
        }

        /// <summary>Aynı materyalin parametreleri yerinde değiştiğinde (örn. slot sayısı değişen dock) ilgili pişirmeyi yeniler.</summary>
        public static void InvalidateMaterial(Material originalMat)
        {
            if (s_Instance == null || originalMat == null) return;
            s_Instance._removeBuffer.Clear();
            foreach (var kv in s_Instance._entries)
            {
                if (kv.Value.original == originalMat)
                {
                    ReleaseEntry(kv.Value, restore: true);
                    s_Instance._removeBuffer.Add(kv.Key);
                }
            }
            for (int i = 0; i < s_Instance._removeBuffer.Count; i++)
                s_Instance._entries.Remove(s_Instance._removeBuffer[i]);
            s_Instance._scanDirty = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInitialize()
        {
            if (s_Instance != null) return;
            var go = new GameObject("GlassChamberBakeCache");
            DontDestroyOnLoad(go);
            s_Instance = go.AddComponent<GlassChamberBakeCache>();
        }

        private void Awake()
        {
            _chamberShader = Shader.Find(ChamberShaderName);
            _slotUiShader = Shader.Find(SlotUiShaderName);
            _bakedShader = Shader.Find(BakedShaderName);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus) _scanDirty = true;
        }

        private void LateUpdate()
        {
            if (_chamberShader == null || _bakedShader == null) return;

            if (!Enabled)
            {
                if (_wasEnabled) RestoreAll();
                _wasEnabled = false;
                return;
            }
            if (!_wasEnabled)
            {
                _wasEnabled = true;
                _scanDirty = true;
            }

            if (!_scanDirty && Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + ScanInterval;

            if (_cachedCam == null || !_cachedCam.isActiveAndEnabled) _cachedCam = Camera.main;
            Camera cam = _cachedCam;
            if (cam == null) return;

            float renderScale = 1f;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp) renderScale = urp.renderScale;

            int sw = Screen.width;
            int sh = Screen.height;
            float ortho = cam.orthographicSize;
            if (sw != _lastScreenW || sh != _lastScreenH
                || !Mathf.Approximately(renderScale, _lastRenderScale)
                || !Mathf.Approximately(ortho, _lastOrthoSize))
            {
                _lastScreenW = sw;
                _lastScreenH = sh;
                _lastRenderScale = renderScale;
                _lastOrthoSize = ortho;
                _scanDirty = true;
            }

            // Mevcut pişirilmiş quad'ları doğrula (sadece 2-6 öğe): yok olan/değişen varsa tam sahne taraması açılır
            _removeBuffer.Clear();
            foreach (var kv in _entries)
            {
                Entry e = kv.Value;
                if (e.renderer == null || e.original == null)
                {
                    ReleaseEntry(e, restore: false);
                    _removeBuffer.Add(kv.Key);
                    _scanDirty = true;
                }
                else if (e.renderer.sharedMaterial != e.baked)
                {
                    // Başka bir kod malzemeyi değiştirdi: bizim pişirmemiz artık geçersiz
                    ReleaseEntry(e, restore: false);
                    _removeBuffer.Add(kv.Key);
                    _scanDirty = true;
                }
                else if (e.renderer.enabled && e.renderer.gameObject.activeInHierarchy)
                {
                    if (!ComputeBakeSize(cam, e.renderer, renderScale, out int w, out int h)) continue;
                    bool lost = e.rt == null || !e.rt.IsCreated();
                    bool resized = !lost
                        && (Mathf.Abs(w - e.rt.width) > e.rt.width * ResizeTolerance
                         || Mathf.Abs(h - e.rt.height) > e.rt.height * ResizeTolerance);
                    if (lost || resized) Bake(e, w, h);
                }
            }
            for (int i = 0; i < _removeBuffer.Count; i++) _entries.Remove(_removeBuffer[i]);

            if (!_scanDirty) return;
            _scanDirty = false;

            var renderers = FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer mr = renderers[i];
                if (!mr.enabled || !mr.gameObject.activeInHierarchy) continue;
                int id = mr.GetInstanceID();
                if (_entries.ContainsKey(id)) continue;

                Material mat = mr.sharedMaterial;
                if (!IsBakeable(mr, mat)) continue;
                if (!ComputeBakeSize(cam, mr, renderScale, out int bw, out int bh)) continue;

                var entry = new Entry { renderer = mr, original = mat };
                if (Bake(entry, bw, bh))
                {
                    mr.sharedMaterial = entry.baked;
                    _entries[id] = entry;
                }
                else
                {
                    ReleaseEntry(entry, restore: false);
                }
            }
        }

        private bool IsBakeable(MeshRenderer mr, Material mat)
        {
            if (mat == null) return false;

            if (mat.shader == _chamberShader)
            {
                bool modeOk = false;
                for (int i = 0; i < BakeableModes.Length; i++)
                    if (mat.HasProperty(BakeableModes[i]) && mat.GetFloat(BakeableModes[i]) > 0.5f) { modeOk = true; break; }
                if (!modeOk) return false;
                for (int i = 0; i < NonBakeableModes.Length; i++)
                    if (mat.HasProperty(NonBakeableModes[i]) && mat.GetFloat(NonBakeableModes[i]) > 0.5f) return false;
            }
            else if (_slotUiShader != null && mat.shader == _slotUiShader)
            {
                // Yalnızca statik büyük SDF yüzeyler: Mode 3 (Trapezoid Dock) ve Mode 4 (Cam Vitrini Arka Planı)
                if (!mat.HasProperty(SlotModeId) || mat.GetFloat(SlotModeId) < 2.5f) return false;
            }
            else
            {
                return false;
            }

            // Yalnızca köşe UV'leri (0,0)-(1,1) olan düz 4 köşeli quad'lar: dokuya UV ile birebir eşlenebilir
            var mf = mr.GetComponent<MeshFilter>();
            Mesh mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null || mesh.vertexCount != 4 || !mesh.isReadable) return false;
            var uvs = mesh.uv;
            if (uvs == null || uvs.Length != 4) return false;
            for (int i = 0; i < 4; i++)
            {
                Vector2 uv = uvs[i];
                bool cornerX = Mathf.Abs(uv.x) < 1e-4f || Mathf.Abs(uv.x - 1f) < 1e-4f;
                bool cornerY = Mathf.Abs(uv.y) < 1e-4f || Mathf.Abs(uv.y - 1f) < 1e-4f;
                if (!cornerX || !cornerY) return false;
            }
            return true;
        }

        /// <summary>Quad'ın ekranda (render scale dahil) kapladığı piksel boyutu: kenar yumuşatma canlı shader'la aynı kalsın.</summary>
        private static bool ComputeBakeSize(Camera cam, MeshRenderer mr, float renderScale, out int w, out int h)
        {
            w = h = 0;
            var mf = mr.GetComponent<MeshFilter>();
            Mesh mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null) return false;

            // Quad'ın yerel x ve y kenarlarını ekrana izdüşür (UV u ve v yönleri)
            Bounds lb = mesh.bounds;
            Transform t = mr.transform;
            Vector3 p00 = cam.WorldToScreenPoint(t.TransformPoint(new Vector3(lb.min.x, lb.min.y, lb.center.z)));
            Vector3 p10 = cam.WorldToScreenPoint(t.TransformPoint(new Vector3(lb.max.x, lb.min.y, lb.center.z)));
            Vector3 p01 = cam.WorldToScreenPoint(t.TransformPoint(new Vector3(lb.min.x, lb.max.y, lb.center.z)));
            float sw = Vector2.Distance(p00, p10) * renderScale;
            float sh = Vector2.Distance(p00, p01) * renderScale;
            if (sw < 1f || sh < 1f) return false;

            // Çok büyükse en-boy oranını koruyarak küçült
            float scale = Mathf.Min(1f, MaxBakeSize / Mathf.Max(sw, sh));
            w = Mathf.Clamp(Mathf.CeilToInt(sw * scale), MinBakeSize, MaxBakeSize);
            h = Mathf.Clamp(Mathf.CeilToInt(sh * scale), MinBakeSize, MaxBakeSize);
            return true;
        }

        private bool Bake(Entry e, int w, int h)
        {
            if (e.rt == null || e.rt.width != w || e.rt.height != h)
            {
                ReleaseRT(e);
                e.rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                {
                    name = "BakedGlass_" + e.original.name,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    useMipMap = false,
                    autoGenerateMips = false,
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
            if (!e.rt.IsCreated() && !e.rt.Create()) return false;

            // Orijinal malzemeyi geçici olarak "üzerine yaz" karışımıyla dokuya çiz: doku, shader'ın ham çıktısını saklar
            Material m = e.original;
            float src = m.HasProperty(SrcBlendId) ? m.GetFloat(SrcBlendId) : (float)BlendMode.SrcAlpha;
            float dst = m.HasProperty(DstBlendId) ? m.GetFloat(DstBlendId) : (float)BlendMode.OneMinusSrcAlpha;
            m.SetFloat(SrcBlendId, (float)BlendMode.One);
            m.SetFloat(DstBlendId, (float)BlendMode.Zero);

            // Pişirme tek seferlik: mesafe alanını FP16 SDF dokusundan değil, shader'ın tam hassasiyetli segment döngüsünden
            // hesaplat. Mobil GPU'lar FP16 dokuyu düşük hassasiyetle filtrelediği için dokudan türetilen normal gürültülüydü
            // ve keskin parlamada karıncalı/kumlu beyaz noktalar yapıyordu. Döngü pahalı ama burada level başına bir kez çalışır.
            float prevUseTrack = Shader.GetGlobalFloat(UseTrackSdfId);
            float prevUseExit = Shader.GetGlobalFloat(UseExitChanSdfId);
            Shader.SetGlobalFloat(UseTrackSdfId, 0f);
            Shader.SetGlobalFloat(UseExitChanSdfId, 0f);

            var prevActive = RenderTexture.active;
            RenderTexture.active = e.rt;
            GL.Clear(false, true, Color.clear);
            Graphics.Blit(null, e.rt, m, 0);
            RenderTexture.active = prevActive;

            Shader.SetGlobalFloat(UseTrackSdfId, prevUseTrack);
            Shader.SetGlobalFloat(UseExitChanSdfId, prevUseExit);

            m.SetFloat(SrcBlendId, src);
            m.SetFloat(DstBlendId, dst);

            if (e.baked == null)
            {
                e.baked = new Material(_bakedShader)
                {
                    name = m.name + "_Baked",
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
            e.baked.SetTexture(MainTexId, e.rt);
            e.baked.SetFloat(SrcBlendId, src);
            e.baked.SetFloat(DstBlendId, dst);
            e.baked.renderQueue = m.renderQueue;
            return true;
        }

        private void RestoreAll()
        {
            foreach (var e in _entries.Values) ReleaseEntry(e, restore: true);
            _entries.Clear();
        }

        private static void ReleaseRT(Entry e)
        {
            if (e.rt == null) return;
            if (RenderTexture.active == e.rt) RenderTexture.active = null;
            e.rt.Release();
            Destroy(e.rt);
            e.rt = null;
        }

        private static void ReleaseEntry(Entry e, bool restore)
        {
            if (restore && e.renderer != null && e.original != null && e.renderer.sharedMaterial == e.baked)
                e.renderer.sharedMaterial = e.original;
            ReleaseRT(e);
            if (e.baked != null) { Destroy(e.baked); e.baked = null; }
        }

        private void OnDestroy()
        {
            RestoreAll();
            if (s_Instance == this) s_Instance = null;
        }
    }
}
