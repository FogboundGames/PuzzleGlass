using System.Collections.Generic;
using UnityEngine;

namespace PuzzleGlass
{
    /// <summary>
    /// Renk kapıları: cam pistin içinde kanalı enine kesen kapılar. Kapıdan geçen topun rengi değişir.
    ///
    /// Kapı, kanalın akış yönüne dik durur (yönü akış alanından okunur), bu yüzden tasarımcı yalnızca
    /// kapının yerini ve uzunluğunu verir. Geçiş tespiti kesişme ile yapılır: topun bir önceki konumu ile
    /// şimdiki konumu arasındaki parça kapı çizgisini kesiyorsa top boyanır. Böylece hızlı toplar kapıyı atlamaz.
    /// </summary>
    public static class TrackColorGates
    {
        private struct ResolvedGate
        {
            public Vector2 a;           // kapı çizgisinin uçları (pist yerel)
            public Vector2 b;
            public Vector2 center;
            public Vector2 dir;         // a -> b yönü (birim)
            public float halfLength;
            public bool anySource;
            public PuzzleColor from;
            public PuzzleColor to;
        }

        private static readonly List<ResolvedGate> s_Gates = new List<ResolvedGate>(4);
        private static GlassTrackLayout.CompiledTrack s_CachedTrack;
        private static int s_CachedCount = -1;
        private static int s_CachedHash;

        public static int ActiveGateCount => s_Gates.Count;

        public static void Invalidate()
        {
            s_Gates.Clear();
            s_CachedTrack = null;
            s_CachedCount = -1;
            s_CachedHash = 0;
        }

        /// <summary>
        /// Kapının pist yerel koordinatlarındaki iki ucu. Kapı kanalın akışına diktir; yön akış alanından okunur.
        /// Hem oyun görseli hem de Cam Stüdyosu önizlemesi bu fonksiyonu kullanır, ikisi aynı yeri gösterir.
        /// </summary>
        public static bool TryGetGateSegment(
            GlassTrackLayout.CompiledTrack track,
            TrackColorGate gate,
            out Vector2 a,
            out Vector2 b,
            out Vector2 center)
        {
            a = b = center = Vector2.zero;
            if (track == null || gate == null) return false;

            center = GlassTrackLayout.DenormalizeTrackPos(track, gate.normalizedCenter);
            track.SampleField(center, out _, out Vector2 flow, out _, out _);
            Vector2 dir = new Vector2(-flow.y, flow.x);
            if (dir.sqrMagnitude < 1e-6f) dir = Vector2.right;
            dir.Normalize();

            float half = Mathf.Max(0.05f, gate.length) * 0.5f;
            a = center - dir * half;
            b = center + dir * half;
            return true;
        }

        private static int ComputeHash(List<TrackColorGate> gates)
        {
            int h = 17;
            for (int i = 0; i < gates.Count; i++)
            {
                var g = gates[i];
                if (g == null) continue;
                h = h * 31 + g.normalizedCenter.GetHashCode();
                h = h * 31 + g.length.GetHashCode();
                h = h * 31 + (g.enabled ? 1 : 0);
                h = h * 31 + (g.anySourceColor ? 1 : 0);
                h = h * 31 + (int)g.fromColor * 97 + (int)g.toColor;
            }
            return h;
        }

        private static void EnsureResolved(ShapedGlassContainer container)
        {
            var track = container.compiledTrack;
            var gates = container.trackColorGates;
            int count = gates != null ? gates.Count : 0;
            int hash = gates != null ? ComputeHash(gates) : 0;

            if (s_CachedTrack == track && s_CachedCount == count && s_CachedHash == hash) return;

            s_Gates.Clear();
            s_CachedTrack = track;
            s_CachedCount = count;
            s_CachedHash = hash;
            if (gates == null || track == null) return;

            for (int i = 0; i < gates.Count; i++)
            {
                var g = gates[i];
                if (g == null || !g.enabled) continue;
                if (!g.anySourceColor && g.fromColor == g.toColor) continue;
                if (!TryGetGateSegment(track, g, out Vector2 a, out Vector2 b, out Vector2 c)) continue;

                Vector2 dir = (b - a);
                float len = dir.magnitude;
                if (len < 1e-4f) continue;

                s_Gates.Add(new ResolvedGate
                {
                    a = a,
                    b = b,
                    center = c,
                    dir = dir / len,
                    halfLength = len * 0.5f,
                    anySource = g.anySourceColor,
                    from = g.fromColor,
                    to = g.toColor
                });
            }

            // Kapı görselleri cam geometrisi kurulurken henüz atanmamış olabiliyor; burada bir kez kurulur.
            container.RefreshColorGateVisuals();
        }

        /// <summary>
        /// Bir renk kapısı, verilen renkteki topu hedef renge çevirebilir mi? (En fazla 3 zincir adımı.)
        /// Kilitlenme kontrolü bunu kullanır: bantta kırmızı top varken slotta mavi kutu duruyorsa ve pistte
        /// kırmızı -> mavi kapı varsa, bu bir hamledir; level haksız yere kaybedilmiş sayılmaz.
        /// </summary>
        public static bool CanBecome(PuzzleColor ballColor, PuzzleColor targetColor)
        {
            if (s_Gates.Count == 0) return false;
            if (ballColor == targetColor) return true;

            PuzzleColor cur = ballColor;
            for (int hop = 0; hop < 3; hop++)
            {
                bool moved = false;
                for (int g = 0; g < s_Gates.Count; g++)
                {
                    var gate = s_Gates[g];
                    if (!gate.anySource && gate.from != cur) continue;
                    if (gate.to == cur) continue;
                    if (gate.to == targetColor) return true;
                    cur = gate.to;
                    moved = true;
                    break;
                }
                if (!moved) break;
            }
            return false;
        }

        /// <summary>İki doğru parçası kesişiyor mu? (p1->p2 ile q1->q2)</summary>
        private static bool SegmentsCross(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
        {
            Vector2 r = p2 - p1;
            Vector2 s = q2 - q1;
            float denom = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(denom) < 1e-9f) return false; // paralel

            Vector2 qp = q1 - p1;
            float t = (qp.x * s.y - qp.y * s.x) / denom;
            float u = (qp.x * r.y - qp.y * r.x) / denom;
            return t >= 0f && t <= 1f && u >= 0f && u <= 1f;
        }

        /// <summary>
        /// Her fizik adımında çağrılır: bir önceki adımdan bu yana kapıyı kesen topları boyar.
        /// </summary>
        public static void Step(ShapedGlassContainer container, List<BallPhysicsController> balls)
        {
            if (container == null || !container.IsTrackShape || balls == null || balls.Count == 0) return;
            EnsureResolved(container);
            if (s_Gates.Count == 0) return;

            for (int i = 0; i < balls.Count; i++)
            {
                var b = balls[i];
                if (b == null || !b.gameObject.activeInHierarchy) continue;
                if (b.state != BallState.InContainer || !b.trackQueuePosValid)
                {
                    b.gatePrevValid = false;
                    continue;
                }

                Vector2 cur = b.trackQueueLocalPos;
                if (!b.gatePrevValid)
                {
                    b.gatePrevLocalPos = cur;
                    b.gatePrevValid = true;
                    continue;
                }

                Vector2 prev = b.gatePrevLocalPos;
                b.gatePrevLocalPos = cur;
                if ((cur - prev).sqrMagnitude < 1e-8f) continue;

                for (int g = 0; g < s_Gates.Count; g++)
                {
                    var gate = s_Gates[g];
                    if (!gate.anySource && b.colorType != gate.from) continue;
                    if (b.colorType == gate.to) continue;
                    if (!SegmentsCross(prev, cur, gate.a, gate.b)) continue;

                    b.RecolorTo(gate.to);
                    break;
                }
            }
        }
    }
}
