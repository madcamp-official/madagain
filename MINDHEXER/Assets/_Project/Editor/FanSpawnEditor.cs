using UnityEditor;
using UnityEngine;
using Game.View;

namespace Game.EditorTools
{
    /// <summary>
    /// FanSpawn 커스텀 에디터. 씬에서 <b>입(mouth)을 드래그</b>해 정확히 배치한다 —
    /// 자동 계산이 안 맞거나 뒤집힌 팬이라 각도로 못 잡을 때, 개구부 위치를 손으로 맞춘다.
    /// 입 위치는 mouthLocal(로컬)로 저장되므로 팬이 움직여도 따라간다.
    /// </summary>
    [CustomEditor(typeof(FanSpawn))]
    public class FanSpawnEditor : Editor
    {
        void OnSceneGUI()
        {
            var fs = (FanSpawn)target;
            if (fs == null) return;

            Vector3 world = fs.Mouth;
            Quaternion rot = fs.ExitDir.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(fs.ExitDir.normalized)
                : fs.transform.rotation;

            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.PositionHandle(world, rot);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(fs, "Fan 입 이동");
                fs.mouthLocal = fs.transform.InverseTransformPoint(moved);
                EditorUtility.SetDirty(fs);
            }

            // 입 표식 + 출구 방향
            Handles.color = new Color(1f, 0.5f, 0.1f);
            Handles.SphereHandleCap(0, fs.Mouth, Quaternion.identity, 0.18f, EventType.Repaint);
            Handles.DrawLine(fs.Mouth, fs.Mouth + fs.ExitDir.normalized * 2f);

            // 드롭 링크 착지점까지 라인(있으면)
            Handles.color = new Color(0.2f, 0.8f, 1f, 0.8f);
            foreach (var l in fs.dropLinks)
                if (l != null) Handles.DrawLine(fs.Mouth, l.Low);

            Handles.Label(fs.Mouth + Vector3.up * 0.25f, "Fan 입(드래그)");
        }
    }
}
