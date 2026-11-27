namespace NyriaNest.Api.Tests.TestSupport;

/// <summary>
/// A clock the test sets and moves (#85), for services that ask TimeProvider for the time.
/// Starts at a fixed moment, so expected dates can be written exactly.
/// </summary>
public sealed class FakeClock : TimeProvider
{
    public static readonly DateTime Start = new(2026, 3, 2, 10, 0, 0, DateTimeKind.Utc);

    public DateTime UtcNow { get; set; } = Start;

    public override DateTimeOffset GetUtcNow() => new(UtcNow);

    public void Advance(TimeSpan by) => UtcNow += by;
}
