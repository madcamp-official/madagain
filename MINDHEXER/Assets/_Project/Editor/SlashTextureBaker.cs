using UnityEngine;
using UnityEditor;

namespace Game.EditorTools
{
    /// <summary>
    /// 베기 이펙트용 텍스처를 구워낸다(외부 에셋 불필요).
    ///
    /// 프로 슬래시 VFX의 구조를 따른다:
    ///   형태 마스크(가장자리가 부드럽게 번짐) × 노이즈1(스크롤) × 노이즈2(스크롤)
    ///   → 두께감·볼륨은 "기하학"이 아니라 "부드러운 마스크 + 곱해진 노이즈"에서 나온다.
    ///
    /// 메뉴: Tools/이펙트/베기 텍스처 굽기
    /// 결과: Assets/_Project/Art/VFX/Resources/  (런타임 Resources.Load용)
    /// </summary>
    public static class SlashTextureBaker
    {
        const string Dir = "Assets/_Project/Art/VFX/Resources";

        [MenuItem("Tools/이펙트/베기 텍스처 굽기")]
        static void Bake()
        {
            EnsureFolder();

            BakeShape($"{Dir}/Slash_Shape.png", 1024, 256);
            BakeNoise($"{Dir}/Slash_Noise1.png", 256, seed: 1337, baseperiod: 4, octaves: 5, gain: 0.55f);
            BakeNoise($"{Dir}/Slash_Noise2.png", 256, seed: 9001, baseperiod: 8, octaves: 4, gain: 0.5f);

            AssetDatabase.Refresh();
            ConfigureImport($"{Dir}/Slash_Shape.png", clampU: true);
            ConfigureImport($"{Dir}/Slash_Noise1.png", clampU: false);
            ConfigureImport($"{Dir}/Slash_Noise2.png", clampU: false);
            AssetDatabase.Refresh();

            Debug.Log($"[베기 텍스처] 구움 완료 → {Dir}\n  Slash_Shape / Slash_Noise1 / Slash_Noise2");
        }

        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Art"))
                AssetDatabase.CreateFolder("Assets/_Project", "Art");
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Art/VFX"))
                AssetDatabase.CreateFolder("Assets/_Project/Art", "VFX");
            if (!AssetDatabase.IsValidFolder(Dir))
                AssetDatabase.CreateFolder("Assets/_Project/Art/VFX", "Resources");
        }

        // ── 형태 마스크 ──
        // u = 스윙 방향, v = 반경(0=손잡이, 1=칼끝).
        // 모든 경계를 "넓은 그라디언트"로 만든다 — 이게 두께감의 근원.
        static void BakeShape(string path, int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color[w * h];

            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h;

                // 반경 방향: 안쪽은 아주 부드럽게 사라지고, 바깥은 넓게 번지며, 칼끝 근처에 밝은 띠
                float inner = Smooth01(Mathf.InverseLerp(0.02f, 0.52f, v));         // 손잡이쪽 넓은 페이드
                float outer = 1f - Smooth01(Mathf.InverseLerp(0.70f, 1.00f, v));    // 칼끝쪽 넓은 페이드
                float rim   = Mathf.Exp(-Mathf.Pow((v - 0.78f) / 0.11f, 2f));       // 바깥 밝은 띠(부드러운 가우시안)
                float radial = Mathf.Clamp01(inner * outer + rim * inner * 0.75f);

                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w;
                    // 스윙 방향: 양 끝을 넓게 테이퍼(딱 잘리지 않게)
                    float sweep = Smooth01(Mathf.InverseLerp(0.00f, 0.30f, u))
                                * (1f - Smooth01(Mathf.InverseLerp(0.80f, 1.00f, u)));

                    float a = Mathf.Clamp01(radial * sweep);
                    a = Mathf.Pow(a, 0.85f);   // 살짝 부풀려 두툼하게
                    px[y * w + x] = new Color(a, a, a, a);
                }
            }

            tex.SetPixels(px);
            tex.Apply();
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        // ── 심리스 노이즈(주기적 value noise fbm) ──
        static void BakeNoise(string path, int size, int seed, int baseperiod, int octaves, float gain)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size;
                float v = (y + 0.5f) / size;

                float sum = 0f, amp = 1f, norm = 0f;
                int period = baseperiod;
                for (int o = 0; o < octaves; o++)
                {
                    sum  += amp * PeriodicValueNoise(u * period, v * period, period, seed + o * 131);
                    norm += amp;
                    amp  *= gain;
                    period *= 2;
                }
                float n = Mathf.Clamp01(sum / Mathf.Max(norm, 1e-4f));
                n = Mathf.SmoothStep(0f, 1f, n);   // 대비 살짝
                px[y * size + x] = new Color(n, n, n, n);
            }

            tex.SetPixels(px);
            tex.Apply();
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        /// <summary>격자를 period로 감싸 타일링이 이어지는 value noise.</summary>
        static float PeriodicValueNoise(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);

            float v00 = Hash(x0,     y0,     period, seed);
            float v10 = Hash(x0 + 1, y0,     period, seed);
            float v01 = Hash(x0,     y0 + 1, period, seed);
            float v11 = Hash(x0 + 1, y0 + 1, period, seed);

            return Mathf.Lerp(Mathf.Lerp(v00, v10, fx), Mathf.Lerp(v01, v11, fx), fy);
        }

        static float Hash(int x, int y, int period, int seed)
        {
            x = ((x % period) + period) % period;   // 주기적 래핑 → 심리스
            y = ((y % period) + period) % period;
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 1274126177;
                h = (h ^ (h >> 13)) * 1274126177;
                h = h ^ (h >> 16);
                return (h & 0x7fffffff) / (float)0x7fffffff;
            }
        }

        static float Smooth01(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        static void ConfigureImport(string path, bool clampU)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) return;
            ti.textureType = TextureImporterType.Default;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = false;
            ti.sRGBTexture = false;                 // 마스크/노이즈는 선형 데이터
            ti.mipmapEnabled = true;
            ti.wrapMode = clampU ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            ti.filterMode = FilterMode.Bilinear;
            ti.SaveAndReimport();
        }
    }
}
