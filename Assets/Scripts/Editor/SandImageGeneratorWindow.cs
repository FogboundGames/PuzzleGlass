using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PuzzleGlass.Editor
{
    /// <summary>
    /// Phase 3 – Sand Image Generator:
    /// PNG/JPG görsellerini cam hazne içerisindeki fiziksel ve renkli kum parçacıklarına dönüştürür.
    /// Renk indirgeme, palet yönetimi, çözünürlük ayarı, cam hazneye otomatik sığdırma ve seviye kaydetme sunar.
    /// </summary>
    public class SandImageGeneratorWindow : EditorWindow, ISerializationCallbackReceiver
    {
        private static readonly PuzzleColor[] AllColors = (PuzzleColor[])Enum.GetValues(typeof(PuzzleColor));

        [MenuItem("Tools/PuzzleGlass/🎨 Sand Image Generator (Görseli Kuma Dönüştürme)", false, 0)]
        [MenuItem("Window/PuzzleGlass/🎨 Sand Image Generator", false, 0)]
        public static void OpenWindow()
        {
            var win = GetWindow<SandImageGeneratorWindow>("Sand Image Generator");
            win.minSize = new Vector2(520, 740);
            win.Show();
        }

        [Header("1. Kaynak Görsel")]
        [SerializeField] private Texture2D _sourceTexture;
        private string _lastLoadedPath = "";

        [Header("2. Çözünürlük ve Boyut")]
        [Range(4, 32)] [SerializeField] private int _gridWidth = 11;
        [Range(4, 32)] [SerializeField] private int _gridHeight = 11;
        [SerializeField] private bool _maintainAspect = true;
        [SerializeField] private bool _flipHorizontal = false;
        [SerializeField] private bool _flipVertical = false;

        [Header("3. Arka Plan & Şeffaflık")]
        [SerializeField] private bool _ignoreTransparent = true;
        [Range(0.01f, 0.50f)] [SerializeField] private float _alphaThreshold = 0.15f;
        [SerializeField] private bool _ignoreWhiteBackground = true;
        [Range(0.70f, 0.99f)] [SerializeField] private float _whiteThreshold = 0.88f;

        [Header("4. Renk Paleti ve Kuantizasyon")]
        [Range(2, 11)] [SerializeField] private int _maxPaletteColors = 8;
        [SerializeField] private PuzzleColor _activeBrushColor = PuzzleColor.Blue;
        [SerializeField] private bool _eraserMode = false;

        [Header("5. Kum Parçacığı Ayarları")]
        [Range(0.08f, 0.28f)] [SerializeField] private float _particleSize = 0.16f;
        [Range(0.08f, 0.32f)] [SerializeField] private float _particleSpacing = 0.165f;
        [SerializeField] private bool _autoCalculateSpacing = true;

        // Dönüştürülmüş Piksel Matrisi [x, y]
        private PuzzleColor?[,] _convertedGrid;
        [SerializeField] private PixelArtPattern _gridSnapshot; // Çok boyutlu dizi serialize edilemediği için
        private Dictionary<PuzzleColor, int> _colorCounts = new Dictionary<PuzzleColor, int>();
        private Vector2 _scrollPos;
        [SerializeField] private string _levelSaveName = "Level_CustomImage";

        private void OnEnable()
        {
            if (_convertedGrid == null)
            {
                LoadPreset(PixelArtPreset.Rainbow);
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
            EditorGUILayout.Space(6);

            DrawHeader();
            EditorGUILayout.Space(6);

            DrawSection1_ImageImport();
            EditorGUILayout.Space(6);

            DrawSection2_ResolutionAndTransform();
            EditorGUILayout.Space(6);

            DrawSection3_ColorPalette();
            EditorGUILayout.Space(6);

            DrawSection4_PixelGridPreview();
            EditorGUILayout.Space(6);

            DrawSection5_GlassAutoFitAndGenerate();
            EditorGUILayout.Space(6);

            DrawSection6_SimulationControls();
            EditorGUILayout.Space(6);

            DrawSection7_LevelSaveLoad();
            EditorGUILayout.Space(12);

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter
            };
            titleStyle.normal.textColor = new Color(0.20f, 0.65f, 1.0f);
            EditorGUILayout.LabelField("🎨 SAND IMAGE GENERATOR", titleStyle);

            GUIStyle subStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter
            };
            EditorGUILayout.LabelField("Görselleri (PNG/JPG) Cam Hazne İçin Renkli Kum Sanatına Dönüştür", subStyle);
            EditorGUILayout.EndVertical();
        }

        // ==================== 1. GÖRSEL YÜKLEME ====================
        private void DrawSection1_ImageImport()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("1. Görsel Yükle veya Şablon Seç", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _sourceTexture = (Texture2D)EditorGUILayout.ObjectField("Kaynak Görsel:", _sourceTexture, typeof(Texture2D), false, GUILayout.Height(20));
            if (EditorGUI.EndChangeCheck() && _sourceTexture != null)
            {
                ConvertImageToPixelGrid();
            }

            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.35f, 0.75f, 1.0f);
            if (GUILayout.Button("📂 Bilgisayardan Görsel Seç (.PNG / .JPG)", GUILayout.Height(28)))
            {
                string path = EditorUtility.OpenFilePanel("Görsel Seç", "", "png,jpg,jpeg");
                if (!string.IsNullOrEmpty(path))
                {
                    LoadTextureFromFile(path);
                }
            }
            GUI.backgroundColor = Color.white;

            if (_sourceTexture != null)
            {
                if (GUILayout.Button("🔄 Yeniden Analiz Et", GUILayout.Height(28), GUILayout.Width(130)))
                {
                    ConvertImageToPixelGrid();
                }
            }
            EditorGUILayout.EndHorizontal();

            // Hızlı Şablonlar
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Hazır Örnek Şablonlar (Tek Tıkla Yükle):", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("🌈 Gökkuşağı", EditorStyles.miniButtonLeft, GUILayout.Height(24))) LoadPreset(PixelArtPreset.Rainbow);
            if (GUILayout.Button("🐱 Kedi", EditorStyles.miniButtonMid, GUILayout.Height(24))) LoadPreset(PixelArtPreset.Cat);
            if (GUILayout.Button("🐸 Kurbağa", EditorStyles.miniButtonMid, GUILayout.Height(24))) LoadPreset(PixelArtPreset.Frog);
            if (GUILayout.Button("❤️ Kalp", EditorStyles.miniButtonMid, GUILayout.Height(24))) LoadPreset(PixelArtPreset.Heart);
            if (GUILayout.Button("⭐ Yıldız", EditorStyles.miniButtonRight, GUILayout.Height(24))) LoadPreset(PixelArtPreset.Star);

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        // ==================== 2. ÇÖZÜNÜRLÜK & DÖNÜŞÜM ====================
        private void DrawSection2_ResolutionAndTransform()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("2. Çözünürlük ve Arka Plan Filtresi", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();

            EditorGUILayout.BeginHorizontal();
            _gridWidth = EditorGUILayout.IntSlider("Genişlik (Sütun):", _gridWidth, 4, 32);
            using (new EditorGUI.DisabledScope(_maintainAspect && _sourceTexture != null))
            {
                _gridHeight = EditorGUILayout.IntSlider("Yükseklik (Satır):", _gridHeight, 4, 32);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            _maintainAspect = EditorGUILayout.ToggleLeft("En-Boy Oranını Koru", _maintainAspect, GUILayout.Width(170));
            _flipHorizontal = EditorGUILayout.ToggleLeft("Yatay Çevir (Flip X)", _flipHorizontal, GUILayout.Width(140));
            _flipVertical = EditorGUILayout.ToggleLeft("Dikey Çevir (Flip Y)", _flipVertical, GUILayout.Width(140));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);
            EditorGUILayout.BeginHorizontal();
            _ignoreTransparent = EditorGUILayout.ToggleLeft("Şeffaf Pikselleri Yoksay", _ignoreTransparent, GUILayout.Width(180));
            if (_ignoreTransparent)
            {
                _alphaThreshold = EditorGUILayout.Slider("Alpha Eşiği:", _alphaThreshold, 0.01f, 0.50f);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            _ignoreWhiteBackground = EditorGUILayout.ToggleLeft("Beyaz Arka Planı Kaldır", _ignoreWhiteBackground, GUILayout.Width(180));
            if (_ignoreWhiteBackground)
            {
                _whiteThreshold = EditorGUILayout.Slider("Beyazlık Eşiği:", _whiteThreshold, 0.70f, 0.99f);
            }
            EditorGUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck())
            {
                if (_sourceTexture != null) ConvertImageToPixelGrid();
                else ResizeGrid(_gridWidth, _gridHeight);
            }

            EditorGUILayout.EndVertical();
        }

        // ==================== 3. RENK PALETİ VE KUANTİZASYON ====================
        private void DrawSection3_ColorPalette()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("3. Renk Paleti ve Algılama", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _maxPaletteColors = EditorGUILayout.IntSlider("Maksimum Renk Sayısı:", _maxPaletteColors, 2, 11);
            if (EditorGUI.EndChangeCheck() && _sourceTexture != null)
            {
                ConvertImageToPixelGrid();
            }

            // Aktif Renkler ve Kum Taneleri Dağılımı
            int totalSand = 0;
            foreach (var kvp in _colorCounts) totalSand += kvp.Value;

            EditorGUILayout.LabelField($"Toplam Kum Parçacığı: {totalSand}", EditorStyles.miniBoldLabel);

            EditorGUILayout.BeginHorizontal();
            foreach (var kvp in _colorCounts)
            {
                PuzzleColor c = kvp.Key;
                int count = kvp.Value;
                Color col = PuzzleColorPalette.GetCubeColor(c);

                Color oldBg = GUI.backgroundColor;
                GUI.backgroundColor = col;

                string label = $"{c}\n{count}";
                if (GUILayout.Button(label, EditorStyles.miniButton, GUILayout.Height(34), GUILayout.MinWidth(45)))
                {
                    _activeBrushColor = c;
                    _eraserMode = false;
                }

                GUI.backgroundColor = oldBg;
            }
            EditorGUILayout.EndHorizontal();

            // Fırça Seçimi
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Çizim Fırçası:", GUILayout.Width(90));
            _activeBrushColor = (PuzzleColor)EditorGUILayout.EnumPopup(_activeBrushColor, GUILayout.Width(110));

            Color brushCol = PuzzleColorPalette.GetCubeColor(_activeBrushColor);
            Rect swatchRect = GUILayoutUtility.GetRect(22, 18, GUILayout.Width(35));
            EditorGUI.DrawRect(swatchRect, brushCol);

            _eraserMode = GUILayout.Toggle(_eraserMode, "🧹 Silgi Modu", "Button", GUILayout.Width(100));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        // ==================== 4. İNTERAKTİF PİKSEL ÖNİZLEME ====================
        private void DrawSection4_PixelGridPreview()
        {
            if (_convertedGrid == null) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("4. Piksel Sanatı Önizleme & Düzenleme", EditorStyles.boldLabel);

            int w = _convertedGrid.GetLength(0);
            int h = _convertedGrid.GetLength(1);

            float cellPixelSize = Mathf.Clamp(Mathf.Floor((position.width - 60f) / w), 12f, 28f);
            float gridTotalW = w * cellPixelSize;
            float gridTotalH = h * cellPixelSize;

            Rect gridRect = GUILayoutUtility.GetRect(gridTotalW, gridTotalH, GUILayout.ExpandWidth(false));
            gridRect.x = (position.width - gridTotalW) * 0.5f;

            // Arka plan kutusu
            EditorGUI.DrawRect(gridRect, new Color(0.12f, 0.15f, 0.20f, 1f));

            Event currentEvent = Event.current;
            bool isMouseOver = gridRect.Contains(currentEvent.mousePosition);

            // Hücreleri çiz
            for (int y = 0; y < h; y++)
            {
                // Görsel y=0 üstte olacak şekilde çiz
                int displayY = h - 1 - y;

                for (int x = 0; x < w; x++)
                {
                    Rect cellR = new Rect(gridRect.x + x * cellPixelSize, gridRect.y + displayY * cellPixelSize, cellPixelSize - 1, cellPixelSize - 1);

                    var col = _convertedGrid[x, y];
                    if (col.HasValue)
                    {
                        Color c = PuzzleColorPalette.GetCubeColor(col.Value);
                        EditorGUI.DrawRect(cellR, c);
                    }
                    else
                    {
                        // Boş hücre (Damalı desen)
                        Color emptyCol = ((x + y) % 2 == 0) ? new Color(0.18f, 0.22f, 0.28f, 1f) : new Color(0.14f, 0.18f, 0.24f, 1f);
                        EditorGUI.DrawRect(cellR, emptyCol);
                    }

                    // Tıklama ile boyama
                    if (isMouseOver && cellR.Contains(currentEvent.mousePosition))
                    {
                        if (currentEvent.type == EventType.MouseDown || currentEvent.type == EventType.MouseDrag)
                        {
                            if (currentEvent.button == 0) // Sol tık
                            {
                                _convertedGrid[x, y] = _eraserMode ? (PuzzleColor?)null : _activeBrushColor;
                                UpdateColorCounts();
                                Repaint();
                                currentEvent.Use();
                            }
                            else if (currentEvent.button == 1) // Sağ tık (silgi)
                            {
                                _convertedGrid[x, y] = null;
                                UpdateColorCounts();
                                Repaint();
                                currentEvent.Use();
                            }
                        }
                    }
                }
            }

            EditorGUILayout.HelpBox("Sol Tık: Seçili renkle boya | Sağ Tık: Hücreyi sil", MessageType.None);
            EditorGUILayout.EndVertical();
        }

        // ==================== 5. CAM HAZNEYE OTOMATİK SIĞDIRMA VE KUM OLUŞTURMA ====================
        private void DrawSection5_GlassAutoFitAndGenerate()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("5. Cam Hazneye Yerleşim ve Kum Oluşturma", EditorStyles.boldLabel);

            var glass = FindFirstObjectByType<ShapedGlassContainer>();
            if (glass != null)
            {
                EditorGUILayout.HelpBox($"Aktif Cam Hazne: {glass.gameObject.name} | Taban: {glass.spoutBottomY:F2} | Boyun: {glass.neckTopY:F2}", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox("Sahnede aktif ShapedGlassContainer bulunamadı! Lütfen bir cam hazne oluşturun.", MessageType.Warning);
            }

            EditorGUILayout.BeginHorizontal();
            _autoCalculateSpacing = EditorGUILayout.ToggleLeft("Hazneye Göre Otomatik Boyutlandır", _autoCalculateSpacing, GUILayout.Width(220));
            if (!_autoCalculateSpacing)
            {
                _particleSpacing = EditorGUILayout.Slider("Aralık (Spacing):", _particleSpacing, 0.08f, 0.30f);
            }
            _particleSize = EditorGUILayout.Slider("Küp Boyutu:", _particleSize, 0.08f, 0.28f);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            GUI.backgroundColor = new Color(0.20f, 0.85f, 0.40f);
            if (GUILayout.Button("✨ CAM İÇİNDE KUMU OLUŞTUR (Generate Sand)", GUILayout.Height(36)))
            {
                GenerateSandInChamber();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndVertical();
        }

        // ==================== 6. SİMÜLASYON MODLARI VE KAPI KONTROLÜ ====================
        private void DrawSection6_SimulationControls()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("6. Simülasyon Modu ve Alt Çıkış Kapağı", EditorStyles.boldLabel);

            var sandEngine = FindFirstObjectByType<SandSimulationEngine>();

            EditorGUILayout.BeginHorizontal();

            // 1. Static Image Mode
            bool isStatic = (sandEngine != null && sandEngine.simulationMode == SandSimulationMode.StaticImage);
            GUI.backgroundColor = isStatic ? new Color(0.35f, 0.75f, 1.0f) : Color.white;
            if (GUILayout.Button("📌 Static Image Mode\n(Resim Sabit Durur)", GUILayout.Height(34)))
            {
                if (sandEngine != null) sandEngine.SetSimulationMode(SandSimulationMode.StaticImage);
            }

            // 2. Dynamic Physics Mode
            bool isDynamic = (sandEngine != null && sandEngine.simulationMode == SandSimulationMode.DynamicPhysics);
            GUI.backgroundColor = isDynamic ? new Color(0.40f, 0.90f, 0.50f) : Color.white;
            if (GUILayout.Button("▶ Physics Mode\n(Kumlar Serbest Düşer)", GUILayout.Height(34)))
            {
                if (sandEngine != null) sandEngine.SetSimulationMode(SandSimulationMode.DynamicPhysics);
            }

            // 3. Preview Mode
            bool isPreview = (sandEngine != null && sandEngine.simulationMode == SandSimulationMode.Preview);
            GUI.backgroundColor = isPreview ? new Color(1.0f, 0.80f, 0.30f) : Color.white;
            if (GUILayout.Button("👁 Preview Mode\n(Editor Testi)", GUILayout.Height(34)))
            {
                if (sandEngine != null) sandEngine.SetSimulationMode(SandSimulationMode.Preview);
            }

            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // Alt Çıkış Kapağı Aç / Kapat
            bool gateOpen = (sandEngine != null && sandEngine.isGateOpen);
            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = gateOpen ? new Color(1.0f, 0.45f, 0.40f) : new Color(0.40f, 0.85f, 0.95f);

            string gateText = gateOpen ? "🔒 Alt Çıkış Kapağını KAPAT (Spout Closed)" : "🔓 Alt Çıkış Kapağını AÇ (Spout Open)";
            if (GUILayout.Button(gateText, GUILayout.Height(30)))
            {
                if (sandEngine != null) sandEngine.ToggleSpoutGate();
            }

            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            // Canavarları Kuma Göre Senkronize Et
            EditorGUILayout.Space(4);
            GUI.backgroundColor = new Color(0.75f, 0.55f, 1.0f);
            if (GUILayout.Button("👾 Canavarları Kum Renklerine Göre Senkronize Et", GUILayout.Height(30)))
            {
                SyncMonstersWithSandColors();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndVertical();
        }

        // ==================== 7. SEVİYE KAYDETME & YÜKLEME ====================
        private void DrawSection7_LevelSaveLoad()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("7. Seviyeyi Kaydet (Save Level)", EditorStyles.boldLabel);

            _levelSaveName = EditorGUILayout.TextField("Seviye Adı:", _levelSaveName);

            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.30f, 0.80f, 0.50f);
            if (GUILayout.Button("💾 Seviye Olarak Kaydet (.asset)", GUILayout.Height(30)))
            {
                SaveCurrentSetupAsLevel();
            }
            GUI.backgroundColor = Color.white;

            if (GUILayout.Button("📂 Mevcut Seviyeyi Yükle...", GUILayout.Height(30)))
            {
                LoadLevelFromAsset();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        // ==================== İŞLEM METOTLARI ====================
        private void LoadPreset(PixelArtPreset preset)
        {
            var pat = PixelArtLibrary.GetPreset(preset);
            if (pat != null)
            {
                _gridWidth = pat.width;
                _gridHeight = pat.height;
                _convertedGrid = pat.GetColorGrid();
                _levelSaveName = $"Level_{pat.patternName}";
                UpdateColorCounts();
                Repaint();
            }
        }

        private void LoadTextureFromFile(string filePath)
        {
            if (!File.Exists(filePath)) return;

            byte[] fileData = File.ReadAllBytes(filePath);
            Texture2D tex = new Texture2D(2, 2);
            if (tex.LoadImage(fileData))
            {
                tex.name = Path.GetFileNameWithoutExtension(filePath);
                if (_sourceTexture != null && !EditorUtility.IsPersistent(_sourceTexture)) DestroyImmediate(_sourceTexture);
                _sourceTexture = tex;
                _lastLoadedPath = filePath;
                _levelSaveName = $"Level_{tex.name}";
                ConvertImageToPixelGrid();
            }
        }

        private void ConvertImageToPixelGrid()
        {
            if (_sourceTexture == null) return;

            if (_maintainAspect)
            {
                float aspect = (float)_sourceTexture.height / _sourceTexture.width;
                _gridHeight = Mathf.Clamp(Mathf.RoundToInt(_gridWidth * aspect), 4, 32);
            }

            // Texture okunabilir değilse veya boyutu ayarlanacaksa geçici RenderTexture kullan
            RenderTexture rt = RenderTexture.GetTemporary(_gridWidth, _gridHeight, 0, RenderTextureFormat.ARGB32);
            rt.filterMode = FilterMode.Point;
            Graphics.Blit(_sourceTexture, rt);

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;

            Texture2D resized = new Texture2D(_gridWidth, _gridHeight, TextureFormat.RGBA32, false);
            resized.ReadPixels(new Rect(0, 0, _gridWidth, _gridHeight), 0, 0);
            resized.Apply();

            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            _convertedGrid = new PuzzleColor?[_gridWidth, _gridHeight];

            for (int y = 0; y < _gridHeight; y++)
            {
                for (int x = 0; x < _gridWidth; x++)
                {
                    int sampleX = _flipHorizontal ? (_gridWidth - 1 - x) : x;
                    int sampleY = _flipVertical ? (_gridHeight - 1 - y) : y;

                    Color c = resized.GetPixel(sampleX, sampleY);

                    // Şeffaflık filtresi
                    if (_ignoreTransparent && c.a < _alphaThreshold)
                    {
                        _convertedGrid[x, y] = null;
                        continue;
                    }

                    // Beyaz arka plan filtresi
                    if (_ignoreWhiteBackground && c.r > _whiteThreshold && c.g > _whiteThreshold && c.b > _whiteThreshold)
                    {
                        _convertedGrid[x, y] = null;
                        continue;
                    }

                    // En yakın PuzzleColor'a eşle
                    _convertedGrid[x, y] = FindClosestPuzzleColor(c, AllColors);
                }
            }

            DestroyImmediate(resized);
            LimitPaletteColors();
            UpdateColorCounts();
            Repaint();
        }

        // En sık kullanılan renkleri korur, kalan pikselleri bunların en yakınına taşır
        private void LimitPaletteColors()
        {
            UpdateColorCounts();
            if (_colorCounts.Count <= _maxPaletteColors) return;

            var keptColors = new List<PuzzleColor>(_colorCounts.Keys);
            keptColors.Sort((a, b) => _colorCounts[b].CompareTo(_colorCounts[a]));
            keptColors.RemoveRange(_maxPaletteColors, keptColors.Count - _maxPaletteColors);

            for (int y = 0; y < _gridHeight; y++)
            {
                for (int x = 0; x < _gridWidth; x++)
                {
                    var c = _convertedGrid[x, y];
                    if (!c.HasValue || keptColors.Contains(c.Value)) continue;

                    _convertedGrid[x, y] = FindClosestPuzzleColor(PuzzleColorPalette.GetCubeColor(c.Value), keptColors);
                }
            }
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

        private PuzzleColor FindClosestPuzzleColor(Color target, IEnumerable<PuzzleColor> candidates)
        {
            PuzzleColor bestColor = PuzzleColor.Blue;
            float minDistance = float.MaxValue;

            foreach (PuzzleColor pc in candidates)
            {
                Color candidate = PuzzleColorPalette.GetCubeColor(pc);

                // Ağırlıklı algısal renk mesafesi (Perceptual RGB Distance)
                float dr = (target.r - candidate.r) * 0.30f;
                float dg = (target.g - candidate.g) * 0.59f;
                float db = (target.b - candidate.b) * 0.11f;
                float dist = dr * dr + dg * dg + db * db;

                if (dist < minDistance)
                {
                    minDistance = dist;
                    bestColor = pc;
                }
            }

            return bestColor;
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

        private void GenerateSandInChamber()
        {
            if (_convertedGrid == null) return;

            var glass = FindFirstObjectByType<ShapedGlassContainer>();
            if (glass == null)
            {
                EditorUtility.DisplayDialog("Cam Hazne Eksik", "Lütfen sahneye bir ShapedGlassContainer yerleştirin veya Glass Shape Editor ile oluşturun.", "Tamam");
                return;
            }

            // SandSimulationEngine ve SandMonsterFlowBridge bileşenlerini doğrula
            var engine = glass.GetComponent<SandSimulationEngine>().OrNull() ?? glass.gameObject.AddComponent<SandSimulationEngine>();
            var bridge = glass.GetComponent<SandMonsterFlowBridge>().OrNull() ?? glass.gameObject.AddComponent<SandMonsterFlowBridge>();

            engine.glassContainer = glass;
            bridge.sandEngine = engine;
            bridge.glassContainer = glass;

            // Hazneye otomatik sığdırma ve ortalama hesapla
            ApplyAutoSpacing(glass);

            glass.customPattern = PixelArtPattern.FromColorGrid(_levelSaveName, _convertedGrid);
            glass.pixelArtPreset = PixelArtPreset.Custom;
            glass.particleMode = ParticleArrangementMode.PixelArtVisual;
            glass.RebuildContainer();

            Selection.activeGameObject = glass.gameObject;
            SceneView.RepaintAll();
            EditorUtility.SetDirty(glass);
            EditorUtility.SetDirty(engine);

            Debug.Log($"<color=#00E5FF>★ SAND IMAGE GENERATED:</color> {_convertedGrid.GetLength(0)}x{_convertedGrid.GetLength(1)} grid, {engine.sandAmount} kum tanesi hazneye yerleştirildi.");
        }

        private static float GetSandCenterY(ShapedGlassContainer glass)
        {
            return (glass.spoutBottomY + 0.85f + glass.neckTopY) * 0.5f;
        }

        private void ApplyAutoSpacing(ShapedGlassContainer glass)
        {
            if (!_autoCalculateSpacing) return;

            glass.GetSafeCubeBoundsAtY(GetSandCenterY(glass), out float safeLeft, out float safeRight);
            float availableWidth = (safeRight - safeLeft) * 0.90f;
            _particleSpacing = Mathf.Clamp(availableWidth / Mathf.Max(1, _gridWidth), 0.10f, 0.22f);
            _particleSize = _particleSpacing * 0.95f;
        }

        private void SyncMonstersWithSandColors()
        {
            UpdateColorCounts();
            if (_colorCounts.Count == 0) return;

            var gm = PuzzleGlassGameManager.Instance.OrNull() ?? FindFirstObjectByType<PuzzleGlassGameManager>();
            if (gm == null)
            {
                EditorUtility.DisplayDialog("GameManager Eksik", "Sahnede PuzzleGlassGameManager bulunamadı.", "Tamam");
                return;
            }

            // Piksel şablonunu oluştur
            PixelArtPattern pattern = PixelArtPattern.FromColorGrid(_levelSaveName, _convertedGrid);

            gm.SynchronizeMonstersForPattern(pattern);
            SceneView.RepaintAll();
            Debug.Log("<color=#A066FF>★ MONSTERS SYNCHRONIZED:</color> Canavarlar kum renk ve adetlerine göre oluşturuldu!");
        }

        private void SaveCurrentSetupAsLevel()
        {
            if (_convertedGrid == null) return;

            // Level Design Studio seviyeleri bu klasörden okur
            string folder = "Assets/Levels";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "Levels");

            string assetPath = $"{folder}/{_levelSaveName}.asset";

            var levelData = AssetDatabase.LoadAssetAtPath<PuzzleGlassLevelData>(assetPath);
            bool isNew = false;
            if (levelData == null)
            {
                levelData = CreateInstance<PuzzleGlassLevelData>();
                levelData.levelNumber = AssetDatabase.FindAssets("t:PuzzleGlassLevelData", new[] { folder }).Length + 1;
                isNew = true;
            }

            var glass = FindFirstObjectByType<ShapedGlassContainer>();
            if (glass != null)
            {
                ApplyAutoSpacing(glass);
                levelData.glassPreset = glass.shapePreset;
                levelData.widthScale = glass.widthScale;
                levelData.heightScale = glass.heightScale;
                levelData.glassBodyColor = glass.glassBodyColor;
                levelData.contourPoints = new List<GlassContourPoint>(glass.contourPoints);
            }

            levelData.levelName = _levelSaveName;
            levelData.particleSize = _particleSize;
            levelData.gridCellSpacing = _particleSpacing;
            levelData.particleMode = ParticleArrangementMode.PixelArtVisual;
            levelData.pixelArtPreset = PixelArtPreset.Custom;
            levelData.initialSimulationMode = SandSimulationMode.StaticImage;

            levelData.customPattern = PixelArtPattern.FromColorGrid(_levelSaveName, _convertedGrid);

            // Canavar hedeflerini kaydet
            levelData.monsters.Clear();
            foreach (var kvp in _colorCounts)
            {
                levelData.monsters.Add(new LevelMonsterEntry(kvp.Key, kvp.Value));
            }

            if (isNew)
            {
                AssetDatabase.CreateAsset(levelData, assetPath);
            }
            else
            {
                EditorUtility.SetDirty(levelData);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = levelData;

            EditorUtility.DisplayDialog("Seviye Kaydedildi!", $"Seviye başarıyla kaydedildi:\n{assetPath}", "Harika");
        }

        private void LoadLevelFromAsset()
        {
            string path = EditorUtility.OpenFilePanel("Seviye Dosyası Seç (.asset)", "Assets/Levels", "asset");
            if (string.IsNullOrEmpty(path)) return;

            if (path.StartsWith(Application.dataPath))
            {
                string relPath = "Assets" + path.Substring(Application.dataPath.Length);
                var levelData = AssetDatabase.LoadAssetAtPath<PuzzleGlassLevelData>(relPath);
                if (levelData != null)
                {
                    var pattern = PixelArtLibrary.Resolve(levelData.pixelArtPreset, levelData.customPattern);
                    _gridWidth = pattern.width;
                    _gridHeight = pattern.height;
                    _convertedGrid = pattern.GetColorGrid();
                    _levelSaveName = levelData.levelName;
                    UpdateColorCounts();
                    Repaint();
                }
            }
        }
    }
}
