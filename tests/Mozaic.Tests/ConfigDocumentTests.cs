using Mozaic.Core;
using Mozaic.Core.Config;

namespace Mozaic.Tests;

public class ConfigDocumentTests
{
    private const string Sample = """
        # my config
        set $mod Alt

        gaps inner 6
        # keep this comment
        focus_follows_mouse no

        bindsym $mod+Return exec wt.exe
        bindsym --release $mod+x kill

        mode "resize" {
            bindsym h resize shrink width 10 px
            bindsym Escape mode "default"
        }

        bar {
            position top
            colors {
                background #000000
            }
        }
        """;

    [Fact]
    public void RoundTripsUnchanged()
    {
        var doc = new ConfigDocument(Sample);
        Assert.Equal(Sample.Replace("\r\n", "\n") + "\n", doc.Text.Replace("\r\n", "\n"));
    }

    [Fact]
    public void ReadsAndEditsSettingsInPlace()
    {
        var doc = new ConfigDocument(Sample);
        Assert.Equal("6", doc.Get("gaps inner"));
        Assert.Equal("Alt", doc.GetVariable("$mod"));
        Assert.Equal("top", doc.Get("position", "bar"));
        Assert.Equal("#000000", doc.Get("background", "bar.colors"));

        doc.Set("gaps inner", "12");
        doc.SetVariable("$mod", "Win");
        doc.Set("position", "bottom", "bar");
        doc.Set("background", "#112233", "bar.colors");
        Assert.Contains("gaps inner 12", doc.Text);
        Assert.Contains("set $mod Win", doc.Text);
        Assert.Contains("    position bottom", doc.Text);
        Assert.Contains("        background #112233", doc.Text);
        Assert.Contains("# keep this comment", doc.Text);
        var cfg = ConfigParser.Parse(doc.Text);
        Assert.Empty(cfg.Errors);
        Assert.Equal(12, cfg.GapsInner);
        Assert.Equal("bottom", cfg.Bar!.Position);
        Assert.Equal(0x112233u, cfg.Bar.Colors.Background);
    }

    [Fact]
    public void InsertsMissingSettingsNextToRelatedLines()
    {
        var doc = new ConfigDocument(Sample);
        doc.Set("gaps outer", "4");
        doc.Set("autotiling", "yes");
        doc.Set("height", "30", "bar");
        doc.Set("statusline", "#ffffff", "bar.colors");
        var lines = doc.Lines.Select(l => l.Trim()).ToList();
        Assert.Equal(lines.IndexOf("gaps inner 6") + 1, lines.IndexOf("gaps outer 4"));
        Assert.True(lines.IndexOf("autotiling yes") < lines.IndexOf("bindsym $mod+Return exec wt.exe"));
        var cfg = ConfigParser.Parse(doc.Text);
        Assert.Empty(cfg.Errors);
        Assert.True(cfg.Autotiling);
        Assert.Equal(4, cfg.GapsTop);
        Assert.Equal(30, cfg.Bar!.Height);
        Assert.Equal(0xFFFFFFu, cfg.Bar.Colors.Statusline);
    }

    [Fact]
    public void CreatesMissingBlocks()
    {
        var doc = new ConfigDocument("set $mod Alt\n");
        doc.Set("background", "#123456", "bar.colors");
        doc.AddBinding("resize", "h", "resize shrink width 10 px", false);
        var cfg = ConfigParser.Parse(doc.Text);
        Assert.Empty(cfg.Errors);
        Assert.Equal(0x123456u, cfg.Bar!.Colors.Background);
        Assert.Contains("resize", cfg.Modes);
    }

    [Fact]
    public void EditsBindings()
    {
        var doc = new ConfigDocument(Sample);
        var b = doc.Bindings();
        Assert.Equal(4, b.Count);
        Assert.Equal("$mod+Return", b[0].Keys);
        Assert.Equal("exec wt.exe", b[0].Command);
        Assert.True(b[1].Release);
        Assert.Equal("resize", b[2].Mode);
        Assert.Equal("mode \"default\"", b[3].Command);

        doc.AddBinding("default", "$mod+t", "layout tabbed", false);
        doc.ReplaceLine(doc.Bindings()[0].Line, ConfigDocument.FormatBinding("$mod+Shift+Return", "exec cmd.exe", false));
        doc.RemoveLine(doc.Bindings().Single(x => x.Keys == "$mod+x").Line);
        var cfg = ConfigParser.Parse(doc.Text);
        Assert.Empty(cfg.Errors);
        Assert.Contains(cfg.Bindings, x => x.Command == "layout tabbed" && x.Mode == "default");
        Assert.Contains(cfg.Bindings, x => x.Command == "exec cmd.exe");
        Assert.DoesNotContain(cfg.Bindings, x => x.Command == "kill");
        Assert.Equal(new[] { "default", "resize" }, doc.ModeNames());
    }

    [Fact]
    public void EditsRulesExecsAndWorkspaceOutputs()
    {
        var doc = new ConfigDocument(Sample);
        doc.AddRule("for_window", "process=\"notepad\"", "floating enable");
        doc.AddRule("assign", "class='Spotify'", "workspace number 9");
        doc.AddExec(false, "explorer.exe");
        doc.SetWorkspaceOutput("1", "DISPLAY2");
        var rules = doc.Rules();
        Assert.Equal(2, rules.Count);
        Assert.Equal("process=\"notepad\"", rules[0].Criteria);
        Assert.Equal("floating enable", rules[0].Action);
        Assert.Equal("assign", rules[1].Kind);
        Assert.Single(doc.Execs());
        Assert.Equal(("1", "DISPLAY2"), (doc.WorkspaceOutputs()[0].Workspace, doc.WorkspaceOutputs()[0].Outputs));
        doc.SetWorkspaceOutput("1", null);
        Assert.Empty(doc.WorkspaceOutputs());
        var cfg = ConfigParser.Parse(doc.Text);
        Assert.Empty(cfg.Errors);
        Assert.Single(cfg.ForWindow);
        Assert.Single(cfg.Assigns);
        Assert.Equal("explorer.exe", cfg.Exec[0]);
    }

    [Fact]
    public void DefaultConfigIsEditable()
    {
        var doc = new ConfigDocument(DefaultConfig.Text);
        Assert.Equal("Alt", doc.GetVariable("$mod"));
        Assert.Equal("6", doc.Get("gaps inner"));
        Assert.Equal("#4c7899 #285577 #ffffff #2e9ef4   #2e9ef4", doc.Get("client.focused"));
        Assert.Contains("resize", doc.ModeNames());
        Assert.True(doc.Bindings().Count > 40);
        Assert.Equal("\"ddd d MMM  HH:mm\"", doc.Get("datetime_format", "bar"));
    }
}

public class NewFeatureTests
{
    [Fact]
    public void AutotilingSplitsAlongTheLongerSide()
    {
        var f = new Fixture(options: new TreeOptions { Autotiling = true });
        f.Open("A");
        f.Compute();
        f.Open("B");                    // A is 1000x500 (wide) → horizontal
        f.Compute();
        Assert.Equal("H[A B]", f.Layout);
        f.Open("C");                    // B is 500x500 (not wider) → vertical
        f.Compute();
        Assert.Equal("H[A V[B C]]", f.Layout);
        f.Open("D");                    // C is 500x250 (wide) → horizontal
        Assert.Equal("H[A V[B H[C D]]]", f.Layout);
    }

    [Fact]
    public void FocusLastJumpsBack()
    {
        var f = new Fixture();
        f.Open("A"); f.Open("B"); f.Open("C");
        f.Tree.Focus(f["A"]);
        Assert.True(f.Tree.FocusLast());
        Assert.Same(f["C"], f.Tree.Focused);
        Assert.True(f.Tree.FocusLast());
        Assert.Same(f["A"], f.Tree.Focused);
    }

    [Fact]
    public void FocusLastSwitchesWorkspace()
    {
        var f = new Fixture();
        f.Open("A");
        var ws2 = f.Tree.GetOrCreateWorkspace("2");
        f.Tree.ShowWorkspace(ws2);
        f.Open("B");
        Assert.True(f.Tree.FocusLast());
        Assert.Same(f["A"], f.Tree.Focused);
        Assert.Equal("1", f.Tree.FocusedWorkspace!.Name);
    }
}
