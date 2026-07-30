using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// <see cref="PartsPose"/>의 자세로 파츠 무리를 <b>가속해 밀어 넣는</b> 컴포넌트.
    ///
    /// <para>물리 시뮬을 쓰지 않는다(기초_설계안 §12) — 정해진 자세로 곡선을 타고 간다.
    /// 그래서 결과가 항상 같고, 프레임률이 흔들려도 타이밍이 어긋나지 않는다.</para>
    ///
    /// <para><b>곡선이 "가속"인 이유</b>: 부서지는 것은 시작이 느리고 끝이 빨라야 한다. 반대로
    /// 감속(ease-out)하면 파편이 공기 저항을 받는 것처럼 보여 무게가 사라진다. 기본값은
    /// <c>t²</c>에 가까운 ease-in이고, 곡선을 직접 넣어 바꿀 수 있다.</para>
    ///
    /// <para><b>홈 복원</b>: <see cref="ResetToHome"/>는 방 리셋(<see cref="IRunResettable"/>)에서도
    /// 불린다. 자세가 남아 있으면 재시도할 때 이미 부서진 입구에서 시작한다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class PartsPoser : MonoBehaviour, IRunResettable
    {
        [Header("대상")]
        [Tooltip("자세를 적용할 파츠들의 부모. 비우면 이 오브젝트 자신.")]
        public Transform root;

        [Tooltip("자세 애셋. 첫 자세가 홈이라는 규약이다.")]
        public PartsPose pose;

        [Header("연출")]
        [Tooltip("자세로 가는 데 걸리는 시간(초). 부서짐은 짧게(0.1~0.2), 눌림은 조금 길게.")]
        public float duration = 0.15f;

        [Tooltip("진행 곡선. 기본은 가속(ease-in) — 부서지는 것은 끝이 빨라야 무게가 있다.")]
        public AnimationCurve curve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 2f, 2f));

        [Header("디버그")]
        [Tooltip("에디터에서 이 이름의 자세를 즉시 적용해 눈으로 확인한다. 비우면 아무것도 안 한다.\n" +
                 "★ 확인이 끝나면 반드시 비우고 홈으로 되돌린 뒤 저장할 것.")]
        public string previewSnapshot = "";

        /// <summary>지금 자세 보간이 진행 중인가.</summary>
        public bool Moving => _moving;

        /// <summary>보간이 끝났을 때. 다음 연출을 물릴 때 쓴다.</summary>
        public event System.Action OnArrived;

        readonly List<Transform> _parts = new List<Transform>();
        readonly List<string> _paths = new List<string>();

        // 보간 시작 자세 — 애셋의 홈이 아니라 "출발한 순간의 실제 자세"다.
        // 중간에 다른 자세로 갈아탈 때 튀지 않게 하려면 이쪽이어야 한다.
        readonly List<Vector3> _fromPos = new List<Vector3>();
        readonly List<Quaternion> _fromRot = new List<Quaternion>();
        readonly List<Vector3> _fromScale = new List<Vector3>();

        PartsPose.Snapshot _to;
        bool _moving;
        float _t, _dur;
        string _lastPreview = "";

        Transform Root => root != null ? root : transform;

        void Awake() => Collect();

        /// <summary>
        /// 루트 밑의 파츠와 그 상대 경로를 모은다. <b>루트 자신은 넣지 않는다</b> — 루트를 옮기면
        /// 자식이 전부 따라가므로 자세가 이중 적용된다.
        /// </summary>
        public void Collect()
        {
            _parts.Clear(); _paths.Clear();
            Transform r = Root;
            var all = r.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == r) continue;
                _parts.Add(all[i]);
                _paths.Add(RelativePath(r, all[i]));
            }
        }

        /// <summary>루트 기준 상대 경로. 이름이 겹쳐도 유일하다.</summary>
        public static string RelativePath(Transform root, Transform t)
        {
            var sb = new System.Text.StringBuilder(t.name);
            Transform p = t.parent;
            while (p != null && p != root)
            {
                sb.Insert(0, '/').Insert(0, p.name);
                p = p.parent;
            }
            return sb.ToString();
        }

        /// <summary>
        /// 지금 자세를 <paramref name="snapshotName"/>이라는 이름으로 애셋에 기록한다(에디터 전용 흐름).
        /// 같은 이름이 있으면 덮어쓴다.
        /// </summary>
        public void Capture(string snapshotName)
        {
            if (pose == null) { Debug.LogError($"[파츠자세] {name}: 자세 애셋이 없습니다.", this); return; }
            if (_parts.Count == 0) Collect();

            var snap = pose.Find(snapshotName);
            if (snap == null)
            {
                snap = new PartsPose.Snapshot { name = snapshotName };
                pose.snapshots.Add(snap);
            }

            snap.parts.Clear();
            for (int i = 0; i < _parts.Count; i++)
                snap.parts.Add(new PartsPose.PartPose
                {
                    path = _paths[i],
                    pos = _parts[i].localPosition,
                    rot = _parts[i].localRotation,
                    scale = _parts[i].localScale,
                });

            Debug.Log($"[파츠자세] {name}: '{snapshotName}' 캡처 — 파츠 {snap.parts.Count}개");
        }

        /// <summary>자세를 <b>즉시</b> 적용한다(보간 없음). 캡처·복원·에디터 확인용.</summary>
        public void ApplyImmediate(PartsPose.Snapshot snap)
        {
            if (snap == null) return;
            if (_parts.Count == 0) Collect();

            for (int i = 0; i < _parts.Count; i++)
            {
                var p = PartsPose.FindPart(snap, _paths[i]);
                if (p == null) continue;   // 애셋에 없는 파츠는 건드리지 않는다
                _parts[i].localPosition = p.pos;
                _parts[i].localRotation = p.rot;
                _parts[i].localScale = p.scale;
            }
            _moving = false;
        }

        /// <summary>이름으로 자세를 향해 가속 보간을 시작한다. 시간을 안 주면 <see cref="duration"/>.</summary>
        public void GoTo(string snapshotName, float seconds = -1f)
        {
            if (pose == null) { Debug.LogError($"[파츠자세] {name}: 자세 애셋이 없습니다.", this); return; }

            var snap = pose.Find(snapshotName);
            if (snap == null)
            {
                Debug.LogError($"[파츠자세] {name}: '{snapshotName}' 자세가 애셋에 없습니다.", this);
                return;
            }
            GoTo(snap, seconds);
        }

        public void GoTo(PartsPose.Snapshot snap, float seconds = -1f)
        {
            if (snap == null) return;
            if (_parts.Count == 0) Collect();

            // 출발점을 "지금 자세"로 굳힌다 — 애셋의 홈에서 출발하면 중간에 갈아탈 때 튄다.
            _fromPos.Clear(); _fromRot.Clear(); _fromScale.Clear();
            for (int i = 0; i < _parts.Count; i++)
            {
                _fromPos.Add(_parts[i].localPosition);
                _fromRot.Add(_parts[i].localRotation);
                _fromScale.Add(_parts[i].localScale);
            }

            _to = snap;
            _dur = seconds > 0f ? seconds : duration;
            _t = 0f;
            _moving = true;
        }

        /// <summary>홈(첫 자세)으로 즉시 되돌린다.</summary>
        public void ResetToHome()
        {
            if (pose == null) return;
            ApplyImmediate(pose.Home);
        }

        void Update()
        {
            if (!Application.isPlaying) { EditorPreview(); return; }
            if (!_moving || _to == null) return;

            _t += Time.deltaTime;
            float u = _dur > 1e-4f ? Mathf.Clamp01(_t / _dur) : 1f;
            float k = curve != null ? curve.Evaluate(u) : u;

            for (int i = 0; i < _parts.Count; i++)
            {
                var p = PartsPose.FindPart(_to, _paths[i]);
                if (p == null) continue;
                _parts[i].localPosition = Vector3.LerpUnclamped(_fromPos[i], p.pos, k);
                _parts[i].localRotation = Quaternion.SlerpUnclamped(_fromRot[i], p.rot, k);
                _parts[i].localScale = Vector3.LerpUnclamped(_fromScale[i], p.scale, k);
            }

            if (u >= 1f)
            {
                _moving = false;
                OnArrived?.Invoke();
            }
        }

        /// <summary>
        /// 에디터에서 <see cref="previewSnapshot"/>이 바뀐 프레임에만 1회 적용한다.
        /// 매 프레임 써넣으면 씬이 계속 더러워지고, 손으로 파츠를 잡아 볼 수도 없다.
        /// </summary>
        void EditorPreview()
        {
            if (previewSnapshot == _lastPreview) return;
            _lastPreview = previewSnapshot;

            if (string.IsNullOrEmpty(previewSnapshot) || pose == null) return;
            var snap = pose.Find(previewSnapshot);
            if (snap == null) { Debug.LogWarning($"[파츠자세] '{previewSnapshot}' 자세가 없습니다.", this); return; }
            ApplyImmediate(snap);
        }

        // ── IRunResettable ────────────────────────────────────────────────
        void IRunResettable.ResetForRestart() => ResetToHome();
    }
}
