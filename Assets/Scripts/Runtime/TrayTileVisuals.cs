using UnityEngine;

namespace PuzzleGlass
{
    /// <summary>
    /// Slot sırasının arkasındaki çok katmanlı (layered) tema uyumlu dok tablası ve
    /// üstündeki koyu indigo-mor bireysel slot yuvalarının ortak malzeme ve geometri yardımcıları.
    /// </summary>
    public static class TrayTileVisuals
    {
        // Slot hücresi ve arka dok tablası oranları
        public const float SlotWellRatio = 0.87f;
        public const float TileOuterRatio = 0.82f;
        public const float TileInnerRatio = 0.73f;
        public const float TileRadiusRatio = 0.155f;
        public const float FramePadding = 0.062f;
        public const float InnerMargin = 0.068f;
        public const float FrameCornerRadius = 0.165f;
        public const float FrameLowerLip = 0.060f;

        // Park alanı tablası (Bus Traffic Fever oranları): slot satırının üstünde dar, altında geniş pay.
        /// <summary>Slot satırı merkezinden tablanın üst kenarına, slotun yarısına ek pay.</summary>
        public const float DockTopMargin = 0.15f;
        /// <summary>Slot satırı merkezinden tablanın alt kenarına, slotun yarısına ek pay.</summary>
        public const float DockBottomMargin = 0.38f;
        /// <summary>Shader'da yüzeyin quad merkezine göre yukarı kaydırılması (kalınlık ve gölge quad'a sığsın diye).</summary>
        public const float DockShaderSurfaceShift = 0.055f * 0.5f + 0.02f;

        /// <summary>Slotun görünen kare boyu.</summary>
        public static float SlotVisualSize(float cellSize)
        {
            float c = Mathf.Max(0.1f, cellSize);
            return (SlotSystem.Instance != null)
                ? Mathf.Min(c * 0.94f, Mathf.Max(0.42f, SlotSystem.Instance.slotScale * 1.18f))
                : c;
        }

        /// <summary>Slot satırı merkezinden tablanın üst ve alt kenarına uzaklık (satır ölçeği öncesi).</summary>
        public static void GetDockExtents(float cellSize, out float top, out float bottom)
        {
            float s = SlotVisualSize(cellSize);
            top = s * 0.5f + DockTopMargin;
            bottom = s * 0.5f + DockBottomMargin;
        }

        private static Material s_FrameDark, s_FrameFront, s_FrameHighlight, s_FrameMidStep, s_FloorShadow, s_Floor;
        private static Material s_SlotWell, s_TileBevel, s_TileBevelHi, s_TileInnerShadow, s_TileBody, s_TileBodyHi;
        private static Material s_VipBevel, s_VipInnerShadow, s_VipBody;
        private static Material s_LockBevel, s_LockInnerShadow, s_LockBody;
        private static Material s_PlusShadowMat, s_PlusBaseMat, s_PlusFrontMat;
        private static Material s_DockDropShadowMat;
        private static Color s_CachedBody;

        // 1) Arka Dok Tablası (Layered Dock Platform — oyunun lavanta/indigo temasına uyumlu katmanlar)
        public static Color ThemeFrameLip       = new Color(0.24f, 0.22f, 0.50f, 1f); // en alt 3D koyu indigo dudak
        public static Color ThemeFrameFront     = new Color(0.56f, 0.58f, 0.88f, 1f); // yükseltilmiş dış çerçeve (periwinkle-lavanta)
        public static Color ThemeFrameHighlight = new Color(0.74f, 0.76f, 0.96f, 1f); // dış çerçeve üst parlak kenar
        public static Color ThemeFrameMidStep   = new Color(0.40f, 0.40f, 0.70f, 1f); // dış çerçeve ile iç havuz arası ara katman
        public static Color ThemeFloorShadow    = new Color(0.20f, 0.18f, 0.43f, 1f); // iç havuz üst 3D gölge basamağı
        public static Color ThemeFloor          = new Color(0.31f, 0.29f, 0.58f, 1f); // gömülü dok ana zemini

        // 2) Bireysel Koyu Slot Hücresi (1. görseldeki koyu indigo-mor slot görünümü)
        public static Color ThemeSlotWell        = new Color(0.09f, 0.08f, 0.25f, 1f); // slotun etrafındaki en koyu oyuk sınır
        public static Color ThemeTileBevel       = new Color(0.28f, 0.26f, 0.57f, 1f); // slot dış çerçeve kenarı
        public static Color ThemeTileBevelHi     = new Color(0.36f, 0.34f, 0.69f, 1f); // slot çerçevesi üst ışık vurgusu
        public static Color ThemeTileInnerShadow = new Color(0.11f, 0.10f, 0.28f, 1f); // slot içi üst derinlik gölgesi
        public static Color ThemeTileBody        = new Color(0.17f, 0.15f, 0.41f, 1f); // slot koyu iç gövdesi (rgb 44,37,104)

        private static Material s_DockTrapMat;
        private static Material s_SlotRegularMat, s_SlotRegularHiMat, s_SlotVipMat, s_SlotVipHiMat, s_SlotLockedMat;

        private static readonly int SlotModePropId  = Shader.PropertyToID("_SlotMode");
        private static readonly int HighlightPropId = Shader.PropertyToID("_Highlight");
        private static readonly int QuadSizePropId  = Shader.PropertyToID("_QuadSize");
        private static readonly int DockParamsPropId = Shader.PropertyToID("_DockParams");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCaches()
        {
            s_FrameFront = null;
            s_DockDropShadowMat = null;
            s_DockTrapMat = null;
            s_SlotRegularMat = null;
            s_SlotRegularHiMat = null;
            s_SlotVipMat = null;
            s_SlotVipHiMat = null;
            s_SlotLockedMat = null;
        }

        private static Shader GetSlotSdfShader()
        {
            return Shader.Find("PuzzleGlass/SlotSquircleUI")
                ?? Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Sprites/Default");
        }

        private static void EnsureSdfSlotMaterials()
        {
            if (s_SlotRegularMat != null) return;
            Shader sh = GetSlotSdfShader();

            s_SlotRegularMat = new Material(sh) { name = "Mat_SlotSDF_Regular", renderQueue = 2990, enableInstancing = true };
            s_SlotRegularMat.SetFloat(SlotModePropId, 0f);
            s_SlotRegularMat.SetFloat(HighlightPropId, 0f);

            s_SlotRegularHiMat = new Material(sh) { name = "Mat_SlotSDF_Regular_Hi", renderQueue = 2990, enableInstancing = true };
            s_SlotRegularHiMat.SetFloat(SlotModePropId, 0f);
            s_SlotRegularHiMat.SetFloat(HighlightPropId, 1f);

            s_SlotVipMat = new Material(sh) { name = "Mat_SlotSDF_VIP", renderQueue = 2990, enableInstancing = true };
            s_SlotVipMat.SetFloat(SlotModePropId, 1f);
            s_SlotVipMat.SetFloat(HighlightPropId, 0f);

            s_SlotVipHiMat = new Material(sh) { name = "Mat_SlotSDF_VIP_Hi", renderQueue = 2990, enableInstancing = true };
            s_SlotVipHiMat.SetFloat(SlotModePropId, 1f);
            s_SlotVipHiMat.SetFloat(HighlightPropId, 1f);

            s_SlotLockedMat = new Material(sh) { name = "Mat_SlotSDF_LockedPlus", renderQueue = 2990, enableInstancing = true };
            s_SlotLockedMat.SetFloat(SlotModePropId, 2f);
            s_SlotLockedMat.SetFloat(HighlightPropId, 0f);
        }

        public static Material GetSdfSlotMaterial(SlotType type, bool highlighted = false)
        {
            EnsureSdfSlotMaterials();
            switch (type)
            {
                case SlotType.VIP:    return highlighted ? s_SlotVipHiMat : s_SlotVipMat;
                case SlotType.Locked: return s_SlotLockedMat;
                default:              return highlighted ? s_SlotRegularHiMat : s_SlotRegularMat;
            }
        }

        public static Material GetConfiguredTrapezoidDockMaterial(float cellSize, int cells, out float quadW, out float quadH)
        {
            if (s_DockTrapMat == null)
            {
                s_DockTrapMat = new Material(GetSlotSdfShader())
                {
                    name = "Mat_SlotDock_TrapezoidSDF",
                    renderQueue = 2985,
                    enableInstancing = true
                };
            }

            ComputeTrapezoidDimensions(cellSize, cells, out float he, out float rBotWidth, out float rTopWidth, out float cornerRad);
            quadW = rBotWidth * 2f + 0.30f;
            quadH = he * 2f + 0.46f;

            s_DockTrapMat.SetFloat(SlotModePropId, 3f);
            s_DockTrapMat.SetVector(QuadSizePropId, new Vector4(quadW, quadH, 0f, 0f));
            s_DockTrapMat.SetVector(DockParamsPropId, new Vector4(he, rBotWidth, rTopWidth, cornerRad));
            return s_DockTrapMat;
        }

        public static void ComputeTrapezoidDimensions(float cellSize, int cells, out float halfH, out float rBotWidth, out float rTopWidth, out float cornerRad)
        {
            float c = Mathf.Max(0.1f, cellSize);
            int n = Mathf.Max(1, cells);
            float slotOuterHalfX = (n * 0.5f - 0.06f) * c;
            GetDockExtents(c, out float top, out float bottom);
            halfH = (top + bottom) * 0.5f;

            // Ekrana dönük düz plaka (pahsız): ekranın iki yanında küçük pay bırakacak kadar geniş, slotlar ve bloklar
            // gibi tam karşıdan görünür. Üst ve alt yarı genişlik eşit; köşeler yuvarlak.
            float screenHalfW = 0f;
            Camera cam = Camera.main;
            if (cam != null && cam.orthographic)
            {
                float rowScale = SlotSystem.Instance != null ? SlotSystem.Instance.RowFitScale : 1f;
                screenHalfW = cam.orthographicSize * cam.aspect / Mathf.Max(0.1f, rowScale);
            }
            float halfW = Mathf.Max(slotOuterHalfX + 0.28f, screenHalfW - 0.16f);
            rBotWidth = halfW;
            rTopWidth = halfW;
            cornerRad = 0.24f;
        }

        private static void EnsureMaterials()
        {
            if (s_FrameFront != null && s_CachedBody == ThemeTileBody) return;
            s_CachedBody = ThemeTileBody;

            // Dok tablası katman malzemeleri
            s_FrameDark      = Mat(ThemeFrameLip,       "Mat_SlotDockFrameDark",      2001);
            s_FrameHighlight = Mat(ThemeFrameHighlight, "Mat_SlotDockFrameHighlight", 2002);
            s_FrameFront     = Mat(ThemeFrameFront,     "Mat_SlotDockFrameFront",     2003);
            s_FrameMidStep   = Mat(ThemeFrameMidStep,   "Mat_SlotDockFrameMidStep",   2004);
            s_FloorShadow    = Mat(ThemeFloorShadow,    "Mat_SlotDockFloorShadow",    2005);
            s_Floor          = Mat(ThemeFloor,          "Mat_SlotDockFloor",          2006);

            // Bireysel koyu slot katman malzemeleri
            s_SlotWell        = Mat(ThemeSlotWell,        "Mat_SlotOuterWell",        2010);
            s_TileBevelHi     = Mat(ThemeTileBevelHi,     "Mat_SlotTileBevelHi",      2011);
            s_TileBevel       = Mat(ThemeTileBevel,       "Mat_SlotTileBevel",        2012);
            s_TileInnerShadow = Mat(ThemeTileInnerShadow, "Mat_SlotTileInnerShadow",  2013);
            s_TileBody        = Mat(ThemeTileBody,        "Mat_SlotTileBody",         2014);
            s_TileBodyHi      = Mat(new Color(0.29f, 0.27f, 0.62f, 1f), "Mat_SlotTileBodyHighlight", 2015);

            // VIP slot: sıcak altın çerçeve + koyu kehribar iç zemin
            s_VipBevel       = Mat(new Color(0.95f, 0.74f, 0.20f, 1f), "Mat_SlotTileVipBevel",       2012);
            s_VipInnerShadow = Mat(new Color(0.34f, 0.21f, 0.04f, 1f), "Mat_SlotTileVipInnerShadow", 2013);
            s_VipBody        = Mat(new Color(0.50f, 0.33f, 0.07f, 1f), "Mat_SlotTileVipBody",        2014);

            // "+" (kilitli) slot: koyu gece-indigo çerçeve ve iç zemin
            s_LockBevel       = Mat(new Color(0.21f, 0.19f, 0.45f, 1f), "Mat_SlotTileLockedBevel",       2012);
            s_LockInnerShadow = Mat(new Color(0.08f, 0.07f, 0.22f, 1f), "Mat_SlotTileLockedInnerShadow", 2013);
            s_LockBody        = Mat(new Color(0.13f, 0.12f, 0.33f, 1f), "Mat_SlotTileLockedBody",        2014);

            // "+" ikonu (2. görseldeki koyu konturlu/gölgeli 3D yeşil artı)
            s_PlusShadowMat = Mat(new Color(0.05f, 0.15f, 0.14f, 1f), "Mat_SlotPlusShadow", 2016);
            s_PlusBaseMat   = Mat(new Color(0.13f, 0.62f, 0.22f, 1f), "Mat_SlotPlusBase",   2017);
            s_PlusFrontMat  = Mat(new Color(0.24f, 0.89f, 0.34f, 1f), "Mat_SlotPlusFront",  2018);
        }

        private static Material Mat(Color c, string name, int renderQueue)
        {
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Color")
                ?? PuzzleColorPalette.FindToonyHybridShader();
            Material m = new Material(unlit)
            {
                name = name,
                color = c,
                enableInstancing = true,
                renderQueue = renderQueue
            };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            return m;
        }

        public static Material FrameDark      { get { EnsureMaterials(); return s_FrameDark; } }
        public static Material FrameFront     { get { EnsureMaterials(); return s_FrameFront; } }
        public static Material FrameHighlight { get { EnsureMaterials(); return s_FrameHighlight; } }
        public static Material FrameMidStep   { get { EnsureMaterials(); return s_FrameMidStep; } }
        public static Material FloorShadow    { get { EnsureMaterials(); return s_FloorShadow; } }
        public static Material Floor          { get { EnsureMaterials(); return s_Floor; } }

        public static Material SlotWell          { get { EnsureMaterials(); return s_SlotWell; } }
        public static Material TileBevelHi       { get { EnsureMaterials(); return s_TileBevelHi; } }
        public static Material TileBodyHighlight { get { EnsureMaterials(); return s_TileBodyHi; } }

        public static Material PlusShadowMat { get { EnsureMaterials(); return s_PlusShadowMat; } }
        public static Material PlusBaseMat   { get { EnsureMaterials(); return s_PlusBaseMat; } }
        public static Material PlusFrontMat  { get { EnsureMaterials(); return s_PlusFrontMat; } }

        public static Material GetDockDropShadowMaterial()
        {
            if (s_DockDropShadowMat != null) return s_DockDropShadowMat;
            Shader shadowShader = Shader.Find("PuzzleGlass/FakeDropShadow");
            if (shadowShader == null) return null;
            s_DockDropShadowMat = new Material(shadowShader)
            {
                name = "Mat_SlotDockFakeShadow",
                enableInstancing = true
            };
            s_DockDropShadowMat.SetColor("_ShadowColor", new Color(0.14f, 0.12f, 0.34f, 0.32f));
            s_DockDropShadowMat.SetFloat("_FeatherWidth", 0.42f);
            s_DockDropShadowMat.SetFloat("_IsRadial", 0.0f);
            return s_DockDropShadowMat;
        }

        public static void GetTileMaterials(SlotType type, out Material bevel, out Material innerShadow, out Material body)
        {
            EnsureMaterials();
            switch (type)
            {
                case SlotType.VIP:
                    bevel = s_VipBevel;
                    innerShadow = s_VipInnerShadow;
                    body = s_VipBody;
                    break;
                case SlotType.Locked:
                    bevel = s_LockBevel;
                    innerShadow = s_LockInnerShadow;
                    body = s_LockBody;
                    break;
                default:
                    bevel = s_TileBevel;
                    innerShadow = s_TileInnerShadow;
                    body = s_TileBody;
                    break;
            }
        }

        public static void GetTileMaterials(SlotType type, out Material bevel, out Material body)
        {
            GetTileMaterials(type, out bevel, out _, out body);
        }

        public static Material TileBody(SlotType type)
        {
            GetTileMaterials(type, out _, out _, out Material body);
            return body;
        }

        /// <summary>Yuvarlak köşeli, hafif kalınlıklı bir katman parçası (MeshRenderer) oluşturur.</summary>
        public static MeshRenderer Part(string name, Transform parent, Vector3 localPos, float w, float h, float depth, float radius, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshUtils.CreatePillBadge(w, h, depth, Mathf.Min(radius, Mathf.Min(w, h) * 0.48f));
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return mr;
        }

        /// <summary>
        /// Yamuk (trapezoid) dok tablasının dış boyutu (ekrana sığdırma ve dikey hizalama için).
        /// </summary>
        public static void GetFrameSize(float cellSize, int cells, out float innerW, out float innerH, out float outerW, out float outerH)
        {
            ComputeTrapezoidDimensions(cellSize, cells, out float halfH, out float rBotWidth, out float rTopWidth, out _);
            innerW = rTopWidth * 2f - 0.10f;
            innerH = halfH * 2f - 0.10f;
            outerW = rBotWidth * 2f + 0.04f;
            outerH = halfH * 2f + 0.04f;
        }
    }
}
