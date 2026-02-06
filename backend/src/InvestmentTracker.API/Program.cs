using System.Text;
using FluentValidation;
using InvestmentTracker.API.Middleware;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Application.Services;
using InvestmentTracker.Application.UseCases.CurrencyLedger;
using InvestmentTracker.Application.UseCases.CurrencyTransactions;
using InvestmentTracker.Application.UseCases.Portfolio;
using InvestmentTracker.Application.UseCases.Benchmark;
using InvestmentTracker.Application.UseCases.UserPreferences;
using InvestmentTracker.Application.UseCases.StockSplits;
using InvestmentTracker.Application.UseCases.StockTransactions;
using InvestmentTracker.Application.UseCases.Assets;
using InvestmentTracker.Application.UseCases.BankAccount;
using InvestmentTracker.Application.UseCases.FundAllocation;
using InvestmentTracker.Application.Validators;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;
using InvestmentTracker.Infrastructure.External;
using InvestmentTracker.Infrastructure.MarketData;
using InvestmentTracker.Infrastructure.Persistence;
using InvestmentTracker.Infrastructure.Repositories;
using InvestmentTracker.Infrastructure.Services;
using InvestmentTracker.Infrastructure.StockPrices;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Npgsql;
using Serilog;
using Serilog.Events;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "InvestmentTracker")
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .WriteTo.File(
        path: "logs/app-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Starting Investment Tracker API");

    var builder = WebApplication.CreateBuilder(args);

    // Use Serilog
    builder.Host.UseSerilog();

// Add controllers
builder.Services.AddControllers();

// Add Memory Cache
builder.Services.AddMemoryCache();

// Configure Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Investment Tracker API",
        Version = "v1",
        Description = "API for tracking investment portfolios"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// Configure Database (SQLite for development, PostgreSQL for production)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var useSqlite = builder.Configuration.GetValue("UseSqlite", false);

if (useSqlite || string.IsNullOrEmpty(connectionString))
{
    var dbPath = Path.Combine(Directory.GetCurrentDirectory(), "investment_tracker.db");
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseSqlite($"Data Source={dbPath}"));
}
else
{
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(connectionString));
}

// Configure JWT Authentication
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret is required (must be provided via configuration or environment variables)");

if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Length < 32)
    throw new InvalidOperationException("Jwt:Secret must be at least 32 characters");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "InvestmentTracker";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "InvestmentTracker";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
        };
    });

builder.Services.AddAuthorization();

// Register HttpContextAccessor
builder.Services.AddHttpContextAccessor();

// Register Infrastructure services
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IAppDbTransactionManager, AppDbTransactionManager>();

// Register Repositories
builder.Services.AddScoped<IPortfolioRepository, PortfolioRepository>();
builder.Services.AddScoped<IStockTransactionRepository, StockTransactionRepository>();
builder.Services.AddScoped<ICurrencyLedgerRepository, CurrencyLedgerRepository>();
builder.Services.AddScoped<ICurrencyTransactionRepository, CurrencyTransactionRepository>();
builder.Services.AddScoped<IBankAccountRepository, BankAccountRepository>();
builder.Services.AddScoped<IStockSplitRepository, StockSplitRepository>();
builder.Services.AddScoped<IUserBenchmarkRepository, UserBenchmarkRepository>();
builder.Services.AddScoped<IUserPreferencesRepository, UserPreferencesRepository>();
builder.Services.AddScoped<IEuronextSymbolMappingRepository, EuronextSymbolMappingRepository>();
builder.Services.AddScoped<IEtfClassificationRepository, EtfClassificationRepository>();
builder.Services.AddScoped<IHistoricalYearEndDataRepository, HistoricalYearEndDataRepository>();
builder.Services.AddScoped<IHistoricalExchangeRateCacheRepository, HistoricalExchangeRateCacheRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddScoped<IBenchmarkAnnualReturnRepository, BenchmarkAnnualReturnRepository>();
builder.Services.AddScoped<IFundAllocationRepository, FundAllocationRepository>();

// Register External API Clients
builder.Services.AddHttpClient<IEuronextApiClient, EuronextApiClient>();

// Register Domain Services
builder.Services.AddScoped<PortfolioCalculator>();
builder.Services.AddScoped<CurrencyLedgerService>();
builder.Services.AddScoped<StockSplitAdjustmentService>();
builder.Services.AddScoped<IReturnCalculator, ReturnCalculator>();
builder.Services.AddScoped<StockTransactionCashFlowStrategy>();
builder.Services.AddScoped<CurrencyLedgerCashFlowStrategy>();
builder.Services.AddScoped<ReturnCashFlowStrategyProvider>();
builder.Services.AddScoped<InterestEstimationService>();
builder.Services.AddScoped<TotalAssetsService>();

// Register Use Cases
builder.Services.AddScoped<CreateStockTransactionUseCase>();
builder.Services.AddScoped<UpdateStockTransactionUseCase>();
builder.Services.AddScoped<DeleteStockTransactionUseCase>();
builder.Services.AddScoped<GetPortfoliosUseCase>();
builder.Services.AddScoped<GetPortfolioSummaryUseCase>();
builder.Services.AddScoped<CreatePortfolioUseCase>();
builder.Services.AddScoped<UpdatePortfolioUseCase>();
builder.Services.AddScoped<DeletePortfolioUseCase>();
builder.Services.AddScoped<CalculateXirrUseCase>();
builder.Services.AddScoped<GetCurrencyLedgerSummaryUseCase>();
builder.Services.AddScoped<CreateCurrencyLedgerUseCase>();
builder.Services.AddScoped<UpdateCurrencyLedgerUseCase>();
builder.Services.AddScoped<DeleteCurrencyLedgerUseCase>();
builder.Services.AddScoped<CreateCurrencyTransactionUseCase>();
builder.Services.AddScoped<UpdateCurrencyTransactionUseCase>();
builder.Services.AddScoped<DeleteCurrencyTransactionUseCase>();
builder.Services.AddScoped<GetStockSplitsUseCase>();
builder.Services.AddScoped<CreateStockSplitUseCase>();
builder.Services.AddScoped<UpdateStockSplitUseCase>();
builder.Services.AddScoped<DeleteStockSplitUseCase>();
builder.Services.AddScoped<GetUserBenchmarksUseCase>();
builder.Services.AddScoped<AddUserBenchmarkUseCase>();
builder.Services.AddScoped<DeleteUserBenchmarkUseCase>();
builder.Services.AddScoped<GetUserPreferencesUseCase>();
builder.Services.AddScoped<UpdateUserPreferencesUseCase>();
builder.Services.AddScoped<CreateBankAccountUseCase>();
builder.Services.AddScoped<UpdateBankAccountUseCase>();
builder.Services.AddScoped<DeleteBankAccountUseCase>();
builder.Services.AddScoped<GetBankAccountsUseCase>();
builder.Services.AddScoped<GetBankAccountUseCase>();
builder.Services.AddScoped<GetFundAllocationsUseCase>();
builder.Services.AddScoped<CreateFundAllocationUseCase>();
builder.Services.AddScoped<UpdateFundAllocationUseCase>();
builder.Services.AddScoped<DeleteFundAllocationUseCase>();
builder.Services.AddScoped<GetTotalAssetsSummaryUseCase>();

// Stock Price Service
builder.Services.AddSingleton<ITwseRateLimiter, TwseRateLimiter>(); // Singleton to share rate limit across all requests
builder.Services.AddHttpClient<IStockPriceProvider, SinaStockPriceProvider>();
builder.Services.AddHttpClient<TwseStockPriceProvider>();
builder.Services.AddHttpClient<IExchangeRateProvider, SinaExchangeRateProvider>();
builder.Services.AddScoped<IStockPriceProvider, SinaStockPriceProvider>();
builder.Services.AddScoped<IStockPriceProvider, TwseStockPriceProvider>();
builder.Services.AddScoped<IExchangeRateProvider, SinaExchangeRateProvider>();
builder.Services.AddScoped<IStockPriceService, StockPriceService>();

// Index Price Service (for CAPE real-time adjustment)
builder.Services.AddHttpClient<ISinaEtfPriceService, SinaEtfPriceService>();
builder.Services.AddHttpClient<IStooqHistoricalPriceService, StooqHistoricalPriceService>();
builder.Services.AddHttpClient<ITwseIndexPriceService, TwseIndexPriceService>();
builder.Services.AddHttpClient<ITwseStockHistoricalPriceService, TwseStockHistoricalPriceService>();
builder.Services.AddHttpClient<IYahooHistoricalPriceService, YahooHistoricalPriceService>();
builder.Services.AddScoped<IIndexPriceService, IndexPriceService>();

// CAPE Data Service
builder.Services.AddHttpClient<ICapeDataService, CapeDataService>();

// TWSE Dividend Service (for YTD dividend adjustment)
builder.Services.AddHttpClient<ITwseDividendService, TwseDividendService>();

// Market YTD Service (needs HttpClient for TWSE 0050 historical data)
builder.Services.AddHttpClient<IMarketYtdService, MarketYtdService>();

// Euronext Quote Service
builder.Services.AddScoped<EuronextQuoteService>();

// Historical Performance Service
builder.Services.AddScoped<IHistoricalPerformanceService, HistoricalPerformanceService>();

// ETF Classification Service
builder.Services.AddSingleton<EtfClassificationService>();

// Historical Year-End Data Service (cache for year-end prices/rates)
builder.Services.AddScoped<IHistoricalYearEndDataService, HistoricalYearEndDataService>();

// Transaction-Date Exchange Rate Service (cache for transaction-date FX rates)
builder.Services.AddScoped<ITransactionDateExchangeRateService, TransactionDateExchangeRateService>();

// Monthly Snapshot Service (monthly net worth history)
builder.Services.AddScoped<IMonthlySnapshotService, MonthlySnapshotService>();

// Transaction Portfolio Snapshot Service (per-event before/after snapshots)
builder.Services.AddScoped<ITransactionPortfolioSnapshotService, TransactionPortfolioSnapshotService>();

// Register FluentValidation validators
builder.Services.AddValidatorsFromAssemblyContaining<CreatePortfolioRequestValidator>();

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(
                builder.Configuration["Cors:Origins"] ?? "http://localhost:5173")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline
app.UseExceptionHandling();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowFrontend");

app.UseAuthentication();
app.UseTenantContext();
app.UseAuthorization();

app.MapControllers();

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
    .WithName("HealthCheck")
    .WithOpenApi();

// Auto-migrate database on startup (skip for Testing environment)
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    try
    {
        Console.WriteLine("Checking for pending database migrations...");
        var context = services.GetRequiredService<AppDbContext>();

        var pendingMigrations = context.Database.GetPendingMigrations().ToList();
        if (pendingMigrations.Count != 0)
        {
            Console.WriteLine($"Found {pendingMigrations.Count} pending migration(s): {string.Join(", ", pendingMigrations)}");
            Console.WriteLine("Applying database migrations...");
            context.Database.Migrate();
            Console.WriteLine("Database migrations applied successfully.");
        }
        else
        {
            Console.WriteLine("No pending migrations found.");
        }
    }
    catch (PostgresException ex) when (ex.SqlState == "42P07")
    {
        // Table already exists - this happens when database was created with EnsureCreated()
        // but no migration history exists. We need to mark migrations as applied.
        Console.WriteLine("Database tables already exist (likely from EnsureCreated). Attempting to sync migration history...");

        var context = services.GetRequiredService<AppDbContext>();
        try
        {
            // Ensure __EFMigrationsHistory table exists
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""__EFMigrationsHistory"" (
                    ""MigrationId"" character varying(150) NOT NULL,
                    ""ProductVersion"" character varying(32) NOT NULL,
                    CONSTRAINT ""PK___EFMigrationsHistory"" PRIMARY KEY (""MigrationId"")
                )");

            // Get all migrations and mark them as applied
            var allMigrations = context.Database.GetMigrations();
            foreach (var migration in allMigrations)
            {
                // Migration names are safe (from EF Core internals), suppress SQL injection warning
#pragma warning disable EF1002
                await context.Database.ExecuteSqlRawAsync($@"
                    INSERT INTO ""__EFMigrationsHistory"" (""MigrationId"", ""ProductVersion"")
                    VALUES ('{migration}', '8.0.0')
                    ON CONFLICT DO NOTHING");
#pragma warning restore EF1002
            }
            Console.WriteLine("Migration history synchronized. Application will continue.");
        }
        catch (Exception syncEx)
        {
            Console.WriteLine($"Failed to sync migration history: {syncEx.Message}");
            throw;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"An error occurred while migrating the database: {ex.Message}");
        throw; // Re-throw to prevent app from starting with broken database
    }
} // End of Testing environment check

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Required for WebApplicationFactory in integration tests
public partial class Program { }
