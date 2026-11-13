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
        private float _levelStartTime = -10f;

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
            _levelStartTime = Time.time;
        }

        private void Update()
        {
            // If SandSimulation is actively running with sand grains (and no physical balls), yield to sand engine
            if (SandSimulationEngine.Instance != null && SandSimulationEngine.Instance.enabled && SandSimulationEngine.Instance.sandAmount > 0)
            {
                if (BallObjectPool.Instance == null || BallObjectPool.Instance.ActiveBalls.Count == 0)
                    return;
            }

            AutoActivateMatchingTopEdgeBlocks();
            PollSlottedMonsters();
        }

        /// <summary>
        /// Automatically activates any grid Carrier Block docked at the top row ("en üst taraf")
        /// whenever the bottom floor of the glass exposes balls of that block's color.
        /// </summary>
        private void AutoActivateMatchingTopEdgeBlocks()
        {
            if (Time.time - _levelStartTime < 0.35f) return;
            var grid = StagingGridBoard.Instance;
            var gm = PuzzleGlassGameManager.Instance;
            if (grid == null || gm == null || gm.activeMonsters == null || gm.isLevelCompleted || gm.isLevelFailed) return;

            var drag = MonsterDragController.Instance;
            for (int i = 0; i < gm.activeMonsters.Count; i++)
            {
                var m = gm.activeMonsters[i];
                if (m == null || m.IsCompleted || m.remainingCount <= 0 || !m.gameObject.activeInHierarchy || m.isCollectingInGrid || !m.armedAtTopEdge)
                    continue;
                if (drag != null && drag.IsBlockCurrentlyDragged(m))
                    continue;

                if (grid.IsBlockAtTopExit(m))
                {
                    m.TryStartInGridCollection(playDenialFeedback: false);
                }
            }
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
        /// Registers a grid Carrier Block as an active collector stream in place (no upper slot needed).
        /// </summary>
        public void ActivateGridCollector(MonsterCharacter monster)
        {
            OnMonsterPlacedInSlot(monster);
        }

        /// <summary>
        /// Checks active grid collectors (and any legacy slotted boxes) and pulls matching bottom-floor balls.
        /// </summary>
        private void PollSlottedMonsters()
        {
            // Remove any streams whose block was detached, completed or hidden
            List<string> keysToRemove = null;
            foreach (var kvp in _activeStreams)
            {
                var s = kvp.Value;
                if (s.monster == null || !s.monster.IsActivelyCollecting)
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

            // Register any actively collecting monster from PuzzleGlassGameManager
            if (PuzzleGlassGameManager.Instance != null && PuzzleGlassGameManager.Instance.activeMonsters != null)
            {
                var all = PuzzleGlassGameManager.Instance.activeMonsters;
                for (int i = 0; i < all.Count; i++)
                {
                    var m = all[i];
                    if (m != null && m.IsActivelyCollecting)
                    {
                        string id = m.monsterId;
                        if (!string.IsNullOrEmpty(id) && !_activeStreams.ContainsKey(id))
                        {
                            _activeStreams[id] = new ActiveMonsterStream
                            {
                                monster = m,
                                lastCollectionTime = -10f,
                                placedTime = Time.time,
                                playedBlockedFeedback = false
                            };
                        }
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
            // During Fever Mode (Streak >= 3), vacuum speed is 2x faster!
            bool isFever = PuzzleGlassGameManager.Instance != null && PuzzleGlassGameManager.Instance.IsFeverActive;
            float effectiveInterval = isFever ? (collectionInterval * 0.45f) : collectionInterval;
            int effectiveMaxInSpout = isFever ? (maxBallsInSpout + 2) : maxBallsInSpout;

            float now = Time.time;
            if (now - _lastGlobalCollectionTime < effectiveInterval) return;
            if (_attractingBalls.Count >= effectiveMaxInSpout) return;

            // Find a physically accessible bottom-floor ball matching an active box's aligned columns
            BallPhysicsController bestBall = FindNextCollectableBall(out ActiveMonsterStream targetStream);
            if (bestBall != null && targetStream != null)
            {
                StartBallAttraction(bestBall, targetStream.monster, targetStream);
                _lastGlobalCollectionTime = now;
                targetStream.lastCollectionTime = now;
                targetStream.playedBlockedFeedback = true;
            }

            // Pause any in-grid stream that has no balls in flight and no reachable matching balls in its own columns
            List<MonsterCharacter> idleGridToPause = null;
            foreach (var kvp in _activeStreams)
            {
                var s = kvp.Value;
                if (s.monster == null || s == targetStream) continue;

                if (!s.playedBlockedFeedback && s.inTransitBalls.Count == 0 && (now - s.placedTime) > 0.25f)
                {
                    s.playedBlockedFeedback = true;
                    s.monster.PlayWaitingForBallsFeedback();
                }

                if (s.monster.assignedSlot == null &&
                    s.inTransitBalls.Count == 0 &&
                    (now - s.placedTime) > 0.45f &&
                    (now - s.lastCollectionTime) > 0.45f &&
                    !HasReachableBottomBallForColor(s.monster.colorType, s.monster))
                {
                    if (idleGridToPause == null) idleGridToPause = new List<MonsterCharacter>();
                    idleGridToPause.Add(s.monster);
                }
            }

            if (idleGridToPause != null)
            {
                for (int i = 0; i < idleGridToPause.Count; i++)
                {
                    idleGridToPause[i].PauseInGridCollection();
                }
            }
        }

        public bool HasBallsInTransitFor(MonsterCharacter monster)
        {
            if (monster == null || string.IsNullOrEmpty(monster.monsterId)) return false;
            if (_activeStreams.TryGetValue(monster.monsterId, out ActiveMonsterStream stream))
            {
                return stream.inTransitBalls != null && stream.inTransitBalls.Count > 0;
            }
            return false;
        }

        private bool IsBallInMonsterCollectionZone(MonsterCharacter monster, BallPhysicsController ball)
        {
            if (monster == null || ball == null) return false;
            if (monster.assignedSlot != null) return true;

            var grid = StagingGridBoard.Instance;
            if (grid == null) return true;

            ShapedGlassContainer container = BallContainerManager.Instance != null
                ? BallContainerManager.Instance.glassContainer
                : null;
            float ballDiam = (container != null && container.particleSize > 0.08f) ? container.particleSize : 0.24f;
            float edgeTolerance = Mathf.Max(0.06f, ballDiam * 0.32f);

            var allMonsters = PuzzleGlassGameManager.Instance != null ? PuzzleGlassGameManager.Instance.activeMonsters : null;
            return grid.IsWorldXAlignedWithBlockTopColumns(monster, ball.transform.position.x, edgeTolerance, allMonsters);
        }

        /// <summary>
        /// Returns true if at least one unblocked bottom-row ball inside the glass container matches targetColor
        /// (and lies within forMonster's aligned horizontal zone when provided).
        /// </summary>
        public bool HasReachableBottomBallForColor(PuzzleColor targetColor, MonsterCharacter forMonster = null)
        {
            List<BallPhysicsController> bottomCandidates = CollectBottomCandidates();
            for (int i = 0; i < bottomCandidates.Count; i++)
            {
                var cand = bottomCandidates[i];
                if (cand == null || cand.colorType != targetColor) continue;
                if (forMonster != null && !IsBallInMonsterCollectionZone(forMonster, cand)) continue;
                return true;
            }
            return false;
        }

        private List<BallPhysicsController> CollectBottomCandidates()
        {
            List<BallPhysicsController> bottomCandidates = new List<BallPhysicsController>();
            if (BallObjectPool.Instance == null) return bottomCandidates;

            var activeBalls = BallObjectPool.Instance.ActiveBalls;
            if (activeBalls == null || activeBalls.Count == 0) return bottomCandidates;

            ShapedGlassContainer container = BallContainerManager.Instance != null
                ? BallContainerManager.Instance.glassContainer
                : FindFirstObjectByType<ShapedGlassContainer>();

            float floorLocalY = (container != null) ? container.GetChamberFloorY() : -0.56f;
            float ballDiam = (container != null && container.particleSize > 0.08f) ? container.particleSize : 0.24f;
            float ballRad = ballDiam * 0.5f;

            // Maximum local Y center for a ball resting at the bottom of its column with no ball underneath it
            float maxBottomFloorCenterLocalY = floorLocalY + ballDiam * 1.15f;

            for (int i = 0; i < activeBalls.Count; i++)
            {
                var b = activeBalls[i];
                if (b == null || b.state != BallState.InContainer || !b.gameObject.activeInHierarchy) continue;

                if (b.rb != null && !b.rb.isKinematic && Mathf.Abs(b.rb.linearVelocity.y) > 1.35f)
                    continue;

                Vector3 bLocal = (container != null)
                    ? container.transform.InverseTransformPoint(b.transform.position)
                    : b.transform.position;

                if (bLocal.y > maxBottomFloorCenterLocalY)
                    continue;

                bool blockedByShelf = false;
                if (container != null && container.dividers != null)
                {
                    for (int d = 0; d < container.dividers.Count; d++)
                    {
                        var div = container.dividers[d];
                        if (div == null || !div.enabled || div.runtimeIsOpened || div.mechanicType != DividerMechanicType.StaticGlass)
                            continue;
                        if (Mathf.Abs(div.angleDegrees) < 55f)
                        {
                            float halfSpanX = div.length * 0.5f;
                            if (bLocal.y > div.center.y - 0.02f && Mathf.Abs(bLocal.x - div.center.x) <= halfSpanX)
                            {
                                blockedByShelf = true;
                                break;
                            }
                        }
                    }
                }
                if (blockedByShelf) continue;

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
                    if (dy > ballRad * 0.35f && dx < ballDiam * 0.85f)
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

            return bottomCandidates;
        }

        /// <summary>
        /// Physically evaluates which balls are resting on the bottom/near side of the glass container with zero balls
        /// blocking them from below, and matches them to the closest hungry active Carrier Block.
        /// </summary>
        private BallPhysicsController FindNextCollectableBall(out ActiveMonsterStream targetStream)
        {
            targetStream = null;
            List<BallPhysicsController> bottomCandidates = CollectBottomCandidates();
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

                for (int i = 0; i < bottomCandidates.Count; i++)
                {
                    var cand = bottomCandidates[i];
                    if (cand.colorType != monster.colorType) continue;
                    if (!IsBallInMonsterCollectionZone(monster, cand)) continue;

                    float distX = Mathf.Abs(cand.transform.position.x - monsterWorldX);
                    // Prefer lower layerIndex first (strictly bottom-to-top), then closest horizontal distance to the carrier box's slot
                    float score = cand.layerIndex * 0.50f + distX;
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
                    if (!IsBallInMonsterCollectionZone(monster, b)) continue;
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

                // If the target block finished or became inactive while a ball was inside the container, re-route or return
                MonsterCharacter targetMonster = ball.TargetMonster;
                if (targetMonster == null || !targetMonster.IsActivelyCollecting)
                {
                    targetMonster = FindMonsterForBall(ball);
                    if (targetMonster == null)
                    {
                        ball.CancelAttractionAndReturnToContainer();
                        _attractingBalls.RemoveAt(i);
                        continue;
                    }
                }

                Vector3 exitPos = GetExitWorldPosForBall(container, ball);

                bool isFever = PuzzleGlassGameManager.Instance != null && PuzzleGlassGameManager.Instance.IsFeverActive;
                float pullF = isFever ? (attractionPullForce * 2.0f) : attractionPullForce;
                float maxSpd = isFever ? (maxBallSpeed * 1.95f) : maxBallSpeed;

                ball.ApplyAttractionToExit(exitPos, pullF, maxSpd);

                bool reachedExit = ball.transform.position.y <= exitPos.y + exitTriggerYOffset;
                bool timedOut = ball.AttractionTime > (isFever ? 0.30f : 0.55f);

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

        private MonsterCharacter FindMonsterForBall(BallPhysicsController ball)
        {
            if (ball == null) return null;
            foreach (var kvp in _activeStreams)
            {
                var m = kvp.Value.monster;
                if (m != null && m.IsActivelyCollecting &&
                    m.colorType == ball.colorType &&
                    IsBallInMonsterCollectionZone(m, ball))
                {
                    return m;
                }
            }
            return null;
        }

        private void OnBallReachedMonster(BallPhysicsController ball, MonsterCharacter monster)
        {
            _totalCollectedThisLevel++;
            PuzzleSoundManager.Instance?.PlayBallPop();
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

        public bool HasAnyBallInTransitOrAttracting()
        {
            if (_attractingBalls.Count > 0) return true;
            foreach (var kvp in _activeStreams)
            {
                if (kvp.Value != null && kvp.Value.inTransitBalls != null && kvp.Value.inTransitBalls.Count > 0)
                    return true;
            }
            if (BallObjectPool.Instance != null)
            {
                var active = BallObjectPool.Instance.ActiveBalls;
                for (int i = 0; i < active.Count; i++)
                {
                    var b = active[i];
                    if (b != null && b.gameObject.activeInHierarchy && b.state != BallState.InContainer && b.state != BallState.Pooled)
                        return true;
                    if (b != null && b.gameObject.activeInHierarchy && b.rb != null && !b.rb.isKinematic && b.rb.linearVelocity.sqrMagnitude > 0.15f)
                        return true;
                }
            }
            return false;
        }

        public bool HasAnyCollectableMoveForSlottedMonsters()
        {
            return FindNextCollectableBall(out _) != null;
        }

        public void ClearAll()
        {
            _attractingBalls.Clear();
            _activeStreams.Clear();
            _totalCollectedThisLevel = 0;
            _lastGlobalCollectionTime = -10f;
            _levelStartTime = Time.time;
        }
    }
}
