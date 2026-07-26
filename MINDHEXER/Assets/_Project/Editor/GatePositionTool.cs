using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Game.View;

namespace Game.EditorTools
{
    /// <summary>
    /// 게이트(ArenaGate) 열림/닫힘 위치를 <b>선택 → 메뉴</b>로 기록한다.
    /// 게이트 하나, 여러 개, 또는 Arena_N_Content를 통째로 선택해도 하위 게이트를 전부 잡는다.
    ///
    /// 워크플로 (게이트마다):
    ///   1) 문을 <b>막는 자리</b>에 놓고 → 메뉴 ①(닫힘 기록)
    ///   2) 문을 <b>열린 자리</b>로 옮기고 → 메뉴 ②(열림 기록)
    ///   3) 미리보기로 확인.
    /// </summary>
    static class GatePositionTool
    {
        [MenuItem("Tools/게이트/① 선택: 현재 문 위치 = 닫힘으로 기록")]
        static void RecordClosed()
        {
            int n = 0;
            foreach (var g in Gates())
            {
                var d = g.door != null ? g.door : g.transform;
                Undo.RecordObject(g, "닫힘 기록");
                g.closedLocalPos = d.localPosition;
                g.positionsRecorded = true;
                EditorUtility.SetDirty(g);
                Debug.Log($"[게이트] {g.name}: 닫힘 = {g.closedLocalPos:F2}", g);
                n++;
            }
            Report(n, "닫힘 기록");
        }

        [MenuItem("Tools/게이트/② 선택: 현재 문 위치 = 열림으로 기록")]
        static void RecordOpen()
        {
            int n = 0;
            foreach (var g in Gates())
            {
                var d = g.door != null ? g.door : g.transform;
                Undo.RecordObject(g, "열림 기록");
                g.openOffset = d.localPosition - g.closedLocalPos;   // 열림 − 닫힘
                g.positionsRecorded = true;
                EditorUtility.SetDirty(g);
                Debug.Log($"[게이트] {g.name}: 열림오프셋 = {g.openOffset:F2}", g);
                n++;
            }
            Report(n, "열림 기록");
        }

        [MenuItem("Tools/게이트/문 → 닫힘 위치로 이동(미리보기)")]
        static void PreviewClosed()
        {
            int n = 0;
            foreach (var g in Gates())
            {
                var d = g.door != null ? g.door : g.transform;
                Undo.RecordObject(d, "문 이동");
                d.localPosition = g.closedLocalPos;
                EditorUtility.SetDirty(d);
                n++;
            }
            Report(n, "닫힘 위치로 이동");
        }

        [MenuItem("Tools/게이트/문 → 열림 위치로 이동(미리보기)")]
        static void PreviewOpen()
        {
            int n = 0;
            foreach (var g in Gates())
            {
                var d = g.door != null ? g.door : g.transform;
                Undo.RecordObject(d, "문 이동");
                d.localPosition = g.closedLocalPos + g.openOffset;
                EditorUtility.SetDirty(d);
                n++;
            }
            Report(n, "열림 위치로 이동");
        }

        // 선택된 오브젝트(및 하위)에서 ArenaGate 전부 수집(중복 제거).
        static List<ArenaGate> Gates()
        {
            var list = new List<ArenaGate>();
            foreach (var go in Selection.gameObjects)
                foreach (var g in go.GetComponentsInChildren<ArenaGate>(true))
                    if (!list.Contains(g)) list.Add(g);
            return list;
        }

        static void Report(int n, string what)
        {
            if (n == 0) Debug.LogWarning("[게이트] 선택된 오브젝트에 ArenaGate가 없습니다. 게이트나 Arena_N_Content를 선택하세요.");
            else        Debug.Log($"[게이트] {n}개 게이트 '{what}' 완료.");
        }
    }
}
