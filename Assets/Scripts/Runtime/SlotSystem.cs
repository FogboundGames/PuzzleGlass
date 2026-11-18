using System.Collections.Generic;
using UnityEngine;

namespace PuzzleGlass
{
    [ExecuteAlways]
    public class SlotSystem : MonoBehaviour
    {
        public static SlotSystem Instance { get; private set; }

        public static bool IsActive =>
            Instance != null && Instance.slotsEnabled && Instance.gameObject.activeInHierarchy && Instance.slots != null && Instance.slots.Count > 0;

        [Header("Slot Configuration")]
        public bool slotsEnabled = true;
        [Range(1, 6)] public int slotCount = 5;
        public float slotSpacing = 0.59f;
        [Range(0.45f, 1.25f)] public float slotScale = 0.74f;
        public float gapBelowGlass = 0.22f;

        [Header("Materials")]
        public Material frameMaterial;
        public Material cavityMaterial;
        public Material shadowMaterial;

        [HideInInspector]
        public List<SlotPlatform> slots = new List<SlotPlatform>();

        private void Awake()
        {
            Instance = this;
            RebuildSlots();
        }

        private void OnEnable()
        {
            Instance = this;
            RebuildSlots();
        }

        [ContextMenu("Rebuild Slots")]
        public void RebuildSlots()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                if (Application.isPlaying)
                    Destroy(transform.GetChild(i).gameObject);
                else
                    DestroyImmediate(transform.GetChild(i).gameObject);
            }
            slots.Clear();

            if (!slotsEnabled)
            {
                return;
            }

            EnsureMaterials();

            // Center the slots around X = 0 with balanced clean spacing
            float totalWidth = (slotCount - 1) * slotSpacing;
            float startX = -totalWidth * 0.5f;

            for (int i = 0; i < slotCount; i++)
            {
                GameObject slotObj = new GameObject($"Slot_{i}");
                slotObj.transform.SetParent(transform, false);
                slotObj.transform.localPosition = new Vector3(startX + i * slotSpacing, 0f, 0f);

                SlotPlatform platform = slotObj.AddComponent<SlotPlatform>();
                platform.Initialize(i, frameMaterial, cavityMaterial, shadowMaterial, slotScale);
                slots.Add(platform);
            }
        }

        /// <summary>Visible vertical world bounds (including lower drop shadow margin) of the slot row.</summary>
        public bool TryGetVisibleVerticalBounds(out float minY, out float maxY)
        {
            if (!slotsEnabled || !gameObject.activeInHierarchy || slots == null || slots.Count == 0)
            {
                minY = transform.position.y;
                maxY = transform.position.y;
                return false;
            }
            float halfH = 0.46f * 0.68f * Mathf.Clamp(slotScale, 0.35f, 1.5f);
            float cy = transform.position.y;
            maxY = cy + halfH + 0.03f;
            minY = cy - halfH - 0.09f; // include soft bottom drop shadow
            return true;
        }

        /// <summary>Positions the horizontal slot row cleanly beneath the upper glass container / track collar.</summary>
        public void AlignBelowGlass(ShapedGlassContainer glass)
        {
            if (glass == null) return;

            float glassCenterX = 0f;
            float glassBottomY = glass.transform.position.y - 0.82f;
            if (glass.TryGetVisibleFrameWorldBounds(out float gMinX, out float gMaxX, out float gMinY, out _))
            {
                glassCenterX = (gMinX + gMaxX) * 0.5f;
                glassBottomY = gMinY;
            }

            float halfH = 0.46f * 0.68f * Mathf.Clamp(slotScale, 0.35f, 1.5f);
            float minCollarGap = (glass.showBottomExitCollar && (glass.IsPoolShape || glass.IsTrackShape)) ? 0.28f : 0.16f;
            float topGap = Mathf.Max(minCollarGap, gapBelowGlass);
            float targetY = glassBottomY - topGap - halfH;

            transform.position = new Vector3(glassCenterX, targetY, 0.05f);
        }

        private void EnsureMaterials()
        {
#if UNITY_EDITOR
            if (frameMaterial == null)
                frameMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Mat_SlotFrame.mat");
            if (cavityMaterial == null)
                cavityMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Mat_SlotCavity.mat");
#endif
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            if (frameMaterial == null)
            {
                frameMaterial = new Material(litShader);
                frameMaterial.name = "Mat_SlotFrame";
                Color frameColor = new Color(0.80f, 0.88f, 0.98f, 1f); // #CCE0FA soft baby-blue
                frameMaterial.color = frameColor;
                if (frameMaterial.HasProperty("_BaseColor")) frameMaterial.SetColor("_BaseColor", frameColor);
                if (frameMaterial.HasProperty("_Smoothness")) frameMaterial.SetFloat("_Smoothness", 0.92f);
            }

            if (cavityMaterial == null)
            {
                cavityMaterial = new Material(litShader);
                cavityMaterial.name = "Mat_SlotCavity";
                Color cavityColor = new Color(0.38f, 0.58f, 0.84f, 1f); // #6094D6 rich periwinkle
                cavityMaterial.color = cavityColor;
                if (cavityMaterial.HasProperty("_BaseColor")) cavityMaterial.SetColor("_BaseColor", cavityColor);
                if (cavityMaterial.HasProperty("_Smoothness")) cavityMaterial.SetFloat("_Smoothness", 0.70f);
            }

            if (shadowMaterial == null)
            {
                shadowMaterial = new Material(litShader);
                shadowMaterial.name = "Mat_SlotShadow";
                Color shadowCol = new Color(0.82f, 0.78f, 0.72f, 0.60f); // warm ambient shadow
                shadowMaterial.color = shadowCol;
                if (shadowMaterial.HasProperty("_BaseColor")) shadowMaterial.SetColor("_BaseColor", shadowCol);
                if (shadowMaterial.HasProperty("_Smoothness")) shadowMaterial.SetFloat("_Smoothness", 0.20f);
            }
        }

        public SlotPlatform GetSlot(int index)
        {
            if (index >= 0 && index < slots.Count)
                return slots[index];
            return null;
        }

        public SlotPlatform GetFirstEmptySlot()
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] != null && !slots[i].isOccupied && slots[i].currentMonster == null)
                    return slots[i];
            }
            return null;
        }

        public void ClearAllOccupied()
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] != null)
                {
                    slots[i].DetachMonster();
                }
            }
        }
    }
}
