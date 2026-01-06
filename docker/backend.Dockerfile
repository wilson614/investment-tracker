# =============================================================================
# Investment Tracker Backend - Multi-stage Dockerfile
# =============================================================================

# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

# Copy solution and project files first for layer caching
COPY InvestmentTracker.sln .
COPY src/InvestmentTracker.Domain/InvestmentTracker.Domain.csproj src/InvestmentTracker.Domain/
COPY src/InvestmentTracker.Application/InvestmentTracker.Application.csproj src/InvestmentTracker.Application/
COPY src/InvestmentTracker.Infrastructure/InvestmentTracker.Infrastructure.csproj src/InvestmentTracker.Infrastructure/
COPY src/InvestmentTracker.API/InvestmentTracker.API.csproj src/InvestmentTracker.API/

# Restore dependencies
RUN dotnet restore

# Copy remaining source code
COPY src/ src/

# Build and publish
RUN dotnet publish src/InvestmentTracker.API/InvestmentTracker.API.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
WORKDIR /app

# Install curl for health checks
RUN apk add --no-cache curl

# Create non-root user for security
RUN addgroup -S appgroup && adduser -S appuser -G appgroup
USER appuser

# Copy published app
COPY --from=build /app/publish .

# Expose port
EXPOSE 5000

# Set environment variables
ENV ASPNETCORE_URLS=http://+:5000
ENV DOTNET_RUNNING_IN_CONTAINER=true

# Entry point
ENTRYPOINT ["dotnet", "InvestmentTracker.API.dll"]
