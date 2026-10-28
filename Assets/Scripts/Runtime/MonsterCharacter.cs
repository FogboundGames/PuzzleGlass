using System;
using System.Collections;
using TMPro;
using UnityEngine;
using DG.Tweening;

namespace PuzzleGlass
{
    [ExecuteAlways]
    public class MonsterCharacter : MonoBehaviour
    {
        [Header("Monster Identity")]
        public string monsterId;
        public PuzzleColor colorType = PuzzleColor.Blue;
        public int remainingCount = 10;
        public int initialCount = 10;
        public SlotPlatform assignedSlot;

        [Header("Visual Parts")]
        public Transform bodyPivot;
        public Transform mouthTargetPoint;
        public Transform badgeTransform;
        public TextMeshPro counterText;
        public Transform eyeLeft;
        public Transform eyeRight;

        [Header("Materials (Auto-Assigned)")]
        public Material bodyMaterial;
        public Material mouthCavityMaterial;
        public Material toothMaterial;
        public Material eyeWhiteMaterial;
        public Material pupilMaterial;
        public Material blushMaterial;
        public Material badgeMaterial;

        private Vector3 _originalBodyScale = Vector3.one;
        private Vector3 _originalBadgeScale = Vector3.one;
        private Tweener _idleTween;
        private Sequence _blinkSeq;
        private bool _isCompleted = false;
        private bool _isMovingToSlot = false;

        public event Action<MonsterCharacter> OnEatenCube;
        public event Action<MonsterCharacter> OnTargetReached;

        private void Awake()
        {
            if (string.IsNullOrEmpty(monsterId))
            {
                monsterId = $"Monster_{colorType}_{Guid.NewGuid().ToString().Substring(0, 4)}";
            }

            EnsureCollider();

            if (transform.childCount == 0)
            {
                RebuildMonsterVisuals();
            }
        }

        private void OnEnable()
        {
            EnsureCollider();

            if (transform.childCount == 0)
            {
                RebuildMonsterVisuals();
            }

            if (Application.isPlaying)
            {
                StartIdleAndBlink();
            }
        }

        private void EnsureCollider()
        {
            BoxCollider col = GetComponent<BoxCollider>();
            if (col == null) col = gameObject.AddComponent<BoxCollider>();
            col.size = new Vector3(1.6f, 1.8f, 4.0f);
            col.center = new Vector3(0f, 0.15f, 0f);
        }

        private void OnMouseDown()
        {
            TryHopToNextAvailableSlot();
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            KillAllTweens();
        }

        private void OnDestroy()
        {
            StopAllCoroutines();
            KillAllTweens();
        }

        public void KillAllTweens()
        {
            transform.DOKill();
            if (bodyPivot != null) bodyPivot.DOKill();
            if (badgeTransform != null) badgeTransform.DOKill();
            if (counterText != null) counterText.transform.DOKill();
            if (eyeLeft != null) eyeLeft.DOKill();
            if (eyeRight != null) eyeRight.DOKill();
            if (_idleTween != null && _idleTween.IsActive()) _idleTween.Kill();
            if (_blinkSeq != null && _blinkSeq.IsActive()) _blinkSeq.Kill();
        }

        public void Initialize(PuzzleColor color, int count, string id = null)
        {
            colorType = color;
            initialCount = count;
            remainingCount = count;
            monsterId = string.IsNullOrEmpty(id) ? $"Monster_{color}_{Guid.NewGuid().ToString().Substring(0, 4)}" : id;
            RebuildMonsterVisuals();
            UpdateBadgeText();
        }

        [ContextMenu("Rebuild Monster Visuals")]
        public void RebuildMonsterVisuals()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                if (Application.isPlaying)
                    Destroy(transform.GetChild(i).gameObject);
                else
                    DestroyImmediate(transform.GetChild(i).gameObject);
            }

            EnsureMaterials();
            BuildMonsterHierarchy();
            UpdateBadgeText();
        }

        private void EnsureMaterials()
        {
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            Color bodyCol = PuzzleColorPalette.GetBodyColor(colorType);
            Color mouthCol = PuzzleColorPalette.GetDarkMouthColor(colorType);

            bodyMaterial = new Material(litShader) { name = $"Mat_Body_{colorType}" };
            bodyMaterial.color = bodyCol;
            if (bodyMaterial.HasProperty("_BaseColor")) bodyMaterial.SetColor("_BaseColor", bodyCol);
            if (bodyMaterial.HasProperty("_Smoothness")) bodyMaterial.SetFloat("_Smoothness", 0.88f);

            mouthCavityMaterial = new Material(litShader) { name = $"Mat_Mouth_{colorType}" };
            mouthCavityMaterial.color = mouthCol;
            if (mouthCavityMaterial.HasProperty("_BaseColor")) mouthCavityMaterial.SetColor("_BaseColor", mouthCol);
            if (mouthCavityMaterial.HasProperty("_Smoothness")) mouthCavityMaterial.SetFloat("_Smoothness", 0.45f);

            toothMaterial = new Material(litShader) { name = "Mat_Tooth" };
            toothMaterial.color = Color.white;
            if (toothMaterial.HasProperty("_BaseColor")) toothMaterial.SetColor("_BaseColor", Color.white);
            if (toothMaterial.HasProperty("_Smoothness")) toothMaterial.SetFloat("_Smoothness", 0.95f);

            eyeWhiteMaterial = new Material(litShader) { name = "Mat_EyeWhite" };
            eyeWhiteMaterial.color = Color.white;
            if (eyeWhiteMaterial.HasProperty("_BaseColor")) eyeWhiteMaterial.SetColor("_BaseColor", Color.white);
            if (eyeWhiteMaterial.HasProperty("_Smoothness")) eyeWhiteMaterial.SetFloat("_Smoothness", 0.98f);

            pupilMaterial = new Material(litShader) { name = "Mat_Pupil" };
            Color pupilCol = new Color(0.08f, 0.12f, 0.22f, 1f);
            pupilMaterial.color = pupilCol;
            if (pupilMaterial.HasProperty("_BaseColor")) pupilMaterial.SetColor("_BaseColor", pupilCol);
            if (pupilMaterial.HasProperty("_Smoothness")) pupilMaterial.SetFloat("_Smoothness", 0.98f);

            blushMaterial = new Material(litShader) { name = "Mat_Blush" };
            Color blushCol = new Color(1.0f, 0.55f, 0.65f, 0.9f);
            blushMaterial.color = blushCol;
            if (blushMaterial.HasProperty("_BaseColor")) blushMaterial.SetColor("_BaseColor", blushCol);

            badgeMaterial = new Material(litShader) { name = "Mat_BellyBadge" };
            badgeMaterial.color = Color.white;
            if (badgeMaterial.HasProperty("_BaseColor")) badgeMaterial.SetColor("_BaseColor", Color.white);
            if (badgeMaterial.HasProperty("_Smoothness")) badgeMaterial.SetFloat("_Smoothness", 0.92f);
        }

        private void BuildMonsterHierarchy()
        {
            // 1. Root Pivot (for squash & stretch animations)
            GameObject pivotObj = new GameObject("BodyPivot");
            pivotObj.transform.SetParent(transform, false);
            bodyPivot = pivotObj.transform;

            // 2. Open-Mouth Monster Body Architecture:
            // Upper Head (forehead dome above mouth)
            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "UpperHead";
            head.transform.SetParent(bodyPivot, false);
            head.transform.localPosition = new Vector3(0, 0.26f, 0.04f);
            head.transform.localScale = new Vector3(0.86f, 0.44f, 0.72f);
            head.GetComponent<MeshRenderer>().sharedMaterial = bodyMaterial;
            DestroyCollider(head);

            // Lower Jaw / Belly (chin bowl below mouth)
            GameObject belly = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            belly.name = "LowerBelly";
            belly.transform.SetParent(bodyPivot, false);
            belly.transform.localPosition = new Vector3(0, -0.28f, 0.05f);
            belly.transform.localScale = new Vector3(0.90f, 0.44f, 0.75f);
            belly.GetComponent<MeshRenderer>().sharedMaterial = bodyMaterial;
            DestroyCollider(belly);

            // Back Shell (encloses the back of the character)
            GameObject backShell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            backShell.name = "BackShell";
            backShell.transform.SetParent(bodyPivot, false);
            backShell.transform.localPosition = new Vector3(0, 0.01f, 0.16f);
            backShell.transform.localScale = new Vector3(0.88f, 0.82f, 0.60f);
            backShell.GetComponent<MeshRenderer>().sharedMaterial = bodyMaterial;
            DestroyCollider(backShell);

            // Side Cheeks (left and right fleshy cheeks framing mouth)
            for (int i = 0; i < 2; i++)
            {
                float sign = (i == 0 ? -1f : 1f);
                GameObject cheek = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                cheek.name = i == 0 ? "CheekLeft" : "CheekRight";
                cheek.transform.SetParent(bodyPivot, false);
                cheek.transform.localPosition = new Vector3(sign * 0.38f, 0.01f, 0.02f);
                cheek.transform.localScale = new Vector3(0.18f, 0.42f, 0.50f);
                cheek.GetComponent<MeshRenderer>().sharedMaterial = bodyMaterial;
                DestroyCollider(cheek);
            }

            // 3. HUGE Gaping Open Mouth Cavity (deep dark bowl recessed into the body!)
            GameObject mouth = new GameObject("MouthCavity");
            mouth.transform.SetParent(bodyPivot, false);
            mouth.transform.localPosition = new Vector3(0, 0.03f, -0.22f);
            mouth.transform.localScale = Vector3.one;
            MeshFilter mfMouth = mouth.AddComponent<MeshFilter>();
            mfMouth.sharedMesh = ProceduralMeshUtils.CreateMouthCavity(0.72f, 0.50f, 0.42f, 16);
            MeshRenderer mrMouth = mouth.AddComponent<MeshRenderer>();
            mrMouth.sharedMaterial = mouthCavityMaterial;

            // Mouth landing point for flying cubes
            GameObject mouthTarget = new GameObject("MouthTargetPoint");
            mouthTarget.transform.SetParent(mouth.transform, false);
            mouthTarget.transform.localPosition = new Vector3(0, 0, 0.10f);
            mouthTargetPoint = mouthTarget.transform;

            // 4. Two Cute White Baby Fangs (hanging from upper lip into mouth)
            BuildTeeth(bodyPivot);

            // 5. Big Cartoon Eyes (perched on forehead above mouth)
            BuildEyes(bodyPivot);

            // 6. Rosy Pink Blush Cheeks
            BuildBlush(bodyPivot);

            // 7. Cute Antenna / Hair Curl on top of head
            BuildHairTuft(bodyPivot);

            // 8. Side Fins / Little Arms
            BuildSideFins(bodyPivot);

            // 9. Cute Stubby Feet
            BuildFeet(bodyPivot);

            // 10. White Belly Pill Badge with Bold Number
            BuildBadge(bodyPivot);

            _originalBodyScale = bodyPivot.localScale;
            if (badgeTransform != null) _originalBadgeScale = badgeTransform.localScale;
        }

        private void BuildTeeth(Transform parent)
        {
            // Two cute white rounded fangs hanging from the top edge of the mouth
            for (int i = 0; i < 2; i++)
            {
                float x = (i == 0 ? -0.14f : 0.14f);
                GameObject tooth = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                tooth.name = i == 0 ? "ToothLeft" : "ToothRight";
                tooth.transform.SetParent(parent, false);
                tooth.transform.localPosition = new Vector3(x, 0.18f, -0.26f);
                tooth.transform.localScale = new Vector3(0.08f, 0.13f, 0.07f);
                tooth.transform.localRotation = Quaternion.Euler(12f, 0, (i == 0 ? 8f : -8f));
                tooth.GetComponent<MeshRenderer>().sharedMaterial = toothMaterial;
                DestroyCollider(tooth);
            }
        }

        private void BuildEyes(Transform parent)
        {
            GameObject eyesContainer = new GameObject("Eyes");
            eyesContainer.transform.SetParent(parent, false);

            eyeLeft = BuildSingleEye(eyesContainer.transform, -0.20f, 0.50f, -0.20f, true);
            eyeRight = BuildSingleEye(eyesContainer.transform, 0.20f, 0.50f, -0.20f, false);
        }

        private Transform BuildSingleEye(Transform parent, float x, float y, float z, bool isLeft)
        {
            GameObject eyeRoot = new GameObject(isLeft ? "Eye_Left" : "Eye_Right");
            eyeRoot.transform.SetParent(parent, false);
            eyeRoot.transform.localPosition = new Vector3(x, y, z);
            eyeRoot.transform.localRotation = Quaternion.Euler(0, 0, isLeft ? 6f : -6f);

            // Eyeball (white sphere)
            GameObject white = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            white.name = "Eyeball";
            white.transform.SetParent(eyeRoot.transform, false);
            white.transform.localScale = new Vector3(0.26f, 0.26f, 0.24f);
            white.GetComponent<MeshRenderer>().sharedMaterial = eyeWhiteMaterial;
            DestroyCollider(white);

            // Pupil (glossy dark sphere)
            GameObject pupil = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pupil.name = "Pupil";
            pupil.transform.SetParent(eyeRoot.transform, false);
            pupil.transform.localPosition = new Vector3(isLeft ? 0.02f : -0.02f, 0.01f, -0.10f);
            pupil.transform.localScale = new Vector3(0.16f, 0.16f, 0.08f);
            pupil.GetComponent<MeshRenderer>().sharedMaterial = pupilMaterial;
            DestroyCollider(pupil);

            // Specular Glint (white sparkle)
            GameObject glint = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            glint.name = "Glint";
            glint.transform.SetParent(eyeRoot.transform, false);
            glint.transform.localPosition = new Vector3(isLeft ? 0.05f : -0.01f, 0.04f, -0.13f);
            glint.transform.localScale = new Vector3(0.05f, 0.05f, 0.03f);
            glint.GetComponent<MeshRenderer>().sharedMaterial = eyeWhiteMaterial;
            DestroyCollider(glint);

            return eyeRoot.transform;
        }

        private void BuildBlush(Transform parent)
        {
            for (int i = 0; i < 2; i++)
            {
                float x = (i == 0 ? -0.36f : 0.36f);
                GameObject blush = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                blush.name = i == 0 ? "BlushLeft" : "BlushRight";
                blush.transform.SetParent(parent, false);
                blush.transform.localPosition = new Vector3(x, 0.10f, -0.24f);
                blush.transform.localScale = new Vector3(0.13f, 0.08f, 0.04f);
                blush.GetComponent<MeshRenderer>().sharedMaterial = blushMaterial;
                DestroyCollider(blush);
            }
        }

        private void BuildHairTuft(Transform parent)
        {
            GameObject tuft = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tuft.name = "HairTuft";
            tuft.transform.SetParent(parent, false);
            tuft.transform.localPosition = new Vector3(0.04f, 0.65f, -0.04f);
            tuft.transform.localScale = new Vector3(0.15f, 0.20f, 0.14f);
            tuft.transform.localRotation = Quaternion.Euler(-10f, 0, 20f);
            tuft.GetComponent<MeshRenderer>().sharedMaterial = bodyMaterial;
            DestroyCollider(tuft);
        }

        private void BuildSideFins(Transform parent)
        {
            for (int i = 0; i < 2; i++)
            {
                float sign = (i == 0 ? -1f : 1f);
                GameObject fin = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                fin.name = i == 0 ? "FinLeft" : "FinRight";
                fin.transform.SetParent(parent, false);
                fin.transform.localPosition = new Vector3(sign * 0.44f, -0.02f, 0.02f);
                fin.transform.localScale = new Vector3(0.12f, 0.24f, 0.16f);
                fin.transform.localRotation = Quaternion.Euler(0, 0, sign * 25f);
                fin.GetComponent<MeshRenderer>().sharedMaterial = bodyMaterial;
                DestroyCollider(fin);
            }
        }

        private void BuildFeet(Transform parent)
        {
            for (int i = 0; i < 2; i++)
            {
                float sign = (i == 0 ? -1f : 1f);
                GameObject foot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                foot.name = i == 0 ? "FootLeft" : "FootRight";
                foot.transform.SetParent(parent, false);
                foot.transform.localPosition = new Vector3(sign * 0.22f, -0.46f, -0.06f);
                foot.transform.localScale = new Vector3(0.20f, 0.12f, 0.22f);
                foot.GetComponent<MeshRenderer>().sharedMaterial = bodyMaterial;
                DestroyCollider(foot);
            }
        }

        private void BuildBadge(Transform parent)
        {
            GameObject badge = new GameObject("BellyBadge");
            badge.transform.SetParent(parent, false);
            badge.transform.localPosition = new Vector3(0, -0.34f, -0.36f);
            badgeTransform = badge.transform;

            // Pill Badge 3D mesh
            MeshFilter mfBadge = badge.AddComponent<MeshFilter>();
            mfBadge.sharedMesh = ProceduralMeshUtils.CreatePillBadge(0.44f, 0.24f, 0.07f, 0.10f);
            MeshRenderer mrBadge = badge.AddComponent<MeshRenderer>();
            mrBadge.sharedMaterial = badgeMaterial;

            // TextMeshPro Counter Text
            GameObject textObj = new GameObject("CountText");
            textObj.transform.SetParent(badge.transform, false);
            textObj.transform.localPosition = new Vector3(0, 0, -0.045f);

            counterText = textObj.AddComponent<TextMeshPro>();
            counterText.alignment = TextAlignmentOptions.Center;
            counterText.fontSize = 2.6f;
            counterText.fontStyle = FontStyles.Bold;
            counterText.color = PuzzleColorPalette.GetTextColor(colorType);
            counterText.text = remainingCount.ToString();
            counterText.rectTransform.sizeDelta = new Vector2(1.2f, 0.6f);
        }

        public void UpdateBadgeText()
        {
            if (counterText != null)
            {
                counterText.text = Mathf.Max(0, remainingCount).ToString();
                counterText.color = PuzzleColorPalette.GetTextColor(colorType);
            }
        }

        private void DestroyCollider(GameObject obj)
        {
            Collider col = obj.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }
        }

        // ==================== ANIMATIONS ====================

        public void StartIdleAndBlink()
        {
            StopIdleAndBlink();

            if (bodyPivot != null)
            {
                bodyPivot.localScale = _originalBodyScale;
                _idleTween = bodyPivot.DOScale(
                    new Vector3(_originalBodyScale.x * 0.95f, _originalBodyScale.y * 1.05f, _originalBodyScale.z * 0.95f),
                    0.65f)
                    .SetEase(Ease.InOutSine)
                    .SetLoops(-1, LoopType.Yoyo);
            }

            if (eyeLeft != null && eyeRight != null)
            {
                _blinkSeq = DOTween.Sequence();
                _blinkSeq.AppendInterval(UnityEngine.Random.Range(2.5f, 4.5f));
                _blinkSeq.Append(eyeLeft.DOScaleY(0.08f, 0.07f).SetEase(Ease.InSine));
                _blinkSeq.Join(eyeRight.DOScaleY(0.08f, 0.07f).SetEase(Ease.InSine));
                _blinkSeq.Append(eyeLeft.DOScaleY(1f, 0.08f).SetEase(Ease.OutSine));
                _blinkSeq.Join(eyeRight.DOScaleY(1f, 0.08f).SetEase(Ease.OutSine));
                _blinkSeq.SetLoops(-1);
            }
        }

        public void StopIdleAndBlink()
        {
            if (_idleTween != null && _idleTween.IsActive()) _idleTween.Kill();
            if (_blinkSeq != null && _blinkSeq.IsActive()) _blinkSeq.Kill();
            if (bodyPivot != null) bodyPivot.localScale = _originalBodyScale;
            if (eyeLeft != null) eyeLeft.localScale = Vector3.one;
            if (eyeRight != null) eyeRight.localScale = Vector3.one;
        }

        public void FeedCube()
        {
            if (_isCompleted) return;

            remainingCount = Mathf.Max(0, remainingCount - 1);
            UpdateBadgeText();

            // 1. Juicy Chomp bite body punch
            if (bodyPivot != null)
            {
                bodyPivot.DOKill(true);
                bodyPivot.DOPunchScale(new Vector3(0.14f, -0.20f, 0.14f), 0.10f, 4, 0.55f);
            }

            // 2. Belly Badge Punch
            if (badgeTransform != null)
            {
                badgeTransform.DOKill(true);
                badgeTransform.DOPunchScale(Vector3.one * 0.25f, 0.10f, 4, 0.5f);
            }

            // 3. Counter Text Punch
            if (counterText != null)
            {
                counterText.transform.DOKill(true);
                counterText.transform.DOPunchScale(Vector3.one * 0.20f, 0.09f, 4, 0.5f);
            }

            OnEatenCube?.Invoke(this);

            if (remainingCount <= 0)
            {
                _isCompleted = true;
                StartCoroutine(CelebrateAndCompleteRoutine());
            }
        }

        private IEnumerator CelebrateAndCompleteRoutine()
        {
            StopIdleAndBlink();

            if (eyeLeft != null) eyeLeft.DOScale(new Vector3(1.15f, 0.25f, 1f), 0.15f);
            if (eyeRight != null) eyeRight.DOScale(new Vector3(1.15f, 0.25f, 1f), 0.15f);

            Vector3 origPos = transform.position;

            // Joyous celebratory victory double jump with playful wiggles!
            Sequence celebSeq = DOTween.Sequence();
            celebSeq.Join(transform.DOJump(origPos, jumpPower: 0.45f, numJumps: 2, duration: 0.60f).SetEase(Ease.OutQuad));
            if (bodyPivot != null)
            {
                celebSeq.Join(bodyPivot.DOLocalRotate(new Vector3(0, 0, 14f), 0.10f).SetLoops(6, LoopType.Yoyo).SetEase(Ease.InOutSine));
            }
            yield return celebSeq.WaitForCompletion();

            if (bodyPivot != null) bodyPivot.localRotation = Quaternion.identity;

            if (assignedSlot != null)
            {
                assignedSlot.DetachMonster();
                assignedSlot = null;
            }

            OnTargetReached?.Invoke(this);

            // Cheerful victory exit: joyful sink and shrink away to leave the slot clean
            Sequence exitSeq = DOTween.Sequence();
            exitSeq.Join(transform.DOMoveY(transform.position.y - 1.2f, 0.30f).SetEase(Ease.InBack));
            exitSeq.Join(transform.DOScale(Vector3.zero, 0.30f).SetEase(Ease.InBack));
            yield return exitSeq.WaitForCompletion();

            gameObject.SetActive(false);
        }

        public bool TryHopToNextAvailableSlot()
        {
            if (_isMovingToSlot || _isCompleted) return false;

            if (assignedSlot != null)
            {
                // Playful wiggle in slot
                transform.DOKill(true);
                transform.DOPunchScale(new Vector3(-0.06f, 0.12f, 0f), 0.22f, 6, 0.5f);
                return false;
            }

            SlotSystem slotSys = SlotSystem.Instance;
            if (slotSys == null) slotSys = FindFirstObjectByType<SlotSystem>();
            if (slotSys == null) return false;

            SlotPlatform targetSlot = slotSys.GetFirstEmptySlot();
            if (targetSlot == null)
            {
                // Denial shake when no slot is available
                transform.DOKill(true);
                transform.DOShakePosition(0.28f, strength: new Vector3(0.12f, 0f, 0f), vibrato: 18, randomness: 90, snapping: false, fadeOut: true);
                return false;
            }

            targetSlot.ReserveSlot(this);
            StartCoroutine(HopToSlotRoutine(targetSlot));
            return true;
        }

        private IEnumerator HopToSlotRoutine(SlotPlatform slot)
        {
            _isMovingToSlot = true;
            Vector3 startScale = transform.localScale;
            Vector3 targetPos = slot.GetPlacementPosition();

            // 1. Anticipation squash
            yield return transform.DOScale(new Vector3(startScale.x * 1.18f, startScale.y * 0.78f, startScale.z * 1.15f), 0.09f)
                .SetEase(Ease.OutQuad).WaitForCompletion();

            // 2. High parabolic leap arc to slot with stretch along jump
            Sequence leapSeq = DOTween.Sequence();
            leapSeq.Join(transform.DOJump(targetPos, jumpPower: 0.62f, numJumps: 1, duration: 0.36f).SetEase(Ease.OutQuad));
            // Airborne stretch then relax before impact
            leapSeq.Join(transform.DOScale(new Vector3(startScale.x * 0.88f, startScale.y * 1.22f, startScale.z * 0.88f), 0.18f).SetEase(Ease.OutQuad));
            leapSeq.Insert(0.18f, transform.DOScale(startScale, 0.18f).SetEase(Ease.InQuad));
            yield return leapSeq.WaitForCompletion();

            transform.position = targetPos;

            // 3. Attach to slot & play slot punch
            slot.AttachMonster(this);

            // Register order with dispenser and trigger auto-feeding
            CubeDispenser dispenser = FindFirstObjectByType<CubeDispenser>();
            if (dispenser != null)
            {
                dispenser.RegisterMonsterOrder(this);
                dispenser.StartFeedingSlottedMonsters();
            }

            // 4. Juicy landing squash bounce
            yield return transform.DOPunchScale(new Vector3(0.20f, -0.22f, 0.20f), 0.22f, 7, 0.6f).WaitForCompletion();

            transform.localScale = startScale;
            _isMovingToSlot = false;
        }

        private IEnumerator DenialShakeRoutine()
        {
            transform.DOKill(true);
            yield return transform.DOShakePosition(0.28f, strength: new Vector3(0.12f, 0f, 0f), vibrato: 18, randomness: 90, snapping: false, fadeOut: true).WaitForCompletion();
        }

        private IEnumerator WiggleInSlotRoutine()
        {
            transform.DOKill(true);
            yield return transform.DOPunchScale(new Vector3(-0.06f, 0.12f, 0f), 0.22f, 6, 0.5f).WaitForCompletion();
        }
    }
}
