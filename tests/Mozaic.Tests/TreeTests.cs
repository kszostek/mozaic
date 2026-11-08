using Mozaic.Core;

namespace Mozaic.Tests;

/// <summary>Builds small trees and describes them compactly, e.g. "H[A V[B C]]".</summary>
internal sealed class Fixture
{
    public Tree Tree { get; }
    public Node Output { get; }
    public Node? Output2 { get; }
    private readonly Dictionary<string, Node> _windows = new();

    public Fixture(bool twoOutputs = false, TreeOptions? options = null)
    {
        Tree = new Tree(options ?? new TreeOptions());
        Output = Tree.AddOutput("DISPLAY1", new Rect(0, 0, 1000, 500), new Rect(0, 0, 1000, 500));
        Tree.EnsureWorkspace(Output, "1");
        if (twoOutputs)
        {
            Output2 = Tree.AddOutput("DISPLAY2", new Rect(1000, 0, 2000, 500), new Rect(1000, 0, 2000, 500));
            Tree.EnsureWorkspace(Output2, "2");
        }
        Tree.Focus(Output.CurrentWorkspace!);
    }

    public Node Ws => Tree.FocusedWorkspace!;

    public Node Open(string name, Node? ws = null, bool focus = true, bool floating = false)
    {
        var n = new Node(NodeKind.Window) { Title = name };
        _windows[name] = n;
        Tree.AddWindow(n, ws ?? Tree.FocusedWorkspace!, floating);
        if (focus) Tree.Focus(n);
        return n;
    }

    public Node this[string name] => _windows[name];

    public static string Describe(Node n)
    {
        if (n.Kind == NodeKind.Window) return n.Title;
        string l = n.Layout switch
        {
            LayoutKind.SplitH => "H",
            LayoutKind.SplitV => "V",
            LayoutKind.Tabbed => "T",
            _ => "S",
        };
        return l + "[" + string.Join(" ", n.Children.Select(Describe)) + "]";
    }

    public string Layout => Describe(Ws);

    public void Compute(int inner = 0, int outer = 0) =>
        LayoutEngine.Compute(Tree, _ => new LayoutSettings
        {
            InnerGap = inner, OuterTop = outer, OuterBottom = outer, OuterLeft = outer, OuterRight = outer, TabHeight = 20,
        });
}

public class TreeTests
{
    [Fact]
    public void NewWindowsOpenAfterTheFocusedOne()
    {
        var f = new Fixture();
        f.Open("A");
        f.Open("B");
        f.Tree.Focus(f["A"]);
        f.Open("C");
        Assert.Equal("H[A C B]", f.Layout);
        Assert.Same(f["C"], f.Tree.Focused);
    }

    [Fact]
    public void PercentsAreSharedEvenly()
    {
        var f = new Fixture();
        f.Open("A"); f.Open("B"); f.Open("C");
        foreach (var c in f.Ws.Children) Assert.Equal(1.0 / 3, c.Percent, 6);
    }

    [Fact]
    public void FocusDirectionMovesBetweenSiblingsAndWraps()
    {
        var f = new Fixture();
        f.Open("A"); f.Open("B"); f.Open("C");
        Assert.True(f.Tree.FocusDirection(Direction.Left));
        Assert.Same(f["B"], f.Tree.Focused);
        f.Tree.FocusDirection(Direction.Left);
        Assert.Same(f["A"], f.Tree.Focused);
        // focus_wrapping yes: wraps around inside the container
        f.Tree.FocusDirection(Direction.Left);
        Assert.Same(f["C"], f.Tree.Focused);
        // up/down has nowhere to go in a horizontal split
        Assert.False(f.Tree.FocusDirection(Direction.Up));
    }

    [Fact]
    public void SplitVerticalThenOpenNestsWindow()
    {
        var f = new Fixture();
        f.Open("A"); f.Open("B");
        f.Tree.Split(Orientation.Vertical);
        f.Open("C");
        Assert.Equal("H[A V[B C]]", f.Layout);
        f.Tree.FocusDirection(Direction.Up);
        Assert.Same(f["B"], f.Tree.Focused);
        f.Tree.FocusDirection(Direction.Left);
        Assert.Same(f["A"], f.Tree.Focused);
        // Entering the vertical container again returns to the last focused window in it (i3 semantics)
        f.Tree.FocusDirection(Direction.Right);
        Assert.Same(f["B"], f.Tree.Focused);
    }

    [Fact]
    public void SplitOnSingleChildJustChangesOrientation()
    {
        var f = new Fixture();
        f.Open("A");
        f.Tree.Split(Orientation.Vertical);
        f.Open("B");
        Assert.Equal("V[A B]", f.Layout);
    }

    [Fact]
    public void MoveSwapsWithSibling()
    {
        var f = new Fixture();
        f.Open("A"); f.Open("B"); f.Open("C");
        f.Tree.MoveDirection(Direction.Left);
        Assert.Equal("H[A C B]", f.Layout);
        Assert.Same(f["C"], f.Tree.Focused);
    }

    [Fact]
    public void MoveUpForcesWorkspaceOrientation()
    {
        var f = new Fixture();
        f.Open("A"); f.Open("B"); f.Open("C");
        f.Tree.Focus(f["B"]);
        f.Tree.MoveDirection(Direction.Up);
        Assert.Equal("V[B H[A C]]", f.Layout);
        Assert.Same(f["B"], f.Tree.Focused);
    }

    [Fact]
    public void MoveIntoAdjacentContainer()
    {
        var f = new Fixture();
        f.Open("A"); f.Open("B");
        f.Tree.Split(Orientation.Vertical);
        f.Open("C");
        // H[A V[B C]] -> move A right: enters V after its last focused child (C),
        // then the workspace absorbs the lone V container.
        f.Tree.Focus(f["A"]);
        f.Tree.MoveDirection(Direction.Right);
        Assert.Equal("V[B C A]", f.Layout);
        Assert.Same(f["A"], f.Tree.Focused);
    }

    [Fact]
    public void MoveOutOfNestedContainer()
    {
        var f = new Fixture();
        f.Open("A"); f.Open("B");
        f.Tree.Split(Orientation.Vertical);
        f.Open("C");
        f.Open("D");
        // H[A V[B C D]]: moving D right leaves the vertical container
        f.Tree.MoveDirection(Direction.Right);
        Assert.Equal("H[A V[B C] D]", f.Layout);
    }

    [Fact]
    public void ClosingFocusesPreviouslyFocusedSibling()
    {
        var f = new Fixture();
        f.Open("A"); f.Open("B"); f.Open("C");
        f.Tree.Focus(f["A"]);
        f.Tree.Focus(f["C"]);
        f.Tree.Remove(f["C"]);
        Assert.Same(f["A"], f.Tree.Focused);
        Assert.Equal("H[A B]", f.Layout);
    }

    [Fact]
    public void ClosingFlattensSingleChildContainers()
    {
        var f = new Fixture();
        f.Open("A"); f.Open("B");
        f.Tree.Split(Orientation.Vertical);
        f.Open("C");
        f.Tree.Remove(f["C"]);
        Assert.Equal("H[A B]", f.Layout);
        Assert.Same(f["B"], f.Tree.Focused);
    }

    [Fact]
    public void ClosingWithoutFlattenKeepsContainer()
    {
        var f = new Fixture(options: new TreeOptions { FlattenContainers = false });
        f.Open("A"); f.Open("B");
        f.Tree.Split(Orientation.Vertical);
        f.Open("C");
        f.Tree.Remove(f["C"]);
        Assert.Equal("H[A V[B]]", f.Layout);
    }

    [Fact]
    public void LayoutTabbedWrapsWorkspaceAndToggleSplitRestores()
    {
        var f = new Fixture();
        f.Open("A"); f.Open("B");
        f.Tree.SetLayout(LayoutKind.Tabbed);
        Assert.Equal("H[T[A B]]", f.Layout);
        f.Open("C");
        Assert.Equal("H[T[A B C]]", f.Layout);
        f.Tree.ToggleLayout(new[] { "split" });
        Assert.Equal("H[A B C]", f.Layout);
    }

    [Fact]
    public void ToggleSplitFlipsOrientation()
    {
        var f = new Fixture();
        f.Open("A"); f.Open("B");
        f.Tree.ToggleLayout(new[] { "split" });
        Assert.Equal("V[A B]", f.Layout);
    }

    [Fact]
    public void WorkspaceSwitchingCreatesAndDestroysWorkspaces()
    {
        var f = new Fixture();
        f.Open("A");
        var ws1 = f.Ws;
        var ws3 = f.Tree.GetOrCreateWorkspace("3");
        f.Tree.ShowWorkspace(ws3);
        Assert.Same(ws3, f.Tree.FocusedWorkspace);
        Assert.Same(ws3, f.Output.CurrentWorkspace);
        Assert.Equal("1", f.Tree.PreviousWorkspaceName);
        // Going back destroys the empty workspace 3
        f.Tree.ShowWorkspace(ws1);
        Assert.Null(f.Tree.FindWorkspace("3"));
        Assert.Same(f["A"], f.Tree.Focused);
    }

    [Fact]
    public void MoveToWorkspaceKeepsFocusOnSourceWorkspace()
    {
        var f = new Fixture();
        f.Open("A"); f.Open("B");
        var ws2 = f.Tree.GetOrCreateWorkspace("2");
        f.Tree.MoveToWorkspace(f["B"], ws2);
        Assert.Equal("H[A]", f.Layout);
        Assert.Same(f["A"], f.Tree.Focused);
        Assert.Same(ws2, Tree.WorkspaceOf(f["B"]));
        f.Tree.ShowWorkspace(ws2);
        Assert.Same(f["B"], f.Tree.Focused);
    }

    [Fact]
    public void FocusCrossesOutputs()
    {
        var f = new Fixture(twoOutputs: true);
        f.Open("A");
        var ws2 = f.Output2!.CurrentWorkspace!;
        f.Open("B", ws2, focus: false);
        Assert.True(f.Tree.FocusDirection(Direction.Right));
        Assert.Same(f["B"], f.Tree.Focused);
        Assert.Same(f.Output2, f.Tree.FocusedOutput);
        Assert.True(f.Tree.FocusDirection(Direction.Left));
        Assert.Same(f["A"], f.Tree.Focused);
    }

    [Fact]
    public void MoveLoneWindowToNextOutput()
    {
        var f = new Fixture(twoOutputs: true);
        f.Open("A");
        Assert.True(f.Tree.MoveDirection(Direction.Right));
        Assert.Same(f.Output2!.CurrentWorkspace, Tree.WorkspaceOf(f["A"]));
        Assert.Same(f["A"], f.Tree.Focused);
    }

    [Fact]
    public void ResizeGrowWidthTakesFromSiblings()
    {
        var f = new Fixture();
        f.Open("A"); f.Open("B");
        f.Compute();
        Assert.True(f.Tree.ResizeTiling(f["B"], width: true, ppt: 0.10, px: null));
        Assert.Equal(0.4, f["A"].Percent, 6);
        Assert.Equal(0.6, f["B"].Percent, 6);
        // Height cannot change in a pure horizontal layout
        Assert.False(f.Tree.ResizeTiling(f["B"], width: false, ppt: 0.10, px: null));
    }

    [Fact]
    public void ResizeEdgeOnlyAffectsNeighbour()
    {
        var f = new Fixture();
        f.Open("A"); f.Open("B"); f.Open("C");
        f.Compute();
        Assert.True(f.Tree.ResizeEdge(f["B"], Direction.Right, 100)); // 100px of 1000px
        Assert.Equal(1.0 / 3, f["A"].Percent, 6);
        Assert.Equal(1.0 / 3 + 0.1, f["B"].Percent, 6);
        Assert.Equal(1.0 / 3 - 0.1, f["C"].Percent, 6);
    }

    [Fact]
    public void FloatingToggleAndModeToggle()
    {
        var f = new Fixture();
        f.Open("A"); f.Open("B");
        f.Tree.SetFloating(f["B"], true);
        Assert.Equal("H[A]", f.Layout);
        Assert.True(f["B"].InFloating);
        Assert.Same(f["B"], f.Tree.Focused);
        f.Tree.FocusModeToggle();
        Assert.Same(f["A"], f.Tree.Focused);
        f.Tree.FocusModeToggle();
        Assert.Same(f["B"], f.Tree.Focused);
        f.Tree.SetFloating(f["B"], false);
        Assert.Equal("H[A B]", f.Layout);
    }

    [Fact]
    public void ScratchpadHidesAndShows()
    {
        var f = new Fixture();
        f.Open("A"); f.Open("B");
        f.Tree.MoveToScratchpad(f["B"]);
        Assert.Equal("H[A]", f.Layout);
        Assert.Contains(f["B"], f.Tree.Scratchpad);
        Assert.Same(f["A"], f.Tree.Focused);
        Assert.True(f.Tree.ScratchpadShow());
        Assert.True(f["B"].InFloating);
        Assert.Same(f["B"], f.Tree.Focused);
        // Showing again while focused hides it
        Assert.True(f.Tree.ScratchpadShow());
        Assert.Contains(f["B"], f.Tree.Scratchpad);
    }

    [Fact]
    public void FocusParentThenNewWindowGoesNextToContainer()
    {
        var f = new Fixture();
        f.Open("A"); f.Open("B");
        f.Tree.Split(Orientation.Vertical);
        f.Open("C");
        f.Tree.FocusParent();
        Assert.Equal(NodeKind.Container, f.Tree.Focused!.Kind);
        f.Open("D");
        Assert.Equal("H[A V[B C] D]", f.Layout);
    }

    [Fact]
    public void SwapExchangesPositions()
    {
        var f = new Fixture();
        f.Open("A"); f.Open("B"); f.Open("C");
        f.Tree.Swap(f["A"], f["C"]);
        Assert.Equal("H[C B A]", f.Layout);
    }

    [Fact]
    public void MoveNextToCreatesSplitWhenOrientationDiffers()
    {
        var f = new Fixture();
        f.Open("A"); f.Open("B"); f.Open("C");
        f.Tree.MoveNextTo(f["C"], f["A"], Direction.Down);
        Assert.Equal("H[V[A C] B]", f.Layout);
        f.Tree.MoveNextTo(f["B"], f["A"], Direction.Left);
        // B leaves the workspace, the lone V container is absorbed into the workspace
        Assert.Equal("V[H[B A] C]", f.Layout);
    }

    [Fact]
    public void MoveNextToLoneWindowChangesOrientationInsteadOfNesting()
    {
        var f = new Fixture();
        f.Open("A"); f.Open("B");
        f.Tree.MoveNextTo(f["A"], f["B"], Direction.Down);
        Assert.Equal("V[B A]", f.Layout);
    }

    [Fact]
    public void StickyFloatingFollowsWorkspace()
    {
        var f = new Fixture();
        f.Open("A");
        f.Open("P", floating: true);
        f["P"].Sticky = true;
        f["P"].FloatRect = new Rect(10, 10, 110, 110);
        var ws2 = f.Tree.GetOrCreateWorkspace("2");
        f.Tree.ShowWorkspace(ws2);
        Assert.Same(ws2, Tree.WorkspaceOf(f["P"]));
    }

    [Fact]
    public void WorkspaceOrderingIsNumericFirst()
    {
        var f = new Fixture();
        f.Tree.GetOrCreateWorkspace("10");
        f.Tree.GetOrCreateWorkspace("web");
        f.Tree.GetOrCreateWorkspace("2:mail");
        var names = f.Output.Children.Select(w => w.Name).ToList();
        Assert.Equal(new[] { "1", "2:mail", "10", "web" }, names);
        Assert.Equal(2, f.Tree.FindWorkspaceByNumber(2)!.Number);
    }

    [Fact]
    public void RemovingOutputMovesWorkspaces()
    {
        var f = new Fixture(twoOutputs: true);
        f.Open("A", f.Output2!.CurrentWorkspace, focus: true);
        f.Tree.RemoveOutput(f.Output2);
        Assert.Single(f.Tree.Outputs);
        Assert.Same(f.Output, Tree.OutputOf(f["A"]));
        Assert.Same(f["A"], f.Tree.Focused);
    }

    [Fact]
    public void MoveWorkspaceToOutput()
    {
        var f = new Fixture(twoOutputs: true);
        f.Open("A");
        var ws1 = f.Ws;
        f.Tree.MoveWorkspaceToOutput(ws1, f.Output2!);
        Assert.Same(f.Output2, ws1.Parent);
        Assert.Same(ws1, f.Output2!.CurrentWorkspace);
        Assert.NotNull(f.Output.CurrentWorkspace);
        Assert.NotSame(ws1, f.Output.CurrentWorkspace);
        Assert.Same(f["A"], f.Tree.Focused);
    }
}

public class LayoutTests
{
    [Fact]
    public void HorizontalSplitFillsOutputExactly()
    {
        var f = new Fixture();
        f.Open("A"); f.Open("B");
        f.Compute();
        Assert.Equal(new Rect(0, 0, 500, 500), f["A"].WindowRect);
        Assert.Equal(new Rect(500, 0, 1000, 500), f["B"].WindowRect);
    }

    [Fact]
    public void GapsFollowI3GapsSemantics()
    {
        var f = new Fixture();
        f.Open("A"); f.Open("B");
        f.Compute(inner: 10, outer: 4);
        var a = f["A"].WindowRect;
        var b = f["B"].WindowRect;
        Assert.Equal(14, a.Left);             // outer + inner at the edge
        Assert.Equal(14, a.Top);
        Assert.Equal(10, b.Left - a.Right);   // inner between windows
        Assert.Equal(1000 - 14, b.Right);
        Assert.Equal(500 - 14, b.Bottom);
    }

    [Fact]
    public void TabbedShowsOnlyFocusedChildBelowTabBar()
    {
        var f = new Fixture();
        f.Open("A"); f.Open("B");
        f.Tree.SetLayout(LayoutKind.Tabbed);
        var bars = LayoutEngine.Compute(f.Tree, _ => new LayoutSettings { TabHeight = 20 });
        Assert.Single(bars);
        Assert.Equal(new Rect(0, 0, 1000, 20), bars[0].Rect);
        Assert.True(f["B"].Visible);
        Assert.False(f["A"].Visible);
        Assert.Equal(new Rect(0, 20, 1000, 500), f["B"].WindowRect);
    }

    [Fact]
    public void StackedReservesOneRowPerChild()
    {
        var f = new Fixture();
        f.Open("A"); f.Open("B"); f.Open("C");
        f.Tree.SetLayout(LayoutKind.Stacked);
        var bars = LayoutEngine.Compute(f.Tree, _ => new LayoutSettings { TabHeight = 20 });
        Assert.Equal(60, bars[0].Rect.Height);
        Assert.Equal(60, f["C"].WindowRect.Top);
    }

    [Fact]
    public void HiddenWorkspacesAreNotVisible()
    {
        var f = new Fixture();
        f.Open("A");
        var ws2 = f.Tree.GetOrCreateWorkspace("2");
        f.Tree.ShowWorkspace(ws2);
        f.Open("B");
        f.Compute();
        Assert.False(f["A"].Visible);
        Assert.True(f["B"].Visible);
    }

    [Fact]
    public void SmartGapsRemoveGapsForSingleWindow()
    {
        var f = new Fixture();
        f.Open("A");
        LayoutEngine.Compute(f.Tree, _ => new LayoutSettings { InnerGap = 10, OuterLeft = 5, OuterTop = 5, OuterRight = 5, OuterBottom = 5, SmartGaps = true });
        Assert.Equal(new Rect(0, 0, 1000, 500), f["A"].WindowRect);
    }

    [Fact]
    public void FullscreenCoversWholeOutput()
    {
        var f = new Fixture();
        f.Open("A"); f.Open("B");
        f.Tree.SetFullscreen(f["A"], true);
        f.Compute(inner: 10);
        Assert.Equal(f.Output.OutputRect, f["A"].WindowRect);
    }

    [Fact]
    public void BorderWidthInsetsWindow()
    {
        var f = new Fixture();
        f.Open("A");
        LayoutEngine.Compute(f.Tree, _ => new LayoutSettings { BorderWidth = 3 });
        Assert.Equal(new Rect(3, 3, 997, 497), f["A"].WindowRect);
    }
}
