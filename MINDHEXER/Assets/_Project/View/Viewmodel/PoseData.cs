using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>포즈 JSON 스키마 — 에디터(저장·클립빌드)와 런타임(콘솔 재생)이 함께 쓴다. (Precog에서 포팅, 변경 없음)</summary>
    [Serializable] public class PoseBone
    {
        public string  path;
        public float[] euler;
        public float[] quat;
    }

    [Serializable] public class PoseObject
    {
        public string  path;
        public float[] pos;
        public float[] euler;
        public float[] quat;
        public float[] scale;
    }

    [Serializable] public class PoseComponentState
    {
        public string path;
        public string type;     // "HandIK" | "FingerPoser"
        public float  weight;
        public float  grip;
    }

    [Serializable] public class PoseFile
    {
        public string       name;
        public string       created;
        public string       root;
        public float[]      rootPos;
        public float[]      rootQuat;
        public float[]      rootScale;
        public PoseBone[]   bones;
        public PoseObject[] objects;
        public PoseComponentState[] states;
    }

    [Serializable] public class PoseTiming
    {
        public string  key;
        public float[] segTimes;
        public float   hold;
        public bool    spring;
        public float   springDamp;
        public float   springFreq;
        public bool    snapReturn;

        public int[]   eases;
        public float[] powers;
        public float[] damps;
        public float[] freqs;
    }

    [Serializable] public class PoseTimingFile
    {
        public List<PoseTiming> items = new List<PoseTiming>();
    }

    public static class PoseMath
    {
        public static Vector3    ToV3(float[] a) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;
        public static Quaternion ToQ(float[] a)  => a != null && a.Length >= 4 ? new Quaternion(a[0], a[1], a[2], a[3]) : Quaternion.identity;
        public static float Smooth(float t) => t * t * (3f - 2f * t);
    }
}
