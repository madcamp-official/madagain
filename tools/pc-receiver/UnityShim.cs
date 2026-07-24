// Unity 없이 shared 소스를 컴파일하기 위한 최소 UnityEngine 타입 shim.
// shared 코드가 실제로 사용하는 멤버만 정의한다. (측정 도구 전용)
namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2(0f, 0f);
        public override string ToString() => $"({x:0.###}, {y:0.###})";
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(0f, 0f, 0f);
        public override string ToString() => $"({x:0.###}, {y:0.###}, {z:0.###})";
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Quaternion identity => new Quaternion(0f, 0f, 0f, 1f);
        public Vector3 eulerAngles => new Vector3(x, y, z); // 더미(shared ToString에서만 참조)
        public override string ToString() => $"({x:0.###}, {y:0.###}, {z:0.###}, {w:0.###})";
    }
}
