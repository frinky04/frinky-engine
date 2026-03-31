using System.Collections;
using FrinkyEngine.Core.Coroutines;
using FrinkyEngine.Core.Tests.TestSupport;

namespace FrinkyEngine.Core.Tests.Coroutines;

public sealed class CoroutineRunnerTests
{
    [Fact]
    public void CoroutineRunner_RespectsScaledAndUnscaledWaitsAndPauseResume()
    {
        var events = new List<string>();
        bool ready = false;
        bool keepWaiting = true;
        var runner = new CoroutineRunner();

        runner.Start(Run(events, () => ready, () => keepWaiting));

        runner.Tick(0.5f, 0.1f);
        events.Should().Equal("start");

        runner.Tick(0.5f, 0.1f);
        events.Should().Equal("start", "after-scaled");

        runner.PauseAll();
        runner.Tick(0.5f, 0.5f);
        events.Should().Equal("start", "after-scaled");

        runner.ResumeAll();
        runner.Tick(0.1f, 0.4f);
        events.Should().Equal("start", "after-scaled");

        runner.Tick(0.1f, 0.1f);
        events.Should().Equal("start", "after-scaled", "after-unscaled");

        ready = true;
        runner.Tick(0.1f, 0.1f);
        events.Should().Equal("start", "after-scaled", "after-unscaled", "after-until");

        keepWaiting = false;
        runner.Tick(0.1f, 0.1f);
        events.Should().Equal("start", "after-scaled", "after-unscaled", "after-until", "done");
        runner.HasCoroutines.Should().BeFalse();
    }

    [Fact]
    public void TimerRunner_HandlesRepeatsCancellationAndExceptions()
    {
        var runner = new TimerRunner();
        int repeatCount = 0;
        int oneShotCount = 0;
        Action repeat = () => repeatCount++;
        Action explode = () => throw new InvalidOperationException("boom");

        runner.Invoke(() => oneShotCount++, 0.25f);
        runner.InvokeRepeating(repeat, 0.1f, 0.2f);
        runner.Invoke(explode, 0.1f);

        using var errorCapture = new ConsoleErrorCapture();

        runner.Tick(0.1f);
        repeatCount.Should().Be(1);
        oneShotCount.Should().Be(0);
        errorCapture.Text.Should().Contain("[Timer] Exception in timer callback");

        runner.Tick(0.2f);
        oneShotCount.Should().Be(1);
        repeatCount.Should().Be(2);
        errorCapture.Text.Should().Contain("[Timer] Exception in timer callback");

        runner.Cancel(repeat);
        runner.Tick(0.05f);
        repeatCount.Should().Be(2);
        runner.HasTimers.Should().BeFalse();
    }

    [Fact]
    public void CoroutineRunner_StopsFaultingCoroutinesAndLogsTheFailure()
    {
        var runner = new CoroutineRunner();

        using var errorCapture = new ConsoleErrorCapture();

        runner.Start(ExplodeAfterYield());

        runner.Tick(0.1f, 0.1f);
        runner.HasCoroutines.Should().BeTrue();

        runner.Tick(0.1f, 0.1f);
        errorCapture.Text.Should().Contain("[Coroutine] Exception during tick");
        runner.HasCoroutines.Should().BeFalse();
    }

    private static IEnumerator Run(List<string> events, Func<bool> ready, Func<bool> keepWaiting)
    {
        events.Add("start");
        yield return new WaitForSeconds(0.5f);
        events.Add("after-scaled");
        yield return new WaitForSecondsRealtime(0.5f);
        events.Add("after-unscaled");
        yield return new WaitUntil(ready);
        events.Add("after-until");
        yield return new WaitWhile(keepWaiting);
        events.Add("done");
    }

    private static IEnumerator ExplodeAfterYield()
    {
        yield return null;
        throw new InvalidOperationException("boom");
    }
}
