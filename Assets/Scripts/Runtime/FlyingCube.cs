using UnityEngine;
using DG.Tweening;

namespace PuzzleGlass
{
    /// <summary>
    /// True Newtonian Free-Fall Projectile Physics for dispensed cubes:
    /// 1. Inside the vertical spout channel (if above the tip), the cube falls strictly vertically under gravity.
    /// 2. Upon clearing the spout tip, it follows a real ballistic parabola (vy -= g * dt, pos += v * dt)
    ///    straight into the monster's open mouth.
    /// 3. Scale is locked at 100% (Vector3.one) and Z-depth is kept in front of the monster's forehead (-0.46f)
    ///    so the cube NEVER shrinks or gets clipped by 3D geometry.
    /// </summary>
    public class FlyingCube : MonoBehaviour
    {
        public PuzzleColor colorType;
        public MonsterCharacter targetMonster;

        private const float FrontFlightZ = -0.46f;
        private const float Gravity = 32.0f;

        private Vector3 _pos;
        private Vector3 _spoutExitPos;
        private float _spoutStartZ = -0.10f;
        private float _vx;
        private float _vy;
        private float _angularVelocityX;
        private float _angularVelocityY;
        private float _angularVelocityZ;
        private float _airElapsed;
        private float _airDuration;
        private int _particlesPerCount = 1;
        private bool _inSpoutChannel;
        private bool _isFlying;
        private bool _hasFedMonster;

        public void LaunchThroughGlassTrack(
            Vector3[] trackWaypoints,
            Vector3 spoutExitPos,
            MonsterCharacter target,
            PuzzleColor color,
            Material cubeMat,
            Mesh cubeMesh,
            int particlesPerCount = 1,
            float initialVy = -2.6f)
        {
            targetMonster = target;
            colorType = color;
            _particlesPerCount = Mathf.Max(1, particlesPerCount);

            var mf = GetComponent<MeshFilter>();
            if (mf == null) mf = gameObject.AddComponent<MeshFilter>();
            if (mf.sharedMesh == null && cubeMesh != null) mf.sharedMesh = cubeMesh;

            var mr = GetComponent<MeshRenderer>();
            if (mr == null) mr = gameObject.AddComponent<MeshRenderer>();
            if (cubeMat != null) mr.sharedMaterial = cubeMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.enabled = true;

            transform.DOKill();

            // Lock at 100% full original scale — NEVER shrink the cube!
            transform.localScale = Vector3.one;

            Vector3 rawStart = (trackWaypoints != null && trackWaypoints.Length > 0)
                ? trackWaypoints[0]
                : transform.position;

            _spoutStartZ = rawStart.z;
            _spoutExitPos = new Vector3(spoutExitPos.x, spoutExitPos.y, FrontFlightZ);
            float startDownVy = Mathf.Min(-2.6f, initialVy);

            // If the cube starts inside the vertical glass spout, slide down inside the glass Z-plane first!
            if (rawStart.y > _spoutExitPos.y + 0.012f)
            {
                _pos = new Vector3(rawStart.x, rawStart.y, _spoutStartZ);
                _vx = (_spoutExitPos.x - rawStart.x) * 12.0f;
                _vy = startDownVy;
                _angularVelocityX = 85f;
                _angularVelocityY = -65f;
                _angularVelocityZ = Mathf.Clamp(-_vx * 90f, -180f, 180f);
                _inSpoutChannel = true;
            }
            else
            {
                _pos = new Vector3(rawStart.x, _spoutExitPos.y, _spoutStartZ);
                _vy = startDownVy;
                _inSpoutChannel = false;
                InitializeBallisticFallToMouth();
            }

            transform.position = _pos;
            _hasFedMonster = false;
            _isFlying = true;
        }

        private void InitializeBallisticFallToMouth()
        {
            Vector3 mouthPos = GetTargetMouthWorldPos();
            float dy = Mathf.Max(0.08f, _pos.y - mouthPos.y);
            float v0 = Mathf.Max(2.8f, -_vy);

            // Solve dy = v0 * t + 0.5 * g * t^2 for exact Newtonian free-fall time t
            _airDuration = (-v0 + Mathf.Sqrt(v0 * v0 + 2f * Gravity * dy)) / Gravity;
            _airDuration = Mathf.Clamp(_airDuration, 0.08f, 0.45f);
            _airElapsed = 0f;

            _vy = -v0;
            _vx = (mouthPos.x - _pos.x) / _airDuration;
            _angularVelocityZ = Mathf.Clamp(-_vx * 85f, -210f, 210f);
            _angularVelocityX = 110f;
            _angularVelocityY = (_vx >= 0f ? 75f : -75f);
        }

        private Vector3 GetTargetMouthWorldPos()
        {
            if (targetMonster != null && targetMonster.mouthTargetPoint != null)
            {
                Vector3 mp = targetMonster.mouthTargetPoint.position;
                return new Vector3(mp.x, mp.y, FrontFlightZ);
            }
            if (targetMonster != null)
            {
                Vector3 tp = targetMonster.transform.position;
                return new Vector3(tp.x, tp.y + 0.03f, FrontFlightZ);
            }
            return _spoutExitPos + new Vector3(0f, -1.2f, 0f);
        }

        private void Update()
        {
            if (!_isFlying) return;

            float dt = Mathf.Min(Time.deltaTime, 0.033f);

            // Enforce 100% constant scale every frame
            transform.localScale = Vector3.one;

            if (_inSpoutChannel)
            {
                // Phase 1: Vertical gravity drop strictly INSIDE the glass spout tube
                _vy -= Gravity * dt;
                _pos.y += _vy * dt;
                _pos.x = Mathf.MoveTowards(_pos.x, _spoutExitPos.x, 2.5f * dt);
                _pos.z = _spoutStartZ;

                if (_pos.y <= _spoutExitPos.y)
                {
                    _pos.y = _spoutExitPos.y;
                    _inSpoutChannel = false;
                    InitializeBallisticFallToMouth();
                }

                transform.position = _pos;
                transform.Rotate(_angularVelocityX * 0.4f * dt, _angularVelocityY * 0.4f * dt, _angularVelocityZ * 0.4f * dt, Space.Self);
                return;
            }

            // Phase 2: Open-air Newtonian projectile free-fall into the monster's open mouth
            _airElapsed += dt;
            Vector3 mouthPos = GetTargetMouthWorldPos();

            float remTime = _airDuration - _airElapsed;
            if (remTime > 0.015f)
            {
                _vx = (mouthPos.x - _pos.x) / remTime;
            }

            _vy -= Gravity * dt;
            _pos.x += _vx * dt;
            _pos.y += _vy * dt;

            // Smoothly emerge from the spout Z plane (-0.10) to the open-air flight plane (-0.46)
            float zBlend = Mathf.Clamp01(_airElapsed / Mathf.Max(0.06f, _airDuration * 0.35f));
            _pos.z = Mathf.Lerp(_spoutStartZ, FrontFlightZ, zBlend);

            transform.position = _pos;
            transform.Rotate(_angularVelocityX * dt, _angularVelocityY * dt, _angularVelocityZ * dt, Space.Self);

            if (_pos.y <= mouthPos.y || _airElapsed >= _airDuration)
            {
                _isFlying = false;
                if (!_hasFedMonster)
                {
                    _hasFedMonster = true;
                    if (targetMonster != null)
                    {
                        targetMonster.FeedParticle(_particlesPerCount);
                    }
                }
                Destroy(gameObject);
            }
        }

        public void Launch(Vector3 start, MonsterCharacter target, PuzzleColor color, Material cubeMat, Mesh cubeMesh)
        {
            LaunchThroughGlassTrack(new Vector3[] { start }, start, target, color, cubeMat, cubeMesh, 1);
        }

        private void OnDestroy()
        {
            transform.DOKill();
        }
    }
}
