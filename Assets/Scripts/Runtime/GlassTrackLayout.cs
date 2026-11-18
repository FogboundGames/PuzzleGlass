using System;
using System.Collections.Generic;
using UnityEngine;

namespace PuzzleGlass
{
    public enum GlassTrackPreset
    {
        ArchLoop = 0,    // Görsel 1: Dış U + İç Kemer Köprü + Orta Ada
        HeartLoop = 1,   // Görsel 3: Kalp / M-Kıvrımlı Pist + Orta Ada
        UChannel = 2,    // Düzgün U-Kanal
        ORing = 3,       // Kapalı Halka (O-Pist)
        SCurve = 4,      // Kıvrımlı S-Kanal
        DoubleArch = 5,  // Çift Kemerli Pist
        Custom = 6       // Cam Stüdyosu'nda Serbest Düzenlenen Pist
    }

    [Serializable]
    public class GlassTrackBranch
    {
        public string name = "Kol";
        [Tooltip("True: İki kol arasına köprü kurar (ortadan iki uca akar). False: Çıkıştan yukarı uzanan ana kol.")]
        public bool isBridge = false;
        public List<Vector2> points = new List<Vector2>();

        public GlassTrackBranch() { }

        public GlassTrackBranch(string name, bool isBridge, params Vector2[] pts)
        {
            this.name = name;
            this.isBridge = isBridge;
            if (pts != null) this.points = new List<Vector2>(pts);
        }

        public GlassTrackBranch Clone()
        {
            return new GlassTrackBranch(name, isBridge, points != null ? points.ToArray() : null);
        }
    }

    /// <summary>
    /// Görsel 1 ve Görsel 3 tarzı çoklu-kollu, iç adalı eğrisel cam pist/kanal geometrisi,
    /// 2D SDF + akış alanı (TrackField2D), Marching Squares duvar çıkarımı ve sıkı top dizilimi.
    /// </summary>
    public static class GlassTrackLayout
    {
        public const int MaxShaderSegments = 64;
        private const int FieldRes = 72;

        public struct TrackSegment
        {
            public Vector2 a;
            public Vector2 b;
            public int branchId;
            public float distA;
            public float distB;
        }

        public struct TrackSlot
        {
            public Vector2 localPos;
            public int branchId;
            public int rowIndex;
            public int laneIndex;
            public float distToExit;
        }

        public sealed class CompiledTrack
        {
            public GlassTrackPreset preset;
            public List<List<Vector2>> polylines = new List<List<Vector2>>();
            public List<bool> polylineIsBridge = new List<bool>();
            public List<TrackSegment> segments = new List<TrackSegment>();
            public float innerHalfWidth;
            public float wallThickness;
            public float ballDiameter;
            public int lanesAcross;
            public Vector2 exitPoint;
            public Rect innerBounds;
            public Rect outerBounds;

            // 2D grid field over fieldRect for O(1) physics flow, wall containment, and collider extraction
            public Rect fieldRect;
            public float[] sdf;
            public Vector2[] flowDir;
            public float[] distToExit;
            public Vector2[] wallNormal;

            public void SampleField(Vector2 p, out float signedDist, out Vector2 flow, out float exitDist, out Vector2 inward)
            {
                if (sdf == null || sdf.Length != FieldRes * FieldRes)
                {
                    signedDist = 0f;
                    flow = Vector2.down;
                    exitDist = 0f;
                    inward = Vector2.up;
                    return;
                }

                float u = Mathf.Clamp01((p.x - fieldRect.xMin) / Mathf.Max(0.001f, fieldRect.width)) * (FieldRes - 1);
                float v = Mathf.Clamp01((p.y - fieldRect.yMin) / Mathf.Max(0.001f, fieldRect.height)) * (FieldRes - 1);
                int ix0 = Mathf.Clamp(Mathf.FloorToInt(u), 0, FieldRes - 2);
                int iy0 = Mathf.Clamp(Mathf.FloorToInt(v), 0, FieldRes - 2);
                float tx = u - ix0;
                float ty = v - iy0;

                int i00 = iy0 * FieldRes + ix0;
                int i10 = i00 + 1;
                int i01 = i00 + FieldRes;
                int i11 = i01 + 1;

                signedDist = Mathf.Lerp(Mathf.Lerp(sdf[i00], sdf[i10], tx), Mathf.Lerp(sdf[i01], sdf[i11], tx), ty);
                exitDist = Mathf.Lerp(Mathf.Lerp(distToExit[i00], distToExit[i10], tx), Mathf.Lerp(distToExit[i01], distToExit[i11], tx), ty);

                Vector2 f0 = Vector2.Lerp(flowDir[i00], flowDir[i10], tx);
                Vector2 f1 = Vector2.Lerp(flowDir[i01], flowDir[i11], tx);
                flow = Vector2.Lerp(f0, f1, ty);
                if (flow.sqrMagnitude > 0.0001f) flow.Normalize();
                else flow = Vector2.down;

                Vector2 n0 = Vector2.Lerp(wallNormal[i00], wallNormal[i10], tx);
                Vector2 n1 = Vector2.Lerp(wallNormal[i01], wallNormal[i11], tx);
                inward = Vector2.Lerp(n0, n1, ty);
                if (inward.sqrMagnitude > 0.0001f) inward.Normalize();
                else inward = Vector2.up;
            }

            /// <summary>
            /// Marching Squares ile pistin dış duvarlarını ve tüm iç ada boşluklarını
            /// kapalı/açık duvar segmentleri olarak çıkarır. Her segmentin normali duvar dışına (sdf > 0) bakar.
            /// </summary>
            public List<(Vector2 a, Vector2 b)> ExtractWallSegments()
            {
                var segs = new List<(Vector2 a, Vector2 b)>(256);
                if (sdf == null || sdf.Length != FieldRes * FieldRes) return segs;

                float dx = fieldRect.width / (FieldRes - 1);
                float dy = fieldRect.height / (FieldRes - 1);

                for (int iy = 0; iy < FieldRes - 1; iy++)
                {
                    float y0 = fieldRect.yMin + iy * dy;
                    float y1 = y0 + dy;
                    for (int ix = 0; ix < FieldRes - 1; ix++)
                    {
                        float x0 = fieldRect.xMin + ix * dx;
                        float x1 = x0 + dx;

                        float d00 = sdf[iy * FieldRes + ix];
                        float d10 = sdf[iy * FieldRes + ix + 1];
                        float d11 = sdf[(iy + 1) * FieldRes + ix + 1];
                        float d01 = sdf[(iy + 1) * FieldRes + ix];

                        int mask = (d00 < 0f ? 1 : 0) | (d10 < 0f ? 2 : 0) | (d11 < 0f ? 4 : 0) | (d01 < 0f ? 8 : 0);
                        if (mask == 0 || mask == 15) continue;

                        Vector2 Interp(Vector2 pa, Vector2 pb, float da, float db)
                        {
                            float denom = da - db;
                            float t = Mathf.Abs(denom) > 1e-5f ? Mathf.Clamp01(da / denom) : 0.5f;
                            return Vector2.Lerp(pa, pb, t);
                        }

                        Vector2 p00 = new Vector2(x0, y0);
                        Vector2 p10 = new Vector2(x1, y0);
                        Vector2 p11 = new Vector2(x1, y1);
                        Vector2 p01 = new Vector2(x0, y1);

                        Vector2 eB = Interp(p00, p10, d00, d10);
                        Vector2 eR = Interp(p10, p11, d10, d11);
                        Vector2 eT = Interp(p01, p11, d01, d11);
                        Vector2 eL = Interp(p00, p01, d00, d01);

                        // Orient each segment so left-hand side (or normal) is consistent; we verify normal via SDF gradient
                        void AddOriented(Vector2 u, Vector2 v)
                        {
                            if ((u - v).sqrMagnitude < 1e-6f) return;
                            Vector2 mid = (u + v) * 0.5f;
                            Vector2 grad = EstimateSdfGradient(mid);
                            Vector2 edge = v - u;
                            // Ensure (edge.y, -edge.x) points outward (same direction as grad)
                            Vector2 rightNorm = new Vector2(edge.y, -edge.x);
                            if (Vector2.Dot(rightNorm, grad) < 0f) segs.Add((v, u));
                            else segs.Add((u, v));
                        }

                        switch (mask)
                        {
                            case 1: AddOriented(eL, eB); break;
                            case 2: AddOriented(eB, eR); break;
                            case 3: AddOriented(eL, eR); break;
                            case 4: AddOriented(eR, eT); break;
                            case 5: AddOriented(eL, eT); AddOriented(eR, eB); break;
                            case 6: AddOriented(eB, eT); break;
                            case 7: AddOriented(eL, eT); break;
                            case 8: AddOriented(eT, eL); break;
                            case 9: AddOriented(eT, eB); break;
                            case 10: AddOriented(eB, eL); AddOriented(eT, eR); break;
                            case 11: AddOriented(eT, eR); break;
                            case 12: AddOriented(eR, eL); break;
                            case 13: AddOriented(eR, eB); break;
                            case 14: AddOriented(eB, eL); break;
                        }
                    }
                }
                return segs;
            }

            public Vector2 EstimateSdfGradient(Vector2 p)
            {
                const float eps = 0.015f;
                SampleField(p + new Vector2(eps, 0f), out float rx, out _, out _, out _);
                SampleField(p - new Vector2(eps, 0f), out float lx, out _, out _, out _);
                SampleField(p + new Vector2(0f, eps), out float ty, out _, out _, out _);
                SampleField(p - new Vector2(0f, eps), out float by, out _, out _, out _);
                Vector2 g = new Vector2(rx - lx, ty - by);
                return g.sqrMagnitude > 1e-8f ? g.normalized : Vector2.up;
            }
        }

        // ==================== Hazır Pist Şekilleri ====================

        public static List<GlassTrackBranch> BuildPresetBranches(GlassTrackPreset preset)
        {
            var list = new List<GlassTrackBranch>();
            switch (preset)
            {
                case GlassTrackPreset.ArchLoop:
                default:
                    // Görsel 1 / Bus Traffic Fever Tarzı: Dış U + İç Kemer Köprü + Ortada Kemerli Ada Boşluğu
                    list.Add(new GlassTrackBranch("Sol Kol", false,
                        new Vector2(0.00f, -0.54f),
                        new Vector2(-0.56f, -0.40f),
                        new Vector2(-0.98f, -0.02f),
                        new Vector2(-0.98f, 0.46f),
                        new Vector2(-0.88f, 0.94f),
                        new Vector2(-0.64f, 1.15f)));
                    list.Add(new GlassTrackBranch("Sağ Kol", false,
                        new Vector2(0.00f, -0.54f),
                        new Vector2(0.56f, -0.40f),
                        new Vector2(0.98f, -0.02f),
                        new Vector2(0.98f, 0.46f),
                        new Vector2(0.88f, 0.94f),
                        new Vector2(0.64f, 1.15f)));
                    list.Add(new GlassTrackBranch("İç Kemer", true,
                        new Vector2(-0.82f, 0.16f),
                        new Vector2(-0.46f, 0.48f),
                        new Vector2(0.00f, 0.58f),
                        new Vector2(0.46f, 0.48f),
                        new Vector2(0.82f, 0.16f)));
                    break;

                case GlassTrackPreset.HeartLoop:
                    // Görsel 3: Kalp / M-Kıvrımlı Pist + Üst Ortada V-Çöküntü + Orta Kalp Ada Boşluğu
                    list.Add(new GlassTrackBranch("Sol Kalp Kolu", false,
                        new Vector2(0.00f, -0.56f),
                        new Vector2(-0.54f, -0.32f),
                        new Vector2(-0.92f, 0.10f),
                        new Vector2(-1.08f, 0.54f),
                        new Vector2(-1.14f, 0.92f)));
                    list.Add(new GlassTrackBranch("Sağ Kalp Kolu", false,
                        new Vector2(0.00f, -0.56f),
                        new Vector2(0.54f, -0.32f),
                        new Vector2(0.92f, 0.10f),
                        new Vector2(1.08f, 0.54f),
                        new Vector2(1.14f, 0.92f)));
                    list.Add(new GlassTrackBranch("Üst Kalp Köprüsü", true,
                        new Vector2(-0.98f, 0.40f),
                        new Vector2(-0.56f, 0.76f),
                        new Vector2(-0.24f, 0.54f),
                        new Vector2(0.00f, 0.24f),
                        new Vector2(0.24f, 0.54f),
                        new Vector2(0.56f, 0.76f),
                        new Vector2(0.98f, 0.40f)));
                    break;

                case GlassTrackPreset.UChannel:
                    // Düzgün U-Kanal
                    list.Add(new GlassTrackBranch("Sol Kol", false,
                        new Vector2(0.00f, -0.54f),
                        new Vector2(-0.62f, -0.44f),
                        new Vector2(-0.98f, -0.04f),
                        new Vector2(-1.00f, 0.44f),
                        new Vector2(-0.94f, 0.90f)));
                    list.Add(new GlassTrackBranch("Sağ Kol", false,
                        new Vector2(0.00f, -0.54f),
                        new Vector2(0.62f, -0.44f),
                        new Vector2(0.98f, -0.04f),
                        new Vector2(1.00f, 0.44f),
                        new Vector2(0.94f, 0.90f)));
                    break;

                case GlassTrackPreset.ORing:
                    // Referans Video Tarzı: Kapalı Oval Halka + Sağ Üstten Akış Giriş Kolu
                    list.Add(new GlassTrackBranch("Sol Yay", false,
                        new Vector2(0.00f, -0.56f),
                        new Vector2(-0.78f, -0.42f),
                        new Vector2(-1.06f, 0.08f),
                        new Vector2(-0.74f, 0.60f),
                        new Vector2(0.02f, 0.72f)));
                    list.Add(new GlassTrackBranch("Sağ Yay ve Giriş", false,
                        new Vector2(0.00f, -0.56f),
                        new Vector2(0.76f, -0.42f),
                        new Vector2(1.04f, 0.06f),
                        new Vector2(0.72f, 0.58f),
                        new Vector2(0.98f, 1.04f)));
                    list.Add(new GlassTrackBranch("Üst Bağ", true,
                        new Vector2(-0.18f, 0.71f),
                        new Vector2(0.32f, 0.70f),
                        new Vector2(0.76f, 0.56f)));
                    break;

                case GlassTrackPreset.SCurve:
                    // Kıvrımlı S-Kanal (tek akışlı)
                    list.Add(new GlassTrackBranch("S-Kanal", false,
                        new Vector2(0.00f, -0.56f),
                        new Vector2(0.74f, -0.36f),
                        new Vector2(0.84f, 0.04f),
                        new Vector2(0.00f, 0.22f),
                        new Vector2(-0.78f, 0.42f),
                        new Vector2(-0.68f, 0.84f),
                        new Vector2(0.18f, 0.96f)));
                    break;

                case GlassTrackPreset.DoubleArch:
                    // Çift Kemerli Pist (2 iç ada boşluğu: Alt Kemer ve Üst Kemer köprüleri):
                    // Sol Kol ve Sağ Kol alt çıkıştan yukarı uzanır.
                    // Alt Kemer (isBridge = true) alt köprüyü oluşturur.
                    // Üst Kemer (isBridge = true) üst köprüyü oluşturur.
                    // Sol Kol ve Sağ Kol, Üst Kemer'in başladığı seviyede temizce birleşir; üst üste binen çift boru olmaz!
                    list.Add(new GlassTrackBranch("Sol Kol", false,
                        new Vector2(0.00f, -0.54f),
                        new Vector2(-0.62f, -0.40f),
                        new Vector2(-1.02f, 0.04f),
                        new Vector2(-1.02f, 0.52f),
                        new Vector2(-0.88f, 0.82f)));
                    list.Add(new GlassTrackBranch("Sağ Kol", false,
                        new Vector2(0.00f, -0.54f),
                        new Vector2(0.62f, -0.40f),
                        new Vector2(1.02f, 0.04f),
                        new Vector2(1.02f, 0.52f),
                        new Vector2(0.88f, 0.82f)));
                    list.Add(new GlassTrackBranch("Alt Kemer", true,
                        new Vector2(-0.98f, -0.02f),
                        new Vector2(-0.48f, 0.22f),
                        new Vector2(0.00f, 0.28f),
                        new Vector2(0.48f, 0.22f),
                        new Vector2(0.98f, -0.02f)));
                    list.Add(new GlassTrackBranch("Üst Kemer", true,
                        new Vector2(-0.88f, 0.82f),
                        new Vector2(-0.46f, 1.08f),
                        new Vector2(0.00f, 1.15f),
                        new Vector2(0.46f, 1.08f),
                        new Vector2(0.88f, 0.82f)));
                    break;
            }
            return list;
        }

        public static List<GlassTrackBranch> CloneBranches(List<GlassTrackBranch> src)
        {
            var res = new List<GlassTrackBranch>();
            if (src == null) return res;
            for (int i = 0; i < src.Count; i++)
            {
                if (src[i] != null) res.Add(src[i].Clone());
            }
            return res;
        }

        // ==================== Derleme, Otomatik Sığdırma ve Sıkı Top Dizilimi ====================

        public static void BuildForLevel(
            GlassTrackPreset preset,
            List<GlassTrackBranch> customBranches,
            List<LevelMonsterEntry> bottomToTopGroups,
            List<ManualPoolBallEntry> manualBalls,
            bool useManualLayout,
            int lanesAcrossSetting,
            bool autoFitToBalls,
            float manualScale,
            float wallThickness,
            out CompiledTrack compiled,
            out OrderedPoolLayout.Result layout)
        {
            List<GlassTrackBranch> sourceBranches = (preset == GlassTrackPreset.Custom && customBranches != null && customBranches.Count > 0)
                ? CloneBranches(customBranches)
                : (customBranches != null && customBranches.Count > 0 && preset == GlassTrackPreset.Custom ? CloneBranches(customBranches) : BuildPresetBranches(preset));

            if (sourceBranches.Count == 0)
            {
                sourceBranches = BuildPresetBranches(GlassTrackPreset.ArchLoop);
            }

            // Hedef top listesi (alttan üste)
            List<PuzzleColor> orderedColors = ExpandBallColors(bottomToTopGroups, manualBalls, useManualLayout);
            int targetCount = orderedColors.Count;

            int lanes = lanesAcrossSetting > 0
                ? Mathf.Clamp(lanesAcrossSetting, 2, 6)
                : ((preset == GlassTrackPreset.ArchLoop || preset == GlassTrackPreset.HeartLoop) ? 4 : (targetCount <= 22 ? 2 : (targetCount <= 78 ? 3 : 4)));

            float chosenScale = Mathf.Clamp(manualScale > 0.05f ? manualScale : 1.0f, 0.55f, 1.45f);
            float chosenDiam = 0.165f;
            float armTrim = 1.0f;

            if (autoFitToBalls && targetCount > 0)
            {
                ChooseBestFitParameters(
                    preset, sourceBranches, targetCount, lanes,
                    out chosenScale, out chosenDiam, out armTrim);
            }
            else
            {
                float maxDiamForHole = (0.36f * chosenScale) / Mathf.Max(1f, lanes * 0.495f);
                chosenDiam = Mathf.Clamp(maxDiamForHole * 0.90f, 0.115f, 0.255f);
            }

            float innerHalfW = lanes * chosenDiam * 0.495f + 0.012f;
            float clampedWall = Mathf.Clamp(wallThickness > 0.01f ? wallThickness : 0.082f, 0.045f, 0.14f);

            List<GlassTrackBranch> fittedBranches = TransformBranches(sourceBranches, chosenScale, armTrim);
            compiled = CompileTrack(preset, fittedBranches, innerHalfW, clampedWall, chosenDiam, lanes);

            List<TrackSlot> allSlots = GeneratePackedTrackSlots(compiled);
            if (autoFitToBalls)
            {
                for (int pass = 0; pass < 2 && targetCount > allSlots.Count && allSlots.Count > 0; pass++)
                {
                    float shrink = Mathf.Sqrt(allSlots.Count / (float)(targetCount + 5));
                    chosenDiam = Mathf.Clamp(chosenDiam * Mathf.Min(shrink, 0.94f), 0.095f, 0.245f);
                    innerHalfW = lanes * chosenDiam * 0.495f + 0.012f;
                    compiled = CompileTrack(preset, fittedBranches, innerHalfW, clampedWall, chosenDiam, lanes);
                    allSlots = GeneratePackedTrackSlots(compiled);
                }
            }

            layout = BuildOrderedResultFromSlots(allSlots, orderedColors, bottomToTopGroups, useManualLayout, chosenDiam, lanes, compiled);
        }

        private static List<PuzzleColor> ExpandBallColors(
            List<LevelMonsterEntry> groups,
            List<ManualPoolBallEntry> manualBalls,
            bool useManual)
        {
            var list = new List<PuzzleColor>();
            if (useManual && manualBalls != null && manualBalls.Count > 0)
            {
                var sorted = new List<ManualPoolBallEntry>(manualBalls);
                sorted.Sort((a, b) => a.row != b.row ? a.row.CompareTo(b.row) : a.column.CompareTo(b.column));
                for (int i = 0; i < sorted.Count; i++)
                {
                    if (sorted[i] != null) list.Add(sorted[i].color);
                }
                return list;
            }

            if (groups != null && groups.Count > 0)
            {
                for (int i = 0; i < groups.Count; i++)
                {
                    var g = groups[i];
                    if (g == null) continue;
                    int cnt = Mathf.Max(1, g.count);
                    for (int k = 0; k < cnt; k++) list.Add(g.color);
                }
            }
            return list;
        }

        private static void ChooseBestFitParameters(
            GlassTrackPreset preset,
            List<GlassTrackBranch> branches,
            int targetCount,
            int lanes,
            out float bestScale,
            out float bestDiam,
            out float bestTrim)
        {
            float baseTotalLen = EstimateUniqueBranchLength(branches);
            bestTrim = 1.0f;

            // Küçük/orta levellerde açık üst kolları top sayısına göre ayarla ki pist boş kalmasın
            if (targetCount <= 48 && (preset == GlassTrackPreset.ArchLoop || preset == GlassTrackPreset.HeartLoop || preset == GlassTrackPreset.UChannel || preset == GlassTrackPreset.DoubleArch))
            {
                bestTrim = Mathf.Lerp(0.54f, 0.95f, Mathf.InverseLerp(12f, 48f, targetCount));
            }

            float effectiveLenAtUnit = Mathf.Max(0.8f, baseTotalLen * (0.32f + 0.68f * bestTrim));

            // İstenen yoğunluk: toplar pistin ~%82-96'sını sıkıca doldursun
            float desiredTotalRowSteps = Mathf.Max(3.5f, (targetCount * 1.08f) / lanes);
            // Kanal yarı genişliği iç ada boşluğunu kapatmamalı (<= 0.36 * scale)
            float maxDiamRatioPerScale = 0.375f / Mathf.Max(1f, lanes * 0.495f);

            bestScale = 0.92f;
            bestDiam = 0.195f;
            float bestScore = float.MaxValue;

            for (int si = 0; si <= 12; si++)
            {
                float s = Mathf.Lerp(0.62f, 0.98f, si / 12f);
                float maxDiamSafe = Mathf.Min(0.255f, maxDiamRatioPerScale * s);
                float minDiamSafe = 0.108f;
                float idealDiam = (effectiveLenAtUnit * s) / (desiredTotalRowSteps * 0.95f);
                float d = Mathf.Clamp(idealDiam, minDiamSafe, maxDiamSafe);

                float capacity = (effectiveLenAtUnit * s) / (d * 0.95f) * lanes;
                float fillRatio = targetCount / Mathf.Max(1f, capacity);

                // Hedef doluluk %80..%96 arası
                float penalty = 0f;
                if (fillRatio > 0.97f) penalty += (fillRatio - 0.97f) * 24f;
                if (fillRatio < 0.78f) penalty += (0.78f - fillRatio) * 8f;
                penalty += Mathf.Abs(fillRatio - 0.88f) * 2.2f;
                penalty += Mathf.Abs(s - 0.88f) * 0.40f;
                penalty -= d * 0.35f;

                if (penalty < bestScore)
                {
                    bestScore = penalty;
                    bestScale = s;
                    bestDiam = d;
                }
            }
        }

        private static float EstimateUniqueBranchLength(List<GlassTrackBranch> branches)
        {
            float sum = 0f;
            for (int b = 0; b < branches.Count; b++)
            {
                var pts = branches[b]?.points;
                if (pts == null || pts.Count < 2) continue;
                float len = 0f;
                for (int i = 1; i < pts.Count; i++) len += Vector2.Distance(pts[i - 1], pts[i]);
                sum += len * (b == 0 ? 1.0f : 0.78f);
            }
            return Mathf.Max(0.8f, sum);
        }

        private static List<GlassTrackBranch> TransformBranches(List<GlassTrackBranch> src, float scale, float openArmTrim)
        {
            var res = new List<GlassTrackBranch>(src.Count);
            if (src.Count == 0) return res;

            // Çıkış tabanını y = -0.56 civarında sabit tut ki tepsi üstüyle mesafe değişmesin
            float minY = float.MaxValue;
            for (int b = 0; b < src.Count; b++)
            {
                var pts = src[b]?.points;
                if (pts == null) continue;
                for (int i = 0; i < pts.Count; i++) minY = Mathf.Min(minY, pts[i].y);
            }
            if (minY > 100f) minY = -0.56f;
            float targetMinY = -0.56f;

            for (int b = 0; b < src.Count; b++)
            {
                var br = src[b];
                if (br == null || br.points == null || br.points.Count < 2) continue;
                var nb = new GlassTrackBranch(br.name, br.isBridge);

                List<Vector2> srcPts = br.points;
                if (!br.isBridge && openArmTrim < 0.99f && srcPts.Count >= 3)
                {
                    // Kolu son noktasından itibaren hafifçe kısalt (köprü birleşimini koru)
                    srcPts = TrimPolylineEnd(srcPts, openArmTrim);
                }

                for (int i = 0; i < srcPts.Count; i++)
                {
                    float x = srcPts[i].x * scale;
                    float y = targetMinY + (srcPts[i].y - minY) * scale;
                    nb.points.Add(new Vector2(x, y));
                }
                res.Add(nb);
            }
            return res;
        }

        private static List<Vector2> TrimPolylineEnd(List<Vector2> pts, float keepFraction)
        {
            int n = pts.Count;
            float[] cum = new float[n];
            cum[0] = 0f;
            for (int i = 1; i < n; i++) cum[i] = cum[i - 1] + Vector2.Distance(pts[i - 1], pts[i]);
            float total = cum[n - 1];
            float target = Mathf.Clamp(keepFraction, 0.55f, 1.0f) * total;

            var outPts = new List<Vector2> { pts[0] };
            for (int i = 1; i < n; i++)
            {
                if (cum[i] <= target)
                {
                    outPts.Add(pts[i]);
                }
                else
                {
                    float seg = Mathf.Max(0.0001f, cum[i] - cum[i - 1]);
                    float t = Mathf.Clamp01((target - cum[i - 1]) / seg);
                    outPts.Add(Vector2.Lerp(pts[i - 1], pts[i], t));
                    break;
                }
            }
            return outPts;
        }

        public static CompiledTrack CompileTrack(
            GlassTrackPreset preset,
            List<GlassTrackBranch> branches,
            float innerHalfWidth,
            float wallThickness,
            float ballDiameter,
            int lanesAcross)
        {
            var track = new CompiledTrack
            {
                preset = preset,
                innerHalfWidth = Mathf.Max(0.10f, innerHalfWidth),
                wallThickness = Mathf.Max(0.035f, wallThickness),
                ballDiameter = Mathf.Clamp(ballDiameter, 0.08f, 0.30f),
                lanesAcross = Mathf.Clamp(lanesAcross, 2, 6),
                exitPoint = new Vector2(0f, -0.56f)
            };

            if (branches == null || branches.Count == 0)
            {
                branches = BuildPresetBranches(preset);
            }

            // Toplam segment sayısı <= MaxShaderSegments (64) kalacak şekilde her kolu örnekle
            int branchCount = branches.Count;
            int maxPtsPerBranch = Mathf.Clamp(MaxShaderSegments / Mathf.Max(1, branchCount) + 1, 6, 18);

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;

            for (int b = 0; b < branchCount; b++)
            {
                var br = branches[b];
                if (br == null || br.points == null || br.points.Count < 2) continue;

                List<Vector2> smooth = SampleSmoothCatmullRom(br.points, maxPtsPerBranch);
                track.polylines.Add(smooth);
                track.polylineIsBridge.Add(br.isBridge);

                for (int i = 0; i < smooth.Count; i++)
                {
                    minX = Mathf.Min(minX, smooth[i].x);
                    maxX = Mathf.Max(maxX, smooth[i].x);
                    minY = Mathf.Min(minY, smooth[i].y);
                    maxY = Mathf.Max(maxY, smooth[i].y);
                }

                // Çıkış noktası ilk ana kolun taban noktasıdır
                if (!br.isBridge && b == 0)
                {
                    track.exitPoint = smooth[0];
                }
            }

            if (minX > maxX)
            {
                minX = -1f; maxX = 1f; minY = -0.56f; maxY = 0.8f;
            }

            // Her kol için çıkışa mesafe (arc-length) ve shader segmentleri
            for (int b = 0; b < track.polylines.Count; b++)
            {
                var poly = track.polylines[b];
                bool isBridge = track.polylineIsBridge[b];
                float[] arc = new float[poly.Count];
                arc[0] = 0f;
                for (int i = 1; i < poly.Count; i++)
                {
                    arc[i] = arc[i - 1] + Vector2.Distance(poly[i - 1], poly[i]);
                }
                float totalLen = arc[poly.Count - 1];

                // Köprü ise uçlarının en yakın ana kola mesafesini ekle ki akış her zaman çıkışa doğru azalsın
                float startBaseDist = 0f;
                float endBaseDist = totalLen;
                if (isBridge)
                {
                    startBaseDist = EstimateDistanceToArmExit(track, poly[0]);
                    endBaseDist = EstimateDistanceToArmExit(track, poly[poly.Count - 1]);
                }

                for (int i = 0; i < poly.Count - 1; i++)
                {
                    if (track.segments.Count >= MaxShaderSegments) break;

                    float dA, dB;
                    if (!isBridge)
                    {
                        dA = arc[i];
                        dB = arc[i + 1];
                    }
                    else
                    {
                        float midA = arc[i];
                        float midB = arc[i + 1];
                        dA = Mathf.Min(startBaseDist + midA, endBaseDist + (totalLen - midA));
                        dB = Mathf.Min(startBaseDist + midB, endBaseDist + (totalLen - midB));
                    }

                    track.segments.Add(new TrackSegment
                    {
                        a = poly[i],
                        b = poly[i + 1],
                        branchId = b,
                        distA = dA,
                        distB = dB
                    });
                }
            }

            float padIn = track.innerHalfWidth;
            float padOut = track.innerHalfWidth + track.wallThickness;
            track.innerBounds = Rect.MinMaxRect(minX - padIn, minY - padIn, maxX + padIn, maxY + padIn);
            track.outerBounds = Rect.MinMaxRect(minX - padOut, minY - padOut, maxX + padOut, maxY + padOut);

            float fieldPad = padOut + 0.16f;
            track.fieldRect = Rect.MinMaxRect(minX - fieldPad, minY - fieldPad, maxX + fieldPad, maxY + fieldPad);

            BuildTrackField2D(track);
            return track;
        }

        private static float EstimateDistanceToArmExit(CompiledTrack track, Vector2 pt)
        {
            float bestDistSq = float.MaxValue;
            float bestArmDist = 1.0f;
            for (int b = 0; b < track.polylines.Count; b++)
            {
                if (track.polylineIsBridge[b]) continue;
                var poly = track.polylines[b];
                float acc = 0f;
                for (int i = 0; i < poly.Count; i++)
                {
                    if (i > 0) acc += Vector2.Distance(poly[i - 1], poly[i]);
                    float dSq = (poly[i] - pt).sqrMagnitude;
                    if (dSq < bestDistSq)
                    {
                        bestDistSq = dSq;
                        bestArmDist = acc;
                    }
                }
            }
            return bestArmDist;
        }

        private static void BuildTrackField2D(CompiledTrack track)
        {
            int n = FieldRes * FieldRes;
            track.sdf = new float[n];
            track.flowDir = new Vector2[n];
            track.distToExit = new float[n];
            track.wallNormal = new Vector2[n];

            float filletK = Mathf.Min(0.055f, track.innerHalfWidth * 0.20f);
            int branchCount = Mathf.Max(1, track.polylines.Count);
            float[] branchMinDist = new float[branchCount];

            for (int iy = 0; iy < FieldRes; iy++)
            {
                float py = Mathf.Lerp(track.fieldRect.yMin, track.fieldRect.yMax, (float)iy / (FieldRes - 1));
                for (int ix = 0; ix < FieldRes; ix++)
                {
                    float px = Mathf.Lerp(track.fieldRect.xMin, track.fieldRect.xMax, (float)ix / (FieldRes - 1));
                    Vector2 p = new Vector2(px, py);

                    for (int b = 0; b < branchCount; b++) branchMinDist[b] = float.MaxValue;

                    float bestFlowExitDist = float.MaxValue;
                    Vector2 bestFlow = Vector2.down;
                    float bestSegDistSq = float.MaxValue;

                    for (int s = 0; s < track.segments.Count; s++)
                    {
                        var seg = track.segments[s];
                        Vector2 ab = seg.b - seg.a;
                        float abSq = ab.sqrMagnitude;
                        float t = abSq > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - seg.a, ab) / abSq) : 0f;
                        Vector2 proj = seg.a + ab * t;
                        float dSq = (p - proj).sqrMagnitude;
                        float d = Mathf.Sqrt(dSq) - track.innerHalfWidth;

                        if (d < branchMinDist[seg.branchId])
                        {
                            branchMinDist[seg.branchId] = d;
                        }

                        if (dSq < bestSegDistSq + 0.002f)
                        {
                            float dExit = Mathf.Lerp(seg.distA, seg.distB, t);
                            if (dSq < bestSegDistSq - 0.002f || dExit < bestFlowExitDist)
                            {
                                bestSegDistSq = dSq;
                                bestFlowExitDist = dExit;

                                // Akış yönü: distToExit'in azaldığı yön + viraj duvarlarında takılmayı önleyen hafif merkez eğilimi
                                Vector2 segDir = abSq > 1e-8f ? ab.normalized : Vector2.up;
                                Vector2 tangDown = (seg.distB < seg.distA) ? segDir : -segDir;
                                Vector2 toCenter = proj - p;
                                float centerPull = Mathf.Clamp(toCenter.magnitude / Mathf.Max(0.05f, track.innerHalfWidth), 0f, 1f) * 0.16f;
                                bestFlow = (tangDown + (toCenter.sqrMagnitude > 1e-6f ? toCenter.normalized * centerPull : Vector2.zero)).normalized;
                            }
                        }
                    }

                    // Kollar arası yumuşak birleşme (SmoothUnion)
                    float combined = branchMinDist[0];
                    for (int b = 1; b < branchCount; b++)
                    {
                        float d2 = branchMinDist[b];
                        if (d2 > 1e6f) continue;
                        float h = Mathf.Clamp01(0.5f + 0.5f * (d2 - combined) / Mathf.Max(0.001f, filletK));
                        combined = Mathf.Lerp(d2, combined, h) - filletK * h * (1f - h);
                    }

                    int idx = iy * FieldRes + ix;
                    track.sdf[idx] = combined;
                    track.flowDir[idx] = bestFlow.sqrMagnitude > 0.001f ? bestFlow : Vector2.down;
                    track.distToExit[idx] = bestFlowExitDist < 1e6f ? bestFlowExitDist : 0f;
                }
            }

            // Duvar içe normali (-∇sdf)
            float cellDx = track.fieldRect.width / (FieldRes - 1);
            float cellDy = track.fieldRect.height / (FieldRes - 1);
            for (int iy = 0; iy < FieldRes; iy++)
            {
                int iy0 = Mathf.Max(0, iy - 1);
                int iy1 = Mathf.Min(FieldRes - 1, iy + 1);
                for (int ix = 0; ix < FieldRes; ix++)
                {
                    int ix0 = Mathf.Max(0, ix - 1);
                    int ix1 = Mathf.Min(FieldRes - 1, ix + 1);
                    float gx = (track.sdf[iy * FieldRes + ix1] - track.sdf[iy * FieldRes + ix0]) / Mathf.Max(0.001f, (ix1 - ix0) * cellDx);
                    float gy = (track.sdf[iy1 * FieldRes + ix] - track.sdf[iy0 * FieldRes + ix]) / Mathf.Max(0.001f, (iy1 - iy0) * cellDy);
                    Vector2 inward = new Vector2(-gx, -gy);
                    track.wallNormal[iy * FieldRes + ix] = inward.sqrMagnitude > 1e-6f ? inward.normalized : Vector2.up;
                }
            }
        }

        // ==================== Şerit Bazlı Sıkı Top Slot Üretimi ====================

        public static List<TrackSlot> GeneratePackedTrackSlots(CompiledTrack track)
        {
            var slots = new List<TrackSlot>();
            if (track == null || track.polylines.Count == 0) return slots;

            float d = track.ballDiameter;
            float r = d * 0.5f;
            int lanes = track.lanesAcross;
            float step = d * 1.02f;
            float minDistOther = d * 1.01f;
            float minDistOtherSq = minDistOther * minDistOther;

            // Şerit genişliğini kanal içine sıkıca oturt (duvar köşe ve kutu çarpıştırıcılarına çarpmayacak emniyetli yayılım)
            float usableSpan = Mathf.Max(d * (lanes - 1), track.innerHalfWidth * 2f - d * 1.22f);
            float laneSpacing = lanes > 1 ? usableSpan / (lanes - 1) : 0f;

            for (int b = 0; b < track.polylines.Count; b++)
            {
                var poly = track.polylines[b];
                if (poly == null || poly.Count < 2) continue;
                bool isBridge = track.polylineIsBridge[b];

                // Çıkışa yakın uçtan başlayarak kolu ince adımlarla yürü
                List<Vector2> dense = ResamplePolylineUniform(poly, d * 0.16f);
                if (dense.Count < 2) continue;

                // Köprüler için ortadan dışa doğru iki yarıyı sırala; ana kollar için tabandan yukarı
                if (!isBridge)
                {
                    AddSlotsAlongDirectedPath(dense, b, 0, lanes, laneSpacing, step, r, minDistOtherSq, track, slots);
                }
                else
                {
                    int mid = dense.Count / 2;
                    var leftHalf = new List<Vector2>();
                    for (int i = mid; i >= 0; i--) leftHalf.Add(dense[i]);
                    var rightHalf = new List<Vector2>();
                    for (int i = mid; i < dense.Count; i++) rightHalf.Add(dense[i]);

                    AddSlotsAlongDirectedPath(leftHalf, b, 0, lanes, laneSpacing, step, r, minDistOtherSq, track, slots);
                    AddSlotsAlongDirectedPath(rightHalf, b, 1000, lanes, laneSpacing, step, r, minDistOtherSq, track, slots);
                }
            }

            return slots;
        }

        private static void AddSlotsAlongDirectedPath(
            List<Vector2> path,
            int branchId,
            int rowBaseOffset,
            int lanes,
            float laneSpacing,
            float step,
            float ballR,
            float minDistOtherSq,
            CompiledTrack track,
            List<TrackSlot> slots)
        {
            if (path == null || path.Count < 2) return;

            bool[] hasPrev = new bool[lanes];
            Vector2[] prevLanePos = new Vector2[lanes];
            int[] laneRowCounter = new int[lanes];
            float stepSq = step * step;

            for (int i = 0; i < path.Count; i++)
            {
                Vector2 prev = path[Mathf.Max(0, i - 1)];
                Vector2 next = path[Mathf.Min(path.Count - 1, i + 1)];
                Vector2 tang = (next - prev).normalized;
                if (tang.sqrMagnitude < 1e-6f) tang = Vector2.up;
                Vector2 normal = new Vector2(-tang.y, tang.x);

                for (int l = 0; l < lanes; l++)
                {
                    float latOffset = (l - (lanes - 1) * 0.5f) * laneSpacing;
                    Vector2 cand = path[i] + normal * latOffset;

                    if (hasPrev[l] && (cand - prevLanePos[l]).sqrMagnitude < stepSq)
                        continue;

                    track.SampleField(cand, out float sDist, out _, out float exitDist, out _);
                    // Duvar içine gömülmesin: köşeli BoxCollider geometrisi ve grid hatasını tolere edecek emniyetli hava payı (~8mm)
                    if (sDist > -ballR * 1.10f)
                        continue;

                    // Çıkış durdurucu kapısının içine gömülmesin
                    float exitStopperTop = (track.exitPoint.y - track.innerHalfWidth) - 0.045f + 0.04f;
                    if (cand.y - ballR < exitStopperTop + 0.012f)
                        continue;

                    // Diğer tüm toplarla (özellikle kavşak ve iç virajlarda) çakışmasın
                    bool overlaps = false;
                    for (int k = 0; k < slots.Count; k++)
                    {
                        if ((slots[k].localPos - cand).sqrMagnitude < minDistOtherSq)
                        {
                            overlaps = true;
                            break;
                        }
                    }
                    if (overlaps) continue;

                    hasPrev[l] = true;
                    prevLanePos[l] = cand;

                    slots.Add(new TrackSlot
                    {
                        localPos = cand,
                        branchId = branchId,
                        rowIndex = rowBaseOffset + laneRowCounter[l],
                        laneIndex = l,
                        distToExit = exitDist
                    });
                    laneRowCounter[l]++;
                }
            }
        }

        private static OrderedPoolLayout.Result BuildOrderedResultFromSlots(
            List<TrackSlot> allSlots,
            List<PuzzleColor> orderedColors,
            List<LevelMonsterEntry> groups,
            bool useManual,
            float ballDiam,
            int lanes,
            CompiledTrack track)
        {
            var res = new OrderedPoolLayout.Result
            {
                balls = new List<OrderedPoolLayout.PoolBall>(),
                diameter = ballDiam,
                laneWidth = lanes,
                totalColumns = Mathf.Max(1, lanes * 2)
            };

            if (allSlots == null || allSlots.Count == 0 || orderedColors == null || orderedColors.Count == 0)
                return res;

            float bandSize = Mathf.Max(0.05f, ballDiam * 0.98f);

            // Manuel serbest dizilim istenmişse, çıkışa olan mesafeyi baz alan sıralamayı kullan
            if (useManual)
            {
                var sorted = new List<TrackSlot>(allSlots);
                sorted.Sort((a, b) =>
                {
                    int bandA = Mathf.RoundToInt(a.distToExit / bandSize);
                    int bandB = Mathf.RoundToInt(b.distToExit / bandSize);
                    if (bandA != bandB) return bandA.CompareTo(bandB);
                    if (a.branchId != b.branchId) return a.branchId.CompareTo(b.branchId);
                    return a.laneIndex.CompareTo(b.laneIndex);
                });

                int manualCount = Mathf.Min(sorted.Count, orderedColors.Count);
                for (int i = 0; i < manualCount; i++)
                {
                    var sl = sorted[i];
                    res.balls.Add(new OrderedPoolLayout.PoolBall
                    {
                        color = orderedColors[i],
                        ballColumn = sl.laneIndex,
                        row = Mathf.Max(0, Mathf.RoundToInt(sl.distToExit / bandSize)),
                        localPos = sl.localPos
                    });
                }
                return res;
            }

            // ==================== Bus Traffic Fever Tarzı Nizami Blok Sıralaması ====================
            // 1. Slotları ait oldukları kollara (branch) göre ayır ve her kolu satır-satır, şerit-şerit sırala
            int branchCount = track != null && track.polylines != null ? track.polylines.Count : 1;
            var branchSlots = new List<TrackSlot>[branchCount];
            for (int b = 0; b < branchCount; b++) branchSlots[b] = new List<TrackSlot>();

            for (int i = 0; i < allSlots.Count; i++)
            {
                int bid = Mathf.Clamp(allSlots[i].branchId, 0, branchCount - 1);
                branchSlots[bid].Add(allSlots[i]);
            }

            for (int b = 0; b < branchCount; b++)
            {
                branchSlots[b].Sort((s1, s2) =>
                {
                    if (s1.rowIndex != s2.rowIndex) return s1.rowIndex.CompareTo(s2.rowIndex);
                    return s1.laneIndex.CompareTo(s2.laneIndex);
                });
            }

            // 2. Çıkışa açılan ana kolları (feeder) ve üst bağ/köprüleri (bridge) tespit et
            var feederIds = new List<int>();
            var bridgeIds = new List<int>();

            for (int b = 0; b < branchCount; b++)
            {
                bool isBridge = track != null && track.polylineIsBridge != null && b < track.polylineIsBridge.Count && track.polylineIsBridge[b];
                if (isBridge) bridgeIds.Add(b);
                else feederIds.Add(b);
            }
            if (feederIds.Count == 0) feederIds.Add(0);

            // Her ana akış için sıralı slot kuyruğunu oluştur
            var streamSlots = new List<List<TrackSlot>>();
            for (int f = 0; f < feederIds.Count; f++)
            {
                streamSlots.Add(new List<TrackSlot>(branchSlots[feederIds[f]]));
            }

            // ArchLoop gibi çift kollu ve iç kemerli pistlerde köprünün sol yarısını sol kola, sağ yarısını sağ kola bağla
            if (feederIds.Count == 2 && bridgeIds.Count > 0)
            {
                for (int bi = 0; bi < bridgeIds.Count; bi++)
                {
                    int bridgeId = bridgeIds[bi];
                    var bSlots = branchSlots[bridgeId];
                    for (int k = 0; k < bSlots.Count; k++)
                    {
                        if (bSlots[k].localPos.x <= 0f) streamSlots[0].Add(bSlots[k]);
                        else streamSlots[1].Add(bSlots[k]);
                    }
                }
            }
            else if (bridgeIds.Count > 0)
            {
                for (int bi = 0; bi < bridgeIds.Count; bi++)
                {
                    streamSlots[0].AddRange(branchSlots[bridgeIds[bi]]);
                }
            }

            // Her akışın slotlarını çıkışa olan mesafesine göre nizami sırala
            // Böylece renk blokları çıkıştan geriye doğru kesintisiz ve nizami bir şekilde akar
            for (int s = 0; s < streamSlots.Count; s++)
            {
                streamSlots[s].Sort((s1, s2) =>
                {
                    int bandA = Mathf.RoundToInt(s1.distToExit / bandSize);
                    int bandB = Mathf.RoundToInt(s2.distToExit / bandSize);
                    if (bandA != bandB) return bandA.CompareTo(bandB);
                    if (s1.rowIndex != s2.rowIndex) return s1.rowIndex.CompareTo(s2.rowIndex);
                    return s1.laneIndex.CompareTo(s2.laneIndex);
                });
            }

            // 3. Renk bloklarını çıkar (LevelMonsterEntry gruplarından veya orderedColors akışından)
            var blocks = new List<(PuzzleColor color, int count)>();
            if (groups != null && groups.Count > 0)
            {
                for (int i = 0; i < groups.Count; i++)
                {
                    if (groups[i] != null && groups[i].count > 0)
                        blocks.Add((groups[i].color, groups[i].count));
                }
            }
            else
            {
                for (int i = 0; i < orderedColors.Count; i++)
                {
                    PuzzleColor c = orderedColors[i];
                    if (blocks.Count > 0 && blocks[blocks.Count - 1].color == c)
                    {
                        var last = blocks[blocks.Count - 1];
                        blocks[blocks.Count - 1] = (last.color, last.count + 1);
                    }
                    else
                    {
                        blocks.Add((c, 1));
                    }
                }
            }

            // 4. Renk bloklarını kollara (Sol Kol & Sağ Kol) sırayla dağıt (bölünmeden monolitik bloklar)
            var streamBlocks = new List<List<(PuzzleColor color, int count)>>();
            for (int f = 0; f < streamSlots.Count; f++) streamBlocks.Add(new List<(PuzzleColor, int)>());

            if (streamSlots.Count >= 2)
            {
                for (int i = 0; i < blocks.Count; i++)
                {
                    int targetStream = i % streamSlots.Count;
                    streamBlocks[targetStream].Add(blocks[i]);
                }
            }
            else
            {
                streamBlocks[0].AddRange(blocks);
            }

            // 5. Her kola ait blokları o kolun slotlarına kesintisiz tam şeritler halinde yerleştir
            int[] streamSlotIndex = new int[streamSlots.Count];

            for (int f = 0; f < streamSlots.Count; f++)
            {
                var sList = streamSlots[f];
                var bList = streamBlocks[f];

                for (int bi = 0; bi < bList.Count; bi++)
                {
                    var blk = bList[bi];
                    for (int k = 0; k < blk.count; k++)
                    {
                        if (streamSlotIndex[f] < sList.Count)
                        {
                            var sl = sList[streamSlotIndex[f]++];
                            res.balls.Add(new OrderedPoolLayout.PoolBall
                            {
                                color = blk.color,
                                ballColumn = f * lanes + sl.laneIndex,
                                row = Mathf.Max(0, Mathf.RoundToInt(sl.distToExit / bandSize)),
                                localPos = sl.localPos
                            });
                        }
                        else
                        {
                            // Kol kapasitesi dolarsa diğer kola taşır
                            bool placed = false;
                            for (int otherF = 0; otherF < streamSlots.Count; otherF++)
                            {
                                if (otherF == f) continue;
                                if (streamSlotIndex[otherF] < streamSlots[otherF].Count)
                                {
                                    var sl = streamSlots[otherF][streamSlotIndex[otherF]++];
                                    res.balls.Add(new OrderedPoolLayout.PoolBall
                                    {
                                        color = blk.color,
                                        ballColumn = otherF * lanes + sl.laneIndex,
                                        row = Mathf.Max(0, Mathf.RoundToInt(sl.distToExit / bandSize)),
                                        localPos = sl.localPos
                                    });
                                    placed = true;
                                    break;
                                }
                            }
                            if (!placed) break;
                        }
                    }
                }
            }

            return res;
        }

        // ==================== Spline / Polyline Yardımcıları ====================

        public static List<Vector2> SampleSmoothCatmullRom(List<Vector2> ctrl, int targetCount)
        {
            var outPts = new List<Vector2>();
            if (ctrl == null || ctrl.Count == 0) return outPts;
            if (ctrl.Count == 1) { outPts.Add(ctrl[0]); return outPts; }
            if (ctrl.Count == 2)
            {
                int segs = Mathf.Max(2, targetCount - 1);
                for (int i = 0; i <= segs; i++) outPts.Add(Vector2.Lerp(ctrl[0], ctrl[1], (float)i / segs));
                return outPts;
            }

            // Önce yoğun Catmull-Rom üret, sonra eşit yay uzunluğunda targetCount noktaya örnekle
            var dense = new List<Vector2>();
            int n = ctrl.Count;
            const int sub = 12;
            for (int i = 0; i < n - 1; i++)
            {
                Vector2 p0 = ctrl[Mathf.Max(0, i - 1)];
                Vector2 p1 = ctrl[i];
                Vector2 p2 = ctrl[i + 1];
                Vector2 p3 = ctrl[Mathf.Min(n - 1, i + 2)];
                for (int s = 0; s < sub; s++)
                {
                    float t = (float)s / sub;
                    dense.Add(EvalCatmullRom(p0, p1, p2, p3, t));
                }
            }
            dense.Add(ctrl[n - 1]);
            return ResamplePolylineByCount(dense, Mathf.Max(2, targetCount));
        }

        private static Vector2 EvalCatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (
                (2f * p1) +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3
            );
        }

        private static List<Vector2> ResamplePolylineByCount(List<Vector2> pts, int count)
        {
            var res = new List<Vector2>(count);
            if (pts.Count < 2 || count < 2) { res.AddRange(pts); return res; }

            float[] cum = new float[pts.Count];
            cum[0] = 0f;
            for (int i = 1; i < pts.Count; i++) cum[i] = cum[i - 1] + Vector2.Distance(pts[i - 1], pts[i]);
            float total = cum[pts.Count - 1];
            if (total < 1e-5f) { res.Add(pts[0]); res.Add(pts[pts.Count - 1]); return res; }

            int cursor = 0;
            for (int k = 0; k < count; k++)
            {
                float d = total * k / (count - 1);
                while (cursor < pts.Count - 2 && cum[cursor + 1] < d) cursor++;
                float seg = Mathf.Max(1e-5f, cum[cursor + 1] - cum[cursor]);
                float t = Mathf.Clamp01((d - cum[cursor]) / seg);
                res.Add(Vector2.Lerp(pts[cursor], pts[cursor + 1], t));
            }
            return res;
        }

        private static List<Vector2> ResamplePolylineUniform(List<Vector2> pts, float step)
        {
            var res = new List<Vector2>();
            if (pts == null || pts.Count < 2) return res;
            float total = 0f;
            for (int i = 1; i < pts.Count; i++) total += Vector2.Distance(pts[i - 1], pts[i]);
            int count = Mathf.Clamp(Mathf.CeilToInt(total / Mathf.Max(0.01f, step)) + 1, 2, 240);
            return ResamplePolylineByCount(pts, count);
        }
    }
}
