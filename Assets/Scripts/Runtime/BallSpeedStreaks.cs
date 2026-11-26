using System.Collections.Generic;
using UnityEngine;

namespace PuzzleGlass
{
    /// <summary>
    /// Pistte hızlı hareket eden topların arkasında, hareket yönünün tersine kısa ve yarı saydam hız izi.
    /// Testçi geri bildirimi: "konveyördeki fiziği gösteren efekt". İz, topun GERÇEK hızından türetilir:
    /// top hızlanınca uzar, yavaşlayınca söner (virajda dönen, önü açılıp ileri atılan, çıkışa çekilen toplar).
    ///
    /// Performans: aynı anda en fazla <see cref="MaxStreaks"/> iz; hepsi tek bir dinamik mesh, tek çizim.
    /// Top başına TrailRenderer kullanılmaz.
    /// </summary>
    [DefaultExecutionOrder(9000)]
    public class BallSpeedStreaks : MonoBehaviour
    {
        public static bool Enabled = true;
        // Hafifletildi (oyuncu geri bildirimi: virajlarda "rüzgar" gibi görünüp topları soluklaştırıyordu):
        // iz yalnızca bant hızını belirgin aşan toplarda, daha kısa ve daha saydam.
        public static int MaxStreaks = 10;
        /// <summary>İz bu hızdan (bant hızının katı) itibaren belirmeye başlar ve <see cref="FullSpeedMul"/>'da tam olur.</summary>
        public static float StartSpeedMul = 1.15f;
        public static float FullSpeedMul = 1.6f;
        /// <summary>İz uzunluğu = hız × bu süre (sn), top çapının <see cref="MaxLengthInDiameters"/> katıyla sınırlı.</summary>
        public static float LengthSeconds = 0.10f;
        public static float MaxLengthInDiameters = 1.6f;
        public static float Alpha = 0.25f;
        /// <summary>İzin z kayması: negatif = topların önünde (dolu halkada arkadaki toplar izi örtmesin).</summary>
        public static float ZOffset = -0.04f;

        private const string ShaderName = "PuzzleGlass/BallSpeedStreak";

        private static BallSpeedStreaks s_Instance;

        private readonly List<(float score, BallPhysicsController ball, Vector3 vel, float strength)> _candidates
            = new List<(float, BallPhysicsController, Vector3, float)>(512);

        private Mesh _mesh;
        private Material _material;
        private Vector3[] _verts;
        private Vector2[] _uvs;
        private Color[] _colors;
        private int[] _tris;
        private int _allocated;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInitialize()
        {
            if (s_Instance != null) return;
            var go = new GameObject("BallSpeedStreaks");
            DontDestroyOnLoad(go);
            s_Instance = go.AddComponent<BallSpeedStreaks>();
        }

        private void Awake()
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null) { enabled = false; return; }
            _material = new Material(shader) { name = "Mat_BallSpeedStreak", hideFlags = HideFlags.HideAndDontSave };
            _mesh = new Mesh { name = "BallSpeedStreaks" };
            _mesh.MarkDynamic();
            // Dünya uzayında çizilir; ekran dışı diye kırpılmasın
            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
        }

        private void EnsureCapacity(int count)
        {
            if (_allocated >= count) return;
            _allocated = count;
            _verts = new Vector3[count * 4];
            _uvs = new Vector2[count * 4];
            _colors = new Color[count * 4];
            _tris = new int[count * 6];
            for (int i = 0; i < count; i++)
            {
                int v = i * 4, t = i * 6;
                _tris[t] = v; _tris[t + 1] = v + 1; _tris[t + 2] = v + 2;
                _tris[t + 3] = v + 2; _tris[t + 4] = v + 1; _tris[t + 5] = v + 3;
                // u: 0 = topun arkası (iz başı), 1 = kuyruk ucu; v: enine 0..1
                _uvs[v] = new Vector2(0f, 0f); _uvs[v + 1] = new Vector2(0f, 1f);
                _uvs[v + 2] = new Vector2(1f, 0f); _uvs[v + 3] = new Vector2(1f, 1f);
            }
            _mesh.Clear();
            _mesh.vertices = _verts;
            _mesh.uv = _uvs;
            _mesh.colors = _colors;
            _mesh.triangles = _tris;
            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
        }

        private void LateUpdate()
        {
            if (_material == null) return;
            float dt = Time.deltaTime;
            if (!Enabled || dt <= 1e-5f || BallObjectPool.Instance == null) return;

            var container = BallContainerManager.Instance != null ? BallContainerManager.Instance.glassContainer : null;
            if (container == null || !container.IsTrackShape) return;

            float containerScale = Mathf.Abs(container.transform.lossyScale.x);
            float beltWorld = TrackCrowdQueue.BeltSpeed * containerScale;
            float vStart = beltWorld * StartSpeedMul;
            float vFull = Mathf.Max(vStart + 0.01f, beltWorld * FullSpeedMul);

            int frame = Time.frameCount;
            var balls = BallObjectPool.Instance.ActiveBalls;
            _candidates.Clear();

            // Hız: konumdan türetilir (rigidbody, tween ya da kinematik fark etmez), kısa süreli yumuşatma ile
            float velBlend = 1f - Mathf.Exp(-dt * 18f);
            float fadeIn = 1f - Mathf.Exp(-dt * 14f);
            float fadeOut = 1f - Mathf.Exp(-dt * 9f);
            for (int i = 0; i < balls.Count; i++)
            {
                var b = balls[i];
                if (b == null) continue;
                bool onTrack = b.state == BallState.InContainer || b.state == BallState.AttractingToExit;
                Vector3 pos = b.transform.position;

                // İzleme durumu topun üzerinde tutulur (havuza dönüp yeniden çıkan top: kare boşluğu > 2 → sıfırlanır)
                if (frame - b.streakLastFrame > 2 || !onTrack)
                {
                    b.streakLastPos = pos;
                    b.streakVel = Vector3.zero;
                    b.streakStrength = 0f;
                    b.streakLastFrame = frame;
                    continue;
                }

                Vector3 instVel = (pos - b.streakLastPos) / dt;
                instVel.z = 0f;
                // Işınlanma / yeniden konumlanma: iz çizme
                if (instVel.sqrMagnitude > (vFull * 6f) * (vFull * 6f)) instVel = Vector3.zero;
                Vector3 vel = Vector3.Lerp(b.streakVel, instVel, velBlend);
                b.streakVel = vel;
                b.streakLastPos = pos;
                b.streakLastFrame = frame;

                float speed = vel.magnitude;
                float target = Mathf.InverseLerp(vStart, vFull, speed);
                float strength = Mathf.Lerp(b.streakStrength, target, target > b.streakStrength ? fadeIn : fadeOut);
                b.streakStrength = strength;

                if (strength > 0.02f) _candidates.Add((strength * speed, b, vel, strength));
            }

            int count = Mathf.Min(MaxStreaks, _candidates.Count);
            if (count == 0) return;
            if (_candidates.Count > count) _candidates.Sort((a, c) => c.score.CompareTo(a.score));

            EnsureCapacity(MaxStreaks);
            for (int i = 0; i < _allocated * 4; i++) _colors[i] = Color.clear;

            for (int i = 0; i < count; i++)
            {
                var (score, ball, vel, strength) = _candidates[i];
                float radius = Mathf.Abs(ball.transform.lossyScale.x) * 0.5f;
                float speed = vel.magnitude;
                if (speed < 1e-4f || radius <= 0f) continue;
                Vector3 dir = vel / speed;
                Vector3 side = new Vector3(-dir.y, dir.x, 0f);

                float len = Mathf.Min(speed * LengthSeconds, radius * 2f * MaxLengthInDiameters) * Mathf.Lerp(0.5f, 1f, strength);
                float halfW = radius * 0.78f;
                // İz topun arkasından başlar; dolu halkada arkadaki toplar örtmesin diye topların önünde çizilir
                Vector3 head = ball.transform.position - dir * (radius * 0.35f) + new Vector3(0f, 0f, ZOffset);
                Vector3 tail = head - dir * len;

                int v = i * 4;
                _verts[v] = head - side * halfW;
                _verts[v + 1] = head + side * halfW;
                _verts[v + 2] = tail - side * (halfW * 0.35f);
                _verts[v + 3] = tail + side * (halfW * 0.35f);

                Color c = Color.Lerp(PuzzleColorPalette.GetPrimaryColor(ball.colorType), Color.white, 0.45f);
                c.a = Alpha * strength;
                _colors[v] = c; _colors[v + 1] = c; _colors[v + 2] = c; _colors[v + 3] = c;
            }

            _mesh.vertices = _verts;
            _mesh.colors = _colors;
            Graphics.DrawMesh(_mesh, Matrix4x4.identity, _material, 0);
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_material != null) Destroy(_material);
            if (s_Instance == this) s_Instance = null;
        }
    }
}
