using System;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

namespace PuzzleGlass
{
    public class FlyingCube : MonoBehaviour
    {
        public PuzzleColor colorType;
        public MonsterCharacter targetMonster;

        // Sand grain mesh variations for organic texture diversity
        private static Mesh _grainSmallMesh;
        private static Mesh _grainMedMesh;
        private static Mesh _grainLargeMesh;

        private static void EnsureGrainMeshes()
        {
            if (_grainSmallMesh == null)
            {
                _grainSmallMesh = ProceduralMeshUtils.CreateRoundedCube(0.034f, 0.008f, 1);
                _grainSmallMesh.name = "SandGrain_Small";
            }
            if (_grainMedMesh == null)
            {
                _grainMedMesh = ProceduralMeshUtils.CreateRoundedCube(0.046f, 0.012f, 2);
                _grainMedMesh.name = "SandGrain_Medium";
            }
            if (_grainLargeMesh == null)
            {
                _grainLargeMesh = ProceduralMeshUtils.CreateRoundedCube(0.058f, 0.014f, 2);
                _grainLargeMesh.name = "SandGrain_Large";
            }
        }

        /// <summary>
        /// Realistic Granular Sand Flow Transfer:
        /// The cube funnels into the bottle nozzle, disintegrates into a cascading stream
        /// of 32 fluid candy sand particles pouring directly into the monster's open mouth!
        /// </summary>
        public void LaunchThroughGlassTrack(
            Vector3[] trackWaypoints,
            Vector3 spoutExitPos,
            MonsterCharacter target,
            PuzzleColor color,
            Material cubeMat,
            Mesh cubeMesh)
        {
            LaunchAsSandFlow(spoutExitPos, target, color, cubeMat);
        }

        public void LaunchAsSandFlow(
            Vector3 spoutExitPos,
            MonsterCharacter target,
            PuzzleColor color,
            Material cubeMat)
        {
            targetMonster = target;
            colorType = color;
            EnsureGrainMeshes();

            Vector3 targetMouth = (targetMonster != null && targetMonster.mouthTargetPoint != null)
                ? targetMonster.mouthTargetPoint.position
                : (targetMonster != null ? targetMonster.transform.position + new Vector3(0, 0.25f, -0.1f) : spoutExitPos + Vector3.down);

            // Phase 1: Front cube swiftly slides into the nozzle and dissolves into sand
            transform.DOMove(spoutExitPos, 0.04f).SetEase(Ease.InQuad);
            transform.DOScale(Vector3.zero, 0.04f).SetEase(Ease.InQuad);

            // Phase 2: Flowing Sand Stream (32 cascading granular sand particles)
            int particleCount = 32;
            GameObject streamRoot = new GameObject($"SandStream_{color}");
            streamRoot.transform.position = spoutExitPos;

            float streamDuration = 0.28f;
            float totalDelaySpread = 0.14f;

            for (int i = 0; i < particleCount; i++)
            {
                float delay = (float)i / particleCount * totalDelaySpread;

                // Stream dispersion: tight at nozzle, organic fluid width mid-air, funnels into mouth
                float jitterX = UnityEngine.Random.Range(-0.035f, 0.035f);
                float jitterZ = UnityEngine.Random.Range(-0.025f, 0.025f);

                Vector3 p0 = spoutExitPos + new Vector3(jitterX * 0.25f, 0f, jitterZ * 0.25f);
                Vector3 p1 = Vector3.Lerp(spoutExitPos, targetMouth, 0.32f) + new Vector3(jitterX, -0.06f, jitterZ + 0.03f);
                Vector3 p2 = Vector3.Lerp(spoutExitPos, targetMouth, 0.70f) + new Vector3(jitterX * 0.45f, -0.03f, jitterZ * 0.45f);
                Vector3 p3 = targetMouth;
                Vector3[] pourPath = new Vector3[] { p0, p1, p2, p3 };

                GameObject grain = new GameObject($"Grain_{i}");
                grain.transform.SetParent(streamRoot.transform, false);
                grain.transform.position = p0;
                grain.transform.localScale = Vector3.zero;

                // Select randomized grain size for organic granular sand look
                int sizeType = i % 3;
                Mesh grainMesh = (sizeType == 0) ? _grainSmallMesh : (sizeType == 1 ? _grainMedMesh : _grainLargeMesh);

                MeshFilter mf = grain.AddComponent<MeshFilter>();
                mf.sharedMesh = grainMesh;

                MeshRenderer mr = grain.AddComponent<MeshRenderer>();
                mr.sharedMaterial = cubeMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;

                float grainFlyTime = 0.17f + UnityEngine.Random.Range(-0.02f, 0.02f);
                Sequence grainSeq = DOTween.Sequence();
                grainSeq.SetDelay(delay);

                // Emerge from nozzle
                grainSeq.Append(grain.transform.DOScale(Vector3.one, 0.025f).SetEase(Ease.OutBack));

                // Flow along fluid parabolic stream with gravitational acceleration
                grainSeq.Join(grain.transform.DOPath(pourPath, grainFlyTime, PathType.CatmullRom).SetEase(Ease.InQuad));

                // Organic tumbling spin in flight
                Vector3 spin = new Vector3(
                    UnityEngine.Random.Range(200f, 400f),
                    UnityEngine.Random.Range(-220f, 220f),
                    UnityEngine.Random.Range(-120f, 120f)
                );
                grainSeq.Join(grain.transform.DORotate(spin, grainFlyTime, RotateMode.WorldAxisAdd).SetEase(Ease.Linear));

                // Funnel and swallow into mouth at the end
                grainSeq.Insert(delay + grainFlyTime - 0.035f, grain.transform.DOScale(Vector3.zero, 0.035f).SetEase(Ease.InQuad));
            }

            // Phase 3: Monster swallows and reacts as the stream flows in
            DOVirtual.DelayedCall(0.18f, () =>
            {
                if (targetMonster != null)
                {
                    targetMonster.FeedCube();
                }
            });

            // Cleanup stream root and main object when pouring finishes
            DOVirtual.DelayedCall(streamDuration + 0.10f, () =>
            {
                if (streamRoot != null) Destroy(streamRoot);
                if (gameObject != null) Destroy(gameObject);
            });
        }

        /// <summary>
        /// Backward-compatible launch from a single point.
        /// </summary>
        public void Launch(Vector3 start, MonsterCharacter target, PuzzleColor color, Material cubeMat, Mesh cubeMesh)
        {
            LaunchThroughGlassTrack(new Vector3[] { start }, start, target, color, cubeMat, cubeMesh);
        }

        private void OnDestroy()
        {
            transform.DOKill();
        }
    }
}
