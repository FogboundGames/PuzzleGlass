using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

namespace PuzzleGlass
{
    /// <summary>
    /// Bir blok dolup tepsiden çıktığında kutlama: kısa bir parlama halkası, bloğun renginde parçacık patlaması
    /// ve mobilde kısa bir titreşim. Kendi kendine yeten statik yardımcı; sahnede kurulum gerektirmez.
    /// Malzeme olarak sprite'ların varsayılan malzemesi kullanılır (build'de her zaman bulunur).
    /// </summary>
    public static class BlockClearCelebration
    {
        /// <summary>Titreşim açık mı? (PlayerPrefs "PG_Haptics", varsayılan açık)</summary>
        public static bool HapticsEnabled
        {
            get => PlayerPrefs.GetInt("PG_Haptics", 1) == 1;
            set { PlayerPrefs.SetInt("PG_Haptics", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>Sprite'ların varsayılan malzemesi (iz/parçacık için; build'de her zaman bulunur).</summary>
        public static Material SpriteMaterial { get { EnsureResources(); return s_SpriteMat; } }

        /// <summary>Kutuların arkasında (renderQueue 2992, slot karesinin 2990 önünde ama kutunun 2995 arkasında) çizilen efekt malzemesi.</summary>
        public static Material BackSpriteMaterial { get { EnsureResources(); return s_BackSpriteMat != null ? s_BackSpriteMat : s_SpriteMat; } }

        private static Sprite s_SoftDot;
        private static Sprite s_CrispRing;
        private static Sprite s_ConfettiStrip;
        private static Material s_SpriteMat;
        private static Material s_BackSpriteMat;
        private static ParticleSystem s_Burst;
        private static Mesh s_ConfettiSphereMesh;
        private static Material s_ConfettiBallMat;
        private static BalloonConfettiRunner s_ConfettiRunner;

        private static readonly PuzzleColor[] s_PartyConfettiColors =
        {
            PuzzleColor.Yellow,
            PuzzleColor.Pink,
            PuzzleColor.Cyan,
            PuzzleColor.Green,
            PuzzleColor.Purple,
            PuzzleColor.Orange,
            PuzzleColor.White
        };

        /// <summary>
        /// Smooth balloon-pop celebration: crisp expanding shockwaves + soft glow + glossy 3D balls scattering outward.
        /// </summary>
        public static void PlayBalloonPop(Vector3 worldPos, Color boxColor, PuzzleColor puzzleColor, float size = 1f, int scatterBallCount = 16)
        {
            if (!Application.isPlaying) return;
            EnsureResources();
            EnsureConfettiRunner();

            Vector3 pos = new Vector3(worldPos.x, worldPos.y, worldPos.z - 0.58f);
            PlayFlash(pos, Color.Lerp(boxColor, Color.white, 0.58f), size * 1.24f);
            PlayRing(pos, boxColor, size * 1.04f, duration: 0.36f, endScale: 2.50f);
            PlayRing(pos, Color.white, size * 0.78f, duration: 0.25f, endScale: 1.75f);

            if (s_ConfettiRunner != null)
            {
                // Soft fluffy 6-lobe cloud puff around the popped box
                if (s_SoftDot != null)
                {
                    Color puffCol = Color.Lerp(Color.white, boxColor, 0.16f);
                    const int puffCount = 6;
                    for (int p = 0; p < puffCount; p++)
                    {
                        float ang = (p / (float)puffCount) * Mathf.PI * 2f + 0.35f;
                        Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                        Vector3 puffPos = pos + (Vector3)(dir * (0.14f * size)) + new Vector3(0f, 0f, -0.02f);
                        s_ConfettiRunner.SpawnSpriteFx(
                            s_SoftDot,
                            puffPos,
                            puffCol,
                            startAlpha: 0.68f,
                            startScale: 0.24f * size,
                            endScale: Random.Range(0.74f, 0.96f) * size,
                            duration: Random.Range(0.24f, 0.32f),
                            sortingOrder: 501,
                            velocity: (Vector3)(dir * (0.55f * size)),
                            drag: 4.5f);
                    }
                }

                s_ConfettiRunner.SpawnBurst(pos, puzzleColor, size, count: Mathf.Clamp(scatterBallCount, 12, 24));
                s_ConfettiRunner.SpawnRibbonBurst(pos, puzzleColor, size, count: 8);
            }

            VibrateBoxClear();
        }

        public static void Play(Vector3 worldPos, Color color, float size = 1f)
        {
            if (!Application.isPlaying) return;
            EnsureResources();
            Vector3 pos = new Vector3(worldPos.x, worldPos.y, worldPos.z - 0.6f);
            PlayFlash(pos, color, size);
            PlayRing(pos, color, size, duration: 0.28f, endScale: 2.15f);
            PlayBurst(pos, color, size, count: 22);
            Vibrate(30);
        }

        /// <summary>
        /// Compact, crisp ASMR seal burst played immediately when a piece finishes filling and latches shut.
        /// </summary>
        public static void PlaySealRingPop(Vector3 worldPos, Color color, float size = 1f)
        {
            if (!Application.isPlaying) return;
            EnsureResources();
            Vector3 pos = new Vector3(worldPos.x, worldPos.y, worldPos.z - 0.6f);
            PlayRing(pos, color, size * 0.92f, duration: 0.22f, endScale: 1.85f);
            PlayBurst(pos, color, size * 0.85f, count: 10);
            Vibrate(18);
        }

        /// <summary>
        /// Clean ASMR implosion pop: one crisp colored ring and tight pastel/white sparkles with zero screen clutter.
        /// </summary>
        public static void PlayImplosionPop(Vector3 worldPos, Color color, float size = 1f)
        {
            if (!Application.isPlaying) return;
            EnsureResources();
            Vector3 pos = new Vector3(worldPos.x, worldPos.y, worldPos.z - 0.55f);
            PlayRing(pos, color, size * 0.82f, duration: 0.20f, endScale: 1.55f);
            PlayBurst(pos, color, size * 0.78f, count: 12);
            Vibrate(22);
        }

        /// <summary>
        /// Compact crisp glass-neck suction pulse when a ball enters an exit channel.
        /// </summary>
        public static void PlayTubeSuctionPulse(Vector3 worldPos, Color color, float size = 0.22f)
        {
            if (!Application.isPlaying) return;
            EnsureResources();
            if (s_CrispRing == null) return;
            EnsureConfettiRunner();

            // Emme: dışa büyüyen halka "patlama" gibi okunuyordu; ağza doğru DARALAN halka + içe çekilen kıvılcımlar
            float r = Mathf.Max(0.08f, size);
            Vector3 pos = new Vector3(worldPos.x, worldPos.y, worldPos.z - 0.14f);
            Color c = Color.Lerp(color, Color.white, 0.35f);
            s_ConfettiRunner.SpawnSpriteFx(s_CrispRing, pos, c, 0.70f, r * 2.7f, r * 0.55f, 0.20f, sortingOrder: 492);

            if (s_Burst == null) s_Burst = CreateBurstSystem();
            var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            const float life = 0.16f;
            for (int i = 0; i < 3; i++)
            {
                float ang = Random.Range(0f, Mathf.PI * 2f);
                Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                float dist = r * Random.Range(1.6f, 2.3f);
                emit.position = pos + (Vector3)(dir * dist);
                emit.velocity = -(Vector3)(dir * (dist / life)); // ömrü bitince tam ağza varır
                emit.startColor = i == 0 ? Color.white : Color.Lerp(color, Color.white, 0.5f);
                emit.startSize = Mathf.Max(0.02f, r * Random.Range(0.18f, 0.28f));
                emit.startLifetime = life;
                s_Burst.Emit(emit, 1);
            }
        }

        /// <summary>
        /// Micro tactile spark + faint contact ripple when a ball collides inside a carrier box cavity.
        /// </summary>
        public static void PlayCavityTapEffect(Vector3 worldPos, Color color, float ballWorldDiameter)
        {
            if (!Application.isPlaying) return;
            EnsureResources();
            Vector3 pos = new Vector3(worldPos.x, worldPos.y, worldPos.z - 0.12f);
            float radius = Mathf.Max(0.055f, ballWorldDiameter * 0.62f);
            PlaySocketSeatRing(pos, Color.Lerp(color, Color.white, 0.35f), radius, alpha: 0.52f);

            if (s_Burst == null) s_Burst = CreateBurstSystem();
            s_Burst.transform.position = pos;
            var emit = new ParticleSystem.EmitParams { applyShapeToPosition = true };
            for (int i = 0; i < 3; i++)
            {
                emit.startColor = i == 0 ? Color.white : Color.Lerp(color, Color.white, 0.45f);
                emit.startSize = Mathf.Max(0.024f, ballWorldDiameter * Random.Range(0.28f, 0.44f));
                float ang = Random.Range(0.15f, Mathf.PI - 0.15f);
                float spd = Random.Range(0.45f, 1.15f);
                emit.velocity = new Vector3(Mathf.Cos(ang) * spd, Mathf.Sin(ang) * spd, 0f);
                emit.startLifetime = Random.Range(0.14f, 0.24f);
                s_Burst.Emit(emit, 1);
            }
        }

        /// <summary>
        /// Subtle thin colored contact ring played right around a gray socket when a ball snaps in.
        /// </summary>
        public static void PlaySocketSeatRing(Vector3 worldPos, Color color, float radius = 0.24f, float alpha = 0.82f)
        {
            if (!Application.isPlaying) return;
            EnsureResources();
            if (s_CrispRing == null) return;
            EnsureConfettiRunner();

            Vector3 pos = new Vector3(worldPos.x, worldPos.y, worldPos.z - 0.14f);
            Color c = Color.Lerp(color, Color.white, 0.55f);
            float startScale = Mathf.Max(0.10f, radius * 1.15f);
            float endScale = Mathf.Max(0.22f, radius * 2.55f);
            s_ConfettiRunner.SpawnSpriteFx(s_CrispRing, pos, c, Mathf.Clamp01(alpha), startScale, endScale, 0.17f, sortingOrder: 492);
        }

        /// <summary>
        /// Kutu slota uçarken arkasında renkli parlak şerit izi ve yumuşak ışıltı parçacıkları bırakır.
        /// renderQueue = 2992 ile kutunun (2995) arkasında, slot zemininin (2990) önünde çizilir.
        /// </summary>
        public static void PlaySlotFlightTrailFx(Transform followTarget, Color color, float worldScale = 0.65f, float duration = 0.27f)
        {
            if (!Application.isPlaying || followTarget == null) return;
            EnsureResources();
            EnsureConfettiRunner();
            if (s_ConfettiRunner == null) return;

            s_ConfettiRunner.StartSlotFlightTrail(followTarget, color, Mathf.Max(0.35f, worldScale), Mathf.Max(0.08f, duration));
        }

        /// <summary>
        /// Kutu slota oturduğunda arkasından yayılan parlak hare, ince halka ve yıldız ışıltıları.
        /// </summary>
        public static void PlaySlotLandingBackFx(Vector3 slotWorldPos, Color color, float worldScale = 0.65f)
        {
            if (!Application.isPlaying) return;
            EnsureResources();
            EnsureConfettiRunner();
            if (s_ConfettiRunner == null) return;

            float s = Mathf.Max(0.40f, worldScale);
            Material backMat = BackSpriteMaterial;
            Vector3 backPos = new Vector3(slotWorldPos.x, slotWorldPos.y, Mathf.Min(-0.03f, slotWorldPos.z + 0.11f));

            Color glowCol = Color.Lerp(color, Color.white, 0.42f);
            Color ringCol = Color.Lerp(color, Color.white, 0.58f);

            if (s_SoftDot != null)
            {
                s_ConfettiRunner.SpawnSpriteFx(
                    s_SoftDot,
                    backPos,
                    glowCol,
                    startAlpha: 0.78f,
                    startScale: 0.55f * s,
                    endScale: 1.85f * s,
                    duration: 0.24f,
                    sortingOrder: 2,
                    overrideMat: backMat);
            }

            if (s_CrispRing != null)
            {
                s_ConfettiRunner.SpawnSpriteFx(
                    s_CrispRing,
                    backPos,
                    ringCol,
                    startAlpha: 0.88f,
                    startScale: 0.62f * s,
                    endScale: 1.65f * s,
                    duration: 0.22f,
                    sortingOrder: 3,
                    overrideMat: backMat);

                s_ConfettiRunner.SpawnSpriteFx(
                    s_CrispRing,
                    backPos,
                    Color.white,
                    startAlpha: 0.72f,
                    startScale: 0.48f * s,
                    endScale: 1.32f * s,
                    duration: 0.17f,
                    sortingOrder: 3,
                    overrideMat: backMat);
            }

            if (s_SoftDot != null)
            {
                const int puffCount = 6;
                for (int i = 0; i < puffCount; i++)
                {
                    float ang = (i / (float)puffCount) * Mathf.PI * 2f + Random.Range(-0.25f, 0.25f);
                    Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    Vector3 spawnPos = backPos + (Vector3)(dir * (0.20f * s));
                    Vector3 vel = (Vector3)(dir * (Random.Range(1.05f, 1.75f) * s));
                    Color puffCol = (i % 2 == 0) ? Color.white : Color.Lerp(color, Color.white, 0.48f);
                    s_ConfettiRunner.SpawnSpriteFx(
                        s_SoftDot,
                        spawnPos,
                        puffCol,
                        startAlpha: 0.90f,
                        startScale: Random.Range(0.14f, 0.22f) * s,
                        endScale: 0.02f * s,
                        duration: Random.Range(0.20f, 0.28f),
                        sortingOrder: 4,
                        velocity: vel,
                        drag: 6.5f,
                        overrideMat: backMat);
                }
            }
        }

        /// <summary>
        /// Top kutu yuvasına girdiğinde giriş noktasında hafif parlama ve yukarı süzülen renkli ışıltılar çıkarır.
        /// </summary>
        public static void PlayBoxBallLandingPop(Vector3 worldPos, Color color, float worldScale = 0.65f)
        {
            if (!Application.isPlaying) return;
            EnsureResources();
            EnsureConfettiRunner();
            if (s_ConfettiRunner == null || s_SoftDot == null) return;

            float s = Mathf.Max(0.35f, worldScale);
            Vector3 pos = new Vector3(worldPos.x, worldPos.y, worldPos.z - 0.10f);
            Color glowCol = Color.Lerp(color, Color.white, 0.45f);

            s_ConfettiRunner.SpawnSpriteFx(
                s_SoftDot,
                pos,
                glowCol,
                startAlpha: 0.62f,
                startScale: 0.14f * s,
                endScale: 0.44f * s,
                duration: 0.15f,
                sortingOrder: 503);

            const int glintCount = 3;
            for (int i = 0; i < glintCount; i++)
            {
                Vector3 offset = new Vector3(Random.Range(-0.10f, 0.10f) * s, Random.Range(0f, 0.06f) * s, -0.02f);
                Vector3 vel = new Vector3(Random.Range(-0.35f, 0.35f) * s, Random.Range(0.85f, 1.45f) * s, 0f);
                Color glintCol = (i == 0) ? Color.white : Color.Lerp(color, Color.white, 0.42f);
                s_ConfettiRunner.SpawnSpriteFx(
                    s_SoftDot,
                    pos + offset,
                    glintCol,
                    startAlpha: 0.88f,
                    startScale: Random.Range(0.08f, 0.13f) * s,
                    endScale: 0.015f * s,
                    duration: Random.Range(0.22f, 0.30f),
                    sortingOrder: 504,
                    velocity: vel,
                    drag: 4.2f);
            }
        }

        // ---------- Parlama ----------

        private static void PlayFlash(Vector3 pos, Color color, float size)
        {
            if (s_SoftDot == null) return;
            EnsureConfettiRunner();
            Color c = Color.Lerp(color, Color.white, 0.55f);
            s_ConfettiRunner.SpawnSpriteFx(s_SoftDot, pos, c, 0.95f, 0.35f * size, 1.9f * size, 0.32f, sortingOrder: 500);
        }

        private static void PlayRing(Vector3 pos, Color color, float size, float duration, float endScale)
        {
            if (s_CrispRing == null) return;
            EnsureConfettiRunner();
            Color c = Color.Lerp(color, Color.white, 0.68f);
            s_ConfettiRunner.SpawnSpriteFx(s_CrispRing, pos, c, 0.92f, 0.42f * size, endScale * size, duration, sortingOrder: 502);
        }

        // ---------- Parçacıklar ----------

        private static void PlayBurst(Vector3 pos, Color color, float size, int count)
        {
            if (s_Burst == null) s_Burst = CreateBurstSystem();
            s_Burst.transform.position = pos;

            var emit = new ParticleSystem.EmitParams
            {
                applyShapeToPosition = true
            };
            for (int i = 0; i < count; i++)
            {
                float t = Random.value;
                emit.startColor = t < 0.25f ? Color.white : Color.Lerp(color, Color.white, t * 0.35f);
                emit.startSize = Random.Range(0.06f, 0.15f) * size;
                float ang = Random.Range(0f, Mathf.PI * 2f);
                float spd = Random.Range(1.4f, 3.2f) * size;
                emit.velocity = new Vector3(Mathf.Cos(ang) * spd, Mathf.Sin(ang) * spd + 0.9f, 0f);
                emit.startLifetime = Random.Range(0.35f, 0.6f);
                s_Burst.Emit(emit, 1);
            }
        }

        private static ParticleSystem CreateBurstSystem()
        {
            var go = new GameObject("BlockClearBurst");
            Object.DontDestroyOnLoad(go);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.maxParticles = 400;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.55f;
            main.startSpeed = 0f;

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.18f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;

            var sizeOL = ps.sizeOverLifetime;
            sizeOL.enabled = true;
            sizeOL.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.2f));

            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.dampen = 0.12f;
            limit.limit = 0.6f;

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = s_SpriteMat;
            r.sortingOrder = 501;
            if (s_SoftDot != null) r.material.mainTexture = s_SoftDot.texture;
            return ps;
        }

        // ---------- Kaynaklar ----------

        private static void EnsureResources()
        {
            if (s_SoftDot == null)
            {
                const int n = 64;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var px = new Color32[n * n];
                float c = (n - 1) * 0.5f;
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * (3f - 2f * a); // yumuşak kenar
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
                tex.SetPixels32(px);
                tex.Apply(false, true);
                s_SoftDot = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
            }
            if (s_CrispRing == null)
            {
                const int n = 96;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var px = new Color32[n * n];
                float c = (n - 1) * 0.5f;
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    float band = Mathf.Exp(-Mathf.Pow((d - 0.80f) / 0.085f, 2f));
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(band) * 255f));
                }
                tex.SetPixels32(px);
                tex.Apply(false, true);
                s_CrispRing = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
            }
            if (s_ConfettiStrip == null)
            {
                const int w = 24;
                const int h = 10;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var px = new Color32[w * h];
                for (int y = 0; y < h; y++)
                {
                    float ny = (y + 0.5f) / h * 2f - 1f;
                    for (int x = 0; x < w; x++)
                    {
                        float nx = (x + 0.5f) / w * 2f - 1f;
                        float edge = Mathf.Max(Mathf.Abs(nx) - 0.55f, 0f) / 0.45f;
                        float dist = Mathf.Sqrt(edge * edge + ny * ny);
                        float a = Mathf.Clamp01((1f - dist) * 4.5f);
                        px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                    }
                }
                tex.SetPixels32(px);
                tex.Apply(false, true);
                s_ConfettiStrip = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 24f);
            }
            if (s_SpriteMat == null)
            {
                // Sprite'ın varsayılan malzemesi (Sprites-Default / URP 2D sprite) her build'de bulunur
                var tmp = new GameObject("tmp");
                s_SpriteMat = tmp.AddComponent<SpriteRenderer>().sharedMaterial;
                if (Application.isPlaying)
                    Object.Destroy(tmp);
                else
                    Object.DestroyImmediate(tmp);
            }
            if (s_BackSpriteMat == null && s_SpriteMat != null)
            {
                s_BackSpriteMat = new Material(s_SpriteMat)
                {
                    name = "Mat_BackSpriteFx",
                    renderQueue = 2992
                };
            }
        }

        // ---------- Top Konfeti Havuzu ----------

        private static void EnsureConfettiRunner()
        {
            if (s_ConfettiRunner != null) return;
            var go = new GameObject("BalloonConfettiPool");
            Object.DontDestroyOnLoad(go);
            s_ConfettiRunner = go.AddComponent<BalloonConfettiRunner>();

            if (s_ConfettiSphereMesh == null)
            {
                s_ConfettiSphereMesh = BallObjectPool.Instance != null ? BallObjectPool.Instance.SharedSphereMesh : null;
                if (s_ConfettiSphereMesh == null)
                {
                    var temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    var mf = temp.GetComponent<MeshFilter>();
                    if (mf != null) s_ConfettiSphereMesh = mf.sharedMesh;
                    Object.Destroy(temp);
                }
            }

            if (s_ConfettiBallMat == null)
            {
                if (BallObjectPool.Instance != null && BallObjectPool.Instance.SharedInstancedBallMaterial != null)
                {
                    s_ConfettiBallMat = BallObjectPool.Instance.SharedInstancedBallMaterial;
                }
                else
                {
                    Shader ballShader = Shader.Find("PuzzleGlass/CandyCubeLit")
                        ?? Shader.Find("Universal Render Pipeline/Lit")
                        ?? Shader.Find("Standard");
                    s_ConfettiBallMat = new Material(ballShader)
                    {
                        name = "Mat_BalloonConfettiBall",
                        enableInstancing = true,
                        renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry + 15
                    };
                }
            }
        }

        private class BalloonConfettiRunner : MonoBehaviour
        {
            private struct ConfettiBall
            {
                public GameObject go;
                public Transform tr;
                public MeshRenderer mr;
                public Vector3 vel;
                public Vector3 spinAxis;
                public float spinSpeed;
                public float baseScale;
                public float age;
                public float lifetime;
                public float drag;
                public float gravity;
                public bool active;
            }

            private struct SpriteFxItem
            {
                public GameObject go;
                public Transform tr;
                public SpriteRenderer sr;
                public Color baseColor;
                public Vector3 vel;
                public float drag;
                public float startAlpha;
                public float startScale;
                public float endScale;
                public float age;
                public float duration;
                public bool active;
            }

            private struct SlotFlightTrailItem
            {
                public GameObject go;
                public Transform tr;
                public TrailRenderer trail;
                public Transform followTarget;
                public Color color;
                public float worldScale;
                public float followAge;
                public float followDuration;
                public float lingerAge;
                public float emitTimer;
                public Vector3 lastTargetPos;
                public bool emittingStopped;
                public bool active;
            }

            private struct RibbonItem
            {
                public GameObject go;
                public Transform tr;
                public SpriteRenderer sr;
                public Color baseColor;
                public Vector3 vel;
                public float zSpinVel;
                public float xFlutterSpeed;
                public float xFlutterPhase;
                public float baseScale;
                public float age;
                public float lifetime;
                public float drag;
                public float gravity;
                public bool active;
            }

            private readonly List<ConfettiBall> _pool = new List<ConfettiBall>(64);
            private readonly List<SpriteFxItem> _spritePool = new List<SpriteFxItem>(64);
            private readonly List<RibbonItem> _ribbonPool = new List<RibbonItem>(64);
            private readonly List<SlotFlightTrailItem> _slotTrailPool = new List<SlotFlightTrailItem>(12);
            private static readonly AnimationCurve s_TrailWidthCurve = new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(0.45f, 0.52f),
                new Keyframe(1f, 0f));

            public void SpawnRibbonBurst(Vector3 center, PuzzleColor mainColor, float size, int count)
            {
                if (s_ConfettiStrip == null) return;
                for (int i = 0; i < count; i++)
                {
                    int idx = AcquireRibbonIndex();
                    RibbonItem r = _ribbonPool[idx];

                    float ang = (i / (float)count) * Mathf.PI * 2f + Random.Range(-0.24f, 0.24f);
                    float speed = Random.Range(1.8f, 3.85f) * size;
                    Vector2 offset = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * Random.Range(0.06f, 0.20f);

                    r.tr.position = new Vector3(center.x + offset.x, center.y + offset.y, center.z - 0.04f);
                    r.tr.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                    r.vel = new Vector3(Mathf.Cos(ang) * speed, Mathf.Sin(ang) * speed + Random.Range(0.85f, 1.65f) * size, 0f);
                    r.zSpinVel = Random.Range(-320f, 320f);
                    r.xFlutterSpeed = Random.Range(7.5f, 13.5f);
                    r.xFlutterPhase = Random.Range(0f, Mathf.PI * 2f);
                    r.baseScale = Random.Range(0.11f, 0.17f) * size;
                    r.age = 0f;
                    r.lifetime = Random.Range(0.75f, 1.08f);
                    r.drag = Random.Range(2.4f, 3.4f);
                    r.gravity = Random.Range(2.2f, 3.4f) * size;
                    r.active = true;

                    PuzzleColor chosen = (i % 3 == 0)
                        ? mainColor
                        : s_PartyConfettiColors[Random.Range(0, s_PartyConfettiColors.Length)];
                    Color col = Color.Lerp(PuzzleColorPalette.GetPrimaryColor(chosen), Color.white, 0.12f);
                    col.a = 1f;
                    r.baseColor = col;
                    r.sr.color = col;
                    r.tr.localScale = Vector3.zero;
                    r.go.SetActive(true);
                    _ribbonPool[idx] = r;
                }
            }

            private int AcquireRibbonIndex()
            {
                for (int i = 0; i < _ribbonPool.Count; i++)
                {
                    if (!_ribbonPool[i].active) return i;
                }

                var go = new GameObject($"ConfettiRibbon_{_ribbonPool.Count}");
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = s_ConfettiStrip;
                sr.sortingOrder = 505;
                go.SetActive(false);

                _ribbonPool.Add(new RibbonItem
                {
                    go = go,
                    tr = go.transform,
                    sr = sr,
                    active = false
                });
                return _ribbonPool.Count - 1;
            }

            public void StartSlotFlightTrail(Transform followTarget, Color color, float worldScale, float duration)
            {
                int idx = AcquireSlotTrailIndex();
                SlotFlightTrailItem item = _slotTrailPool[idx];

                Vector3 startPos = followTarget.position;
                Vector3 trailPos = new Vector3(startPos.x, startPos.y, Mathf.Min(-0.03f, startPos.z + 0.08f));
                item.tr.position = trailPos;

                if (item.trail != null)
                {
                    item.trail.sharedMaterial = BackSpriteMaterial;
                    item.trail.widthMultiplier = Mathf.Clamp(0.42f * worldScale, 0.16f, 0.44f);
                    item.trail.time = 0.20f;

                    Color headCol = Color.Lerp(color, Color.white, 0.35f);
                    Color tailCol = Color.Lerp(color, Color.white, 0.65f);
                    var grad = new Gradient();
                    grad.SetKeys(
                        new[] { new GradientColorKey(headCol, 0f), new GradientColorKey(tailCol, 1f) },
                        new[] { new GradientAlphaKey(0.68f, 0f), new GradientAlphaKey(0.28f, 0.55f), new GradientAlphaKey(0f, 1f) });
                    item.trail.colorGradient = grad;
                    item.trail.Clear();
                    item.trail.emitting = true;
                }

                item.followTarget = followTarget;
                item.color = color;
                item.worldScale = worldScale;
                item.followAge = 0f;
                item.followDuration = duration;
                item.lingerAge = 0f;
                item.emitTimer = 0f;
                item.lastTargetPos = startPos;
                item.emittingStopped = false;
                item.active = true;
                item.go.SetActive(true);
                _slotTrailPool[idx] = item;
            }

            private int AcquireSlotTrailIndex()
            {
                for (int i = 0; i < _slotTrailPool.Count; i++)
                {
                    if (!_slotTrailPool[i].active) return i;
                }

                var go = new GameObject($"SlotFlightTrail_{_slotTrailPool.Count}");
                go.transform.SetParent(transform, false);
                var tr = go.AddComponent<TrailRenderer>();
                tr.minVertexDistance = 0.02f;
                tr.numCapVertices = 4;
                tr.numCornerVertices = 3;
                tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                tr.receiveShadows = false;
                tr.alignment = LineAlignment.TransformZ;
                tr.sortingOrder = 2;
                tr.widthCurve = s_TrailWidthCurve;
                tr.sharedMaterial = BackSpriteMaterial;
                tr.emitting = false;
                go.SetActive(false);

                _slotTrailPool.Add(new SlotFlightTrailItem
                {
                    go = go,
                    tr = go.transform,
                    trail = tr,
                    active = false
                });
                return _slotTrailPool.Count - 1;
            }

            public void SpawnSpriteFx(
                Sprite sprite,
                Vector3 pos,
                Color color,
                float startAlpha,
                float startScale,
                float endScale,
                float duration,
                int sortingOrder,
                Vector3 velocity = default,
                float drag = 0f,
                Material overrideMat = null)
            {
                int idx = AcquireSpriteFxIndex();
                SpriteFxItem fx = _spritePool[idx];
                fx.tr.position = pos;
                fx.tr.localScale = Vector3.one * startScale;
                fx.sr.sprite = sprite;
                fx.sr.sortingOrder = sortingOrder;
                fx.sr.sharedMaterial = overrideMat != null ? overrideMat : s_SpriteMat;
                color.a = startAlpha;
                fx.sr.color = color;
                fx.baseColor = new Color(color.r, color.g, color.b, 1f);
                fx.vel = velocity;
                fx.drag = drag;
                fx.startAlpha = startAlpha;
                fx.startScale = startScale;
                fx.endScale = endScale;
                fx.age = 0f;
                fx.duration = Mathf.Max(0.02f, duration);
                fx.active = true;
                fx.go.SetActive(true);
                _spritePool[idx] = fx;
            }

            private int AcquireSpriteFxIndex()
            {
                for (int i = 0; i < _spritePool.Count; i++)
                {
                    if (!_spritePool[i].active) return i;
                }

                var go = new GameObject($"FxRing_{_spritePool.Count}");
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                go.SetActive(false);

                _spritePool.Add(new SpriteFxItem
                {
                    go = go,
                    tr = go.transform,
                    sr = sr,
                    active = false
                });
                return _spritePool.Count - 1;
            }

            public void SpawnBurst(Vector3 center, PuzzleColor mainColor, float size, int count)
            {
                for (int i = 0; i < count; i++)
                {
                    int idx = AcquireIndex();
                    ConfettiBall cb = _pool[idx];

                    float baseAngle = (i / (float)count) * Mathf.PI * 2f + Random.Range(-0.18f, 0.18f);
                    bool isHighArcTier = (i % 3 == 0);
                    float speed = isHighArcTier
                        ? Random.Range(1.55f, 2.70f) * size
                        : Random.Range(2.35f, 3.95f) * size;
                    float upLift = isHighArcTier
                        ? Random.Range(1.05f, 1.70f) * size
                        : Random.Range(0.55f, 1.15f) * size;
                    float zVel = isHighArcTier
                        ? Random.Range(-0.52f, -0.15f)
                        : Random.Range(-0.30f, 0.12f);

                    Vector2 offset = new Vector2(Mathf.Cos(baseAngle), Mathf.Sin(baseAngle)) * Random.Range(0.05f, 0.17f);

                    cb.tr.position = new Vector3(center.x + offset.x, center.y + offset.y, center.z + Random.Range(-0.18f, 0.04f));
                    cb.tr.rotation = Random.rotation;
                    cb.vel = new Vector3(Mathf.Cos(baseAngle) * speed, Mathf.Sin(baseAngle) * speed + upLift, zVel);
                    cb.spinAxis = Random.onUnitSphere;
                    cb.spinSpeed = Random.Range(95f, 230f);
                    cb.baseScale = Random.Range(0.078f, 0.132f) * size;
                    cb.age = 0f;
                    cb.lifetime = Random.Range(0.64f, 0.92f);
                    cb.drag = Random.Range(2.5f, 3.3f);
                    cb.gravity = Random.Range(3.6f, 4.9f) * size;
                    cb.active = true;

                    PuzzleColor chosenCol = (i % 6 == 5)
                        ? PuzzleColor.White
                        : mainColor;
                    int variant = i % 3;
                    BallObjectPool.ApplyBallPropertyBlock(cb.mr, chosenCol, variant, wobblePhase: i * 0.45f);

                    cb.tr.localScale = Vector3.zero;
                    cb.go.SetActive(true);
                    _pool[idx] = cb;
                }
            }

            private int AcquireIndex()
            {
                for (int i = 0; i < _pool.Count; i++)
                {
                    if (!_pool[i].active) return i;
                }

                var go = new GameObject($"ConfettiBall_{_pool.Count}");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = s_ConfettiSphereMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = s_ConfettiBallMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                go.SetActive(false);

                _pool.Add(new ConfettiBall
                {
                    go = go,
                    tr = go.transform,
                    mr = mr,
                    active = false
                });
                return _pool.Count - 1;
            }

            private void Update()
            {
                float dt = Mathf.Min(Time.deltaTime, 0.04f);
                if (dt <= 0f) return;

                for (int i = 0; i < _pool.Count; i++)
                {
                    ConfettiBall cb = _pool[i];
                    if (!cb.active || cb.go == null) continue;

                    cb.age += dt;
                    if (cb.age >= cb.lifetime)
                    {
                        cb.active = false;
                        cb.go.SetActive(false);
                        _pool[i] = cb;
                        continue;
                    }

                    cb.vel *= Mathf.Exp(-cb.drag * dt);
                    cb.vel.y -= cb.gravity * dt;
                    cb.tr.position += cb.vel * dt;
                    cb.tr.Rotate(cb.spinAxis, cb.spinSpeed * dt, Space.World);

                    float scaleEnv;
                    const float fadeInDur = 0.085f;
                    float fadeOutDur = Mathf.Min(0.30f, cb.lifetime * 0.42f);
                    if (cb.age < fadeInDur)
                    {
                        float u = cb.age / fadeInDur;
                        float inv = 1f - u;
                        // Smooth overshoot pop-in (0 -> ~1.12 -> 1.0)
                        scaleEnv = Mathf.Max(0f, 1f + Mathf.Sin(u * Mathf.PI) * 0.18f - inv * inv * inv);
                    }
                    else if (cb.age > cb.lifetime - fadeOutDur)
                    {
                        float u = Mathf.Clamp01((cb.lifetime - cb.age) / fadeOutDur);
                        scaleEnv = u * u * (3f - 2f * u);
                    }
                    else
                    {
                        scaleEnv = 1f;
                    }

                    cb.tr.localScale = Vector3.one * (cb.baseScale * scaleEnv);
                    _pool[i] = cb;
                }

                for (int i = 0; i < _spritePool.Count; i++)
                {
                    SpriteFxItem fx = _spritePool[i];
                    if (!fx.active || fx.go == null) continue;

                    fx.age += dt;
                    float t = Mathf.Clamp01(fx.age / fx.duration);
                    if (t >= 1f)
                    {
                        fx.active = false;
                        fx.go.SetActive(false);
                        _spritePool[i] = fx;
                        continue;
                    }

                    if (fx.vel.sqrMagnitude > 1e-6f)
                    {
                        if (fx.drag > 0f) fx.vel *= Mathf.Exp(-fx.drag * dt);
                        fx.tr.position += fx.vel * dt;
                    }

                    float inv = 1f - t;
                    float easeOut = 1f - inv * inv * inv;
                    float fade = 1f - t * t;
                    fx.tr.localScale = Vector3.one * Mathf.Lerp(fx.startScale, fx.endScale, easeOut);
                    Color c = fx.baseColor;
                    c.a = fx.startAlpha * fade;
                    fx.sr.color = c;
                    _spritePool[i] = fx;
                }

                for (int i = 0; i < _slotTrailPool.Count; i++)
                {
                    SlotFlightTrailItem item = _slotTrailPool[i];
                    if (!item.active || item.go == null) continue;

                    if (!item.emittingStopped)
                    {
                        item.followAge += dt;
                        bool targetAlive = item.followTarget != null && item.followTarget.gameObject.activeInHierarchy;
                        if (targetAlive && item.followAge < item.followDuration)
                        {
                            Vector3 targetPos = item.followTarget.position;
                            Vector3 trailPos = new Vector3(targetPos.x, targetPos.y, Mathf.Min(-0.03f, targetPos.z + 0.08f));
                            item.tr.position = trailPos;

                            item.emitTimer += dt;
                            if (item.emitTimer >= 0.028f && s_SoftDot != null)
                            {
                                item.emitTimer = 0f;
                                Vector3 moveDir = targetPos - item.lastTargetPos;
                                moveDir.z = 0f;
                                Vector2 backDir = moveDir.sqrMagnitude > 1e-5f ? -((Vector2)moveDir).normalized : Vector2.down;
                                Vector2 perp = new Vector2(-backDir.y, backDir.x);
                                float s = item.worldScale;
                                Vector3 puffPos = trailPos + (Vector3)(perp * Random.Range(-0.14f, 0.14f) * s);
                                Vector3 puffVel = (Vector3)(backDir * (Random.Range(0.25f, 0.65f) * s) + perp * (Random.Range(-0.28f, 0.28f) * s));
                                Color sparkCol = Random.value < 0.35f ? Color.white : Color.Lerp(item.color, Color.white, 0.45f);
                                SpawnSpriteFx(
                                    s_SoftDot,
                                    puffPos,
                                    sparkCol,
                                    startAlpha: 0.78f,
                                    startScale: Random.Range(0.11f, 0.18f) * s,
                                    endScale: 0.015f * s,
                                    duration: 0.20f,
                                    sortingOrder: 3,
                                    velocity: puffVel,
                                    drag: 5.5f,
                                    overrideMat: BackSpriteMaterial);
                            }
                            item.lastTargetPos = targetPos;
                        }
                        else
                        {
                            item.emittingStopped = true;
                            if (item.trail != null) item.trail.emitting = false;
                        }
                    }
                    else
                    {
                        item.lingerAge += dt;
                        float waitDur = item.trail != null ? item.trail.time + 0.04f : 0.24f;
                        if (item.lingerAge >= waitDur)
                        {
                            item.active = false;
                            item.followTarget = null;
                            if (item.trail != null) item.trail.Clear();
                            item.go.SetActive(false);
                        }
                    }

                    _slotTrailPool[i] = item;
                }

                for (int i = 0; i < _ribbonPool.Count; i++)
                {
                    RibbonItem r = _ribbonPool[i];
                    if (!r.active || r.go == null) continue;

                    r.age += dt;
                    if (r.age >= r.lifetime)
                    {
                        r.active = false;
                        r.go.SetActive(false);
                        _ribbonPool[i] = r;
                        continue;
                    }

                    r.vel *= Mathf.Exp(-r.drag * dt);
                    r.vel.y -= r.gravity * dt;
                    r.tr.position += r.vel * dt;

                    float zRot = r.tr.localEulerAngles.z + r.zSpinVel * dt;
                    float xTilt = Mathf.Sin(r.age * r.xFlutterSpeed + r.xFlutterPhase) * 65f;
                    r.tr.localRotation = Quaternion.Euler(xTilt, 0f, zRot);

                    float alpha = 1f;
                    float scaleEnv = 1f;
                    const float fadeIn = 0.05f;
                    float fadeOut = 0.30f;
                    if (r.age < fadeIn)
                    {
                        scaleEnv = r.age / fadeIn;
                    }
                    else if (r.age > r.lifetime - fadeOut)
                    {
                        float u = Mathf.Clamp01((r.lifetime - r.age) / fadeOut);
                        alpha = u * u;
                        scaleEnv = 0.65f + 0.35f * u;
                    }

                    r.tr.localScale = Vector3.one * (r.baseScale * scaleEnv);
                    Color c = r.baseColor;
                    c.a = alpha;
                    r.sr.color = c;
                    _ribbonPool[i] = r;
                }
            }
        }

        // ---------- Titreşim ----------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetHapticTimers()
        {
            s_LastTickTime = -1f;
            s_LastSlotPlaceTime = -1f;
            s_LastBoxClearTime = -1f;
        }

        /// <summary>Art arda iki tık arasındaki en kısa süre (sn): hızlı akışta telefon sürekli vızıldamasın.</summary>
        public static float TickMinInterval = 0.09f;
        private static float s_LastTickTime = -1f;
        private static float s_LastSlotPlaceTime = -1f;
        private static float s_LastBoxClearTime = -1f;

#if UNITY_ANDROID && !UNITY_EDITOR
        private static AndroidJavaObject s_Vibrator;
        private static AndroidJavaClass s_EffectClass;
        private static AndroidJavaObject s_TickEffect;
        private static AndroidJavaObject s_SlotPlaceEffect;
        private static AndroidJavaObject s_BoxClearEffect;
        private static int s_AndroidSdk = -1;
        private static bool s_VibratorInitDone;

        private static bool EnsureAndroidVibrator()
        {
            if (s_VibratorInitDone) return s_Vibrator != null;
            s_VibratorInitDone = true;
            try
            {
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                    s_AndroidSdk = version.GetStatic<int>("SDK_INT");

                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                    s_Vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");

                if (s_Vibrator != null && s_AndroidSdk >= 26)
                {
                    s_EffectClass = new AndroidJavaClass("android.os.VibrationEffect");
                    s_TickEffect = s_AndroidSdk >= 29
                        ? s_EffectClass.CallStatic<AndroidJavaObject>("createPredefined", 2) // EFFECT_TICK
                        : s_EffectClass.CallStatic<AndroidJavaObject>("createOneShot", 12L, 70);
                    s_SlotPlaceEffect = s_EffectClass.CallStatic<AndroidJavaObject>("createOneShot", 14L, 90);
                    s_BoxClearEffect = s_EffectClass.CallStatic<AndroidJavaObject>("createOneShot", 20L, 118);
                }
            }
            catch (System.Exception)
            {
                ResetAndroidVibratorCache();
            }
            return s_Vibrator != null;
        }

        private static void ResetAndroidVibratorCache()
        {
            s_Vibrator = null;
            s_EffectClass = null;
            s_TickEffect = null;
            s_SlotPlaceEffect = null;
            s_BoxClearEffect = null;
            s_VibratorInitDone = false;
        }

        private static void PlayCachedAndroidEffect(AndroidJavaObject cachedEffect, long legacyMilliseconds)
        {
            try
            {
                if (!EnsureAndroidVibrator()) return;
                if (s_AndroidSdk >= 26)
                {
                    if (cachedEffect != null) s_Vibrator.Call("vibrate", cachedEffect);
                }
                else if (legacyMilliseconds > 0)
                {
                    s_Vibrator.Call("vibrate", legacyMilliseconds);
                }
            }
            catch (System.Exception)
            {
                ResetAndroidVibratorCache();
            }
        }
#endif

        /// <summary>Kısa ve hafif titreşim. Android'de milisaniye ve genlik (1..255) ayarlıdır.</summary>
        public static void Vibrate(long milliseconds, int amplitude = 115)
        {
            if (!HapticsEnabled) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (!EnsureAndroidVibrator()) return;
                if (s_AndroidSdk >= 26 && s_EffectClass != null)
                {
                    int amp = amplitude < 0 ? -1 : Mathf.Clamp(amplitude, 1, 255);
                    using (var effect = s_EffectClass.CallStatic<AndroidJavaObject>("createOneShot", milliseconds, amp))
                        s_Vibrator.Call("vibrate", effect);
                }
                else
                {
                    s_Vibrator.Call("vibrate", milliseconds);
                }
            }
            catch (System.Exception)
            {
                Handheld.Vibrate();
            }
#elif UNITY_IOS && !UNITY_EDITOR
            Handheld.Vibrate();
#endif
        }

        /// <summary>
        /// Kutu slota yerleştiğinde kısa, hafif ve net dokunsal geri bildirim.
        /// Top toplama tiklerinden bağımsız zamanlayıcı kullanır; böylece top akışı sırasında yutulmaz.
        /// </summary>
        public static void VibrateSlotPlace()
        {
            if (!HapticsEnabled) return;
            float now = Time.unscaledTime;
            if (now - s_LastSlotPlaceTime < 0.04f) return;
            s_LastSlotPlaceTime = now;
#if UNITY_ANDROID && !UNITY_EDITOR
            PlayCachedAndroidEffect(s_SlotPlaceEffect, 14L);
#endif
        }

        /// <summary>
        /// Kutu dolup patlayarak yok olurken hafif ama hissedilir kutlama titreşimi.
        /// </summary>
        public static void VibrateBoxClear()
        {
            if (!HapticsEnabled) return;
            float now = Time.unscaledTime;
            if (now - s_LastBoxClearTime < 0.05f) return;
            s_LastBoxClearTime = now;
#if UNITY_ANDROID && !UNITY_EDITOR
            PlayCachedAndroidEffect(s_BoxClearEffect, 18L);
#elif UNITY_IOS && !UNITY_EDITOR
            Handheld.Vibrate();
#endif
        }

        /// <summary>
        /// Top kutuya girdiğinde çok kısa, hafif "tık" titreşimi (slot/kutu kapanışından daha yumuşak).
        /// Android 10+ sistemin hazır EFFECT_TICK'ini, daha eskilerde 12 ms'lik düşük genlikli titreşimi kullanır.
        /// Titreşim nesneleri bir kez alınıp saklanır (her topta JNI ile yeniden aranmaz). iOS'ta uzun sistem
        /// titreşimi olduğu için kullanılmaz.
        /// </summary>
        public static void VibrateTick()
        {
            if (!HapticsEnabled) return;
            float now = Time.unscaledTime;
            if (now - s_LastTickTime < TickMinInterval) return;
            s_LastTickTime = now;
#if UNITY_ANDROID && !UNITY_EDITOR
            PlayCachedAndroidEffect(s_TickEffect, 0L);
#endif
        }
    }
}
