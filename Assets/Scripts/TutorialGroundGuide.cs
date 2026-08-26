using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// <b>첫 플레이의 웨이브 1 바닥에 조작 안내 그림 3장을 깔아주는 튜토리얼</b>
/// (2026-08-26 사용자 요청: "튜토리얼을 한번도 진행하지 않았을때, 게임 시작하면 튜토리얼
/// 이미지가 인게임 웨이브 1 바닥에 나왔으면 좋겠어. 들어가면 바로 보이는 위치에").
///
/// 설계 요점:
/// - <b>씬에 배치하지 않는다.</b> <see cref="MusicManager"/>/<see cref="SFXManager"/>와 같은
///   부트스트랩 방식이라 씬 파일을 건드리지 않고, 씬을 다시 로드해도(재시작) 알아서 다시 판단한다.
/// - <b>이벤트 대신 폴링으로 판단한다.</b> 웨이브 1은 <see cref="WaveManager.Start"/>(또는
///   데이터 로드 완료 콜백)에서 시작되는데, 이 오브젝트가 만들어지는 시점과의 순서가 보장되지
///   않아 <c>OnWaveStarted</c>를 구독하면 첫 웨이브를 통째로 놓칠 수 있다. 매 프레임 웨이브
///   번호만 보면 순서 문제가 아예 없다.
/// - 안내판은 <b>바닥(맵) 위, 캐릭터/적 아래</b>에 그려진다(맵은 sortingOrder 0, 캐릭터 조각은
///   3 이상이므로 1을 쓴다).
///
/// "한 번도 진행하지 않았을 때"의 판단은 <see cref="PlayerPrefs"/> 한 칸이다. 웨이브 1을
/// <b>끝냈을 때</b> 기록하므로, 시작하자마자 껐다면 다음 실행에도 다시 나온다.
/// </summary>
public class TutorialGroundGuide : MonoBehaviour
{
    /// <summary>PlayerPrefs 키(프로젝트 관례: comstock_ 접두사).</summary>
    private const string SeenPrefsKey = "comstock_tutorial_seen";

    /// <summary>Resources 안의 안내 그림. <b>배열 순서가 곧 화면 왼쪽→오른쪽 순서다.</b></summary>
    private static readonly string[] GuideSprites =
    {
        "Tutorial/Move",   // 1.png - WASD 이동
        "Tutorial/Skill",  // 2.png - 스페이스바 구르기
        "Tutorial/Attack"  // 3.png - 자동 공격
    };

    /// <summary>안내판 한 장의 세로 크기(월드 유닛). 가로는 원본 비율로 따라간다.</summary>
    private const float GuideHeight = 2.6f;

    /// <summary>안내판 사이 간격(월드 유닛).</summary>
    private const float GuideGap = 0.55f;

    /// <summary>
    /// 안내판 줄의 세로 위치. 플레이어 시작 위치는 (0,0)이고 카메라 세로 반경은 5.4라
    /// 이 값이면 <b>캐릭터에 가리지 않으면서</b> 첫 화면 안에 전부 들어온다.
    /// </summary>
    private const float GuideCenterY = -2.7f;

    /// <summary>맵(0) 위, 캐릭터 조각(3 이상) 아래.</summary>
    private const int GuideSortingOrder = 1;

    private static bool scene_hook_installed;

    /// <summary>이 기기에서 튜토리얼을 이미 한 번 봤는지.</summary>
    public static bool Seen => PlayerPrefs.GetInt(SeenPrefsKey, 0) != 0;

    /// <summary>튜토리얼을 봤다고 기록한다(웨이브 1을 마쳤을 때).</summary>
    public static void MarkSeen()
    {
        if (Seen) return;

        PlayerPrefs.SetInt(SeenPrefsKey, 1);
        PlayerPrefs.Save();
    }

    /// <summary>검수/디버그용 - 다시 보고 싶을 때 기록을 지운다.</summary>
    public static void ClearSeen()
    {
        PlayerPrefs.DeleteKey(SeenPrefsKey);
        PlayerPrefs.Save();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (!scene_hook_installed)
        {
            scene_hook_installed = true;
            SceneManager.sceneLoaded += (scene, mode) => TrySpawn();
        }

        TrySpawn();
    }

    private static void TrySpawn()
    {
        if (Seen) return;

        // 전투 씬에만 만든다(타이틀 씬에는 WaveManager가 없다).
        if (FindFirstObjectByType<WaveManager>() == null) return;

        new GameObject("TutorialGroundGuide").AddComponent<TutorialGroundGuide>();
    }

    private bool shown;

    private void Update()
    {
        if (!shown)
        {
            // 웨이브 1이 실제로 시작한 뒤에 깔린다(데이터 로드가 늦으면 WaveNumber는 잠깐 0이다).
            if (RunState.WaveNumber != 1) return;

            SpawnGuides();
            shown = true;
            return;
        }

        // 웨이브 1이 끝나면(정비/상점 진입 또는 웨이브 2 시작) 역할이 끝난다.
        if (RunState.WaveNumber > 1 || GameFlowManager.IsIntermission || GameOverManager.IsGameOver)
        {
            if (RunState.WaveNumber > 1 || GameFlowManager.IsIntermission) MarkSeen();
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 세 장을 왼쪽→오른쪽 순서로 한 줄에 깐다. 원본 그림의 가로세로 비율이 제각각이라
    /// (586x665 / 669x665 / 745x665) <b>세로를 맞추고 가로는 비율대로</b> 두고,
    /// 전체 폭을 먼저 구해 플레이어 시작 위치(0,0) 기준으로 가운데 정렬한다.
    /// </summary>
    private void SpawnGuides()
    {
        var sprites = new Sprite[GuideSprites.Length];
        var widths = new float[GuideSprites.Length];
        float total = 0f;
        int count = 0;

        for (int i = 0; i < GuideSprites.Length; i++)
        {
            sprites[i] = Resources.Load<Sprite>(GuideSprites[i]);
            if (sprites[i] == null)
            {
                Debug.LogWarning($"[TutorialGroundGuide] 안내 그림을 찾지 못했습니다: Resources/{GuideSprites[i]}");
                continue;
            }

            Vector2 size = sprites[i].bounds.size;
            widths[i] = size.y > 0.0001f ? GuideHeight * (size.x / size.y) : GuideHeight;
            total += widths[i];
            count++;
        }

        if (count == 0) { Destroy(gameObject); return; }

        total += GuideGap * (count - 1);

        float cursor = -total * 0.5f;
        for (int i = 0; i < sprites.Length; i++)
        {
            if (sprites[i] == null) continue;

            var go = new GameObject($"TutorialGuide_{i + 1}");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(cursor + widths[i] * 0.5f, GuideCenterY, 0f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprites[i];
            sr.sortingOrder = GuideSortingOrder;

            // 원본 해상도/PPU가 제각각이라 스프라이트의 실제 월드 크기로 나눠서 배율을 구한다.
            Vector2 size = sprites[i].bounds.size;
            go.transform.localScale = new Vector3(
                size.x > 0.0001f ? widths[i] / size.x : 1f,
                size.y > 0.0001f ? GuideHeight / size.y : 1f,
                1f);

            cursor += widths[i] + GuideGap;
        }
    }
}
