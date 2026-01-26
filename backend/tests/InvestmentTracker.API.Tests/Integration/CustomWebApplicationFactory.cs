using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
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

            // Capture TestUserId at this point
            var userId = _testUserId;
            services.AddScoped<ICurrentUserService>(_ =>
            {
                var mock = new Mock<ICurrentUserService>();
                mock.Setup(x => x.UserId).Returns(userId);
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
