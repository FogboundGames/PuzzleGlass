using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PuzzleGlass
{
    [System.Serializable]
    public class AssignedCubeOrder
    {
        public MonsterCharacter targetMonster;
        public PuzzleColor color;
        public int assignedTotal;
        public int dispatchedCount;

        public bool IsFinished => dispatchedCount >= assignedTotal;
    }

    public class CubeDispenser : MonoBehaviour
    {
        public ShapedGlassContainer glassContainer;
        public float feedInterval = 0.11f;
        public bool isAutoFeeding = false;

        [SerializeField]
        private List<AssignedCubeOrder> _orders = new List<AssignedCubeOrder>();
        private Dictionary<PuzzleColor, Material> _cubeMaterials = new Dictionary<PuzzleColor, Material>();
        private Mesh _cubeMesh;
        private Coroutine _autoFeedRoutine;
        private int _lastServedIndex = -1;
        private float _continuousFeedTimer = 0f;

        private void Awake()
        {
            feedInterval = 0.11f;
            EnsureResources();
            if (glassContainer == null)
            {
                glassContainer = FindFirstObjectByType<ShapedGlassContainer>();
            }
        }

        private void Start()
        {
            ClearOrders();
            SyncAllSceneMonsters();
        }

        private void Update()
        {
            if (!Application.isPlaying) return;

            // Single deterministic feed loop: whenever the cube closest to the tip of the bottle
            // matches a slotted monster's color, that monster collects it 1-by-1!
            _continuousFeedTimer += Time.deltaTime;
            if (_continuousFeedTimer >= feedInterval)
            {
                if (HasAnySlottedMonsterWaiting())
                {
                    bool fed = FeedNextCube();
                    isAutoFeeding = fed;
                    // If the next matching cube is currently sliding down into the spout, re-check in 0.025s
                    _continuousFeedTimer = fed ? 0f : (feedInterval - 0.025f);
                }
                else
                {
                    isAutoFeeding = false;
                }
            }
        }

        private bool HasAnySlottedMonsterWaiting()
        {
            SyncAllSceneMonsters();
            for (int i = 0; i < _orders.Count; i++)
            {
                var o = _orders[i];
                if (!o.IsFinished && o.targetMonster != null && o.targetMonster.assignedSlot != null && o.targetMonster.remainingCount > 0)
                {
                    return true;
                }
            }
            return false;
        }

        private void SyncAllSceneMonsters()
        {
            if (SlotSystem.Instance != null && SlotSystem.Instance.slots != null)
            {
                for (int i = 0; i < SlotSystem.Instance.slots.Count; i++)
                {
                    var slot = SlotSystem.Instance.slots[i];
                    if (slot != null && slot.currentMonster != null)
                    {
                        RegisterMonsterOrder(slot.currentMonster);
                    }
                }
            }
        }

        private void EnsureResources()
        {
            if (_cubeMesh == null)
            {
                float pSize = (glassContainer != null) ? Mathf.Clamp(glassContainer.particleSize, 0.08f, 0.22f) : 0.16f;
                _cubeMesh = ProceduralMeshUtils.CreateRoundedCube(pSize, pSize * 0.28f, 4);
            }

            foreach (PuzzleColor col in Enum.GetValues(typeof(PuzzleColor)))
            {
                if (!_cubeMaterials.ContainsKey(col) || _cubeMaterials[col] == null)
                {
                    _cubeMaterials[col] = PuzzleColorPalette.CreateCandyCubeMaterial(col, $"Mat_FlyingCube_{col}");
                }
            }
        }

        public void RegisterMonsterOrder(MonsterCharacter monster)
        {
            if (monster == null) return;

            // Exact 1-to-1 count: if the monster says 10, it collects literally 10 cubes!
            int exactCubeQuota = (monster.initialCount > 0) ? monster.initialCount : monster.remainingCount;

            for (int i = 0; i < _orders.Count; i++)
            {
                if (_orders[i].targetMonster == monster)
                {
                    _orders[i].assignedTotal = exactCubeQuota;
                    _orders[i].color = monster.colorType;
                    return;
                }
            }

            AssignedCubeOrder order = new AssignedCubeOrder
            {
                targetMonster = monster,
                color = monster.colorType,
                assignedTotal = exactCubeQuota,
                dispatchedCount = 0
            };

            _orders.Add(order);
        }

        public void ClearOrders()
        {
            _orders.Clear();
            _lastServedIndex = -1;
        }

        public void StartFeedingSlottedMonsters()
        {
            SyncAllSceneMonsters();
            _continuousFeedTimer = feedInterval;
        }

        public bool FeedNextCube()
        {
            if (glassContainer == null)
            {
                glassContainer = FindFirstObjectByType<ShapedGlassContainer>();
                if (glassContainer == null) return false;
            }

            SyncAllSceneMonsters();

            // 1. Inspect the single cube closest to the tip/mouth of the bottle!
            var frontCube = glassContainer.GetFrontCube();
            if (frontCube == null)
            {
                return false;
            }

            PuzzleColor frontColor = frontCube.color;

            // 2. ONLY a slotted monster whose color matches the cube closest to the tip can collect it!
            List<AssignedCubeOrder> matchingOrders = new List<AssignedCubeOrder>();
            for (int i = 0; i < _orders.Count; i++)
            {
                var o = _orders[i];
                if (!o.IsFinished && o.targetMonster != null && o.targetMonster.assignedSlot != null && o.targetMonster.remainingCount > 0)
                {
                    if (o.color == frontColor)
                    {
                        matchingOrders.Add(o);
                    }
                }
            }

            if (matchingOrders.Count == 0)
            {
                // No slotted monster matches the color currently at the tip of the bottle.
                // The cubes remain in the bottle until a matching monster is placed into a slot!
                return false;
            }

            // 3. Round-robin among slotted monsters that share this front color
            _lastServedIndex = (_lastServedIndex + 1) % matchingOrders.Count;
            AssignedCubeOrder targetOrder = matchingOrders[_lastServedIndex];

            // 4. Consume the single frontmost cube from the bottle tip and drop it into the monster's mouth!
            if (DispatchCube(targetOrder.targetMonster, frontColor))
            {
                targetOrder.dispatchedCount++;
                return true;
            }

            return false;
        }

        private bool DispatchCube(MonsterCharacter targetMonster, PuzzleColor color)
        {
            EnsureResources();

            if (glassContainer != null)
            {
                Material mat = _cubeMaterials.ContainsKey(color) ? _cubeMaterials[color] : null;
                bool launchedFromTrack = glassContainer.ConsumeAndLaunchCube(targetMonster, color, mat, _cubeMesh);
                if (launchedFromTrack)
                {
                    glassContainer.PulseSpout();
                    return true;
                }
            }

            return false;
        }

        public void SetAutoFeed(bool enable)
        {
            isAutoFeeding = enable;
            if (isAutoFeeding)
            {
                if (_autoFeedRoutine != null) StopCoroutine(_autoFeedRoutine);
                _autoFeedRoutine = StartCoroutine(AutoFeedRoutine());
            }
            else
            {
                if (_autoFeedRoutine != null)
                {
                    StopCoroutine(_autoFeedRoutine);
                    _autoFeedRoutine = null;
                }
            }
        }

        private IEnumerator AutoFeedRoutine()
        {
            isAutoFeeding = true;
            yield return new WaitForSeconds(0.04f);

            while (isAutoFeeding)
            {
                bool fed = FeedNextCube();
                if (!fed)
                {
                    isAutoFeeding = false;
                    _autoFeedRoutine = null;
                    yield break;
                }
                yield return new WaitForSeconds(feedInterval);
            }

            _autoFeedRoutine = null;
        }
    }
}
