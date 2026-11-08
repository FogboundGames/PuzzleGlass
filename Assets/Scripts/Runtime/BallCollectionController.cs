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
        public float attractionPullForce = 28f;

        [Tooltip("Maximum velocity for balls while navigating towards exit.")]
        public float maxBallSpeed = 4.2f;

        [Tooltip("Interval between initiating collection of successive balls for a monster.")]
        public float collectionInterval = 0.14f;

        [Tooltip("Maximum balls currently traveling in spout/air per monster.")]
        public int maxBallsInTransitPerMonster = 3;

        [Tooltip("Vertical tolerance around spout bottom to trigger mouth launch.")]
        public float exitTriggerYOffset = 0.08f;

        [Header("State (Read Only)")]
        [SerializeField] private int _totalCollectedThisLevel = 0;

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
        /// Checks all slotted monsters in the SlotSystem and triggers ball attraction.
        /// </summary>
        private void PollSlottedMonsters()
        {
            SlotSystem slotSys = SlotSystem.Instance ?? FindFirstObjectByType<SlotSystem>();
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

            // Process each active monster stream
            float now = Time.time;
            foreach (var kvp in _activeStreams)
            {
                var stream = kvp.Value;
                var monster = stream.monster;

                // Clean up finished balls in transit
                for (int b = stream.inTransitBalls.Count - 1; b >= 0; b--)
                {
                    var ball = stream.inTransitBalls[b];
                    if (ball == null || ball.state == BallState.Pooled || !ball.gameObject.activeInHierarchy)
                    {
                        stream.inTransitBalls.RemoveAt(b);
                    }
                }

                if (stream.inTransitBalls.Count >= maxBallsInTransitPerMonster) continue;
                if (now - stream.lastCollectionTime < collectionInterval) continue;

                // How many more balls does the monster need including balls already in flight?
                int needed = monster.remainingCount - stream.inTransitBalls.Count;
                if (needed <= 0) continue;

                // Find best candidate ball in container
                BallPhysicsController bestBall = FindNextMatchingBall(monster.colorType);
                if (bestBall != null)
                {
                    StartBallAttraction(bestBall, monster, stream);
                    stream.lastCollectionTime = now;
                }
            }
        }

        /// <summary>
        /// Selects the matching ball closest to the bottom spout exit (lowest Y, centered X).
        /// </summary>
        private BallPhysicsController FindNextMatchingBall(PuzzleColor targetColor)
        {
            if (BallObjectPool.Instance == null) return null;

            var activeBalls = BallObjectPool.Instance.ActiveBalls;
            BallPhysicsController bestBall = null;
            float lowestY = float.MaxValue;
            float closestAbsX = float.MaxValue;

            for (int i = 0; i < activeBalls.Count; i++)
            {
                var ball = activeBalls[i];
                if (ball == null || ball.state != BallState.InContainer) continue;
                if (ball.colorType != targetColor) continue;

                Vector3 pos = ball.transform.position;
                if (pos.y < lowestY || (Mathf.Abs(pos.y - lowestY) < 0.05f && Mathf.Abs(pos.x) < closestAbsX))
                {
                    lowestY = pos.y;
                    closestAbsX = Mathf.Abs(pos.x);
                    bestBall = ball;
                }
            }

            return bestBall;
        }

        private void StartBallAttraction(BallPhysicsController ball, MonsterCharacter monster, ActiveMonsterStream stream)
        {
            ball.state = BallState.AttractingToExit;
            stream.inTransitBalls.Add(ball);

            if (!_attractingBalls.Contains(ball))
            {
                _attractingBalls.Add(ball);
            }

            // Disable collision between this collecting ball and the bottom spout stopper
            // so it can physically slide through the spout while non-matching balls stay inside!
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

            float spoutExitY = container != null ? container.spoutBottomY : -0.70f;
            Vector3 exitPos = new Vector3(0f, spoutExitY, -0.10f);

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

                // Check if ball reached or passed the spout exit threshold
                if (ball.transform.position.y <= spoutExitY + exitTriggerYOffset)
                {
                    _attractingBalls.RemoveAt(i);

                    // Find which monster is waiting for this ball
                    MonsterCharacter targetMonster = FindMonsterForColor(ball.colorType);
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
        }
    }
}
