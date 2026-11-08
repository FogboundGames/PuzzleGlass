using System;
using System.Collections.Generic;
using UnityEngine;

namespace PuzzleGlass
{
    public enum SandSimulationMode
    {
        StaticImage = 0,    // Kumlar görsel düzeninde sabit durur, bozulmaz ve düşmez
        DynamicPhysics = 1, // Kumlar fizik simülasyonuna katılır (yerçekimi, yığılma, akış)
        Preview = 2         // Editor içerisinde test için önizleme simülasyonu
    }

    public enum PhysicsQuality
    {
        Fast = 1,           // 1 alt-adım (Düşük donanımlı mobil cihazlar)
        Balanced = 3,       // 3 alt-adım (Dengeli mobil performans ve pürüzsüzlük)
        HighQuality = 5     // 5 alt-adım (Masaüstü ve yüksek kalite testler)
    }

    [System.Serializable]
    public struct SandParticle
    {
        public int id;
        public Vector2 position;
        public Vector2 prevPosition;
        public Vector2 velocity;
        public Vector2 staticHomePos;
        public PuzzleColor color;
        public Color renderColor;
        public bool isStatic;
        public bool isDraining;
        public float angle;
        public float angularVelocity;
        public float depthZ;
        public bool isBackground;
        public bool isFlowing;
        public bool isFilling;
        public float fillDelay;
        public int cellX;
        public int cellY;
    }

    [ExecuteAlways]
    public class SandSimulationEngine : MonoBehaviour
    {
        public static SandSimulationEngine Instance { get; private set; }

        [Header("References")]
        public ShapedGlassContainer glassContainer;
        public BallContainerManager containerPhysics;

        [Header("Phase 2 – Sand Physics Settings")]
        [Range(0.08f, 0.28f)]
        public float particleSize = 0.16f;

        [Range(0.2f, 3.0f)]
        public float sandDensity = 1.0f;

        [Range(-30.0f, -1.0f)]
        public float gravity = -14.0f;

        [Range(0.2f, 3.0f)]
        public float flowSpeed = 1.0f;

        [Range(0.1f, 3.0f)]
        public float horizontalMovement = 1.25f;

        [Range(1, 6)]
        public int simulationResolution = 3;

        public PhysicsQuality physicsQuality = PhysicsQuality.Balanced;

        [Header("Simulation State")]
        public SandSimulationMode simulationMode = SandSimulationMode.StaticImage;
        public bool isGateOpen = false;

        [Tooltip("Aktif kum parçacığı sayısı")]
        public int sandAmount = 0;

        [Header("Kum Görseli")]
        [Tooltip("Her desen pikselinin bir kenarındaki kum tanesi sayısı")]
        [Range(1, 4)]
        public int grainsPerPixel = 3;
        public bool fillBackground = true;
        public Color backgroundSandColor = new Color(0.96f, 0.83f, 0.52f, 1f);
        public float settleStepInterval = 0.03f;
        public float settleGrainSpeed = 3.5f;
        public float flowGrainSpeed = 6f;
        public float fillGrainSpeed = 9f;

        [Tooltip("Görselin bariyer arkasında kilitli duran üst oranı (0 = kilit yok)")]
        [Range(0f, 0.6f)]
        public float lockedSectionRatio = 0.3f;
        public Color lockBarrierColor = new Color(0.36f, 0.69f, 0.97f, 1f);

        public bool HasLockedSection => _lockRow >= 0;

        /// <summary>Canavar sayacını bir düşürmek için gereken kum tanesi sayısı.</summary>
        public int GrainsPerCount { get; private set; } = 1;

        // Partikül Listesi
        [SerializeField]
        private List<SandParticle> _particles = new List<SandParticle>();

        // Olaylar (Events)
        public event Action<SandParticle> OnParticleDrainedFromSpout;
        public event Action OnAllSandCleared;
        public event Action OnSandRebuilt;

        // GPU Instancing Verileri
        private Mesh _particleMesh;
        private Dictionary<PuzzleColor, Material> _materials = new Dictionary<PuzzleColor, Material>();
        private Dictionary<PuzzleColor, List<Matrix4x4>> _batches = new Dictionary<PuzzleColor, List<Matrix4x4>>();
        private static readonly Matrix4x4[] _tempMatrixArray = new Matrix4x4[1023];

        // Mekânsal Izgara (Spatial Grid) - Sıfır GC Tahsisi
        private const int GridHashSize = 4096;
        private readonly int[] _gridHead = new int[GridHashSize];
        private int[] _gridNext = new int[2048];
        private float _lastCellSize = 0.18f;

        // Kum Izgarası (hücre başına parçacık indeksi, boşsa -1)
        private const float MaxGrainSpacing = 0.14f;
        private const float MinGrainSpacing = 0.05f;
        private const float LayerGrainSpacing = 0.085f;
        private int _cols;
        private int _rows;
        private float _originX;
        private float _originY;
        private float _floorY;
        private float _cellSpacing;
        private int[] _cellGrains;
        private bool[] _validCells;
        private float _settleTimer;
        private int _settleTick;
        private int _lockRow = -1; // Bu satır ve üstü kilitli
        private Material _lockBarrierMaterial;
        private readonly List<Matrix4x4> _lockBarrierBatch = new List<Matrix4x4>(64);
        private Material _backgroundMaterial;
        private readonly List<Matrix4x4> _backgroundBatch = new List<Matrix4x4>(1024);

        private void Awake()
        {
            Instance = this;
            EnsureReferences();
            EnsureMeshAndMaterials();
        }

        private void OnEnable()
        {
            Instance = this;
            EnsureReferences();
            EnsureMeshAndMaterials();
        }

        private void EnsureReferences()
        {
            if (glassContainer == null)
            {
                glassContainer = GetComponent<ShapedGlassContainer>().OrNull() ?? FindFirstObjectByType<ShapedGlassContainer>();
            }
            if (containerPhysics == null)
            {
                containerPhysics = GetComponent<BallContainerManager>().OrNull() ?? FindFirstObjectByType<BallContainerManager>();
            }
        }

        private void EnsureMeshAndMaterials()
        {
            if (_particleMesh == null)
            {
                // Defterdeki ve referans görseldeki gibi tatlı, yuvarlatılmış şeker küpü (Candy Cube)
                _particleMesh = ProceduralMeshUtils.CreateRoundedCube(0.16f, 0.045f, 3);
            }

            foreach (PuzzleColor col in Enum.GetValues(typeof(PuzzleColor)))
            {
                if (!_materials.ContainsKey(col) || _materials[col] == null)
                {
                    _materials[col] = PuzzleColorUtility.GetMaterial(col);
                }
                if (!_batches.ContainsKey(col))
                {
                    _batches[col] = new List<Matrix4x4>(256);
                }
            }

            if (_lockBarrierMaterial == null)
            {
                _lockBarrierMaterial = PuzzleColorPalette.CreateCandyCubeMaterial(PuzzleColor.Blue, "Mat_SandLockBarrier");
                _lockBarrierMaterial.color = lockBarrierColor;
                if (_lockBarrierMaterial.HasProperty("_BaseColor")) _lockBarrierMaterial.SetColor("_BaseColor", lockBarrierColor);
            }

            if (_backgroundMaterial == null)
            {
                _backgroundMaterial = PuzzleColorPalette.CreateCandyCubeMaterial(PuzzleColor.Yellow, "Mat_SandBackground");
                _backgroundMaterial.color = backgroundSandColor;
                if (_backgroundMaterial.HasProperty("_BaseColor")) _backgroundMaterial.SetColor("_BaseColor", backgroundSandColor);
                if (_backgroundMaterial.HasProperty("_ShadowColor")) _backgroundMaterial.SetColor("_ShadowColor", backgroundSandColor * 0.82f);
                if (_backgroundMaterial.HasProperty("_HighlightColor")) _backgroundMaterial.SetColor("_HighlightColor", Color.Lerp(backgroundSandColor, Color.white, 0.45f));
            }
        }

        public void SetSimulationMode(SandSimulationMode mode)
        {
            simulationMode = mode;
            if (mode == SandSimulationMode.StaticImage)
            {
                // Kumları orijinal piksel sanatındaki konumlarına ve açılarına geri sabitle
                for (int i = 0; i < _particles.Count; i++)
                {
                    var p = _particles[i];
                    p.position = p.staticHomePos;
                    p.prevPosition = p.staticHomePos;
                    p.velocity = Vector2.zero;
                    p.angle = 0f;
                    p.angularVelocity = 0f;
                    p.isDraining = false;
                    _particles[i] = p;
                }
            }
        }

        public void SetSpoutGate(bool open)
        {
            isGateOpen = open;
            if (glassContainer != null)
            {
                glassContainer.SetSpoutGate(open);
            }
        }

        public void ToggleSpoutGate()
        {
            SetSpoutGate(!isGateOpen);
        }

        public void ClearAll()
        {
            _particles.Clear();
            _cellGrains = null;
            _lockRow = -1;
            sandAmount = 0;
            OnAllSandCleared?.Invoke();
        }

        public List<SandParticle> GetParticles() => _particles;
        public int GetParticleCount() => _particles.Count;

        public Dictionary<PuzzleColor, int> GetColorCounts()
        {
            var counts = new Dictionary<PuzzleColor, int>();
            for (int i = 0; i < _particles.Count; i++)
            {
                if (_particles[i].isDraining || _particles[i].isBackground) continue;
                var c = _particles[i].color;
                if (!counts.ContainsKey(c)) counts[c] = 0;
                counts[c]++;
            }
            return counts;
        }

        /// <summary>
        /// Deseni kum tanelerine çevirir: her desen pikseli grainsPerPixel x grainsPerPixel tane olur,
        /// haznenin kalanı arka plan kumuyla dolar.
        /// </summary>
        public void BuildFromPattern(PixelArtPattern pattern)
        {
            if (pattern == null || !PrepareBuild()) return;

            PuzzleColor?[,] art = pattern.GetColorGrid();
            FitArtInChamber(art, pattern.width, pattern.height, out int scale, out int artCol, out int artRow);
            GrainsPerCount = scale * scale;

            int artW = pattern.width * scale;
            int artH = pattern.height * scale;

            for (int row = 0; row < _rows; row++)
            {
                for (int col = 0; col < _cols; col++)
                {
                    bool inArt = col >= artCol && col < artCol + artW && row >= artRow && row < artRow + artH;
                    PuzzleColor? color = inArt ? art[(col - artCol) / scale, (row - artRow) / scale] : null;

                    if (color.HasValue) AddGrain(col, row, color.Value, false);
                    else if (fillBackground && _validCells[CellIndex(col, row)]) AddGrain(col, row, PuzzleColor.Yellow, true);
                }
            }

            FinishBuild();
        }

        /// <summary>
        /// Desensiz level'lar için canavar renklerini cam katmanı olarak dizer (ilk canavar en altta).
        /// </summary>
        public void BuildFromLayers(List<LevelMonsterEntry> layers)
        {
            if (layers == null || !PrepareBuild()) return;

            LayoutChamber(LayerGrainSpacing, 0, 0);

            var cells = new List<int>(_cols * _rows);
            for (int i = 0; i < _validCells.Length; i++)
            {
                if (_validCells[i]) cells.Add(i);
            }
            cells.Sort((a, b) => glassContainer.EvaluateLayerSortScore(CellCenter(a % _cols, a / _cols))
                .CompareTo(glassContainer.EvaluateLayerSortScore(CellCenter(b % _cols, b / _cols))));

            int totalCount = 0;
            for (int i = 0; i < layers.Count; i++) totalCount += layers[i].count;
            GrainsPerCount = Mathf.Max(1, Mathf.FloorToInt(cells.Count * 0.85f / Mathf.Max(1, totalCount)));

            var assigned = new PuzzleColor?[_validCells.Length];
            int cursor = 0;
            for (int i = 0; i < layers.Count; i++)
            {
                int grains = layers[i].count * GrainsPerCount;
                for (int g = 0; g < grains && cursor < cells.Count; g++)
                {
                    assigned[cells[cursor++]] = layers[i].color;
                }
            }

            for (int row = 0; row < _rows; row++)
            {
                for (int col = 0; col < _cols; col++)
                {
                    int cell = CellIndex(col, row);
                    if (assigned[cell].HasValue) AddGrain(col, row, assigned[cell].Value, false);
                    else if (fillBackground && _validCells[cell]) AddGrain(col, row, PuzzleColor.Yellow, true);
                }
            }

            FinishBuild();
        }

        public void InitializeFromPattern(PixelArtPattern pattern, float spacing = -1f)
        {
            BuildFromPattern(pattern);
        }

        private bool PrepareBuild()
        {
            EnsureReferences();
            EnsureMeshAndMaterials();
            _particles.Clear();
            _cellGrains = null;
            _lockRow = -1;

            if (glassContainer == null) return false;
            glassContainer.EnsureContourInitialized();
            return glassContainer.contourPoints != null && glassContainer.contourPoints.Count >= 3;
        }

        private void FinishBuild()
        {
            sandAmount = _particles.Count;
            particleSize = _cellSpacing;
            simulationMode = SandSimulationMode.StaticImage;
            ApplySectionLock();
            OnSandRebuilt?.Invoke();
        }

        private void ApplySectionLock()
        {
            if (lockedSectionRatio <= 0f) return;

            int minRow = int.MaxValue;
            int maxRow = -1;
            for (int i = 0; i < _particles.Count; i++)
            {
                if (_particles[i].isBackground) continue;
                minRow = Mathf.Min(minRow, _particles[i].cellY);
                maxRow = Mathf.Max(maxRow, _particles[i].cellY);
            }

            if (maxRow < 0) return;

            int lockedRows = Mathf.RoundToInt((maxRow - minRow + 1) * lockedSectionRatio);
            if (lockedRows > 0) _lockRow = maxRow + 1 - lockedRows;
        }

        public void UnlockSection()
        {
            _lockRow = -1;
        }

        /// <summary>
        /// Kilit altındaki, henüz çıkışa yollanmamış renkli tane sayısı.
        /// </summary>
        public int CountReachableArtGrains()
        {
            if (_cellGrains == null) return 0;

            int rowLimit = HasLockedSection ? _lockRow : _rows;
            int count = 0;
            for (int cell = 0; cell < rowLimit * _cols; cell++)
            {
                int grain = _cellGrains[cell];
                if (grain >= 0 && !_particles[grain].isBackground) count++;
            }
            return count;
        }

        private void FitArtInChamber(PuzzleColor?[,] art, int width, int height, out int scale, out int artCol, out int artRow)
        {
            for (scale = Mathf.Max(1, grainsPerPixel); scale >= 1; scale--)
            {
                for (float spacing = MaxGrainSpacing; spacing >= MinGrainSpacing; spacing -= 0.005f)
                {
                    LayoutChamber(spacing, width * scale, 0);
                    PlaceArt(width, height, scale, out artCol, out artRow);
                    if (ArtFits(art, width, height, scale, artCol, artRow)) return;
                }
            }

            // Hiçbir boyut sığmadı: en küçük ızgarada tüm pikseller yine de yerleşir
            scale = 1;
            LayoutChamber(MinGrainSpacing, width, height);
            PlaceArt(width, height, scale, out artCol, out artRow);
            artRow = Mathf.Clamp(artRow, 0, _rows - height);
        }

        private void PlaceArt(int width, int height, int scale, out int artCol, out int artRow)
        {
            artCol = (_cols - width * scale) / 2;
            float centerRow = (glassContainer.CalculateChamberCenterY() - _originY) / _cellSpacing;
            artRow = Mathf.RoundToInt(centerRow - (height * scale - 1) * 0.5f);
        }

        private bool ArtFits(PuzzleColor?[,] art, int width, int height, int scale, int artCol, int artRow)
        {
            if (artRow < 0 || artRow + height * scale > _rows) return false;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!art[x, y].HasValue) continue;

                    for (int sy = 0; sy < scale; sy++)
                    {
                        for (int sx = 0; sx < scale; sx++)
                        {
                            if (!_validCells[CellIndex(artCol + x * scale + sx, artRow + y * scale + sy)]) return false;
                        }
                    }
                }
            }
            return true;
        }

        private void LayoutChamber(float spacing, int minCols, int minRows)
        {
            var points = glassContainer.contourPoints;
            float heightScale = glassContainer.heightScale;
            bool flatBox = glassContainer.shapePreset == GlassShapePreset.SquareBox || glassContainer.shapePreset == GlassShapePreset.RectangleBox;

            _floorY = points[flatBox ? 0 : 1].y * heightScale;
            float topY = points[flatBox ? points.Count - 1 : points.Count - 2].y * heightScale;

            _cellSpacing = spacing;
            _originY = _floorY + spacing * 0.5f;
            _rows = Mathf.Max(Mathf.Max(1, minRows), Mathf.FloorToInt((topY - _floorY) / spacing));

            float widest = 0f;
            for (int row = 0; row < _rows; row++)
            {
                glassContainer.GetSafeCubeBoundsAtY(_originY + row * spacing, out float left, out float right);
                widest = Mathf.Max(widest, right - left);
            }

            int cols = Mathf.Max(minCols, Mathf.CeilToInt(widest / spacing));
            if ((cols - minCols) % 2 != 0) cols++;
            _cols = Mathf.Max(1, cols);
            _originX = GetSpoutCenterX() - (_cols - 1) * 0.5f * spacing;

            int cellCount = _cols * _rows;
            _validCells = new bool[cellCount];
            _cellGrains = new int[cellCount];
            Array.Fill(_cellGrains, -1);

            float halfGrain = spacing * 0.45f;
            for (int row = 0; row < _rows; row++)
            {
                float y = _originY + row * spacing;
                glassContainer.GetSafeCubeBoundsAtY(y, out float left, out float right);

                for (int col = 0; col < _cols; col++)
                {
                    float x = _originX + col * spacing;
                    _validCells[CellIndex(col, row)] = x - halfGrain >= left && x + halfGrain <= right;
                }
            }
        }

        private float GetSpoutCenterX()
        {
            if (glassContainer == null || glassContainer.symmetricMode || glassContainer.contourPoints == null || glassContainer.contourPoints.Count == 0)
            {
                return 0f;
            }
            return glassContainer.contourPoints[0].centerX * glassContainer.widthScale;
        }

        private int CellIndex(int col, int row) => row * _cols + col;

        private Vector2 CellCenter(int col, int row) => new Vector2(_originX + col * _cellSpacing, _originY + row * _cellSpacing);

        private void AddGrain(int col, int row, PuzzleColor color, bool isBackground)
        {
            Vector2 pos = CellCenter(col, row);
            int cell = CellIndex(col, row);
            _validCells[cell] = true;
            _cellGrains[cell] = _particles.Count;

            _particles.Add(new SandParticle
            {
                id = _particles.Count,
                position = pos,
                prevPosition = pos,
                staticHomePos = pos,
                color = color,
                renderColor = isBackground ? backgroundSandColor : PuzzleColorPalette.GetCubeColor(color),
                isStatic = true,
                isBackground = isBackground,
                cellX = col,
                cellY = row,
                depthZ = -0.10f + ((col + row) % 3 - 1) * 0.008f
            });
        }

        /// <summary>
        /// Kumu üst açıklıktan döker; taneler alttan üste doğru yerlerine oturur.
        /// </summary>
        public void PlayFillAnimation(float totalDuration = 1.6f)
        {
            if (_cellGrains == null || _particles.Count == 0) return;

            Vector2 inlet = new Vector2(GetSpoutCenterX(), glassContainer.neckTopY);
            for (int i = 0; i < _particles.Count; i++)
            {
                var p = _particles[i];
                p.position = inlet;
                p.isFilling = true;
                p.fillDelay = (float)i / _particles.Count * totalDuration;
                _particles[i] = p;
            }
        }

        /// <summary>
        /// Bu renkteki, çıkışa en yakın taneyi serbest bırakıp çıkışa yollar.
        /// </summary>
        public bool TryReleaseGrain(PuzzleColor color)
        {
            if (_cellGrains == null) return false;

            int middle = _cols / 2;
            int rowLimit = HasLockedSection ? _lockRow : _rows;
            for (int row = 0; row < rowLimit; row++)
            {
                int bestCol = -1;
                for (int col = 0; col < _cols; col++)
                {
                    int grain = _cellGrains[CellIndex(col, row)];
                    if (grain < 0) continue;

                    var p = _particles[grain];
                    if (p.isBackground || p.isFilling || p.color != color) continue;
                    if (bestCol < 0 || Mathf.Abs(col - middle) < Mathf.Abs(bestCol - middle)) bestCol = col;
                }

                if (bestCol < 0) continue;

                int cell = CellIndex(bestCol, row);
                int index = _cellGrains[cell];
                var released = _particles[index];
                released.isFlowing = true;
                released.depthZ = -0.18f;
                _particles[index] = released;
                _cellGrains[cell] = -1;
                return true;
            }

            return false;
        }

        private void StepGrainFlow(float dt)
        {
            if (_cellGrains == null) return;

            _settleTimer += dt;
            if (_settleTimer >= settleStepInterval)
            {
                _settleTimer = 0f;
                SettleGrainsOneStep();
            }

            float spoutX = GetSpoutCenterX();
            Vector2 funnelMouth = new Vector2(spoutX, _floorY);
            Vector2 exit = new Vector2(spoutX, glassContainer.spoutBottomY - 0.06f);

            for (int i = 0; i < _particles.Count; i++)
            {
                var p = _particles[i];
                if (p.isDraining) continue;

                if (p.isFilling)
                {
                    p.fillDelay -= dt;
                    if (p.fillDelay <= 0f)
                    {
                        p.position = Vector2.MoveTowards(p.position, p.staticHomePos, fillGrainSpeed * dt);
                        if (p.position == p.staticHomePos) p.isFilling = false;
                    }
                }
                else if (p.isFlowing)
                {
                    Vector2 target = p.position.y > funnelMouth.y + 0.01f ? funnelMouth : exit;
                    p.position = Vector2.MoveTowards(p.position, target, flowGrainSpeed * dt);

                    if ((p.position - exit).sqrMagnitude < 0.0001f)
                    {
                        p.isDraining = true;
                        sandAmount = Mathf.Max(0, sandAmount - 1);
                        _particles[i] = p;
                        OnParticleDrainedFromSpout?.Invoke(p);
                        continue;
                    }
                }
                else
                {
                    p.position = Vector2.MoveTowards(p.position, p.staticHomePos, settleGrainSpeed * dt);
                }

                _particles[i] = p;
            }
        }

        // Taneler boşalan hücrelere önce düz aşağı, olmazsa çapraz aşağı kayar; görsel böylece bozulmadan iner
        private void SettleGrainsOneStep()
        {
            _settleTick++;

            int rowLimit = HasLockedSection ? _lockRow : _rows;
            for (int row = 1; row < rowLimit; row++)
            {
                for (int col = 0; col < _cols; col++)
                {
                    int cell = CellIndex(col, row);
                    int grain = _cellGrains[cell];
                    if (grain < 0 || _particles[grain].isFilling) continue;

                    int side = ((_settleTick + col + row) & 1) == 0 ? 1 : -1;
                    if (TryMoveGrain(grain, cell, col, row - 1)) continue;
                    if (TryMoveGrain(grain, cell, col + side, row - 1)) continue;
                    TryMoveGrain(grain, cell, col - side, row - 1);
                }
            }
        }

        private bool TryMoveGrain(int grain, int fromCell, int col, int row)
        {
            if (col < 0 || col >= _cols) return false;

            int toCell = CellIndex(col, row);
            if (!_validCells[toCell] || _cellGrains[toCell] >= 0) return false;

            _cellGrains[fromCell] = -1;
            _cellGrains[toCell] = grain;

            var p = _particles[grain];
            p.cellX = col;
            p.cellY = row;
            p.staticHomePos = CellCenter(col, row);
            _particles[grain] = p;
            return true;
        }

        private void Update()
        {
            if (Application.isPlaying)
            {
                if (simulationMode == SandSimulationMode.DynamicPhysics)
                {
                    StepSimulation(Time.deltaTime);
                }
                else
                {
                    StepGrainFlow(Time.deltaTime);
                }
            }
            else
            {
                // Editor Önizleme Modu
                if (simulationMode == SandSimulationMode.Preview)
                {
                    StepSimulation(0.016f);
                }
            }
        }

        private void LateUpdate()
        {
            RenderInstancedSandParticles();
        }

        /// <summary>
        /// Hücre tabanlı PBD (Position-Based Dynamics) parçacık simülasyonu.
        /// </summary>
        public void StepSimulation(float dt)
        {
            if (_particles.Count == 0) return;
            EnsureReferences();

            int subSteps = (int)physicsQuality;
            if (simulationResolution > 0) subSteps = simulationResolution;
            subSteps = Mathf.Clamp(subSteps, 1, 6);

            float subDt = dt / subSteps;
            float radius = particleSize * 0.5f;
            float diameter = particleSize;
            float diameterSq = diameter * diameter;

            float spoutBottomY = (glassContainer != null) ? glassContainer.spoutBottomY : -1.20f;
            float spoutWidth = (glassContainer != null) ? glassContainer.SpoutWidth : 0.40f;
            float halfSpout = spoutWidth * 0.5f - radius * 0.5f;
            float spoutCenterX = 0f;
            if (glassContainer != null && glassContainer.contourPoints != null && glassContainer.contourPoints.Count > 0)
            {
                spoutCenterX = glassContainer.contourPoints[0].centerX;
            }

            // Izgara boyutu hazırlığı
            _lastCellSize = diameter * 1.08f;
            int pCount = _particles.Count;
            if (_gridNext.Length < pCount)
            {
                _gridNext = new int[pCount * 2];
            }

            List<int> drainedIndices = null;

            for (int step = 0; step < subSteps; step++)
            {
                // 1. Dış Kuvvetler (Yerçekimi, Hız İntegrasyonu)
                for (int i = 0; i < pCount; i++)
                {
                    var p = _particles[i];
                    if (p.isDraining) continue;

                    // Yerçekimi ivmesi
                    p.velocity.y += (gravity * sandDensity) * subDt;

                    // Yatay sönümleme (doğal kum sürtünmesi)
                    p.velocity.x *= Mathf.Pow(0.88f, subDt * 60f);

                    // Konum güncellemesi
                    p.prevPosition = p.position;
                    p.position += p.velocity * (subDt * flowSpeed);

                    // Dönme açısı
                    p.angle += p.angularVelocity * subDt;
                    p.angularVelocity *= 0.94f;

                    _particles[i] = p;
                }

                // 2. Mekânsal Izgarayı Oluştur
                Array.Fill(_gridHead, -1);
                for (int i = 0; i < pCount; i++)
                {
                    if (_particles[i].isDraining) continue;
                    int hash = GetCellHash(_particles[i].position.x, _particles[i].position.y, _lastCellSize);
                    _gridNext[i] = _gridHead[hash];
                    _gridHead[hash] = i;
                }

                // 3. Parçacık-Parçacık Çarpışması & Doğal Yığılma (Angle of Repose)
                for (int i = 0; i < pCount; i++)
                {
                    var pi = _particles[i];
                    if (pi.isDraining) continue;

                    int cx = Mathf.FloorToInt(pi.position.x / _lastCellSize);
                    int cy = Mathf.FloorToInt(pi.position.y / _lastCellSize);

                    for (int nx = -1; nx <= 1; nx++)
                    {
                        for (int ny = -1; ny <= 1; ny++)
                        {
                            int neighborHash = GetCellHashDirect(cx + nx, cy + ny);
                            int neighborIdx = _gridHead[neighborHash];

                            while (neighborIdx != -1)
                            {
                                if (neighborIdx > i)
                                {
                                    var pj = _particles[neighborIdx];
                                    if (!pj.isDraining)
                                    {
                                        Vector2 diff = pi.position - pj.position;
                                        float distSq = diff.sqrMagnitude;

                                        if (distSq < diameterSq && distSq > 0.000001f)
                                        {
                                            float dist = Mathf.Sqrt(distSq);
                                            Vector2 normal = diff / dist;
                                            float overlap = diameter - dist;

                                            // Birbirini itme
                                            Vector2 separation = normal * (overlap * 0.5f);
                                            pi.position += separation;
                                            pj.position -= separation;

                                            // Açısal momentum ve doğal kum kayması
                                            float relativeVx = pi.velocity.x - pj.velocity.x;
                                            pi.angularVelocity -= relativeVx * 12.0f;
                                            pj.angularVelocity += relativeVx * 12.0f;

                                            // Üst üste binen taneler sağa-sola doğal kayma açısı oluşturur
                                            if (Mathf.Abs(diff.x) < radius * 0.75f && diff.y > 0.005f)
                                            {
                                                float slideDir = (diff.x >= 0f) ? 1.0f : -1.0f;
                                                float slidePush = slideDir * (horizontalMovement * 0.025f * overlap);
                                                pi.position.x += slidePush;
                                                pj.position.x -= slidePush;
                                            }

                                            _particles[neighborIdx] = pj;
                                        }
                                    }
                                }
                                neighborIdx = _gridNext[neighborIdx];
                            }
                        }
                    }
                    _particles[i] = pi;
                }

                // 4. Cam Duvar Sınırları ve Alt Çıkış Sınır Kontrolü
                for (int i = 0; i < pCount; i++)
                {
                    var p = _particles[i];
                    if (p.isDraining) continue;

                    // Yan cam duvar sınırları
                    if (glassContainer != null)
                    {
                        glassContainer.GetSafeCubeBoundsAtY(p.position.y, out float safeLeft, out float safeRight);
                        safeLeft += radius;
                        safeRight -= radius;

                        if (p.position.x < safeLeft)
                        {
                            p.position.x = safeLeft;
                            if (p.velocity.x < 0f) p.velocity.x = 0f;
                        }
                        else if (p.position.x > safeRight)
                        {
                            p.position.x = safeRight;
                            if (p.velocity.x > 0f) p.velocity.x = 0f;
                        }
                    }

                    // Alt Taban & Çıkış Boğazı
                    float exitThresholdY = spoutBottomY - 0.06f;

                    if (!isGateOpen)
                    {
                        // Kapı kapalıyken kumlar tabanda durur
                        float floorY = spoutBottomY + radius + 0.02f;
                        if (p.position.y < floorY)
                        {
                            p.position.y = floorY;
                            p.velocity.y = 0f;
                            p.velocity.x *= 0.70f;
                        }
                    }
                    else
                    {
                        // Kapı açıkken çıkış kanalından aşağı dökülebilir
                        bool insideSpoutChannel = Mathf.Abs(p.position.x - spoutCenterX) <= halfSpout;

                        if (insideSpoutChannel)
                        {
                            // Çıkış ağzından dökülen kum parçacığı algılandı
                            if (p.position.y <= exitThresholdY)
                            {
                                p.isDraining = true;
                                if (drainedIndices == null) drainedIndices = new List<int>();
                                drainedIndices.Add(i);
                            }
                        }
                        else
                        {
                            // Spout yanlarındaki hunimsi yamaçlar kumu merkeze doğru kaydırır
                            float funnelFloorY = spoutBottomY + 0.35f;
                            if (p.position.y < funnelFloorY)
                            {
                                p.position.y = funnelFloorY;
                                p.position.x += Mathf.Sign(spoutCenterX - p.position.x) * (0.015f * horizontalMovement);
                                p.velocity.y = 0f;
                            }
                        }
                    }

                    // Hızı güncellenen konum farkından türet
                    p.velocity = (p.position - p.prevPosition) / subDt;
                    _particles[i] = p;
                }
            }

            // Çıkıştan dökülen parçacıkları teslim et ve listeden kaldır
            if (drainedIndices != null && drainedIndices.Count > 0)
            {
                // Ters sırada sil
                drainedIndices.Sort((a, b) => b.CompareTo(a));
                for (int k = 0; k < drainedIndices.Count; k++)
                {
                    int idx = drainedIndices[k];
                    SandParticle drained = _particles[idx];
                    _particles.RemoveAt(idx);
                    OnParticleDrainedFromSpout?.Invoke(drained);
                }
                sandAmount = _particles.Count;
            }
        }

        private static int GetCellHash(float x, float y, float cellSize)
        {
            int cx = Mathf.FloorToInt(x / cellSize);
            int cy = Mathf.FloorToInt(y / cellSize);
            return GetCellHashDirect(cx, cy);
        }

        private static int GetCellHashDirect(int cx, int cy)
        {
            uint hash = (uint)((cx * 73856093) ^ (cy * 19349663));
            return (int)(hash % GridHashSize);
        }

        /// <summary>
        /// GPU Instancing ile binlerce kum tanesini tek bir draw call içinde çizer.
        /// </summary>
        private void RenderInstancedSandParticles()
        {
            if (_particles == null || _particles.Count == 0 || _particleMesh == null) return;
            EnsureMeshAndMaterials();

            // Renk gruplarını temizle
            foreach (var kvp in _batches)
            {
                kvp.Value.Clear();
            }
            _backgroundBatch.Clear();

            Vector3 scale = Vector3.one * (particleSize / 0.16f);

            // Parçacık konumları cam haznenin yerel uzayında tutulur
            Matrix4x4 localToWorld = glassContainer != null ? glassContainer.transform.localToWorldMatrix : Matrix4x4.identity;

            // Matrisleri hazırla
            for (int i = 0; i < _particles.Count; i++)
            {
                var p = _particles[i];
                if (p.isDraining || (p.isFilling && p.fillDelay > 0f)) continue;

                Vector3 pos = new Vector3(p.position.x, p.position.y, p.depthZ);
                Quaternion rot = Quaternion.Euler(0f, 0f, p.angle);
                Matrix4x4 m = localToWorld * Matrix4x4.TRS(pos, rot, scale);

                if (p.isBackground)
                {
                    _backgroundBatch.Add(m);
                }
                else if (_batches.TryGetValue(p.color, out var list))
                {
                    list.Add(m);
                }
            }

            // Her renk grubunu DrawMeshInstanced ile GPU'ya gönder (maksimum 1023'lük dilimler)
            CollectLockBarrier(localToWorld);
            DrawBatch(_backgroundMaterial, _backgroundBatch);
            DrawBatch(_lockBarrierMaterial, _lockBarrierBatch);
            foreach (var kvp in _batches)
            {
                if (_materials.TryGetValue(kvp.Key, out var mat)) DrawBatch(mat, kvp.Value);
            }
        }

        private void CollectLockBarrier(Matrix4x4 localToWorld)
        {
            _lockBarrierBatch.Clear();
            if (!HasLockedSection || _validCells == null) return;

            float meshScale = _cellSpacing / 0.16f;
            Vector3 barScale = new Vector3(meshScale * 1.05f, meshScale * 0.45f, meshScale);
            float y = _originY + (_lockRow - 0.5f) * _cellSpacing;

            for (int col = 0; col < _cols; col++)
            {
                if (!_validCells[CellIndex(col, _lockRow)]) continue;

                Vector3 pos = new Vector3(_originX + col * _cellSpacing, y, -0.16f);
                _lockBarrierBatch.Add(localToWorld * Matrix4x4.TRS(pos, Quaternion.identity, barScale));
            }
        }

        private void DrawBatch(Material mat, List<Matrix4x4> list)
        {
            if (mat == null) return;

            int count = list.Count;
            int offset = 0;
            while (offset < count)
            {
                int batchSize = Mathf.Min(count - offset, 1023);
                list.CopyTo(offset, _tempMatrixArray, 0, batchSize);

                Graphics.DrawMeshInstanced(
                    _particleMesh,
                    0,
                    mat,
                    _tempMatrixArray,
                    batchSize,
                    null,
                    UnityEngine.Rendering.ShadowCastingMode.Off,
                    false);

                offset += batchSize;
            }
        }
    }
}
