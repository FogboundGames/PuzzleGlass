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
        public float flyDuration = 0.22f;

        /// <summary>
        /// Physically realistic two-phase cube dispatch:
        /// Phase 1: Glides inside the glass channel along the spline path down to the spout exit opening.
        /// Phase 2: Emerges from the spout nozzle into open air and follows a natural gravity arc into the monster's mouth.
        /// Scale remains 100% full original size (no shrinking).
        /// </summary>
        public void LaunchThroughGlassTrack(
            Vector3[] trackWaypoints,
            Vector3 spoutExitPos,
            MonsterCharacter target,
            PuzzleColor color,
            Material cubeMat,
            Mesh cubeMesh)
        {
            targetMonster = target;
            colorType = color;

            // Retain full 100% original scale (do not shrink down)
            if (transform.localScale == Vector3.zero || transform.localScale.x < 0.01f)
            {
                transform.localScale = Vector3.one;
            }

            MeshFilter mf = GetComponent<MeshFilter>();
            if (mf == null) mf = gameObject.AddComponent<MeshFilter>();
            if (mf.sharedMesh == null) mf.sharedMesh = cubeMesh;

            MeshRenderer mr = GetComponent<MeshRenderer>();
            if (mr == null) mr = gameObject.AddComponent<MeshRenderer>();
            if (mr.sharedMaterial == null) mr.sharedMaterial = cubeMat;

            Vector3 targetMouth = (targetMonster != null && targetMonster.mouthTargetPoint != null)
                ? targetMonster.mouthTargetPoint.position
                : (targetMonster != null ? targetMonster.transform.position + new Vector3(0, 0.25f, -0.1f) : spoutExitPos + Vector3.down);

            Sequence transferSeq = DOTween.Sequence();

            // PHASE 1: Move inside the glass track channel down towards the spout exit
            float slideDuration = 0f;
            if (trackWaypoints != null && trackWaypoints.Length > 1)
            {
                transform.position = trackWaypoints[0];

                float pathDist = 0f;
                for (int i = 1; i < trackWaypoints.Length; i++)
                {
                    pathDist += Vector3.Distance(trackWaypoints[i], trackWaypoints[i - 1]);
                }
                slideDuration = Mathf.Clamp(pathDist * 0.08f, 0.04f, 0.16f);

                // Slide along the channel accelerating down the chute
                transferSeq.Append(transform.DOPath(trackWaypoints, slideDuration, PathType.Linear).SetEase(Ease.InQuad));
            }
            else
            {
                transform.position = spoutExitPos;
            }

            // PHASE 2: Pouring & cascading out of the spout nozzle into the monster's mouth
            // Creates a fluid, natural waterfall/spill of cubes with gravity acceleration and lively tumbling
            float jitterX = UnityEngine.Random.Range(-0.045f, 0.045f);
            float jitterZ = UnityEngine.Random.Range(-0.035f, 0.035f);

            Vector3 p0 = spoutExitPos;
            // Immediate downward-sagging exit velocity from chute
            Vector3 p1 = Vector3.Lerp(spoutExitPos, targetMouth, 0.30f) + new Vector3(jitterX, -0.09f, jitterZ + 0.04f);
            // Gravity acceleration towards mouth
            Vector3 p2 = Vector3.Lerp(spoutExitPos, targetMouth, 0.72f) + new Vector3(jitterX * 0.4f, -0.05f, jitterZ * 0.4f);
            Vector3 p3 = targetMouth;

            Vector3[] pourPath = new Vector3[] { p0, p1, p2, p3 };
            float pourDuration = 0.20f;

            // Cascading fall curve accelerating downward
            transferSeq.Append(transform.DOPath(pourPath, pourDuration, PathType.CatmullRom).SetEase(Ease.InQuad));

            // Organic tumbling spin as it pours through the air
            Vector3 tumbleRotation = new Vector3(
                UnityEngine.Random.Range(220f, 380f),
                UnityEngine.Random.Range(-180f, 180f),
                UnityEngine.Random.Range(-90f, 90f)
            );
            transferSeq.Join(transform.DORotate(tumbleRotation, pourDuration, RotateMode.WorldAxisAdd).SetEase(Ease.Linear));

            // Quick swallow into mouth at the very end
            float totalDuration = slideDuration + pourDuration;
            transferSeq.Insert(totalDuration - 0.04f, transform.DOScale(Vector3.one * 0.65f, 0.04f).SetEase(Ease.InQuad));

            transferSeq.OnComplete(() =>
            {
                if (targetMonster != null)
                {
                    targetMonster.FeedCube();
                }
                Destroy(gameObject);
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
