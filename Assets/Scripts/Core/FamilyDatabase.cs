using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// 把族谱 JSON 解析成 FamilyPerson 列表。
    /// 数据源为族谱编辑器导出的格式，根节点可以是数组，也可以是 { "persons": [...] }。
    ///
    /// 注意：Core 层只做「文本 → 对象」，不负责读文件。
    /// Unity 侧由 TextAsset 提供字符串，dotnet 测试侧由 File.ReadAllText 提供，
    /// 这样同一份解析逻辑两边共用。
    ///
    /// @trace DATA-002
    /// </summary>
    public static class FamilyDatabase
    {
        public static List<FamilyPerson> LoadFromJson(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));

            object root = MiniJson.Parse(json);
            List<object> array = root as List<object>;
            if (array == null)
            {
                var map = root as Dictionary<string, object>;
                if (map != null && map.ContainsKey("persons"))
                {
                    array = map["persons"] as List<object>;
                }
            }
            if (array == null)
                throw new FormatException("族谱 JSON 根节点既不是数组，也不含 persons 数组");

            var result = new List<FamilyPerson>(array.Count);
            for (int i = 0; i < array.Count; i++)
            {
                var node = array[i] as Dictionary<string, object>;
                if (node == null)
                    throw new FormatException("persons 第 " + i + " 项不是对象");

                string id = GetString(node, "id");
                if (string.IsNullOrEmpty(id))
                    throw new FormatException("persons 第 " + i + " 项缺少 id");

                result.Add(new FamilyPerson(
                    id,
                    GetString(node, "title"),
                    ParseGender(GetString(node, "gender")),
                    GetInt(node, "gen", 1),
                    GetNullableString(node, "fatherId"),
                    GetNullableString(node, "motherId"),
                    GetNullableString(node, "spouseId"),
                    ParseCategory(GetString(node, "category"))));
            }
            return result;
        }

        static string GetString(Dictionary<string, object> node, string key)
        {
            object v;
            if (!node.TryGetValue(key, out v) || v == null) return null;
            return v as string;
        }

        static string GetNullableString(Dictionary<string, object> node, string key)
        {
            string s = GetString(node, key);
            return string.IsNullOrEmpty(s) ? null : s;
        }

        static int GetInt(Dictionary<string, object> node, string key, int fallback)
        {
            object v;
            if (!node.TryGetValue(key, out v) || v == null) return fallback;
            if (v is double) return (int)Math.Round((double)v);
            return fallback;
        }

        static Gender ParseGender(string raw)
        {
            return string.Equals(raw, "female", StringComparison.OrdinalIgnoreCase) ? Gender.Female : Gender.Male;
        }

        static KinCategory ParseCategory(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return KinCategory.None;
            switch (raw)
            {
                case "sibling": return KinCategory.Sibling;
                case "tang": return KinCategory.Tang;
                case "biao": return KinCategory.Biao;
                case "zhi": return KinCategory.Zhi;
                case "sheng": return KinCategory.Sheng;
                default: return KinCategory.None;
            }
        }
    }
}
