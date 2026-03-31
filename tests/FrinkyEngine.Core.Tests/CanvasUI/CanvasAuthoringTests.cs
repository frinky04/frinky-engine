using FrinkyEngine.Core.CanvasUI;
using FrinkyEngine.Core.CanvasUI.Authoring;
using FrinkyEngine.Core.CanvasUI.Panels;
using FrinkyEngine.Core.CanvasUI.Styles;
using FrinkyEngine.Core.CanvasUI.Styles.Css;
using Raylib_cs;

namespace FrinkyEngine.Core.Tests.CanvasUI;

public sealed class CanvasAuthoringTests
{
    [Fact]
    public void RootPanelLoadMarkup_BindsClassesStylesAndInheritedContext()
    {
        var root = new RootPanel();

        var created = root.LoadMarkup(
            """
            <Panel class="primary">
              <Label text="Ready" style="color: red; font-size: 22px;" />
            </Panel>
            """,
            bindingContext: "view-model");

        var panel = created.Should().BeOfType<Panel>().Subject;
        var label = panel.Children.Should().ContainSingle().Subject.Should().BeOfType<Label>().Subject;

        panel.HasClass("primary").Should().BeTrue();
        label.Text.Should().Be("Ready");
        label.Style.Color.Should().Be(new Color(255, 0, 0, 255));
        label.Style.FontSize.Should().Be(22f);
        label.BindingContext.Should().Be("view-model");
    }

    [Fact]
    public void RootPanelLoadMarkup_InlineStylesOverrideStylesheetRules()
    {
        var root = new RootPanel();
        root.LoadStyleSheet("Label.primary { color: blue; font-size: 12px; }");

        var created = root.LoadMarkup("""<Label class="primary" text="Hello" style="color: green;" />""");
        var label = created.Should().BeOfType<Label>().Subject;

        var resolved = StyleResolver.Resolve(label, CssParser.Parse("Label.primary { color: blue; font-size: 12px; }"));
        resolved.Color.Should().Be(new Color(0, 128, 0, 255));
        resolved.FontSize.Should().Be(12f);
    }

    [Fact]
    public void CssParser_MalformedDeclarationDoesNotPoisonFollowingRule()
    {
        var rules = CssParser.Parse(
            """
            Label {
              color:
            }

            Button.primary {
              color: red;
            }
            """);

        rules.Should().Contain(rule => rule.Selector.Parts[0].TypeName == "Button");
        rules.Last(rule => rule.Selector.Parts[0].TypeName == "Button").Declarations.Color.Should().Be(new Color(255, 0, 0, 255));
    }

    [Fact]
    public void CanvasValueConverter_ParsesBindingsEnumsScalarsAndColors()
    {
        CanvasValueConverter.TryParseBindingExpression("{ PlayerName }", out var binding).Should().BeTrue();
        binding.Should().Be("PlayerName");

        CanvasValueConverter.TryConvertValue("center", typeof(TextAlign), out var textAlign).Should().BeTrue();
        textAlign.Should().Be(TextAlign.Center);

        CanvasValueConverter.TryConvertValue("42", typeof(int), out var integerValue).Should().BeTrue();
        integerValue.Should().Be(42);

        CanvasValueConverter.TryConvertValue("1.5", typeof(float), out var floatValue).Should().BeTrue();
        floatValue.Should().Be(1.5f);

        CanvasValueConverter.TryConvertValue("#336699", typeof(Color), out var colorValue).Should().BeTrue();
        colorValue.Should().Be(new Color(0x33, 0x66, 0x99, 255));
    }

    [Fact]
    public void CanvasValueConverter_ToPascalCaseHandlesHyphenatedNames()
    {
        CanvasValueConverter.ToPascalCase("space-between").Should().Be("SpaceBetween");
    }
}
