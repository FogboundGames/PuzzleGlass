using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using DG.Tweening;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace PuzzleGlass
{
    public class MonsterDragController : MonoBehaviour
    {
        public static MonsterDragController Instance { get; private set; }

        [Header("Drag Settings")]
        public float snapRadius = 0.78f;
        public float dragHeightZ = -0.35f;
        public float dragFollowSpeed = 35f;
        public float dragLiftScale = 1.15f;

        [Header("State (Read Only)")]
        [SerializeField] private MonsterCharacter _draggedMonster;
        [SerializeField] private SlotPlatform _hoveredSlot;
        [SerializeField] private bool _isDragging = false;

        private Vector3 _dragOriginPos;
        private Vector3 _dragStartScale = Vector3.one;
        private Vector2Int _dragStartAnchor;
        private MonsterCharacter _draggedPartner;
        private Vector3 _dragPartnerStartScale = Vector3.one;
        private Vector3 _dragOffset;
        private Vector2 _pointerDownScreenPos;
        private float _pointerDownTime;
        private bool _hasMovedBeyondTapThreshold = false;
        private bool _isLiftedOutOfGrid = false;
        private Camera _mainCam;
        private PointerEventData _uiRaycastEvent;
        private readonly List<RaycastResult> _uiRaycastResults = new List<RaycastResult>(16);

        public event Action<MonsterCharacter, SlotPlatform> OnMonsterPlaced;
        public event Action<MonsterCharacter> OnMonsterReturnedToTray;

        public bool IsBlockCurrentlyDragged(MonsterCharacter block)
        {
            return _isDragging && block != null && (_draggedMonster == block || _draggedPartner == block);
        }

        private void Awake()
        {
            Instance = this;
            _mainCam = Camera.main;
        }

        private void Update()
        {
            if (_mainCam == null) _mainCam = Camera.main;
            if (_mainCam == null) return;

            Vector2 pointerScreenPos;
            bool pointerDown = GetPointerDown(out pointerScreenPos);
            bool pointerHeld = GetPointerHeld(out pointerScreenPos);
            bool pointerUp = GetPointerUp();

            if (pointerDown)
            {
                HandlePointerDown(pointerScreenPos);
            }

            if (_isDragging && !pointerDown && pointerHeld)
            {
                HandlePointerDrag(pointerScreenPos);
            }

            if (_isDragging && pointerUp)
            {
                // Hızlı hareketlerde son karedeki parmak hareketi kaybolmasın: bırakmadan önce
                // son konumla bir kez daha kaydır, sonra yerleştir.
                if (GetPointerPosition(out Vector2 releasePos))
                {
                    if (!pointerDown) HandlePointerDrag(releasePos);
                    pointerScreenPos = releasePos;
                }
                HandlePointerUp(pointerScreenPos);
            }
        }

        private bool IsPointerOverBlockingUI(Vector2 screenPos)
        {
            if (EventSystem.current == null) return false;
            if (_uiRaycastEvent == null) _uiRaycastEvent = new PointerEventData(EventSystem.current);
            _uiRaycastEvent.Reset();
            _uiRaycastEvent.position = screenPos;
            _uiRaycastResults.Clear();
            EventSystem.current.RaycastAll(_uiRaycastEvent, _uiRaycastResults);
            return _uiRaycastResults.Count > 0;
        }

        private void HandlePointerDown(Vector2 screenPos)
        {
            if (IsPointerOverBlockingUI(screenPos))
            {
                return;
            }
            if (PuzzleGlassGameManager.Instance != null &&
                (PuzzleGlassGameManager.Instance.isLevelCompleted || PuzzleGlassGameManager.Instance.isLevelFailed))
            {
                return;
            }
            // Level geçişi sırasında (toplar ekranı kaplarken / düşerken) dokunma yok
            if (LevelTransitionController.IsTransitioning)
            {
                return;
            }

            Ray ray = _mainCam.ScreenPointToRay(screenPos);
            RaycastHit[] allHits = Physics.RaycastAll(ray, 100f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            Vector3 worldTouch = ScreenToWorldOnPlane(screenPos, 0f);

            bool spotlightActive = TutorialOverlay.Instance != null && TutorialOverlay.Instance.IsSpotlightTutorialActive;

            // 0. Check if a locked "+" slot platform was tapped directly
            if (!spotlightActive)
            {
                for (int i = 0; i < allHits.Length; i++)
                {
                    var hitSlot = allHits[i].collider.GetComponentInParent<SlotPlatform>();
                    if (hitSlot != null && hitSlot.isLocked)
                    {
                        hitSlot.TryUnlockSlot();
                        return;
                    }
                }
            }

            MonsterCharacter selectedMonster = null;

            // 1. Raycast for Monster BoxCollider (includes both staging monsters AND slotted monsters!)
            for (int i = 0; i < allHits.Length; i++)
            {
                var m = allHits[i].collider.GetComponentInParent<MonsterCharacter>();
                if (m != null)
                {
                    selectedMonster = m;
                    break;
                }
            }

            // 2. Proximity fallback: tight tolerance (< 1 grid cell) so tapping only picks the intended crate
            if (selectedMonster == null)
            {
                float closestDist = 0.40f;

                var allMonsters = FindObjectsByType<MonsterCharacter>(FindObjectsSortMode.None);
                foreach (var m in allMonsters)
                {
                    if (m == null || m.IsCompleted || m.remainingCount <= 0 || !m.gameObject.activeInHierarchy) continue;
                    float d = Vector2.Distance(
                        new Vector2(m.transform.position.x, m.transform.position.y),
                        new Vector2(worldTouch.x, worldTouch.y));
                    if (d < closestDist)
                    {
                        closestDist = d;
                        selectedMonster = m;
                    }
                }
            }

            if (selectedMonster == null || selectedMonster.IsCompleted || selectedMonster.remainingCount <= 0) return;

            if (TutorialOverlay.Instance != null && !TutorialOverlay.Instance.CanInteractWithBlock(selectedMonster))
            {
                return;
            }

            if (selectedMonster.IsBlockedIncludingLink)
            {
                selectedMonster.PlayBlockedMechanicFeedback();
                return;
            }

            MonsterCharacter partner = selectedMonster.HasLinkedPartner ? selectedMonster.linkedPartner : null;

            // Lock slotted/in-flight boxes, and lock in-grid boxes only while balls are actively flying into them
            if (selectedMonster.assignedSlot != null || selectedMonster.IsMovingToSlot ||
                (partner != null && (partner.assignedSlot != null || partner.IsMovingToSlot)))
            {
                selectedMonster.PlayLockedInSlotFeedback();
                return;
            }
            if (selectedMonster.isCollectingInGrid)
            {
                if (BallCollectionController.Instance != null &&
                    BallCollectionController.Instance.HasBallsInTransitFor(selectedMonster))
                {
                    selectedMonster.PlayLockedInSlotFeedback();
                    return;
                }
                selectedMonster.PauseInGridCollection();
            }

            selectedMonster.EnsureStagingHomeRecorded();
            partner?.EnsureStagingHomeRecorded();

            _isDragging = true;
            _draggedMonster = selectedMonster;
            _draggedPartner = partner;
            _dragStartAnchor = selectedMonster.gridAnchor;
            _pointerDownScreenPos = screenPos;
            _pointerDownTime = Time.time;
            _hasMovedBeyondTapThreshold = false;
            _isLiftedOutOfGrid = false;

            _dragOriginPos = _draggedMonster.transform.position;
            _dragStartScale = (_draggedMonster.hasStagingHome && _draggedMonster.stagingHomeScale.sqrMagnitude > 0.001f)
                ? _draggedMonster.stagingHomeScale
                : _draggedMonster.transform.localScale;

            if (_draggedPartner != null)
            {
                _dragPartnerStartScale = (_draggedPartner.hasStagingHome && _draggedPartner.stagingHomeScale.sqrMagnitude > 0.001f)
                    ? _draggedPartner.stagingHomeScale
                    : _draggedPartner.transform.localScale;
                _draggedPartner.StopAllCoroutines();
                _draggedPartner.transform.DOKill();
            }

            Vector3 pointerWorld = ScreenToWorldOnPlane(screenPos, 0f);
            _dragOffset = _dragOriginPos - pointerWorld;
            _dragOffset.z = 0f;

            _draggedMonster.StopAllCoroutines();
            _draggedMonster.transform.DOKill();
        }

        private void HandlePointerDrag(Vector2 screenPos)
        {
            if (_draggedMonster == null)
            {
                _isDragging = false;
                return;
            }

            if (TutorialOverlay.Instance != null && TutorialOverlay.Instance.IsSpotlightTutorialActive)
            {
                return;
            }

            if (!_hasMovedBeyondTapThreshold)
            {
                if (Vector2.Distance(screenPos, _pointerDownScreenPos) > 16f)
                {
                    _hasMovedBeyondTapThreshold = true;
                    _draggedMonster.SetDragHighlight(true);
                    _draggedPartner?.SetDragHighlight(true);
                }
                else
                {
                    return;
                }
            }

            var grid = StagingGridBoard.Instance;
            var allMonsters = PuzzleGlassGameManager.Instance != null ? PuzzleGlassGameManager.Instance.activeMonsters : null;

            if (grid != null)
            {
                Vector3 pointerWorldGrid = ScreenToWorldOnPlane(screenPos, 0f);

                // Slota sürükleme yok: sürükleme yalnızca alt ızgara içinde adım adım kaydırma yapar
                _isLiftedOutOfGrid = false;

                Vector3 desiredCenter = pointerWorldGrid + _dragOffset;
                desiredCenter.z = 0f;

                Vector2Int desiredAnchor = grid.WorldCenterToNearestAnchor(desiredCenter, _draggedMonster.blockShape);
                Vector2Int stepped = grid.SlideAnchorStepByStep(_draggedMonster, desiredAnchor, allMonsters);
                if (stepped != _draggedMonster.gridAnchor)
                {
                    Vector2Int stepDelta = stepped - _draggedMonster.gridAnchor;
                    _draggedMonster.gridAnchor = stepped;
                    _draggedMonster.stagingColumn = stepped.x;
                    _draggedMonster.stagingRow = stepped.y;
                    if (_draggedPartner != null)
                    {
                        _draggedPartner.gridAnchor += stepDelta;
                        _draggedPartner.stagingColumn = _draggedPartner.gridAnchor.x;
                        _draggedPartner.stagingRow = _draggedPartner.gridAnchor.y;
                    }
                    grid.RefreshAllGridBlockHighlightStates(allMonsters);
                    PuzzleSoundManager.Instance?.PlayGridSlide();
                }

                Vector3 snappedWorld = grid.GetWorldPositionForAnchor(_draggedMonster.gridAnchor, _draggedMonster.blockShape);
                Vector3 previewPos = ComputeConstrainedGridPreview(grid, _draggedMonster, snappedWorld, desiredCenter, allMonsters);
                Vector3 previewOffset = previewPos - snappedWorld;
                previewPos.z = -0.08f;

                _draggedMonster.transform.position = Vector3.Lerp(
                    _draggedMonster.transform.position,
                    previewPos,
                    Mathf.Max(dragFollowSpeed, 38f) * Time.deltaTime);
                _draggedMonster.transform.localScale = Vector3.Lerp(
                    _draggedMonster.transform.localScale,
                    _dragStartScale,
                    22f * Time.deltaTime);

                if (_draggedPartner != null)
                {
                    Vector3 partnerSnapped = grid.GetWorldPositionForAnchor(_draggedPartner.gridAnchor, _draggedPartner.blockShape);
                    Vector3 partnerPreview = partnerSnapped + new Vector3(previewOffset.x, previewOffset.y, 0f);
                    partnerPreview.z = -0.08f;
                    _draggedPartner.transform.position = Vector3.Lerp(
                        _draggedPartner.transform.position,
                        partnerPreview,
                        Mathf.Max(dragFollowSpeed, 38f) * Time.deltaTime);
                    _draggedPartner.transform.localScale = Vector3.Lerp(
                        _draggedPartner.transform.localScale,
                        _dragPartnerStartScale,
                        22f * Time.deltaTime);
                }
                return;
            }
        }

        private void UpdateHoveredSlotHighlight(SlotPlatform bestSlot)
        {
            if (bestSlot != _hoveredSlot)
            {
                if (_hoveredSlot != null)
                {
                    _hoveredSlot.SetHighlight(false);
                }

                _hoveredSlot = bestSlot;

                if (_hoveredSlot != null)
                {
                    _hoveredSlot.SetHighlight(true);
                }
            }
        }

        private Vector3 ComputeConstrainedGridPreview(
            StagingGridBoard grid,
            MonsterCharacter block,
            Vector3 anchorWorld,
            Vector3 desiredWorld,
            List<MonsterCharacter> allMonsters)
        {
            Vector3 delta = desiredWorld - anchorWorld;
            float maxOpen = grid.cellSize * 0.45f;
            float maxBlocked = 0.022f;

            float minX = grid.CanPlaceBlockAt(block, block.gridAnchor + Vector2Int.left, allMonsters) ? -maxOpen : -maxBlocked;
            float maxX = grid.CanPlaceBlockAt(block, block.gridAnchor + Vector2Int.right, allMonsters) ? maxOpen : maxBlocked;
            float minY = grid.CanPlaceBlockAt(block, block.gridAnchor + Vector2Int.down, allMonsters) ? -maxOpen : -maxBlocked;
            float maxY = grid.CanPlaceBlockAt(block, block.gridAnchor + Vector2Int.up, allMonsters) ? maxOpen : maxBlocked;

            float cx = Mathf.Clamp(delta.x, minX, maxX);
            float cy = Mathf.Clamp(delta.y, minY, maxY);
            return new Vector3(anchorWorld.x + cx, anchorWorld.y + cy, 0f);
        }

        private void HandlePointerUp(Vector2 screenPos)
        {
            if (_draggedMonster == null)
            {
                _isDragging = false;
                return;
            }

            MonsterCharacter monster = _draggedMonster;
            MonsterCharacter partner = _draggedPartner;
            SlotPlatform targetSlot = _hoveredSlot;
            bool wasLifted = _isLiftedOutOfGrid;

            UpdateHoveredSlotHighlight(null);

            _draggedMonster = null;
            _draggedPartner = null;
            _isDragging = false;
            _isLiftedOutOfGrid = false;

            var grid = StagingGridBoard.Instance;
            var allMonsters = PuzzleGlassGameManager.Instance != null ? PuzzleGlassGameManager.Instance.activeMonsters : null;

            monster.SetDragHighlight(false);
            partner?.SetDragHighlight(false);

            bool isQuickTap = !_hasMovedBeyondTapThreshold;
            bool movedCell = monster.gridAnchor != _dragStartAnchor;
            bool slotsActive = SlotSystem.IsActive;

            if (slotsActive)
            {
                bool wantsSlotMove = isQuickTap;
                bool exitClear = grid == null || grid.IsBlockExitClearToSlots(monster, allMonsters);

                if (wantsSlotMove && exitClear)
                {
                    if (partner != null && monster.HasLinkedPartner)
                    {
                        if (SlotSystem.Instance.TryGetAdjacentEmptySlotPair(out SlotPlatform leftSlot, out SlotPlatform rightSlot))
                        {
                            PlaceLinkedPairIntoSlots(monster, partner, leftSlot, rightSlot);
                            grid?.RefreshAllGridBlockHighlightStates(allMonsters);
                            PuzzleGlassGameManager.Instance?.RegisterMove();
                            return;
                        }
                    }
                    else
                    {
                        SlotPlatform destSlot = SlotSystem.Instance.GetFirstEmptySlot();
                        if (destSlot != null)
                        {
                            PlaceMonsterIntoSlot(monster, destSlot, monster.GetSlotFitScale());
                            grid?.RefreshAllGridBlockHighlightStates(allMonsters);
                            PuzzleGlassGameManager.Instance?.RegisterMove();
                            return;
                        }
                    }
                }

                // Either staying inside grid after sliding, blocked path, or insufficient / non-adjacent empty slots -> snap back
                if (grid != null)
                {
                    Vector3 finalPos = grid.GetWorldPositionForAnchor(monster.gridAnchor, monster.blockShape);
                    bool atTopExit = grid.IsBlockAtTopExit(monster);
                    monster.armedAtTopEdge = atTopExit;
                    monster.SetGridState(monster.gridAnchor, finalPos, _dragStartScale, monster.GetSlotFitScale(), exitClear, animateMove: !isQuickTap || wasLifted);
                    if (partner != null)
                    {
                        Vector3 pFinalPos = grid.GetWorldPositionForAnchor(partner.gridAnchor, partner.blockShape);
                        partner.armedAtTopEdge = grid.IsBlockAtTopExit(partner);
                        partner.SetGridState(partner.gridAnchor, pFinalPos, _dragPartnerStartScale, partner.GetSlotFitScale(), exitClear, animateMove: !isQuickTap || wasLifted);
                    }
                    if (wantsSlotMove && !exitClear)
                    {
                        monster.PlayLockedInSlotFeedback();
                    }
                    else if (wantsSlotMove && exitClear)
                    {
                        // All slots full or no 2 adjacent empty slots for linked pair
                        PuzzleSoundManager.Instance?.PlayBlocked();
                        monster.PlayLockedInSlotFeedback();
                    }
                    else if (!isQuickTap)
                    {
                        PuzzleSoundManager.Instance?.PlayBlockSnap();
                    }
                    grid.RefreshAllGridBlockHighlightStates(allMonsters);
                    if (movedCell) PuzzleGlassGameManager.Instance?.RegisterMove();
                }
                else
                {
                    monster.ReturnToStagingHome(() => OnMonsterReturnedToTray?.Invoke(monster));
                    partner?.ReturnToStagingHome(() => OnMonsterReturnedToTray?.Invoke(partner));
                }

                return;
            }

            // Legacy non-slot mode: settle visually onto valid grid anchor
            if (grid != null)
            {
                Vector3 finalPos = grid.GetWorldPositionForAnchor(monster.gridAnchor, monster.blockShape);
                bool atTopExit = grid.IsBlockAtTopExit(monster);
                monster.armedAtTopEdge = atTopExit;
                monster.SetGridState(monster.gridAnchor, finalPos, _dragStartScale, monster.GetSlotFitScale(), atTopExit, animateMove: !isQuickTap);
                if (isQuickTap)
                {
                    monster.transform.localScale = _dragStartScale;
                }
                else
                {
                    PuzzleSoundManager.Instance?.PlayBlockSnap();
                }
                grid.RefreshAllGridBlockHighlightStates(allMonsters);
            }

            // 1. Quick Tap OR sliding/bringing a block up to the top exit row:
            // Starts direct in-grid collection automatically if at the top row & matching bottom ball color!
            bool startedCollection = false;
            if (isQuickTap || wasLifted)
            {
                startedCollection = monster.TryStartInGridCollection(playDenialFeedback: true);
            }
            else if (grid != null && grid.IsBlockAtTopExit(monster))
            {
                startedCollection = monster.TryStartInGridCollection(playDenialFeedback: false);
            }

            if (movedCell || startedCollection) PuzzleGlassGameManager.Instance?.RegisterMove();

            // 2. Fallback if grid is missing
            if (grid == null)
            {
                monster.ReturnToStagingHome(() => OnMonsterReturnedToTray?.Invoke(monster));
            }
        }

        private void PlaceLinkedPairIntoSlots(MonsterCharacter a, MonsterCharacter b, SlotPlatform leftSlot, SlotPlatform rightSlot)
        {
            if (a == null || b == null || leftSlot == null || rightSlot == null) return;

            // Yatay bağlıysa soldaki -> leftSlot, sağdaki -> rightSlot; dikey bağlıysa üstteki/öndeki -> leftSlot, alttaki -> rightSlot
            MonsterCharacter first, second;
            if (a.gridAnchor.x != b.gridAnchor.x)
            {
                bool aIsLeft = a.gridAnchor.x < b.gridAnchor.x;
                first = aIsLeft ? a : b;
                second = aIsLeft ? b : a;
            }
            else
            {
                bool aIsTop = a.gridAnchor.y >= b.gridAnchor.y;
                first = aIsTop ? a : b;
                second = aIsTop ? b : a;
            }

            // İkisini de aynı anda yola çıkar ve slotları ayır ki sütun kuyrukları iki hücreyi birden boş görsün
            first.SetMovingToSlot(true);
            second.SetMovingToSlot(true);
            leftSlot.ReserveSlot(first);
            rightSlot.ReserveSlot(second);
            PuzzleSoundManager.Instance?.PlayBlockSnap();

            if (PuzzleGlassGameManager.Instance != null)
            {
                PuzzleGlassGameManager.Instance.OnStagingBoxPlacedInSlot(first);
            }

            LaunchMonsterFlightToSlot(first, leftSlot, first.GetSlotFitScale(), playSnapSound: false, notifyGameManager: false);
            LaunchMonsterFlightToSlot(second, rightSlot, second.GetSlotFitScale(), playSnapSound: false, notifyGameManager: false);
        }

        private void PlaceMonsterIntoSlot(MonsterCharacter placedMonster, SlotPlatform targetSlot, Vector3 baseScale)
        {
            if (placedMonster == null || targetSlot == null) return;

            placedMonster.SetMovingToSlot(true);
            targetSlot.ReserveSlot(placedMonster);
            LaunchMonsterFlightToSlot(placedMonster, targetSlot, baseScale, playSnapSound: true, notifyGameManager: true);
        }

        private void LaunchMonsterFlightToSlot(
            MonsterCharacter placedMonster,
            SlotPlatform targetSlot,
            Vector3 baseScale,
            bool playSnapSound,
            bool notifyGameManager)
        {
            if (placedMonster == null || targetSlot == null) return;

            Vector3 slotPlacePos = targetSlot.GetPlacementPosition();
            Vector3 startPos = placedMonster.transform.position;
            Vector3 startScale = placedMonster.transform.localScale;
            Vector3 peakScale = new Vector3(
                Mathf.Max(startScale.x, baseScale.x),
                Mathf.Max(startScale.y, baseScale.y),
                Mathf.Max(startScale.z, baseScale.z)) * 1.14f;

            float dx = slotPlacePos.x - startPos.x;
            float bankTilt = Mathf.Abs(dx) > 0.08f ? Mathf.Clamp(-dx * 11f, -14f, 14f) : 5f;
            Color boxColor = PuzzleColorPalette.GetPrimaryColor(placedMonster.colorType);
            float fxScale = Mathf.Max(0.45f, Mathf.Max(startScale.x, baseScale.x));

            if (playSnapSound) PuzzleSoundManager.Instance?.PlayBlockSnap();

            if (notifyGameManager && PuzzleGlassGameManager.Instance != null)
            {
                PuzzleGlassGameManager.Instance.OnStagingBoxPlacedInSlot(placedMonster);
            }

            placedMonster.transform.DOKill();
            Transform bodyPivot = placedMonster.bodyPivot;
            Vector3 origBodyScale = placedMonster.OriginalBodyScale;
            if (origBodyScale.sqrMagnitude <= 0.001f) origBodyScale = Vector3.one;
            if (bodyPivot != null)
            {
                bodyPivot.DOKill(false);
                bodyPivot.localPosition = Vector3.zero;
                bodyPivot.localRotation = Quaternion.identity;
                bodyPivot.localScale = origBodyScale;
            }

            const float anticDur = 0.055f;
            const float flyDur = 0.27f;

            Sequence snapSeq = DOTween.Sequence();
            if (bodyPivot != null)
            {
                snapSeq.Append(bodyPivot.DOScale(new Vector3(origBodyScale.x * 1.10f, origBodyScale.y * 0.85f, origBodyScale.z * 1.04f), anticDur).SetEase(Ease.OutSine));
                snapSeq.Join(bodyPivot.DOLocalMoveY(-0.022f, anticDur).SetEase(Ease.OutSine));
            }
            else
            {
                snapSeq.Append(placedMonster.transform.DOScale(startScale * 0.94f, anticDur).SetEase(Ease.OutSine));
            }

            snapSeq.AppendCallback(() =>
            {
                if (placedMonster == null) return;
                placedMonster.SetSlotFlightGlow(0.65f);
                Transform trailTarget = bodyPivot != null ? bodyPivot : placedMonster.transform;
                BlockClearCelebration.PlaySlotFlightTrailFx(trailTarget, boxColor, fxScale, flyDur);
            });

            // Parabolic leap into slot + peak pop scale
            snapSeq.Append(placedMonster.transform.DOJump(slotPlacePos, jumpPower: 0.44f, numJumps: 1, duration: flyDur).SetEase(Ease.InOutQuad));

            Sequence scaleSeq = DOTween.Sequence();
            scaleSeq.Append(placedMonster.transform.DOScale(peakScale, flyDur * 0.42f).SetEase(Ease.OutQuad));
            scaleSeq.Append(placedMonster.transform.DOScale(baseScale, flyDur * 0.58f).SetEase(Ease.InOutQuad));
            snapSeq.Join(scaleSeq);

            if (bodyPivot != null)
            {
                Sequence bodyAirSeq = DOTween.Sequence();
                bodyAirSeq.Append(bodyPivot.DOScale(new Vector3(origBodyScale.x * 0.92f, origBodyScale.y * 1.11f, origBodyScale.z), flyDur * 0.40f).SetEase(Ease.OutQuad));
                bodyAirSeq.Join(bodyPivot.DOLocalRotate(new Vector3(0f, 0f, bankTilt), flyDur * 0.40f).SetEase(Ease.OutQuad));
                bodyAirSeq.Join(bodyPivot.DOLocalMoveY(0f, flyDur * 0.25f).SetEase(Ease.OutQuad));
                bodyAirSeq.Append(bodyPivot.DOScale(origBodyScale, flyDur * 0.42f).SetEase(Ease.InOutSine));
                bodyAirSeq.Join(bodyPivot.DOLocalRotate(new Vector3(0f, 0f, -bankTilt * 0.22f), flyDur * 0.42f).SetEase(Ease.InOutSine));
                bodyAirSeq.Append(bodyPivot.DOLocalRotate(Vector3.zero, flyDur * 0.18f).SetEase(Ease.OutSine));
                snapSeq.Join(bodyAirSeq);

                Sequence zArcSeq = DOTween.Sequence();
                zArcSeq.Append(bodyPivot.DOLocalMoveZ(-0.22f, flyDur * 0.45f).SetEase(Ease.OutQuad));
                zArcSeq.Append(bodyPivot.DOLocalMoveZ(0f, flyDur * 0.55f).SetEase(Ease.InQuad));
                snapSeq.Join(zArcSeq);
            }

            snapSeq.SetLink(placedMonster.gameObject, LinkBehaviour.KillOnDisable);
            snapSeq.OnComplete(() =>
            {
                if (placedMonster == null) return;
                placedMonster.SetSlotFlightGlow(0f);
                placedMonster.SetMovingToSlot(false);
                targetSlot.AttachMonster(placedMonster);
                placedMonster.PlaySlotLandingSquash();
                BlockClearCelebration.PlaySlotLandingBackFx(slotPlacePos, boxColor, fxScale);
                PuzzleSoundManager.Instance?.PlayBallTap(0.45f);
                BlockClearCelebration.VibrateSlotPlace();

                TutorialOverlay.Instance?.NotifySpotlightBlockPlaced(placedMonster);
                OnMonsterPlaced?.Invoke(placedMonster, targetSlot);

                if (SandMonsterFlowBridge.Instance != null)
                {
                    SandMonsterFlowBridge.Instance.NotifyMonsterPlacedInSlot(placedMonster);
                }

                if (BallCollectionController.Instance != null)
                {
                    BallCollectionController.Instance.OnMonsterPlacedInSlot(placedMonster);
                }
            });
        }

        /// <summary>
        /// Programmatically places an unobstructed grid block directly into the first available open slot.
        /// Used by automatic end-of-level slot cascade.
        /// </summary>
        public bool TryAutoPlaceInFirstEmptySlot(MonsterCharacter monster)
        {
            if (monster == null || monster.IsCompleted || monster.remainingCount <= 0 || !monster.gameObject.activeInHierarchy) return false;
            if (monster.assignedSlot != null || monster.IsMovingToSlot || monster.IsBlockedIncludingLink) return false;
            if (!SlotSystem.IsActive || SlotSystem.Instance == null) return false;

            if (monster.HasLinkedPartner)
            {
                MonsterCharacter partner = monster.linkedPartner;
                if (partner == null || partner.assignedSlot != null || partner.IsMovingToSlot || partner.IsCompleted || partner.remainingCount <= 0)
                    return false;
                if (!SlotSystem.Instance.TryGetAdjacentEmptySlotPair(out SlotPlatform leftSlot, out SlotPlatform rightSlot))
                    return false;

                if (_isDragging && (_draggedMonster == monster || _draggedMonster == partner))
                {
                    _isDragging = false;
                    _draggedMonster = null;
                    _draggedPartner = null;
                    _isLiftedOutOfGrid = false;
                    UpdateHoveredSlotHighlight(null);
                }

                monster.SetDragHighlight(false);
                partner.SetDragHighlight(false);
                PlaceLinkedPairIntoSlots(monster, partner, leftSlot, rightSlot);
                var gridL = StagingGridBoard.Instance;
                var allL = PuzzleGlassGameManager.Instance != null ? PuzzleGlassGameManager.Instance.activeMonsters : null;
                gridL?.RefreshAllGridBlockHighlightStates(allL);
                return true;
            }

            SlotPlatform destSlot = SlotSystem.Instance.GetFirstEmptySlot();
            if (destSlot == null) return false;

            if (_isDragging && _draggedMonster == monster)
            {
                _isDragging = false;
                _draggedMonster = null;
                _draggedPartner = null;
                _isLiftedOutOfGrid = false;
                UpdateHoveredSlotHighlight(null);
            }

            monster.SetDragHighlight(false);
            PlaceMonsterIntoSlot(monster, destSlot, monster.GetSlotFitScale());
            var grid = StagingGridBoard.Instance;
            var allMonsters = PuzzleGlassGameManager.Instance != null ? PuzzleGlassGameManager.Instance.activeMonsters : null;
            grid?.RefreshAllGridBlockHighlightStates(allMonsters);
            return true;
        }

        private SlotPlatform FindClosestSlot(Vector3 monsterPos)
        {
            SlotSystem slotSys = SlotSystem.Instance.OrNull() ?? FindFirstObjectByType<SlotSystem>();
            if (slotSys == null || slotSys.slots == null) return null;

            SlotPlatform closest = null;
            float closestDist = snapRadius;

            for (int i = 0; i < slotSys.slots.Count; i++)
            {
                var slot = slotSys.slots[i];
                if (slot == null || slot.isLocked || slot.isOccupied || slot.currentMonster != null) continue;

                float dist = Vector2.Distance(
                    new Vector2(monsterPos.x, monsterPos.y),
                    new Vector2(slot.transform.position.x, slot.transform.position.y));

                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = slot;
                }
            }

            return closest;
        }

        private Vector3 ScreenToWorldOnPlane(Vector2 screenPos, float zPlane)
        {
            if (_mainCam == null) return Vector3.zero;

            Ray ray = _mainCam.ScreenPointToRay(screenPos);
            Plane plane = new Plane(Vector3.forward, new Vector3(0f, 0f, zPlane));

            if (plane.Raycast(ray, out float enter))
            {
                return ray.GetPoint(enter);
            }

            Vector3 world = _mainCam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, -_mainCam.transform.position.z + zPlane));
            world.z = zPlane;
            return world;
        }

        private bool GetPointerDown(out Vector2 screenPos)
        {
            screenPos = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                screenPos = Mouse.current.position.ReadValue();
                return true;
            }
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            {
                screenPos = Touchscreen.current.primaryTouch.position.ReadValue();
                return true;
            }
            if (Pointer.current != null && Pointer.current.press.wasPressedThisFrame)
            {
                screenPos = Pointer.current.position.ReadValue();
                return true;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetMouseButtonDown(0))
            {
                screenPos = Input.mousePosition;
                return true;
            }
            if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
            {
                screenPos = Input.GetTouch(0).position;
                return true;
            }
#endif
            return false;
        }

        private bool GetPointerHeld(out Vector2 screenPos)
        {
            screenPos = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && Mouse.current.leftButton.isPressed)
            {
                screenPos = Mouse.current.position.ReadValue();
                return true;
            }
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
            {
                screenPos = Touchscreen.current.primaryTouch.position.ReadValue();
                return true;
            }
            if (Pointer.current != null && Pointer.current.press.isPressed)
            {
                screenPos = Pointer.current.position.ReadValue();
                return true;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetMouseButton(0))
            {
                screenPos = Input.mousePosition;
                return true;
            }
            if (Input.touchCount > 0 && (Input.GetTouch(0).phase == TouchPhase.Moved || Input.GetTouch(0).phase == TouchPhase.Stationary))
            {
                screenPos = Input.GetTouch(0).position;
                return true;
            }
#endif
            return false;
        }

        /// <summary>Basılı olsun olmasın, işaretçinin şu anki ekran konumu (bırakma karesi için).</summary>
        private bool GetPointerPosition(out Vector2 screenPos)
        {
            screenPos = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null && (Touchscreen.current.primaryTouch.press.wasReleasedThisFrame || Touchscreen.current.primaryTouch.press.isPressed))
            {
                screenPos = Touchscreen.current.primaryTouch.position.ReadValue();
                return true;
            }
            if (Mouse.current != null)
            {
                screenPos = Mouse.current.position.ReadValue();
                return true;
            }
            if (Pointer.current != null)
            {
                screenPos = Pointer.current.position.ReadValue();
                return true;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.touchCount > 0)
            {
                screenPos = Input.GetTouch(0).position;
                return true;
            }
            screenPos = Input.mousePosition;
            return true;
#endif
            return false;
        }

        private bool GetPointerUp()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame) return true;
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasReleasedThisFrame) return true;
            if (Pointer.current != null && Pointer.current.press.wasReleasedThisFrame) return true;
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetMouseButtonUp(0)) return true;
            if (Input.touchCount > 0 && (Input.GetTouch(0).phase == TouchPhase.Ended || Input.GetTouch(0).phase == TouchPhase.Canceled)) return true;
#endif
            return false;
        }
    }
}
