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

        // Partikül Listesi
        [SerializeField]
        private List<SandParticle> _particles = new List<SandParticle>();

        // Olaylar (Events)
        public event Action<SandParticle> OnParticleDrainedFromSpout;
        public event Action OnAllSandCleared;

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
                glassContainer = GetComponent<ShapedGlassContainer>() ?? FindFirstObjectByType<ShapedGlassContainer>();
            }
            if (containerPhysics == null)
            {
                containerPhysics = GetComponent<BallContainerManager>() ?? FindFirstObjectByType<BallContainerManager>();
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
                var c = _particles[i].color;
                if (!counts.ContainsKey(c)) counts[c] = 0;
                counts[c]++;
            }
            return counts;
        }

        /// <summary>
        /// 2D Renk matrisinden (Pixel Art) kum tanelerini cam hazneye simetrik ve sabit olarak yerleştirir.
        /// </summary>
        public void InitializeFromGrid(PuzzleColor?[,] grid, float spacing = -1f, float startY = -999f, float centerX = 0f)
        {
            EnsureReferences();
            EnsureMeshAndMaterials();
            _particles.Clear();

            if (grid == null) return;

            int width = grid.GetLength(0);
            int height = grid.GetLength(1);

            float cellSpacing = (spacing > 0.05f) ? spacing : particleSize * 1.04f;

            // Hazne sınırlarını kontrol et ve dikey olarak tam ortala
            float safeCenterY = 0.5f;
            if (glassContainer != null)
            {
                float midChamber = (glassContainer.spoutBottomY + 0.90f + glassContainer.neckTopY) * 0.5f;
                safeCenterY = (startY > -900f) ? startY : midChamber;
                if (glassContainer.contourPoints != null && glassContainer.contourPoints.Count > 0)
                {
                    centerX = glassContainer.contourPoints[0].centerX;
                }
            }

            float halfW = (width - 1) * 0.5f * cellSpacing;
            float halfH = (height - 1) * 0.5f * cellSpacing;

            int nextId = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    PuzzleColor? c = grid[x, y];
                    if (!c.HasValue) continue;

                    float px = centerX - halfW + (x * cellSpacing);
                    float py = safeCenterY - halfH + (y * cellSpacing);

                    // Ön-arka hafif doğal varyasyon (-0.10f merkezli)
                    float depthZ = -0.10f + ((x + y) % 3 - 1) * 0.008f;

                    SandParticle p = new SandParticle
                    {
                        id = nextId++,
                        position = new Vector2(px, py),
                        prevPosition = new Vector2(px, py),
                        velocity = Vector2.zero,
                        staticHomePos = new Vector2(px, py),
                        color = c.Value,
                        renderColor = PuzzleColorPalette.GetCubeColor(c.Value),
                        isStatic = true,
                        isDraining = false,
                        angle = 0f,
                        angularVelocity = 0f,
                        depthZ = depthZ
                    };

                    _particles.Add(p);
                }
            }

            sandAmount = _particles.Count;
            simulationMode = SandSimulationMode.StaticImage;
        }

        /// <summary>
        /// Hazır PixelArtPattern şablonundan kumları oluşturur.
        /// </summary>
        public void InitializeFromPattern(PixelArtPattern pattern, float spacing = -1f)
        {
            if (pattern == null) return;
            var grid = pattern.GetColorGrid();
            InitializeFromGrid(grid, spacing);
        }

        private void Update()
        {
            if (Application.isPlaying)
            {
                if (simulationMode == SandSimulationMode.DynamicPhysics)
                {
                    StepSimulation(Time.deltaTime);
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
                Array.Clear(_gridHead, 0, GridHashSize);
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

            Vector3 scale = Vector3.one * (particleSize / 0.16f);

            // Matrisleri hazırla
            for (int i = 0; i < _particles.Count; i++)
            {
                var p = _particles[i];
                if (p.isDraining) continue;

                Vector3 pos = new Vector3(p.position.x, p.position.y, p.depthZ);
                Quaternion rot = Quaternion.Euler(0f, 0f, p.angle);
                Matrix4x4 m = Matrix4x4.TRS(pos, rot, scale);

                if (_batches.TryGetValue(p.color, out var list))
                {
                    list.Add(m);
                }
            }

            // Her renk grubunu DrawMeshInstanced ile GPU'ya gönder (maksimum 1023'lük dilimler)
            foreach (var kvp in _batches)
            {
                var col = kvp.Key;
                var list = kvp.Value;
                int count = list.Count;
                if (count == 0) continue;

                if (!_materials.TryGetValue(col, out var mat) || mat == null) continue;

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
}
