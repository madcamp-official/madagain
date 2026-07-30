using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 선·원을 모아 <b>메시 하나</b>로 만드는 빌더. UI 전용.
    ///
    /// <para><b>왜 Canvas가 아닌가</b> — 이 UI가 그리는 것은 선과 점뿐이다. 자동 레이아웃도, 레이캐스트도,
    /// 폰트도 안 쓴다. 반면 <b>매 프레임 전부 다시 그린다</b> — Canvas에게는 최악의 조건이다
    /// (요소 하나만 바뀌어도 그 캔버스 전체 메시를 다시 만든다). 얻는 것 없이 비용만 내는 셈이라
    /// 절차적 메시로 간다. 드로우콜도 1개로 끝난다.</para>
    ///
    /// <para><b>좌표계</b> — 모든 좌표는 <b>눈이 원점</b>인 로컬 공간이다(보통 <c>[Head]</c> 로컬).
    /// 선에 두께를 주려면 어느 쪽이 '옆'인지 알아야 하는데, 눈이 원점이면 그 방향을 좌표만으로
    /// 구할 수 있다 — 카메라를 따로 참조할 필요가 없다.</para>
    ///
    /// <para>리스트를 재사용하므로 매 프레임 호출해도 GC가 거의 돌지 않는다.</para>
    /// </summary>
    public class UiMeshBuilder
    {
        readonly List<Vector3> _verts = new List<Vector3>(512);
        readonly List<Color32> _colors = new List<Color32>(512);
        readonly List<int> _tris = new List<int>(1024);

        public void Clear()
        {
            _verts.Clear();
            _colors.Clear();
            _tris.Clear();
        }

        /// <summary>선분 하나. 두께는 눈을 향하도록 자동으로 눕힌다.</summary>
        public void AddLine(Vector3 a, Vector3 b, float width, Color color)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-5f || width <= 0f || color.a <= 0f) return;

            d /= len;

            // 눈(원점)에서 선분 가운데를 보는 방향. 그 방향과 선분에 모두 수직인 축이 '옆'이다.
            Vector3 mid = (a + b) * 0.5f;
            Vector3 view = mid.sqrMagnitude > 1e-8f ? mid.normalized : Vector3.forward;

            Vector3 side = Vector3.Cross(d, view);
            if (side.sqrMagnitude < 1e-8f) side = Vector3.Cross(d, Vector3.up);   // 선분이 시선과 나란한 예외
            side = side.normalized * (width * 0.5f);

            int v = _verts.Count;
            _verts.Add(a - side); _verts.Add(a + side);
            _verts.Add(b + side); _verts.Add(b - side);

            Color32 c32 = color;
            _colors.Add(c32); _colors.Add(c32); _colors.Add(c32); _colors.Add(c32);

            _tris.Add(v); _tris.Add(v + 1); _tris.Add(v + 2);
            _tris.Add(v); _tris.Add(v + 2); _tris.Add(v + 3);
        }

        /// <summary>
        /// 살짝 부푼 곡선(2차 베지에). <paramref name="bulge"/>가 0이면 직선과 같다.
        ///
        /// <para><b>왜 필요한가</b> — 같은 두 점 사이를 여러 번 지나면 선이 완전히 겹쳐서
        /// <b>몇 번 지났는지 보이지 않는다.</b> 부푸는 방향과 크기를 달리해 갈라 놓는다.</para>
        /// </summary>
        public void AddCurve(Vector3 a, Vector3 b, float bulge, float width, Color color, int segments = 10)
        {
            if (Mathf.Abs(bulge) < 1e-5f) { AddLine(a, b, width, color); return; }

            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-5f) return;
            d /= len;

            Vector3 mid = (a + b) * 0.5f;
            Vector3 view = mid.sqrMagnitude > 1e-8f ? mid.normalized : Vector3.forward;
            Vector3 perp = Vector3.Cross(d, view);
            if (perp.sqrMagnitude < 1e-8f) perp = Vector3.Cross(d, Vector3.up);
            perp = perp.normalized;

            // 2차 베지에는 제어점의 '절반'만 지나므로 2배를 준다 → 중간점이 정확히 bulge만큼 부푼다.
            Vector3 ctrl = mid + perp * (bulge * 2f);

            Vector3 prev = a;
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;
                float u = 1f - t;
                Vector3 p = u * u * a + 2f * u * t * ctrl + t * t * b;
                AddLine(prev, p, width, color);
                prev = p;
            }
        }

        /// <summary>원(채움). 눈을 향하는 평면에 그린다.</summary>
        public void AddCircle(Vector3 center, float radius, Color color, int segments = 14)
        {
            if (radius <= 0f || color.a <= 0f) return;

            Vector3 n = center.sqrMagnitude > 1e-8f ? center.normalized : Vector3.forward;
            Vector3 u = Vector3.Cross(n, Vector3.up);
            if (u.sqrMagnitude < 1e-6f) u = Vector3.Cross(n, Vector3.right);
            u = u.normalized;
            Vector3 w = Vector3.Cross(n, u);

            Color32 c32 = color;
            int center0 = _verts.Count;
            _verts.Add(center);
            _colors.Add(c32);

            for (int i = 0; i < segments; i++)
            {
                float ang = i / (float)segments * Mathf.PI * 2f;
                _verts.Add(center + (u * Mathf.Cos(ang) + w * Mathf.Sin(ang)) * radius);
                _colors.Add(c32);
            }

            for (int i = 0; i < segments; i++)
            {
                int a = center0 + 1 + i;
                int b = center0 + 1 + ((i + 1) % segments);
                _tris.Add(center0); _tris.Add(a); _tris.Add(b);
            }
        }

        /// <summary>모아 둔 것을 메시에 올린다. 비어 있으면 메시를 비운다.</summary>
        public void Apply(Mesh mesh)
        {
            if (mesh == null) return;

            mesh.Clear();
            if (_verts.Count == 0) return;

            mesh.SetVertices(_verts);
            mesh.SetColors(_colors);
            mesh.SetTriangles(_tris, 0, false);   // 경계 재계산은 아래에서 직접 준다

            // ★ 자동 바운즈 계산에 맡기면 안 된다 — 메시가 매 프레임 바뀌는데 컬링 경계가
            //   따라 흔들리면 UI가 화면 가장자리에서 통째로 사라진다. 넉넉히 고정한다.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100f);
        }
    }
}
