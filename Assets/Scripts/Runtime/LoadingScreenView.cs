using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace PuzzleGlass
{
    /// <summary>
    /// Puzzle Glass loading ekranının görünümü (LevelTransitionController kurar ve ilerlemeyi besler):
    /// - Yumuşak lavanta degrade arka plan + logonun arkasında hafif ışık.
    /// - Kenarlarda yavaşça süzülen yarı saydam cam objeler (küp, halka, kabarcık) ve oyunun renkli topları.
    ///   Orta alan boş kalır. Sprite'lar bir kez kodla üretilir (yeni asset/bağımlılık yok).
    /// - Katmanlı derinlik + beyaz kontur + parlak degradeli "PUZZLE / GLASS" logosu, girişte pop.
    /// - Tam yuvarlak kapsül bar: beyaz zemin, mor degrade dolgu, üst parlaklık ve kayan ışıltı; %100'de küçük pop.
    /// İlerleme değeri dışarıdan gelir (gerçek yükleme adımları); bu sınıf sadece gösterir.
    /// </summary>
    public class LoadingScreenView : MonoBehaviour
    {
        public CanvasGroup Group { get; private set; }

        // Renkler (oyunun lavanta/indigo paleti)
        private static readonly Color BgTop = new Color(0.80f, 0.76f, 0.99f, 1f);
        private static readonly Color BgMid = new Color(0.93f, 0.91f, 1.00f, 1f);
        private static readonly Color BgBottom = new Color(0.76f, 0.71f, 0.97f, 1f);
        private static readonly Color LogoTop = new Color(0.60f, 0.45f, 1.00f, 1f);
        private static readonly Color LogoBottom = new Color(0.32f, 0.19f, 0.84f, 1f);
        private static readonly Color LogoDepth = new Color(0.19f, 0.10f, 0.47f, 1f);
        private static readonly Color FillLeft = new Color(0.72f, 0.58f, 1.00f, 1f);
        private static readonly Color FillRight = new Color(0.46f, 0.30f, 0.96f, 1f);
        private static readonly Color LabelColor = new Color(0.44f, 0.37f, 0.80f, 1f);
        private static readonly Color PercentColor = new Color(0.29f, 0.18f, 0.72f, 1f);

        private const float BarW = 660f;
        private const float BarH = 60f;
        private const float BarPad = 8f;

        private struct Floater
        {
            public RectTransform rt;
            public RectTransform shadow;
            public Vector2 basePos;
            public Vector2 shadowOffset;
            public float amp, freq, phase, rotSpeed, baseRot, tilt;
        }

        private Floater[] _floaters;
        private RectTransform _logo;
        private CanvasGroup _logoGroup;
        private RectTransform _barRoot;
        private RectTransform _fillRect;
        private RectTransform _shine;
        private Image _fillGloss;
        private RectTransform _percentRect;
        private Text _percentText;
        private RectTransform _completeRing;
        private Image _completeRingImg;
        private float _fillFullWidth;
        private int _shownPercent = -1;
        private float _time;
        private float _shineT;

        private static string[] s_PercentStrings;
        private static Sprite s_Bg, s_Glow, s_Shadow, s_Cube, s_Ring, s_Bubble, s_Ball, s_ShineBand;

        // ------------------------------------------------------------------ kurulum

        public static LoadingScreenView Create(Transform parent, Font font, string title, Sprite pill)
        {
            EnsureSprites();
            if (s_PercentStrings == null)
            {
                s_PercentStrings = new string[101];
                for (int i = 0; i <= 100; i++) s_PercentStrings[i] = i + "%";
            }

            var go = new GameObject("LoadingScreen", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var root = (RectTransform)go.transform;
            Stretch(root);
            var view = go.AddComponent<LoadingScreenView>();
            view.Group = go.AddComponent<CanvasGroup>();
            view.Group.alpha = 0f;

            // Arka plan: dikey lavanta degrade (dokunuşları da yakalar: perde arkasındaki oyun tıklanmasın)
            var bg = NewImage("Background", root, s_Bg, Color.white);
            Stretch(bg.rectTransform);
            bg.raycastTarget = true;

            // Logonun arkasında yumuşak ışık
            var glow = NewImage("LogoGlow", root, s_Glow, new Color(1f, 1f, 1f, 0.75f));
            Anchor(glow.rectTransform, new Vector2(0.5f, 0.63f), new Vector2(1250f, 1000f), Vector2.zero);

            view.BuildDecor(root);
            view.BuildLogo(root, font, title);
            view.BuildBar(root, font, pill);
            return view;
        }

        private void BuildDecor(RectTransform root)
        {
            // Hareketli kenar objeleri ayrı bir alt kanvasta: her kare sadece bu katman yeniden çizilir
            var decorGo = new GameObject("Decor", typeof(RectTransform));
            decorGo.transform.SetParent(root, false);
            var decor = (RectTransform)decorGo.transform;
            Stretch(decor);
            decorGo.AddComponent<Canvas>();

            Color lav = new Color(0.86f, 0.82f, 1.00f, 1f);
            Color pink = new Color(1.00f, 0.84f, 0.96f, 1f);
            Color sky = new Color(0.80f, 0.90f, 1.00f, 1f);

            _floaters = new[]
            {
                // sol üst
                MakeFloater(decor, s_Cube, lav, new Vector2(0f, 1f), new Vector2(135f, -255f), 230f, 14f, 0.22f, 0.0f, 7f, 16f, true),
                MakeFloater(decor, s_Ball, PuzzleColorPalette.GetPrimaryColor(PuzzleColor.Yellow), new Vector2(0f, 1f), new Vector2(300f, -125f), 64f, 9f, 0.31f, 1.3f, 0f, 0f, true),
                // sağ üst
                MakeFloater(decor, s_Ring, sky, new Vector2(1f, 1f), new Vector2(-140f, -330f), 190f, 12f, 0.26f, 2.1f, -9f, 0f, true),
                MakeFloater(decor, s_Ball, PuzzleColorPalette.GetPrimaryColor(PuzzleColor.Blue), new Vector2(1f, 1f), new Vector2(-300f, -170f), 78f, 10f, 0.34f, 0.6f, 0f, 0f, true),
                // orta kenarlar (küçük, orta alana girmez)
                MakeFloater(decor, s_Bubble, Color.white, new Vector2(0f, 0.5f), new Vector2(72f, 120f), 120f, 16f, 0.19f, 3.0f, 0f, 0f, false),
                MakeFloater(decor, s_Cube, pink, new Vector2(1f, 0.5f), new Vector2(-82f, -20f), 120f, 13f, 0.24f, 4.2f, -6f, -12f, true),
                // sol alt
                MakeFloater(decor, s_Ring, pink, new Vector2(0f, 0f), new Vector2(165f, 340f), 165f, 12f, 0.21f, 5.0f, 8f, 0f, true),
                MakeFloater(decor, s_Ball, PuzzleColorPalette.GetPrimaryColor(PuzzleColor.Green), new Vector2(0f, 0f), new Vector2(330f, 210f), 58f, 9f, 0.37f, 2.6f, 0f, 0f, true),
                // sağ alt
                MakeFloater(decor, s_Cube, sky, new Vector2(1f, 0f), new Vector2(-160f, 290f), 205f, 15f, 0.2f, 1.7f, -5f, 24f, true),
                MakeFloater(decor, s_Ball, PuzzleColorPalette.GetPrimaryColor(PuzzleColor.Red), new Vector2(1f, 0f), new Vector2(-345f, 165f), 80f, 10f, 0.29f, 3.6f, 0f, 0f, true),
                MakeFloater(decor, s_Bubble, Color.white, new Vector2(1f, 0f), new Vector2(-95f, 520f), 70f, 11f, 0.33f, 0.9f, 0f, 0f, false),
            };
        }

        private static Floater MakeFloater(RectTransform parent, Sprite sprite, Color tint, Vector2 anchor, Vector2 pos,
            float size, float amp, float freq, float phase, float rotSpeed, float baseRot, bool withShadow)
        {
            var f = new Floater
            {
                basePos = pos,
                amp = amp,
                freq = freq,
                phase = phase,
                rotSpeed = rotSpeed,
                baseRot = baseRot,
                tilt = rotSpeed == 0f ? 6f : 0f,
                shadowOffset = new Vector2(size * 0.06f, -size * 0.12f)
            };
            if (withShadow)
            {
                var sh = NewImage("Shadow", parent, s_Shadow, new Color(0.36f, 0.27f, 0.74f, 0.20f));
                Anchor(sh.rectTransform, anchor, new Vector2(size * 1.05f, size * 0.55f), pos + f.shadowOffset);
                f.shadow = sh.rectTransform;
            }
            var img = NewImage(sprite.name, parent, sprite, tint);
            Anchor(img.rectTransform, anchor, new Vector2(size, size), pos);
            img.rectTransform.localRotation = Quaternion.Euler(0f, 0f, baseRot);
            f.rt = img.rectTransform;

            if (sprite == s_Ball)
            {
                // Topun parlak noktası ayrı (tint'ten etkilenmesin, oyundaki toplar gibi beyaz parlasın)
                var hi = NewImage("Highlight", img.rectTransform, s_Glow, new Color(1f, 1f, 1f, 0.85f));
                Anchor(hi.rectTransform, new Vector2(0.36f, 0.68f), new Vector2(size * 0.42f, size * 0.32f), Vector2.zero);
                hi.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 35f);
            }
            return f;
        }

        private void BuildLogo(RectTransform root, Font font, string title)
        {
            var logoGo = new GameObject("Logo", typeof(RectTransform));
            logoGo.transform.SetParent(root, false);
            _logo = (RectTransform)logoGo.transform;
            Anchor(_logo, new Vector2(0.5f, 0.63f), new Vector2(1000f, 440f), Vector2.zero);
            logoGo.AddComponent<Canvas>(); // girişte ve boşta hafif hareket ediyor: ayrı alt kanvas
            _logoGroup = logoGo.AddComponent<CanvasGroup>();

            string upper = string.IsNullOrEmpty(title) ? "PUZZLE GLASS" : title.ToUpperInvariant();
            int split = upper.LastIndexOf(' ');
            string line1 = split > 0 ? upper.Substring(0, split) : upper;
            string line2 = split > 0 ? upper.Substring(split + 1) : "";

            if (line2.Length > 0)
            {
                BuildLogoLine(_logo, font, line1, 148, new Vector2(0f, 88f));
                BuildLogoLine(_logo, font, line2, 178, new Vector2(0f, -84f));
            }
            else
            {
                BuildLogoLine(_logo, font, line1, 150, Vector2.zero);
            }
        }

        private static void BuildLogoLine(RectTransform parent, Font font, string text, int size, Vector2 pos)
        {
            var box = new Vector2(1000f, size * 1.35f);

            // 1) Derinlik: koyu indigo katmanlar aşağı doğru (3B kalınlık) + tamamını saran beyaz kontur
            var depth = NewText("Depth", parent, font, text, size, LogoDepth);
            Anchor(depth.rectTransform, new Vector2(0.5f, 0.5f), box, pos);
            var d1 = depth.gameObject.AddComponent<Shadow>();
            d1.effectColor = LogoDepth;
            d1.effectDistance = new Vector2(0f, -7f);
            var d2 = depth.gameObject.AddComponent<Shadow>();
            d2.effectColor = LogoDepth;
            d2.effectDistance = new Vector2(0f, -14f);
            var ol = depth.gameObject.AddComponent<Outline>();
            ol.effectColor = Color.white;
            ol.effectDistance = new Vector2(7f, -7f);
            var soft = depth.gameObject.AddComponent<Shadow>();
            soft.effectColor = new Color(0.30f, 0.20f, 0.70f, 0.22f);
            soft.effectDistance = new Vector2(0f, -26f);

            // 2) Yüz: açık mordan koyu indigoya dikey degrade
            var face = NewText("Face", parent, font, text, size, Color.white);
            Anchor(face.rectTransform, new Vector2(0.5f, 0.5f), box, pos);
            face.gameObject.AddComponent<UIVertexGradient>().Set(LogoTop, LogoBottom, false);

            // 3) Parlaklık: üstte beyaz, ortaya doğru kaybolan cam parıltısı
            var gloss = NewText("Gloss", parent, font, text, size, Color.white);
            Anchor(gloss.rectTransform, new Vector2(0.5f, 0.5f), box, pos);
            gloss.gameObject.AddComponent<UIVertexGradient>().Set(new Color(1f, 1f, 1f, 0.55f), new Color(1f, 1f, 1f, 0f), false);
        }

        private void BuildBar(RectTransform root, Font font, Sprite pill)
        {
            var barGo = new GameObject("LoadingBar", typeof(RectTransform));
            barGo.transform.SetParent(root, false);
            _barRoot = (RectTransform)barGo.transform;
            Anchor(_barRoot, new Vector2(0.5f, 0.405f), new Vector2(BarW, BarH), Vector2.zero);

            // Üst satır: solda "LOADING", sağda gerçek yüzde
            var label = NewText("Label", _barRoot, font, "LOADING", 30, LabelColor);
            label.alignment = TextAnchor.MiddleLeft;
            Anchor(label.rectTransform, new Vector2(0f, 1f), new Vector2(360f, 50f), new Vector2(190f, 42f));
            _percentText = NewText("Percent", _barRoot, font, "0%", 46, PercentColor);
            _percentText.alignment = TextAnchor.MiddleRight;
            _percentRect = _percentText.rectTransform;
            _percentRect.pivot = new Vector2(1f, 0.5f);
            Anchor(_percentRect, new Vector2(1f, 1f), new Vector2(240f, 60f), new Vector2(-12f, 44f));

            // Yumuşak gölge + ince lavanta kenar + beyaz kapsül zemin (pill sprite yüksekliğe göre tam yuvarlak)
            var shadow = NewPill("Shadow", _barRoot, pill, new Color(0.42f, 0.33f, 0.86f, 0.22f), BarH + 14f);
            Anchor(shadow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(BarW + 14f, BarH + 14f), new Vector2(0f, -9f));
            var rim = NewPill("Rim", _barRoot, pill, new Color(0.80f, 0.76f, 0.98f, 1f), BarH + 6f);
            Anchor(rim.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(BarW + 6f, BarH + 6f), Vector2.zero);
            var track = NewPill("Track", _barRoot, pill, Color.white, BarH);
            Anchor(track.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(BarW, BarH), Vector2.zero);

            // Dolgu: mor degrade kapsül, kendi şekliyle maskeli (parlaklık ve ışıltı uçlardan taşmaz)
            float fillH = BarH - BarPad * 2f;
            _fillFullWidth = BarW - BarPad * 2f;
            var fill = NewPill("Fill", track.rectTransform, pill, Color.white, fillH);
            _fillRect = fill.rectTransform;
            _fillRect.anchorMin = _fillRect.anchorMax = new Vector2(0f, 0.5f);
            _fillRect.pivot = new Vector2(0f, 0.5f);
            _fillRect.anchoredPosition = new Vector2(BarPad, 0f);
            _fillRect.sizeDelta = new Vector2(fillH, fillH);
            fill.gameObject.AddComponent<UIVertexGradient>().Set(FillLeft, FillRight, true);
            fill.gameObject.AddComponent<Mask>().showMaskGraphic = true;

            _fillGloss = NewPill("Gloss", _fillRect, pill, new Color(1f, 1f, 1f, 0.34f), fillH * 0.42f);
            var gl = _fillGloss.rectTransform;
            gl.anchorMin = new Vector2(0f, 0.52f);
            gl.anchorMax = new Vector2(1f, 0.92f);
            gl.offsetMin = new Vector2(8f, 0f);
            gl.offsetMax = new Vector2(-8f, 0f);

            var shine = NewImage("Shine", _fillRect, s_ShineBand, new Color(1f, 1f, 1f, 0.55f));
            _shine = shine.rectTransform;
            _shine.anchorMin = new Vector2(0f, 0f);
            _shine.anchorMax = new Vector2(0f, 1f);
            _shine.pivot = new Vector2(0.5f, 0.5f);
            _shine.sizeDelta = new Vector2(70f, 0f);

            // %100 olunca bar ucunda açılan halka
            _completeRingImg = NewImage("CompleteRing", _barRoot, s_Ring, new Color(1f, 1f, 1f, 0f));
            _completeRing = _completeRingImg.rectTransform;
            Anchor(_completeRing, new Vector2(1f, 0.5f), new Vector2(120f, 120f), new Vector2(-BarPad - 16f, 0f));
        }

        // ------------------------------------------------------------------ dış API

        /// <summary>Ekran gösterilirken: bar sıfırlanır, logo pop ile girer.</summary>
        public void PlayIntro()
        {
            _time = 0f;
            _shineT = 0f;
            _shownPercent = -1;
            SetProgress(0f);
            _barRoot.DOKill(true);
            _barRoot.localScale = Vector3.one;
            _logo.DOKill(true);
            _logoGroup.DOKill(true);
            _logo.localScale = Vector3.one * 0.72f;
            _logoGroup.alpha = 0f;
            _logo.DOScale(1f, 0.45f).SetEase(Ease.OutBack, 1.7f);
            _logoGroup.DOFade(1f, 0.25f);
            _completeRingImg.color = new Color(1f, 1f, 1f, 0f);
        }

        /// <summary>Gösterilen ilerleme (0..1). Değer dışarıdaki gerçek yükleme adımlarından gelir.</summary>
        public void SetProgress(float p)
        {
            p = Mathf.Clamp01(p);
            float minW = BarH - BarPad * 2f; // kapsül en az bir daire kadar
            _fillRect.sizeDelta = new Vector2(Mathf.Lerp(minW, _fillFullWidth, p), _fillRect.sizeDelta.y);
            int pct = Mathf.RoundToInt(p * 100f);
            if (pct != _shownPercent)
            {
                _shownPercent = pct;
                _percentText.text = s_PercentStrings[pct];
            }
        }

        /// <summary>%100 anı: bar hafif zıplar, yüzde pop yapar, dolgu parlar, uçta halka açılır.</summary>
        public IEnumerator PlayComplete()
        {
            _barRoot.DOKill(true);
            _barRoot.DOPunchScale(new Vector3(0.06f, 0.10f, 0f), 0.3f, 5, 0.6f);
            _percentRect.DOKill(true);
            _percentRect.DOPunchScale(Vector3.one * 0.28f, 0.3f, 5, 0.6f);
            _fillGloss.DOKill(true);
            _fillGloss.color = new Color(1f, 1f, 1f, 0.9f);
            _fillGloss.DOFade(0.34f, 0.35f);
            _completeRing.DOKill(true);
            _completeRingImg.DOKill(true);
            _completeRing.localScale = Vector3.one * 0.3f;
            _completeRingImg.color = new Color(1f, 1f, 1f, 0.9f);
            _completeRing.DOScale(1.5f, 0.35f).SetEase(Ease.OutCubic);
            _completeRingImg.DOFade(0f, 0.35f).SetEase(Ease.InQuad);
            yield return new WaitForSeconds(0.28f);
        }

        /// <summary>Perde kalkarken logo hafifçe büyüyerek uzaklaşır.</summary>
        public void PlayOutro(float duration)
        {
            _logo.DOKill(true);
            _logo.DOScale(1.08f, duration).SetEase(Ease.OutQuad);
        }

        // ------------------------------------------------------------------ boşta animasyon

        private void Update()
        {
            if (Group == null || Group.alpha <= 0.001f) return;
            float dt = Time.unscaledDeltaTime;
            _time += dt;

            for (int i = 0; i < _floaters.Length; i++)
            {
                ref Floater f = ref _floaters[i];
                float w = _time * f.freq * Mathf.PI * 2f + f.phase;
                Vector2 off = new Vector2(Mathf.Sin(w * 0.7f) * f.amp * 0.45f, Mathf.Sin(w) * f.amp);
                f.rt.anchoredPosition = f.basePos + off;
                float rot = f.baseRot + f.rotSpeed * _time + Mathf.Sin(w * 0.8f) * f.tilt;
                f.rt.localRotation = Quaternion.Euler(0f, 0f, rot);
                if (f.shadow != null)
                {
                    // Gölge objeyle birlikte kayar, yükseldikçe hafif küçülür
                    f.shadow.anchoredPosition = f.basePos + f.shadowOffset + new Vector2(off.x, off.y * 0.35f);
                    float s = 1f - (off.y / Mathf.Max(1f, f.amp)) * 0.08f;
                    f.shadow.localScale = new Vector3(s, s, 1f);
                }
            }

            // Logo boşta çok hafif nefes alır
            if (!DOTween.IsTweening(_logo))
            {
                float b = 1f + Mathf.Sin(_time * 1.7f) * 0.012f;
                _logo.localScale = new Vector3(b, b, 1f);
            }

            // Dolgu üzerinde kayan ışıltı
            _shineT += dt / 1.5f;
            if (_shineT > 1f) _shineT -= 1f;
            float fw = _fillRect.sizeDelta.x;
            _shine.anchoredPosition = new Vector2(Mathf.Lerp(-60f, fw + 60f, _shineT), 0f);
        }

        // ------------------------------------------------------------------ UI yardımcıları

        private static Image NewImage(string name, Transform parent, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Kapsül: pill sprite sliced, köşe yarıçapı tam yüksekliğin yarısı olacak şekilde ölçeklenir.</summary>
        private static Image NewPill(string name, Transform parent, Sprite pill, Color color, float height)
        {
            var img = NewImage(name, parent, pill, color);
            img.type = Image.Type.Sliced;
            float border = pill != null ? pill.border.x : 22f;
            img.pixelsPerUnitMultiplier = Mathf.Max(0.05f, border / Mathf.Max(1f, height * 0.5f));
            return img;
        }

        private static Text NewText(string name, Transform parent, Font font, string text, int size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = FontStyle.Bold;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void Anchor(RectTransform rt, Vector2 anchor, Vector2 size, Vector2 pos)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
        }

        // ------------------------------------------------------------------ kodla üretilen sprite'lar (bir kez)

        private static void EnsureSprites()
        {
            if (s_Bg != null) return;
            s_Bg = MakeBackground();
            s_Glow = MakeSprite("Glow", 128, (x, y) =>
            {
                float r = Mathf.Sqrt(x * x + y * y);
                float a = Mathf.Clamp01(1f - r);
                return new Color(1f, 1f, 1f, a * a);
            });
            s_Shadow = MakeSprite("Shadow", 128, (x, y) =>
            {
                float r2 = x * x + y * y;
                return new Color(1f, 1f, 1f, Mathf.Exp(-r2 * 4.5f) * (r2 < 1f ? 1f : 0f));
            });
            s_Cube = MakeSprite("GlassCube", 256, GlassCube);
            s_Ring = MakeSprite("GlassRing", 256, GlassRing);
            s_Bubble = MakeSprite("GlassBubble", 256, GlassBubble);
            s_Ball = MakeSprite("Ball", 128, Ball);
            s_ShineBand = MakeSprite("ShineBand", 64, (x, y) =>
            {
                float d = (x - y * 0.45f);
                float a = Mathf.Exp(-d * d * 6f);
                return new Color(1f, 1f, 1f, a);
            });
        }

        private static Sprite MakeBackground()
        {
            const int h = 256;
            var tex = new Texture2D(2, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "LoadingBg" };
            var px = new Color32[2 * h];
            for (int y = 0; y < h; y++)
            {
                float t = y / (float)(h - 1); // 0 alt, 1 üst
                Color c = t > 0.55f ? Color.Lerp(BgMid, BgTop, Mathf.SmoothStep(0f, 1f, (t - 0.55f) / 0.45f))
                                    : Color.Lerp(BgBottom, BgMid, Mathf.SmoothStep(0f, 1f, t / 0.55f));
                px[y * 2] = px[y * 2 + 1] = c;
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, 2, h), new Vector2(0.5f, 0.5f), 100f);
        }

        private delegate Color PixelFn(float x, float y);

        /// <summary>x, y: -1..1 (merkez 0). Kenar yumuşatması fonksiyonlarda yapılır.</summary>
        private static Sprite MakeSprite(string name, int size, PixelFn fn)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = name };
            var px = new Color32[size * size];
            for (int j = 0; j < size; j++)
            {
                float y = (j + 0.5f) / size * 2f - 1f;
                for (int i = 0; i < size; i++)
                {
                    float x = (i + 0.5f) / size * 2f - 1f;
                    px[j * size + i] = fn(x, y);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            var s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            s.name = name;
            return s;
        }

        private static float SdRoundBox(float x, float y, float half, float r)
        {
            float qx = Mathf.Abs(x) - half + r, qy = Mathf.Abs(y) - half + r;
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Min(Mathf.Max(qx, qy), 0f) + Mathf.Sqrt(ox * ox + oy * oy) - r;
        }

        private const float Px = 2f / 256f; // 256'lık sprite'ta bir pikselin -1..1 uzayındaki boyu

        /// <summary>Yuvarlak köşeli cam küp: yarı saydam gövde, sol üstte parlak kalın kenar, çapraz yansıma çizgisi.</summary>
        private static Color GlassCube(float x, float y)
        {
            float d = SdRoundBox(x, y, 0.86f, 0.26f);
            float inside = Mathf.Clamp01(0.5f - d / (Px * 1.5f));
            if (inside <= 0f) return new Color(1f, 1f, 1f, 0f);
            float depth = -d;                                   // kenardan içeri
            float lit = Mathf.Clamp01(0.5f + (-x + y) * 0.35f); // sol üst aydınlık
            float a = Mathf.Lerp(0.16f, 0.32f, lit);
            float rim = Mathf.Exp(-depth / 0.035f);
            float innerLine = Mathf.Exp(-Mathf.Pow((depth - 0.10f) / 0.02f, 2f));
            float streak = Mathf.Exp(-Mathf.Pow(((x + y) * 0.7071f + 0.32f) / 0.07f, 2f)) * Mathf.Clamp01(1f - (depth < 0.12f ? 1f : 0f));
            float spot = Mathf.Exp(-((x + 0.5f) * (x + 0.5f) + (y - 0.52f) * (y - 0.52f)) / 0.006f);
            float rgbDark = 1f - innerLine * 0.25f;
            a += rim * (0.45f + 0.45f * lit) + streak * 0.42f + spot * 0.6f + innerLine * 0.12f;
            float white = Mathf.Clamp01(rim * lit + streak + spot);
            float c = Mathf.Lerp(0.92f * rgbDark, 1f, white);
            return new Color(c, c, c, Mathf.Clamp01(a) * inside);
        }

        /// <summary>Cam halka (simit): tüp kesiti boyunca gölgeli, sol üst kenarı parlak.</summary>
        private static Color GlassRing(float x, float y)
        {
            float r = Mathf.Sqrt(x * x + y * y);
            const float rIn = 0.52f, rOut = 0.94f;
            float edge = Mathf.Min(r - rIn, rOut - r);
            float inside = Mathf.Clamp01(0.5f + edge / (Px * 1.5f));
            if (inside <= 0f) return new Color(1f, 1f, 1f, 0f);
            float t = Mathf.Clamp01((r - rIn) / (rOut - rIn));     // 0 iç, 1 dış
            float tube = Mathf.Sin(t * Mathf.PI);                    // tüpün ortası kalın
            float lit = Mathf.Clamp01(0.5f + (-x + y) / Mathf.Max(0.01f, r) * 0.5f);
            float rimO = Mathf.Exp(-Mathf.Pow((t - 0.88f) / 0.06f, 2f));
            float rimI = Mathf.Exp(-Mathf.Pow((t - 0.12f) / 0.06f, 2f));
            float spec = Mathf.Exp(-Mathf.Pow((t - 0.62f) / 0.08f, 2f)) * Mathf.Pow(lit, 3f);
            float a = 0.14f + (1f - tube) * 0.22f + rimO * (0.35f + 0.4f * lit) + rimI * 0.25f + spec * 0.7f;
            float white = Mathf.Clamp01(rimO * lit + spec * 1.2f);
            float c = Mathf.Lerp(0.9f, 1f, white);
            return new Color(c, c, c, Mathf.Clamp01(a) * inside);
        }

        /// <summary>Cam kabarcık: şeffaf merkez, kenarda Fresnel parlaması, sol üstte yansıma lekesi.</summary>
        private static Color GlassBubble(float x, float y)
        {
            float r = Mathf.Sqrt(x * x + y * y);
            float inside = Mathf.Clamp01(0.5f + (0.93f - r) / (Px * 1.5f));
            if (inside <= 0f) return new Color(1f, 1f, 1f, 0f);
            float fres = Mathf.Pow(Mathf.Clamp01(r / 0.93f), 4f);
            float spot = Mathf.Exp(-(((x + 0.36f) * (x + 0.36f)) / 0.03f + ((y - 0.42f) * (y - 0.42f)) / 0.012f));
            float spot2 = Mathf.Exp(-((x - 0.42f) * (x - 0.42f) + (y + 0.46f) * (y + 0.46f)) / 0.01f);
            float a = 0.10f + fres * 0.62f + spot * 0.8f + spot2 * 0.3f;
            return new Color(1f, 1f, 1f, Mathf.Clamp01(a) * inside);
        }

        /// <summary>Parlak top (tint ile oyunun top rengini alır): yuvarlak aydınlatma + alt kenarda koyulaşma.</summary>
        private static Color Ball(float x, float y)
        {
            float r2 = x * x + y * y;
            float r = Mathf.Sqrt(r2);
            float inside = Mathf.Clamp01(0.5f + (0.94f - r) / (2f / 128f * 1.5f));
            if (inside <= 0f) return new Color(1f, 1f, 1f, 0f);
            float nz = Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Min(1f, r2 / (0.94f * 0.94f))));
            Vector3 n = new Vector3(x / 0.94f, y / 0.94f, nz);
            Vector3 l = new Vector3(-0.45f, 0.6f, 0.66f).normalized;
            float diff = Mathf.Clamp01(Vector3.Dot(n, l));
            float c = Mathf.Lerp(0.55f, 1f, diff);
            c *= 1f - Mathf.Pow(Mathf.Clamp01(r / 0.94f), 6f) * 0.25f; // kenar koyulaşması
            return new Color(c, c, c, inside);
        }
    }
}
