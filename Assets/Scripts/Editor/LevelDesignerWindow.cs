using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PuzzleGlass.Editor
{
    public class LevelDesignerWindow : EditorWindow
    {
        private const string LevelsFolder = "Assets/Levels";

        private List<PuzzleGlassLevelData> _allLevels = new List<PuzzleGlassLevelData>();
        private PuzzleGlassLevelData _selectedLevel;
        private Vector2 _leftScroll;
        private Vector2 _rightScroll;
        private bool _liveSyncToScene = true;
        private int _batchLevelCount = 10;
        private int _batchBallsPerBox = 3;
        private int _customGenColorCount = 3;
        private int _customGenBoxCount = 9;

        private static readonly string[] ShapeNames = new string[]
        {
            "🔵 Bombeli Flask",
            "🧪 Klasik Şişe",
            "⏳ Kum Saati",
            "💙 Kalp Şişe",
            "💎 Elmas Karaf",
            "📐 Konik Şişe",
            "⛄ Çift Boğum",
            "🏺 Amfora Vazo",
            "🐍 S-Kıvrım Tüp",
            "🔲 Serbest Özel",
            "⬛ Hunili Kare",
            "📱 Dikey Dikdörtgen",
            "🖥️ Geniş Dikdörtgen",
            "📦 Düz Kare Kutu",
            "🏷️ Düz Dikdörtgen"
        };

        private static readonly string[] ThemeNames = new string[]
        {
            "💧 Gök Mavisi",
            "💎 Şeffaf Kristal",
            "🟢 Zümrüt Yeşili",
            "🌸 Pembe Kuvars",
            "🟣 Mor Ametist",
            "🟠 Kehribar Sarısı"
        };

        private static readonly string[] PatternNames = new string[]
        {
            "🔻 Huni V-Katmanları (Video)",
            "🌊 Yatay Dalgalı Katmanlar",
            "🌗 Sol-Sağ Bölünmüş Blok",
            "📐 Çapraz Renk Kuşakları"
        };

        [MenuItem("Tools/PuzzleGlass/Eski/Level Design Studio (Eski Bölüm Aracı)", false, 100)]
        public static void OpenWindow()
        {
            var win = GetWindow<LevelDesignerWindow>("Level Design Studio");
            win.minSize = new Vector2(820f, 640f);
            win.Show();
        }

        private void OnEnable()
        {
            RefreshLevelList();
            if (_allLevels.Count == 0)
            {
                GenerateStarterLevels();
            }
            if (_selectedLevel == null && _allLevels.Count > 0)
            {
                _selectedLevel = _allLevels[0];
            }
        }

        private void RefreshLevelList()
        {
            _allLevels.Clear();
            if (!Directory.Exists(LevelsFolder))
            {
                Directory.CreateDirectory(LevelsFolder);
                AssetDatabase.Refresh();
            }

            string[] guids = AssetDatabase.FindAssets("t:PuzzleGlassLevelData", new[] { LevelsFolder });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<PuzzleGlassLevelData>(path);
                if (asset != null)
                {
                    _allLevels.Add(asset);
                }
            }

            _allLevels.Sort((a, b) => a.levelNumber.CompareTo(b.levelNumber));
            SyncLevelsWithGameManager();
        }

        private void SyncLevelsWithGameManager()
        {
            var manager = FindFirstObjectByType<PuzzleGlassGameManager>();
            if (manager != null)
            {
                manager.levels = new List<PuzzleGlassLevelData>(_allLevels);
                EditorUtility.SetDirty(manager);
            }
        }

        private void OnGUI()
        {
            DrawTopHeader();

            EditorGUILayout.BeginHorizontal();

            // Left Panel: Level List Sidebar (245px)
            DrawLeftLevelListSidebar();

            // Right Panel: Selected Level Visual Designer
            DrawRightLevelInspector();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawTopHeader()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();

            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 15,
                alignment = TextAnchor.MiddleLeft
            };
            EditorGUILayout.LabelField("🎮 PuzzleGlass - Level Design Studio (Bölüm Tasarım Aracı)", titleStyle, GUILayout.Height(26));

            _liveSyncToScene = GUILayout.Toggle(
                _liveSyncToScene,
                "⚡ Anlık Sahne Önizleme (Live Sync)",
                "Button",
                GUILayout.Width(210),
                GUILayout.Height(24));

            if (GUILayout.Button("✨ Cam Şekli Stüdyosu", GUILayout.Width(150), GUILayout.Height(24)))
            {
                GlassShapeDesignerWindow.OpenWindow();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawLeftLevelListSidebar()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(250), GUILayout.ExpandHeight(true));

            EditorGUILayout.LabelField($"📚 Bölüm Listesi ({_allLevels.Count} Level)", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.28f, 0.85f, 0.50f);
            if (GUILayout.Button("➕ Yeni Level", GUILayout.Height(28)))
            {
                CreateNewLevelAsset();
            }

            GUI.backgroundColor = new Color(0.35f, 0.75f, 0.98f);
            if (GUILayout.Button("📋 Kopyala", GUILayout.Height(28)))
            {
                DuplicateSelectedLevel();
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            _batchLevelCount = EditorGUILayout.IntSlider("Paket Boyutu", _batchLevelCount, 5, 30);
            _batchBallsPerBox = EditorGUILayout.IntSlider("Kutu Boşluk Sayısı", _batchBallsPerBox, 1, MonsterCharacter.MaxVisualSockets);
            EditorGUILayout.EndVertical();

            GUI.backgroundColor = new Color(0.98f, 0.76f, 0.24f);
            if (GUILayout.Button($"🎲 {_batchLevelCount} Levellık İlerlemeli Paket Üret ({_batchBallsPerBox} Boşluklu)", GUILayout.Height(28))
                && EditorUtility.DisplayDialog(
                    "İlerlemeli Level Paketi Üret",
                    $"Level_01 - Level_{_batchLevelCount:00} arası bölümler Kolay -> Orta -> Zor -> Boss zorluk eğrisine göre {_batchBallsPerBox} yuvalı kutu sistemiyle otomatik üretilecek. Devam edilsin mi?",
                    "Üret ve Kaydet",
                    "Vazgeç"))
            {
                GenerateProgressiveLevelPack(_batchLevelCount);
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space(6);

            _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll);
            for (int i = 0; i < _allLevels.Count; i++)
            {
                var lvl = _allLevels[i];
                if (lvl == null) continue;

                bool isSelected = (_selectedLevel == lvl);
                GUI.backgroundColor = isSelected ? new Color(0.25f, 0.78f, 1.0f) : Color.white;

                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                if (GUILayout.Button($"#{lvl.levelNumber}  {lvl.levelName}", EditorStyles.label, GUILayout.ExpandWidth(true), GUILayout.Height(26)))
                {
                    _selectedLevel = lvl;
                    if (_liveSyncToScene) ApplyLevelToScene(lvl);
                }

                GUI.backgroundColor = new Color(0.30f, 0.88f, 0.52f);
                if (GUILayout.Button("▶", GUILayout.Width(26), GUILayout.Height(24)))
                {
                    _selectedLevel = lvl;
                    ApplyLevelToScene(lvl);
                }

                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
        }

        private void DrawRightLevelInspector()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            if (_selectedLevel == null)
            {
                EditorGUILayout.HelpBox("Sol taraftaki listeden bir level seçin veya '➕ Yeni Level' butonuna tıklayın.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);

            EditorGUI.BeginChangeCheck();

            // 1. Live Level Visual Card (Bottle Layers + Slots + Monsters)
            DrawLevelVisualSummaryCard(_selectedLevel);

            EditorGUILayout.Space(6);

            // 2. Level Metadata & Slot Count
            DrawMetadataAndSlotsSection(_selectedLevel);

            EditorGUILayout.Space(6);

            // 3. Glass Shape, Theme & Layer Pattern
            DrawGlassShapeAndThemeSection(_selectedLevel);

            EditorGUILayout.Space(6);

            // 4. Monster & Bottle Color Layer Builder
            DrawMonstersAndLayersSection(_selectedLevel);

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(_selectedLevel);
                if (_liveSyncToScene)
                {
                    ApplyLevelToScene(_selectedLevel);
                }
            }

            EditorGUILayout.Space(10);

            // Bottom Action Bar
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUI.backgroundColor = new Color(0.25f, 0.82f, 0.52f);
            if (GUILayout.Button("🚀 Bu Levelı Sahneye Yükle & Güncelle", GUILayout.Height(36)))
            {
                ApplyLevelToScene(_selectedLevel);
            }

            GUI.backgroundColor = new Color(0.28f, 0.68f, 0.98f);
            if (GUILayout.Button("💾 Tüm Level Assetlerini Diske Kaydet", GUILayout.Height(36)))
            {
                EditorUtility.SetDirty(_selectedLevel);
                AssetDatabase.SaveAssets();
                Debug.Log($"<color=#2ED170><b>[Level Design Studio]</b></color> '{_selectedLevel.levelName}' başarıyla kaydedildi!");
            }

            GUI.backgroundColor = new Color(0.95f, 0.38f, 0.38f);
            if (_allLevels.Count > 1 && GUILayout.Button("🗑️ Sil", GUILayout.Width(65), GUILayout.Height(36)))
            {
                DeleteSelectedLevel();
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();
                return;
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawLevelVisualSummaryCard(PuzzleGlassLevelData lvl)
        {
            lvl.EnsureCarrierBoxPuzzleFormat();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            int totalBoxes = (lvl.monsters != null) ? lvl.monsters.Count : 0;
            int totalBalls = lvl.GetTotalBallCount();

            bool mixedCounts = false;
            int firstCount = (totalBoxes > 0 && lvl.monsters[0] != null) ? lvl.monsters[0].count : lvl.ballsPerBox;
            for (int i = 1; i < totalBoxes; i++)
            {
                if (lvl.monsters[i] != null && lvl.monsters[i].count != firstCount)
                {
                    mixedCounts = true;
                    break;
                }
            }

            List<LevelMonsterEntry> groupsForSim = (lvl.bottomToTopGroups != null && lvl.bottomToTopGroups.Count > 0)
                ? lvl.bottomToTopGroups
                : new List<LevelMonsterEntry>();

            bool isSolvable = PuzzleLevelGenerator.SimulateCanSolve(
                groupsForSim,
                lvl.monsters,
                lvl.stagingColumns,
                lvl.slotCount,
                lvl.useStagingColumnQueue,
                lvl.slotCount - 1);

            string diffName = PuzzleLevelGenerator.GetDifficultyTurkishLabel(lvl.difficulty);
            string solveTag = isSolvable ? "✅ %100 Çözülebilir" : "⚠️ Dikkat: Slot Sayısı Yetersiz Olabilir";
            string socketInfo = mixedCounts ? "Karma Boşluklu" : $"Her Kutu {firstCount} Boşluklu";

            EditorGUILayout.LabelField(
                $"📊 Bölüm Özeti [{diffName.ToUpperInvariant()}]:  {totalBoxes} Kutu ({socketInfo})  |  {totalBalls} Top  |  {lvl.slotCount} Kilitli Slot  |  {solveTag}",
                EditorStyles.boldLabel);

            Rect previewRect = GUILayoutUtility.GetRect(480f, 136f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(previewRect, new Color(0.12f, 0.15f, 0.22f, 1f));

            // Left side of preview card: Glass Container Bottom-to-Top 3 Lanes
            Rect bottleRect = new Rect(previewRect.x + 14f, previewRect.y + 10f, 175f, previewRect.height - 20f);
            EditorGUI.DrawRect(bottleRect, new Color(0.08f, 0.10f, 0.15f, 1f));

            if (groupsForSim.Count > 0)
            {
                var lanes = PuzzleLevelGenerator.DistributeGroupsIntoThreeLanes(groupsForSim);
                int maxLaneCount = Mathf.Max(1, Mathf.Max(lanes[0].Count, Mathf.Max(lanes[1].Count, lanes[2].Count)));
                float availH = bottleRect.height - 10f;
                float rowH = Mathf.Min(24f, availH / maxLaneCount);
                float innerW = bottleRect.width - 12f;
                float cellW = innerW / 3f;
                GUIStyle miniCountStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 10,
                    normal = { textColor = Color.white }
                };

                for (int l = 0; l < 3; l++)
                {
                    float curY = bottleRect.yMax - 5f;
                    for (int gIdx = 0; gIdx < lanes[l].Count; gIdx++)
                    {
                        var g = lanes[l][gIdx];
                        Rect tRect = new Rect(bottleRect.x + 6f + l * cellW, curY - rowH, cellW - 2f, rowH - 2f);
                        EditorGUI.DrawRect(tRect, PuzzleColorPalette.GetBodyColor(g.color));

                        if (g.count <= 4 && rowH >= 13f)
                        {
                            for (int b = 0; b < g.count; b++)
                            {
                                float bx = tRect.x + (b + 0.5f) * (tRect.width / g.count) - 2f;
                                float by = tRect.center.y - 2f;
                                EditorGUI.DrawRect(new Rect(bx, by, 4f, 4f), Color.white);
                            }
                        }
                        else if (rowH >= 11f)
                        {
                            GUI.Label(tRect, g.count.ToString(), miniCountStyle);
                        }
                        curY -= rowH;
                    }
                }
            }

            // Right side of preview card: Slots + Staging Columns of Variable-Socket Carrier Boxes
            float rightStartX = bottleRect.xMax + 24f;
            float rightWidth = previewRect.xMax - rightStartX - 14f;

            // Draw Slots Row
            float slotBoxW = 34f;
            float slotGap = 6f;
            float slotsTotalW = lvl.slotCount * slotBoxW + (lvl.slotCount - 1) * slotGap;
            float slotStartX = rightStartX + (rightWidth - slotsTotalW) * 0.5f;

            for (int s = 0; s < lvl.slotCount; s++)
            {
                Rect sRect = new Rect(slotStartX + s * (slotBoxW + slotGap), previewRect.y + 8f, slotBoxW, 24f);
                EditorGUI.DrawRect(sRect, new Color(0.75f, 0.86f, 0.98f, 1f));
                Rect sIn = new Rect(sRect.x + 3f, sRect.y + 3f, sRect.width - 6f, sRect.height - 6f);
                EditorGUI.DrawRect(sIn, new Color(0.38f, 0.58f, 0.84f, 1f));
            }

            // Draw Carrier Box Staging Grid (stagingColumns x rows)
            if (lvl.monsters != null && lvl.monsters.Count > 0)
            {
                int mCount = lvl.monsters.Count;
                int cols = Mathf.Clamp(lvl.stagingColumns, 2, 4);
                int rows = Mathf.CeilToInt(mCount / (float)cols);
                float boxW = Mathf.Min(46f, (rightWidth - (cols - 1) * 8f) / cols);
                float boxH = Mathf.Min(28f, (previewRect.height - 44f) / Mathf.Max(1, rows) - 3f);
                float gridW = cols * boxW + (cols - 1) * 8f;
                float gridStartX = rightStartX + (rightWidth - gridW) * 0.5f;
                float gridStartY = previewRect.y + 38f;
                Color sockGray = new Color(0.56f, 0.57f, 0.59f, 1f);

                for (int i = 0; i < mCount; i++)
                {
                    int c = i % cols;
                    int r = i / cols;
                    var m = lvl.monsters[i];

                    Rect mRect = new Rect(gridStartX + c * (boxW + 8f), gridStartY + r * (boxH + 3f), boxW, boxH);
                    EditorGUI.DrawRect(mRect, PuzzleColorPalette.GetBodyColor(m.color));

                    Rect plateRect = new Rect(mRect.x + 3f, mRect.y + 3f, mRect.width - 6f, mRect.height - 6f);
                    EditorGUI.DrawRect(plateRect, new Color(0.96f, 0.97f, 0.99f, 1f));

                    // Render exact socket layout (1..16) inside the preview box plate
                    MonsterCharacter.ComputeSocketLayout(m.count, out Vector2[] sockPts, out float sockRad, out _);
                    float dotSize = Mathf.Clamp(sockRad * plateRect.height * 1.45f, 2f, 6f);
                    for (int k = 0; k < sockPts.Length; k++)
                    {
                        float nx = sockPts[k].x / 1.11f;
                        float ny = -(sockPts[k].y + 0.04f) / 0.85f;
                        float px = plateRect.center.x + nx * plateRect.width - dotSize * 0.5f;
                        float py = plateRect.center.y + ny * plateRect.height - dotSize * 0.5f;
                        EditorGUI.DrawRect(new Rect(px, py, dotSize, dotSize), sockGray);
                    }
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawMetadataAndSlotsSection(PuzzleGlassLevelData lvl)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("⚙️ Bölüm Zorluğu, Kilitli Slotlar & Otomatik Level Üretici", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            lvl.levelNumber = EditorGUILayout.IntField("Level Numarası", lvl.levelNumber, GUILayout.Width(220));
            lvl.difficulty = (LevelDifficulty)EditorGUILayout.EnumPopup("Zorluk Derecesi", lvl.difficulty);
            EditorGUILayout.EndHorizontal();

            lvl.levelName = EditorGUILayout.TextField("Bölüm Adı", lvl.levelName);

            EditorGUILayout.BeginHorizontal();
            lvl.slotCount = EditorGUILayout.IntSlider("Üst Slot Sayısı (2 - 5)", lvl.slotCount, 2, 5);
            lvl.stagingGridCols = EditorGUILayout.IntSlider("Izgara Genişliği (3 - 6)", Mathf.Clamp(lvl.stagingGridCols, 3, 6), 3, 6);
            lvl.stagingGridRows = EditorGUILayout.IntSlider("Izgara Yüksekliği (3 - 5)", Mathf.Clamp(lvl.stagingGridRows, 3, 5), 3, 5);
            EditorGUILayout.EndHorizontal();

            // Kutu İçi Yuva (Boşluk) Kapasitesi Ayarı (1 - 16)
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            int newBallsPerBox = EditorGUILayout.IntSlider("Kutu Boşluk Sayısı (1 - 16)", lvl.ballsPerBox, 1, MonsterCharacter.MaxVisualSockets);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(lvl, "Change Box Socket Count");
                lvl.SetAllBoxesSocketCount(newBallsPerBox);
                EditorUtility.SetDirty(lvl);
                if (_liveSyncToScene) ApplyLevelToScene(lvl);
            }
            if (GUILayout.Button("Tüm Kutulara Uygula", GUILayout.Width(145)))
            {
                Undo.RecordObject(lvl, "Apply Socket Count To All Boxes");
                lvl.SetAllBoxesSocketCount(lvl.ballsPerBox);
                EditorUtility.SetDirty(lvl);
                if (_liveSyncToScene) ApplyLevelToScene(lvl);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Hızlı Yuva Seç:", GUILayout.Width(100));
            int[] quickSockets = { 3, 4, 5, 6, 7, 8, 9, 12, 16 };
            for (int q = 0; q < quickSockets.Length; q++)
            {
                int qVal = quickSockets[q];
                bool isCurrent = (lvl.ballsPerBox == qVal);
                GUI.backgroundColor = isCurrent ? new Color(0.28f, 0.86f, 0.56f) : Color.white;
                if (GUILayout.Button(qVal.ToString(), GUILayout.Width(32)))
                {
                    Undo.RecordObject(lvl, $"Set All Boxes {qVal} Sockets");
                    lvl.SetAllBoxesSocketCount(qVal);
                    EditorUtility.SetDirty(lvl);
                    if (_liveSyncToScene) ApplyLevelToScene(lvl);
                }
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginHorizontal();
            lvl.puzzleLayerStyle = (PuzzleLayerStyle)EditorGUILayout.EnumPopup("Cam Katman Dizilimi", lvl.puzzleLayerStyle);
            lvl.useStagingColumnQueue = EditorGUILayout.ToggleLeft("Sütun Kuyruk Kilidi (Önce En Üst Kutu Seçilir)", lvl.useStagingColumnQueue);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("🎲 Tek Tıkla Bu Levelı Otomatik Üret (%100 Çözülebilir):", EditorStyles.miniBoldLabel);

            EditorGUILayout.BeginHorizontal();
            _customGenColorCount = EditorGUILayout.IntSlider("Renk Sayısı (2 - 6)", _customGenColorCount, 2, 6);
            int rowsCount = EditorGUILayout.IntSlider("Satır Sayısı (Her Satır 3 Kutu)", Mathf.Clamp((lvl.monsters != null ? lvl.monsters.Count : 9) / 3, 2, 5), 2, 5);
            _customGenBoxCount = rowsCount * 3;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.24f, 0.85f, 0.48f);
            if (GUILayout.Button("🟢 Kolay Üret", GUILayout.Height(28)))
            {
                Undo.RecordObject(lvl, "Generate Easy Level");
                lvl.difficulty = LevelDifficulty.Easy;
                PuzzleLevelGenerator.GenerateForLevelData(lvl, customBallsPerBox: lvl.ballsPerBox);
                EditorUtility.SetDirty(lvl);
                if (_liveSyncToScene) ApplyLevelToScene(lvl);
            }

            GUI.backgroundColor = new Color(0.28f, 0.72f, 0.98f);
            if (GUILayout.Button("🔵 Orta Üret", GUILayout.Height(28)))
            {
                Undo.RecordObject(lvl, "Generate Medium Level");
                lvl.difficulty = LevelDifficulty.Medium;
                PuzzleLevelGenerator.GenerateForLevelData(lvl, customBallsPerBox: lvl.ballsPerBox);
                EditorUtility.SetDirty(lvl);
                if (_liveSyncToScene) ApplyLevelToScene(lvl);
            }

            GUI.backgroundColor = new Color(0.98f, 0.65f, 0.22f);
            if (GUILayout.Button("🟠 Zor Üret", GUILayout.Height(28)))
            {
                Undo.RecordObject(lvl, "Generate Hard Level");
                lvl.difficulty = LevelDifficulty.Hard;
                PuzzleLevelGenerator.GenerateForLevelData(lvl, customBallsPerBox: lvl.ballsPerBox);
                EditorUtility.SetDirty(lvl);
                if (_liveSyncToScene) ApplyLevelToScene(lvl);
            }

            GUI.backgroundColor = new Color(0.95f, 0.35f, 0.45f);
            if (GUILayout.Button("🔴 Boss Üret", GUILayout.Height(28)))
            {
                Undo.RecordObject(lvl, "Generate Boss Level");
                lvl.difficulty = LevelDifficulty.Boss;
                PuzzleLevelGenerator.GenerateForLevelData(lvl, customBallsPerBox: lvl.ballsPerBox);
                EditorUtility.SetDirty(lvl);
                if (_liveSyncToScene) ApplyLevelToScene(lvl);
            }

            GUI.backgroundColor = new Color(0.75f, 0.48f, 1.0f);
            if (GUILayout.Button($"✨ Özel ({_customGenColorCount} Renk, {_customGenBoxCount} Kutu) Üret", GUILayout.Height(28)))
            {
                Undo.RecordObject(lvl, "Generate Custom Parameter Level");
                PuzzleLevelGenerator.GenerateForLevelData(
                    lvl,
                    customColorCount: _customGenColorCount,
                    customTotalBoxes: _customGenBoxCount,
                    customSlotCount: lvl.slotCount,
                    customStagingColumns: lvl.stagingColumns,
                    customLayerStyle: lvl.puzzleLayerStyle,
                    customBallsPerBox: lvl.ballsPerBox);
                EditorUtility.SetDirty(lvl);
                if (_liveSyncToScene) ApplyLevelToScene(lvl);
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndVertical();
        }

        private void DrawGlassShapeAndThemeSection(PuzzleGlassLevelData lvl)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("🍾 Cam Şekli, Renk Teması & Katman Deseni", EditorStyles.boldLabel);

            if (GUILayout.Button("📥 Sahnedeki Özel Cam Şeklini Bu Levela Kaydet", GUILayout.Width(290)))
            {
                var sceneGlass = FindFirstObjectByType<ShapedGlassContainer>();
                if (sceneGlass != null)
                {
                    Undo.RecordObject(lvl, "Copy Glass Shape From Scene");
                    lvl.glassPreset = sceneGlass.shapePreset;
                    lvl.symmetricMode = sceneGlass.symmetricMode;
                    lvl.curveSubdivisions = sceneGlass.curveSubdivisions;
                    lvl.widthScale = sceneGlass.widthScale;
                    lvl.heightScale = sceneGlass.heightScale;
                    lvl.contourPoints = new List<GlassContourPoint>(sceneGlass.contourPoints);
                    EditorUtility.SetDirty(lvl);
                }
            }
            EditorGUILayout.EndHorizontal();

            // 9 Glass Shape Buttons
            int cols = 3;
            for (int i = 0; i < ShapeNames.Length; i += cols)
            {
                EditorGUILayout.BeginHorizontal();
                for (int j = 0; j < cols && (i + j) < ShapeNames.Length; j++)
                {
                    int idx = i + j;
                    GlassShapePreset preset = (GlassShapePreset)idx;
                    GUI.backgroundColor = (lvl.glassPreset == preset)
                        ? new Color(0.28f, 0.88f, 0.55f)
                        : new Color(0.35f, 0.72f, 0.98f);
                    if (GUILayout.Button(ShapeNames[idx], GUILayout.Height(25)))
                    {
                        Undo.RecordObject(lvl, "Set Level Glass Shape");
                        lvl.glassPreset = preset;
                        lvl.contourPoints.Clear(); // Will auto-generate from preset
                        EditorUtility.SetDirty(lvl);
                        if (_liveSyncToScene) ApplyLevelToScene(lvl);
                    }
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(4);

            // 6 Glass Color Theme Buttons
            for (int i = 0; i < ThemeNames.Length; i += cols)
            {
                EditorGUILayout.BeginHorizontal();
                for (int j = 0; j < cols && (i + j) < ThemeNames.Length; j++)
                {
                    int idx = i + j;
                    GlassColorTheme theme = (GlassColorTheme)idx;
                    GUI.backgroundColor = (lvl.colorTheme == theme) ? new Color(0.35f, 0.90f, 0.58f) : Color.white;
                    if (GUILayout.Button(ThemeNames[idx], GUILayout.Height(24)))
                    {
                        Undo.RecordObject(lvl, "Set Level Color Theme");
                        ApplyThemeColorsToLevelData(lvl, theme);
                        EditorUtility.SetDirty(lvl);
                        if (_liveSyncToScene) ApplyLevelToScene(lvl);
                    }
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(4);

            // 4 Bottle Layer Pattern Buttons
            EditorGUILayout.LabelField("Şişe İçi Renk Katmanı Dizilim Deseni:", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < PatternNames.Length; i++)
            {
                BottleLayerPattern pat = (BottleLayerPattern)i;
                GUI.backgroundColor = (lvl.layerPattern == pat) ? new Color(0.98f, 0.80f, 0.25f) : Color.white;
                if (GUILayout.Button(PatternNames[i], GUILayout.Height(25)))
                {
                    Undo.RecordObject(lvl, "Set Bottle Layer Pattern");
                    lvl.layerPattern = pat;
                    EditorUtility.SetDirty(lvl);
                    if (_liveSyncToScene) ApplyLevelToScene(lvl);
                }
            }
            EditorGUILayout.EndHorizontal();
            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("🎨 2. Görseldeki Piksel Sanatı Görselleri (Image 2 Presets):", EditorStyles.miniBoldLabel);
            EditorGUILayout.HelpBox("Camın içinde 2. görseldeki gibi sabit durarak piksel sanatı görseli oluşturan toplar.", MessageType.None);

            string[] pixelLabels = PixelArtLibrary.PresetDisplayNames;
            int pCols = 3;
            for (int i = 0; i < pixelLabels.Length; i += pCols)
            {
                EditorGUILayout.BeginHorizontal();
                for (int j = 0; j < pCols && (i + j) < pixelLabels.Length; j++)
                {
                    int pIdx = i + j;
                    PixelArtPreset pPreset = (PixelArtPreset)pIdx;
                    bool isSelected = lvl.particleMode == ParticleArrangementMode.PixelArtVisual && lvl.pixelArtPreset == pPreset;
                    GUI.backgroundColor = isSelected ? new Color(0.28f, 0.90f, 0.55f) : new Color(0.95f, 0.85f, 0.45f);
                    if (GUILayout.Button(pixelLabels[pIdx], GUILayout.Height(25)))
                    {
                        Undo.RecordObject(lvl, "Set Pixel Art Preset");
                        lvl.particleMode = ParticleArrangementMode.PixelArtVisual;
                        lvl.pixelArtPreset = pPreset;
                        lvl.freezePatternInPlace = true;
                        EditorUtility.SetDirty(lvl);
                        if (_liveSyncToScene) ApplyLevelToScene(lvl);
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndVertical();
        }

        private void DrawMonstersAndLayersSection(PuzzleGlassLevelData lvl)
        {
            lvl.EnsureCarrierBoxPuzzleFormat();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            int cols = Mathf.Clamp(lvl.stagingColumns, 2, 4);
            int defSockets = Mathf.Clamp(lvl.ballsPerBox, 1, MonsterCharacter.MaxVisualSockets);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("🧰 Değişken Yuvalı Kutular & Cam Katmanları (Alttan Üste)", EditorStyles.boldLabel);

            GUI.backgroundColor = new Color(0.24f, 0.78f, 0.92f);
            if (GUILayout.Button($"➕ 1 Kutu Ekle (+{defSockets})", GUILayout.Width(130), GUILayout.Height(24)))
            {
                Undo.RecordObject(lvl, "Add 1 Carrier Box To Level");
                PuzzleColor[] cycle = new PuzzleColor[]
                {
                    PuzzleColor.Blue, PuzzleColor.Yellow, PuzzleColor.Red,
                    PuzzleColor.Green, PuzzleColor.Purple, PuzzleColor.Orange
                };
                PuzzleColor nextCol = cycle[lvl.monsters.Count % cycle.Length];
                lvl.monsters.Add(new LevelMonsterEntry(nextCol, defSockets));
                lvl.RebuildTripletsFromBoxes();
                EditorUtility.SetDirty(lvl);
                if (_liveSyncToScene) ApplyLevelToScene(lvl);
            }

            GUI.backgroundColor = new Color(0.30f, 0.88f, 0.52f);
            if (GUILayout.Button($"➕ {cols} Kutu Ekle (+{cols * defSockets} Top)", GUILayout.Width(175), GUILayout.Height(24)))
            {
                Undo.RecordObject(lvl, "Add Row of Carrier Boxes To Level");
                PuzzleColor[] cycle = new PuzzleColor[]
                {
                    PuzzleColor.Blue, PuzzleColor.Yellow, PuzzleColor.Red,
                    PuzzleColor.Green, PuzzleColor.Purple, PuzzleColor.Orange
                };
                PuzzleColor nextCol = cycle[(lvl.monsters.Count / cols) % cycle.Length];
                for (int k = 0; k < cols; k++)
                {
                    lvl.monsters.Add(new LevelMonsterEntry(nextCol, defSockets));
                }
                lvl.RebuildTripletsFromBoxes();
                EditorUtility.SetDirty(lvl);
                if (_liveSyncToScene) ApplyLevelToScene(lvl);
            }

            GUI.backgroundColor = new Color(0.95f, 0.72f, 0.25f);
            if (GUILayout.Button("🎲 Zorluğa Göre Yeniden Diz", GUILayout.Width(175), GUILayout.Height(24)))
            {
                Undo.RecordObject(lvl, "Regenerate Level Boxes");
                PuzzleLevelGenerator.GenerateForLevelData(lvl, customBallsPerBox: lvl.ballsPerBox);
                EditorUtility.SetDirty(lvl);
                if (_liveSyncToScene) ApplyLevelToScene(lvl);
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(
                "Her kutunun içindeki gri boşluk (yuva) sayısını 1 - 16 arasında tek tek veya yukarıdan topluca ayarlayabilirsiniz.",
                EditorStyles.miniLabel);

            if (lvl.monsters == null) lvl.monsters = new List<LevelMonsterEntry>();

            for (int i = 0; i < lvl.monsters.Count; i++)
            {
                var entry = lvl.monsters[i];
                entry.count = Mathf.Clamp(entry.count, 1, MonsterCharacter.MaxVisualSockets);

                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                Rect swatchRect = GUILayoutUtility.GetRect(22f, 22f, GUILayout.Width(22f));
                EditorGUI.DrawRect(swatchRect, PuzzleColorPalette.GetBodyColor(entry.color));

                EditorGUILayout.LabelField(
                    $"Küp #{i + 1}",
                    GUILayout.Width(62));

                EditorGUI.BeginChangeCheck();
                PuzzleColor newBoxCol = (PuzzleColor)EditorGUILayout.EnumPopup(entry.color, GUILayout.Width(82));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(lvl, "Change Carrier Box Color");
                    entry.color = newBoxCol;
                    lvl.RebuildTripletsFromBoxes();
                    EditorUtility.SetDirty(lvl);
                    if (_liveSyncToScene) ApplyLevelToScene(lvl);
                }

                EditorGUI.BeginChangeCheck();
                CarrierBlockShape newShape = (CarrierBlockShape)EditorGUILayout.EnumPopup(entry.blockShape, GUILayout.Width(96));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(lvl, "Change Carrier Block Shape");
                    entry.blockShape = newShape;
                    entry.hasGridAnchor = true;
                    EditorUtility.SetDirty(lvl);
                    if (_liveSyncToScene) ApplyLevelToScene(lvl);
                }

                EditorGUILayout.LabelField("XY:", GUILayout.Width(22));
                EditorGUI.BeginChangeCheck();
                int gx = EditorGUILayout.IntField(entry.gridAnchor.x, GUILayout.Width(24));
                int gy = EditorGUILayout.IntField(entry.gridAnchor.y, GUILayout.Width(24));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(lvl, "Change Carrier Block Grid Anchor");
                    entry.gridAnchor = new Vector2Int(
                        Mathf.Clamp(gx, 0, Mathf.Max(0, lvl.stagingGridCols - 1)),
                        Mathf.Clamp(gy, 0, Mathf.Max(0, lvl.stagingGridRows - 1)));
                    entry.hasGridAnchor = true;
                    EditorUtility.SetDirty(lvl);
                    if (_liveSyncToScene) ApplyLevelToScene(lvl);
                }

                EditorGUILayout.LabelField("Yuva:", GUILayout.Width(36));
                if (GUILayout.Button("-", GUILayout.Width(22)) && entry.count > 1)
                {
                    Undo.RecordObject(lvl, "Decrease Box Socket Count");
                    entry.count--;
                    lvl.RebuildTripletsFromBoxes();
                    EditorUtility.SetDirty(lvl);
                    if (_liveSyncToScene) ApplyLevelToScene(lvl);
                }

                EditorGUI.BeginChangeCheck();
                int editedCount = EditorGUILayout.IntField(entry.count, GUILayout.Width(32));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(lvl, "Edit Box Socket Count");
                    entry.count = Mathf.Clamp(editedCount, 1, MonsterCharacter.MaxVisualSockets);
                    lvl.RebuildTripletsFromBoxes();
                    EditorUtility.SetDirty(lvl);
                    if (_liveSyncToScene) ApplyLevelToScene(lvl);
                }

                if (GUILayout.Button("+", GUILayout.Width(22)) && entry.count < MonsterCharacter.MaxVisualSockets)
                {
                    Undo.RecordObject(lvl, "Increase Box Socket Count");
                    entry.count++;
                    lvl.RebuildTripletsFromBoxes();
                    EditorUtility.SetDirty(lvl);
                    if (_liveSyncToScene) ApplyLevelToScene(lvl);
                }

                if (lvl.bottomToTopGroups != null && i < lvl.bottomToTopGroups.Count)
                {
                    int glassRow = (i / 3) + 1;
                    string posTag = (i % 3 == 0) ? "Sol" : ((i % 3 == 1) ? "Orta" : "Sağ");
                    var grp = lvl.bottomToTopGroups[i];
                    EditorGUILayout.LabelField($"Cam {glassRow} ({posTag}): {grp.color}x{grp.count}", GUILayout.Width(145));
                }

                GUI.enabled = i > 0;
                if (GUILayout.Button("▲", GUILayout.Width(24)))
                {
                    Undo.RecordObject(lvl, "Move Box Up");
                    var tmp = lvl.monsters[i - 1];
                    lvl.monsters[i - 1] = lvl.monsters[i];
                    lvl.monsters[i] = tmp;
                    EditorUtility.SetDirty(lvl);
                    if (_liveSyncToScene) ApplyLevelToScene(lvl);
                    GUI.enabled = true;
                    EditorGUILayout.EndHorizontal();
                    break;
                }

                GUI.enabled = i < lvl.monsters.Count - 1;
                if (GUILayout.Button("▼", GUILayout.Width(24)))
                {
                    Undo.RecordObject(lvl, "Move Box Down");
                    var tmp = lvl.monsters[i + 1];
                    lvl.monsters[i + 1] = lvl.monsters[i];
                    lvl.monsters[i] = tmp;
                    EditorUtility.SetDirty(lvl);
                    if (_liveSyncToScene) ApplyLevelToScene(lvl);
                    GUI.enabled = true;
                    EditorGUILayout.EndHorizontal();
                    break;
                }

                GUI.enabled = lvl.monsters.Count > 1;
                GUI.backgroundColor = new Color(0.95f, 0.42f, 0.42f);
                if (GUILayout.Button("X", GUILayout.Width(24)))
                {
                    Undo.RecordObject(lvl, "Remove Box");
                    lvl.monsters.RemoveAt(i);
                    lvl.RebuildTripletsFromBoxes();
                    EditorUtility.SetDirty(lvl);
                    if (_liveSyncToScene) ApplyLevelToScene(lvl);
                    GUI.backgroundColor = Color.white;
                    GUI.enabled = true;
                    EditorGUILayout.EndHorizontal();
                    break;
                }
                GUI.backgroundColor = Color.white;
                GUI.enabled = true;

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        private void ApplyLevelToScene(PuzzleGlassLevelData lvl)
        {
            if (lvl == null) return;

            var manager = FindFirstObjectByType<PuzzleGlassGameManager>();
            if (manager == null)
            {
                PuzzleGlassSceneSetup.BuildCompleteScene();
                manager = FindFirstObjectByType<PuzzleGlassGameManager>();
            }

            if (manager != null)
            {
                SyncLevelsWithGameManager();
                manager.LoadLevel(lvl);
                if (!Application.isPlaying)
                {
                    EditorUtility.SetDirty(manager);
                    EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                }
                SceneView.RepaintAll();
            }
        }

        private void CreateNewLevelAsset()
        {
            if (!Directory.Exists(LevelsFolder)) Directory.CreateDirectory(LevelsFolder);

            int nextNum = _allLevels.Count + 1;
            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{LevelsFolder}/Level_{nextNum:00}.asset");

            PuzzleGlassLevelData newLvl = CreateInstance<PuzzleGlassLevelData>();
            newLvl.levelNumber = nextNum;
            newLvl.difficulty = PuzzleLevelGenerator.GetProgressiveDifficulty(nextNum);
            PuzzleLevelGenerator.GenerateForLevelData(newLvl, randomSeed: nextNum * 1337);

            AssetDatabase.CreateAsset(newLvl, assetPath);
            AssetDatabase.SaveAssets();
            RefreshLevelList();
            _selectedLevel = newLvl;
            if (_liveSyncToScene) ApplyLevelToScene(newLvl);
        }

        private void DuplicateSelectedLevel()
        {
            if (_selectedLevel == null) return;
            int nextNum = _allLevels.Count + 1;
            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{LevelsFolder}/Level_{nextNum:00}.asset");

            PuzzleGlassLevelData copy = Instantiate(_selectedLevel);
            copy.levelNumber = nextNum;
            copy.levelName = $"{_selectedLevel.levelName} (Kopya)";

            AssetDatabase.CreateAsset(copy, assetPath);
            AssetDatabase.SaveAssets();
            RefreshLevelList();
            _selectedLevel = copy;
            if (_liveSyncToScene) ApplyLevelToScene(copy);
        }

        private void DeleteSelectedLevel()
        {
            if (_selectedLevel == null) return;

            if (!EditorUtility.DisplayDialog(
                    "Level Sil",
                    $"'{_selectedLevel.levelName}' çöp kutusuna taşınacak. Devam edilsin mi?",
                    "Sil",
                    "Vazgeç"))
            {
                return;
            }

            string path = AssetDatabase.GetAssetPath(_selectedLevel);
            if (!string.IsNullOrEmpty(path))
            {
                AssetDatabase.MoveAssetToTrash(path);
                AssetDatabase.SaveAssets();
            }
            RefreshLevelList();
            _selectedLevel = _allLevels.Count > 0 ? _allLevels[0] : null;
            if (_selectedLevel != null && _liveSyncToScene) ApplyLevelToScene(_selectedLevel);
        }

        private void RandomizeMonstersForLevel(PuzzleGlassLevelData lvl)
        {
            PuzzleColor[] pool = new PuzzleColor[]
            {
                PuzzleColor.Blue, PuzzleColor.Yellow, PuzzleColor.Red,
                PuzzleColor.Green, PuzzleColor.Purple, PuzzleColor.Orange
            };
            int count = UnityEngine.Random.Range(2, 5);
            lvl.monsters.Clear();
            int startIdx = UnityEngine.Random.Range(0, pool.Length);
            for (int i = 0; i < count; i++)
            {
                PuzzleColor c = pool[(startIdx + i) % pool.Length];
                int quota = UnityEngine.Random.Range(2, 5) * 5; // 10, 15, or 20
                lvl.monsters.Add(new LevelMonsterEntry(c, quota));
            }
        }

        public static void ApplyThemeColorsToLevelData(PuzzleGlassLevelData lvl, GlassColorTheme theme)
        {
            lvl.colorTheme = theme;
            switch (theme)
            {
                case GlassColorTheme.SkyBlueGlossy:
                default:
                    lvl.glassBodyColor = new Color(0.35f, 0.70f, 0.98f, 0.76f);
                    lvl.glassCenterGlowColor = new Color(0.58f, 0.84f, 1.00f, 0.42f);
                    lvl.glassOuterRimColor = new Color(0.36f, 0.69f, 0.97f, 1.00f);
                    lvl.glassInnerRimColor = new Color(0.74f, 0.91f, 1.00f, 0.88f);
                    lvl.glassFrontLensColor = new Color(0.85f, 0.96f, 1.00f, 0.12f);
                    lvl.earHandleColor = new Color(0.92f, 0.96f, 1.00f, 1.00f);
                    break;
                case GlassColorTheme.CrystalClear:
                    lvl.glassBodyColor = new Color(0.82f, 0.93f, 0.99f, 0.38f);
                    lvl.glassCenterGlowColor = new Color(0.94f, 0.98f, 1.00f, 0.25f);
                    lvl.glassOuterRimColor = new Color(0.76f, 0.89f, 0.98f, 1.00f);
                    lvl.glassInnerRimColor = new Color(0.95f, 0.99f, 1.00f, 0.92f);
                    lvl.glassFrontLensColor = new Color(0.95f, 0.99f, 1.00f, 0.16f);
                    lvl.earHandleColor = new Color(0.96f, 0.98f, 1.00f, 1.00f);
                    break;
                case GlassColorTheme.EmeraldGreen:
                    lvl.glassBodyColor = new Color(0.22f, 0.82f, 0.64f, 0.72f);
                    lvl.glassCenterGlowColor = new Color(0.52f, 0.95f, 0.80f, 0.40f);
                    lvl.glassOuterRimColor = new Color(0.20f, 0.76f, 0.58f, 1.00f);
                    lvl.glassInnerRimColor = new Color(0.72f, 0.98f, 0.88f, 0.88f);
                    lvl.glassFrontLensColor = new Color(0.82f, 1.00f, 0.92f, 0.12f);
                    lvl.earHandleColor = new Color(0.90f, 0.99f, 0.95f, 1.00f);
                    break;
                case GlassColorTheme.RoseQuartz:
                    lvl.glassBodyColor = new Color(0.96f, 0.52f, 0.76f, 0.72f);
                    lvl.glassCenterGlowColor = new Color(1.00f, 0.74f, 0.88f, 0.40f);
                    lvl.glassOuterRimColor = new Color(0.94f, 0.45f, 0.72f, 1.00f);
                    lvl.glassInnerRimColor = new Color(1.00f, 0.84f, 0.93f, 0.88f);
                    lvl.glassFrontLensColor = new Color(1.00f, 0.90f, 0.96f, 0.12f);
                    lvl.earHandleColor = new Color(1.00f, 0.94f, 0.97f, 1.00f);
                    break;
                case GlassColorTheme.AmethystPurple:
                    lvl.glassBodyColor = new Color(0.64f, 0.46f, 0.96f, 0.74f);
                    lvl.glassCenterGlowColor = new Color(0.80f, 0.68f, 1.00f, 0.40f);
                    lvl.glassOuterRimColor = new Color(0.58f, 0.40f, 0.92f, 1.00f);
                    lvl.glassInnerRimColor = new Color(0.86f, 0.78f, 1.00f, 0.88f);
                    lvl.glassFrontLensColor = new Color(0.92f, 0.86f, 1.00f, 0.12f);
                    lvl.earHandleColor = new Color(0.95f, 0.92f, 1.00f, 1.00f);
                    break;
                case GlassColorTheme.AmberGold:
                    lvl.glassBodyColor = new Color(0.98f, 0.74f, 0.28f, 0.72f);
                    lvl.glassCenterGlowColor = new Color(1.00f, 0.88f, 0.55f, 0.40f);
                    lvl.glassOuterRimColor = new Color(0.94f, 0.66f, 0.20f, 1.00f);
                    lvl.glassInnerRimColor = new Color(1.00f, 0.92f, 0.72f, 0.88f);
                    lvl.glassFrontLensColor = new Color(1.00f, 0.96f, 0.84f, 0.12f);
                    lvl.earHandleColor = new Color(1.00f, 0.97f, 0.90f, 1.00f);
                    break;
            }
        }

        private void GenerateStarterLevels()
        {
            GenerateProgressiveLevelPack(10);
        }

        public void GenerateProgressiveLevelPack(int totalCount)
        {
            if (!Directory.Exists(LevelsFolder)) Directory.CreateDirectory(LevelsFolder);

            int count = Mathf.Clamp(totalCount, 1, 50);
            for (int i = 1; i <= count; i++)
            {
                string path = $"{LevelsFolder}/Level_{i:00}.asset";
                PuzzleGlassLevelData asset = AssetDatabase.LoadAssetAtPath<PuzzleGlassLevelData>(path);
                bool isNew = false;
                if (asset == null)
                {
                    asset = CreateInstance<PuzzleGlassLevelData>();
                    isNew = true;
                }

                asset.levelNumber = i;
                asset.difficulty = PuzzleLevelGenerator.GetProgressiveDifficulty(i);
                asset.useStagingColumnQueue = true;
                PuzzleLevelGenerator.GenerateForLevelData(asset, randomSeed: i * 7919, customBallsPerBox: _batchBallsPerBox);

                if (isNew)
                {
                    AssetDatabase.CreateAsset(asset, path);
                }
                else
                {
                    EditorUtility.SetDirty(asset);
                }
            }

            AssetDatabase.SaveAssets();
            RefreshLevelList();
            if (_allLevels.Count > 0)
            {
                _selectedLevel = _allLevels[0];
                if (_liveSyncToScene) ApplyLevelToScene(_selectedLevel);
            }
            Debug.Log($"<color=#00FF88><b>[Level Design Studio]</b></color> {count} adet ilerlemeli (Kolay -> Orta -> Zor -> Boss) level başarıyla üretildi!");
        }

        private void CreateOrUpdateStarterLevel(
            int number,
            string title,
            LevelDifficulty diff,
            int slots,
            GlassShapePreset shape,
            GlassColorTheme theme,
            BottleLayerPattern pattern,
            bool showEars,
            params LevelMonsterEntry[] monsters)
        {
            string path = $"{LevelsFolder}/Level_{number:00}.asset";
            PuzzleGlassLevelData asset = AssetDatabase.LoadAssetAtPath<PuzzleGlassLevelData>(path);
            bool isNew = false;
            if (asset == null)
            {
                asset = CreateInstance<PuzzleGlassLevelData>();
                isNew = true;
            }

            asset.levelNumber = number;
            asset.levelName = title;
            asset.difficulty = diff;
            asset.slotCount = slots;
            asset.glassPreset = shape;
            asset.layerPattern = pattern;
            asset.showShoulderHandles = showEars;
            asset.symmetricMode = (shape != GlassShapePreset.SCurveFunnel);
            asset.particleSize = 0.155f;
            asset.particlesPerMonsterCount = 1;
            ApplyThemeColorsToLevelData(asset, theme);
            asset.monsters = new List<LevelMonsterEntry>(monsters);

            if (isNew)
            {
                AssetDatabase.CreateAsset(asset, path);
            }
            else
            {
                EditorUtility.SetDirty(asset);
            }
        }
    }
}

