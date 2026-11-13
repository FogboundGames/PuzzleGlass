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
        private Vector3 _dragOffset;
        private Vector2 _pointerDownScreenPos;
        private float _pointerDownTime;
        private bool _wasSlottedOnDragStart = false;
        private SlotPlatform _previousSlot;
        private bool _hasMovedBeyondTapThreshold = false;
        private bool _isLiftedOutOfGrid = false;
        private Camera _mainCam;

        public event Action<MonsterCharacter, SlotPlatform> OnMonsterPlaced;
        public event Action<MonsterCharacter> OnMonsterReturnedToTray;

        public bool IsBlockCurrentlyDragged(MonsterCharacter block)
        {
            return _isDragging && _draggedMonster != null && _draggedMonster == block;
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
            else if (_isDragging && pointerHeld)
            {
                HandlePointerDrag(pointerScreenPos);
            }
            else if (_isDragging && pointerUp)
            {
                HandlePointerUp(pointerScreenPos);
            }
        }

        private void HandlePointerDown(Vector2 screenPos)
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }
            if (PuzzleGlassGameManager.Instance != null &&
                (PuzzleGlassGameManager.Instance.isLevelCompleted || PuzzleGlassGameManager.Instance.isLevelFailed))
            {
                return;
            }

            Ray ray = _mainCam.ScreenPointToRay(screenPos);
            RaycastHit[] allHits = Physics.RaycastAll(ray, 100f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            Vector3 worldTouch = ScreenToWorldOnPlane(screenPos, 0f);

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

            // 2. Proximity fallback: check distance on screen / world plane (both staging and slotted monsters)
            if (selectedMonster == null)
            {
                float closestDist = 0.85f;

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

            // Lock legacy slotted boxes, and lock in-grid boxes only while balls are actively flying into them
            if (selectedMonster.assignedSlot != null)
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

            _isDragging = true;
            _draggedMonster = selectedMonster;
            _pointerDownScreenPos = screenPos;
            _pointerDownTime = Time.time;
            _hasMovedBeyondTapThreshold = false;
            _isLiftedOutOfGrid = false;

            _wasSlottedOnDragStart = false;
            _previousSlot = null;

            _dragOriginPos = _draggedMonster.transform.position;
            _dragStartScale = (_draggedMonster.hasStagingHome && _draggedMonster.stagingHomeScale.sqrMagnitude > 0.001f)
                ? _draggedMonster.stagingHomeScale
                : _draggedMonster.transform.localScale;

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

            if (!_hasMovedBeyondTapThreshold)
            {
                if (Vector2.Distance(screenPos, _pointerDownScreenPos) > 10f)
                {
                    _hasMovedBeyondTapThreshold = true;
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
                bool exitClear = grid.IsBlockExitClearToSlots(_draggedMonster, allMonsters);
                bool wantsPullActivate = exitClear && pointerWorldGrid.y > grid.TopFrameInnerY + 0.04f;

                if (wantsPullActivate)
                {
                    // Gentle upward tug feedback while keeping the block aligned in its top grid cell;
                    // release triggers direct in-grid collection!
                    _isLiftedOutOfGrid = true;
                    Vector3 snappedAnchorPos = grid.GetWorldPositionForAnchor(_draggedMonster.gridAnchor, _draggedMonster.blockShape);
                    Vector3 tugPos = snappedAnchorPos + new Vector3(0f, Mathf.Min(0.14f, (pointerWorldGrid.y - grid.TopFrameInnerY) * 0.4f), -0.05f);
                    _draggedMonster.transform.position = Vector3.Lerp(
                        _draggedMonster.transform.position,
                        tugPos,
                        28f * Time.deltaTime);
                    _draggedMonster.transform.localScale = Vector3.Lerp(
                        _draggedMonster.transform.localScale,
                        _dragStartScale * 1.05f,
                        20f * Time.deltaTime);
                    return;
                }

                // Sliding smoothly inside the bottom grid tray with solid collision against walls & other blocks
                if (_isLiftedOutOfGrid)
                {
                    _isLiftedOutOfGrid = false;
                }

                Vector3 desiredCenter = pointerWorldGrid + _dragOffset;
                desiredCenter.z = 0f;

                Vector2Int desiredAnchor = grid.WorldCenterToNearestAnchor(desiredCenter, _draggedMonster.blockShape);
                Vector2Int stepped = grid.SlideAnchorStepByStep(_draggedMonster, desiredAnchor, allMonsters);
                if (stepped != _draggedMonster.gridAnchor)
                {
                    _draggedMonster.gridAnchor = stepped;
                    _draggedMonster.stagingColumn = stepped.x;
                    _draggedMonster.stagingRow = stepped.y;
                    grid.RefreshAllGridBlockHighlightStates(allMonsters);
                    PuzzleSoundManager.Instance?.PlayGridSlide();
                }

                Vector3 snappedWorld = grid.GetWorldPositionForAnchor(_draggedMonster.gridAnchor, _draggedMonster.blockShape);
                Vector3 previewPos = ComputeConstrainedGridPreview(grid, _draggedMonster, snappedWorld, desiredCenter, allMonsters);
                previewPos.z = -0.05f;

                _draggedMonster.transform.position = Vector3.Lerp(
                    _draggedMonster.transform.position,
                    previewPos,
                    Mathf.Max(dragFollowSpeed, 38f) * Time.deltaTime);
                _draggedMonster.transform.localScale = Vector3.Lerp(
                    _draggedMonster.transform.localScale,
                    _dragStartScale,
                    22f * Time.deltaTime);
                return;
            }

            Vector3 pointerWorld = ScreenToWorldOnPlane(screenPos, dragHeightZ);
            Vector3 targetPos = pointerWorld + _dragOffset;
            targetPos.z = dragHeightZ;

            _draggedMonster.transform.position = Vector3.Lerp(
                _draggedMonster.transform.position,
                targetPos,
                dragFollowSpeed * Time.deltaTime);

            UpdateHoveredSlotHighlight(FindClosestSlot(_draggedMonster.transform.position));
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
            SlotPlatform targetSlot = _hoveredSlot;
            bool wasLifted = _isLiftedOutOfGrid;

            UpdateHoveredSlotHighlight(null);

            _draggedMonster = null;
            _isDragging = false;
            _isLiftedOutOfGrid = false;

            var grid = StagingGridBoard.Instance;
            var allMonsters = PuzzleGlassGameManager.Instance != null ? PuzzleGlassGameManager.Instance.activeMonsters : null;

            bool isQuickTap = !_hasMovedBeyondTapThreshold && (Time.time - _pointerDownTime < 0.30f);

            // Always settle visually onto the valid grid anchor cleanly first
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
            if (isQuickTap || wasLifted)
            {
                monster.TryStartInGridCollection(playDenialFeedback: true);
            }
            else if (grid != null && grid.IsBlockAtTopExit(monster))
            {
                monster.TryStartInGridCollection(playDenialFeedback: false);
            }

            // 2. Fallback if grid is missing
            if (grid == null)
            {
                monster.ReturnToStagingHome(() => OnMonsterReturnedToTray?.Invoke(monster));
            }
            _previousSlot = null;
        }

        private void PlaceMonsterIntoSlot(MonsterCharacter placedMonster, SlotPlatform targetSlot, Vector3 baseScale)
        {
            if (placedMonster == null || targetSlot == null) return;

            Vector3 slotPlacePos = targetSlot.GetPlacementPosition();
            targetSlot.ReserveSlot(placedMonster);

            if (PuzzleGlassGameManager.Instance != null)
            {
                PuzzleGlassGameManager.Instance.OnStagingBoxPlacedInSlot(placedMonster);
            }

            placedMonster.transform.DOKill();
            Sequence snapSeq = DOTween.Sequence();
            snapSeq.Append(placedMonster.transform.DOJump(slotPlacePos, jumpPower: 0.25f, numJumps: 1, duration: 0.18f).SetEase(Ease.OutQuad));
            snapSeq.Join(placedMonster.transform.DOScale(baseScale, 0.18f).SetEase(Ease.OutQuad));
            snapSeq.OnComplete(() =>
            {
                targetSlot.AttachMonster(placedMonster);
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

        private SlotPlatform FindClosestSlot(Vector3 monsterPos)
        {
            SlotSystem slotSys = SlotSystem.Instance.OrNull() ?? FindFirstObjectByType<SlotSystem>();
            if (slotSys == null || slotSys.slots == null) return null;

            SlotPlatform closest = null;
            float closestDist = snapRadius;

            for (int i = 0; i < slotSys.slots.Count; i++)
            {
                var slot = slotSys.slots[i];
                if (slot == null || slot.isOccupied || slot.currentMonster != null) continue;

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
