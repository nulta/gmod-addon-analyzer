using System.Text;
using System.Text.RegularExpressions;

namespace GmodAddonAnalyzer.Services;

public static class LuaHighlighter
{
    private static readonly Regex Token = new(
        @"(?<comment>--\[\[.*?\]\]|--[^\n]*)" +
        @"|(?<string>\[\[.*?\]\]|""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*')" +
        @"|(?<number>\b\d+\.?\d*(?:[eE][+-]?\d+)?\b)" +
        @"|(?<keyword>\b(?:and|break|do|else|elseif|end|false|for|function|goto|if|in|local|nil|not|or|repeat|return|then|true|until|while|continue)\b)" +
        @"|(?<name>[A-Za-z_]\w*)" +
        @"|(?<other>.|\n)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    public static string Highlight(string source)
    {
        var sb = new StringBuilder(source.Length * 2);
        foreach (Match m in Token.Matches(source))
        {
            var text = m.Value;
            string? cls =
                m.Groups["comment"].Success ? "c" :
                m.Groups["string"].Success  ? "s" :
                m.Groups["number"].Success  ? "n" :
                m.Groups["keyword"].Success ? "k" :
                m.Groups["name"].Success    ? "i" : null;
            var escaped = Esc(text);
            sb.Append(cls is null ? escaped : "<span class=\"lua-" + cls + "\">" + escaped + "</span>");
        }
        return sb.ToString();
    }

    private static string Esc(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}