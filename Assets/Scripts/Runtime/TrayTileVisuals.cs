using UnityEngine;

namespace PuzzleGlass
{
    /// <summary>
    /// Eski sistemdeki alt tahtanın (StagingGridBoard) görünümü: koyu indigo çerçeve, gömülü taban ve her hücrede
    /// açık mor kenar + koyu iç zemin. Slot sırası da aynı görünümü kullanır (tahta hücresi gibi slotlar).
    /// Renkler oyunun açık temasına göre (ThemeXxx alanları).
    /// </summary>
    public static class TrayTileVisuals
    {
        // Tahtanın varsayılan ölçüleri (StagingGridBoard ile aynı oranlar)
        public const float TileOuterRatio = 0.81f;
        public const float TileInnerRatio = 0.76f;
        public const float TileRadiusRatio = 0.15f;
        public const float FramePadding = 0.068f;
        public const float InnerMargin = 0.048f;
        public const float FrameCornerRadius = 0.135f;
        public const float FrameLowerLip = 0.032f;

        private static Material s_FrameDark, s_FrameFront, s_Floor, s_TileBevel, s_TileBody, s_TileBodyHi;
        private static Material s_VipBevel, s_VipBody, s_LockBevel, s_LockBody;
        private static Color s_CachedFront;

        // Oyunun açık temasına uygun palet (HUD'daki periwinkle level rozeti ve lavanta arka planla aynı aile).
        // Tahtanın şekli korunur; renkler eski koyu indigo yerine açık ve yumuşak.
        public static Color ThemeFrameLip   = new Color(0.36f, 0.39f, 0.82f, 1f); // alt dudak (rozet gölgesi tonu)
        public static Color ThemeFrameFront = new Color(0.50f, 0.53f, 0.95f, 1f); // ön çerçeve (periwinkle)
        public static Color ThemeFloor      = new Color(0.62f, 0.64f, 0.96f, 1f); // gömülü taban
        public static Color ThemeTileBevel  = new Color(0.88f, 0.89f, 1.00f, 1f); // hücre kenarı (açık lavanta)
        public static Color ThemeTileBody   = new Color(0.76f, 0.78f, 0.98f, 1f); // hücre içi

        private static void GetColors(out Color frameDark, out Color frameFront, out Color floor, out Color bevel, out Color body)
        {
            frameDark = ThemeFrameLip; frameFront = ThemeFrameFront; floor = ThemeFloor;
            bevel = ThemeTileBevel; body = ThemeTileBody;
        }

        private static void EnsureMaterials()
        {
            GetColors(out Color frameDark, out Color frameFront, out Color floor, out Color bevel, out Color body);
            // Tahta renkleri değiştiyse (tema) malzemeleri yeniden kur
            if (s_FrameFront != null && s_CachedFront == frameFront) return;
            s_CachedFront = frameFront;

            Color coolShadow = new Color(0.62f, 0.64f, 0.86f, 1f);
            s_FrameDark = Mat(frameDark, coolShadow, "Mat_SlotTrayFrameDark", 0.14f);
            s_FrameFront = Mat(frameFront, coolShadow, "Mat_SlotTrayFrameFront", 0.14f);
            s_Floor = Mat(floor, new Color(0.40f, 0.42f, 0.60f, 1f), "Mat_SlotTrayFloor", 0.12f);
            s_TileBevel = Mat(bevel, coolShadow, "Mat_SlotTileBevel", 0.14f);
            s_TileBody = Mat(body, new Color(0.44f, 0.46f, 0.64f, 1f), "Mat_SlotTileBody", 0.12f);
            s_TileBodyHi = Mat(new Color(0.97f, 0.97f, 1.00f, 1f), coolShadow, "Mat_SlotTileBodyHighlight", 0.20f);

            // VIP: aynı hücre, sıcak altın tonları
            s_VipBevel = Mat(new Color(0.95f, 0.72f, 0.22f, 1f), new Color(0.75f, 0.55f, 0.30f, 1f), "Mat_SlotTileVipBevel", 0.16f);
            s_VipBody = Mat(new Color(0.62f, 0.42f, 0.10f, 1f), new Color(0.70f, 0.55f, 0.35f, 1f), "Mat_SlotTileVipBody", 0.14f);
            // "+" (kilitli) slot: tahtadan daha koyu ve soluk
            s_LockBevel = Mat(Color.Lerp(bevel, floor, 0.45f), coolShadow, "Mat_SlotTileLockedBevel", 0.14f);
            s_LockBody = Mat(Color.Lerp(body, floor, 0.55f), coolShadow, "Mat_SlotTileLockedBody", 0.12f);
        }

        private static Material Mat(Color c, Color shadow, string name, float indirect)
        {
            return PuzzleColorPalette.CreateToonyHypercasualMaterial(c, shadow, name, glossyCandy: false, indirectIntensity: indirect);
        }

        public static Material FrameDark { get { EnsureMaterials(); return s_FrameDark; } }
        public static Material FrameFront { get { EnsureMaterials(); return s_FrameFront; } }
        public static Material Floor { get { EnsureMaterials(); return s_Floor; } }
        public static Material TileBodyHighlight { get { EnsureMaterials(); return s_TileBodyHi; } }

        public static void GetTileMaterials(SlotType type, out Material bevel, out Material body)
        {
            EnsureMaterials();
            switch (type)
            {
                case SlotType.VIP: bevel = s_VipBevel; body = s_VipBody; break;
                case SlotType.Locked: bevel = s_LockBevel; body = s_LockBody; break;
                default: bevel = s_TileBevel; body = s_TileBody; break;
            }
        }

        public static Material TileBody(SlotType type)
        {
            GetTileMaterials(type, out _, out Material body);
            return body;
        }

        /// <summary>Tahtadaki gibi yuvarlak köşeli, hafif kalınlıklı bir parça (MeshRenderer) oluşturur.</summary>
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
        /// Tahta çerçevesinin dış boyutu: içte (cols x 1) hücre + iç boşluk + çerçeve kalınlığı.
        /// </summary>
        public static void GetFrameSize(float cellSize, int cells, out float innerW, out float innerH, out float outerW, out float outerH)
        {
            innerW = cells * cellSize + InnerMargin * 2f;
            innerH = cellSize + InnerMargin * 2f;
            outerW = innerW + FramePadding * 2f;
            outerH = innerH + FramePadding * 2f;
        }
    }
}
