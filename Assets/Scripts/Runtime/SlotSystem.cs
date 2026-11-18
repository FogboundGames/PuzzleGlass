using System;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

namespace PuzzleGlass
{
    [ExecuteAlways]
    public class SlotSystem : MonoBehaviour
    {
        public static SlotSystem Instance { get; private set; }

        public static bool IsActive =>
            Instance != null && Instance.slotsEnabled && Instance.gameObject.activeInHierarchy && Instance.slots != null && Instance.slots.Count > 0;

        [Header("Slot Dock Configuration (Bus Traffic Fever Style)")]
        public bool slotsEnabled = true;
        public bool enableVipSlot = false;
        [Range(1, 8)] public int regularSlotCount = 4;
        public bool showAddSlotButton = true;
        [Range(4, 9)] public int maxRegularSlotCount = 7;

        /// <summary>Geriye dönük uyumluluk için regular slot sayısını döndürür/ayarlar.</summary>
        public int slotCount
        {
            get => regularSlotCount;
            set => regularSlotCount = Mathf.Clamp(value, 1, maxRegularSlotCount);
        }

        public float slotSpacing = 0.52f;
        [Range(0.40f, 1.25f)] public float slotScale = 0.68f;
        public float gapBelowGlass = 0.22f;

        [Header("Materials")]
        public Material frameMaterial;
        public Material cavityMaterial;
        public Material shadowMaterial;

        [HideInInspector]
        public List<SlotPlatform> slots = new List<SlotPlatform>();

        public event Action<SlotPlatform> OnAnySlotUnlocked;

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

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Oyun modunda inspector'da VIP / "+" / slot sayısı değişince sıra hemen yeniden kurulur.
            // (Edit modunda yapılmaz: OnValidate her derlemede de çağrılır, sahne boşuna kirlenirdi.)
            if (!Application.isPlaying) return;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null || !isActiveAndEnabled || !Application.isPlaying) return;
                RebuildSlots();
            };
        }
#endif

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

            // Dok Sistemi:
            // [VIP (varsa)] + [Normal Aktif Slotlar] + [Tek Bir Dinamik '+' Slotu (sağda)]
            int vipCount = enableVipSlot ? 1 : 0;
            int plusCount = (showAddSlotButton && regularSlotCount < maxRegularSlotCount) ? 1 : 0;
            int totalSlots = vipCount + regularSlotCount + plusCount;
            if (totalSlots <= 0) return;

            float totalWidth = (totalSlots - 1) * slotSpacing;
            float startX = -totalWidth * 0.5f;
            int currentIndex = 0;

            // 1. VIP Slot (Varsayılan olarak kapalı, istenirse en solda açılabilir)
            if (enableVipSlot)
            {
                GameObject slotObj = new GameObject("Slot_VIP");
                slotObj.transform.SetParent(transform, false);
                slotObj.transform.localPosition = new Vector3(startX + currentIndex * slotSpacing, 0f, 0f);

                SlotPlatform platform = slotObj.AddComponent<SlotPlatform>();
                platform.Initialize(currentIndex, frameMaterial, cavityMaterial, shadowMaterial, slotScale, SlotType.VIP);
                slots.Add(platform);
                currentIndex++;
            }

            // 2. Normal Aktif Slotlar
            for (int i = 0; i < regularSlotCount; i++)
            {
                GameObject slotObj = new GameObject($"Slot_Regular_{i}");
                slotObj.transform.SetParent(transform, false);
                slotObj.transform.localPosition = new Vector3(startX + currentIndex * slotSpacing, 0f, 0f);

                SlotPlatform platform = slotObj.AddComponent<SlotPlatform>();
                platform.Initialize(currentIndex, frameMaterial, cavityMaterial, shadowMaterial, slotScale, SlotType.Regular);
                slots.Add(platform);
                currentIndex++;
            }

            // 3. Sağ tarafta tek bir dinamik "+" slotu
            if (plusCount > 0)
            {
                GameObject slotObj = new GameObject("Slot_Locked_Plus");
                slotObj.transform.SetParent(transform, false);
                slotObj.transform.localPosition = new Vector3(startX + currentIndex * slotSpacing, 0f, 0f);

                SlotPlatform platform = slotObj.AddComponent<SlotPlatform>();
                platform.Initialize(currentIndex, frameMaterial, cavityMaterial, shadowMaterial, slotScale, SlotType.Locked);
                slots.Add(platform);
                currentIndex++;
            }

            BuildTrayFrame(totalSlots);
            FitRowToScreen(totalSlots);
        }

        /// <summary>Slot sırasının ekrana sığması için uygulanan ölçek (1 = tam boy).</summary>
        public float RowFitScale => Mathf.Max(0.1f, transform.localScale.x);

        /// <summary>
        /// Çerçeveli sıra ekran genişliğini aşarsa (VIP + çok slot + "+") tüm sırayı orantılı küçültür; sığıyorsa tam boy.
        /// Slottaki bloklar da aynı oranda küçülür (MonsterCharacter.GetSlotFitScale bu ölçeği kullanır).
        /// </summary>
        private void FitRowToScreen(int cellCount)
        {
            float scale = 1f;
            Camera cam = Camera.main;
            if (cam != null && cam.orthographic && cellCount > 0)
            {
                TrayTileVisuals.GetFrameSize(Mathf.Max(0.1f, slotSpacing), cellCount, out _, out _, out float outerW, out _);
                float available = cam.orthographicSize * cam.aspect * 2f - 0.24f; // her iki yanda küçük pay
                if (outerW > available) scale = Mathf.Max(0.5f, available / outerW);
            }
            transform.localScale = Vector3.one * scale;

            // Slotta duran blokları yeni ölçeğe uyarla
            for (int i = 0; i < slots.Count; i++)
            {
                var sl = slots[i];
                if (sl == null || sl.currentMonster == null || !Application.isPlaying) continue;
                sl.currentMonster.transform.localScale = sl.currentMonster.GetSlotFitScale();
            }
        }

        private GameObject _trayFrameObj;

        /// <summary>
        /// Slot sırasının arkasına eski alt tahtadaki gibi çerçeve: koyu alt dudak, ön çerçeve ve gömülü taban.
        /// Hücre boyu = slotlar arası mesafe; slot sayısı değişince yeniden boyutlanır.
        /// </summary>
        private void BuildTrayFrame(int cellCount)
        {
            if (_trayFrameObj != null)
            {
                if (Application.isPlaying) Destroy(_trayFrameObj); else DestroyImmediate(_trayFrameObj);
                _trayFrameObj = null;
            }
            // Ezgi: slotlar toplu bir çerçevede değil, her biri kendi 3 katmanlı mor çerçevesinde (SlotPlatform kuruyor)
            if (cellCount <= 0 || SlotPlatform.UseIndividualFrames) return;

            _trayFrameObj = new GameObject("SlotTrayFrame");
            _trayFrameObj.transform.SetParent(transform, false);
            _trayFrameObj.transform.localPosition = Vector3.zero;

            float cell = Mathf.Max(0.1f, slotSpacing);
            TrayTileVisuals.GetFrameSize(cell, cellCount, out float innerW, out float innerH, out float outerW, out float outerH);
            float cornerR = TrayTileVisuals.FrameCornerRadius;
            float innerCornerR = Mathf.Max(0.03f, cornerR - TrayTileVisuals.FramePadding * 0.55f);
            var t = _trayFrameObj.transform;
            TrayTileVisuals.Part("FrameLowerBevel", t, new Vector3(0f, -TrayTileVisuals.FrameLowerLip, 0.08f), outerW, outerH, 0.045f, cornerR, TrayTileVisuals.FrameDark);
            TrayTileVisuals.Part("FrameMain", t, new Vector3(0f, 0f, 0.05f), outerW, outerH, 0.048f, cornerR, TrayTileVisuals.FrameFront);
            TrayTileVisuals.Part("TrayRecessedBed", t, new Vector3(0f, 0f, 0.02f), innerW, innerH, 0.04f, innerCornerR, TrayTileVisuals.Floor);
        }

        /// <summary>Çerçeve dahil slot sırasının yarı yüksekliği (merkezden üst kenara).</summary>
        private float FrameHalfHeight
        {
            get
            {
                if (SlotPlatform.UseIndividualFrames) return Mathf.Max(0.1f, slotSpacing) * SlotPlatform.FrameRatio * 0.5f;
                TrayTileVisuals.GetFrameSize(Mathf.Max(0.1f, slotSpacing), 1, out _, out _, out _, out float outerH);
                return outerH * 0.5f;
            }
        }

        /// <summary>
        /// "+" slotuna basıldığında onu normal slota çevirir, slotları kaydırarak ortalar
        /// ve sağda yeni tek bir "+" slotu belirmesini sağlar.
        /// </summary>
        public void UnlockSlot(SlotPlatform targetSlot)
        {
            if (targetSlot == null || !targetSlot.isLocked) return;

            // 1. Tıklanan '+' slotunu normal slota dönüştür
            targetSlot.slotType = SlotType.Regular;
            targetSlot.BuildSlotMesh();
            regularSlotCount = Mathf.Min(maxRegularSlotCount, regularSlotCount + 1);

            PuzzleSoundManager.Instance?.PlayUnlock();

            // 2. Eğer maksimum limite ulaşılmamışsa, en sağa yeni bir '+' slotu ekle
            bool canAddAnotherPlus = showAddSlotButton && regularSlotCount < maxRegularSlotCount;
            SlotPlatform newPlusPlatform = null;

            if (canAddAnotherPlus)
            {
                int newIdx = slots.Count;
                GameObject slotObj = new GameObject("Slot_Locked_Plus");
                slotObj.transform.SetParent(transform, false);

                // Başlangıçta yeni slotu mevcut son slotun hemen sağına koy
                float currentRightX = slots.Count > 0 ? slots[slots.Count - 1].transform.localPosition.x + slotSpacing : 0f;
                slotObj.transform.localPosition = new Vector3(currentRightX, 0f, 0f);

                newPlusPlatform = slotObj.AddComponent<SlotPlatform>();
                newPlusPlatform.Initialize(newIdx, frameMaterial, cavityMaterial, shadowMaterial, slotScale, SlotType.Locked);
                newPlusPlatform.transform.localScale = Vector3.zero;
                slots.Add(newPlusPlatform);
            }

            // 3. Tüm slotları yeni genişliğe göre pürüzsüzce kaydırarak ortala
            AnimateSlotPositions(0.32f);

            // Açılan slotun büyüme efekti, kaydırma tween'lerinden SONRA başlar: AnimateSlotPositions her slotta
            // DOKill çağırdığı için önce başlatılırsa yarıda kalıyor ve slot %75 boyutta takılı kalıyordu.
            targetSlot.transform.localScale = Vector3.one * 0.75f;
            targetSlot.transform.DOScale(Vector3.one, 0.30f).SetEase(Ease.OutBack);

            // 4. Yeni '+' slotunu tatlı bir yaylanma efektiyle sahneye çıkar
            if (newPlusPlatform != null)
            {
                newPlusPlatform.transform.DOScale(Vector3.one, 0.35f).SetEase(Ease.OutBack).SetDelay(0.08f);
            }

            OnAnySlotUnlocked?.Invoke(targetSlot);
        }

        /// <summary>
        /// Tüm slotları dok genişliğine göre ortalayarak pürüzsüzce yeni konumlarına kaydırır.
        /// </summary>
        public void AnimateSlotPositions(float duration = 0.30f)
        {
            int total = slots.Count;
            if (total == 0) return;
            BuildTrayFrame(total);
            FitRowToScreen(total);

            float totalWidth = (total - 1) * slotSpacing;
            float startX = -totalWidth * 0.5f;

            for (int i = 0; i < total; i++)
            {
                var sl = slots[i];
                if (sl == null) continue;

                sl.slotIndex = i;
                float targetX = startX + i * slotSpacing;
                // DOKill tüm tween'leri keser; yarıda kalan bir büyüme (açılan / yeni gelen slot) varsa slot küçük
                // kalmasın diye tam boyuta tamamlanır.
                bool wasGrowing = sl.transform.localScale.x < 0.999f;
                sl.transform.DOKill();
                if (wasGrowing && Application.isPlaying && sl.transform.localScale.x > 0.01f)
                    sl.transform.DOScale(Vector3.one, 0.22f).SetEase(Ease.OutBack);

                if (Application.isPlaying && duration > 0.01f)
                {
                    sl.transform.DOLocalMoveX(targetX, duration).SetEase(Ease.OutQuad).OnUpdate(() =>
                    {
                        if (sl.currentMonster != null)
                        {
                            sl.currentMonster.transform.position = sl.GetPlacementPosition();
                        }
                    });
                }
                else
                {
                    Vector3 pos = sl.transform.localPosition;
                    pos.x = targetX;
                    sl.transform.localPosition = pos;
                    if (sl.currentMonster != null)
                    {
                        sl.currentMonster.transform.position = sl.GetPlacementPosition();
                    }
                }
            }
        }

        public void OnSlotUnlocked(SlotPlatform platform)
        {
            if (platform == null) return;
            OnAnySlotUnlocked?.Invoke(platform);
        }

        /// <summary>
        /// Kilitli "+" slotunu açar.
        /// </summary>
        public bool UnlockNextLockedSlot()
        {
            for (int i = 0; i < slots.Count; i++)
            {
                var sl = slots[i];
                if (sl != null && sl.isLocked)
                {
                    UnlockSlot(sl);
                    return true;
                }
            }
            return false;
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
            float halfH = FrameHalfHeight * RowFitScale;
            float cy = transform.position.y;
            maxY = cy + halfH;
            minY = cy - halfH - TrayTileVisuals.FrameLowerLip * RowFitScale; // çerçevenin alt dudağı dahil
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

            float halfH = FrameHalfHeight * RowFitScale;
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
            // Öncelik 1: Normal açık slotlar (soldan sağa)
            for (int i = 0; i < slots.Count; i++)
            {
                var sl = slots[i];
                if (sl != null && !sl.isLocked && !sl.isVip && !sl.isOccupied && sl.currentMonster == null)
                    return sl;
            }
            // Öncelik 2: VIP slot boşsa kullanılabilir
            for (int i = 0; i < slots.Count; i++)
            {
                var sl = slots[i];
                if (sl != null && !sl.isLocked && sl.isVip && !sl.isOccupied && sl.currentMonster == null)
                    return sl;
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
