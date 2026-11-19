using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Game.Core
{
    /// <summary>
    /// 极简 JSON 解析器，只覆盖本项目用得到的语法：对象 / 数组 / 字符串 / 数字 / true / false / null。
    ///
    /// 为什么不用现成的库：
    /// - System.Text.Json 在团结引擎（Unity 2022.3）运行时不带这个程序集，Core 层用了会编译失败。
    /// - Newtonsoft 需要额外装包，而 Core 层的硬约束是「零引擎依赖、零第三方依赖」。
    /// - JsonUtility 属于 UnityEngine，同样不能进 Core。
    ///
    /// 因此自带一个够用的实现，保证同一份代码在 dotnet 测试工程与 Unity 里行为完全一致。
    /// @trace DATA-002
    /// </summary>
    public static class MiniJson
    {
        /// <summary>解析 JSON 文本，返回 Dictionary&lt;string,object&gt; / List&lt;object&gt; / string / double / bool / null。</summary>
        public static object Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            var p = new Parser(json);
            object value = p.ParseValue();
            p.SkipWhitespace();
            if (!p.End) throw new FormatException("JSON 尾部存在多余字符，位置 " + p.Position);
            return value;
        }

        sealed class Parser
        {
            readonly string _s;
            int _i;

            public Parser(string s)
            {
                _s = s;
                _i = 0;
            }

            public bool End { get { return _i >= _s.Length; } }
            public int Position { get { return _i; } }

            public void SkipWhitespace()
            {
                while (_i < _s.Length)
                {
                    char ch = _s[_i];
                    if (ch == ' ' || ch == '\t' || ch == '\n' || ch == '\r') { _i++; continue; }
                    break;
                }
            }

            public object ParseValue()
            {
                SkipWhitespace();
                if (End) throw new FormatException("JSON 意外结束");

                char ch = _s[_i];
                switch (ch)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default: return ParseNumber();
                }
            }

            Dictionary<string, object> ParseObject()
            {
                var result = new Dictionary<string, object>();
                _i++; // 跳过 '{'
                SkipWhitespace();
                if (!End && _s[_i] == '}') { _i++; return result; }

                while (true)
                {
                    SkipWhitespace();
                    if (End || _s[_i] != '"') throw new FormatException("对象的键必须是字符串，位置 " + _i);
                    string key = ParseString();

                    SkipWhitespace();
                    if (End || _s[_i] != ':') throw new FormatException("缺少冒号，位置 " + _i);
                    _i++;

                    result[key] = ParseValue();

                    SkipWhitespace();
                    if (End) throw new FormatException("对象未闭合");
                    char ch = _s[_i];
                    if (ch == ',') { _i++; continue; }
                    if (ch == '}') { _i++; return result; }
                    throw new FormatException("对象内缺少逗号或右花括号，位置 " + _i);
                }
            }

            List<object> ParseArray()
            {
                var result = new List<object>();
                _i++; // 跳过 '['
                SkipWhitespace();
                if (!End && _s[_i] == ']') { _i++; return result; }

                while (true)
                {
                    result.Add(ParseValue());

                    SkipWhitespace();
                    if (End) throw new FormatException("数组未闭合");
                    char ch = _s[_i];
                    if (ch == ',') { _i++; continue; }
                    if (ch == ']') { _i++; return result; }
                    throw new FormatException("数组内缺少逗号或右方括号，位置 " + _i);
                }
            }

            string ParseString()
            {
                var sb = new StringBuilder();
                _i++; // 跳过起始引号

                while (true)
                {
                    if (End) throw new FormatException("字符串未闭合");
                    char ch = _s[_i++];
                    if (ch == '"') return sb.ToString();
                    if (ch != '\\') { sb.Append(ch); continue; }

                    if (End) throw new FormatException("转义序列未完成");
                    char esc = _s[_i++];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_i + 4 > _s.Length) throw new FormatException("\\u 转义不完整");
                            string hex = _s.Substring(_i, 4);
                            _i += 4;
                            sb.Append((char)int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            break;
                        default:
                            throw new FormatException("未知转义字符 \\" + esc);
                    }
                }
            }

            object ParseNumber()
            {
                int start = _i;
                if (!End && (_s[_i] == '-' || _s[_i] == '+')) _i++;

                while (!End)
                {
                    char ch = _s[_i];
                    bool digit = ch >= '0' && ch <= '9';
                    if (digit || ch == '.' || ch == 'e' || ch == 'E' || ch == '+' || ch == '-') { _i++; continue; }
                    break;
                }

                string raw = _s.Substring(start, _i - start);
                double value;
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    throw new FormatException("非法数字: '" + raw + "'，位置 " + start);
                return value;
            }

            void Expect(string literal)
            {
                if (_i + literal.Length > _s.Length) throw new FormatException("期望 '" + literal + "' 但已到结尾");
                for (int k = 0; k < literal.Length; k++)
                {
                    if (_s[_i + k] != literal[k])
                        throw new FormatException("期望 '" + literal + "'，位置 " + _i);
                }
                _i += literal.Length;
            }
        }
    }
}
