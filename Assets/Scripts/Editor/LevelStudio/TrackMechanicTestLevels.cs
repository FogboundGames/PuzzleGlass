using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PuzzleGlass.Editor.Studio
{
    /// <summary>
    /// Pist içi mekaniklerin test levellerini üretir: Renk kapısı (Level 30) ve Boğaz (Level 31).
    /// Her level mekaniği TEK BAŞINA tanıtır (buz/kilit/dolu kutu yok), böylece mekaniğin oyuncu kararını
    /// gerçekten değiştirip değiştirmediği ayrı ölçülebilir.
    ///
    /// Önemli: Banttaki toplar bottomToTopGroups'tan, kutuların talebi monsters'tan gelir. Renk kapısı leveli bu
    /// ayrımı kullanır: bantta kırmızı top var ama kırmızı kutu yok; kırmızılar ancak boyandıktan sonra toplanır.
    /// </summary>
    public static class TrackMechanicTestLevels
    {
        private const string LevelsFolder = "Assets/Levels";
        private const string Template = "Assets/Levels/Level_73.asset";

        [MenuItem("Tools/PuzzleGlass/🧪 Pist İçi Mekanik Test Levelleri (Renk Kapısı + Boğaz + Yapışık Küme)", false, 31)]
        public static void CreateTestLevels()
        {
            var template = AssetDatabase.LoadAssetAtPath<PuzzleGlassLevelData>(Template);
            if (template == null)
            {
                EditorUtility.DisplayDialog("Şablon yok", $"{Template} bulunamadı.", "Tamam");
                return;
            }
            if (!Directory.Exists(LevelsFolder)) Directory.CreateDirectory(LevelsFolder);

            var gateLevel = BuildGateLevel(template);
            var throat = BuildThroatLevel(template);
            var cluster = BuildClusterLevel(template);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            SyncGameManagerLevels();

            EditorUtility.DisplayDialog(
                "Test levelleri hazır",
                "Level 30 — Renk kapısı\n" +
                "  Bantta 130 kırmızı top var, ama kırmızı kutu YOK.\n" +
                "  Pistin sol kenarındaki kapı kırmızıyı maviye çevirir; mavi talebin yarısı\n" +
                "  ancak boyama ile karşılanır.\n\n" +
                "Level 31 — Boğaz\n" +
                "  Pistin sol kenarında kanal ~%42 genişliğe daralır. Bandın kapasitesi düşer,\n" +
                "  kalan toplar giriş kolunda kuyrukta bekler (gizli kuyruk 0.15).\n\n" +
                "Level 32 — Yapışık toplar\n" +
                "  Bantta iki gülen yüz tek parça dönüyor: kırmızı ve yeşil, 18'er top.\n" +
                "  Kırmızı yüzü MAVİ kutu, yeşil yüzü SARI kutu tamamlanınca çözüyor.\n" +
                "  Ön sıra tamamen kırmızı/yeşil: 5 slotu onlarla doldurmak = kayıp.\n\n" +
                "Üçü de oyunun level listesine eklendi.",
                "Tamam");

            if (gateLevel != null) Selection.activeObject = gateLevel;
            Debug.Log($"[TestLevels] {AssetDatabase.GetAssetPath(gateLevel)}, {AssetDatabase.GetAssetPath(throat)} " +
                      $"ve {AssetDatabase.GetAssetPath(cluster)} yazıldı.");
        }

        // ==================== Level 30: Renk Kapısı ====================

        private static PuzzleGlassLevelData BuildGateLevel(PuzzleGlassLevelData template)
        {
            var lvl = CloneBase(template, levelNumber: 30, difficulty: LevelDifficulty.Medium);

            // Bant listesi kutulardan türetilir (motor renk dengesini kendisi kurar); kapının preloadCount'u
            // mavi talebin 130'unu bantta kırmızıya çevirir.
            lvl.bottomToTopGroups = new List<LevelMonsterEntry>();

            // Kutuların talebi: 260 mavi + 130 sarı = 390. Kırmızı kutu yok.
            // Mavi talebin 130'u ancak kırmızı toplar boyanınca karşılanabilir.
            int[] blueCounts = { 33, 32, 33, 32, 33, 32, 33, 32 };   // 8 kutu, 260
            int[] yellowCounts = { 33, 32, 33, 32 };                 // 4 kutu, 130

            // Kutu ızgarası (B = mavi, Y = sarı). Üstteki satır y=2, oyuncunun eriştiği ön sıra.
            // Her satırda iki renk de var: oyuncu her an "hemen toplanan sarı" ile "boyama bekleyen mavi"
            // arasında seçim yapar. Toplam 8 mavi + 4 sarı kutu.
            string[] layout = { "BYBB", "BBYB", "YBBY" };

            var boxes = new List<LevelMonsterEntry>(12);
            int bi = 0, yi = 0;
            for (int r = 0; r < layout.Length; r++)
            {
                int row = 2 - r;
                for (int col = 0; col < 4; col++)
                {
                    bool blue = layout[r][col] == 'B';
                    boxes.Add(new LevelMonsterEntry(
                        blue ? PuzzleColor.Blue : PuzzleColor.Yellow,
                        blue ? blueCounts[bi++] : yellowCounts[yi++],
                        CarrierBlockShape.Rect1x1,
                        new Vector2Int(col, row)));
                }
            }
            lvl.monsters = boxes;

            // Renk kapısı: halkanın sol kenarında kanalı enine kesen kırmızı -> mavi kapı
            lvl.trackColorGates = new List<TrackColorGate>
            {
                new TrackColorGate(new Vector2(0.10f, 0.50f), 0.62f, PuzzleColor.Red, PuzzleColor.Blue)
                {
                    name = "Kırmızı → Mavi",
                    preloadCount = 130
                }
            };
            lvl.trackThroatEnabled = false;
            lvl.trackHiddenRatio = 0f;
            lvl.ballClusters = new List<BallClusterShape>();

            // Bant listesini kutular + boyacı ön yüklemesine göre kur
            lvl.RebuildTripletsFromBoxes();

            return SaveLevel(lvl, 30);
        }

        // ==================== Level 31: Boğaz ====================

        private static PuzzleGlassLevelData BuildThroatLevel(PuzzleGlassLevelData template)
        {
            var lvl = CloneBase(template, levelNumber: 31, difficulty: LevelDifficulty.Medium);

            var palette = new[] { PuzzleColor.Blue, PuzzleColor.Yellow, PuzzleColor.Red, PuzzleColor.Green };

            // Bant ve talep birebir: 4 renk x 95 top = 380
            lvl.bottomToTopGroups = new List<LevelMonsterEntry>();
            for (int c = 0; c < palette.Length; c++)
                AddBallGroup(lvl.bottomToTopGroups, palette[c], 95);

            // 12 kutu (4x3): renk başına 3 kutu, 95 = 32 + 32 + 31
            int[] split = { 32, 32, 31 };
            var boxes = new List<LevelMonsterEntry>(12);
            int idx = 0;
            for (int row = 2; row >= 0; row--)
            {
                for (int col = 0; col < 4; col++)
                {
                    boxes.Add(new LevelMonsterEntry(
                        palette[col],
                        split[idx / 4],
                        CarrierBlockShape.Rect1x1,
                        new Vector2Int(col, row)));
                    idx++;
                }
            }
            lvl.monsters = boxes;

            lvl.trackColorGates = new List<TrackColorGate>();
            lvl.trackThroatEnabled = true;
            lvl.trackThroatNormalizedCenter = new Vector2(0.10f, 0.52f);
            lvl.trackThroatRadius = 0.24f;
            lvl.trackThroatWidthRatio = 0.42f;
            // Boğaz bandın kapasitesini düşürür: sığmayan toplar giriş kolunda kuyrukta beklesin
            lvl.trackHiddenRatio = 0.15f;
            lvl.ballClusters = new List<BallClusterShape>();

            return SaveLevel(lvl, 31);
        }

        // ==================== Level 32: Zincirli Toplar ====================

        /// <summary>
        /// Yapışık toplar. Bandın üstünde iki gülen yüz tek parça döner: biri KIRMIZI, biri YEŞİL.
        /// Kırmızı yüz ancak bir MAVİ kutu, yeşil yüz ancak bir SARI kutu tamamlanınca çözülür.
        ///
        /// İlk hamle tuzağı: ön sıra tamamen kırmızı/yeşil kutu. Bantta serbest kırmızı/yeşil sayısı iki
        /// kutuya ancak yetiyor, yani üçüncüsü slotta tıkanıp kalıyor. Oyuncu 5 slotu kırmızı/yeşil ile
        /// doldurursa hiçbiri toplayamaz ve level kaybedilir. Doğru oyun: arka sıralardaki mavi ve sarı
        /// kutuları kazıp önce yüzleri çözmek.
        /// </summary>
        private static PuzzleGlassLevelData BuildClusterLevel(PuzzleGlassLevelData template)
        {
            var lvl = CloneBase(template, levelNumber: 32, difficulty: LevelDifficulty.Hard);

            var palette = new[] { PuzzleColor.Blue, PuzzleColor.Yellow, PuzzleColor.Red, PuzzleColor.Green };

            lvl.stagingGridCols = 4;
            lvl.stagingGridRows = 3;

            // Bant: kırmızı 24, yeşil 24, mavi 12, sarı 12 = 72. Talep birebir aynı.
            // Yüzler 18'er topu dondurur: serbest kırmızı/yeşil sadece 6, yani ilk kutu tam biter,
            // sonraki her kırmızı/yeşil kutu yüz çözülene kadar slotta tıkalı kalır.
            lvl.bottomToTopGroups = new List<LevelMonsterEntry>();
            AddBallGroup(lvl.bottomToTopGroups, PuzzleColor.Red, 24);
            AddBallGroup(lvl.bottomToTopGroups, PuzzleColor.Green, 24);
            AddBallGroup(lvl.bottomToTopGroups, PuzzleColor.Blue, 12);
            AddBallGroup(lvl.bottomToTopGroups, PuzzleColor.Yellow, 12);

            // Izgara (üst satır = ön sıra). Yüzleri çözen mavi/sarı kutular arkada.
            //   row2:  R G R G   <- hepsi tıkanmaya aday
            //   row1:  G R G R   <- hâlâ tıkanmaya aday; mavi/sarıya ulaşmak için bunları harcaman gerek
            //   row0:  B Y B Y   <- yüzleri çözen kutular en arkada
            string[] layout = { "RGRG", "GRGR", "BYBY" };
            const int perBox = 6;

            var boxes = new List<LevelMonsterEntry>(12);
            for (int r = 0; r < layout.Length; r++)
            {
                int row = 2 - r;
                for (int col = 0; col < 4; col++)
                {
                    boxes.Add(new LevelMonsterEntry(ColorFromCode(layout[r][col]), perBox,
                        CarrierBlockShape.Rect1x1, new Vector2Int(col, row)));
                }
            }
            lvl.monsters = boxes;

            // İki gülen yüz: kırmızı (mavi kutu çözer) ve yeşil (sarı kutu çözer)
            var redFace = BallClusterShape.CreateSmiley(PuzzleColor.Red);
            redFace.name = "Kırmızı Yüz";
            redFace.positionAlongBelt = 0.26f;
            redFace.unlockBoxColor = PuzzleColor.Blue;
            redFace.unlockAfterBoxes = 1;

            var greenFace = BallClusterShape.CreateSmiley(PuzzleColor.Green);
            greenFace.name = "Yeşil Yüz";
            greenFace.positionAlongBelt = 0.70f;
            greenFace.unlockBoxColor = PuzzleColor.Yellow;
            greenFace.unlockAfterBoxes = 1;

            lvl.ballClusters = new List<BallClusterShape> { redFace, greenFace };

            lvl.trackHiddenRatio = 0f;
            lvl.trackColorGates = new List<TrackColorGate>();
            lvl.trackThroatEnabled = false;

            return SaveLevel(lvl, 32);
        }

        private static PuzzleColor ColorFromCode(char c)
        {
            switch (c)
            {
                case 'B': return PuzzleColor.Blue;
                case 'Y': return PuzzleColor.Yellow;
                case 'R': return PuzzleColor.Red;
                case 'G': return PuzzleColor.Green;
                case 'P': return PuzzleColor.Purple;
                case 'O': return PuzzleColor.Orange;
                default: return PuzzleColor.Blue;
            }
        }

        // ==================== Yardımcılar ====================

        /// <summary>
        /// Bant listesine renk ekler. LevelMonsterEntry.count 99 ile sınırlı (MaxBallsPerEntry), bu yüzden
        /// daha büyük sayılar birden çok girdiye bölünür; yoksa toplar sessizce 99'a kırpılır.
        /// </summary>
        private static void AddBallGroup(List<LevelMonsterEntry> list, PuzzleColor color, int total)
        {
            int left = total;
            while (left > 0)
            {
                int chunk = Mathf.Min(left, LevelMonsterEntry.MaxBallsPerEntry);
                list.Add(new LevelMonsterEntry(color, chunk));
                left -= chunk;
            }
        }

        private static PuzzleGlassLevelData CloneBase(
            PuzzleGlassLevelData template, int levelNumber, LevelDifficulty difficulty)
        {
            var lvl = Object.Instantiate(template);
            lvl.levelNumber = levelNumber;
            lvl.levelName = $"Level {levelNumber}";
            lvl.difficulty = difficulty;

            // Pist: halka (ArchLoop), otomatik dizim, katman kilidi/katmanları koru kapalı
            lvl.useCustomGlassShape = true;
            lvl.glassPreset = GlassShapePreset.TrackChannel;
            lvl.trackPreset = GlassTrackPreset.ArchLoop;
            lvl.trackLanesAcross = 0;
            lvl.trackAutoFitToBalls = true;
            lvl.trackScale = 1f;
            lvl.trackBranches = new List<GlassTrackBranch>();
            lvl.trackLayerLock = false;
            lvl.trackKeepLayers = false;
            lvl.useManualBallLayout = false;
            lvl.manualBalls = new List<ManualPoolBallEntry>();
            lvl.manualTrackCapacity = 0;

            // Tepsi: 4 sütun x 3 sıra
            lvl.stagingGridCols = 4;
            lvl.stagingGridRows = 3;
            lvl.gridWalls = new List<Vector2Int>();

            // Diğer mekanikler kapalı: mekanik tek başına tanıtılır
            lvl.dividers = new List<GlassDividerData>();
            lvl.compartmentZones = new List<CompartmentColorZone>();
            lvl.tutorial = LevelTutorial.None;

            return lvl;
        }

        private static PuzzleGlassLevelData SaveLevel(PuzzleGlassLevelData lvl, int levelNumber)
        {
            string path = $"{LevelsFolder}/Level_{levelNumber + 45}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<PuzzleGlassLevelData>(path);
            if (existing != null)
            {
                // Varsa üstüne yaz: GUID korunur, level listesindeki referans bozulmaz
                EditorUtility.CopySerialized(lvl, existing);
                Object.DestroyImmediate(lvl);
                EditorUtility.SetDirty(existing);
                return existing;
            }

            AssetDatabase.CreateAsset(lvl, path);
            EditorUtility.SetDirty(lvl);
            return lvl;
        }

        private static void SyncGameManagerLevels()
        {
            var gm = Object.FindFirstObjectByType<PuzzleGlassGameManager>();
            if (gm == null) return;

            var all = new List<PuzzleGlassLevelData>();
            foreach (string guid in AssetDatabase.FindAssets("t:PuzzleGlassLevelData", new[] { LevelsFolder }))
            {
                var a = AssetDatabase.LoadAssetAtPath<PuzzleGlassLevelData>(AssetDatabase.GUIDToAssetPath(guid));
                if (a != null) all.Add(a);
            }
            all.Sort((x, y) => x.levelNumber.CompareTo(y.levelNumber));
            gm.levels = all;
            EditorUtility.SetDirty(gm);
        }
    }
}
