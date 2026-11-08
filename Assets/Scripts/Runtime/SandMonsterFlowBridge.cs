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
        [Tooltip("Her kum tanesi için canavar sayacından kaç adet düşürüleceği")]
        public int particlesPerCount = 1;

        [Tooltip("Kum tanesinin namludan canavar ağzına uçuş süresi (saniye)")]
        [Range(0.12f, 0.45f)]
        public float ballisticFlightDuration = 0.22f;

        [Tooltip("Balistik uçuş kavis yüksekliği")]
        [Range(0.05f, 0.80f)]
        public float arcHeight = 0.25f;

        [Tooltip("Slotlarda uygun renkte canavar yoksa kapağı otomatik kapat")]
        public bool autoManageSpoutGate = true;

        [Tooltip("İlk canavar slota oturduğunda statik görsel modundan dinamik fiziğe otomatik geç")]
        public bool autoStartPhysicsOnMonsterSlotted = true;

        // Uçan kum küpü prefab ve havuzu
        private Transform _cubePoolRoot;
        private readonly Queue<GameObject> _cubePool = new Queue<GameObject>();
        private Mesh _cubeMesh;
        private readonly Dictionary<PuzzleColor, Material> _cubeMaterials = new Dictionary<PuzzleColor, Material>();

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
                sandEngine = GetComponent<SandSimulationEngine>() ?? FindFirstObjectByType<SandSimulationEngine>();
            }
            if (glassContainer == null)
            {
                glassContainer = GetComponent<ShapedGlassContainer>() ?? FindFirstObjectByType<ShapedGlassContainer>();
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
            }
        }

        private void UnhookEvents()
        {
            if (sandEngine != null)
            {
                sandEngine.OnParticleDrainedFromSpout -= HandleParticleDrained;
            }
        }

        private void Update()
        {
            if (!Application.isPlaying) return;

            // Kapı yönetimi: Eğer slotlarda hiç aktif canavar kalmadıysa kapağı kapat
            if (autoManageSpoutGate && sandEngine != null)
            {
                bool hasHungryMonster = HasAnyHungrySlottedMonster();
                if (sandEngine.isGateOpen && !hasHungryMonster)
                {
                    sandEngine.SetSpoutGate(false);
                }
            }
        }

        /// <summary>
        /// Bir canavar slota oturduğunda çağrılır (tıklama veya sürükleme ile).
        /// </summary>
        public void NotifyMonsterPlacedInSlot(MonsterCharacter monster)
        {
            if (monster == null) return;
            EnsureReferences();

            // Eğer kum simülasyonu henüz statik görsel modundaysa, oyunu başlatıp dinamik fiziğe geçir
            if (autoStartPhysicsOnMonsterSlotted && sandEngine != null)
            {
                if (sandEngine.simulationMode == SandSimulationMode.StaticImage)
                {
                    sandEngine.SetSimulationMode(SandSimulationMode.DynamicPhysics);
                }

                // Alt çıkış kapağını aç
                sandEngine.SetSpoutGate(true);
            }

            if (glassContainer != null)
            {
                glassContainer.PulseSpout();
            }
        }

        /// <summary>
        /// Haznenin alt çıkışından dökülen kum parçacığını yakalar ve canavar ağzına gönderir.
        /// </summary>
        private void HandleParticleDrained(SandParticle particle)
        {
            if (!Application.isPlaying) return;
            EnsureReferences();

            // Bu renkte kum bekleyen slotlanmış canavarı bul
            MonsterCharacter target = GetSlottedMonsterForColor(particle.color);

            Vector3 spoutWorldPos = (glassContainer != null)
                ? glassContainer.GetSpoutExitWorldPosition()
                : transform.position;

            if (target != null)
            {
                // Hedef canavarın ağzına doğru balistik küp fırlat
                SpawnBallisticSandCube(spoutWorldPos, target, particle.color);
            }
            else
            {
                // Slotlarda bu renkte canavar yoksa, herhangi bir aç canavara veya haznede bekletmeye uygun
                // Fallback: İlk aç canavarı dene
                MonsterCharacter fallbackTarget = GetAnyHungrySlottedMonster();
                if (fallbackTarget != null)
                {
                    SpawnBallisticSandCube(spoutWorldPos, fallbackTarget, particle.color);
                }
                else
                {
                    // Slotlar boşsa kapağı kapat
                    if (sandEngine != null)
                    {
                        sandEngine.SetSpoutGate(false);
                    }
                }
            }
        }

        private void SpawnBallisticSandCube(Vector3 startPos, MonsterCharacter target, PuzzleColor color)
        {
            GameObject cubeObj = GetPooledCube();
            cubeObj.transform.position = startPos;
            cubeObj.transform.localScale = Vector3.one;
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

            Vector3 endPos = (target.mouthTargetPoint != null)
                ? target.mouthTargetPoint.position
                : target.transform.position + Vector3.up * 0.25f;

            // Rastgele hafif dönüş torku
            Vector3 randomTorque = new Vector3(
                UnityEngine.Random.Range(-360f, 360f),
                UnityEngine.Random.Range(-360f, 360f),
                UnityEngine.Random.Range(-360f, 360f));

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                if (target != null && target.mouthTargetPoint != null)
                {
                    endPos = target.mouthTargetPoint.position;
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
            if (target != null && !target.IsCompleted)
            {
                target.FeedParticle(particlesPerCount);
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
