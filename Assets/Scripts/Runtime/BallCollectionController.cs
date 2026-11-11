using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PuzzleGlass
{
    public class BallCollectionController : MonoBehaviour
    {
        public static BallCollectionController Instance { get; private set; }

        [Header("Attraction Physics Settings")]
        [Tooltip("Force pulling matching balls down towards the spout exit.")]
        public float attractionPullForce = 24f;

        [Tooltip("Maximum velocity for balls while navigating towards exit.")]
        public float maxBallSpeed = 3.4f;

        [Tooltip("Interval between initiating collection of successive balls from the spout.")]
        [Range(0.10f, 0.60f)]
        public float collectionInterval = 0.15f;

        [Tooltip("Maximum balls simultaneously traveling through the spout neck.")]
        public int maxBallsInSpout = 3;

        [Tooltip("Maximum balls currently traveling in air per monster.")]
        public int maxBallsInTransitPerMonster = 4;

        [Tooltip("Vertical tolerance around spout bottom to trigger mouth launch.")]
        public float exitTriggerYOffset = 0.16f;

        [Tooltip("Vertical tolerance for balls considered part of the bottom layer in container.")]
        public float bottomLayerTolerance = 0.14f;

        [Header("State (Read Only)")]
        [SerializeField] private int _totalCollectedThisLevel = 0;
        private float _lastGlobalCollectionTime = -10f;

        private class ActiveMonsterStream
        {
            public MonsterCharacter monster;
            public float lastCollectionTime;
            public float placedTime;
            public bool playedBlockedFeedback;
            public List<BallPhysicsController> inTransitBalls = new List<BallPhysicsController>();
        }

        private readonly Dictionary<string, ActiveMonsterStream> _activeStreams = new Dictionary<string, ActiveMonsterStream>();
        private readonly List<BallPhysicsController> _attractingBalls = new List<BallPhysicsController>();

        public event Action<BallPhysicsController, MonsterCharacter> OnBallCollected;
        public event Action OnAllMatchingBallsCollected;

        private void Awake()
        {
            Instance = this;
        }

        private void OnEnable()
        {
            Instance = this;
        }

        private void Update()
        {
            // If SandSimulation is actively running with sand grains (and no physical balls), yield to sand engine
            if (SandSimulationEngine.Instance != null && SandSimulationEngine.Instance.enabled && SandSimulationEngine.Instance.sandAmount > 0)
            {
                if (BallObjectPool.Instance == null || BallObjectPool.Instance.ActiveBalls.Count == 0)
                    return;
            }

            PollSlottedMonsters();
        }

        private void FixedUpdate()
        {
            UpdateAttractionPhysics();
            ApplyFloorDriftTowardHungryMonsters();
        }

        /// <summary>
        /// Called when a monster is placed into a slot to start its collection stream.
        /// </summary>
        public void OnMonsterPlacedInSlot(MonsterCharacter monster)
        {
            if (monster == null) return;

            string key = monster.monsterId;
            if (!_activeStreams.ContainsKey(key))
            {
                _activeStreams[key] = new ActiveMonsterStream
                {
                    monster = monster,
                    lastCollectionTime = -10f,
                    placedTime = Time.time,
                    playedBlockedFeedback = false
                };
            }
            else
            {
                _activeStreams[key].placedTime = Time.time;
                _activeStreams[key].playedBlockedFeedback = false;
            }
        }

        /// <summary>
        /// Called when a player drags or taps a monster out of its slot to return it to the staging tray or move it.
        /// Immediately stops its collection stream and releases any still-in-container attracting balls back to the pool.
        /// </summary>
        public void OnMonsterRemovedFromSlot(MonsterCharacter monster)
        {
            if (monster == null) return;

            string key = monster.monsterId;
            if (_activeStreams.TryGetValue(key, out ActiveMonsterStream stream))
            {
                for (int i = stream.inTransitBalls.Count - 1; i >= 0; i--)
                {
                    var b = stream.inTransitBalls[i];
                    if (b != null && b.state == BallState.AttractingToExit)
                    {
                        b.CancelAttractionAndReturnToContainer();
                        _attractingBalls.Remove(b);
                        stream.inTransitBalls.RemoveAt(i);
                    }
                }
                _activeStreams.Remove(key);
            }

            for (int i = _attractingBalls.Count - 1; i >= 0; i--)
            {
                var b = _attractingBalls[i];
                if (b != null && b.TargetMonster == monster && b.state == BallState.AttractingToExit)
                {
                    b.CancelAttractionAndReturnToContainer();
                    _attractingBalls.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Checks slotted monsters and attracts ONLY balls that have physically reached the bottom/near side of the glass
        /// and have no other balls underneath them.
        /// </summary>
        private void PollSlottedMonsters()
        {
            SlotSystem slotSys = SlotSystem.Instance.OrNull() ?? FindFirstObjectByType<SlotSystem>();
            if (slotSys == null || slotSys.slots == null) return;

            // Remove any streams whose monster was detached, completed or destroyed
            List<string> keysToRemove = null;
            foreach (var kvp in _activeStreams)
            {
                var s = kvp.Value;
                if (s.monster == null || s.monster.assignedSlot == null || s.monster.remainingCount <= 0 || !s.monster.gameObject.activeInHierarchy)
                {
                    if (keysToRemove == null) keysToRemove = new List<string>();
                    keysToRemove.Add(kvp.Key);
                }
            }
            if (keysToRemove != null)
            {
                for (int i = 0; i < keysToRemove.Count; i++)
                {
                    _activeStreams.Remove(keysToRemove[i]);
                }
            }

            // Register any slotted monster not yet in streams
            for (int i = 0; i < slotSys.slots.Count; i++)
            {
                var slot = slotSys.slots[i];
                if (slot != null && slot.isOccupied && slot.currentMonster != null && slot.currentMonster.remainingCount > 0)
                {
                    string id = slot.currentMonster.monsterId;
                    if (!_activeStreams.ContainsKey(id))
                    {
                        _activeStreams[id] = new ActiveMonsterStream
                        {
                            monster = slot.currentMonster,
                            lastCollectionTime = -10f,
                            placedTime = Time.time,
                            playedBlockedFeedback = false
                        };
                    }
                }
            }

            if (_activeStreams.Count == 0) return;

            // Clean up finished balls in transit for all streams
            foreach (var kvp in _activeStreams)
            {
                var stream = kvp.Value;
                for (int b = stream.inTransitBalls.Count - 1; b >= 0; b--)
                {
                    var ball = stream.inTransitBalls[b];
                    if (ball == null || ball.state == BallState.Pooled || !ball.gameObject.activeInHierarchy)
                    {
                        stream.inTransitBalls.RemoveAt(b);
                    }
                }
            }

            // Pacing & congestion limit: ensure smooth, readable flow without overcrowding
            float now = Time.time;
            if (now - _lastGlobalCollectionTime < collectionInterval) return;
            if (_attractingBalls.Count >= maxBallsInSpout) return;

            // Find a physically accessible bottom-floor ball matching a slotted monster
            BallPhysicsController bestBall = FindNextCollectableBall(out ActiveMonsterStream targetStream);
            if (bestBall != null && targetStream != null)
            {
                StartBallAttraction(bestBall, targetStream.monster, targetStream);
                _lastGlobalCollectionTime = now;
                targetStream.lastCollectionTime = now;
                targetStream.playedBlockedFeedback = true;
            }
            else
            {
                // If a monster was recently placed in a slot but its balls are blocked in upper rows, play subtle feedback once
                foreach (var kvp in _activeStreams)
                {
                    var s = kvp.Value;
                    if (!s.playedBlockedFeedback && s.inTransitBalls.Count == 0 && (now - s.placedTime) > 0.25f)
                    {
                        s.playedBlockedFeedback = true;
                        if (s.monster != null)
                        {
                            s.monster.PlayWaitingForBallsFeedback();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Physically evaluates which balls are resting on the bottom/near side of the glass container with zero balls
        /// blocking them from below, and matches them to the closest hungry slotted monster on the same side.
        /// </summary>
        private BallPhysicsController FindNextCollectableBall(out ActiveMonsterStream targetStream)
        {
            targetStream = null;
            if (BallObjectPool.Instance == null) return null;

            var activeBalls = BallObjectPool.Instance.ActiveBalls;
            if (activeBalls == null || activeBalls.Count == 0) return null;

            ShapedGlassContainer container = BallContainerManager.Instance != null
                ? BallContainerManager.Instance.glassContainer
                : FindFirstObjectByType<ShapedGlassContainer>();

            float floorLocalY = (container != null) ? container.GetChamberFloorY() : -0.56f;
            float ballDiam = (container != null && container.particleSize > 0.08f) ? container.particleSize : 0.24f;
            float ballRad = ballDiam * 0.5f;

            // Maximum local Y center for a ball resting on the bottom floor of the glass (Row 0 is at floorLocalY + 0.12f,
            // while Row 1 is at floorLocalY + 0.325f). Setting threshold to floorLocalY + ballDiam * 0.85f (+0.204f)
            // guarantees only balls that have physically reached the bottom floor can be collected!
            float maxBottomFloorCenterLocalY = floorLocalY + ballDiam * 0.85f;

            List<BallPhysicsController> bottomCandidates = new List<BallPhysicsController>();

            for (int i = 0; i < activeBalls.Count; i++)
            {
                var b = activeBalls[i];
                if (b == null || b.state != BallState.InContainer || !b.gameObject.activeInHierarchy) continue;

                // Wait until a dropping ball has settled onto the floor (not falling in mid-air)
                if (b.rb != null && !b.rb.isKinematic && Mathf.Abs(b.rb.linearVelocity.y) > 1.10f)
                    continue;

                Vector3 bLocal = (container != null)
                    ? container.transform.InverseTransformPoint(b.transform.position)
                    : b.transform.position;

                // 1. Must be physically on the bottom floor ("yakın tarafta")
                if (bLocal.y > maxBottomFloorCenterLocalY)
                    continue;

                // 2. Check that NO other resting ball in the same compartment is underneath or blocking this ball
                bool blockedFromBelow = false;
                for (int j = 0; j < activeBalls.Count; j++)
                {
                    if (i == j) continue;
                    var other = activeBalls[j];
                    if (other == null || other.state != BallState.InContainer || !other.gameObject.activeInHierarchy) continue;

                    Vector3 oLocal = (container != null)
                        ? container.transform.InverseTransformPoint(other.transform.position)
                        : other.transform.position;

                    if (!ArePositionsInSameBottomCompartment(container, bLocal.x, oLocal.x, floorLocalY + ballRad))
                        continue;

                    float dy = bLocal.y - oLocal.y;
                    float dx = Mathf.Abs(bLocal.x - oLocal.x);

                    // If another ball is below this ball and horizontally overlapping its downward path
                    if (dy > ballRad * 0.35f && dx < ballDiam * 0.86f)
                    {
                        blockedFromBelow = true;
                        break;
                    }

                    // If a lower-layer ball is immediately adjacent on the bottom floor, finish the lower layer first
                    if (other.layerIndex < b.layerIndex && dx < ballDiam * 1.18f && oLocal.y <= maxBottomFloorCenterLocalY + 0.04f)
                    {
                        blockedFromBelow = true;
                        break;
                    }
                }

                if (!blockedFromBelow)
                {
                    bottomCandidates.Add(b);
                }
            }

            if (bottomCandidates.Count == 0) return null;

            // 3. Match against active slotted monsters, prioritizing the ball closest horizontally to each monster's slot!
            BallPhysicsController bestOverallBall = null;
            ActiveMonsterStream bestOverallStream = null;
            float bestOverallScore = float.MaxValue;

            foreach (var kvp in _activeStreams)
            {
                var stream = kvp.Value;
                var monster = stream.monster;
                if (monster == null) continue;

                int needed = monster.remainingCount - stream.inTransitBalls.Count;
                if (needed <= 0) continue;
                if (stream.inTransitBalls.Count >= maxBallsInTransitPerMonster) continue;

                float monsterWorldX = monster.transform.position.x;
                float monsterLocalX = (container != null)
                    ? container.transform.InverseTransformPoint(monster.transform.position).x
                    : monsterWorldX;

                for (int i = 0; i < bottomCandidates.Count; i++)
                {
                    var cand = bottomCandidates[i];
                    if (cand.colorType != monster.colorType) continue;

                    Vector3 candLocal = (container != null)
                        ? container.transform.InverseTransformPoint(cand.transform.position)
                        : cand.transform.position;

                    // If a vertical glass divider separates the bottom pool into compartments,
                    // a monster can only collect from the compartment aligned with / reachable from its slot side!
                    if (!ArePositionsInSameBottomCompartment(container, monsterLocalX, candLocal.x, floorLocalY + ballRad))
                    {
                        continue;
                    }

                    float distX = Mathf.Abs(cand.transform.position.x - monsterWorldX);
                    // Prefer lower layerIndex first, then closest horizontal distance to the monster's slot
                    float score = cand.layerIndex * 0.35f + distX;
                    if (score < bestOverallScore)
                    {
                        bestOverallScore = score;
                        bestOverallBall = cand;
                        bestOverallStream = stream;
                    }
                }
            }

            if (bestOverallBall != null && bestOverallStream != null)
            {
                targetStream = bestOverallStream;
                return bestOverallBall;
            }

            return null;
        }

        /// <summary>
        /// Gently rolls resting bottom-floor balls laterally toward a hungry slotted monster of the same color
        /// when space opens up beside them, giving the ball pool a lively, magnetic physical response!
        /// </summary>
        private void ApplyFloorDriftTowardHungryMonsters()
        {
            if (_activeStreams.Count == 0 || BallObjectPool.Instance == null) return;

            var activeBalls = BallObjectPool.Instance.ActiveBalls;
            if (activeBalls == null || activeBalls.Count == 0) return;

            ShapedGlassContainer container = BallContainerManager.Instance != null
                ? BallContainerManager.Instance.glassContainer
                : null;

            float floorLocalY = (container != null) ? container.GetChamberFloorY() : -0.56f;
            float ballDiam = (container != null && container.particleSize > 0.08f) ? container.particleSize : 0.24f;
            float maxBottomY = floorLocalY + ballDiam * 0.85f;

            foreach (var kvp in _activeStreams)
            {
                var monster = kvp.Value.monster;
                if (monster == null || monster.remainingCount <= 0) continue;

                float monsterWorldX = monster.transform.position.x;
                float monsterLocalX = (container != null)
                    ? container.transform.InverseTransformPoint(monster.transform.position).x
                    : monsterWorldX;

                for (int i = 0; i < activeBalls.Count; i++)
                {
                    var b = activeBalls[i];
                    if (b == null || b.state != BallState.InContainer || b.colorType != monster.colorType || b.rb == null || b.rb.isKinematic)
                        continue;

                    Vector3 bLocal = (container != null)
                        ? container.transform.InverseTransformPoint(b.transform.position)
                        : b.transform.position;

                    if (bLocal.y > maxBottomY) continue;
                    if (!ArePositionsInSameBottomCompartment(container, monsterLocalX, bLocal.x, floorLocalY + ballDiam * 0.5f))
                        continue;

                    float dx = monsterWorldX - b.transform.position.x;
                    if (Mathf.Abs(dx) > 0.08f)
                    {
                        float pullAccel = Mathf.Clamp(dx * 3.2f, -2.2f, 2.2f);
                        b.rb.AddForce(new Vector3(pullAccel, 0f, 0f), ForceMode.Acceleration);
                    }
                }
            }
        }

        private bool ArePositionsInSameBottomCompartment(
            ShapedGlassContainer container,
            float localXA,
            float localXB,
            float checkLocalY)
        {
            if (container == null || container.dividers == null || container.dividers.Count == 0) return true;

            for (int d = 0; d < container.dividers.Count; d++)
            {
                var div = container.dividers[d];
                if (div != null && div.enabled && div.BlocksHorizontalBetween(localXA, localXB, checkLocalY))
                {
                    return false;
                }
            }
            return true;
        }

        private Vector3 GetExitWorldPosForBall(ShapedGlassContainer container, BallPhysicsController ball)
        {
            if (container != null && container.IsFlatBottomBox)
            {
                float floorLocalY = container.GetChamberFloorY() * container.heightScale;
                float worldFloorY = container.transform.TransformPoint(new Vector3(0f, floorLocalY, 0f)).y;
                float ballX = (ball != null) ? ball.transform.position.x : 0f;
                return new Vector3(ballX, worldFloorY - 0.06f, -0.16f);
            }

            float botY = (container != null && container.contourPoints != null && container.contourPoints.Count > 0)
                ? container.contourPoints[0].y * container.heightScale
                : (container != null ? container.spoutBottomY : -1.20f);

            float spoutCenterX = (container != null && container.contourPoints != null && container.contourPoints.Count > 0)
                ? (container.symmetricMode ? 0f : container.contourPoints[0].centerX) * container.widthScale
                : 0f;

            Vector3 localExit = new Vector3(spoutCenterX, botY, -0.10f);
            return (container != null) ? container.transform.TransformPoint(localExit) : localExit;
        }

        private void StartBallAttraction(BallPhysicsController ball, MonsterCharacter monster, ActiveMonsterStream stream)
        {
            stream.inTransitBalls.Add(ball);

            if (!_attractingBalls.Contains(ball))
            {
                _attractingBalls.Add(ball);
            }

            ShapedGlassContainer container = BallContainerManager.Instance != null
                ? BallContainerManager.Instance.glassContainer
                : FindFirstObjectByType<ShapedGlassContainer>();

            Vector3 exitPos = GetExitWorldPosForBall(container, ball);

            ball.BeginAttraction(monster, exitPos);

            if (BallContainerManager.Instance != null && BallContainerManager.Instance.bottomSpoutStopper != null)
            {
                Physics.IgnoreCollision(ball.sphereCollider, BallContainerManager.Instance.bottomSpoutStopper, true);
            }
        }

        private void UpdateAttractionPhysics()
        {
            if (_attractingBalls.Count == 0) return;

            ShapedGlassContainer container = BallContainerManager.Instance != null
                ? BallContainerManager.Instance.glassContainer
                : FindFirstObjectByType<ShapedGlassContainer>();

            for (int i = _attractingBalls.Count - 1; i >= 0; i--)
            {
                var ball = _attractingBalls[i];
                if (ball == null || ball.state != BallState.AttractingToExit || !ball.gameObject.activeInHierarchy)
                {
                    _attractingBalls.RemoveAt(i);
                    continue;
                }

                // If the target monster was removed from its slot while the ball was still inside the container, return it
                MonsterCharacter targetMonster = ball.TargetMonster;
                if (targetMonster == null || targetMonster.assignedSlot == null || targetMonster.remainingCount <= 0 || !targetMonster.gameObject.activeInHierarchy)
                {
                    targetMonster = FindMonsterForColor(ball.colorType);
                    if (targetMonster == null)
                    {
                        ball.CancelAttractionAndReturnToContainer();
                        _attractingBalls.RemoveAt(i);
                        continue;
                    }
                }

                Vector3 exitPos = GetExitWorldPosForBall(container, ball);

                ball.ApplyAttractionToExit(exitPos, attractionPullForce, maxBallSpeed);

                bool reachedExit = ball.transform.position.y <= exitPos.y + exitTriggerYOffset;
                bool timedOut = ball.AttractionTime > 0.55f;

                if (reachedExit || timedOut)
                {
                    _attractingBalls.RemoveAt(i);

                    if (targetMonster != null)
                    {
                        Vector3 launchPos = ball.transform.position;
                        ball.LaunchToMonsterMouth(launchPos, targetMonster, (b) =>
                        {
                            OnBallReachedMonster(b, targetMonster);
                        });
                    }
                    else
                    {
                        ball.CancelAttractionAndReturnToContainer();
                    }
                }
            }
        }

        private MonsterCharacter FindMonsterForColor(PuzzleColor color)
        {
            foreach (var kvp in _activeStreams)
            {
                if (kvp.Value.monster != null && kvp.Value.monster.assignedSlot != null &&
                    kvp.Value.monster.colorType == color && kvp.Value.monster.remainingCount > 0)
                {
                    return kvp.Value.monster;
                }
            }
            return null;
        }

        private void OnBallReachedMonster(BallPhysicsController ball, MonsterCharacter monster)
        {
            _totalCollectedThisLevel++;
            OnBallCollected?.Invoke(ball, monster);
            CheckLevelCompletion();
        }

        public void CheckLevelCompletion()
        {
            if (BallObjectPool.Instance != null && BallObjectPool.Instance.ActiveBalls.Count == 0)
            {
                OnAllMatchingBallsCollected?.Invoke();
            }

            if (PuzzleGlassGameManager.Instance != null)
            {
                PuzzleGlassGameManager.Instance.CheckLevelCompletionState();
            }
        }

        public void ClearAll()
        {
            _attractingBalls.Clear();
            _activeStreams.Clear();
            _totalCollectedThisLevel = 0;
            _lastGlobalCollectionTime = -10f;
        }
    }
}
