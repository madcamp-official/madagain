using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>포즈 JSON 스키마 — 에디터(저장·클립빌드)와 런타임(콘솔 재생)이 함께 쓴다.</summary>
    [Serializable] public class PoseBone
    {
        public string  path;    // 뷰모델 루트 기준 경로
        public float[] euler;   // 사람이 읽기 위한 값
        public float[] quat;    // x,y,z,w — 적용은 이걸로
    }

    [Serializable] public class PoseObject
    {
        public string  path;
        public float[] pos;
        public float[] euler;
        public float[] quat;
        public float[] scale;
    }

    /// <summary>포즈를 재현하는 데 필요한 컴포넌트 상태(IK 가중치·손가락 그립 값).</summary>
    [Serializable] public class PoseComponentState
    {
        public string path;     // 컴포넌트가 붙은 오브젝트 경로(루트 기준)
        public string type;     // "HandIK" | "FingerPoser"
        public float  weight;   // HandIK.weight
        public float  grip;     // FingerPoser.grip
    }

    [Serializable] public class PoseFile
    {
        public string       name;
        public string       created;
        public string       root;
        public float[]      rootPos;   // 루트 자체의 로컬 TRS(카메라 기준 배치) — 이것까지 저장해야 "전부"다
        public float[]      rootQuat;
        public float[]      rootScale;
        public PoseBone[]   bones;     // 회전만
        public PoseObject[] objects;   // 칼·그립 — 위치·회전·크기
        public PoseComponentState[] states;   // IK·손가락 컴포넌트 상태
    }

    /// <summary>애니메이션 하나의 타이밍 프로파일. 구간별 시간까지 전부 저장해 다음 실행에도 유지된다.</summary>
    [Serializable] public class PoseTiming
    {
        public string  key;            // 시퀀스 식별자 (예: "slash1_" 또는 "slash1_0|slash1_1|…")
        public float[] segTimes;       // 구간별 시간(초) — 기본포즈 복귀 구간 포함
        public float   hold;           // 마지막 포즈 정지(초)
        public bool    spring;         // (구버전 호환) 포즈 사이 스프링 여부 — 구간별 설정이 없을 때의 기본
        public float   springDamp;
        public float   springFreq;
        public bool    snapReturn;     // 기본포즈 복귀를 순간이동으로

        // ── 구간별 이징 (없거나 짧으면 위의 전역값으로 대체) ──
        public int[]   eases;          // 0 선형 · 1 이즈인 · 2 이즈아웃 · 3 인아웃 · 4 스프링 · 5 계단
        public float[] powers;         // 가속 강도(지수) — 이즈 계열에서 클수록 급가속
        public float[] damps;          // 스프링 감쇠
        public float[] freqs;          // 스프링 진동수
    }

    [Serializable] public class PoseTimingFile
    {
        public List<PoseTiming> items = new List<PoseTiming>();
    }

    public static class PoseMath
    {
        public static Vector3    ToV3(float[] a) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;
        public static Quaternion ToQ(float[] a)  => a != null && a.Length >= 4 ? new Quaternion(a[0], a[1], a[2], a[3]) : Quaternion.identity;
        /// <summary>양끝이 부드럽고 가운데가 빠른 이징.</summary>
        public static float Smooth(float t) => t * t * (3f - 2f * t);
    }
}
