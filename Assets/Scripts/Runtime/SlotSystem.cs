using System.Collections.Generic;
using UnityEngine;

namespace PuzzleGlass
{
    [ExecuteAlways]
    public class SlotSystem : MonoBehaviour
    {
        public static SlotSystem Instance { get; private set; }

        [Header("Slot Configuration (Reference Photo 2)")]
        public int slotCount = 4;
        public float slotSpacing = 0.84f;

        [Header("Materials")]
        public Material frameMaterial;
        public Material cavityMaterial;
        public Material shadowMaterial;

        [HideInInspector]
        public List<SlotPlatform> slots = new List<SlotPlatform>();

        private void Awake()
        {
            Instance = this;
            if (slots.Count == 0 || transform.childCount == 0 || transform.Find("Slot_0/SlotShadow") == null)
            {
                RebuildSlots();
            }
        }

        private void OnEnable()
        {
            Instance = this;
            if (slots.Count == 0 || transform.childCount == 0 || transform.Find("Slot_0/SlotShadow") == null)
            {
                RebuildSlots();
            }
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

            EnsureMaterials();

            // Center the slots around X = 0 with distinct clean spacing matching Photo 2
            float totalWidth = (slotCount - 1) * slotSpacing;
            float startX = -totalWidth * 0.5f;

            for (int i = 0; i < slotCount; i++)
            {
                GameObject slotObj = new GameObject($"Slot_{i}");
                slotObj.transform.SetParent(transform, false);
                slotObj.transform.localPosition = new Vector3(startX + i * slotSpacing, 0f, 0f);

                SlotPlatform platform = slotObj.AddComponent<SlotPlatform>();
                platform.Initialize(i, frameMaterial, cavityMaterial, shadowMaterial);
                slots.Add(platform);
            }
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
