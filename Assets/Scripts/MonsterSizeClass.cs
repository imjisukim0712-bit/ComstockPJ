using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 좀비 기획서(Ver04, 2026-08-07) p.6 "좀비 규격" 4단계.
///
/// 이미지(원화) 픽셀 크기는 인게임(표시) 픽셀 크기의 <b>정확히 2배</b>다(원화를 2배로 그려
/// 축소해서 쓰는 관례). 이 프로젝트의 기존 규칙("기획 문서 수치 ÷ 50 = 월드 유닛",
/// 무기 사거리에서 이미 검증됨)을 인게임 픽셀 수치에 적용하면 <b>네 규격 전부 PPU=100에서
/// 정확히 맞아떨어진다</b>(이미지px / 100 == 인게임px / 50). 실제로:
///   소형 이미지250/100=2.5, 인게임125/50=2.5 / 중형 350/100=3.5, 175/50=3.5 /
///   대형 450/100=4.5, 225/50=4.5 / 초대형 800/100=8.0, 400/50=8.0
///
/// 그래서 규칙이 아주 단순해진다: <b>원화를 규격의 이미지 픽셀 크기로 물리적으로 축소
/// 저장하고, PPU=100, Transform 스케일=1로 두면 축소·확대가 전혀 없이 원본 비율이
/// 그대로 유지된다.</b> 콜라이더도 로컬 값이 곧 월드 값이 되어 스케일을 역산할 필요가 없다.
/// </summary>
public enum MonsterSizeClass
{
    Small,      // 소형
    Medium,     // 중형
    Large,      // 대형
    ExtraLarge, // 초대형
}

/// <summary>규격별 이미지/충돌 스펙(기획서 원본 px 값 + 월드 유닛 환산값)을 담는 조회 테이블.</summary>
public static class MonsterSizeSpec
{
    public struct Spec
    {
        public int ImagePixels;          // 원화(이미지) 정사각 픽셀 크기(기획서 p.6)
        public Vector2 ColliderPixels;   // 충돌 범위(가로 x 세로, 픽셀)
        public string SpriteResourceName; // Resources 폴더의 스프라이트 파일명(확장자 제외)

        /// <summary>PPU 100 기준 표시 크기(유닛). Transform 스케일이 1이면 이 값이 곧 실제 표시 크기다.</summary>
        public float WorldSize => ImagePixels / 100f;

        /// <summary>충돌 범위를 기존 규칙(÷50)으로 환산한 월드 유닛 크기(가로 x 세로).</summary>
        public Vector2 WorldColliderSize => ColliderPixels / 50f;
    }

    private static readonly Dictionary<MonsterSizeClass, Spec> Specs = new Dictionary<MonsterSizeClass, Spec>
    {
        { MonsterSizeClass.Small,      new Spec { ImagePixels = 250, ColliderPixels = new Vector2(100, 80),  SpriteResourceName = "Enemy_zombie_S" } },
        { MonsterSizeClass.Medium,     new Spec { ImagePixels = 350, ColliderPixels = new Vector2(140, 90),  SpriteResourceName = "Enemy_zombie_M" } },
        { MonsterSizeClass.Large,      new Spec { ImagePixels = 450, ColliderPixels = new Vector2(180, 100), SpriteResourceName = "Enemy_zombie_L" } },
        { MonsterSizeClass.ExtraLarge, new Spec { ImagePixels = 800, ColliderPixels = new Vector2(300, 150), SpriteResourceName = "Enemy_zombie_XL" } },
    };

    public static Spec Get(MonsterSizeClass size) => Specs[size];
}
