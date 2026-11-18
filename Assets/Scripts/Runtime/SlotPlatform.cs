using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace PuzzleGlass
{
    [ExecuteAlways]
    public class SlotPlatform : MonoBehaviour
    {
        public int slotIndex;
        public bool isOccupied;
        public bool isHighlighted;
        public MonsterCharacter currentMonster;
        public float visualQuadScale = 0.74f;

        /// <summary>Half-width/height (world units) of the visible 3D tile + bottom lip.</summary>
        public float VisibleHalfExtent => 0.46f * 0.68f * Mathf.Clamp(visualQuadScale, 0.35f, 1.5f);

        [Header("Materials")]
        public Material frameMaterial;
        public Material cavityMaterial;
        public Material shadowMaterial;
        public Material slotUiMaterial;

        private GameObject _uiCanvasObj;
        private RawImage _slotRawImage;
        private Tweener _highlightScaleTween;
        private Tweener _highlightColorTween;

        private void OnEnable()
        {
            if (transform.childCount == 0 || _uiCanvasObj == null)
            {
                BuildSlotMesh();
            }
        }

        public void Initialize(int index, Material frameMat, Material cavityMat, Material shadowMat = null, float quadScale = 0.74f)
        {
            slotIndex = index;
            frameMaterial = frameMat;
            cavityMaterial = cavityMat;
            shadowMaterial = shadowMat;
            visualQuadScale = Mathf.Clamp(quadScale, 0.35f, 1.5f);
            BuildSlotMesh();
        }

        [ContextMenu("Rebuild Slot Mesh")]
        public void BuildSlotMesh()
        {
            // Clear previous children
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                if (Application.isPlaying)
                    Destroy(transform.GetChild(i).gameObject);
                else
                    DestroyImmediate(transform.GetChild(i).gameObject);
            }

            EnsureMaterials();

            // World-Space UI Canvas + RawImage rendering the icy sky-blue 3D rounded-square slot + soft drop shadow
            _uiCanvasObj = new GameObject("SlotUICanvas");
            _uiCanvasObj.transform.SetParent(transform, false);
            _uiCanvasObj.transform.localPosition = new Vector3(0f, 0f, 0.04f);
            _uiCanvasObj.transform.localRotation = Quaternion.identity;
            float s = 0.01f * Mathf.Clamp(visualQuadScale, 0.35f, 1.5f);
            _uiCanvasObj.transform.localScale = new Vector3(s, s, s);

            Canvas canvas = _uiCanvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = -5;

            RectTransform canvasRect = _uiCanvasObj.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(92f, 92f);

            GameObject imgObj = new GameObject("SlotSquircleUI_WithFakeShadow");
            imgObj.transform.SetParent(_uiCanvasObj.transform, false);

            RectTransform imgRect = imgObj.AddComponent<RectTransform>();
            imgRect.anchorMin = new Vector2(0.5f, 0.5f);
            imgRect.anchorMax = new Vector2(0.5f, 0.5f);
            imgRect.pivot = new Vector2(0.5f, 0.5f);
            imgRect.anchoredPosition = Vector2.zero;
            imgRect.sizeDelta = new Vector2(92f, 92f);

            _slotRawImage = imgObj.AddComponent<RawImage>();
            _slotRawImage.texture = Texture2D.whiteTexture;
            _slotRawImage.material = slotUiMaterial;
            _slotRawImage.color = Color.white;
            _slotRawImage.raycastTarget = false;
        }

        private void EnsureMaterials()
        {
            Shader slotShader = Shader.Find("PuzzleGlass/SlotSquircleUI")
                ?? Shader.Find("Sprites/Default")
                ?? Shader.Find("Universal Render Pipeline/Unlit");

            if (slotUiMaterial == null || slotUiMaterial.shader != slotShader)
            {
                slotUiMaterial = new Material(slotShader);
                slotUiMaterial.name = "Mat_SlotSquircleUI_Image2";
            }

            // Frosted Icy Sky-Blue + Crisp White Top Cushion + Vivid Sky-Blue 3D Lower Lip
            slotUiMaterial.SetColor("_FrameColor", new Color(0.83f, 0.92f, 1.00f, 1.0f));      // #D4EBFF pale icy lower face
            slotUiMaterial.SetColor("_FrameHighlight", new Color(0.98f, 0.99f, 1.00f, 1.0f));  // #FAFCFF crisp white top bevel
            slotUiMaterial.SetColor("_FrameEdgeShade", new Color(0.25f, 0.66f, 1.00f, 1.0f));  // #40A8FF vivid sky-blue 3D bottom lip
            slotUiMaterial.SetColor("_CavityColor", new Color(0.91f, 0.96f, 1.00f, 1.0f));     // #E8F5FF frosty upper face
            slotUiMaterial.SetColor("_CavityShadow", new Color(0.16f, 0.53f, 0.94f, 1.0f));    // #2987F0 deeper sky-blue side lip
            slotUiMaterial.SetColor("_FakeShadowColor", new Color(0.18f, 0.23f, 0.35f, 0.34f));// Soft cool slate drop shadow
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
        }

        public void AttachMonster(MonsterCharacter monster)
        {
            currentMonster = monster;
            isOccupied = monster != null;
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
        }

        public void SetHighlight(bool highlight)
        {
            if (isHighlighted == highlight) return;
            isHighlighted = highlight;

            if (!gameObject.activeInHierarchy) return;

            if (_highlightScaleTween != null && _highlightScaleTween.IsActive()) _highlightScaleTween.Kill();
            if (_highlightColorTween != null && _highlightColorTween.IsActive()) _highlightColorTween.Kill();

            Vector3 targetScale = highlight ? Vector3.one * 1.12f : Vector3.one;
            _highlightScaleTween = transform.DOScale(targetScale, 0.15f).SetEase(highlight ? Ease.OutBack : Ease.OutQuad);

            if (_slotRawImage != null)
            {
                Color targetCol = highlight ? new Color(1.15f, 1.15f, 1.25f, 1f) : Color.white;
                _highlightColorTween = _slotRawImage.DOColor(targetCol, 0.15f);
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
