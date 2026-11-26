using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PuzzleGlass
{
    /// <summary>
    /// Captures tightly-framed offscreen snapshots of the game's actual MonsterCharacter blocks
    /// (Filled Box, Frozen Block, Lock &amp; Key) and generates crisp celebratory sprites.
    /// </summary>
    public static class MechanicFeaturePreviewRenderer
    {
        private const int PreviewLayer = 30;
        private const int TextureSize = 512;

        public struct PreviewSnapshot
        {
            public RenderTexture texture;
            public Vector2 contentVBounds;
        }

        private static readonly Dictionary<GameFeatureKind, PreviewSnapshot> s_Cache = new Dictionary<GameFeatureKind, PreviewSnapshot>();
        private static Sprite s_SunburstSprite;
        private static Sprite s_RadialGlowSprite;
        private static Sprite s_SparkleSprite;
        private static Sprite s_CelebrationRingSprite;
        private static Sprite s_ConfettiSprite;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCaches()
        {
            ReleaseAll();
        }

        public static void ReleaseAll()
        {
            foreach (var kv in s_Cache)
            {
                if (kv.Value.texture != null)
                {
                    kv.Value.texture.Release();
                    Object.Destroy(kv.Value.texture);
                }
            }
            s_Cache.Clear();
        }

        public static PreviewSnapshot GetOrCapturePreview(GameFeatureKind kind, LevelMonsterEntry sampleEntry)
        {
            if (s_Cache.TryGetValue(kind, out var existing) && existing.texture != null && existing.texture.IsCreated())
            {
                return existing;
            }

            var captured = CaptureSnapshot(kind, sampleEntry);
            s_Cache[kind] = captured;
            return captured;
        }

        private static PreviewSnapshot CaptureSnapshot(GameFeatureKind kind, LevelMonsterEntry entry)
        {
            var rt = new RenderTexture(TextureSize, TextureSize, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = $"RT_FeaturePreview_{kind}",
                antiAliasing = 4,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false
            };
            rt.Create();

            if (entry == null)
            {
                entry = CreateFallbackEntry(kind);
            }

            var stageObj = new GameObject("FeaturePreviewStage");
            stageObj.transform.position = new Vector3(500f, 500f, 0f);

            var monsterObj = new GameObject("PreviewMonster");
            monsterObj.SetActive(false);
            monsterObj.transform.SetParent(stageObj.transform, false);
            monsterObj.transform.localPosition = Vector3.zero;
            monsterObj.transform.localScale = Vector3.one;

            var mc = monsterObj.AddComponent<MonsterCharacter>();
            var gm = PuzzleGlassGameManager.Instance;
            mc.SetVisualStyle(gm != null ? gm.blockVisualStyle : BlockVisualStyle.PuzzleWithNumber);
            mc.Initialize(
                entry.color,
                Mathf.Max(1, entry.count),
                $"Preview_{kind}",
                CarrierBlockShape.Rect1x1,
                entry.hasKey,
                entry.lockKeyCount,
                entry.iceCount,
                entry.isFilledBox,
                entry.filledBallColor);
            monsterObj.SetActive(true);
            mc.KillAllTweens();

            // Disable shadow & all numeric TMP counters so preview shows pure mechanic visuals without sliced digits
            var allTransforms = monsterObj.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < allTransforms.Length; i++)
            {
                var tr = allTransforms[i];
                if (tr != null && tr.name == "CarrierBoxFakeShadow")
                {
                    tr.gameObject.SetActive(false);
                }
            }

            var tmps = monsterObj.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < tmps.Length; i++)
            {
                if (tmps[i] != null) tmps[i].gameObject.SetActive(false);
            }

            // For Frozen Block preview, add our crisp theme snowflake icon centered on the ice cap
            if (kind == GameFeatureKind.FrozenBlock)
            {
                var theme = PuzzleGlassUITheme.Load();
                if (theme != null && theme.iconSnow != null)
                {
                    Transform iceOverlay = mc.transform.Find("BodyPivot/IceOverlay");
                    Transform snowParent = iceOverlay != null ? iceOverlay : mc.transform;
                    LockKeyVisuals.CreateSpriteIcon("PreviewSnowIcon", snowParent, theme.iconSnow, 0.44f, new Vector3(0f, 0.01f, -0.05f), 16);
                }
            }

            SetLayerRecursively(stageObj, PreviewLayer);

            Camera mainCam = gm != null && gm.mainCamera != null ? gm.mainCamera : Camera.main;
            if (mainCam != null)
            {
                mainCam.cullingMask &= ~(1 << PreviewLayer);
            }

            // Use the actual visible SDF box geometry bounds (not the transparent quad padding) so the block fills the frame cleanly
            CarrierBlockShapeUtility.GetRectParts(CarrierBlockShape.Rect1x1, out Vector4 r0, out _, out _, out _, out _);
            float pad = entry.hasKey ? 0.07f : 0.035f;
            float minX = r0.x - r0.z - 0.035f;
            float maxX = r0.x + r0.z + pad;
            float minY = r0.y - r0.w - CarrierBlockShapeUtility.LipDepthOffset - 0.015f;
            float maxY = r0.y + r0.w + (entry.hasKey ? 0.065f : 0.020f);

            Vector3 center = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, 0f) + stageObj.transform.position;
            float halfW = (maxX - minX) * 0.5f;
            float halfH = (maxY - minY) * 0.5f;
            float halfSpan = Mathf.Max(halfW, halfH, 0.42f);

            var camObj = new GameObject("FeaturePreviewCamera");
            camObj.transform.SetParent(stageObj.transform, false);
            camObj.transform.position = new Vector3(center.x, center.y, center.z - 5f);
            camObj.transform.rotation = Quaternion.identity;

            var cam = camObj.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = halfSpan * 1.12f;
            cam.aspect = 1f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 20f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.cullingMask = 1 << PreviewLayer;
            cam.allowHDR = false;
            cam.allowMSAA = true;
            cam.targetTexture = rt;
            cam.enabled = false;

            var urpData = cam.GetUniversalAdditionalCameraData();
            if (urpData != null)
            {
                urpData.renderShadows = false;
                urpData.renderPostProcessing = false;
                urpData.antialiasing = AntialiasingMode.None;
            }

            Vector3 visMin = stageObj.transform.position + new Vector3(minX, minY, 0f);
            Vector3 visMax = stageObj.transform.position + new Vector3(maxX, maxY, 0f);
            float minV = Mathf.Clamp01(cam.WorldToViewportPoint(visMin).y);
            float maxV = Mathf.Clamp01(cam.WorldToViewportPoint(visMax).y);
            if (maxV - minV < 0.05f)
            {
                minV = 0.08f;
                maxV = 0.92f;
            }

            cam.Render();
            cam.targetTexture = null;

            if (Application.isPlaying) Object.Destroy(stageObj);
            else Object.DestroyImmediate(stageObj);

            return new PreviewSnapshot
            {
                texture = rt,
                contentVBounds = new Vector2(minV, maxV)
            };
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            Transform t = go.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                SetLayerRecursively(t.GetChild(i).gameObject, layer);
            }
        }

        private static LevelMonsterEntry CreateFallbackEntry(GameFeatureKind kind)
        {
            switch (kind)
            {
                case GameFeatureKind.FilledBox:
                    return new LevelMonsterEntry(PuzzleColor.Purple, 18, CarrierBlockShape.Rect1x1, Vector2Int.zero)
                    {
                        isFilledBox = true,
                        filledBallColor = PuzzleColor.Pink
                    };
                case GameFeatureKind.FrozenBlock:
                    return new LevelMonsterEntry(PuzzleColor.Blue, 18, CarrierBlockShape.Rect1x1, Vector2Int.zero)
                    {
                        iceCount = 15
                    };
                case GameFeatureKind.LockAndKey:
                default:
                    return new LevelMonsterEntry(PuzzleColor.Green, 18, CarrierBlockShape.Rect1x1, Vector2Int.zero)
                    {
                        lockKeyCount = 1,
                        hasKey = true
                    };
            }
        }

        /// <summary>Generates crisp high-contrast wedge sunburst rays extending clearly past the showcase box.</summary>
        public static Sprite GetOrCreateSunburstSprite()
        {
            if (s_SunburstSprite != null) return s_SunburstSprite;

            const int S = 512;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "FeatureSunburst"
            };

            var px = new Color32[S * S];
            const int rays = 14;
            for (int y = 0; y < S; y++)
            {
                float ny = (y + 0.5f) / S * 2f - 1f;
                for (int x = 0; x < S; x++)
                {
                    float nx = (x + 0.5f) / S * 2f - 1f;
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    float ang = Mathf.Atan2(ny, nx);

                    float wave = 0.5f + 0.5f * Mathf.Cos(ang * rays);
                    // Crisp wedge beams with soft AA transition
                    float beam = Mathf.SmoothStep(0.38f, 0.56f, wave);
                    float centerHalo = Mathf.Exp(-r * r * 3.8f);
                    float outerFade = 1f - Mathf.SmoothStep(0.45f, 0.98f, r);
                    float a = Mathf.Clamp01((beam * 0.72f + centerHalo * 0.85f) * outerFade);

                    px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            s_SunburstSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
            return s_SunburstSprite;
        }

        /// <summary>Generates a smooth luminous radial glow sprite for behind-box halos.</summary>
        public static Sprite GetOrCreateRadialGlowSprite()
        {
            if (s_RadialGlowSprite != null) return s_RadialGlowSprite;

            const int S = 256;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "FeatureRadialGlow"
            };

            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
            {
                float ny = (y + 0.5f) / S * 2f - 1f;
                for (int x = 0; x < S; x++)
                {
                    float nx = (x + 0.5f) / S * 2f - 1f;
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    float core = Mathf.Exp(-r * r * 5.0f);
                    float aura = Mathf.Exp(-r * r * 2.1f) * 0.65f;
                    float edgeFade = 1f - Mathf.SmoothStep(0.70f, 1.0f, r);
                    float a = Mathf.Clamp01(Mathf.Max(core, aura) * edgeFade);
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            s_RadialGlowSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
            return s_RadialGlowSprite;
        }

        /// <summary>Generates a crisp 4-point star sparkle sprite for celebratory highlights.</summary>
        public static Sprite GetOrCreateSparkleSprite()
        {
            if (s_SparkleSprite != null) return s_SparkleSprite;

            const int S = 96;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "FeatureSparkle"
            };

            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
            {
                float ny = Mathf.Abs((y + 0.5f) / S * 2f - 1f);
                for (int x = 0; x < S; x++)
                {
                    float nx = Mathf.Abs((x + 0.5f) / S * 2f - 1f);
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    float starH = Mathf.Exp(-ny * 10.5f) * Mathf.Exp(-nx * 2.6f);
                    float starV = Mathf.Exp(-nx * 10.5f) * Mathf.Exp(-ny * 2.6f);
                    float core = Mathf.Exp(-r * r * 14f);
                    float edge = 1f - Mathf.SmoothStep(0.75f, 1.0f, r);
                    float a = Mathf.Clamp01((Mathf.Max(starH, starV) + core * 0.85f) * edge);
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            s_SparkleSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
            return s_SparkleSprite;
        }

        /// <summary>Generates a crisp luminous ring sprite for 100% unlock shockwave bursts.</summary>
        public static Sprite GetOrCreateCelebrationRingSprite()
        {
            if (s_CelebrationRingSprite != null) return s_CelebrationRingSprite;

            const int S = 192;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "FeatureCelebrationRing"
            };

            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
            {
                float ny = (y + 0.5f) / S * 2f - 1f;
                for (int x = 0; x < S; x++)
                {
                    float nx = (x + 0.5f) / S * 2f - 1f;
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    float d = Mathf.Abs(r - 0.80f);
                    float a = Mathf.Exp(-d * d * 260f);
                    if (r > 0.98f) a *= 1f - Mathf.SmoothStep(0.92f, 0.98f, r);
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            s_CelebrationRingSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
            return s_CelebrationRingSprite;
        }

        /// <summary>Generates a crisp rounded confetti piece sprite.</summary>
        public static Sprite GetOrCreateConfettiSprite()
        {
            if (s_ConfettiSprite != null) return s_ConfettiSprite;

            const int S = 48;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "FeatureConfetti"
            };

            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
            {
                float ny = Mathf.Abs((y + 0.5f) / S * 2f - 1f);
                for (int x = 0; x < S; x++)
                {
                    float nx = Mathf.Abs((x + 0.5f) / S * 2f - 1f);
                    float d = Mathf.Max(nx - 0.74f, ny - 0.46f);
                    float a = Mathf.Clamp01(0.5f - d * 18f);
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            s_ConfettiSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
            return s_ConfettiSprite;
        }
    }
}
