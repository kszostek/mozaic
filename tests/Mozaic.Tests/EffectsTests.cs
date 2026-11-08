using Mozaic.Core;
using Mozaic.Core.Config;

namespace Mozaic.Tests;

public class GroupEdgeTests
{
    /// <summary>H[A, V[B, C]] on a 1000×500 output with 10 px inner gaps.</summary>
    private static Fixture Nested()
    {
        var f = new Fixture();
        f.Open("A");
        f.Open("B");
        f.Tree.Split(Orientation.Vertical);
        f.Open("C");
        Assert.Equal("H[A V[B C]]", f.Layout);
        f.Compute(inner: 10);
        return f;
    }

    [Fact]
    public void LinesOnlyBetweenWindowsOfTheSameTile()
    {
        var f = Nested();
        // A and the B/C tile are separate tiles of the workspace: no line between them.
        var edge = Assert.Single(GroupEdges.Compute(f.Ws, f["C"], 10));
        Assert.Equal(f["C"].Parent, edge.Container);
        Assert.False(edge.Vertical);
        Assert.Equal(f["B"].WindowRect.Bottom, edge.Rect.Top);
        Assert.Equal(f["C"].WindowRect.Top, edge.Rect.Bottom);
        Assert.Equal(f["B"].WindowRect.Left, edge.Rect.Left);
        Assert.Equal(f["B"].WindowRect.Right, edge.Rect.Right);

        // Windows side by side directly on the workspace are separate tiles.
        var flat = new Fixture();
        flat.Open("A");
        flat.Open("B");
        flat.Open("C");
        flat.Compute(inner: 10);
        Assert.Empty(GroupEdges.Compute(flat.Ws, flat["C"], 10));

        // Splitting the whole workspace wraps it in one container: still not a tile of its own.
        flat.Tree.Focus(flat.Ws);
        flat.Tree.Split(Orientation.Vertical);
        flat.Compute(inner: 10);
        Assert.Equal("V[H[A B C]]", flat.Layout);
        Assert.Empty(GroupEdges.Compute(flat.Ws, flat["C"], 10));
    }

    [Fact]
    public void NestedTilesAndLevels()
    {
        var f = Nested();
        f.Tree.Focus(f["C"]);
        f.Tree.Split(Orientation.Horizontal);
        f.Open("D");
        f.Compute(inner: 10);
        Assert.Equal("H[A V[B H[C D]]]", f.Layout);

        var edges = GroupEdges.Compute(f.Ws, f["D"], 10);
        Assert.Equal(2, edges.Count);
        Assert.Equal(0, edges.Single(e => e.Container == f["D"].Parent).Level); // D's own tile
        Assert.Equal(1, edges.Single(e => e.Container == f["B"].Parent).Level); // the tile around it
        Assert.True(edges.Single(e => e.Container == f["D"].Parent).Vertical);
        Assert.Equal(f["C"].WindowRect.Right, edges.Single(e => e.Container == f["D"].Parent).Rect.Left);

        Assert.All(GroupEdges.Compute(f.Ws, f["A"], 10), e => Assert.Equal(2, e.Level)); // A is in no tile with them

        // "focus parent" on C/D: the container itself is the active tile.
        var container = f["C"].Parent!;
        Assert.Equal(0, GroupEdges.Compute(f.Ws, container, 10).Single(e => e.Container == container).Level);
    }

    [Fact]
    public void NoEdgesWithoutGapsOrInsideTabs()
    {
        var f = Nested();
        Assert.Empty(GroupEdges.Compute(f.Ws, f["C"], 0));

        f.Tree.Focus(f["C"]);
        f.Tree.SetLayout(LayoutKind.Tabbed);
        f.Compute(inner: 10);
        Assert.Empty(GroupEdges.Compute(f.Ws, f["C"], 10));
    }
}

public class OpacityCurveTests
{
    [Fact]
    public void ActiveIsOpaqueAndOlderWindowsFade()
    {
        Assert.Equal(100, OpacityCurve.Percent(0, 90, 70));
        Assert.Equal(90, OpacityCurve.Percent(1, 90, 70));
        Assert.Equal(84, OpacityCurve.Percent(2, 90, 70));
        int prev = 100;
        for (int rank = 1; rank < 30; rank++)
        {
            int p = OpacityCurve.Percent(rank, 90, 70);
            Assert.True(p <= prev && p >= 70, $"rank {rank}: {p}");
            prev = p;
        }
        Assert.Equal(70, OpacityCurve.Percent(40, 90, 70));
        Assert.Equal(80, OpacityCurve.Percent(5, 80, 95)); // a minimum above the first value is ignored
        Assert.Equal(255, OpacityCurve.ToAlpha(100));
        Assert.Equal(230, OpacityCurve.ToAlpha(90));
    }
}

public class EffectsConfigTests
{
    [Fact]
    public void DefaultsAndOptions()
    {
        var d = ConfigParser.Parse(DefaultConfig.Text);
        Assert.Empty(d.Errors);
        Assert.Equal("on", d.GroupLines);
        Assert.Equal(90, d.InactiveOpacity);
        Assert.Equal(70, d.InactiveOpacityMin);

        Assert.Equal(0x2E9EF4u, d.GroupLinesColor);
        Assert.Null(ConfigParser.Parse("group_lines on").GroupLinesColor); // falls back to client.focused's indicator

        var cfg = ConfigParser.Parse("""
            group_lines always
            group_lines_color #ff8800
            inactive_opacity 85 60
            opacity_ignore [process="^mpv$"]
            """);
        Assert.Empty(cfg.Errors);
        Assert.Equal("always", cfg.GroupLines);
        Assert.Equal(0xFF8800u, cfg.GroupLinesColor);
        Assert.Equal(85, cfg.InactiveOpacity);
        Assert.Equal(60, cfg.InactiveOpacityMin);
        Assert.Single(cfg.OpacityIgnore);

        Assert.Null(ConfigParser.Parse("inactive_opacity off").InactiveOpacity);
        Assert.Equal("off", ConfigParser.Parse("group_lines no").GroupLines);
        var single = ConfigParser.Parse("inactive_opacity 80%");
        Assert.Equal((80, 80), (single.InactiveOpacity!.Value, single.InactiveOpacityMin));
        Assert.Single(ConfigParser.Parse("inactive_opacity 5").Errors);
        Assert.Single(ConfigParser.Parse("group_lines sometimes").Errors);
    }

    [Fact]
    public void CheatsheetIsBoundEvenInOlderConfigs()
    {
        var grave = new KeyCombo(Modifiers.Alt, 0xC0);
        var shiftGrave = new KeyCombo(Modifiers.Alt | Modifiers.Shift, 0xC0);

        // The default config binds it explicitly.
        var d = ConfigParser.Parse(DefaultConfig.Text);
        Assert.False(d.Bindings.Single(b => b.Combo == grave && b.Mode == "default").Implicit);
        Assert.Equal("cheatsheet", d.Bindings.Single(b => b.Combo == shiftGrave && b.Mode == "default").Command);
        Assert.Contains(d.Bindings, b => b.Command == "focus last");

        // A config without it gets both keys...
        var old = ConfigParser.Parse("set $mod Alt\nbindsym $mod+h focus left");
        Assert.True(old.Bindings.Single(b => b.Combo == grave).Implicit);
        Assert.Equal("cheatsheet", old.Bindings.Single(b => b.Combo == shiftGrave).Command);

        // ...unless it uses the keys for something else, or binds the cheat sheet elsewhere.
        var taken = ConfigParser.Parse("set $mod Alt\nbindsym $mod+grave focus last");
        Assert.Equal("focus last", taken.Bindings.Single(b => b.Combo == grave).Command);
        Assert.Equal("cheatsheet", taken.Bindings.Single(b => b.Combo == shiftGrave).Command);
        var elsewhere = ConfigParser.Parse("set $mod Alt\nbindsym $mod+F12 cheatsheet");
        Assert.DoesNotContain(elsewhere.Bindings, b => b.Combo == grave || b.Combo == shiftGrave);

        // Win as $mod.
        var win = ConfigParser.Parse("set $mod Win");
        Assert.Contains(win.Bindings, b => b.Combo == new KeyCombo(Modifiers.Win, 0xC0) && b.Command == "cheatsheet");
    }

    [Fact]
    public void KeyboardLabelForCheatsheet()
    {
        var (label, cat) = KeyboardLabels.Describe("cheatsheet");
        Assert.Equal(KeyCategory.System, cat);
        Assert.False(string.IsNullOrEmpty(label));
        Assert.Contains(KeyboardLayout.Caps, c => c.Vk == 0xC0);
        var cfg = ConfigParser.Parse(DefaultConfig.Text);
        Assert.Equal("cheatsheet", KeyboardLayout.BindingsFor(cfg, "default", Modifiers.Alt)[0xC0].Command);
        Assert.True(KeyboardLayout.IsLayerModifier(0xA4, Modifiers.Alt));
        Assert.False(KeyboardLayout.IsLayerModifier(0xA0, Modifiers.Alt));
    }
}
