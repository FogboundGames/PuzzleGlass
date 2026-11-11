using System;
using System.Collections.Generic;
using UnityEngine;
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
        private Camera _mainCam;

        public event Action<MonsterCharacter, SlotPlatform> OnMonsterPlaced;
        public event Action<MonsterCharacter> OnMonsterReturnedToTray;

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
            Ray ray = _mainCam.ScreenPointToRay(screenPos);
            MonsterCharacter selectedMonster = null;

            // 1. Raycast for Monster BoxCollider (includes both staging monsters AND slotted monsters!)
            if (Physics.Raycast(ray, out RaycastHit hit, 100f))
            {
                selectedMonster = hit.collider.GetComponentInParent<MonsterCharacter>();
            }

            // 2. Proximity fallback: check distance on screen / world plane (both staging and slotted monsters)
            if (selectedMonster == null)
            {
                Vector3 worldTouch = ScreenToWorldOnPlane(screenPos, 0f);
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

            selectedMonster.EnsureStagingHomeRecorded();

            _isDragging = true;
            _draggedMonster = selectedMonster;
            _pointerDownScreenPos = screenPos;
            _pointerDownTime = Time.time;
            _hasMovedBeyondTapThreshold = false;

            _wasSlottedOnDragStart = (selectedMonster.assignedSlot != null);
            _previousSlot = selectedMonster.assignedSlot;

            _dragOriginPos = _draggedMonster.transform.position;
            _dragStartScale = (_draggedMonster.hasStagingHome && _draggedMonster.stagingHomeScale.sqrMagnitude > 0.01f)
                ? _draggedMonster.stagingHomeScale
                : _draggedMonster.transform.localScale;

            Vector3 pointerWorld = ScreenToWorldOnPlane(screenPos, dragHeightZ);
            _dragOffset = _dragOriginPos - pointerWorld;
            _dragOffset.z = 0f;

            _draggedMonster.StopAllCoroutines();
            _draggedMonster.transform.DOKill();
            _draggedMonster.transform.DOScale(_dragStartScale * dragLiftScale, 0.12f).SetEase(Ease.OutQuad);
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
                if (Vector2.Distance(screenPos, _pointerDownScreenPos) > 12f)
                {
                    _hasMovedBeyondTapThreshold = true;

                    // Detach from previous slot as soon as the player starts dragging it out
                    if (_previousSlot != null)
                    {
                        _previousSlot.DetachMonster();
                        _draggedMonster.assignedSlot = null;
                        if (BallCollectionController.Instance != null)
                        {
                            BallCollectionController.Instance.OnMonsterRemovedFromSlot(_draggedMonster);
                        }
                    }
                }
                else
                {
                    return;
                }
            }

            Vector3 pointerWorld = ScreenToWorldOnPlane(screenPos, dragHeightZ);
            Vector3 targetPos = pointerWorld + _dragOffset;
            targetPos.z = dragHeightZ;

            _draggedMonster.transform.position = Vector3.Lerp(
                _draggedMonster.transform.position,
                targetPos,
                dragFollowSpeed * Time.deltaTime);

            // Check hover over any slot (empty or occupied for swap)
            SlotPlatform bestSlot = FindClosestSlot(_draggedMonster.transform.position);

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

        private void HandlePointerUp(Vector2 screenPos)
        {
            if (_draggedMonster == null)
            {
                _isDragging = false;
                return;
            }

            MonsterCharacter monster = _draggedMonster;
            SlotPlatform targetSlot = _hoveredSlot;

            if (_hoveredSlot != null)
            {
                _hoveredSlot.SetHighlight(false);
                _hoveredSlot = null;
            }

            _draggedMonster = null;
            _isDragging = false;

            bool isQuickTap = !_hasMovedBeyondTapThreshold && (Time.time - _pointerDownTime < 0.30f);

            // 1. Quick Tap Behavior:
            // - If monster was in a slot -> Return it to its staging home!
            // - If monster was in staging tray -> Hop it to the first available empty slot!
            if (isQuickTap)
            {
                if (_wasSlottedOnDragStart)
                {
                    monster.ReturnToStagingHome(() => OnMonsterReturnedToTray?.Invoke(monster));
                }
                else
                {
                    monster.transform.localScale = _dragStartScale;
                    monster.TryHopToNextAvailableSlot();
                }
                _previousSlot = null;
                return;
            }

            // 2. Drag & Drop onto a Slot (Empty or Swap with Occupied Slot)
            if (targetSlot != null)
            {
                if (targetSlot.isOccupied && targetSlot.currentMonster != null && targetSlot.currentMonster != monster)
                {
                    MonsterCharacter displacedMonster = targetSlot.currentMonster;
                    targetSlot.DetachMonster();
                    displacedMonster.assignedSlot = null;

                    if (BallCollectionController.Instance != null)
                    {
                        BallCollectionController.Instance.OnMonsterRemovedFromSlot(displacedMonster);
                    }

                    // If we dragged from another slot, swap the displaced monster into our previous slot;
                    // otherwise send the displaced monster back to its staging tray home!
                    if (_previousSlot != null && !_previousSlot.isOccupied)
                    {
                        PlaceMonsterIntoSlot(displacedMonster, _previousSlot, _dragStartScale);
                    }
                    else
                    {
                        displacedMonster.ReturnToStagingHome(() => OnMonsterReturnedToTray?.Invoke(displacedMonster));
                    }
                }

                PlaceMonsterIntoSlot(monster, targetSlot, _dragStartScale);
                _previousSlot = null;
                return;
            }

            // 3. Dropped outside any slot -> Return monster to its staging home tray!
            if (_previousSlot != null && monster.assignedSlot == _previousSlot)
            {
                _previousSlot.DetachMonster();
                monster.assignedSlot = null;
            }

            monster.ReturnToStagingHome(() => OnMonsterReturnedToTray?.Invoke(monster));
            _previousSlot = null;
        }

        private void PlaceMonsterIntoSlot(MonsterCharacter placedMonster, SlotPlatform targetSlot, Vector3 baseScale)
        {
            if (placedMonster == null || targetSlot == null) return;

            Vector3 slotPlacePos = targetSlot.GetPlacementPosition();
            targetSlot.ReserveSlot(placedMonster);

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
                if (slot == null) continue;

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
