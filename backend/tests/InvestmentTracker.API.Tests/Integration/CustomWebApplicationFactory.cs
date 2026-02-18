using System.Collections.Generic;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Infrastructure.MarketData;
using InvestmentTracker.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Moq;

namespace InvestmentTracker.API.Tests.Integration;

/// <summary>
/// Custom WebApplicationFactory for integration testing.
/// Uses an in-memory database and configurable mock services.
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestJwtSecret = "your-256-bit-secret-key-here-minimum-32-chars";

    static CustomWebApplicationFactory()
    {
        Environment.SetEnvironmentVariable("Jwt__Secret", TestJwtSecret);
        Environment.SetEnvironmentVariable("Jwt__Issuer", "InvestmentTracker");
        Environment.SetEnvironmentVariable("Jwt__Audience", "InvestmentTracker");
    }

    private Guid _testUserId = Guid.NewGuid();

    public Guid TestUserId
    {
        get => _testUserId;
        set => _testUserId = value;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = TestJwtSecret,
                ["Jwt:Issuer"] = "InvestmentTracker",
                ["Jwt:Audience"] = "InvestmentTracker",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove the existing DbContext options
            var dbContextDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (dbContextDescriptor != null)
                services.Remove(dbContextDescriptor);

            // Remove any EF Core related service registrations that depend on the original provider
            services.RemoveAll(typeof(DbContextOptions));

            // Create a unique database name for each test run
            var databaseName = $"InvestmentTrackerTest_{Guid.NewGuid()}";

            // Add in-memory database for testing
            services.AddDbContext<AppDbContext>((_, options) =>
            {
                options.UseInMemoryDatabase(databaseName);
            });

            // Replace ICurrentUserService with a mock that returns TestUserId
            var currentUserServiceDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(ICurrentUserService));
            if (currentUserServiceDescriptor != null)
                services.Remove(currentUserServiceDescriptor);

            services.AddScoped<ICurrentUserService>(_ =>
            {
                var mock = new Mock<ICurrentUserService>();
                mock.Setup(x => x.UserId).Returns(() => _testUserId);
                return mock.Object;
            });

            // Avoid external network dependency in integration tests.
            services.RemoveAll<IYahooHistoricalPriceService>();
            services.AddScoped<IYahooHistoricalPriceService>(_ =>
            {
                var mock = new Mock<IYahooHistoricalPriceService>();
                mock.Setup(x => x.GetExchangeRateAsync(
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.IsAny<DateOnly>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync((string from, string to, DateOnly date, CancellationToken _) =>
                    {
                        var fromNormalized = from.ToUpperInvariant();
                        var toNormalized = to.ToUpperInvariant();

                        if (string.Equals(fromNormalized, toNormalized, StringComparison.OrdinalIgnoreCase))
                        {
                            return new YahooExchangeRateResult
                            {
                                CurrencyPair = $"{fromNormalized}{toNormalized}",
                                Rate = 1m,
                                ActualDate = date
                            };
                        }

                        if (fromNormalized == "USD" && toNormalized == "TWD")
                        {
                            return new YahooExchangeRateResult
                            {
                                CurrencyPair = "USDTWD",
                                Rate = 32m,
                                ActualDate = date
                            };
                        }

                        if (fromNormalized == "TWD" && toNormalized == "USD")
                        {
                            return new YahooExchangeRateResult
                            {
                                CurrencyPair = "TWDUSD",
                                Rate = 0.03125m,
                                ActualDate = date
                            };
                        }

                        return new YahooExchangeRateResult
                        {
                            CurrencyPair = $"{fromNormalized}{toNormalized}",
                            Rate = 1m,
                            ActualDate = date
                        };
                    });

                mock.Setup(x => x.GetHistoricalPriceAsync(
                        It.IsAny<string>(),
                        It.IsAny<DateOnly>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync((string symbol, DateOnly date, CancellationToken _) =>
                    {
                        var currency = symbol.EndsWith(".TW", StringComparison.OrdinalIgnoreCase)
                            || symbol.EndsWith(".TWO", StringComparison.OrdinalIgnoreCase)
                            ? "TWD"
                            : "USD";

                        return new YahooHistoricalPriceResult
                        {
                            Price = 100m,
                            ActualDate = date,
                            Currency = currency
                        };
                    });

                mock.Setup(x => x.GetHistoricalPriceSeriesAsync(
                        It.IsAny<string>(),
                        It.IsAny<DateOnly>(),
                        It.IsAny<DateOnly>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync((string symbol, DateOnly fromDate, DateOnly toDate, CancellationToken _) =>
                    {
                        var currency = symbol.EndsWith(".TW", StringComparison.OrdinalIgnoreCase)
                            || symbol.EndsWith(".TWO", StringComparison.OrdinalIgnoreCase)
                            ? "TWD"
                            : "USD";

                        var series = new List<YahooHistoricalPricePoint>
                        {
                            new() { Date = fromDate, Price = 100m, Currency = currency }
                        };

                        if (toDate != fromDate)
                        {
                            series.Add(new YahooHistoricalPricePoint
                            {
                                Date = toDate,
                                Price = 100m,
                                Currency = currency
                            });
                        }

                        return (IReadOnlyList<YahooHistoricalPricePoint>)series;
                    });

                return mock.Object;
            });
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Build service provider to ensure database is created
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var scopedServices = scope.ServiceProvider;
            var db = scopedServices.GetRequiredService<AppDbContext>();

            // Ensure the database is created (no migrations for in-memory)
            db.Database.EnsureCreated();
        });

        return base.CreateHost(builder);
    }
}
