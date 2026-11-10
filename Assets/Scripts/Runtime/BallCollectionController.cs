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
        public float attractionPullForce = 16f;

        [Tooltip("Maximum velocity for balls while navigating towards exit.")]
        public float maxBallSpeed = 2.4f;

        [Tooltip("Interval between initiating collection of successive balls from the spout.")]
        [Range(0.18f, 0.60f)]
        public float collectionInterval = 0.32f;

        [Tooltip("Maximum balls simultaneously traveling through the spout neck.")]
        public int maxBallsInSpout = 1;

        [Tooltip("Maximum balls currently traveling in air per monster.")]
        public int maxBallsInTransitPerMonster = 2;

        [Tooltip("Vertical tolerance around spout bottom to trigger mouth launch.")]
        public float exitTriggerYOffset = 0.14f;

        [Tooltip("Vertical tolerance for balls considered part of the bottom layer in container.")]
        public float bottomLayerTolerance = 0.07f;

        [Header("State (Read Only)")]
        [SerializeField] private int _totalCollectedThisLevel = 0;
        private float _lastGlobalCollectionTime = -10f;

        private class ActiveMonsterStream
        {
            public MonsterCharacter monster;
            public float lastCollectionTime;
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
                    lastCollectionTime = -10f
                };
            }
        }

        /// <summary>
        /// Checks slotted monsters and attracts ONLY the bottom-most matching ball.
        /// Upper balls cannot be pulled through other balls.
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
                            lastCollectionTime = -10f
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

            // Pacing & congestion limit: ensure smooth, readable flow without overcrowding the spout
            float now = Time.time;
            if (now - _lastGlobalCollectionTime < collectionInterval) return;
            if (_attractingBalls.Count >= maxBallsInSpout) return;

            // Find strictly the bottom-most matching ball (upper layer balls are blocked)
            BallPhysicsController bestBall = FindNextCollectableBall(out ActiveMonsterStream targetStream);
            if (bestBall != null && targetStream != null)
            {
                StartBallAttraction(bestBall, targetStream.monster, targetStream);
                _lastGlobalCollectionTime = now;
                targetStream.lastCollectionTime = now;
            }
        }

        /// <summary>
        /// Searches only the bottom-most layer of balls. If a color is higher up in the container,
        /// it CANNOT be collected until lower balls have emptied and it falls to the bottom.
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

            float botY = (container != null && container.contourPoints != null && container.contourPoints.Count > 0)
                ? container.contourPoints[0].y * container.heightScale
                : (container != null ? container.spoutBottomY : -1.20f);

            float spoutCenterX = (container != null && container.contourPoints != null && container.contourPoints.Count > 0)
                ? (container.symmetricMode ? 0f : container.contourPoints[0].centerX) * container.widthScale
                : 0f;

            Vector3 localExit = new Vector3(spoutCenterX, botY, -0.10f);
            Vector3 exitPos = (container != null) ? container.transform.TransformPoint(localExit) : localExit;

            float throatY = (container != null) ? container.GetSpoutThroatY() : -0.72f;
            float worldThroatY = (container != null) ? container.transform.TransformPoint(new Vector3(0f, throatY, 0f)).y : throatY;

            // 1. Find the lowest Y position among resting balls in the container
            float lowestY = float.MaxValue;
            int restingCount = 0;
            for (int i = 0; i < activeBalls.Count; i++)
            {
                var b = activeBalls[i];
                if (b == null || b.state != BallState.InContainer || !b.gameObject.activeInHierarchy) continue;
                restingCount++;
                if (b.transform.position.y < lowestY)
                {
                    lowestY = b.transform.position.y;
                }
            }

            if (restingCount == 0 || lowestY == float.MaxValue) return null;

            // 2. Tolerance check:
            // If the bottom-most ball is inside the vertical spout tube, the tube is single-lane:
            // ONLY that specific bottom ball can exit.
            // If in the wider hopper, only balls within bottomLayerTolerance of the lowest Y are eligible.
            bool lowestInSpout = lowestY <= worldThroatY + 0.04f;
            float currentTolerance = lowestInSpout ? 0.035f : bottomLayerTolerance;

            // 3. Filter candidates strictly to the bottom layer
            List<BallPhysicsController> candidates = new List<BallPhysicsController>();
            for (int i = 0; i < activeBalls.Count; i++)
            {
                var b = activeBalls[i];
                if (b == null || b.state != BallState.InContainer || !b.gameObject.activeInHierarchy) continue;

                if (b.transform.position.y <= lowestY + currentTolerance)
                {
                    candidates.Add(b);
                }
            }

            if (candidates.Count == 0) return null;

            // Sort candidates by closeness to spout exit
            candidates.Sort((a, b) =>
            {
                float distA = Vector2.Distance(new Vector2(a.transform.position.x, a.transform.position.y), new Vector2(exitPos.x, exitPos.y));
                float distB = Vector2.Distance(new Vector2(b.transform.position.x, b.transform.position.y), new Vector2(exitPos.x, exitPos.y));
                return distA.CompareTo(distB);
            });

            // 4. Check if any candidate matches an active, hungry slotted monster
            for (int i = 0; i < candidates.Count; i++)
            {
                var cand = candidates[i];
                foreach (var kvp in _activeStreams)
                {
                    var stream = kvp.Value;
                    var monster = stream.monster;
                    if (monster == null || monster.colorType != cand.colorType) continue;

                    int needed = monster.remainingCount - stream.inTransitBalls.Count;
                    if (needed <= 0) continue;
                    if (stream.inTransitBalls.Count >= maxBallsInTransitPerMonster) continue;

                    targetStream = stream;
                    return cand;
                }

                // If lowest ball is inside the spout tube, it blocks the exit — no other ball can pass it
                if (lowestInSpout)
                {
                    break;
                }
            }

            return null;
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

            float botY = (container != null && container.contourPoints != null && container.contourPoints.Count > 0)
                ? container.contourPoints[0].y * container.heightScale
                : (container != null ? container.spoutBottomY : -1.20f);

            float spoutCenterX = (container != null && container.contourPoints != null && container.contourPoints.Count > 0)
                ? (container.symmetricMode ? 0f : container.contourPoints[0].centerX) * container.widthScale
                : 0f;

            Vector3 localExit = new Vector3(spoutCenterX, botY, -0.10f);
            Vector3 exitPos = (container != null) ? container.transform.TransformPoint(localExit) : localExit;

            // Start attraction flow with trigger collision so it glides smoothly past other balls and stopper
            ball.BeginAttraction(monster, exitPos);

            // Also explicitly disable collision with stopper as extra guarantee
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

            float botY = (container != null && container.contourPoints != null && container.contourPoints.Count > 0)
                ? container.contourPoints[0].y * container.heightScale
                : (container != null ? container.spoutBottomY : -1.20f);

            float spoutCenterX = (container != null && container.contourPoints != null && container.contourPoints.Count > 0)
                ? (container.symmetricMode ? 0f : container.contourPoints[0].centerX) * container.widthScale
                : 0f;

            Vector3 localExit = new Vector3(spoutCenterX, botY, -0.10f);
            Vector3 exitPos = (container != null) ? container.transform.TransformPoint(localExit) : localExit;

            for (int i = _attractingBalls.Count - 1; i >= 0; i--)
            {
                var ball = _attractingBalls[i];
                if (ball == null || ball.state != BallState.AttractingToExit || !ball.gameObject.activeInHierarchy)
                {
                    _attractingBalls.RemoveAt(i);
                    continue;
                }

                // Apply downward attraction force towards spout exit
                ball.ApplyAttractionToExit(exitPos, attractionPullForce, maxBallSpeed);

                // Check if ball reached or passed the spout exit threshold OR timed out fail-safe
                bool reachedExit = ball.transform.position.y <= exitPos.y + exitTriggerYOffset;
                bool timedOut = ball.AttractionTime > 1.25f;

                if (reachedExit || timedOut)
                {
                    _attractingBalls.RemoveAt(i);

                    // Prefer the monster that ordered this ball, fallback to any matching monster
                    MonsterCharacter targetMonster = ball.TargetMonster;
                    if (targetMonster == null || targetMonster.remainingCount <= 0 || !targetMonster.gameObject.activeInHierarchy)
                    {
                        targetMonster = FindMonsterForColor(ball.colorType);
                    }

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
                        // Fallback recycle if monster was detached mid-air
                        ball.RecycleSelf();
                    }
                }
            }
        }

        private MonsterCharacter FindMonsterForColor(PuzzleColor color)
        {
            foreach (var kvp in _activeStreams)
            {
                if (kvp.Value.monster != null && kvp.Value.monster.colorType == color && kvp.Value.monster.remainingCount > 0)
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

            // Check if level has finished all goals
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