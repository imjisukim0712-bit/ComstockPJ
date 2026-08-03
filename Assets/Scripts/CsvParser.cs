using System.Collections.Generic;

public static class CsvParser
{
    /// <summary>
    /// 1행 한글명 / 2행 필드명 / 3행 타입 순서인 시트를 전제로,
    /// 앞 3줄을 건너뛰고 실제 데이터 행만 문자열 배열 리스트로 반환한다.
    /// </summary>
    public static List<string[]> ParseDataRows(string csvText)
    {
        var rows = new List<string[]>();
        string[] lines = csvText.Replace("\r\n", "\n").Split('\n');

        for (int i = 3; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            string[] cols = line.Split(',');
            // 첫 컬럼(ID)이 비어있으면 빈 행이므로 스킵
            if (cols.Length == 0 || string.IsNullOrEmpty(cols[0])) continue;

            rows.Add(cols);
        }

        return rows;
    }
}
