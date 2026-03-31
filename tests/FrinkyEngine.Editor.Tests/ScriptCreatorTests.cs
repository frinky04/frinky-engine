namespace FrinkyEngine.Editor.Tests;

public sealed class ScriptCreatorTests
{
    [Theory]
    [InlineData("PlayerController", true)]
    [InlineData("_Spawner2", true)]
    [InlineData("2Spawner", false)]
    [InlineData("Bad Name", false)]
    public void IsValidClassName_ValidatesIdentifiers(string name, bool expected)
    {
        ScriptCreator.IsValidClassName(name).Should().Be(expected);
    }

    [Fact]
    public void GenerateScript_ForComponentBaseEmitsLifecycleTemplate()
    {
        var script = ScriptCreator.GenerateScript("PlayerController", "Orbit", typeof(Component));

        script.Should().Contain("namespace Orbit.Scripts;");
        script.Should().Contain("public class PlayerController : Component");
        script.Should().Contain("public override void Start()");
        script.Should().Contain("public override void Update(float dt)");
    }

    [Fact]
    public void GenerateScript_ForFObjectBaseEmitsDisplayNameOverride()
    {
        var script = ScriptCreator.GenerateScript("OrbitDefinition", "Orbit", typeof(SampleFObject));

        script.Should().Contain("public class OrbitDefinition : SampleFObject");
        script.Should().Contain("public override string DisplayName => \"OrbitDefinition\"");
    }

    private sealed class SampleFObject : FObject
    {
    }
}
