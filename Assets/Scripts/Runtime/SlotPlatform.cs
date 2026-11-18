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
            float s = 0.54f * Mathf.Clamp(visualQuadScale, 0.35f, 1.5f);
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

            // Eski alt tahtanın hücresi gibi: açık mor kenar (bevel) + içte koyu, gömülü zemin (3D, tahtayla aynı malzeme)
            float cell = CellSize;
            float tileOuter = cell * TrayTileVisuals.TileOuterRatio;
            float tileInner = cell * TrayTileVisuals.TileInnerRatio;
            float tileRad = cell * TrayTileVisuals.TileRadiusRatio;
            TrayTileVisuals.GetTileMaterials(slotType, out Material bevelMat, out Material bodyMat);
            TrayTileVisuals.Part("CellBevel", transform, new Vector3(0f, 0.007f, 0.005f), tileOuter, tileOuter, 0.02f, tileRad, bevelMat);
            _tileBodyRenderer = TrayTileVisuals.Part("CellTile", transform, new Vector3(0f, 0f, -0.005f), tileInner, tileInner, 0.02f, tileRad * 0.88f, bodyMat);

            // VIP / "+" rozetleri için dünya uzayı canvas (hücrenin önünde)
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

            // 1. VIP Slot Badge ("VIP" gold text on empty slot)
            if (isVip)
            {
                BuildVipBadge();
            }

            // 2. Locked Slot Badge (Green "+" circular button)
            if (isLocked)
            {
                BuildLockedBadge();
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
            rect.anchoredPosition = new Vector2(0f, 3.5f);
            rect.sizeDelta = new Vector2(76f, 34f);

            var tmp = _vipBadgeObj.AddComponent<TextMeshProUGUI>();
            tmp.text = "VIP";
            tmp.fontSize = 24;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.88f, 0.58f, 0.05f, 0.95f); // Rich golden bronze
            tmp.raycastTarget = false;

            _vipBadgeObj.SetActive(currentMonster == null);
        }

        private void BuildLockedBadge()
        {
            _lockedBadgeObj = new GameObject("Locked_PlusBadge");
            _lockedBadgeObj.transform.SetParent(_uiCanvasObj.transform, false);

            RectTransform rect = _lockedBadgeObj.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, 3.5f);
            rect.sizeDelta = new Vector2(38f, 38f);

            // Circular Green Background
            var bgImg = _lockedBadgeObj.AddComponent<Image>();
            bgImg.color = new Color(0.20f, 0.85f, 0.42f, 1f); // Vibrant emerald-lime green #33D96B
            bgImg.raycastTarget = false;

            // Plus Sign "+" Text
            GameObject plusTxtObj = new GameObject("Plus_Text");
            plusTxtObj.transform.SetParent(_lockedBadgeObj.transform, false);

            RectTransform txtRect = plusTxtObj.AddComponent<RectTransform>();
            txtRect.anchorMin = Vector2.zero;
            txtRect.anchorMax = Vector2.one;
            txtRect.sizeDelta = Vector2.zero;
            txtRect.anchoredPosition = new Vector2(0f, 1f);

            var tmp = plusTxtObj.AddComponent<TextMeshProUGUI>();
            tmp.text = "+";
            tmp.fontSize = 30;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
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

            // Üzerine blok getirilince iç zemin tahtanın açık tonuna döner
            if (_tileBodyRenderer != null)
            {
                _tileBodyRenderer.sharedMaterial = highlight ? TrayTileVisuals.TileBodyHighlight : TrayTileVisuals.TileBody(slotType);
            }
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
