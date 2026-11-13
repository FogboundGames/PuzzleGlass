using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

namespace PuzzleGlass
{
    /// <summary>
    /// Phase 4 – Sand Flow & Monster Mouth System:
    /// Hazneden dökülen kumları (SandParticle) algılar, alt slotlardaki uygun renkli canavara
    /// balistik eğri ile fırlatır, ağız açılma (anticipation) ve yutma (gulp) animasyonlarını tetikler,
    /// canavar sayaçlarını düşürür ve seviye tamamlanmasını yönetir.
    /// </summary>
    [ExecuteAlways]
    public class SandMonsterFlowBridge : MonoBehaviour
    {
        public static SandMonsterFlowBridge Instance { get; private set; }

        [Header("References")]
        public SandSimulationEngine sandEngine;
        public ShapedGlassContainer glassContainer;
        public SlotSystem slotSystem;

        [Header("Flow & Mouth Tuning")]
        [Tooltip("Aynı renkten iki kum tanesinin hazneden serbest bırakılması arasındaki süre (saniye)")]
        [Range(0.005f, 0.10f)]
        public float grainReleaseInterval = 0.016f;

        [Tooltip("Kum tanesinin namludan canavar ağzına uçuş süresi (saniye)")]
        [Range(0.12f, 0.45f)]
        public float ballisticFlightDuration = 0.22f;

        [Tooltip("Balistik uçuş kavis yüksekliği")]
        [Range(0.05f, 0.80f)]
        public float arcHeight = 0.25f;

        [Tooltip("Slotlarda uygun renkte canavar yoksa kapağı otomatik kapat")]
        public bool autoManageSpoutGate = true;

        // Uçan kum küpü prefab ve havuzu
        private Transform _cubePoolRoot;
        private readonly Queue<GameObject> _cubePool = new Queue<GameObject>();
        private Mesh _cubeMesh;
        private readonly Dictionary<PuzzleColor, Material> _cubeMaterials = new Dictionary<PuzzleColor, Material>();

        // Çıkışa veya ağza yolda olan taneler; aynı tanenin iki kez sayılmasını önler
        private readonly Dictionary<PuzzleColor, int> _grainsInFlight = new Dictionary<PuzzleColor, int>();
        private readonly Dictionary<PuzzleColor, int> _grainsNeeded = new Dictionary<PuzzleColor, int>();
        private readonly Dictionary<MonsterCharacter, int> _reservedGrains = new Dictionary<MonsterCharacter, int>();
        private float _releaseTimer;

        private void Awake()
        {
            Instance = this;
            EnsureReferences();
            EnsurePool();
        }

        private void OnEnable()
        {
            Instance = this;
            EnsureReferences();
            EnsurePool();
            HookEvents();
        }

        private void OnDisable()
        {
            UnhookEvents();
        }

        private void EnsureReferences()
        {
            if (sandEngine == null)
            {
                sandEngine = GetComponent<SandSimulationEngine>().OrNull() ?? FindFirstObjectByType<SandSimulationEngine>();
            }
            if (glassContainer == null)
            {
                glassContainer = GetComponent<ShapedGlassContainer>().OrNull() ?? FindFirstObjectByType<ShapedGlassContainer>();
            }
            if (slotSystem == null)
            {
                slotSystem = FindFirstObjectByType<SlotSystem>();
            }
        }

        private void EnsurePool()
        {
            if (_cubeMesh == null)
            {
                _cubeMesh = ProceduralMeshUtils.CreateRoundedCube(0.16f, 0.045f, 3);
            }

            foreach (PuzzleColor col in Enum.GetValues(typeof(PuzzleColor)))
            {
                if (!_cubeMaterials.ContainsKey(col) || _cubeMaterials[col] == null)
                {
                    _cubeMaterials[col] = PuzzleColorUtility.GetMaterial(col);
                }
            }

            if (_cubePoolRoot == null)
            {
                Transform existing = transform.Find("SandFlowCubePool");
                if (existing != null)
                {
                    _cubePoolRoot = existing;
                }
                else
                {
                    GameObject poolObj = new GameObject("SandFlowCubePool");
                    poolObj.transform.SetParent(transform, false);
                    _cubePoolRoot = poolObj.transform;
                }
            }
        }

        private void HookEvents()
        {
            UnhookEvents();
            if (sandEngine != null)
            {
                sandEngine.OnParticleDrainedFromSpout += HandleParticleDrained;
                sandEngine.OnSandRebuilt += ResetFlowState;
            }
        }

        private void UnhookEvents()
        {
            if (sandEngine != null)
            {
                sandEngine.OnParticleDrainedFromSpout -= HandleParticleDrained;
                sandEngine.OnSandRebuilt -= ResetFlowState;
            }
        }

        private void Update()
        {
            if (!Application.isPlaying || sandEngine == null) return;

            ReleaseGrainsForSlottedMonsters();

            // Kapı yönetimi: Eğer slotlarda hiç aktif canavar kalmadıysa kapağı kapat
            if (autoManageSpoutGate && sandEngine.isGateOpen && !HasAnyHungrySlottedMonster())
            {
                sandEngine.SetSpoutGate(false);
            }
        }

        private void ResetFlowState()
        {
            _grainsInFlight.Clear();
            _reservedGrains.Clear();
            _releaseTimer = 0f;
        }

        private void ReleaseGrainsForSlottedMonsters()
        {
            if (slotSystem == null || slotSystem.slots == null) return;

            _releaseTimer += Time.deltaTime;
            if (_releaseTimer < grainReleaseInterval) return;
            _releaseTimer = 0f;

            _grainsNeeded.Clear();
            int hungrySlots = 0;
            for (int i = 0; i < slotSystem.slots.Count; i++)
            {
                var slot = slotSystem.slots[i];
                if (slot == null || !slot.isOccupied || slot.currentMonster == null || slot.currentMonster.IsCompleted) continue;

                hungrySlots++;
                var monster = slot.currentMonster;
                _grainsNeeded.TryGetValue(monster.colorType, out int needed);
                _grainsNeeded[monster.colorType] = needed + monster.GetGrainsNeeded(sandEngine.GrainsPerCount);
            }

            bool anyGrainMoving = false;
            foreach (var kvp in _grainsNeeded)
            {
                _grainsInFlight.TryGetValue(kvp.Key, out int inFlight);
                if (kvp.Value > inFlight && sandEngine.TryReleaseGrain(kvp.Key))
                {
                    _grainsInFlight[kvp.Key] = ++inFlight;
                }
                if (inFlight > 0) anyGrainMoving = true;
            }

            UnlockSectionIfReady(hungrySlots, anyGrainMoving);
        }

        // Kilit, alttaki renkli kum yenince açılır; slotlar dolu ve hepsi kilitli kumu bekliyorsa oyun tıkanmasın diye yine açılır
        private void UnlockSectionIfReady(int hungrySlots, bool anyGrainMoving)
        {
            if (!sandEngine.HasLockedSection) return;

            bool slotsBlocked = hungrySlots == slotSystem.slots.Count && !anyGrainMoving;
            if (sandEngine.CountReachableArtGrains() == 0 || slotsBlocked)
            {
                sandEngine.UnlockSection();
                if (glassContainer != null) glassContainer.PulseSpout();
            }
        }

        /// <summary>
        /// Bir canavar slota oturduğunda çağrılır (tıklama veya sürükleme ile).
        /// </summary>
        public void NotifyMonsterPlacedInSlot(MonsterCharacter monster)
        {
            if (monster == null) return;
            EnsureReferences();

            if (sandEngine != null)
            {
                sandEngine.SetSpoutGate(true);
            }

            if (glassContainer != null)
            {
                glassContainer.PulseSpout();
            }
        }

        /// <summary>
        /// Haznenin alt çıkışından dökülen kum parçacığını aynı renkteki aç canavarın ağzına gönderir.
        /// </summary>
        private void HandleParticleDrained(SandParticle particle)
        {
            if (!Application.isPlaying) return;
            EnsureReferences();

            MonsterCharacter target = ReserveMonsterForColor(particle.color);
            if (target == null)
            {
                FinishGrainFlight(null, particle.color);
                return;
            }

            Vector3 spoutWorldPos = (glassContainer != null)
                ? glassContainer.GetSpoutExitWorldPosition()
                : transform.position;

            SpawnBallisticSandCube(spoutWorldPos, target, particle.color);
        }

        private MonsterCharacter ReserveMonsterForColor(PuzzleColor color)
        {
            if (slotSystem == null || slotSystem.slots == null) return null;

            for (int i = 0; i < slotSystem.slots.Count; i++)
            {
                var slot = slotSystem.slots[i];
                if (slot == null || !slot.isOccupied || slot.currentMonster == null) continue;

                var monster = slot.currentMonster;
                if (monster.colorType != color || monster.IsCompleted) continue;

                _reservedGrains.TryGetValue(monster, out int reserved);
                if (monster.GetGrainsNeeded(sandEngine.GrainsPerCount) > reserved)
                {
                    _reservedGrains[monster] = reserved + 1;
                    return monster;
                }
            }

            return null;
        }

        private void FinishGrainFlight(MonsterCharacter target, PuzzleColor color)
        {
            if (_grainsInFlight.TryGetValue(color, out int inFlight))
            {
                _grainsInFlight[color] = Mathf.Max(0, inFlight - 1);
            }

            if (target != null && _reservedGrains.TryGetValue(target, out int reserved))
            {
                _reservedGrains[target] = Mathf.Max(0, reserved - 1);
            }
        }

        private void SpawnBallisticSandCube(Vector3 startPos, MonsterCharacter target, PuzzleColor color)
        {
            GameObject cubeObj = GetPooledCube();
            cubeObj.transform.position = startPos;
            cubeObj.transform.localScale = Vector3.one * (sandEngine.particleSize / 0.16f);
            cubeObj.SetActive(true);

            // Materyal ata
            var mr = cubeObj.GetComponent<MeshRenderer>();
            if (mr != null && _cubeMaterials.TryGetValue(color, out var mat))
            {
                mr.sharedMaterial = mat;
            }

            StartCoroutine(BallisticFlightRoutine(cubeObj, startPos, target, color));
        }

        private IEnumerator BallisticFlightRoutine(GameObject cubeObj, Vector3 startPos, MonsterCharacter target, PuzzleColor color)
        {
            float elapsed = 0f;
            float duration = ballisticFlightDuration;
            bool anticipationTriggered = false;
            int socketIndex = (target != null) ? target.ClaimNextSocketIndex() : 0;

            Vector3 endPos = (target != null)
                ? target.GetSocketWorldPosition(socketIndex)
                : startPos + Vector3.down * 1.2f;

            // Rastgele hafif dönüş torku
            Vector3 randomTorque = new Vector3(
                UnityEngine.Random.Range(-360f, 360f),
                UnityEngine.Random.Range(-360f, 360f),
                UnityEngine.Random.Range(-360f, 360f));

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                if (target != null)
                {
                    endPos = target.GetSocketWorldPosition(socketIndex);
                }

                // Parabolik balistik kavis (Z=-0.40f düzleminde önde kalır)
                float currentX = Mathf.Lerp(startPos.x, endPos.x, t);
                float currentY = Mathf.Lerp(startPos.y, endPos.y, t) + Mathf.Sin(t * Mathf.PI) * arcHeight;
                float currentZ = -0.38f;

                cubeObj.transform.position = new Vector3(currentX, currentY, currentZ);
                cubeObj.transform.Rotate(randomTorque * Time.deltaTime);

                // Ağza %60 yaklaştığında canavarın ağzı heyecanla açılır (Anticipation)
                if (!anticipationTriggered && t >= 0.55f && target != null && !target.IsCompleted)
                {
                    anticipationTriggered = true;
                    target.OnBallIncoming(null);
                }

                yield return null;
            }

            // Ağza ulaştı: Yutma ve sayaç düşürme
            FinishGrainFlight(target, color);
            if (target != null && !target.IsCompleted)
            {
                target.FeedParticle(sandEngine.GrainsPerCount);
            }

            // Küpü havuza geri gönder
            ReturnCubeToPool(cubeObj);

            // Tüm canavarlar bitti mi kontrol et
            CheckLevelCompletion();
        }

        private GameObject GetPooledCube()
        {
            if (_cubePool.Count > 0)
            {
                return _cubePool.Dequeue();
            }

            GameObject cube = new GameObject("FlowSandCube");
            cube.transform.SetParent(_cubePoolRoot, false);

            var mf = cube.AddComponent<MeshFilter>();
            mf.sharedMesh = _cubeMesh;

            var mr = cube.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            return cube;
        }

        private void ReturnCubeToPool(GameObject cube)
        {
            if (cube == null) return;
            cube.SetActive(false);
            cube.transform.SetParent(_cubePoolRoot, false);
            _cubePool.Enqueue(cube);
        }

        public MonsterCharacter GetSlottedMonsterForColor(PuzzleColor color)
        {
            if (slotSystem == null || slotSystem.slots == null) return null;

            for (int i = 0; i < slotSystem.slots.Count; i++)
            {
                var slot = slotSystem.slots[i];
                if (slot != null && slot.isOccupied && slot.currentMonster != null)
                {
                    var m = slot.currentMonster;
                    if (m.colorType == color && m.remainingCount > 0 && !m.IsCompleted)
                    {
                        return m;
                    }
                }
            }
            return null;
        }

        public MonsterCharacter GetAnyHungrySlottedMonster()
        {
            if (slotSystem == null || slotSystem.slots == null) return null;

            for (int i = 0; i < slotSystem.slots.Count; i++)
            {
                var slot = slotSystem.slots[i];
                if (slot != null && slot.isOccupied && slot.currentMonster != null)
                {
                    var m = slot.currentMonster;
                    if (m.remainingCount > 0 && !m.IsCompleted)
                    {
                        return m;
                    }
                }
            }
            return null;
        }

        public bool HasAnyHungrySlottedMonster()
        {
            return GetAnyHungrySlottedMonster() != null;
        }

        private void CheckLevelCompletion()
        {
            if (PuzzleGlassGameManager.Instance != null)
            {
                PuzzleGlassGameManager.Instance.CheckLevelCompletionState();
            }
        }
    }
}
