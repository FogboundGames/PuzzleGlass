using UnityEngine;
using DG.Tweening;

namespace PuzzleGlass
{
    /// <summary>
    /// 2'li bağlı kutuların arasındaki iki renkli 3D köprü çubuğunu çizer ve iki kutunun
    /// canlı dünya konumunu takip eder (ızgarada yatay/dikey, slota uçuşta yumuşak dönüş,
    /// yan yana slotlarda yatay bağlantı ve birlikte tamamlanma animasyonu).
    /// </summary>
    [ExecuteAlways]
    public class LinkedBoxConnector : MonoBehaviour
    {
        private static readonly int ColorAPropId = Shader.PropertyToID("_ColorA");
        private static readonly int DarkColorAPropId = Shader.PropertyToID("_DarkColorA");
        private static readonly int HighlightColorAPropId = Shader.PropertyToID("_HighlightColorA");
        private static readonly int ColorBPropId = Shader.PropertyToID("_ColorB");
        private static readonly int DarkColorBPropId = Shader.PropertyToID("_DarkColorB");
        private static readonly int HighlightColorBPropId = Shader.PropertyToID("_HighlightColorB");
        private static readonly int QuadSizePropId = Shader.PropertyToID("_QuadSize");
        private static readonly int BarHalfSizePropId = Shader.PropertyToID("_BarHalfSize");
        private static readonly int DownDirLocalPropId = Shader.PropertyToID("_DownDirLocal");
        private static readonly int ExitHighlightPropId = Shader.PropertyToID("_ExitHighlight");
        private static readonly int IceAPropId = Shader.PropertyToID("_IceA");
        private static readonly int IceBPropId = Shader.PropertyToID("_IceB");
        private static readonly int FlashAmountPropId = Shader.PropertyToID("_FlashAmount");
        private static readonly int WaitingSidePropId = Shader.PropertyToID("_WaitingSide");

        private static Material s_SharedBridgeMaterial;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCaches()
        {
            s_SharedBridgeMaterial = null;
        }

        public MonsterCharacter boxA;
        public MonsterCharacter boxB;

        private MeshRenderer _renderer;
        private MaterialPropertyBlock _mpb;
        private bool _completing;
        private float _completeScaleMul = 1f;
        private float _completeFlash = 0f;

        private bool _hasLastSync;
        private Vector3 _lastPosA;
        private Vector3 _lastPosB;
        private float _lastScaleA;
        private float _lastScaleB;
        private PuzzleColor _lastColorA;
        private PuzzleColor _lastColorB;
        private bool _lastPlayable;
        private bool _lastIceA;
        private bool _lastIceB;
        private float _lastWaitingSide;
        private float _lastFlash;
        private float _lastCompleteScaleMul;

        public static LinkedBoxConnector Create(Transform parent, MonsterCharacter a, MonsterCharacter b)
        {
            if (a == null || b == null) return null;

            var go = new GameObject($"LinkedBoxBridge_{a.linkGroupId}_{a.colorType}_{b.colorType}");
            if (parent != null) go.transform.SetParent(parent, false);

            var conn = go.AddComponent<LinkedBoxConnector>();
            conn.Initialize(a, b);
            return conn;
        }

        private static Material GetSharedMaterial()
        {
            if (s_SharedBridgeMaterial != null) return s_SharedBridgeMaterial;

            Shader shader = Shader.Find("PuzzleGlass/LinkedBoxBridge")
                ?? Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Sprites/Default");

            s_SharedBridgeMaterial = new Material(shader)
            {
                name = "Mat_LinkedBoxBridge_Shared",
                renderQueue = 2997
            };
            return s_SharedBridgeMaterial;
        }

        public void Initialize(MonsterCharacter a, MonsterCharacter b)
        {
            boxA = a;
            boxB = b;
            _completing = false;
            _completeScaleMul = 1f;
            _completeFlash = 0f;
            _hasLastSync = false;

            EnsureVisualQuad();
            SyncGeometryAndVisuals(force: true);
        }

        private void EnsureVisualQuad()
        {
            if (_renderer != null) return;

            Transform existing = transform.Find("BridgeQuad");
            GameObject quad;
            if (existing != null)
            {
                quad = existing.gameObject;
            }
            else
            {
                quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "BridgeQuad";
                quad.transform.SetParent(transform, false);
                quad.transform.localPosition = Vector3.zero;
                quad.transform.localRotation = Quaternion.identity;
                var col = quad.GetComponent<Collider>();
                if (col != null)
                {
                    if (Application.isPlaying) Destroy(col);
                    else DestroyImmediate(col);
                }
            }

            _renderer = quad.GetComponent<MeshRenderer>();
            if (_renderer != null)
            {
                _renderer.sharedMaterial = GetSharedMaterial();
                _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _renderer.receiveShadows = false;
            }

            if (_mpb == null) _mpb = new MaterialPropertyBlock();
        }

        private void LateUpdate()
        {
            if (boxA == null || boxB == null)
            {
                if (Application.isPlaying) Destroy(gameObject);
                else DestroyImmediate(gameObject);
                return;
            }

            if (!boxA.gameObject.activeInHierarchy || !boxB.gameObject.activeInHierarchy)
            {
                if (_renderer != null && _renderer.enabled) _renderer.enabled = false;
                return;
            }

            if (_renderer != null && !_renderer.enabled) _renderer.enabled = true;
            SyncGeometryAndVisuals(force: false);
        }

        public void SyncGeometryAndVisuals(bool force = true)
        {
            if (boxA == null || boxB == null) return;
            EnsureVisualQuad();
            if (_renderer == null) return;

            Transform tA = boxA.bodyPivot != null ? boxA.bodyPivot : boxA.transform;
            Transform tB = boxB.bodyPivot != null ? boxB.bodyPivot : boxB.transform;

            Vector3 pA = tA.position;
            Vector3 pB = tB.position;
            float scaleA = Mathf.Abs(tA.lossyScale.x);
            float scaleB = Mathf.Abs(tB.lossyScale.x);

            bool playable = (boxA.isExitClearInGrid || boxA.assignedSlot != null || boxA.IsMovingToSlot)
                         && (boxB.isExitClearInGrid || boxB.assignedSlot != null || boxB.IsMovingToSlot)
                         && !boxA.IsBlockedIncludingLink;
            bool iceA = boxA.IsFrozenByIce;
            bool iceB = boxB.IsFrozenByIce;

            float waitingSide = 0f;
            if (!_completing)
            {
                if (boxA.IsWaitingForLinkedPartner) waitingSide = -1f;
                else if (boxB.IsWaitingForLinkedPartner) waitingSide = 1f;
            }

            float flash = _completing ? _completeFlash : Mathf.Max(boxA.CurrentDragHighlight, boxB.CurrentDragHighlight);
            PuzzleColor colA = boxA.colorType;
            PuzzleColor colB = boxB.colorType;

            if (!force && _hasLastSync
                && (pA - _lastPosA).sqrMagnitude < 1e-8f
                && (pB - _lastPosB).sqrMagnitude < 1e-8f
                && Mathf.Abs(scaleA - _lastScaleA) < 1e-4f
                && Mathf.Abs(scaleB - _lastScaleB) < 1e-4f
                && colA == _lastColorA
                && colB == _lastColorB
                && playable == _lastPlayable
                && iceA == _lastIceA
                && iceB == _lastIceB
                && Mathf.Abs(waitingSide - _lastWaitingSide) < 1e-4f
                && Mathf.Abs(flash - _lastFlash) < 1e-4f
                && Mathf.Abs(_completeScaleMul - _lastCompleteScaleMul) < 1e-4f)
            {
                return;
            }

            _hasLastSync = true;
            _lastPosA = pA;
            _lastPosB = pB;
            _lastScaleA = scaleA;
            _lastScaleB = scaleB;
            _lastColorA = colA;
            _lastColorB = colB;
            _lastPlayable = playable;
            _lastIceA = iceA;
            _lastIceB = iceB;
            _lastWaitingSide = waitingSide;
            _lastFlash = flash;
            _lastCompleteScaleMul = _completeScaleMul;

            float avgScale = Mathf.Max(0.12f, 0.5f * (scaleA + scaleB));

            // CarrierBox front face center is biased slightly upward by VerticalFrontBias (+0.024 local)
            Vector2 cA = new Vector2(pA.x, pA.y + 0.024f * scaleA);
            Vector2 cB = new Vector2(pB.x, pB.y + 0.024f * scaleB);

            Vector2 diff = cB - cA;
            float dist = diff.magnitude;
            if (dist < 0.005f)
            {
                _renderer.enabled = false;
                return;
            }

            Vector2 dir = diff / dist;
            float angleDeg = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

            // Clamp solidly onto the colored outer rim of each box (outer edge = 0.445, hollow pit starts at 0.327)
            // so the connector visibly locks both frames without obstructing the hollow pit or numbers.
            float insetA = 0.365f * scaleA;
            float insetB = 0.365f * scaleB;
            Vector2 endA = cA + dir * Mathf.Min(insetA, dist * 0.42f);
            Vector2 endB = cB - dir * Mathf.Min(insetB, dist * 0.42f);

            Vector2 mid = 0.5f * (endA + endB);
            float barLen = Mathf.Max(0.08f * avgScale, (endB - endA).magnitude);
            float barHalfLen = barLen * 0.5f * _completeScaleMul;
            float barHalfThick = 0.118f * avgScale * _completeScaleMul;
            float cornerRad = barHalfThick * 0.62f;
            float lipWorldY = 0.068f * avgScale * _completeScaleMul;

            // Place cleanly on top of box front rim (-0.14 local) and behind numbers (-0.26 local)
            float bridgeZ = Mathf.Min(pA.z, pB.z) - 0.155f * avgScale;

            transform.position = new Vector3(mid.x, mid.y, bridgeZ);
            transform.rotation = Quaternion.Euler(0f, 0f, angleDeg);
            transform.localScale = Vector3.one;

            float pad = Mathf.Max(barHalfThick * 1.4f, lipWorldY * 2.2f);
            float quadW = barHalfLen * 2f + pad * 2f;
            float quadH = barHalfThick * 2f + pad * 2f;
            _renderer.transform.localScale = new Vector3(quadW, quadH, 1f);

            // Transform world-down (0, -1) into bar local space so 3D skirt ALWAYS extrudes downward on screen!
            float rad = angleDeg * Mathf.Deg2Rad;
            Vector2 downLocal = new Vector2(-Mathf.Sin(rad), -Mathf.Cos(rad));

            Color bodyA = PuzzleColorPalette.GetBodyColor(colA);
            Color darkA = ComputeDarkColor(colA, bodyA);
            Color hiA = Color.Lerp(bodyA, new Color(0.99f, 0.99f, 1.0f, 1f), 0.34f);

            Color bodyB = PuzzleColorPalette.GetBodyColor(colB);
            Color darkB = ComputeDarkColor(colB, bodyB);
            Color hiB = Color.Lerp(bodyB, new Color(0.99f, 0.99f, 1.0f, 1f), 0.34f);

            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            _mpb.Clear();
            _mpb.SetColor(ColorAPropId, bodyA);
            _mpb.SetColor(DarkColorAPropId, darkA);
            _mpb.SetColor(HighlightColorAPropId, hiA);
            _mpb.SetColor(ColorBPropId, bodyB);
            _mpb.SetColor(DarkColorBPropId, darkB);
            _mpb.SetColor(HighlightColorBPropId, hiB);
            _mpb.SetVector(QuadSizePropId, new Vector4(quadW, quadH, 0f, 0f));
            _mpb.SetVector(BarHalfSizePropId, new Vector4(barHalfLen, barHalfThick, cornerRad, lipWorldY));
            _mpb.SetVector(DownDirLocalPropId, new Vector4(downLocal.x, downLocal.y, 0f, 0f));
            _mpb.SetFloat(ExitHighlightPropId, playable ? 1f : 0f);
            _mpb.SetFloat(IceAPropId, iceA ? 1f : 0f);
            _mpb.SetFloat(IceBPropId, iceB ? 1f : 0f);
            _mpb.SetFloat(FlashAmountPropId, flash);
            _mpb.SetFloat(WaitingSidePropId, waitingSide);

            _renderer.SetPropertyBlock(_mpb);
        }

        private static Color ComputeDarkColor(PuzzleColor color, Color c)
        {
            Color tint = PuzzleColorPalette.GetTcp2ShadowTint(color);
            return new Color(
                c.r * Mathf.Lerp(tint.r, 0.78f, 0.35f),
                c.g * Mathf.Lerp(tint.g, 0.78f, 0.35f),
                c.b * Mathf.Lerp(tint.b, 0.84f, 0.35f),
                1f);
        }

        /// <summary>İkili birlikte patlarken köprüyü kutularla eşzamanlı şişirip parlatarak yok eder.</summary>
        public void PlayCompleteAndDestroy()
        {
            if (_completing) return;
            _completing = true;

            if (!Application.isPlaying)
            {
                DestroyImmediate(gameObject);
                return;
            }

            DOTween.Kill(this);
            // CelebrateAndCompleteRoutine timings: shrink 0.19s, swell 0.23s, hold 0.045s.
            // Box bodyPivots already drive scale via avgScale in SyncGeometryAndVisuals, so only flash here.
            Sequence seq = DOTween.Sequence().SetTarget(this).SetLink(gameObject, LinkBehaviour.KillOnDestroy);
            seq.Append(DOTween.To(() => _completeFlash, v => _completeFlash = v, 0.55f, 0.19f).SetEase(Ease.InOutSine));
            seq.Append(DOTween.To(() => _completeFlash, v => _completeFlash = v, 1.85f, 0.23f).SetEase(Ease.InQuad));
            seq.AppendInterval(0.045f);
            seq.OnComplete(() =>
            {
                if (this != null && gameObject != null) Destroy(gameObject);
            });
        }

        private void OnDisable()
        {
            DOTween.Kill(this);
        }

        private void OnDestroy()
        {
            DOTween.Kill(this);
        }
    }
}
