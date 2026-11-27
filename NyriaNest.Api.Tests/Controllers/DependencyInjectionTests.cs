using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Application.Interfaces;
using NyriaNest.Application.Loans;
using NyriaNest.Infrastructure.Data;

namespace NyriaNest.Api.Tests.Controllers;

public class DependencyInjectionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public void IAppDbContext_IsTheSameInstanceAsAppDbContext_WithinARequest()
    {
        // The services use IAppDbContext and the repositories use AppDbContext.
        // Two instances would split tracked entities, and AddEbookAsync's
        // transaction wouldn't cover what the repositories save.
        using var scope = factory.Services.CreateScope();

        var viaInterface = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var concrete = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        viaInterface.Should().BeSameAs(concrete);
    }

    [Fact]
    public void TheClock_IsTheSystemClock()
    {
        // #85: new code asks TimeProvider for the time, so tests can swap in a fake clock
        factory.Services.GetRequiredService<TimeProvider>().Should().BeSameAs(TimeProvider.System);
    }

    [Fact]
    public void LendingOptions_DefaultTo14DaysAnd1Month()
    {
        // #85: remind the owner after 14 days, expire a request after 1 month
        var options = factory.Services.GetRequiredService<IOptions<LendingOptions>>().Value;

        options.RequestReminderDays.Should().Be(14);
        options.RequestExpiryMonths.Should().Be(1);
    }
}
