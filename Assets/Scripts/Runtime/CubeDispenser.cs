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
        public float feedInterval = 0.085f; // Fast, continuous pouring stream
        public bool isAutoFeeding = false;

        private List<AssignedCubeOrder> _orders = new List<AssignedCubeOrder>();
        private Dictionary<PuzzleColor, Material> _cubeMaterials = new Dictionary<PuzzleColor, Material>();
        private Mesh _cubeMesh;
        private Coroutine _autoFeedRoutine;
        private int _lastServedIndex = -1;

        private void Awake()
        {
            EnsureResources();
            if (glassContainer == null)
            {
                glassContainer = FindFirstObjectByType<ShapedGlassContainer>();
            }
        }

        private void EnsureResources()
        {
            if (_cubeMesh == null)
            {
                _cubeMesh = ProceduralMeshUtils.CreateRoundedCube(0.24f, 0.06f, 3);
            }

            Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            foreach (PuzzleColor col in Enum.GetValues(typeof(PuzzleColor)))
            {
                if (!_cubeMaterials.ContainsKey(col))
                {
                    Material m = new Material(litShader);
                    m.name = $"Mat_FlyingCube_{col}";
                    Color c = PuzzleColorPalette.GetCubeColor(col);
                    m.color = c;
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                    if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.18f);
                    if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.18f);
                    if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
                    m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
                    _cubeMaterials[col] = m;
                }
                else
                {
                    Material m = _cubeMaterials[col];
                    if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.18f);
                    if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.18f);
                    if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
                }
            }
        }

        public void RegisterMonsterOrder(MonsterCharacter monster)
        {
            if (monster == null) return;

            // Avoid duplicate orders for the same monster
            for (int i = 0; i < _orders.Count; i++)
            {
                if (_orders[i].targetMonster == monster)
                {
                    if (_orders[i].assignedTotal == 0)
                    {
                        _orders[i].assignedTotal = monster.remainingCount;
                    }
                    return;
                }
            }

            // Strict assignment: this order is unique to this specific monster instance!
            AssignedCubeOrder order = new AssignedCubeOrder
            {
                targetMonster = monster,
                color = monster.colorType,
                assignedTotal = monster.remainingCount,
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
            if (_autoFeedRoutine == null)
            {
                _autoFeedRoutine = StartCoroutine(AutoFeedRoutine());
            }
        }

        public bool FeedNextCube()
        {
            // Collect all active orders for monsters currently placed in a slot
            List<AssignedCubeOrder> eligibleOrders = new List<AssignedCubeOrder>();
            for (int i = 0; i < _orders.Count; i++)
            {
                var o = _orders[i];
                if (!o.IsFinished && o.targetMonster != null && o.targetMonster.assignedSlot != null && o.targetMonster.remainingCount > 0)
                {
                    eligibleOrders.Add(o);
                }
            }

            if (eligibleOrders.Count == 0)
            {
                // No active slotted monster orders pending
                return false;
            }

            // Round-robin selection among active slotted monsters
            _lastServedIndex = (_lastServedIndex + 1) % eligibleOrders.Count;
            AssignedCubeOrder nextOrder = eligibleOrders[_lastServedIndex];

            // Dispatch one cube matching this monster's exact color
            DispatchCube(nextOrder.targetMonster, nextOrder.color);
            nextOrder.dispatchedCount++;

            return true;
        }

        private void DispatchCube(MonsterCharacter targetMonster, PuzzleColor color)
        {
            EnsureResources();

            // 1. Take and launch the ACTUAL cube directly from the glass track conveyor!
            if (glassContainer != null)
            {
                bool launchedFromTrack = glassContainer.ConsumeAndLaunchCube(targetMonster, color, _cubeMaterials[color], _cubeMesh);
                if (launchedFromTrack)
                {
                    glassContainer.PulseSpout();
                    return;
                }
            }

            // 2. Fallback if no matching cube was found in track: launch from spout opening
            Vector3 startPos = glassContainer != null
                ? glassContainer.GetSpoutExitWorldPosition()
                : transform.position + new Vector3(0, -0.90f, 0);

            GameObject cubeObj = new GameObject($"Cube_{color}_To_{targetMonster.monsterId}");
            cubeObj.transform.position = startPos;
            cubeObj.transform.localScale = Vector3.one;

            FlyingCube flying = cubeObj.AddComponent<FlyingCube>();
            flying.LaunchThroughGlassTrack(new Vector3[] { startPos }, startPos, targetMonster, color, _cubeMaterials[color], _cubeMesh);

            if (glassContainer != null)
            {
                glassContainer.PulseSpout();
            }
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

            // Brief anticipation pause (0.10s) before dispensing first cube
            yield return new WaitForSeconds(0.10f);

            while (isAutoFeeding)
            {
                bool fed = FeedNextCube();
                if (!fed)
                {
                    // No more pending cubes for any currently slotted monster
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
