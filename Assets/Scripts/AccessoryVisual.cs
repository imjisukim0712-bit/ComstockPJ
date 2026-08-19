using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 구매한 악세사리(2026-08-19 Phase D)를 캐릭터에 실제로 그려주는 컴포넌트.
/// <see cref="PlayerRobotController"/>가 HitFlash / DiscEffectRuntime과 같은 방식으로 Awake에서
/// 자동으로 붙인다(씬 수정 없음).
///
/// <b>붙이는 자리</b> — <see cref="ProceduralCharacterRig.BodyVisual"/>(몸통=머리 스프라이트)의
/// 자식으로 만든다. 리그가 매 프레임 몸통 스프라이트를 강제 복구하므로(RestorePartSprites)
/// 몸통 렌더러 자체를 건드리면 다음 프레임에 지워진다 - 반드시 별도 자식 오브젝트여야 한다
/// (네온아이 색 순환에서 겪은 함정, 2026-08-19).
///
/// <b>크기·위치 기준</b> — 스프라이트의 <b>실제로 픽셀이 그려진 영역</b>(타이트 메시 정점의
/// 최소/최대)을 쓴다. 머리 12종과 악세사리 6종 모두 원본 PNG의 여백이 제각각이라(머리는
/// 250x250 안에서 좌우 6~68px, 악세사리는 128x128 안에서 10~40px씩 비어 있다) 스프라이트
/// 사각형을 그대로 쓰면 왕관이 머리 위에 붕 뜨거나 파고든다. 여백을 뺀 영역을 쓰면
/// 머리를 바꿔도(=몸통 스프라이트가 바뀌어도) 위치가 저절로 따라온다.
/// </summary>
[DisallowMultipleComponent]
// 리그(ProceduralCharacterRig, 10000)가 몸통 자세·flipX를 확정한 뒤에 배치해야 한 프레임 밀리지 않는다.
[DefaultExecutionOrder(10100)]
public class AccessoryVisual : MonoBehaviour
{
    private const string ChildNamePrefix = "Accessory_";

    /// <summary>스택형끼리 겹치는 비율. 0이면 그림의 최외곽끼리 한 점에서만 닿아 "탑처럼 떠 있는"
    /// 모양이 된다(첫 실측 스크린샷). 서로 조금 파고들게 해야 얹혀 있는 것처럼 보인다.</summary>
    private const float StackOverlap = 0.15f;

    /// <summary>머리 위 첫 악세사리를 머리 안쪽으로 눌러 앉히는 깊이(머리 높이 비율).
    /// 왕관·고양이 귀는 머리에 걸터앉아야 자연스럽다.</summary>
    private const float StackSeatDepth = 0.06f;

    /// <summary>얼굴(선글라스)의 세로 위치. 머리 위쪽 끝에서 머리 높이의 이 비율만큼 내려온 곳.</summary>
    private const float FaceHeightRatio = 0.46f;

    /// <summary>목(목걸이)의 세로 위치. 머리 아래쪽 끝에서 머리 높이의 이 비율만큼 올라간 곳.</summary>
    private const float NeckHeightRatio = 0.16f;

    /// <summary>머리 종류별 얼굴 높이 예외. 머리 12종을 전부 실측(2026-08-19)해 보니 11종은
    /// 기본값 <see cref="FaceHeightRatio"/>로 눈 위치에 정확히 맞았고, <b>팬봇만</b> 어긋났다 -
    /// 머리 위로 길게 솟은 프로펠러까지 스프라이트 높이에 포함되는 바람에 기본 비율이 이마
    /// 위로 올라간다. 이렇게 "위로 긴 장식이 달린 머리"만 여기에 적는다.
    /// 키는 스프라이트 이름이며, 네온아이처럼 프레임이 여러 장인 머리는 '_0' 앞부분으로 비교한다.</summary>
    private static readonly Dictionary<string, float> faceHeightRatioOverrides = new Dictionary<string, float>
    {
        { "FanBot", 0.64f },
    };

    private ProceduralCharacterRig rig;

    // 지금 화면에 붙어 있는 악세사리. 두 리스트는 항상 같은 길이/순서를 유지한다
    // (카탈로그에 없는 ID는 애초에 만들지 않으므로 인덱스가 어긋나지 않는다).
    private readonly List<SpriteRenderer> spawned = new List<SpriteRenderer>();
    private readonly List<AccessoryData> spawnedData = new List<AccessoryData>();
    private int builtCount = -1;

    // Sprite -> 픽셀이 실제로 그려진 로컬 영역. 스프라이트 정점을 매 프레임 훑지 않으려는 캐시다.
    // Resources에서 로드한 원본만 키로 들어오므로(런타임 생성 스프라이트 없음) 판이 바뀌어도
    // 그대로 재사용된다.
    private static readonly Dictionary<Sprite, Bounds> visualBoundsCache = new Dictionary<Sprite, Bounds>();

    private void LateUpdate()
    {
        if (rig == null) rig = GetComponentInChildren<ProceduralCharacterRig>();
        if (rig == null || rig.BodyVisual == null) return;

        SpriteRenderer body = rig.BodyRenderer;
        if (body == null || body.sprite == null) return;

        List<int> order = RunState.AccessoryPurchaseOrder;
        if (order.Count != builtCount || SpawnedObjectsLost()) Rebuild(order);
        if (spawned.Count == 0) return;

        Layout(body);
    }

    /// <summary>리그가 통째로 다시 만들어져(<see cref="ProceduralCharacterRig.Build"/>가
    /// 이전 RigRoot를 DestroyImmediate한다) 우리가 붙여둔 자식들이 같이 사라졌는지 확인한다.
    /// 구매 개수만 비교하면 이 경우를 놓쳐 악세사리가 조용히 사라진다.</summary>
    private bool SpawnedObjectsLost()
    {
        for (int i = 0; i < spawned.Count; i++)
        {
            if (spawned[i] == null) return true;
            if (spawned[i].transform.parent != rig.BodyVisual) return true;
        }

        return false;
    }

    /// <summary>구매 목록이 바뀌었을 때만 자식 오브젝트를 다시 만든다(구매는 상점에서만
    /// 일어나므로 전투 중에는 절대 실행되지 않는다).</summary>
    private void Rebuild(List<int> order)
    {
        for (int i = 0; i < spawned.Count; i++)
        {
            if (spawned[i] != null) Destroy(spawned[i].gameObject);
        }
        spawned.Clear();
        spawnedData.Clear();

        for (int i = 0; i < order.Count; i++)
        {
            if (!AccessoryCatalog.TryGet(order[i], out AccessoryData data)) continue;

            Sprite sprite = data.LoadIcon();
            if (sprite == null)
            {
                Debug.LogWarning($"악세사리 '{data.accessoryName}'의 스프라이트를 찾을 수 없습니다: Resources/{data.iconResourceName}");
                continue;
            }

            var go = new GameObject(ChildNamePrefix + data.accessoryName);
            go.transform.SetParent(rig.BodyVisual, false);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;

            spawned.Add(sr);
            spawnedData.Add(data);
        }

        builtCount = order.Count;
    }

    /// <summary>매 프레임 크기·위치·좌우 반전을 몸통에 맞춘다. 몸통이 머리 종류에 따라 바뀌고
    /// (네온아이는 프레임마다도 바뀐다) flipX도 수시로 뒤집히므로 한 번 배치하고 끝낼 수 없다.</summary>
    private void Layout(SpriteRenderer body)
    {
        Bounds head = GetVisualBounds(body.sprite);
        bool flip = body.flipX;

        float headWidth = head.size.x;
        float headHeight = head.size.y;
        // flipX는 픽셀을 피벗 기준으로 좌우로 뒤집을 뿐 자식 Transform은 뒤집지 않는다.
        // 그래서 "보이는 영역"의 가로 좌표는 우리가 직접 부호를 뒤집어 줘야 한다.
        float headCenterX = flip ? -head.center.x : head.center.x;

        // 스택형이 쌓여 올라갈 다음 바닥 높이. 머리 꼭대기보다 조금 아래에서 시작한다(걸터앉기).
        float stackY = head.max.y - headHeight * StackSeatDepth;
        int sortingBase = body.sortingOrder + 1;

        for (int i = 0; i < spawned.Count; i++)
        {
            SpriteRenderer sr = spawned[i];
            if (sr == null) continue;

            AccessoryData data = spawnedData[i];
            Bounds acc = GetVisualBounds(sr.sprite);
            if (acc.size.x <= 0.0001f || acc.size.y <= 0.0001f) continue;

            float scale = headWidth * Mathf.Max(0.01f, data.visualWidthRatio) / acc.size.x;
            float accHeight = acc.size.y * scale;

            float centerY;
            switch (data.attachPoint)
            {
                case AccessoryAttachPoint.Face:
                    centerY = head.max.y - headHeight * ResolveFaceHeightRatio(body.sprite);
                    break;
                case AccessoryAttachPoint.Neck:
                    centerY = head.min.y + headHeight * NeckHeightRatio;
                    break;
                default: // Stack - 구매 순서대로 위로 쌓인다(뿔 위에 왕관 위에 뿔...)
                    centerY = stackY + accHeight * 0.5f;
                    stackY += accHeight * (1f - StackOverlap);
                    break;
            }

            // 스프라이트 안에서 그림이 치우쳐 있어도(예: 고양이 귀는 위쪽에만 있다) 보이는 영역의
            // 중심이 목표 지점에 오도록 그 오프셋만큼 되민다.
            float accCenterX = flip ? -acc.center.x : acc.center.x;

            sr.transform.localScale = new Vector3(scale, scale, 1f);
            sr.transform.localPosition = new Vector3(headCenterX - accCenterX * scale, centerY - acc.center.y * scale, 0f);
            sr.flipX = flip;
            sr.sortingLayerID = body.sortingLayerID;
            sr.sortingOrder = sortingBase + i;
        }
    }

    /// <summary>이 머리의 얼굴 높이 비율. 예외 표에 없으면 기본값을 쓴다.</summary>
    private static float ResolveFaceHeightRatio(Sprite bodySprite)
    {
        if (bodySprite == null) return FaceHeightRatio;

        string name = bodySprite.name;
        if (faceHeightRatioOverrides.TryGetValue(name, out float ratio)) return ratio;

        int underscore = name.LastIndexOf('_');
        if (underscore > 0 && faceHeightRatioOverrides.TryGetValue(name.Substring(0, underscore), out ratio)) return ratio;

        return FaceHeightRatio;
    }

    /// <summary>스프라이트에서 실제로 픽셀이 그려진 로컬 영역. 타이트 메시(스프라이트 임포터
    /// 기본값)의 정점 범위라 텍스처를 Read/Write 가능으로 바꾸지 않아도 알파 여백을 뺄 수 있다.
    /// 타이트 메시가 아니면(정점 없음) 스프라이트 사각형을 그대로 돌려준다.</summary>
    private static Bounds GetVisualBounds(Sprite sprite)
    {
        if (sprite == null) return new Bounds();
        if (visualBoundsCache.TryGetValue(sprite, out Bounds cached)) return cached;

        Bounds result = sprite.bounds;
        Vector2[] vertices = sprite.vertices;
        if (vertices != null && vertices.Length > 0)
        {
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector2 v = vertices[i];
                if (v.x < minX) minX = v.x;
                if (v.x > maxX) maxX = v.x;
                if (v.y < minY) minY = v.y;
                if (v.y > maxY) maxY = v.y;
            }

            result = new Bounds(new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, 0f),
                                new Vector3(maxX - minX, maxY - minY, 0f));
        }

        visualBoundsCache[sprite] = result;
        return result;
    }
}
