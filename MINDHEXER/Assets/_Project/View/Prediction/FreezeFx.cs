using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.View
{
    /// <summary>예측 프리뷰와 리듬 실행의 화면 색감 전환을 담당한다.</summary>
    public class FreezeFx
    {
        Volume volume;
        ColorAdjustments colorAdjustments;
        Vignette vignette;
        float targetWeight;
        float currentWeight;
        float targetExecution;
        float currentExecution;

        public void Init()
        {
            var go = new GameObject("PredictFreezeVolume");
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100f;
            volume.weight = 0f;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            volume.sharedProfile = profile;
            colorAdjustments = profile.Add<ColorAdjustments>(true);
            colorAdjustments.saturation.overrideState = true;
            colorAdjustments.colorFilter.overrideState = true;
            colorAdjustments.postExposure.overrideState = true;

            vignette = profile.Add<Vignette>(true);
            vignette.intensity.overrideState = true;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = PredictionConfig.FxVignetteSmooth;
            vignette.color.overrideState = true;
            ApplyStyle(0f);
        }

        public void EnableOnCamera(Camera cam)
        {
            if (cam == null) return;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data != null) data.renderPostProcessing = true;
        }

        public void SetActive(bool on) => targetWeight = on ? 1f : 0f;
        public void SetExecution(bool on) => targetExecution = on ? 1f : 0f;

        public void Update()
        {
            if (volume == null) return;
            float dt = Time.unscaledDeltaTime;
            currentWeight = Mathf.MoveTowards(
                currentWeight, targetWeight, PredictionConfig.FxWeightSpeed * dt);
            currentExecution = Mathf.MoveTowards(
                currentExecution, targetExecution, PredictionConfig.FxWeightSpeed * 0.55f * dt);
            ApplyStyle(currentExecution);
            volume.weight = currentWeight;
        }

        void ApplyStyle(float t)
        {
            if (colorAdjustments == null || vignette == null) return;
            colorAdjustments.saturation.value = Mathf.Lerp(PredictionConfig.FxSaturation, 18f, t);
            colorAdjustments.colorFilter.value = Color.Lerp(
                PredictionConfig.FxTint, PredictionConfig.ExecutionFxTint, t);
            colorAdjustments.postExposure.value = Mathf.Lerp(PredictionConfig.FxExposure, 0.08f, t);
            vignette.intensity.value = Mathf.Lerp(PredictionConfig.FxVignette, 0.34f, t);
            vignette.color.value = Color.Lerp(
                PredictionConfig.FxVignetteColor, PredictionConfig.ExecutionFxVignetteColor, t);
        }
    }
}
