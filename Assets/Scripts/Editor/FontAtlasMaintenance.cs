using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 한글 TMP 폰트(Dynamic)의 아틀라스를 정리한다.
///
/// <b>왜 필요한가</b> (2026-08-20 실측으로 원인 규명): `NotoSansKR-Regular SDF`는 Dynamic 폰트라
/// 런타임에 만나는 글자를 아틀라스에 그때그때 구워 넣는다. 그 상태로 에셋이 저장되면서
/// <b>아틀라스가 1024x1024 한 장을 넘겨 2번째 텍스처로 넘어갔고</b>, 글리프 테이블에는 기록이
/// 남았지만 텍스처에는 픽셀이 없는 어긋난 상태가 됐다. 결과는 <b>특정 글자만 자리는 잡히고
/// 그림이 안 나오는 현상</b>이다 - "상점"이 "점"으로, "(클릭 = 상세)"가 "(클릭 = 세)"로,
/// "부품 상자 12개"가 "자 12개"로 보였다(`TMP_FontAsset.HasCharacter()`는 True를 돌려주므로
/// 코드로는 정상처럼 보인다 - 캡처를 봐야 드러난다).
///
/// <b>무엇을 하는가</b>
/// 1. 아틀라스 크기를 1024 → 2048로 올린다(면적 4배). 글리프가 한 장에 들어가면 2번째 텍스처가
///    아예 생기지 않아 같은 어긋남이 재발하지 않는다.
/// 2. 동적으로 쌓인 글리프/아틀라스를 비운다(<see cref="TMP_FontAsset.ClearFontAssetData"/>).
///    비워도 손실은 없다 - Dynamic 폰트는 다음 렌더에서 필요한 글자를 다시 굽는다.
/// 3. "Clear Dynamic Data on Build"를 켜서 빌드에 어긋난 아틀라스가 실려 나가지 않게 한다.
///
/// 글자가 다시 안 보이는 일이 생기면 이 메뉴를 다시 실행하면 된다.
/// </summary>
public static class FontAtlasMaintenance
{
    private static readonly string[] FontPaths =
    {
        "Assets/Fonts/NotoSansKR/NotoSansKR-Regular SDF.asset",
        "Assets/Fonts/NotoSansKR/NotoSansKR-Bold SDF.asset"
    };

    private const int TargetAtlasSize = 2048;

    [MenuItem("Comstock/한글 폰트 아틀라스 초기화 (2048)")]
    public static void Rebuild()
    {
        var log = new System.Text.StringBuilder();

        foreach (string path in FontPaths)
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (font == null)
            {
                log.AppendLine($"  (건너뜀) 폰트를 찾지 못함: {path}");
                continue;
            }

            int beforeTextures = font.atlasTextureCount;
            int beforeGlyphs = font.glyphTable.Count;
            int beforeSize = font.atlasWidth;

            SetPrivateInt(font, "m_AtlasWidth", TargetAtlasSize);
            SetPrivateInt(font, "m_AtlasHeight", TargetAtlasSize);
            SetPrivateBool(font, "m_ClearDynamicDataOnBuild", true);

            // ClearFontAssetData가 0번 텍스처를 새 크기로 다시 만들어 주지만, TMP 버전에 따라
            // 크기를 그대로 두는 경우가 있어 여기서 직접 맞춰 준다.
            Texture2D atlas = font.atlasTexture;
            if (atlas != null && (atlas.width != TargetAtlasSize || atlas.height != TargetAtlasSize))
            {
                atlas.Reinitialize(TargetAtlasSize, TargetAtlasSize);
                atlas.Apply(false, false);
            }

            font.ClearFontAssetData(true);

            EditorUtility.SetDirty(font);
            if (font.atlasTexture != null) EditorUtility.SetDirty(font.atlasTexture);
            if (font.material != null) EditorUtility.SetDirty(font.material);

            log.AppendLine($"  {font.name}: 아틀라스 {beforeSize} → {font.atlasWidth}, " +
                           $"텍스처 {beforeTextures}장 → {font.atlasTextureCount}장, 글리프 {beforeGlyphs}개 → {font.glyphTable.Count}개");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[폰트 아틀라스 정리] 완료\n" + log);
    }

    /// <summary>현재 상태만 확인한다(고치지 않는다).</summary>
    [MenuItem("Comstock/한글 폰트 아틀라스 상태 확인")]
    public static void Inspect()
    {
        var log = new System.Text.StringBuilder();

        foreach (string path in FontPaths)
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (font == null) continue;

            // 텍스처가 2장 이상이면 위 주석의 "글자가 안 보이는" 상태에 빠질 수 있다.
            string warn = font.atlasTextureCount > 1 ? "  ⚠ 텍스처가 2장 이상 - 글리프 누락 위험" : string.Empty;
            log.AppendLine($"  {font.name}: {font.atlasWidth}x{font.atlasHeight}, 텍스처 {font.atlasTextureCount}장, " +
                           $"글리프 {font.glyphTable.Count}개, population={font.atlasPopulationMode}{warn}");
        }

        Debug.Log("[폰트 아틀라스 상태]\n" + log);
    }

    private static void SetPrivateInt(TMP_FontAsset font, string fieldName, int value)
    {
        FieldInfo fi = typeof(TMP_FontAsset).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (fi != null) fi.SetValue(font, value);
        else Debug.LogWarning($"[폰트 아틀라스 정리] 필드 '{fieldName}'을 찾지 못했습니다(TMP 버전 차이).");
    }

    private static void SetPrivateBool(TMP_FontAsset font, string fieldName, bool value)
    {
        FieldInfo fi = typeof(TMP_FontAsset).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (fi != null) fi.SetValue(font, value);
    }
}
