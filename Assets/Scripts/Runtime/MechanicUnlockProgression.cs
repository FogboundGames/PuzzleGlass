using System.Collections.Generic;
using UnityEngine;

namespace PuzzleGlass
{
    public enum GameFeatureKind
    {
        FilledBox = 0,
        FrozenBlock = 1,
        LockAndKey = 2,
        LinkedPair = 3
    }

    public struct FeatureMilestone
    {
        public GameFeatureKind kind;
        public string featureTitle;
        public int unlockLevel;
        public LevelMonsterEntry sampleEntry;
        public LevelMonsterEntry samplePartnerEntry;
    }

    public struct FeatureProgressInfo
    {
        public bool hasUpcoming;
        public GameFeatureKind kind;
        public string featureTitle;
        public int unlockLevel;
        public int prevPercent;
        public int newPercent;
        public int currentStep;
        public int totalSteps;
        public bool isJustUnlocked;
        public LevelMonsterEntry sampleEntry;
        public LevelMonsterEntry samplePartnerEntry;
    }

    /// <summary>
    /// Scans the game's ordered levels to detect when each existing mechanic first appears
    /// and computes level-by-level percentage progress toward the next feature unlock.
    /// </summary>
    public static class MechanicUnlockProgression
    {
        public static List<FeatureMilestone> BuildMilestones(IReadOnlyList<PuzzleGlassLevelData> levels)
        {
            var result = new List<FeatureMilestone>(3);
            if (levels == null || levels.Count == 0) return result;

            var sorted = new List<PuzzleGlassLevelData>(levels.Count);
            for (int i = 0; i < levels.Count; i++)
            {
                if (levels[i] != null) sorted.Add(levels[i]);
            }
            sorted.Sort((a, b) => a.levelNumber.CompareTo(b.levelNumber));

            bool foundFilled = false;
            bool foundFrozen = false;
            bool foundLock = false;
            bool foundLinked = false;

            for (int i = 0; i < sorted.Count; i++)
            {
                var lvl = sorted[i];
                int lvlNum = Mathf.Max(1, lvl.levelNumber);

                if (!foundFilled && HasFilledBoxMechanic(lvl, out var filledSample))
                {
                    foundFilled = true;
                    result.Add(new FeatureMilestone
                    {
                        kind = GameFeatureKind.FilledBox,
                        featureTitle = "FILLED BOX",
                        unlockLevel = lvlNum,
                        sampleEntry = filledSample
                    });
                }

                if (!foundFrozen && HasFrozenMechanic(lvl, out var frozenSample))
                {
                    foundFrozen = true;
                    result.Add(new FeatureMilestone
                    {
                        kind = GameFeatureKind.FrozenBlock,
                        featureTitle = "FROZEN BLOCK",
                        unlockLevel = lvlNum,
                        sampleEntry = frozenSample
                    });
                }

                if (!foundLock && HasLockKeyMechanic(lvl, out var lockSample))
                {
                    foundLock = true;
                    result.Add(new FeatureMilestone
                    {
                        kind = GameFeatureKind.LockAndKey,
                        featureTitle = "LOCK & KEY",
                        unlockLevel = lvlNum,
                        sampleEntry = lockSample
                    });
                }

                if (!foundLinked && HasLinkedPairMechanic(lvl, out var linkedA, out var linkedB))
                {
                    foundLinked = true;
                    result.Add(new FeatureMilestone
                    {
                        kind = GameFeatureKind.LinkedPair,
                        featureTitle = "LINKED BOXES",
                        unlockLevel = lvlNum,
                        sampleEntry = linkedA,
                        samplePartnerEntry = linkedB
                    });
                }
            }

            result.Sort((a, b) => a.unlockLevel.CompareTo(b.unlockLevel));
            return result;
        }

        public static bool TryGetProgressForCompletedLevel(
            IReadOnlyList<PuzzleGlassLevelData> levels,
            int completedLevelNumber,
            out FeatureProgressInfo info)
        {
            info = default;
            if (completedLevelNumber < 1) return false;

            var milestones = BuildMilestones(levels);
            if (milestones.Count == 0) return false;

            int targetIdx = -1;
            for (int i = 0; i < milestones.Count; i++)
            {
                if (milestones[i].unlockLevel > completedLevelNumber)
                {
                    targetIdx = i;
                    break;
                }
            }

            if (targetIdx < 0) return false;

            var target = milestones[targetIdx];
            int prevUnlockLevel = targetIdx > 0 ? milestones[targetIdx - 1].unlockLevel : 1;
            int span = Mathf.Max(1, target.unlockLevel - prevUnlockLevel);

            int prevSteps = Mathf.Clamp(completedLevelNumber - prevUnlockLevel, 0, span);
            int newSteps = Mathf.Clamp(completedLevelNumber - prevUnlockLevel + 1, 0, span);

            int prevPct = Mathf.RoundToInt((prevSteps / (float)span) * 100f);
            int newPct = Mathf.RoundToInt((newSteps / (float)span) * 100f);

            info = new FeatureProgressInfo
            {
                hasUpcoming = true,
                kind = target.kind,
                featureTitle = target.featureTitle,
                unlockLevel = target.unlockLevel,
                prevPercent = prevPct,
                newPercent = newPct,
                currentStep = newSteps,
                totalSteps = span,
                isJustUnlocked = (completedLevelNumber + 1 >= target.unlockLevel) || newPct >= 100,
                sampleEntry = target.sampleEntry,
                samplePartnerEntry = target.samplePartnerEntry
            };
            return true;
        }

        private static bool HasFilledBoxMechanic(PuzzleGlassLevelData lvl, out LevelMonsterEntry sample)
        {
            sample = null;
            if (lvl.monsters != null)
            {
                for (int i = 0; i < lvl.monsters.Count; i++)
                {
                    var m = lvl.monsters[i];
                    if (m != null && m.isFilledBox)
                    {
                        sample = new LevelMonsterEntry(m.color, m.count, CarrierBlockShape.Rect1x1, Vector2Int.zero)
                        {
                            isFilledBox = true,
                            filledBallColor = m.filledBallColor
                        };
                        return true;
                    }
                }
            }
            if (lvl.tutorial == LevelTutorial.FilledBox)
            {
                sample = new LevelMonsterEntry(PuzzleColor.Purple, 18, CarrierBlockShape.Rect1x1, Vector2Int.zero)
                {
                    isFilledBox = true,
                    filledBallColor = PuzzleColor.Pink
                };
                return true;
            }
            return false;
        }

        private static bool HasFrozenMechanic(PuzzleGlassLevelData lvl, out LevelMonsterEntry sample)
        {
            sample = null;
            if (lvl.monsters != null)
            {
                for (int i = 0; i < lvl.monsters.Count; i++)
                {
                    var m = lvl.monsters[i];
                    if (m != null && m.iceCount > 0)
                    {
                        sample = new LevelMonsterEntry(m.color, m.count, CarrierBlockShape.Rect1x1, Vector2Int.zero)
                        {
                            iceCount = m.iceCount
                        };
                        return true;
                    }
                }
            }
            if (lvl.tutorial == LevelTutorial.FrozenBlock)
            {
                sample = new LevelMonsterEntry(PuzzleColor.Blue, 18, CarrierBlockShape.Rect1x1, Vector2Int.zero)
                {
                    iceCount = 15
                };
                return true;
            }
            return false;
        }

        private static bool HasLockKeyMechanic(PuzzleGlassLevelData lvl, out LevelMonsterEntry sample)
        {
            sample = null;
            if (lvl.monsters != null)
            {
                LevelMonsterEntry locked = null;
                bool anyKey = false;
                for (int i = 0; i < lvl.monsters.Count; i++)
                {
                    var m = lvl.monsters[i];
                    if (m == null) continue;
                    if (m.hasKey) anyKey = true;
                    if (locked == null && m.lockKeyCount > 0) locked = m;
                }
                if (locked != null)
                {
                    // Show both the padlock and key badge together so the Lock & Key mechanic reads at a glance
                    sample = new LevelMonsterEntry(locked.color, locked.count, CarrierBlockShape.Rect1x1, Vector2Int.zero)
                    {
                        lockKeyCount = Mathf.Max(1, locked.lockKeyCount),
                        hasKey = true
                    };
                    return true;
                }
                if (anyKey)
                {
                    sample = new LevelMonsterEntry(PuzzleColor.Green, 18, CarrierBlockShape.Rect1x1, Vector2Int.zero)
                    {
                        lockKeyCount = 1,
                        hasKey = true
                    };
                    return true;
                }
            }
            if (lvl.tutorial == LevelTutorial.LockAndKey)
            {
                sample = new LevelMonsterEntry(PuzzleColor.Green, 18, CarrierBlockShape.Rect1x1, Vector2Int.zero)
                {
                    lockKeyCount = 1,
                    hasKey = true
                };
                return true;
            }
            return false;
        }

        private static bool HasLinkedPairMechanic(PuzzleGlassLevelData lvl, out LevelMonsterEntry sampleA, out LevelMonsterEntry sampleB)
        {
            sampleA = null;
            sampleB = null;
            if (lvl == null || lvl.monsters == null) return false;
            var firstById = new Dictionary<int, LevelMonsterEntry>();
            for (int i = 0; i < lvl.monsters.Count; i++)
            {
                var m = lvl.monsters[i];
                if (m == null || m.linkGroupId <= 0) continue;
                if (firstById.TryGetValue(m.linkGroupId, out var first))
                {
                    sampleA = new LevelMonsterEntry(first.color, first.count, CarrierBlockShape.Rect1x1, Vector2Int.zero) { linkGroupId = 1 };
                    sampleB = new LevelMonsterEntry(m.color, m.count, CarrierBlockShape.Rect1x1, Vector2Int.right) { linkGroupId = 1 };
                    return true;
                }
                firstById[m.linkGroupId] = m;
            }
            return false;
        }
    }
}
