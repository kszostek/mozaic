using Mozaic.Core;
using Mozaic.Core.Commands;
using Mozaic.Core.Config;

namespace Mozaic.Tests;

public class ConfigTests
{
    [Fact]
    public void DefaultConfigParsesWithoutErrors()
    {
        var cfg = ConfigParser.Parse(DefaultConfig.Text);
        Assert.Empty(cfg.Errors);
        Assert.Equal("Alt", cfg.Variables["$mod"]);
        Assert.Contains("resize", cfg.Modes);
        Assert.NotNull(cfg.Bar);
        Assert.Equal(6, cfg.GapsInner);
        Assert.Equal(Modifiers.Alt, cfg.FloatingModifier);
        var enter = cfg.Bindings.Single(b => b.Mode == "default" && b.Combo == new KeyCombo(Modifiers.Alt, 0x0D));
        Assert.Equal("exec wt.exe", enter.Command);
        Assert.Contains(cfg.Bindings, b => b.Mode == "resize" && b.Combo == new KeyCombo(Modifiers.None, 'H'));
        Assert.Equal(new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10" }, cfg.WorkspaceOrder);
    }

    [Fact]
    public void ParsesTypicalI3Config()
    {
        const string text = """
            set $mod Mod4
            set $left h
            font pango:DejaVu Sans Mono 9
            bindsym $mod+$left focus left
            bindsym --release $mod+Shift+x exec --no-startup-id notepad.exe
            bindcode $mod+65 exec foo
            workspace 1 output DISPLAY2 primary
            assign [class="^Firefox$"] → 2
            assign [process="spotify"] workspace number 9
            for_window [title="Settings"] floating enable, move position center
            exec_always --no-startup-id something.exe
            gaps inner 10
            gaps outer 5
            client.focused #ff0000 #00ff00 #0000ff
            default_border pixel 2
            hide_edge_borders smart
            bar {
                status_command i3status
                position bottom
                colors {
                    background #123456
                }
            }
            """;
        var cfg = ConfigParser.Parse(text);
        Assert.Empty(cfg.Errors);
        Assert.Contains(cfg.Bindings, b => b.Combo == new KeyCombo(Modifiers.Win, 'H') && b.Command == "focus left");
        var rel = cfg.Bindings.Single(b => b.OnRelease);
        Assert.Equal(new KeyCombo(Modifiers.Win | Modifiers.Shift, 'X'), rel.Combo);
        Assert.Contains(cfg.Bindings, b => b.Combo == new KeyCombo(Modifiers.Win, 65));
        Assert.Equal(new FontSpec("DejaVu Sans Mono", 9), cfg.Font);
        Assert.Equal(new[] { "DISPLAY2", "primary" }, cfg.WorkspaceOutputs["1"]);
        Assert.Equal(2, cfg.Assigns.Count);
        Assert.True(cfg.Assigns[1].ByNumber);
        Assert.Equal("9", cfg.Assigns[1].Target);
        Assert.Single(cfg.ForWindow);
        Assert.Equal("something.exe", cfg.ExecAlways[0]);
        Assert.Equal(10, cfg.GapsInner);
        Assert.Equal(5, cfg.GapsLeft);
        Assert.Equal(0xFF0000u, cfg.Focused.Border);
        Assert.Equal(0x00FF00u, cfg.Focused.ChildBorder);
        Assert.Equal(2, cfg.BorderWidth);
        Assert.Equal("bottom", cfg.Bar!.Position);
        Assert.Equal("i3status", cfg.Bar.StatusCommand);
        Assert.Equal(0x123456u, cfg.Bar.Colors.Background);
    }

    [Fact]
    public void ReportsErrorsWithLineNumbers()
    {
        var cfg = ConfigParser.Parse("bindsym Mod1+Foo kill\nfrobnicate yes\nbindsym Mod1+q kill");
        Assert.Equal(2, cfg.Errors.Count);
        Assert.Equal(1, cfg.Errors[0].Line);
        Assert.Equal(2, cfg.Errors[1].Line);
        Assert.Single(cfg.Bindings);
    }

    [Fact]
    public void LineContinuations()
    {
        var cfg = ConfigParser.Parse("bindsym Mod1+q \\\n   kill");
        Assert.Empty(cfg.Errors);
        Assert.Equal("kill", cfg.Bindings[0].Command);
    }
}

public class CommandParserTests
{
    [Fact]
    public void ChainsCommandsAndCriteria()
    {
        var cmds = CommandParser.Parse("[class=\"Firefox\"] focus, move left; workspace \"2: web\"");
        Assert.Equal(3, cmds.Count);
        Assert.NotNull(cmds[0].Criteria);
        Assert.Same(cmds[0].Criteria, cmds[1].Criteria);
        Assert.Null(cmds[2].Criteria);
        Assert.Equal(new[] { "move", "left" }, cmds[1].Tokens);
        Assert.Equal(new[] { "workspace", "2: web" }, cmds[2].Tokens);
    }

    [Fact]
    public void ExecKeepsRawArguments()
    {
        var cmds = CommandParser.Parse("exec wt.exe -d \"C:\\Program Files\"");
        Assert.Single(cmds);
        Assert.Equal("wt.exe -d \"C:\\Program Files\"", cmds[0].RawArgs);
        var (file, args) = CommandParser.SplitCommandLine(cmds[0].RawArgs);
        Assert.Equal("wt.exe", file);
        Assert.Equal("-d \"C:\\Program Files\"", args);
    }

    [Fact]
    public void CriteriaMatching()
    {
        var c = Criteria.Parse("class=\"^firefox$\" title=\"Mail\"");
        var facts = new WindowFacts(1, 42, "MozillaWindowClass", "Inbox - Mail", "firefox.exe", false, false, false, false, "1", Array.Empty<string>());
        Assert.True(c.Matches(facts));
        Assert.False(c.Matches(facts with { Title = "Inbox - mail" })); // title is case-sensitive
        var p = Criteria.Parse("process=\"FIREFOX\" floating");
        Assert.False(p.Matches(facts));
        Assert.True(p.Matches(facts with { Floating = true }));
        Assert.True(Criteria.Parse("window_type=dialog").Matches(facts with { Dialog = true }));
    }

    [Fact]
    public void SingleQuotedCriteria()
    {
        var cmds = CommandParser.Parse("[title='My App] x' class=\"a'b\"] focus");
        Assert.Single(cmds);
        var facts = new WindowFacts(1, 1, "a'b", "My App] x", "x.exe", false, false, false, false, "1", Array.Empty<string>());
        Assert.True(cmds[0].Criteria!.Matches(facts));
    }

    [Fact]
    public void BadInputThrows()
    {
        Assert.Throws<CommandParseException>(() => CommandParser.Parse("[class=\"x\" focus"));
        Assert.Throws<CommandParseException>(() => CommandParser.Parse("[bogus=1] focus"));
    }
}

public class KeyNameTests
{
    [Theory]
    [InlineData("Mod1+Shift+q", Modifiers.Alt | Modifiers.Shift, 'Q')]
    [InlineData("Mod4+Return", Modifiers.Win, 0x0D)]
    [InlineData("Control+F5", Modifiers.Ctrl, 0x74)]
    [InlineData("Win+minus", Modifiers.Win, 0xBD)]
    [InlineData("Escape", Modifiers.None, 0x1B)]
    [InlineData("Mod1+space", Modifiers.Alt, 0x20)]
    public void ParsesCombos(string text, Modifiers mods, int vk)
    {
        Assert.True(KeyNames.TryParseCombo(text, null, false, out var combo, out var err), err);
        Assert.Equal(new KeyCombo(mods, vk), combo);
    }

    [Fact]
    public void WorkspaceNumbers()
    {
        Assert.Equal(3, WorkspaceNames.ParseNumber("3"));
        Assert.Equal(3, WorkspaceNames.ParseNumber("3:web"));
        Assert.Equal(-1, WorkspaceNames.ParseNumber("web"));
        Assert.Equal(-1, WorkspaceNames.ParseNumber("3web"));
        Assert.Equal("web", WorkspaceNames.StripNumber("3:web"));
    }
}
