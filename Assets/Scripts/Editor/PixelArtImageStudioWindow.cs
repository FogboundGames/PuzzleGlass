using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PuzzleGlass.Editor
{
    public class PixelArtImageStudioWindow : EditorWindow, ISerializationCallbackReceiver
    {
        public static void OpenWindow()
        {
            var win = GetWindow<PixelArtImageStudioWindow>("Pixel Art Stüdyosu");
            win.minSize = new Vector2(480, 680);
            win.Show();
        }

        [Header("Kaynak Görsel")]
        [SerializeField] private Texture2D _sourceTexture;
        [SerializeField] private string _lastLoadedPath = "";

        [Header("Piksel Grid Ayarları")]
        [SerializeField] private int _gridWidth = 10;
        [SerializeField] private int _gridHeight = 10;
        [SerializeField] private bool _ignoreWhiteBackground = true;
        [Range(0.60f, 0.99f)]
        [SerializeField] private float _whiteThreshold = 0.88f;

        [Header("Cam Şişe Şekli")]
        [SerializeField] private GlassShapePreset _containerShape = GlassShapePreset.SquareFlask;

        // Dönüştürülmüş piksel matrisi
        private PuzzleColor?[,] _convertedGrid;
        [SerializeField] private PixelArtPattern _gridSnapshot; // Çok boyutlu dizi serialize edilemediği için
        private Dictionary<PuzzleColor, int> _colorCounts = new Dictionary<PuzzleColor, int>();
        [SerializeField] private PuzzleColor _selectedBrushColor = PuzzleColor.Blue;
        [SerializeField] private bool _eraseMode = false;
        private Vector2 _scrollPos;

        private void OnEnable()
        {
            if (_convertedGrid == null)
            {
                // Varsayılan olarak defterdeki kalp veya kurbağa desenini yükle
                LoadPreset(PixelArtPreset.Frog);
                return;
            }

            _gridWidth = _convertedGrid.GetLength(0);
            _gridHeight = _convertedGrid.GetLength(1);
            UpdateColorCounts();
        }

        public void OnBeforeSerialize()
        {
            _gridSnapshot = _convertedGrid != null ? PixelArtPattern.FromColorGrid("GridSnapshot", _convertedGrid) : null;
        }

        public void OnAfterDeserialize()
        {
            if (_gridSnapshot != null && _gridSnapshot.HasData)
            {
                _convertedGrid = _gridSnapshot.GetColorGrid();
            }
        }

        private void OnGUI()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            EditorGUILayout.Space(8);

            // Başlık
            GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 15,
                alignment = TextAnchor.MiddleCenter
            };
            EditorGUILayout.LabelField("🎨 Pixel Art Görsel Dönüştürücü & Tasarım Aracı", headerStyle);
            EditorGUILayout.HelpBox("İstediğiniz resmi buraya yükleyin! Resim otomatik olarak defterdeki gibi piksel sanatına ve cam içindeki 3D toplara dönüştürülür.", MessageType.Info);

            EditorGUILayout.Space(6);

            // ==================== 1. GÖRSEL YÜKLEME ====================
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("1. Görsel Seç veya Sürükle Bırak", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _sourceTexture = (Texture2D)EditorGUILayout.ObjectField("Görsel (Texture2D):", _sourceTexture, typeof(Texture2D), false, GUILayout.Height(20));
            if (EditorGUI.EndChangeCheck() && _sourceTexture != null)
            {
                ConvertImageToPixelArt();
            }

            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.35f, 0.75f, 1.0f);
            if (GUILayout.Button("📂 Bilgisayardan Görsel Seç (.png, .jpg)", GUILayout.Height(28)))
            {
                string path = EditorUtility.OpenFilePanel("Piksel Sanatı Görseli Seç", "", "png,jpg,jpeg");
                if (!string.IsNullOrEmpty(path))
                {
                    LoadTextureFromFile(path);
                }
            }
            GUI.backgroundColor = Color.white;

            if (_sourceTexture != null)
            {
                if (GUILayout.Button("🔄 Yeniden Dönüştür", GUILayout.Height(28), GUILayout.Width(130)))
                {
                    ConvertImageToPixelArt();
                }
            }
            EditorGUILayout.EndHorizontal();

            // Hazır Defter Şablonları (Tek Tıkla Yükleme)
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Veya Hazır Defter Şablonu Seç:", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("🐸 Kurbağa", GUILayout.Height(22))) LoadPreset(PixelArtPreset.Frog);
            if (GUILayout.Button("❤️ Kalp", GUILayout.Height(22))) LoadPreset(PixelArtPreset.Heart);
            if (GUILayout.Button("🍄 Mantar", GUILayout.Height(22))) LoadPreset(PixelArtPreset.Mushroom);
            if (GUILayout.Button("🐝 Arı", GUILayout.Height(22))) LoadPreset(PixelArtPreset.Bee);
            if (GUILayout.Button("🐙 Ahtapot", GUILayout.Height(22))) LoadPreset(PixelArtPreset.OctopusSkull);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("🪐 Satürn", GUILayout.Height(22))) LoadPreset(PixelArtPreset.Saturn);
            if (GUILayout.Button("🐧 Penguen", GUILayout.Height(22))) LoadPreset(PixelArtPreset.Penguin);
            if (GUILayout.Button("🦋 Kelebek", GUILayout.Height(22))) LoadPreset(PixelArtPreset.Butterfly);
            if (GUILayout.Button("🌷 Lale", GUILayout.Height(22))) LoadPreset(PixelArtPreset.Tulip);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();

            // ==================== 2. DÖNÜŞTÜRME AYARLARI ====================
            EditorGUILayout.Space(6);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("2. Grid & Arka Plan Ayarları", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Hızlı Grid Boyutu:", GUILayout.Width(120));
            if (GUILayout.Button("8x8", GUILayout.Height(20))) SetGridSize(8, 8);
            if (GUILayout.Button("10x10", GUILayout.Height(20))) SetGridSize(10, 10);
            if (GUILayout.Button("12x12", GUILayout.Height(20))) SetGridSize(12, 12);
            if (GUILayout.Button("14x14", GUILayout.Height(20))) SetGridSize(14, 14);
            if (GUILayout.Button("16x16", GUILayout.Height(20))) SetGridSize(16, 16);
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginChangeCheck();
            _gridWidth = EditorGUILayout.IntSlider("Genişlik (Sütun):", _gridWidth, 4, 24);
            _gridHeight = EditorGUILayout.IntSlider("Yükseklik (Satır):", _gridHeight, 4, 24);

            _ignoreWhiteBackground = EditorGUILayout.Toggle("Beyaz Arka Planı Temizle", _ignoreWhiteBackground);
            if (_ignoreWhiteBackground)
            {
                _whiteThreshold = EditorGUILayout.Slider("Beyazlık Eşiği:", _whiteThreshold, 0.60f, 0.99f);
            }

            if (EditorGUI.EndChangeCheck())
            {
                if (_sourceTexture != null) ConvertImageToPixelArt();
                else ResizeGrid(_gridWidth, _gridHeight);
            }

            EditorGUILayout.EndVertical();

            // ==================== 3. CAM ŞİŞE ŞEKLİ ====================
            EditorGUILayout.Space(6);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("3. Hedef Cam Şekli (Kare / Dikdörtgen / Şişe)", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            bool isSquare = _containerShape == GlassShapePreset.SquareFlask;
            bool isRect = _containerShape == GlassShapePreset.RectangleFlask;
            bool isSquareBox = _containerShape == GlassShapePreset.SquareBox;
            bool isFlask = _containerShape == GlassShapePreset.BulbousFlask || _containerShape == GlassShapePreset.DiamondCarafe;

            GUI.backgroundColor = isSquare ? new Color(0.3f, 0.85f, 0.5f) : Color.white;
            if (GUILayout.Button("🟦 Kare Cam (SquareFlask)", GUILayout.Height(26))) _containerShape = GlassShapePreset.SquareFlask;

            GUI.backgroundColor = isRect ? new Color(0.3f, 0.85f, 0.5f) : Color.white;
            if (GUILayout.Button("▯ Dikdörtgen Cam", GUILayout.Height(26))) _containerShape = GlassShapePreset.RectangleFlask;

            GUI.backgroundColor = isSquareBox ? new Color(0.3f, 0.85f, 0.5f) : Color.white;
            if (GUILayout.Button("⏹ Kare Kutu (Düz)", GUILayout.Height(26))) _containerShape = GlassShapePreset.SquareBox;

            GUI.backgroundColor = isFlask ? new Color(0.3f, 0.85f, 0.5f) : Color.white;
            if (GUILayout.Button("⚗️ Huni Şişe", GUILayout.Height(26))) _containerShape = GlassShapePreset.BulbousFlask;
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();

            // ==================== 4. CANLI PİKSEL ÖNİZLEME ====================
            EditorGUILayout.Space(6);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("4. Canlı Piksel Önizleme & Düzenleme (Tıklayarak Boyayabilirsiniz)", EditorStyles.boldLabel);

            DrawColorPaletteToolbar();

            EditorGUILayout.Space(4);
            DrawInteractiveGridPreview();

            // Renk Dağılımı Özeti
            DrawColorSummary();

            EditorGUILayout.EndVertical();

            // ==================== 5. SAHNEYE UYGULA BUTONU ====================
            EditorGUILayout.Space(10);
            GUI.backgroundColor = new Color(0.2f, 0.9f, 0.45f);
            if (GUILayout.Button("✨ CAM ŞİŞEYE UYGULA VE OTOMATİK OLUŞTUR", GUILayout.Height(42)))
            {
                ApplyToGlassContainerAndScene();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space(12);
            EditorGUILayout.EndScrollView();
        }

        private void SetGridSize(int w, int h)
        {
            _gridWidth = w;
            _gridHeight = h;
            if (_sourceTexture != null) ConvertImageToPixelArt();
            else ResizeGrid(w, h);
        }

        private void LoadPreset(PixelArtPreset preset)
        {
            var pat = PixelArtLibrary.GetPreset(preset);
            if (pat != null)
            {
                _gridWidth = pat.width;
                _gridHeight = pat.height;
                _convertedGrid = pat.GetColorGrid();
                UpdateColorCounts();
                Repaint();
            }
        }

        private void LoadTextureFromFile(string filePath)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                Texture2D tex = new Texture2D(2, 2);
                if (tex.LoadImage(bytes))
                {
                    if (_sourceTexture != null && !EditorUtility.IsPersistent(_sourceTexture)) DestroyImmediate(_sourceTexture);
                    _sourceTexture = tex;
                    _lastLoadedPath = filePath;
                    ConvertImageToPixelArt();
                }
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Görsel Yükleme Hatası", ex.Message, "Tamam");
            }
        }

        /// <summary>
        /// Resmi hedef grid çözünürlüğüne küçültür ve her pikseli PuzzleColor paletine eşler.
        /// </summary>
        private void ConvertImageToPixelArt()
        {
            if (_sourceTexture == null) return;

            Texture2D readable = MakeReadableTexture(_sourceTexture);
            if (readable == null) return;

            _convertedGrid = new PuzzleColor?[_gridWidth, _gridHeight];

            int srcW = readable.width;
            int srcH = readable.height;

            for (int y = 0; y < _gridHeight; y++)
            {
                // Y'yi aşağıdan yukarıya çevir (Unity Texture koordinatları alt-soldan başlar)
                float v = (float)y / (_gridHeight - 1);
                int srcY = Mathf.Clamp(Mathf.RoundToInt(v * (srcH - 1)), 0, srcH - 1);

                for (int x = 0; x < _gridWidth; x++)
                {
                    float u = (float)x / (_gridWidth - 1);
                    int srcX = Mathf.Clamp(Mathf.RoundToInt(u * (srcW - 1)), 0, srcW - 1);

                    Color pixel = readable.GetPixel(srcX, srcY);

                    // Şeffaf veya Beyaz arka plan kontrolü
                    if (pixel.a < 0.25f)
                    {
                        _convertedGrid[x, y] = null;
                        continue;
                    }

                    if (_ignoreWhiteBackground && pixel.r >= _whiteThreshold && pixel.g >= _whiteThreshold && pixel.b >= _whiteThreshold)
                    {
                        _convertedGrid[x, y] = null;
                        continue;
                    }

                    _convertedGrid[x, y] = FindClosestPuzzleColor(pixel);
                }
            }

            DestroyImmediate(readable);
            UpdateColorCounts();
            Repaint();
        }

        private Texture2D MakeReadableTexture(Texture2D source)
        {
            RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, rt);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;

            Texture2D readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            readable.Apply();

            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return readable;
        }

        private PuzzleColor FindClosestPuzzleColor(Color c)
        {
            PuzzleColor best = PuzzleColor.Blue;
            float bestDist = float.MaxValue;

            var allColors = (PuzzleColor[])Enum.GetValues(typeof(PuzzleColor));
            for (int i = 0; i < allColors.Length; i++)
            {
                var pc = allColors[i];
                Color palCol = PuzzleColorPalette.GetCubeColor(pc);

                // Algısal ağırlıklı renk mesafesi (Perceptual RGB distance)
                float dr = c.r - palCol.r;
                float dg = c.g - palCol.g;
                float db = c.b - palCol.b;
                float dist = dr * dr * 0.30f + dg * dg * 0.59f + db * db * 0.11f;

                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = pc;
                }
            }

            return best;
        }

        private void ResizeGrid(int newW, int newH)
        {
            var newGrid = new PuzzleColor?[newW, newH];
            int copyW = Mathf.Min(newW, _convertedGrid != null ? _convertedGrid.GetLength(0) : 0);
            int copyH = Mathf.Min(newH, _convertedGrid != null ? _convertedGrid.GetLength(1) : 0);

            for (int y = 0; y < copyH; y++)
            {
                for (int x = 0; x < copyW; x++)
                {
                    newGrid[x, y] = _convertedGrid[x, y];
                }
            }
            _convertedGrid = newGrid;
            UpdateColorCounts();
        }

        private void UpdateColorCounts()
        {
            _colorCounts.Clear();
            if (_convertedGrid == null) return;

            int w = _convertedGrid.GetLength(0);
            int h = _convertedGrid.GetLength(1);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var c = _convertedGrid[x, y];
                    if (c.HasValue)
                    {
                        if (!_colorCounts.ContainsKey(c.Value)) _colorCounts[c.Value] = 0;
                        _colorCounts[c.Value]++;
                    }
                }
            }
        }

        private void DrawColorPaletteToolbar()
        {
            EditorGUILayout.LabelField("Piksel Fırçası (Tıklayıp Düzenlemek İçin):", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();

            var colors = (PuzzleColor[])Enum.GetValues(typeof(PuzzleColor));
            for (int i = 0; i < colors.Length; i++)
            {
                var c = colors[i];
                Color col = PuzzleColorPalette.GetCubeColor(c);
                bool isSel = !_eraseMode && _selectedBrushColor == c;

                GUI.backgroundColor = isSel ? Color.white : col;
                if (GUILayout.Button(isSel ? "●" : "", GUILayout.Width(26), GUILayout.Height(24)))
                {
                    _selectedBrushColor = c;
                    _eraseMode = false;
                }
            }

            GUI.backgroundColor = _eraseMode ? new Color(1f, 0.4f, 0.4f) : Color.white;
            if (GUILayout.Button("Silgi", GUILayout.Width(45), GUILayout.Height(24)))
            {
                _eraseMode = true;
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndHorizontal();
        }

        private void DrawInteractiveGridPreview()
        {
            if (_convertedGrid == null) return;

            int w = _convertedGrid.GetLength(0);
            int h = _convertedGrid.GetLength(1);

            float cellSize = Mathf.Clamp((position.width - 60f) / w, 16f, 28f);
            float totalW = w * cellSize;
            float totalH = h * cellSize;

            Rect previewBox = GUILayoutUtility.GetRect(totalW, totalH);
            // Ortala
            previewBox.x = (position.width - totalW) * 0.5f;
            previewBox.width = totalW;
            previewBox.height = totalH;

            // Koyu arka plan
            EditorGUI.DrawRect(previewBox, new Color(0.12f, 0.15f, 0.22f, 1f));

            Event evt = Event.current;

            for (int y = 0; y < h; y++)
            {
                // Y=0 aşağıda olacak şekilde çiz
                float drawY = previewBox.y + (h - 1 - y) * cellSize;

                for (int x = 0; x < w; x++)
                {
                    float drawX = previewBox.x + x * cellSize;
                    Rect cellRect = new Rect(drawX + 1, drawY + 1, cellSize - 2, cellSize - 2);

                    var col = _convertedGrid[x, y];
                    if (col.HasValue)
                    {
                        Color c = PuzzleColorPalette.GetCubeColor(col.Value);
                        EditorGUI.DrawRect(cellRect, c);
                    }
                    else
                    {
                        // Boş hücre: damalı veya açık arka plan
                        EditorGUI.DrawRect(cellRect, new Color(0.18f, 0.22f, 0.32f, 0.55f));
                    }

                    // Tıklama etkileşimi: hücreye tıklandığında fırça rengiyle boya
                    if (evt.type == EventType.MouseDown && cellRect.Contains(evt.mousePosition))
                    {
                        if (_eraseMode)
                        {
                            _convertedGrid[x, y] = null;
                        }
                        else
                        {
                            _convertedGrid[x, y] = _selectedBrushColor;
                        }
                        UpdateColorCounts();
                        evt.Use();
                        Repaint();
                    }
                }
            }
        }

        private void DrawColorSummary()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Görseldeki Top & Canavar Dağılımı:", EditorStyles.miniBoldLabel);

            EditorGUILayout.BeginHorizontal();
            int totalBalls = 0;
            foreach (var kvp in _colorCounts)
            {
                totalBalls += kvp.Value;
                Color c = PuzzleColorPalette.GetCubeColor(kvp.Key);
                GUI.color = c;
                GUILayout.Label($"■ {kvp.Key}: {kvp.Value} top", GUILayout.Width(110));
            }
            GUI.color = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField($"Toplam Fiziksel Top Sayısı: <b>{totalBalls}</b>", new GUIStyle(EditorStyles.label) { richText = true });
        }

        /// <summary>
        /// Seçilen piksel desenini sahnedeki ShapedGlassContainer ve PuzzleGlassGameManager'a uygular.
        /// </summary>
        public void ApplyToGlassContainerAndScene()
        {
            if (_convertedGrid == null) return;

            var container = FindFirstObjectByType<ShapedGlassContainer>();
            if (container == null)
            {
                EditorUtility.DisplayDialog("Uyarı", "Sahnede 'ShapedGlassContainer' bulunamadı!", "Tamam");
                return;
            }

            Undo.RecordObject(container, "Apply Pixel Art Image");

            // 1. Deseni PixelArtPattern nesnesine dönüştür
            string patternName = !string.IsNullOrEmpty(_lastLoadedPath) ? Path.GetFileNameWithoutExtension(_lastLoadedPath) : "CustomImagePattern";
            PixelArtPattern pattern = PixelArtPattern.FromColorGrid(patternName, _convertedGrid);

            int w = pattern.width;
            int h = pattern.height;

            // 2. Cam Şişe Şeklini Ayarla (Kare, Dikdörtgen vb.)
            container.shapePreset = _containerShape;
            container.ApplyShapePreset(_containerShape, false);

            // 3. Deseni ve parametreleri ayarla
            container.particleMode = ParticleArrangementMode.PixelArtVisual;
            container.pixelArtPreset = PixelArtPreset.Custom;
            container.customPattern = pattern;
            container.freezePatternInPlace = false;
            container.patternVerticalOffset = 0f;
            container.gridCellSpacing = 0.165f;

            // 4. Eski parçaları temizle ve otomatik yeniden oluştur
            container.CleanOrphanedChildren();
            container.RebuildContainer();

            // 5. Cam Fizik Sınırlarını Yeniden İnşa Et
            var bcm = container.GetComponent<BallContainerManager>().OrNull() ?? container.gameObject.AddComponent<BallContainerManager>();
            bcm.glassContainer = container;
            bcm.RebuildColliders();

            // 6. PuzzleGlassGameManager'daki alt canavarları bu görselin renklerine göre ayarla
            var gameManager = PuzzleGlassGameManager.Instance.OrNull() ?? FindFirstObjectByType<PuzzleGlassGameManager>();
            if (gameManager != null)
            {
                Undo.RecordObject(gameManager, "Update Monster Goals");
                gameManager.SynchronizeMonstersForPattern(pattern);
                EditorUtility.SetDirty(gameManager);
            }

            EditorUtility.SetDirty(container);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(container.gameObject.scene);
            SceneView.RepaintAll();

            EditorUtility.DisplayDialog("Başarılı! ✨",
                $"Piksel deseni başarıyla cam şişeye uygulandı!\n\n" +
                $"Şekil: {_containerShape}\n" +
                $"Boyut: {w}x{h}\n" +
                $"Desen camın karnının tam ortasına otomatik hizalandı.", "Tamam");
        }

        private void SpawnMonstersForGoals(PuzzleGlassGameManager mgr, List<LevelMonsterEntry> goals)
        {
            int totalM = goals.Count;
            int maxPerRow = 4;

            for (int i = 0; i < totalM; i++)
            {
                var entry = goals[i];
                int row = i / maxPerRow;
                int indexInRow = i % maxPerRow;
                int countInThisRow = Mathf.Min(maxPerRow, totalM - row * maxPerRow);

                float spacingX = countInThisRow <= 2 ? 1.50f : (countInThisRow == 3 ? 1.05f : 0.84f);
                float startX = -(countInThisRow - 1) * spacingX * 0.5f;
                float x = startX + indexInRow * spacingX;
                float y = -1.98f - row * 0.92f;
                float scale = countInThisRow <= 2 ? 0.72f : (countInThisRow == 3 ? 0.64f : 0.58f);

                GameObject monObj = new GameObject($"Monster_{entry.color}_{entry.count}_{i + 1}");
                monObj.transform.SetParent(mgr.transform, false);
                monObj.transform.position = new Vector3(x, y, 0f);
                monObj.transform.localScale = Vector3.one * scale;

                MonsterCharacter mon = monObj.AddComponent<MonsterCharacter>();
                mon.Initialize(entry.color, entry.count, $"{entry.color}_{entry.count}_{i + 1}");
                mgr.activeMonsters.Add(mon);
            }
        }
    }
}

