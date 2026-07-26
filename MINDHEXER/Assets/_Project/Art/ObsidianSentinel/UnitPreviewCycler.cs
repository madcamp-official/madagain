using UnityEngine;

// Test-only: cycles the biped Animator through Walk -> Run -> Jump so both
// states and the aerial unit's bob can be eyeballed without manual input.
public class UnitPreviewCycler : MonoBehaviour
{
    public Animator bipedAnimator;
    public Transform aerialUnit;
    public float phaseDuration = 3f;
    public float aerialBobHeight = 0.5f;
    public float aerialBobSpeed = 1f;

    private float _timer;
    private int _phase; // 0 = walk, 1 = run, 2 = jump
    private Vector3 _aerialStartPos;

    private void Start()
    {
        if (aerialUnit != null) _aerialStartPos = aerialUnit.position;
    }

    private void Update()
    {
        _timer += Time.deltaTime;
        if (_timer >= phaseDuration)
        {
            _timer = 0f;
            _phase = (_phase + 1) % 3;
            ApplyPhase();
        }

        if (aerialUnit != null)
        {
            var y = _aerialStartPos.y + Mathf.Sin(Time.time * aerialBobSpeed) * aerialBobHeight;
            aerialUnit.position = new Vector3(_aerialStartPos.x, y, _aerialStartPos.z);
        }
    }

    private void ApplyPhase()
    {
        if (bipedAnimator == null) return;
        bipedAnimator.SetBool("IsAirborne", _phase == 2);
        bipedAnimator.SetBool("IsCharging", _phase == 1);
    }
}
