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
        public MonsterCharacter currentMonster;

        [Header("Materials")]
        public Material frameMaterial;
        public Material cavityMaterial;
        public Material shadowMaterial;
        public Material slotUiMaterial;

        private GameObject _uiCanvasObj;
        private RawImage _slotRawImage;

        private void OnEnable()
        {
            if (transform.childCount == 0 || _uiCanvasObj == null)
            {
                BuildSlotMesh();
            }
        }

        public void Initialize(int index, Material frameMat, Material cavityMat, Material shadowMat = null)
        {
            slotIndex = index;
            frameMaterial = frameMat;
            cavityMaterial = cavityMat;
            shadowMaterial = shadowMat;
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

            // World-Space UI Canvas + RawImage rendering the analytic Image-2 Periwinkle Squircle Slot + Fake Drop Shadow
            _uiCanvasObj = new GameObject("SlotUICanvas");
            _uiCanvasObj.transform.SetParent(transform, false);
            _uiCanvasObj.transform.localPosition = new Vector3(0f, 0f, 0.04f);
            _uiCanvasObj.transform.localRotation = Quaternion.identity;
            _uiCanvasObj.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);

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

            // Image 2 Reference Periwinkle-Lavender UI Palette + Warm Feathered Fake Drop Shadow
            slotUiMaterial.SetColor("_FrameColor", new Color(0.62f, 0.67f, 0.98f, 1.0f));      // #9EABFA soft periwinkle-lavender
            slotUiMaterial.SetColor("_FrameHighlight", new Color(0.86f, 0.89f, 1.00f, 1.0f));  // #DBE3FF icy-lavender top-left catchlight
            slotUiMaterial.SetColor("_FrameEdgeShade", new Color(0.49f, 0.55f, 0.89f, 1.0f));  // #7D8CE3 3D lower-left lip shade
            slotUiMaterial.SetColor("_CavityColor", new Color(0.42f, 0.49f, 0.88f, 1.0f));     // #6B7DE0 rich periwinkle inner well
            slotUiMaterial.SetColor("_CavityShadow", new Color(0.28f, 0.35f, 0.75f, 1.0f));    // #4759BF top-left inner cast shadow
            slotUiMaterial.SetColor("_FakeShadowColor", new Color(0.74f, 0.58f, 0.44f, 0.44f));// Warm caramel-peach fake drop shadow
        }

        public Vector3 GetPlacementPosition()
        {
            // Center of the slot tray, resting directly in the recessed socket in front of the UI slot
            return transform.position + new Vector3(0f, 0.08f, -0.15f);
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
