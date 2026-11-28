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

        public const int FixedSlotCount = 5;

        [Header("Slot Dock Configuration (Bus Traffic Fever Style)")]
        public bool slotsEnabled = true;
        public bool enableVipSlot = false;
        [Range(1, 8)] public int regularSlotCount = FixedSlotCount;
        public bool showAddSlotButton = false;
        [Range(4, 9)] public int maxRegularSlotCount = FixedSlotCount;

        [SerializeField, HideInInspector] private int slotLayoutVersion = 0;

        /// <summary>Geriye dönük uyumluluk için regular slot sayısını döndürür/ayarlar.</summary>
        public int slotCount
        {
            get => regularSlotCount;
            set => regularSlotCount = Mathf.Clamp(value, 1, maxRegularSlotCount);
        }

        public float slotSpacing = 0.72f;
        [Range(0.40f, 1.25f)] public float slotScale = 0.56f;
        public float gapBelowGlass = 0.28f;

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
            MigrateSlotLayoutIfNeeded();
            RebuildSlots();
        }

        private void OnEnable()
        {
            Instance = this;
            MigrateSlotLayoutIfNeeded();
            RebuildSlots();
        }

        private void MigrateSlotLayoutIfNeeded()
        {
            // v2: '+' slotu kaldırıldı; tüm seviyelerde sabit 5 slot düzeni
            if (slotLayoutVersion >= 2) return;
            slotLayoutVersion = 2;
            regularSlotCount = FixedSlotCount;
            maxRegularSlotCount = FixedSlotCount;
            showAddSlotButton = false;
            slotSpacing = 0.72f;
            slotScale = 0.56f;
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

            // Dok Sistemi: Sabit 5 slot (+ butonu olmadan; isteğe bağlı VIP slotu)
            regularSlotCount = FixedSlotCount;
            maxRegularSlotCount = FixedSlotCount;
            showAddSlotButton = false;
            int vipCount = enableVipSlot ? 1 : 0;
            int plusCount = 0;
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

            FitRowToScreen(totalSlots);
            BuildTrayFrame(totalSlots); // tabla genişliği satır ölçeğine göre ekrandan taşacak şekilde hesaplanır
            NotifyGlassExitChannels();
        }

        private void NotifyGlassExitChannels(ShapedGlassContainer glass = null)
        {
            if (glass == null)
            {
                glass = BallContainerManager.Instance != null
                    ? BallContainerManager.Instance.glassContainer
                    : FindFirstObjectByType<ShapedGlassContainer>();
            }
            if (glass != null && glass.HasPerSlotExitChannels)
            {
                glass.SyncExitChannelsToSlots(this);
            }
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
                float cell = Mathf.Max(0.1f, slotSpacing);
                // Sığdırma kontrolünde slotların kendi etkileşimli genişliğini baz al;
                // yamuk dok tablasının yan kulakları 4 slotta dar ekran kenarına kadar uzanabilir.
                float slotsWidth = (cellCount - 1) * cell + cell * 0.94f;
                float available = cam.orthographicSize * cam.aspect * 2f - 0.22f;
                if (slotsWidth > available) scale = Mathf.Max(0.5f, available / slotsWidth);
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
        /// Slot sırasının arkasındaki ayrı dok plakası artık birleşik kademeli alt arka plan
        /// (PuzzleGlassBackdrop) tarafından çizildiği için varsa eski SlotTrayFrame nesnesini temizler.
        /// </summary>
        private void BuildTrayFrame(int cellCount)
        {
            if (_trayFrameObj != null)
            {
                if (Application.isPlaying) Destroy(_trayFrameObj); else DestroyImmediate(_trayFrameObj);
                _trayFrameObj = null;
            }
            Transform leftover = transform.Find("SlotTrayFrame");
            if (leftover != null)
            {
                if (Application.isPlaying) Destroy(leftover.gameObject); else DestroyImmediate(leftover.gameObject);
            }
        }

        /// <summary>Çerçeve dahil slot sırasının yarı yüksekliği (merkezden üst kenara).</summary>
        private float FrameHalfHeight
        {
            get
            {
                if (SlotPlatform.UseIndividualFrames) return Mathf.Max(0.1f, slotSpacing) * SlotPlatform.FrameRatio * 0.5f;
                TrayTileVisuals.GetDockExtents(Mathf.Max(0.1f, slotSpacing), out float top, out _);
                return top; // camın altına hizalama tablanın üst kenarına göre yapılır
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
            FitRowToScreen(total);
            BuildTrayFrame(total);

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
            if (!Application.isPlaying || duration <= 0.01f)
            {
                NotifyGlassExitChannels();
            }
            else
            {
                DOVirtual.DelayedCall(duration, () => NotifyGlassExitChannels()).SetTarget(this);
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
            float cy = transform.position.y;
            if (SlotPlatform.UseIndividualFrames)
            {
                float halfH = FrameHalfHeight * RowFitScale;
                maxY = cy + halfH;
                minY = cy - halfH - TrayTileVisuals.FrameLowerLip * RowFitScale;
                return true;
            }
            TrayTileVisuals.GetDockExtents(Mathf.Max(0.1f, slotSpacing), out float top, out float bottom);
            maxY = cy + top * RowFitScale;
            minY = cy - (bottom + TrayTileVisuals.FrameLowerLip) * RowFitScale; // tablanın kalınlığı dahil
            return true;
        }

        /// <summary>Visible vertical bounds of the slot squares themselves (without dock bay margin).</summary>
        public bool TryGetSlotSquaresVerticalBounds(out float minY, out float maxY)
        {
            if (!slotsEnabled || !gameObject.activeInHierarchy || slots == null || slots.Count == 0)
            {
                minY = transform.position.y;
                maxY = transform.position.y;
                return false;
            }
            float cy = transform.position.y;
            float slotVis = TrayTileVisuals.SlotVisualSize(Mathf.Max(0.1f, slotSpacing));
            float halfSlot = slotVis * 0.5f * 0.86f * RowFitScale;
            minY = cy - halfSlot;
            maxY = cy + halfSlot;
            return true;
        }

        /// <summary>
        /// Kademeli arka planın (___/‾‾‾\___) slot sırasını saran yükseltilmiş orta bölüm sınırlarını dünya uzayında döndürür.
        /// </summary>
        public bool TryGetRaisedBayWorldBounds(out float centerX, out float bayHalfW, out float bayTopY, out float wingY)
        {
            centerX = transform.position.x;
            bayHalfW = 1.6f;
            bayTopY = transform.position.y + 0.45f;
            wingY = transform.position.y - 0.65f;

            if (!slotsEnabled || !gameObject.activeInHierarchy || slots == null || slots.Count == 0)
            {
                return false;
            }

            float minX = float.MaxValue;
            float maxX = float.MinValue;
            bool anyActive = false;
            for (int i = 0; i < slots.Count; i++)
            {
                var sl = slots[i];
                if (sl == null || !sl.gameObject.activeInHierarchy) continue;
                if (sl.transform.localScale.x <= 0.02f) continue;
                float x = sl.transform.position.x;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                anyActive = true;
            }

            if (!anyActive)
            {
                for (int i = 0; i < slots.Count; i++)
                {
                    var sl = slots[i];
                    if (sl == null) continue;
                    float x = sl.transform.position.x;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    anyActive = true;
                }
            }

            if (!anyActive) return false;

            float rowScale = RowFitScale;
            float slotVis = TrayTileVisuals.SlotVisualSize(Mathf.Max(0.1f, slotSpacing));
            // PuzzleSlotUI.shader spotHalf = 0.84 -> görünen slot yarı genişliği
            float slotVisHalf = slotVis * 0.5f * 0.84f * rowScale;
            float sidePad = 0.14f * rowScale;

            centerX = (minX + maxX) * 0.5f;
            bayHalfW = (maxX - minX) * 0.5f + slotVisHalf + sidePad;

            float cy = transform.position.y;
            TrayTileVisuals.GetDockExtents(Mathf.Max(0.1f, slotSpacing), out float top, out float bottom);
            bayTopY = cy + top * rowScale;
            wingY = cy - bottom * rowScale;
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
            float minCollarGap = glass.HasPerSlotExitChannels
                ? 0.62f
                : ((glass.showBottomExitCollar && (glass.IsPoolShape || glass.IsTrackShape)) ? 0.50f : 0.30f);
            float topGap = Mathf.Max(minCollarGap, gapBelowGlass);
            float targetY = glassBottomY - topGap - halfH;
            // Kısa cam şekillerinde slot tablası ekran ortasından yukarı fazla çıkmasın
            const float maxDockCenterY = -0.20f;
            if (targetY > maxDockCenterY)
            {
                targetY = maxDockCenterY;
            }
            // Çok uzun camlarda bile yaka ile dok arasında en az 0.32 birim nefes payı korunsun
            float maxAllowedY = glassBottomY - (glass.HasPerSlotExitChannels ? 0.48f : 0.32f) - halfH;
            if (targetY > maxAllowedY)
            {
                targetY = maxAllowedY;
            }

            transform.position = new Vector3(glassCenterX, targetY, 0.05f);
            NotifyGlassExitChannels(glass);
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

        /// <summary>
        /// 2'li bağlı kutular için yan yana duran ilk 2 boş açık slotu bulur.
        /// Önce normal slot çiftlerini, yoksa VIP dahil komşu açık slot çiftini dener.
        /// </summary>
        public bool TryGetAdjacentEmptySlotPair(out SlotPlatform leftSlot, out SlotPlatform rightSlot)
        {
            leftSlot = null;
            rightSlot = null;
            if (slots == null || slots.Count < 2) return false;

            // Öncelik 1: İkisi de normal, açık ve boş yan yana slotlar
            for (int i = 0; i < slots.Count - 1; i++)
            {
                var a = slots[i];
                var b = slots[i + 1];
                if (a != null && b != null &&
                    !a.isLocked && !b.isLocked &&
                    !a.isVip && !b.isVip &&
                    !a.isOccupied && a.currentMonster == null &&
                    !b.isOccupied && b.currentMonster == null)
                {
                    leftSlot = a;
                    rightSlot = b;
                    return true;
                }
            }

            // Öncelik 2: VIP slot dahil yan yana açık ve boş slot çifti
            for (int i = 0; i < slots.Count - 1; i++)
            {
                var a = slots[i];
                var b = slots[i + 1];
                if (a != null && b != null &&
                    !a.isLocked && !b.isLocked &&
                    !a.isOccupied && a.currentMonster == null &&
                    !b.isOccupied && b.currentMonster == null)
                {
                    leftSlot = a;
                    rightSlot = b;
                    return true;
                }
            }

            return false;
        }

        public bool HasAdjacentEmptySlotPair() => TryGetAdjacentEmptySlotPair(out _, out _);

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
