using GmodAddonAnalyzer.Services;

namespace GmodAddonAnalyzer.Tests;

public sealed class LuaHighlighterTests
{
    [Fact]
    public void Keywords_AreWrapped()
    {
        var html = LuaHighlighter.Highlight("local x = 1");
        Assert.Contains("<span class=\"lua-k\">local</span>", html);
    }

    [Fact]
    public void Strings_AreWrapped()
    {
        var html = LuaHighlighter.Highlight("print('hi')");
        Assert.Contains("<span class=\"lua-s\">", html);
    }

    [Fact]
    public void Comments_AreWrapped()
    {
        var html = LuaHighlighter.Highlight("-- a comment\nx = 1");
        Assert.Contains("<span class=\"lua-c\">-- a comment</span>", html);
    }

    [Fact]
    public void Html_IsEscaped()
    {
        var html = LuaHighlighter.Highlight("x = '<b>'");
        Assert.DoesNotContain("<b>", html);
        Assert.Contains("&lt;b&gt;", html);
    }

    [Fact]
    public void Numbers_AreWrapped()
    {
        var html = LuaHighlighter.Highlight("x = 42");
        Assert.Contains("<span class=\"lua-n\">42</span>", html);
    }
}