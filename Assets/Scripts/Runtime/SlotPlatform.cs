using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using TMPro;

namespace PuzzleGlass
{
    public enum SlotType
    {
        Regular = 0,
        VIP = 1,
        Locked = 2
    }

    [ExecuteAlways]
    public class SlotPlatform : MonoBehaviour
    {
        public int slotIndex;
        public SlotType slotType = SlotType.Regular;
        public bool isOccupied;
        public bool isHighlighted;
        public MonsterCharacter currentMonster;
        public float visualQuadScale = 0.74f;

        /// <summary>Her slot kendi çerçevesinde mi (true) yoksa tüm sıra ortak çok katmanlı dok tablasında mı (false)?</summary>
        public static readonly bool UseIndividualFrames = false;
        /// <summary>Tek slot çerçevesinin hücre aralığına oranı (aradaki boşluk = 1 - FrameRatio).</summary>
        public const float FrameRatio = 0.82f;

        public bool isLocked => slotType == SlotType.Locked;
        public bool isVip => slotType == SlotType.VIP;

        /// <summary>Half-width/height (world units) of the visible 3D tile + bottom lip.</summary>
        public float VisibleHalfExtent => 0.46f * 0.68f * Mathf.Clamp(visualQuadScale, 0.35f, 1.5f);

        [Header("Materials")]
        public Material frameMaterial;
        public Material cavityMaterial;
        public Material shadowMaterial;
        public Material slotUiMaterial;
        public Material vipMaterial;
        public Material lockedMaterial;

        private GameObject _uiCanvasObj;
        private MeshRenderer _tileBodyRenderer;
        private GameObject _vipBadgeObj;
        private GameObject _lockedBadgeObj;
        private Tweener _highlightScaleTween;
        private Tweener _highlightColorTween;
        private MaterialPropertyBlock _warningMpb;
        private bool _isWarningActive;
        private static readonly int ColorPropId = Shader.PropertyToID("_Color");

        public event Action<SlotPlatform> OnUnlocked;

        private void OnEnable()
        {
            EnsureCollider();
            if (transform.childCount == 0 || _uiCanvasObj == null)
            {
                BuildSlotMesh();
            }
        }

        private void EnsureCollider()
        {
            BoxCollider col = GetComponent<BoxCollider>();
            if (col == null) col = gameObject.AddComponent<BoxCollider>();
            float s = Mathf.Max(0.54f * Mathf.Clamp(visualQuadScale, 0.35f, 1.5f), CellSize * 0.90f);
            col.size = new Vector3(s, s, 0.5f);
            col.center = Vector3.zero;
        }

        private void OnMouseDown()
        {
            if (isLocked)
            {
                TryUnlockSlot();
            }
        }

        /// <summary>Slot hücresinin boyutu: slotlar arası mesafe (tahtadaki hücre aralığı gibi); yoksa görsel ölçekten.</summary>
        private float CellSize
        {
            get
            {
                var sys = SlotSystem.Instance;
                if (sys != null && sys.slotSpacing > 0.1f) return sys.slotSpacing;
                return VisibleHalfExtent * 2f / TrayTileVisuals.TileOuterRatio;
            }
        }

        public void Initialize(int index, Material frameMat, Material cavityMat, Material shadowMat = null, float quadScale = 0.74f, SlotType type = SlotType.Regular)
        {
            slotIndex = index;
            slotType = type;
            frameMaterial = frameMat;
            cavityMaterial = cavityMat;
            shadowMaterial = shadowMat;
            visualQuadScale = Mathf.Clamp(quadScale, 0.35f, 1.5f);
            BuildSlotMesh();
        }

        [ContextMenu("Rebuild Slot Mesh")]
        public void BuildSlotMesh()
        {
            EnsureCollider();

            // Clear previous children
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                if (Application.isPlaying)
                    Destroy(transform.GetChild(i).gameObject);
                else
                    DestroyImmediate(transform.GetChild(i).gameObject);
            }

            // Pikselleşmeyi tamamen önlemek için her slot yüksek çözünürlüklü SDF shader (PuzzleGlass/SlotSquircleUI) ile tek bir Quad üzerinde çizilir
            float cell = CellSize;
            float quadSize = Mathf.Min(cell * 0.94f, Mathf.Max(0.42f, visualQuadScale * 1.18f));

            GameObject slotQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            slotQuad.name = "SlotSDFQuad";
            slotQuad.transform.SetParent(transform, false);
            slotQuad.transform.localPosition = Vector3.zero;
            slotQuad.transform.localScale = new Vector3(quadSize, quadSize, 1f);
            Collider qCol = slotQuad.GetComponent<Collider>();
            if (qCol != null)
            {
                if (Application.isPlaying) Destroy(qCol); else DestroyImmediate(qCol);
            }

            _tileBodyRenderer = slotQuad.GetComponent<MeshRenderer>();
            _tileBodyRenderer.sharedMaterial = TrayTileVisuals.GetSdfSlotMaterial(slotType, isHighlighted);
            _tileBodyRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _tileBodyRenderer.receiveShadows = false;

            // VIP rozeti için dünya uzayı canvas (hücrenin önünde; '+' ikonu doğrudan SDF shader içinde pürüzsüz çizilir)
            _uiCanvasObj = new GameObject("SlotUICanvas");
            _uiCanvasObj.transform.SetParent(transform, false);
            _uiCanvasObj.transform.localPosition = new Vector3(0f, 0f, -0.04f);
            _uiCanvasObj.transform.localRotation = Quaternion.identity;
            float s = 0.01f * Mathf.Clamp(visualQuadScale, 0.35f, 1.5f);
            _uiCanvasObj.transform.localScale = new Vector3(s, s, s);

            Canvas canvas = _uiCanvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 5;

            RectTransform canvasRect = _uiCanvasObj.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(92f, 92f);

            // VIP Slot Badge ("VIP" gold text on empty slot)
            if (isVip)
            {
                BuildVipBadge();
            }
        }

        private void BuildVipBadge()
        {
            _vipBadgeObj = new GameObject("VIP_Badge");
            _vipBadgeObj.transform.SetParent(_uiCanvasObj.transform, false);

            RectTransform rect = _vipBadgeObj.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, 2.0f);
            rect.sizeDelta = new Vector2(76f, 34f);

            var tmp = _vipBadgeObj.AddComponent<TextMeshProUGUI>();
            tmp.text = "VIP";
            tmp.fontSize = 23;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(1.00f, 0.84f, 0.28f, 0.98f);
            tmp.raycastTarget = false;

            _vipBadgeObj.SetActive(currentMonster == null);
        }

        private void BuildLockedBadge(float cell)
        {
            _lockedBadgeObj = new GameObject("Locked_PlusBadge3D");
            _lockedBadgeObj.transform.SetParent(transform, false);
            _lockedBadgeObj.transform.localPosition = new Vector3(0f, -0.005f, -0.018f);

            float armLen = cell * 0.31f;
            float armThick = cell * 0.105f;
            float shadowPad = cell * 0.032f;
            float armRad = armThick * 0.46f;
            float shadowRad = (armThick + shadowPad) * 0.46f;

            // Koyu dış kontur / alt gölge artısı
            TrayTileVisuals.Part("PlusShadowH", _lockedBadgeObj.transform, new Vector3(0f, -0.006f, 0.006f), armLen + shadowPad, armThick + shadowPad, 0.010f, shadowRad, TrayTileVisuals.PlusShadowMat);
            TrayTileVisuals.Part("PlusShadowV", _lockedBadgeObj.transform, new Vector3(0f, -0.006f, 0.006f), armThick + shadowPad, armLen + shadowPad, 0.010f, shadowRad, TrayTileVisuals.PlusShadowMat);

            // 3D koyu yeşil alt dudak
            TrayTileVisuals.Part("PlusBaseH", _lockedBadgeObj.transform, new Vector3(0f, -0.004f, 0.003f), armLen, armThick, 0.010f, armRad, TrayTileVisuals.PlusBaseMat);
            TrayTileVisuals.Part("PlusBaseV", _lockedBadgeObj.transform, new Vector3(0f, -0.004f, 0.003f), armThick, armLen, 0.010f, armRad, TrayTileVisuals.PlusBaseMat);

            // Parlak yeşil ön "+" yüzeyi
            TrayTileVisuals.Part("PlusFrontH", _lockedBadgeObj.transform, Vector3.zero, armLen, armThick, 0.010f, armRad, TrayTileVisuals.PlusFrontMat);
            TrayTileVisuals.Part("PlusFrontV", _lockedBadgeObj.transform, Vector3.zero, armThick, armLen, 0.010f, armRad, TrayTileVisuals.PlusFrontMat);
        }

        public Vector3 GetPlacementPosition()
        {
            // Center of the slot pad face, resting cleanly in front of the frosted UI slot
            return transform.position + new Vector3(0f, 0.018f * Mathf.Clamp(visualQuadScale, 0.35f, 1.5f), -0.15f);
        }

        public void ReserveSlot(MonsterCharacter monster)
        {
            isOccupied = true;
            currentMonster = monster;
            if (_vipBadgeObj != null) _vipBadgeObj.SetActive(false);
        }

        public void AttachMonster(MonsterCharacter monster)
        {
            currentMonster = monster;
            isOccupied = monster != null;
            if (_vipBadgeObj != null) _vipBadgeObj.SetActive(monster == null);

            if (monster != null)
            {
                monster.transform.position = GetPlacementPosition();
                monster.assignedSlot = this;
                PlayImpactPunch();
            }
        }

        public void DetachMonster()
        {
            isOccupied = false;
            currentMonster = null;
            if (_vipBadgeObj != null) _vipBadgeObj.SetActive(true);
        }

        public void TryUnlockSlot()
        {
            if (!isLocked) return;

            if (SlotSystem.Instance != null)
            {
                SlotSystem.Instance.UnlockSlot(this);
            }
            else
            {
                slotType = SlotType.Regular;
                BuildSlotMesh();

                PuzzleSoundManager.Instance?.PlayUnlock();

                transform.DOKill(false);
                transform.localScale = Vector3.one * 0.70f;
                transform.DOScale(Vector3.one, 0.35f).SetEase(Ease.OutBack);

                OnUnlocked?.Invoke(this);
            }
        }

        public void SetHighlight(bool highlight)
        {
            if (isHighlighted == highlight || isLocked) return;
            isHighlighted = highlight;

            if (!gameObject.activeInHierarchy) return;

            if (_highlightScaleTween != null && _highlightScaleTween.IsActive()) _highlightScaleTween.Kill();
            if (_highlightColorTween != null && _highlightColorTween.IsActive()) _highlightColorTween.Kill();

            Vector3 targetScale = highlight ? Vector3.one * 1.12f : Vector3.one;
            _highlightScaleTween = transform.DOScale(targetScale, 0.15f).SetEase(highlight ? Ease.OutBack : Ease.OutQuad);

            // Üzerine blok getirilince SDF slot malzemesi parlak vurgulu varyanta geçer
            if (_tileBodyRenderer != null)
            {
                _tileBodyRenderer.sharedMaterial = TrayTileVisuals.GetSdfSlotMaterial(slotType, highlight);
                if (highlight) SetWarningPulse(false);
            }
        }

        /// <summary>Son boş slot kaldığında oyuncuya yaklaşan doluluk riskini yumuşak renk nabzıyla gösterir.</summary>
        public void SetWarningPulse(bool active, float pulse01 = 0f)
        {
            if (_tileBodyRenderer == null) return;
            if (!active || isOccupied || isLocked || isHighlighted)
            {
                if (!_isWarningActive) return;
                _isWarningActive = false;
                if (_warningMpb == null) _warningMpb = new MaterialPropertyBlock();
                _warningMpb.SetColor(ColorPropId, Color.white);
                _tileBodyRenderer.SetPropertyBlock(_warningMpb);
                return;
            }

            _isWarningActive = true;
            if (_warningMpb == null) _warningMpb = new MaterialPropertyBlock();
            Color warnTint = Color.Lerp(Color.white, new Color(1.0f, 0.68f, 0.56f, 1f), Mathf.Clamp01(pulse01));
            _warningMpb.SetColor(ColorPropId, warnTint);
            _tileBodyRenderer.SetPropertyBlock(_warningMpb);
        }

        public void PlayImpactPunch()
        {
            if (!gameObject.activeInHierarchy) return;

            transform.DOKill(false);
            Sequence s = DOTween.Sequence();
            s.Append(transform.DOScale(new Vector3(1.06f, 0.94f, 1.06f), 0.07f).SetEase(Ease.OutSine));
            s.Append(transform.DOScale(Vector3.one, 0.14f).SetEase(Ease.OutCubic));
        }

        private void OnDisable()
        {
            transform.DOKill();
        }

        private void OnDestroy()
        {
            transform.DOKill();
        }
    }
}
