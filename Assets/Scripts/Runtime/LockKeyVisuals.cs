using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

namespace PuzzleGlass
{
    /// <summary>
    /// Kilit / anahtar görselleri ve animasyonları, blokların düz ve yumuşak stilinde:
    ///  - Kilitli blok: beyaz plaka bloğun kendi renginde bir kapakla kapanır, ortasında düz beyaz asma kilit
    ///    (PuzzleGlass/CarrierBox shader'ında çizilir: _LockAmount, _LockOpen, _LockWiggle, _LockCenter).
    ///  - Anahtar: beyaz gövde, bloğun renginde kenar (bloğun beyaz plaka + renkli çerçeve diliyle aynı).
    ///  - Anahtarlı blok: köşede beyaz yuvarlak rozet, renkli çerçeve, içinde bloğun renginde anahtar.
    ///  - Kilit açma: anahtar uçar → deliğe girer → döner → halka kalkar → kapak açılır, delikler görünür.
    /// </summary>
    public static class LockKeyVisuals
    {
        public static readonly int LockAmountId = Shader.PropertyToID("_LockAmount");
        public static readonly int LockOpenId = Shader.PropertyToID("_LockOpen");
        public static readonly int LockWiggleId = Shader.PropertyToID("_LockWiggle");
        public static readonly int LockCenterId = Shader.PropertyToID("_LockCenter");

        /// <summary>Kapak üstündeki kilit simgesinin ölçeği.</summary>
        public const float GlyphScale = 1.3f;

        /// <summary>Kilit simgesinin yerel merkezine göre anahtar deliği (shader'daki çizimle aynı).</summary>
        public static readonly Vector2 KeyholeOffset = new Vector2(0f, -0.034f);

        private static readonly Dictionary<Color, Material> s_Flat = new Dictionary<Color, Material>();

        /// <summary>Düz (gölgesiz görünen) renk malzemesi; renk başına bir kez oluşturulur.</summary>
        private static Material Flat(Color c)
        {
            if (!s_Flat.TryGetValue(c, out var m) || m == null)
            {
                m = PuzzleColorPalette.CreateToonyHypercasualMaterial(c, new Color(c.r * 0.96f, c.g * 0.96f, c.b * 0.96f, 1f),
                    $"Mat_LK_{ColorUtility.ToHtmlStringRGB(c)}", glossyCandy: false, indirectIntensity: 0.35f);
                s_Flat[c] = m;
            }
            return m;
        }

        /// <summary>Tema varlığından gümüş kilit sprite'ını döner.</summary>
        public static Sprite GetLockSprite()
        {
            var theme = PuzzleGlassUITheme.Load();
            if (theme == null) return null;
            return theme.coloredGoldLock != null ? theme.coloredGoldLock : theme.iconLock;
        }

        /// <summary>Tema varlığından gümüş anahtar sprite'ını döner.</summary>
        public static Sprite GetKeySprite()
        {
            var theme = PuzzleGlassUITheme.Load();
            if (theme == null) return null;
            return theme.coloredGoldKey != null ? theme.coloredGoldKey : theme.iconKey;
        }

        /// <summary>Verilen sprite'ın max eksenini targetWorldSize'a ölçekleyen tekdüze localScale değerini hesaplar.</summary>
        public static Vector3 GetIconScale(Sprite sprite, float targetWorldSize)
        {
            if (sprite == null) return Vector3.one;
            Vector3 b = sprite.bounds.size;
            float maxDim = Mathf.Max(b.x, b.y, 0.001f);
            return Vector3.one * (targetWorldSize / maxDim);
        }

        /// <summary>Uçan altın anahtar izinin rengi.</summary>
        public static readonly Color LockKeyYellowTint = new Color(1.00f, 0.84f, 0.18f, 1f);

        /// <summary>Gold Lock / Gold Key sprite'ını yumuşak gölgeli bir SpriteRenderer olarak oluşturur.</summary>
        public static GameObject CreateSpriteIcon(string name, Transform parent, Sprite sprite, float targetWorldSize, Vector3 localPos, int sortingOrder)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            Vector3 scale = GetIconScale(sprite, targetWorldSize);
            go.transform.localScale = scale;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = Color.white;
            sr.sortingOrder = sortingOrder;

            float invS = scale.x > 0.001f ? (1f / scale.x) : 1f;
            var sh = new GameObject("Shadow");
            sh.transform.SetParent(go.transform, false);
            sh.transform.localPosition = new Vector3(0.022f * invS, -0.028f * invS, 0.004f * invS);
            sh.transform.localScale = Vector3.one;
            var shSr = sh.AddComponent<SpriteRenderer>();
            shSr.sprite = sprite;
            shSr.color = new Color(0.05f, 0.06f, 0.18f, 0.42f);
            shSr.sortingOrder = sortingOrder - 1;

            return go;
        }

        private static GameObject Part(string name, Transform parent, Vector3 pos, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        private static Mesh Pill(float w, float h, float t, float r) => ProceduralMeshUtils.CreatePillBadge(w, h, t, r);
        private static Mesh Disc(float d, float t) => ProceduralMeshUtils.CreatePillBadge(d, d, t, d * 0.5f);

        // ==================== Anahtar ====================

        /// <summary>
        /// Yatay, sade anahtar: yuvarlak delikli baş solda, iki diş sağda. fill = gövde rengi, edge = kenar rengi
        /// (edge alfa 0 ise kenar çizilmez). Parçalar "KeyMesh" çocuğundadır (yerel X = şaft ekseni, döndürme için).
        /// </summary>
        public static GameObject CreateKey(Transform parent, Color fill, Color edge, string name = "Key")
        {
            var root = new GameObject(name);
            if (parent != null) root.transform.SetParent(parent, false);
            var mesh = new GameObject("KeyMesh").transform;
            mesh.SetParent(root.transform, false);

            Material fm = Flat(fill);
            bool hasEdge = edge.a > 0.01f;
            Material em = hasEdge ? Flat(edge) : null;
            const float back = 0.02f;
            const float e = 0.026f; // kenar kalınlığı
            Vector3 bow = new Vector3(-0.14f, 0f, 0f);

            // Baş: kalın halka (ortası boş)
            if (hasEdge) Part("BowEdge", mesh, bow + new Vector3(0, 0, back), ProceduralMeshUtils.CreateCircularPipe(Vector3.zero, 0.088f, 0.046f + e, 24, 8), em);
            Part("Bow", mesh, bow, ProceduralMeshUtils.CreateCircularPipe(Vector3.zero, 0.088f, 0.046f, 24, 8), fm);

            // Şaft
            if (hasEdge) Part("ShaftEdge", mesh, new Vector3(0.12f, 0f, back), Pill(0.30f + e * 2f, 0.070f + e * 2f, 0.03f, 0.035f + e), em);
            Part("Shaft", mesh, new Vector3(0.12f, 0f, 0f), Pill(0.30f, 0.070f, 0.04f, 0.035f), fm);

            // Dişler
            if (hasEdge)
            {
                Part("Tooth1Edge", mesh, new Vector3(0.185f, -0.060f, back), Pill(0.052f + e * 2f, 0.09f + e * 2f, 0.03f, 0.02f + e), em);
                Part("Tooth2Edge", mesh, new Vector3(0.250f, -0.050f, back), Pill(0.046f + e * 2f, 0.07f + e * 2f, 0.03f, 0.02f + e), em);
            }
            Part("Tooth1", mesh, new Vector3(0.185f, -0.060f, 0f), Pill(0.052f, 0.09f, 0.04f, 0.02f), fm);
            Part("Tooth2", mesh, new Vector3(0.250f, -0.050f, 0f), Pill(0.046f, 0.07f, 0.04f, 0.02f), fm);
            return root;
        }

        /// <summary>Anahtarlı bloğun sağ üst köşesindeki belirgin, gölgeli hypercasual anahtar ikonu.</summary>
        public static GameObject CreateKeyBadge(Transform parent, Color rim, Color face, Color glyph, string name = "KeyBadge")
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            var swing = new GameObject("Swing").transform;
            swing.SetParent(root.transform, false);

            Sprite keySprite = GetKeySprite();
            GameObject key;
            if (keySprite != null)
            {
                key = CreateSpriteIcon("BadgeKey", swing, keySprite, 0.45f, Vector3.zero, 15);
            }
            else
            {
                Part("BadgeShadow", swing, new Vector3(0f, -0.03f, 0.03f), Disc(0.42f, 0.02f), Flat(Color.Lerp(rim, Color.black, 0.18f)));
                Part("BadgeRim", swing, new Vector3(0, 0, 0.012f), Disc(0.42f, 0.04f), Flat(rim));
                Part("BadgeFace", swing, new Vector3(0, 0.004f, -0.012f), Disc(0.33f, 0.03f), Flat(face));
                key = CreateKey(swing, glyph, new Color(0, 0, 0, 0), "BadgeKey");
                key.transform.localPosition = new Vector3(-0.006f, 0.004f, -0.04f);
                key.transform.localRotation = Quaternion.Euler(0, 0, 40f);
                key.transform.localScale = Vector3.one * 0.66f;
            }
            Vector3 keyRestScale = key.transform.localScale;

            if (Application.isPlaying)
            {
                // Yumuşak süzülme + sallanma + hafif parlama nabzı
                swing.localRotation = Quaternion.Euler(0, 0, -6f);
                swing.DOLocalRotate(new Vector3(0, 0, 6f), 1.15f).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetLink(root);
                swing.DOLocalMoveY(0.022f, 1.35f).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetLink(root);
                Sequence pulse = DOTween.Sequence();
                pulse.AppendInterval(2.2f);
                pulse.Append(key.transform.DOPunchScale(keyRestScale * 0.15f, 0.35f, 5, 0.6f));
                pulse.SetLoops(-1).SetLink(root);
            }
            return root;
        }

        /// <summary>Kilitli bloğa dokununca: anahtarlı bloğun rozeti "beni doldur" diye zıplar.</summary>
        public static void PlayKeyHint(GameObject badge)
        {
            if (badge == null || !Application.isPlaying) return;
            var t = badge.transform;

            // Rozetin asıl boyutu/açısı ilk ipucunda kaydedilir; her ipucu buradan başlar. Aksi halde arka arkaya
            // dokununca yeni zıplama, bir öncekinin büyümüş boyutunu taban alıp anahtarı kalıcı büyütüyordu.
            var rest = badge.GetComponent<KeyHintRest>();
            if (rest == null)
            {
                rest = badge.AddComponent<KeyHintRest>();
                rest.scale = t.localScale;
                rest.rotation = t.localRotation;
            }
            DOTween.Kill(t); // önceki ipucu (sequence hedefi = rozet) durur
            t.localScale = rest.scale;
            t.localRotation = rest.rotation;

            Sequence s = DOTween.Sequence();
            s.Append(t.DOPunchScale(rest.scale * 0.40f, 0.45f, 6, 0.5f));
            s.Insert(0f, t.DOPunchRotation(new Vector3(0, 0, 22f), 0.45f, 8, 0.6f));
            s.SetTarget(t).SetLink(badge);
        }

        /// <summary>Kilitli bloğa dokununca kilit simgesi sallanır (shader'da).</summary>
        public static void PlayLockWiggle(Material bodyMaterial, GameObject link)
        {
            if (bodyMaterial == null || !bodyMaterial.HasProperty(LockWiggleId)) return;
            DOTween.Kill(bodyMaterial, true);
            float w = 0f;
            DOTween.To(() => w, v => { w = v; if (bodyMaterial != null) bodyMaterial.SetFloat(LockWiggleId, Mathf.Sin(v * Mathf.PI * 5f) * 0.32f * (1f - v)); }, 1f, 0.42f)
                .SetTarget(bodyMaterial).SetLink(link)
                .OnComplete(() => { if (bodyMaterial != null) bodyMaterial.SetFloat(LockWiggleId, 0f); });
        }

        /// <summary>Kilitli bloğa dokunulunca kilit simgesi MaterialPropertyBlock ve kilit sprite'ı üzerinden sallanır.</summary>
        public static void PlayLockWiggle(MonsterCharacter box, Transform lockVisual = null)
        {
            if (box == null) return;
            DOTween.Kill(box, true);
            float w = 0f;
            DOTween.To(() => w, v =>
            {
                w = v;
                float angle = Mathf.Sin(v * Mathf.PI * 5f) * 0.32f * (1f - v);
                if (box != null) box.SetLockWiggle(angle);
                if (lockVisual != null) lockVisual.localRotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg);
            }, 1f, 0.42f)
                .SetTarget(box).SetLink(box.gameObject)
                .OnComplete(() =>
                {
                    if (box != null) box.SetLockWiggle(0f);
                    if (lockVisual != null) lockVisual.localRotation = Quaternion.identity;
                });
        }

        // ==================== Kilit açma ====================

        /// <summary>
        /// Anahtarı başlangıçtan kilidin deliğine uçurur (yumuşak beyaz iz), deliğe sokar ve çevirir. Bitince onTurned
        /// çağrılır (kilidin açılması orada başlar). Oluşturulan anahtar objesini döner.
        /// </summary>
        public static GameObject FlyKeyIntoLock(Transform vfxParent, Vector3 start, Func<Vector3> keyholeWorld, float scale,
                                                Color fill, Color edge, Action onTurned)
        {
            Sprite keySprite = GetKeySprite();
            GameObject key;
            Transform meshT;
            bool useSprite = keySprite != null;
            if (useSprite)
            {
                key = new GameObject("FlyingKey");
                if (vfxParent != null) key.transform.SetParent(vfxParent, false);
                var visual = CreateSpriteIcon("KeyMesh", key.transform, keySprite, 0.44f, Vector3.zero, 401);
                meshT = visual.transform;
                key.transform.position = start;
                key.transform.localScale = Vector3.one * scale;
                key.transform.rotation = Quaternion.identity;
            }
            else
            {
                key = CreateKey(vfxParent, fill, edge, "FlyingKey");
                key.transform.position = start;
                key.transform.localScale = Vector3.one * scale;
                key.transform.rotation = Quaternion.Euler(0, 0, 40f);
                meshT = key.transform.Find("KeyMesh");
            }

            var trail = key.AddComponent<TrailRenderer>();
            trail.sharedMaterial = BlockClearCelebration.SpriteMaterial;
            trail.time = 0.20f;
            trail.minVertexDistance = 0.02f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.14f * scale), new Keyframe(1f, 0f));
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(useSprite ? LockKeyYellowTint : Color.Lerp(edge, Color.white, 0.5f), 1f) },
                      new[] { new GradientAlphaKey(0.75f, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = g;
            trail.sortingOrder = 400;

            Vector3 hole = keyholeWorld();
            // Silver Key sprite'ı +45° eğik olduğundan z = -135° ile dişleri doğrudan deliğe bakar
            float arriveRotZ = useSprite ? (-135f - 360f) : (-90f - 360f);
            float insertScale = scale * 0.8f;
            Vector3 above = hole + new Vector3(0f, 0.26f * insertScale + 0.10f, -0.12f);
            Vector3 inside = hole + new Vector3(0f, 0.16f * insertScale, -0.12f);

            Sequence seq = DOTween.Sequence();
            seq.Append(key.transform.DOJump(above, 0.55f, 1, 0.42f).SetEase(Ease.InOutQuad));
            seq.Join(key.transform.DORotate(new Vector3(0, 0, arriveRotZ), 0.42f, RotateMode.FastBeyond360).SetEase(Ease.OutCubic));
            seq.Join(key.transform.DOScale(insertScale, 0.42f));
            seq.AppendCallback(() => { if (trail != null) trail.emitting = false; });
            seq.Append(key.transform.DOMove(inside, 0.10f).SetEase(Ease.InQuad));
            seq.AppendCallback(() => PuzzleSoundManager.Instance?.PlayBlockSnap());
            if (meshT != null)
            {
                Vector3 turnRot = useSprite ? new Vector3(0f, 70f, 0f) : new Vector3(85f, 0f, 0f);
                seq.Append(meshT.DOLocalRotate(turnRot, 0.16f).SetEase(Ease.OutBack));
            }
            seq.AppendInterval(0.04f);
            seq.AppendCallback(() => onTurned?.Invoke());
            seq.Append(key.transform.DOScale(0f, 0.16f).SetEase(Ease.InBack));
            seq.OnComplete(() => { if (key != null) UnityEngine.Object.Destroy(key); });
            seq.SetLink(key);
            return key;
        }

        /// <summary>
        /// Kilidin açılması (shader'da): halka kalkar, kilit büyüyüp kapakla birlikte kaybolur, plaka ve delikler görünür;
        /// bloğun renginde parıltı. Bittiğinde onDone çağrılır.
        /// </summary>
        public static void PlayUnlock(Material bodyMaterial, Vector3 lockWorldPos, Color celebrateColor, GameObject link, Action onDone)
        {
            if (bodyMaterial == null || !bodyMaterial.HasProperty(LockAmountId))
            {
                onDone?.Invoke();
                return;
            }
            Vector4 center = bodyMaterial.GetVector(LockCenterId);
            float baseScale = center.z > 0.01f ? center.z : 1f;

            Sequence seq = DOTween.Sequence();
            // 1) Halka yukarı kalkar
            seq.Append(DOTween.To(() => bodyMaterial.GetFloat(LockOpenId), v => bodyMaterial.SetFloat(LockOpenId, v), 1f, 0.14f).SetEase(Ease.OutBack));
            seq.AppendCallback(() =>
            {
                PuzzleSoundManager.Instance?.PlayUnlock();
                BlockClearCelebration.Play(lockWorldPos, celebrateColor, 0.75f);
            });
            // 2) Kilit biraz büyür, kapak açılır (plaka + delikler görünür)
            seq.Append(DOTween.To(() => baseScale, v =>
            {
                var c = bodyMaterial.GetVector(LockCenterId);
                c.z = v;
                bodyMaterial.SetVector(LockCenterId, c);
            }, baseScale * 1.25f, 0.30f).SetEase(Ease.OutQuad));
            seq.Join(DOTween.To(() => bodyMaterial.GetFloat(LockAmountId), v => bodyMaterial.SetFloat(LockAmountId, v), 0f, 0.30f).SetEase(Ease.InQuad));
            seq.OnComplete(() =>
            {
                if (bodyMaterial != null)
                {
                    bodyMaterial.SetFloat(LockOpenId, 0f);
                    var c = bodyMaterial.GetVector(LockCenterId);
                    c.z = baseScale;
                    bodyMaterial.SetVector(LockCenterId, c);
                }
                onDone?.Invoke();
            });
            if (link != null) seq.SetLink(link);
        }

        /// <summary>
        /// Kilidin açılması (GPU Instancing / MaterialPropertyBlock + Silver Lock sprite üzerinden):
        /// kilit yukarı sıçrar, büyüyüp kapakla birlikte kaybolur, plaka ve delikler görünür.
        /// </summary>
        public static void PlayUnlock(MonsterCharacter box, Vector3 lockWorldPos, Color celebrateColor, Action onDone, Transform lockVisual = null)
        {
            if (box == null)
            {
                onDone?.Invoke();
                return;
            }

            Vector4 center = box.LockCenterValue;
            float baseScale = center.z > 0.01f ? center.z : 1f;
            Vector3 restPos = lockVisual != null ? lockVisual.localPosition : Vector3.zero;
            Vector3 restScale = lockVisual != null ? lockVisual.localScale : Vector3.one;

            Sequence seq = DOTween.Sequence();
            seq.Append(DOTween.To(() => box.LockOpenValue, v => { if (box != null) box.SetLockOpen(v); }, 1f, 0.14f).SetEase(Ease.OutBack));
            if (lockVisual != null)
            {
                seq.Join(lockVisual.DOLocalMoveY(restPos.y + 0.075f, 0.14f).SetEase(Ease.OutBack));
                seq.Join(lockVisual.DOLocalRotate(new Vector3(0f, 0f, -10f), 0.14f).SetEase(Ease.OutBack));
                seq.Join(lockVisual.DOScale(restScale * 1.18f, 0.14f).SetEase(Ease.OutBack));
            }
            seq.AppendCallback(() =>
            {
                PuzzleSoundManager.Instance?.PlayUnlock();
                BlockClearCelebration.Play(lockWorldPos, celebrateColor, 0.75f);
            });
            seq.Append(DOTween.To(() => baseScale, v => { if (box != null) box.SetLockGlyphScale(v); }, baseScale * 1.25f, 0.30f).SetEase(Ease.OutQuad));
            seq.Join(DOTween.To(() => box.LockAmountValue, v => { if (box != null) box.SetLockAmount(v); }, 0f, 0.30f).SetEase(Ease.InQuad));
            if (lockVisual != null)
            {
                seq.Join(lockVisual.DOScale(Vector3.zero, 0.26f).SetEase(Ease.InBack));
            }
            seq.OnComplete(() =>
            {
                if (lockVisual != null)
                {
                    lockVisual.localPosition = restPos;
                    lockVisual.localRotation = Quaternion.identity;
                    lockVisual.localScale = restScale;
                }
                if (box != null)
                {
                    box.SetLockOpen(0f);
                    box.SetLockGlyphScale(baseScale);
                    box.SetLockAmount(0f);
                }
                onDone?.Invoke();
            });
            seq.SetLink(box.gameObject);
        }
    }
}
