using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// <see cref="ProceduralCharacterRig"/> 확인용 데모 조작기. 실제 게임 로직과는 무관하며
/// JointRigDemo 씬에서만 쓴다.
///
/// - A/D(또는 좌우 화살표)로 이동, 손을 떼면 대기 자세로 돌아간다
/// - Tab: 자동 왕복 걷기 on/off
/// - 화면 왼쪽 슬라이더로 보폭/발 높이/스탠스 비율/바운스/골반 높이를 실시간으로 바꿔본다
/// </summary>
[RequireComponent(typeof(ProceduralCharacterRig))]
public class ProceduralRigDemo : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 3.0f;
    [SerializeField] private float acceleration = 12f;
    [Tooltip("자동 왕복 시 돌아서는 x 범위")]
    [SerializeField] private float patrolHalfWidth = 2.4f;
    [SerializeField] private bool autoWalk = true;

    private ProceduralCharacterRig rig;
    private float velocityX;
    private int patrolDir = 1;

    private void Awake()
    {
        rig = GetComponent<ProceduralCharacterRig>();
        // 이 씬에는 GameDataManager가 없어서 아무도 이걸 켜주지 않는다. 꺼져 있으면 Editor 창이
        // 백그라운드일 때 게임 시간이 거의 안 흘러 애니메이션이 멈춘 것처럼 보인다.
        Application.runInBackground = true;
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
            autoWalk = !autoWalk;

        float input = 0f;
        if (Keyboard.current != null)
        {
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) input -= 1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) input += 1f;
        }

        if (Mathf.Approximately(input, 0f) && autoWalk)
        {
            if (transform.position.x > patrolHalfWidth) patrolDir = -1;
            else if (transform.position.x < -patrolHalfWidth) patrolDir = 1;
            input = patrolDir;
        }

        velocityX = Mathf.MoveTowards(velocityX, input * moveSpeed, acceleration * dt);

        transform.position += new Vector3(velocityX * dt, 0f, 0f);
        rig.SetLocomotion(new Vector2(velocityX, 0f));
    }

    private void OnGUI()
    {
        const float w = 300f;
        GUILayout.BeginArea(new Rect(12, 12, w, 450), GUI.skin.box);
        GUILayout.Label("절차적 관절 리그 데모");
        GUILayout.Label("A/D 이동   ·   Tab 자동왕복 " + (autoWalk ? "켬" : "끔"));
        GUILayout.Label($"속도 {rig.CurrentSpeed:0.00} u/s   ·   다리 길이 {rig.LegLength:0.00} u");

        // 길이 계열은 전부 '다리 길이에 대한 배수'라 legScale을 바꿔도 그대로 통한다
        rig.StrideRatio = Slider("보폭 (다리 배수)", rig.StrideRatio, 0.2f, 2.0f);
        rig.StepHeightRatio = Slider("발 드는 높이", rig.StepHeightRatio, 0f, 0.8f);
        rig.StanceRatio = Slider("지면 접촉 비율", rig.StanceRatio, 0.3f, 0.85f);
        rig.BobRatio = Slider("몸통 상하 흔들림", rig.BobRatio, 0f, 0.3f);
        rig.HipHeightRatio = Slider("골반 높이(무릎 굽힘)", rig.HipHeightRatio, 0.6f, 0.99f);
        // 아래 둘은 뼈 길이가 바뀌므로 리그를 다시 조립한다(값이 실제로 변할 때만)
        rig.LegScale = Slider("다리 파츠 크기", rig.LegScale, 0.1f, 0.8f);
        rig.HipSeparation = Slider("고관절 좌우 간격", rig.HipSeparation, 0f, 2.0f);
        rig.FrontLegPullBack = Slider("앞다리 뒤로 당기기", rig.FrontLegPullBack, 0f, 0.3f);
        moveSpeed = Slider("이동 속도", moveSpeed, 0f, 8f);

        GUILayout.Space(6);
        GUILayout.Label("발 각도 (뒤꿈치착지 → 발끝밀기 → 스윙)");
        rig.HeelStrikeDegrees = Slider("뒤꿈치착지 각도", rig.HeelStrikeDegrees, -10f, 30f);
        rig.PushOffDegrees = Slider("발끝밀기 각도", rig.PushOffDegrees, -40f, 10f);
        rig.ToeLiftDegrees = Slider("스윙 중 추가로 드는 각도", rig.ToeLiftDegrees, 0f, 40f);

        if (GUILayout.Button(rig.KneeBendsForward ? "무릎: 앞으로 굽음" : "무릎: 뒤로 굽음"))
            rig.KneeBendsForward = !rig.KneeBendsForward;

        GUILayout.EndArea();
    }

    private static float Slider(string label, float value, float min, float max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label($"{label} {value:0.00}", GUILayout.Width(150));
        float v = GUILayout.HorizontalSlider(value, min, max);
        GUILayout.EndHorizontal();
        return v;
    }
}
