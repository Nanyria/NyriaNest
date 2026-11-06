using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NyriaNest.Api.Data;
using NyriaNest.Api.Tests.TestSupport;

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
}
