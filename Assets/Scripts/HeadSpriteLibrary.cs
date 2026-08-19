using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 머리(로봇) 스프라이트 로더. `Assets/Resources/Heads/` 아래의 250x250 PNG를 이름으로 읽어온다.
///
/// <b>왜 250x250인가</b> — 기존 리그 몸통 스프라이트(`Parts/Body.png`)가 250x250이고 그게 곧
/// 컴스톡 MK-01의 얼굴 달린 원통이다. 머리 아트를 같은 규격으로 맞춰두면 PPU=100·스케일 1에서
/// 확대·축소 없이 원본 비율이 유지되고, 리그의 다리 배율·콜라이더·무기 소켓 위치를 하나도
/// 건드리지 않고 몸통만 갈아끼울 수 있다(사용자가 준 폴더 이름 `final_250`이 이 규격을 뜻한다).
///
/// <b>애니메이션</b> — 네온아이처럼 여러 장인 머리는 `NeonEye_0` ~ `NeonEye_7` 형식으로 두고
/// <see cref="PartsCatalog.HeadModdingInfo.spriteFrameCount"/>에 장수를 적는다. 눈 색만 바뀌는
/// 같은 그림이라 프레임을 느리게 순환시키면 "눈 색이 천천히 변하는" 컨셉이 된다.
/// 파일 순서는 이미 색상환 순서(빨강→주황→노랑→초록→하늘→파랑→보라→분홍)로 저장해뒀으므로
/// 인덱스를 그냥 증가시키면 자연스럽게 순환한다.
/// </summary>
public static class HeadSpriteLibrary
{
    private const string ResourceFolder = "Heads/";

    /// <summary>스프라이트 이름이 비어 있는 머리가 쓰는 폴백(= 기존 컴스톡 MK-01 몸통).</summary>
    public const string FallbackResourcePath = "Parts/Body";

    // Resources.Load는 내부 캐시가 있지만 매 프레임(애니메이션 갱신) 호출하기엔 무거워서
    // 여기서 한 번 더 캐싱한다. PartIconLibrary와 같은 패턴.
    private static readonly Dictionary<string, Sprite> single_cache = new Dictionary<string, Sprite>();
    private static readonly Dictionary<string, Sprite[]> frames_cache = new Dictionary<string, Sprite[]>();

    /// <summary>씬을 다시 시작할 때 파괴된 스프라이트 참조가 남지 않도록 비운다.</summary>
    public static void ClearCache()
    {
        single_cache.Clear();
        frames_cache.Clear();
    }

    /// <summary>
    /// 이 머리의 대표 스프라이트 1장(UI 아이콘·선택 화면·정비 화면 머리 칸용).
    /// 여러 프레임인 머리는 첫 프레임을 돌려준다.
    /// </summary>
    public static Sprite GetIcon(PartsCatalog.HeadModdingInfo info)
    {
        Sprite[] frames = GetFrames(info);
        if (frames != null && frames.Length > 0) return frames[0];

        return Resources.Load<Sprite>(FallbackResourcePath);
    }

    /// <summary>
    /// 이 머리의 모든 프레임. 단일 이미지면 길이 1, 데이터가 없으면 null.
    /// 프레임이 하나도 로드되지 않으면(파일명 오타 등) null을 돌려주고 호출부가 폴백을 쓰게 한다.
    /// </summary>
    public static Sprite[] GetFrames(PartsCatalog.HeadModdingInfo info)
    {
        if (string.IsNullOrWhiteSpace(info.spriteName)) return null;

        string key = info.spriteName.Trim();
        if (frames_cache.TryGetValue(key, out Sprite[] cached)) return cached;

        Sprite[] result;

        if (info.spriteFrameCount >= 2)
        {
            var list = new List<Sprite>(info.spriteFrameCount);
            for (int i = 0; i < info.spriteFrameCount; i++)
            {
                Sprite frame = Resources.Load<Sprite>(ResourceFolder + key + "_" + i);
                if (frame != null) list.Add(frame);
            }

            // 프레임을 하나도 못 찾았으면 단일 이미지 이름으로 한 번 더 시도한다
            // (spriteFrameCount를 잘못 적어둔 경우의 구제).
            result = list.Count > 0 ? list.ToArray() : LoadSingleAsArray(key);
        }
        else
        {
            result = LoadSingleAsArray(key);
        }

        if (result == null)
        {
            Debug.LogWarning($"머리 스프라이트 '{ResourceFolder}{key}'를 찾을 수 없습니다. " +
                             $"Assets/Resources/Heads/{key}.png가 있고 임포트 타입이 Sprite인지 확인하세요. " +
                             "일단 기본 몸통 이미지로 대체합니다.");
        }

        frames_cache[key] = result;
        return result;
    }

    // ── 현재 선택된 머리 편의 조회 ──────────────────────────────────────────────────
    // 리그(몸통 스프라이트)와 정비·상점·게임오버 UI(머리 아이콘)가 전부 같은 그림을 써야 하므로
    // "지금 고른 머리의 그림" 조회를 여기 한 곳에 모아둔다.

    /// <summary>
    /// 지금 선택된 머리의 몸통 스프라이트. 머리 데이터가 없거나(리그 데모 씬 등) 파일이 없으면
    /// 기존 기본 몸통(<see cref="FallbackResourcePath"/>)을 돌려주므로 반환값이 null이 되지 않는다.
    ///
    /// <paramref name="unscaledTime"/>은 네온아이처럼 여러 프레임인 머리의 색 순환에 쓰인다.
    /// <c>Time.timeScale = 0</c>인 정비·상점 화면에서도 멈추지 않도록 unscaled 시간을 넘겨야 한다.
    /// </summary>
    public static Sprite GetCurrentBodySprite(float unscaledTime)
    {
        if (!HeadEffects.HeadModdingInfoIsValid) return Resources.Load<Sprite>(FallbackResourcePath);
        return GetAnimatedFrame(HeadEffects.CurrentHeadInfo, unscaledTime);
    }

    /// <summary>지금 선택된 머리의 대표 아이콘 1장(정비·상점 화면의 머리 칸용).</summary>
    public static Sprite GetCurrentIcon()
    {
        if (!HeadEffects.HeadModdingInfoIsValid) return Resources.Load<Sprite>(FallbackResourcePath);
        return GetIcon(HeadEffects.CurrentHeadInfo);
    }

    /// <summary>지금 선택된 머리가 여러 프레임(색 순환)을 가지는지. 리그가 매 프레임 갱신할지 판단한다.</summary>
    public static bool CurrentHeadIsAnimated()
    {
        if (!HeadEffects.HeadModdingInfoIsValid) return false;

        Sprite[] frames = GetFrames(HeadEffects.CurrentHeadInfo);
        return frames != null && frames.Length > 1;
    }

    private static Sprite[] LoadSingleAsArray(string key)
    {
        if (!single_cache.TryGetValue(key, out Sprite sprite) || sprite == null)
        {
            sprite = Resources.Load<Sprite>(ResourceFolder + key);
            single_cache[key] = sprite;
        }

        return sprite != null ? new[] { sprite } : null;
    }

    /// <summary>
    /// 경과 시간으로 지금 보여줄 프레임을 고른다. 프레임이 1장이면 항상 그 1장이다.
    /// <paramref name="unscaledTime"/>을 쓰는 이유: 정비·상점 화면은 `Time.timeScale = 0`이라
    /// deltaTime을 쓰면 UI 아이콘의 색 순환이 멈춘다.
    /// </summary>
    public static Sprite GetAnimatedFrame(PartsCatalog.HeadModdingInfo info, float unscaledTime)
    {
        Sprite[] frames = GetFrames(info);
        if (frames == null || frames.Length == 0) return Resources.Load<Sprite>(FallbackResourcePath);
        if (frames.Length == 1) return frames[0];

        float secondsPerFrame = info.spriteFrameSeconds > 0f ? info.spriteFrameSeconds : 1f;
        int index = Mathf.FloorToInt(unscaledTime / secondsPerFrame) % frames.Length;
        if (index < 0) index += frames.Length;

        return frames[index];
    }
}
