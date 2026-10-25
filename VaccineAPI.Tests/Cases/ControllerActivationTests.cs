using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VaccineAPI.Controllers;
using VaccineAPI.Tests.Infrastructure;
using Xunit;

namespace VaccineAPI.Tests.Cases;

/// <summary>
/// The framework must be able to construct PaCashHandoverController. A second public constructor makes
/// activation ambiguous and fails EVERY endpoint on the controller (reconciliation, confirm, cash-in-hand).
/// </summary>
public class ControllerActivationTests
{
    [Fact]
    public void Controller_can_be_activated_by_the_framework()
    {
        using var w = new StockWorld();
        var cfg = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var sc = new ServiceCollection();
        sc.AddSingleton<IConfiguration>(cfg);
        sc.AddScoped(_ => w.NewContext());
        using var sp = sc.BuildServiceProvider();
        // The same factory the MVC controller activator builds. Unit tests construct it with (db) only.
        var factory = ActivatorUtilities.CreateFactory(typeof(PaCashHandoverController), Type.EmptyTypes);
        var c = factory(sp, null);
        Assert.NotNull(c);
    }
}
