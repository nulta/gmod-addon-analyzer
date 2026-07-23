using GmodAddonAnalyzer.Data.Entities;
using GmodAddonAnalyzer.Services;

namespace GmodAddonAnalyzer.Tests;

public sealed class LuaAnalyzerTests
{
    private readonly LuaAnalyzer _a = new();

    [Theory]
    [InlineData("lua/autorun/client/cl.lua",     Realm.Client)]
    [InlineData("lua/autorun/server/sv.lua",     Realm.Server)]
    [InlineData("lua/autorun/shared.lua",        Realm.Shared)]
    [InlineData("lua/vgui/frame.lua",            Realm.Client)]
    [InlineData("lua/weapons/gmod_tool/stool.lua", Realm.Shared)]
    [InlineData("lua/weapons/crowbar.lua",       Realm.Shared)]
    [InlineData("lua/entities/npc_thing/init.lua", Realm.Shared)]
    [InlineData("random/path.lua",               Realm.Unknown)]
    public void Realm_FromPath(string path, Realm expected)
    {
        var r = _a.Analyze(path, "");
        Assert.Equal(expected, r.Realm);
    }

    [Theory]
    [InlineData("lua/weapons/gmod_tool/foo.lua", "TOOL.Name='x'",    LuaKind.Tool)]
    [InlineData("lua/weapons/wep.lua",           "SWEP.PrintName='x'", LuaKind.Swep)]
    [InlineData("lua/entities/ent_x/init.lua",   "ENT.Type='anim'",   LuaKind.Entity)]
    [InlineData("lua/entities/ai_npc/init.lua",  "ENT.Type='ai'",     LuaKind.Snpc)]
    [InlineData("lua/effects/fx.lua",            "ENT.Type='anim'",   LuaKind.Effect)]
    [InlineData("lua/something.lua",             "local x = 1",       LuaKind.Util)]
    public void Kind_Detection(string path, string src, LuaKind expected)
    {
        var r = _a.Analyze(path, src);
        Assert.Equal(expected, r.Kind);
    }

    [Theory]
    [InlineData("RunString('print(1)')",              RiskLevel.High, "RunString")]
    [InlineData("CompileString('x', 'y')()",          RiskLevel.High, "CompileString")]
    [InlineData("loadstring('x')()",                  RiskLevel.High, "LoadString")]
    [InlineData("_G['RandomName']()",                 RiskLevel.Medium, "GlobalIndex")]
    [InlineData("os.execute('rm -rf /')",             RiskLevel.High, "OsExecute")]
    [InlineData("io.open('/etc/passwd')",             RiskLevel.High, "IoOpen")]
    [InlineData("RunConsoleCommand('say','hi')",      RiskLevel.Low, "RunConsoleCommand")]
    [InlineData("http.Post(url, {})",                 RiskLevel.Low, "HttpPost")]
    [InlineData("require('thing')",                   RiskLevel.Low, "Require")]
    [InlineData("local x = 1 + 2",                    RiskLevel.None, null)]
    public void Risk_Detection(string src, RiskLevel expectedRisk, string? expectedSignal)
    {
        var r = _a.Analyze("lua/x.lua", src);
        Assert.Equal(expectedRisk, r.RiskLevel);
        if (expectedSignal is not null)
        {
            Assert.Contains(expectedSignal, r.RiskSignals);
        }
        else
        {
            Assert.Equal("[]", r.RiskSignals);
        }
    }

    [Fact]
    public void Risk_HighestWins()
    {
        var src = "RunConsoleCommand('x')\nloadstring('y')()";
        var r = _a.Analyze("lua/x.lua", src);
        Assert.Equal(RiskLevel.High, r.RiskLevel);
        Assert.Contains("LoadString", r.RiskSignals);
        Assert.Contains("RunConsoleCommand", r.RiskSignals);
    }

    [Fact]
    public void Comments_AreStripped()
    {
        var src = "-- RunString('not real')\nlocal x = 1";
        var r = _a.Analyze("lua/x.lua", src);
        Assert.Equal(RiskLevel.None, r.RiskLevel);
    }

    [Fact]
    public void Hooks_DeclaredAndCalled()
    {
        var src = "hook.Add('Think', 'x', f)\nhook.Add('Think', 'y', g)\nhook.Call('OpenMenu')";
        var r = _a.Analyze("lua/x.lua", src);
        Assert.Contains("Think", r.HooksDeclared);
        Assert.Contains("OpenMenu", r.HooksCalled);
    }

    [Fact]
    public void Timers_Counted()
    {
        var src = "timer.Create('t', 1, 0, f)\ntimer.Simple(2, g)";
        var r = _a.Analyze("lua/x.lua", src);
        Assert.Equal(2, r.TimersCreated);
    }

    [Fact]
    public void Net_MessagesTracked()
    {
        var src = "util.AddNetworkString('my_msg')\nnet.Receive('my_msg', handler)";
        var r = _a.Analyze("lua/x.lua", src);
        Assert.Contains("my_msg", r.NetMessagesSent);
        Assert.Equal(1, r.NetReceivers);
    }

    [Fact]
    public void Lines_Counted()
    {
        var src = "a\nb\nc";
        var r = _a.Analyze("lua/x.lua", src);
        Assert.Equal(3, r.Lines);
    }
}