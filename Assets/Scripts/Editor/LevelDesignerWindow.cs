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
            "🐍 S-Kıvrım Tüp"
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

        [MenuItem("Tools/PuzzleGlass/🎮 Level Design Studio (Bölüm Tasarım Aracı)", false, 0)]
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

            if (GUILayout.Button("🎲 5 Örnek Starter Level Paketi Üret", GUILayout.Height(24)))
            {
                GenerateStarterLevels();
            }

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
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            int totalMonsterCounts = 0;
            if (lvl.monsters != null)
            {
                for (int i = 0; i < lvl.monsters.Count; i++) totalMonsterCounts += lvl.monsters[i].count;
            }
            int totalParticles = totalMonsterCounts * Mathf.Max(1, lvl.particlesPerMonsterCount);

            EditorGUILayout.LabelField(
                $"📊 Canlı Bölüm Özeti:  {lvl.monsters.Count} Canavar  |  Toplam Sayaç: {totalMonsterCounts}  |  Şişedeki Küp: {totalParticles} Adet  |  {lvl.slotCount} Slot",
                EditorStyles.boldLabel);

            Rect previewRect = GUILayoutUtility.GetRect(480f, 118f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(previewRect, new Color(0.12f, 0.15f, 0.22f, 1f));

            // Left side of preview card: Bottle Color Bands from top to bottom
            Rect bottleRect = new Rect(previewRect.x + 14f, previewRect.y + 10f, 150f, previewRect.height - 20f);
            EditorGUI.DrawRect(bottleRect, new Color(0.08f, 0.10f, 0.15f, 1f));

            if (lvl.monsters != null && lvl.monsters.Count > 0 && totalMonsterCounts > 0)
            {
                float curY = bottleRect.yMax - 4f;
                float availH = bottleRect.height - 8f;

                for (int i = 0; i < lvl.monsters.Count; i++)
                {
                    var m = lvl.monsters[i];
                    float frac = (float)m.count / totalMonsterCounts;
                    float bandH = Mathf.Max(8f, availH * frac);
                    Rect bandRect = new Rect(bottleRect.x + 6f, curY - bandH, bottleRect.width - 12f, bandH - 1f);
                    EditorGUI.DrawRect(bandRect, PuzzleColorPalette.GetCubeColor(m.color));

                    GUIStyle bandLabel = new GUIStyle(EditorStyles.miniBoldLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        normal = { textColor = Color.white }
                    };
                    GUI.Label(bandRect, $"{m.color} ({m.count})", bandLabel);
                    curY -= bandH;
                }
            }

            // Right side of preview card: Slots + Monsters lineup
            float rightStartX = bottleRect.xMax + 24f;
            float rightWidth = previewRect.xMax - rightStartX - 14f;

            // Draw Slots Row
            float slotBoxW = 36f;
            float slotGap = 8f;
            float slotsTotalW = lvl.slotCount * slotBoxW + (lvl.slotCount - 1) * slotGap;
            float slotStartX = rightStartX + (rightWidth - slotsTotalW) * 0.5f;

            for (int s = 0; s < lvl.slotCount; s++)
            {
                Rect sRect = new Rect(slotStartX + s * (slotBoxW + slotGap), previewRect.y + 14f, slotBoxW, 28f);
                EditorGUI.DrawRect(sRect, new Color(0.75f, 0.86f, 0.98f, 1f));
                Rect sIn = new Rect(sRect.x + 4f, sRect.y + 4f, sRect.width - 8f, sRect.height - 8f);
                EditorGUI.DrawRect(sIn, new Color(0.38f, 0.58f, 0.84f, 1f));
            }

            // Draw Monsters Row
            if (lvl.monsters != null && lvl.monsters.Count > 0)
            {
                int mCount = lvl.monsters.Count;
                float monW = Mathf.Min(54f, (rightWidth - (mCount - 1) * 8f) / Mathf.Max(1, mCount));
                float monTotalW = mCount * monW + (mCount - 1) * 8f;
                float monStartX = rightStartX + (rightWidth - monTotalW) * 0.5f;

                for (int i = 0; i < mCount; i++)
                {
                    var m = lvl.monsters[i];
                    Rect mRect = new Rect(monStartX + i * (monW + 8f), previewRect.y + 54f, monW, 50f);
                    EditorGUI.DrawRect(mRect, PuzzleColorPalette.GetBodyColor(m.color));

                    Rect badgeRect = new Rect(mRect.x + 6f, mRect.yMax - 20f, mRect.width - 12f, 16f);
                    EditorGUI.DrawRect(badgeRect, Color.white);

                    GUIStyle numStyle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        fontSize = 11,
                        normal = { textColor = PuzzleColorPalette.GetTextColor(m.color) }
                    };
                    GUI.Label(badgeRect, m.count.ToString(), numStyle);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawMetadataAndSlotsSection(PuzzleGlassLevelData lvl)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("⚙️ Bölüm Bilgileri & Slot Ayarları", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            lvl.levelNumber = EditorGUILayout.IntField("Level Numarası", lvl.levelNumber, GUILayout.Width(220));
            lvl.difficulty = (LevelDifficulty)EditorGUILayout.EnumPopup("Zorluk Derecesi", lvl.difficulty);
            EditorGUILayout.EndHorizontal();

            lvl.levelName = EditorGUILayout.TextField("Bölüm Adı", lvl.levelName);

            EditorGUILayout.BeginHorizontal();
            lvl.slotCount = EditorGUILayout.IntSlider("Slot Sayısı (1 - 5)", lvl.slotCount, 1, 5);
            lvl.slotSpacing = EditorGUILayout.Slider("Slot Aralığı", lvl.slotSpacing, 0.65f, 1.05f);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            lvl.particleSize = EditorGUILayout.Slider("Partikül Boyutu", lvl.particleSize, 0.09f, 0.20f);
            lvl.particlesPerMonsterCount = EditorGUILayout.IntSlider("Sayaç Başına Küp Çarpanı", lvl.particlesPerMonsterCount, 1, 8);
            EditorGUILayout.EndHorizontal();

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

            EditorGUILayout.EndVertical();
        }

        private void DrawMonstersAndLayersSection(PuzzleGlassLevelData lvl)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("👾 Canavarlar & Şişe Katman Sırası (Alttan Üste)", EditorStyles.boldLabel);

            GUI.backgroundColor = new Color(0.30f, 0.88f, 0.52f);
            if (GUILayout.Button("➕ Canavar Ekle", GUILayout.Width(130), GUILayout.Height(24)))
            {
                Undo.RecordObject(lvl, "Add Monster To Level");
                PuzzleColor[] cycle = new PuzzleColor[]
                {
                    PuzzleColor.Blue, PuzzleColor.Yellow, PuzzleColor.Red,
                    PuzzleColor.Green, PuzzleColor.Purple, PuzzleColor.Orange
                };
                PuzzleColor nextCol = cycle[lvl.monsters.Count % cycle.Length];
                lvl.monsters.Add(new LevelMonsterEntry(nextCol, 10));
                EditorUtility.SetDirty(lvl);
                if (_liveSyncToScene) ApplyLevelToScene(lvl);
            }

            GUI.backgroundColor = new Color(0.95f, 0.72f, 0.25f);
            if (GUILayout.Button("🎲 Rastgele Dizilim", GUILayout.Width(135), GUILayout.Height(24)))
            {
                Undo.RecordObject(lvl, "Randomize Level Monsters");
                RandomizeMonstersForLevel(lvl);
                EditorUtility.SetDirty(lvl);
                if (_liveSyncToScene) ApplyLevelToScene(lvl);
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(
                "Not: #1 numaralı canavarın rengi şişenin en alt (ağız/spout) katmanına, sonrakiler sırasıyla üst katmanlara yerleşir.",
                EditorStyles.miniLabel);

            if (lvl.monsters == null) lvl.monsters = new List<LevelMonsterEntry>();

            for (int i = 0; i < lvl.monsters.Count; i++)
            {
                var entry = lvl.monsters[i];
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                // Color swatch box
                Rect swatchRect = GUILayoutUtility.GetRect(24f, 24f, GUILayout.Width(24f));
                EditorGUI.DrawRect(swatchRect, PuzzleColorPalette.GetBodyColor(entry.color));

                EditorGUILayout.LabelField(i == 0 ? $"#{i + 1} (En Alt/Ağız)" : $"#{i + 1} ({i + 1}. Katman)", GUILayout.Width(105));

                entry.color = (PuzzleColor)EditorGUILayout.EnumPopup(entry.color, GUILayout.Width(95));
                entry.count = EditorGUILayout.IntSlider("Sayaç (Count)", entry.count, 2, 40);

                // Quick count preset buttons
                if (GUILayout.Button("10", GUILayout.Width(28))) entry.count = 10;
                if (GUILayout.Button("15", GUILayout.Width(28))) entry.count = 15;
                if (GUILayout.Button("20", GUILayout.Width(28))) entry.count = 20;

                // Move Up / Down buttons
                GUI.enabled = i > 0;
                if (GUILayout.Button("▲", GUILayout.Width(24)))
                {
                    Undo.RecordObject(lvl, "Move Monster Up");
                    var tmp = lvl.monsters[i - 1];
                    lvl.monsters[i - 1] = lvl.monsters[i];
                    lvl.monsters[i] = tmp;
                    GUI.enabled = true;
                    EditorGUILayout.EndHorizontal();
                    break;
                }

                GUI.enabled = i < lvl.monsters.Count - 1;
                if (GUILayout.Button("▼", GUILayout.Width(24)))
                {
                    Undo.RecordObject(lvl, "Move Monster Down");
                    var tmp = lvl.monsters[i + 1];
                    lvl.monsters[i + 1] = lvl.monsters[i];
                    lvl.monsters[i] = tmp;
                    GUI.enabled = true;
                    EditorGUILayout.EndHorizontal();
                    break;
                }

                GUI.enabled = lvl.monsters.Count > 1;
                GUI.backgroundColor = new Color(0.95f, 0.42f, 0.42f);
                if (GUILayout.Button("X", GUILayout.Width(24)))
                {
                    Undo.RecordObject(lvl, "Remove Monster");
                    lvl.monsters.RemoveAt(i);
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
            newLvl.levelName = $"Level {nextNum}";
            newLvl.glassPreset = GlassShapePreset.BulbousFlask;
            ApplyThemeColorsToLevelData(newLvl, GlassColorTheme.SkyBlueGlossy);
            newLvl.monsters = new List<LevelMonsterEntry>
            {
                new LevelMonsterEntry(PuzzleColor.Blue, 10),
                new LevelMonsterEntry(PuzzleColor.Yellow, 15),
                new LevelMonsterEntry(PuzzleColor.Red, 10)
            };

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
            string path = AssetDatabase.GetAssetPath(_selectedLevel);
            if (!string.IsNullOrEmpty(path))
            {
                AssetDatabase.DeleteAsset(path);
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
            if (!Directory.Exists(LevelsFolder)) Directory.CreateDirectory(LevelsFolder);

            CreateOrUpdateStarterLevel(
                1, "Gök Mavisi Flask (Referans)", LevelDifficulty.Easy, 4,
                GlassShapePreset.BulbousFlask, GlassColorTheme.SkyBlueGlossy, BottleLayerPattern.VFunnelBands, true,
                new LevelMonsterEntry(PuzzleColor.Blue, 10),
                new LevelMonsterEntry(PuzzleColor.Yellow, 20));

            CreateOrUpdateStarterLevel(
                2, "Kum Saati Düellosu", LevelDifficulty.Easy, 4,
                GlassShapePreset.Hourglass, GlassColorTheme.EmeraldGreen, BottleLayerPattern.HorizontalWaves, true,
                new LevelMonsterEntry(PuzzleColor.Green, 10),
                new LevelMonsterEntry(PuzzleColor.Yellow, 12),
                new LevelMonsterEntry(PuzzleColor.Blue, 10));

            CreateOrUpdateStarterLevel(
                3, "Kalp İksiri", LevelDifficulty.Medium, 4,
                GlassShapePreset.HeartFlask, GlassColorTheme.RoseQuartz, BottleLayerPattern.VFunnelBands, true,
                new LevelMonsterEntry(PuzzleColor.Red, 10),
                new LevelMonsterEntry(PuzzleColor.Purple, 12),
                new LevelMonsterEntry(PuzzleColor.Yellow, 10));

            CreateOrUpdateStarterLevel(
                4, "Elmas Kristal Karaf", LevelDifficulty.Hard, 4,
                GlassShapePreset.DiamondCarafe, GlassColorTheme.AmethystPurple, BottleLayerPattern.LeftRightSplit, false,
                new LevelMonsterEntry(PuzzleColor.Purple, 10),
                new LevelMonsterEntry(PuzzleColor.Blue, 10),
                new LevelMonsterEntry(PuzzleColor.Orange, 10),
                new LevelMonsterEntry(PuzzleColor.Red, 10));

            CreateOrUpdateStarterLevel(
                5, "S-Kıvrımlı Kaydırak", LevelDifficulty.Boss, 4,
                GlassShapePreset.SCurveFunnel, GlassColorTheme.AmberGold, BottleLayerPattern.DiagonalBands, false,
                new LevelMonsterEntry(PuzzleColor.Orange, 12),
                new LevelMonsterEntry(PuzzleColor.Blue, 12),
                new LevelMonsterEntry(PuzzleColor.Green, 10),
                new LevelMonsterEntry(PuzzleColor.Purple, 10));

            AssetDatabase.SaveAssets();
            RefreshLevelList();
            if (_allLevels.Count > 0) _selectedLevel = _allLevels[0];
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

