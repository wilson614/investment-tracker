using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Infrastructure.Repositories;
using InvestmentTracker.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace InvestmentTracker.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddTwseSymbolMappingServices(this IServiceCollection services)
    {
        services.AddScoped<ITwSecurityMappingRepository, TwSecurityMappingRepository>();
        services.AddHttpClient<TwseSymbolMappingService>();
        services.AddScoped<ITwseSymbolMappingSyncService, TwseSymbolMappingSyncServiceAdapter>();

        return services;
    }
}
