using FrinkyEngine.Core.Physics;

namespace FrinkyEngine.Core.Tests.Physics;

public sealed class PhysicsProjectSettingsTests
{
    [Fact]
    public void ApplyFromRuntimeProjectSettings_NormalizesCurrent()
    {
        var runtime = new RuntimeProjectSettings
        {
            PhysicsFixedTimestep = 10f,
            PhysicsMaxSubstepsPerFrame = 0,
            PhysicsSolverVelocityIterations = 100,
            PhysicsSolverSubsteps = 0,
            PhysicsContactSpringFrequency = float.NaN,
            PhysicsContactDampingRatio = -2f,
            PhysicsMaximumRecoveryVelocity = 1000f,
            PhysicsDefaultFriction = -1f,
            PhysicsDefaultRestitution = 10f,
            PhysicsInterpolationEnabled = false
        };

        PhysicsProjectSettings.ApplyFrom(runtime);

        PhysicsProjectSettings.Current.FixedTimestep.Should().BeApproximately(1f / 15f, 0.0001f);
        PhysicsProjectSettings.Current.MaxSubstepsPerFrame.Should().Be(1);
        PhysicsProjectSettings.Current.SolverVelocityIterations.Should().Be(32);
        PhysicsProjectSettings.Current.SolverSubsteps.Should().Be(1);
        PhysicsProjectSettings.Current.ContactSpringFrequency.Should().Be(30f);
        PhysicsProjectSettings.Current.ContactDampingRatio.Should().Be(0f);
        PhysicsProjectSettings.Current.MaximumRecoveryVelocity.Should().Be(100f);
        PhysicsProjectSettings.Current.DefaultFriction.Should().Be(0f);
        PhysicsProjectSettings.Current.DefaultRestitution.Should().Be(1f);
        PhysicsProjectSettings.Current.InterpolationEnabled.Should().BeFalse();
    }

    [Fact]
    public void ApplyFromManifest_UsesManifestValuesAndFallbacks()
    {
        var manifest = new ExportManifest
        {
            PhysicsFixedTimestep = 1f / 120f,
            PhysicsMaxSubstepsPerFrame = 6,
            PhysicsSolverVelocityIterations = 10,
            PhysicsDefaultRestitution = 0.25f
        };

        PhysicsProjectSettings.ApplyFrom(manifest);

        PhysicsProjectSettings.Current.FixedTimestep.Should().BeApproximately(1f / 120f, 0.0001f);
        PhysicsProjectSettings.Current.MaxSubstepsPerFrame.Should().Be(6);
        PhysicsProjectSettings.Current.SolverVelocityIterations.Should().Be(10);
        PhysicsProjectSettings.Current.SolverSubsteps.Should().Be(1);
        PhysicsProjectSettings.Current.DefaultRestitution.Should().Be(0.25f);
        PhysicsProjectSettings.Current.InterpolationEnabled.Should().BeTrue();
    }
}
