using FrinkyEngine.Core.CanvasUI;
using FrinkyEngine.Core.CanvasUI.Styles;
using FrinkyEngine.Core.CanvasUI.Styles.Css;
using FrinkyEngine.Core.Tests.TestSupport;
using Raylib_cs;

namespace FrinkyEngine.Core.Tests.CanvasUI;

public sealed class CanvasUiTests
{
    [Fact]
    public void CssParser_ParsesSelectorListsAndDeclarations()
    {
        var rules = CssParser.Parse(
            """
            Panel.root:hover > Panel.child, Panel.alt {
              color: red;
              font-size: 24px;
            }
            """);

        rules.Should().HaveCount(2);
        rules[0].Selector.Parts.Should().HaveCount(2);
        rules[0].Selector.Parts[0].ClassNames.Should().Contain("root");
        rules[0].Selector.Parts[0].PseudoClasses.Should().Contain("hover");
        rules[0].Selector.Parts[1].Combinator.Should().Be(CssCombinator.Child);
        rules[0].Declarations.Color.Should().Be(new Color(255, 0, 0, 255));
        rules[0].Declarations.FontSize.Should().Be(24f);
    }

    [Fact]
    public void CssSelectorMatcher_MatchesDescendantsChildrenAndPseudoClasses()
    {
        var root = new Panel();
        root.AddClass("root");
        root.PseudoClasses = PseudoClassFlags.Hover;

        var middle = new Panel();
        root.AddChild(middle);

        var leaf = new Panel();
        leaf.AddClass("child");
        middle.AddChild(leaf);

        var descendant = CssParser.Parse("Panel.root:hover Panel.child { color: red; }").Single().Selector;
        var child = CssParser.Parse("Panel.root:hover > Panel.child { color: red; }").Single().Selector;

        CssSelectorMatcher.Matches(leaf, descendant).Should().BeTrue();
        CssSelectorMatcher.Matches(leaf, child).Should().BeFalse();
    }

    [Fact]
    public void Panel_AddChildAndReparent_InvokesOnCreatedOnlyOnce()
    {
        var firstParent = new Panel();
        var secondParent = new Panel();
        var child = new ProbePanel();

        firstParent.AddChild(child);
        child.CreatedCount.Should().Be(1);

        secondParent.AddChild(child);
        child.CreatedCount.Should().Be(1);
        child.Parent.Should().Be(secondParent);
    }

    [Fact]
    public void BindingContextAndStyleResolution_RespectInheritanceSpecificityAndInlineOverrides()
    {
        var parent = new Panel();
        parent.SetBindingContext("ctx");

        var child = new Panel();
        child.AddClass("target");
        parent.AddChild(child);

        child.BindingContext.Should().Be("ctx");
        child.SetBindingContext("local");
        parent.SetBindingContext("updated");
        child.BindingContext.Should().Be("local");

        var rules = CssParser.Parse(
            """
            Panel { color: red; font-size: 12px; }
            Panel.target { color: blue; }
            """
        );
        StyleResolver.SortRules(rules);
        child.Style.Color = new Color(1, 2, 3, 255);

        var resolved = StyleResolver.Resolve(child, rules, parent: null);
        resolved.Color.Should().Be(new Color(1, 2, 3, 255));
        resolved.FontSize.Should().Be(12f);
    }
}
