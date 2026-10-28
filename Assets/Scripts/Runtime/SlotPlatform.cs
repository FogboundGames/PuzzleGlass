using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
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

        private GameObject _frameObj;
        private GameObject _cavityObj;
        private GameObject _shadowObj;

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

            // Dimensions matching Reference Photo 2:
            // Symmetrical rounded square tray with raised tubular rim and recessed central cavity
            float width = 0.58f;
            float height = 0.58f;
            float cornerRadius = 0.15f;
            float rimPipeRadius = 0.054f;

            // 1. Soft Ambient Drop Shadow Base Plate underneath the slot (Z = +0.055f)
            _shadowObj = new GameObject("SlotShadow");
            _shadowObj.transform.SetParent(transform, false);
            _shadowObj.transform.localPosition = new Vector3(0f, -0.015f, 0.055f);
            MeshFilter mfShadow = _shadowObj.AddComponent<MeshFilter>();
            MeshRenderer mrShadow = _shadowObj.AddComponent<MeshRenderer>();
            mfShadow.sharedMesh = ProceduralMeshUtils.CreatePillBadge(width * 1.15f, height * 1.15f, 0.015f, cornerRadius * 1.25f);
            mrShadow.sharedMaterial = shadowMaterial;

            // 2. Inner Recessed Cavity Floor (Z = +0.022f)
            _cavityObj = new GameObject("SlotCavity");
            _cavityObj.transform.SetParent(transform, false);
            _cavityObj.transform.localPosition = new Vector3(0f, 0f, 0.022f);
            MeshFilter mfCavity = _cavityObj.AddComponent<MeshFilter>();
            MeshRenderer mrCavity = _cavityObj.AddComponent<MeshRenderer>();
            mfCavity.sharedMesh = ProceduralMeshUtils.CreatePillBadge(width * 0.88f, height * 0.88f, 0.035f, cornerRadius * 0.80f);
            mrCavity.sharedMaterial = cavityMaterial;

            // 3. Raised Pillowy Tubular Outer Rim / Frame (Z = 0.0f)
            _frameObj = new GameObject("SlotFrame");
            _frameObj.transform.SetParent(transform, false);
            _frameObj.transform.localPosition = Vector3.zero;
            MeshFilter mfFrame = _frameObj.AddComponent<MeshFilter>();
            MeshRenderer mrFrame = _frameObj.AddComponent<MeshRenderer>();
            Vector3[] rimPath = GenerateRoundedRectPath(width, height, cornerRadius, 8);
            mfFrame.sharedMesh = ProceduralMeshUtils.CreatePipeMesh(rimPath, rimPipeRadius, 14, true);
            mrFrame.sharedMaterial = frameMaterial;
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
                Color frameColor = new Color(0.80f, 0.88f, 0.98f, 1f); // #CCE0FA soft pastel baby-blue
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

        private Vector3[] GenerateRoundedRectPath(float w, float h, float r, int segsPerCorner)
        {
            float innerW = w * 0.5f - r;
            float innerH = h * 0.5f - r;

            Vector2[] centers = new Vector2[]
            {
                new Vector2(innerW, innerH),    // TR: 0 to 90 deg
                new Vector2(-innerW, innerH),   // TL: 90 to 180 deg
                new Vector2(-innerW, -innerH),  // BL: 180 to 270 deg
                new Vector2(innerW, -innerH)    // BR: 270 to 360 deg
            };

            List<Vector3> path = new List<Vector3>();

            for (int c = 0; c < 4; c++)
            {
                float startAngle = c * Mathf.PI * 0.5f;
                for (int s = 0; s < segsPerCorner; s++)
                {
                    float a = startAngle + (float)s / segsPerCorner * Mathf.PI * 0.5f;
                    float x = centers[c].x + Mathf.Cos(a) * r;
                    float y = centers[c].y + Mathf.Sin(a) * r;
                    path.Add(new Vector3(x, y, 0f));
                }
            }

            return path.ToArray();
        }

        public Vector3 GetPlacementPosition()
        {
            // Center of the slot tray, resting directly in the recessed socket
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

            transform.DOKill(true);
            transform.DOPunchScale(new Vector3(0.12f, -0.15f, 0.12f), 0.24f, vibrato: 8, elasticity: 0.65f);

            if (_cavityObj != null)
            {
                _cavityObj.transform.DOKill(true);
                _cavityObj.transform.DOPunchPosition(new Vector3(0, 0, 0.02f), 0.20f, vibrato: 6, elasticity: 0.5f);
            }
        }

        private void OnDisable()
        {
            transform.DOKill();
            if (_cavityObj != null) _cavityObj.transform.DOKill();
        }

        private void OnDestroy()
        {
            transform.DOKill();
            if (_cavityObj != null) _cavityObj.transform.DOKill();
        }
    }
}
