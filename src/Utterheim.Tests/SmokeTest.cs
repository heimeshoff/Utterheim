namespace Utterheim.Tests;

/// <summary>
/// Smoke test to prove the build/test pipeline works end-to-end.
/// Exists only to validate that <c>dotnet test</c> can discover and run an
/// xUnit fact under this project. Real per-domain tests are introduced by
/// later tasks (first being main-040 — voice library language field).
/// </summary>
public class SmokeTest
{
    [Fact]
    public void TestInfrastructureIsWired()
    {
        Assert.True(true);
    }
}
