using System;
using UnityEngine;

namespace MindHexer.Shared.Input
{
    /// <summary>
    /// 브롤스타즈식 **플로팅 조이스틱** 입력 코어(Unity 비의존).
    /// 누르는 순간 그 지점이 중심이 되고, 떼면 사라지며, 다시 누른 새 위치가 새 중심이 된다.
    ///
    /// 좌표는 화면 픽셀(원점 좌하단, y는 위쪽) 기준으로 넣는다. 출력 <see cref="Value"/>는
    /// 반지름으로 정규화된 -1..1 디스크 벡터(x=오른쪽, y=위쪽, 크기 0..1).
    ///
    /// UnityEngine.Vector2를 데이터 반환용으로만 쓰고 내부 계산은 float 성분으로 처리 →
    /// 콘솔 하니스/EditMode로 결정론적 검증 가능. 스레드 안전 아님(입력 스레드에서만 호출).
    /// </summary>
    public sealed class FloatingJoystick
    {
        /// <summary>최대 반지름(픽셀). 이 거리에서 세기 1.0.</summary>
        public float Radius;

        /// <summary>0..1 데드존. 중심 근처 미세 입력을 0으로 죽이고 나머지를 재정규화.</summary>
        public float DeadZone;

        /// <summary>true면 손가락이 반지름 밖으로 나갈 때 중심이 따라온다(thumb-walk). 기본 false(중심 고정).</summary>
        public bool FollowOnOverflow;

        private float _cx, _cy;   // 중심(픽셀)
        private float _kx, _ky;   // 노브 위치(클램프됨, 그리기용)
        private float _vx, _vy;   // 출력 값 -1..1
        private bool _active;

        public FloatingJoystick(float radius = 140f, float deadZone = 0.08f, bool followOnOverflow = false)
        {
            Radius = radius;
            DeadZone = deadZone;
            FollowOnOverflow = followOnOverflow;
        }

        /// <summary>현재 조이스틱이 눌려 있는지(중심이 정해진 상태).</summary>
        public bool Active => _active;

        /// <summary>중심 픽셀 좌표(그리기용). Active일 때만 유효.</summary>
        public Vector2 Center => new Vector2(_cx, _cy);

        /// <summary>노브 픽셀 좌표(반지름으로 클램프됨, 그리기용).</summary>
        public Vector2 Knob => new Vector2(_kx, _ky);

        /// <summary>정규화된 출력 벡터(-1..1 디스크, x 오른쪽/y 위쪽). 비활성 시 (0,0).</summary>
        public Vector2 Value => new Vector2(_vx, _vy);

        /// <summary>출력 세기(0..1).</summary>
        public float Magnitude => (float)Math.Sqrt(_vx * _vx + _vy * _vy);

        /// <summary>
        /// 새 터치 시작 → 이 지점이 중심이 된다(플로팅). 떼었다 다시 누르면 매번 새 중심.
        /// </summary>
        public void Press(float x, float y)
        {
            _active = true;
            _cx = x; _cy = y;
            _kx = x; _ky = y;
            _vx = 0f; _vy = 0f;
        }

        /// <summary>드래그 → 노브/값 갱신. Press 전이면 무시.</summary>
        public void Drag(float x, float y)
        {
            if (!_active) return;
            if (Radius <= 0f) { _kx = _cx; _ky = _cy; _vx = 0f; _vy = 0f; return; }

            float dx = x - _cx;
            float dy = y - _cy;
            float dist = (float)Math.Sqrt(dx * dx + dy * dy);

            if (dist > Radius)
            {
                if (FollowOnOverflow)
                {
                    // 중심을 노브 방향으로 끌어당겨 노브가 링에 걸치게 한다(thumb-walk).
                    float over = dist - Radius;
                    _cx += dx / dist * over;
                    _cy += dy / dist * over;
                    dx = x - _cx; dy = y - _cy;
                    dist = Radius;
                }
                else
                {
                    // 노브를 링에 클램프(중심 고정).
                    dx = dx / dist * Radius;
                    dy = dy / dist * Radius;
                    dist = Radius;
                }
            }

            _kx = _cx + dx;
            _ky = _cy + dy;

            float mag = dist / Radius; // 0..1
            if (mag <= DeadZone || dist <= 0f)
            {
                _vx = 0f; _vy = 0f;
                return;
            }

            // 데드존 밖을 0..1로 재정규화.
            float scaled = DeadZone < 1f ? (mag - DeadZone) / (1f - DeadZone) : mag;
            float nx = dx / dist; // 단위 방향
            float ny = dy / dist;
            _vx = nx * scaled;
            _vy = ny * scaled;
        }

        /// <summary>터치 해제 → 비활성, 값 0. (다음 Press가 새 중심)</summary>
        public void Release()
        {
            _active = false;
            _vx = 0f; _vy = 0f;
        }

        /// <summary>강제 초기화.</summary>
        public void Reset() => Release();
    }
}
