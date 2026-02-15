using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using InvestmentTracker.API.Tests.Integration;

namespace InvestmentTracker.API.Tests.Controllers;

/// <summary>
/// Contract tests for TWSE on-demand symbol sync endpoint.
/// Includes per-unresolved-row sync-attempt assertions with non-fragile baseline checks.
/// </summary>
public class MarketDataControllerTwseSyncTests(CustomWebApplicationFactory factory)
    : IntegrationTestBase(factory)
{
    private const string Endpoint = "/api/market-data/twse/symbol-mappings/sync-on-demand";

    [Fact]
    public async Task SyncOnDemand_ReturnsBadRequest_WhenSecurityNamesIsEmpty()
    {
        // Arrange
        var request = new { SecurityNames = Array.Empty<string>() };

        // Act
        var response = await Client.PostAsJsonAsync(Endpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SyncOnDemand_ReturnsContract_AndPerUnresolvedRowSyncAttemptAssertions()
    {
        // Arrange
        // Use likely-unresolved names to keep assertions robust across external source variability.
        var unresolvedNameA = $"未知公司-{Guid.NewGuid():N}";
        var unresolvedNameB = $"不存在標的-{Guid.NewGuid():N}";

        var requestNames = new[]
        {
            unresolvedNameA,
            unresolvedNameB
        };

        var request = new { SecurityNames = requestNames };

        // Act
        var response = await Client.PostAsJsonAsync(Endpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(payload);
        var root = json.RootElement;

        root.TryGetProperty("requested", out var requestedElement).Should().BeTrue();
        root.TryGetProperty("resolved", out var resolvedElement).Should().BeTrue();
        root.TryGetProperty("unresolved", out var unresolvedElement).Should().BeTrue();
        root.TryGetProperty("mappings", out var mappingsElement).Should().BeTrue();
        root.TryGetProperty("errors", out var errorsElement).Should().BeTrue();

        requestedElement.ValueKind.Should().Be(JsonValueKind.Number);
        resolvedElement.ValueKind.Should().Be(JsonValueKind.Number);
        unresolvedElement.ValueKind.Should().Be(JsonValueKind.Number);
        mappingsElement.ValueKind.Should().Be(JsonValueKind.Array);
        errorsElement.ValueKind.Should().Be(JsonValueKind.Array);

        var requested = requestedElement.GetInt32();
        var resolved = resolvedElement.GetInt32();
        var unresolved = unresolvedElement.GetInt32();

        requested.Should().Be(2, "each unresolved input row should be attempted");
        resolved.Should().Be(0, "random unresolved names should not be resolved");
        unresolved.Should().Be(2, "all unresolved rows should stay unresolved");
        (resolved + unresolved).Should().Be(requested,
            "every requested unresolved row should result in exactly one resolved/unresolved outcome");

        var mappedNames = mappingsElement
            .EnumerateArray()
            .Select(x => x.GetProperty("securityName").GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .ToHashSet(StringComparer.Ordinal);

        var erroredNames = errorsElement
            .EnumerateArray()
            .Select(x => x.GetProperty("securityName").GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .ToHashSet(StringComparer.Ordinal);

        // Per-unresolved-row sync-attempt assertion:
        // each requested unresolved row appears exactly once in either mappings or errors.
        foreach (var requestedName in requestNames)
        {
            var appearsInMapping = mappedNames.Contains(requestedName);
            var appearsInError = erroredNames.Contains(requestedName);

            (appearsInMapping || appearsInError).Should().BeTrue(
                $"{requestedName} should have a sync attempt result");

            (appearsInMapping && appearsInError).Should().BeFalse(
                $"{requestedName} should not be both resolved and unresolved simultaneously");
        }

        errorsElement.GetArrayLength().Should().Be(unresolved,
            "unresolved rows should have one row-level error each");

        foreach (var mapping in mappingsElement.EnumerateArray())
        {
            mapping.TryGetProperty("securityName", out var securityName).Should().BeTrue();
            securityName.ValueKind.Should().Be(JsonValueKind.String);
            securityName.GetString().Should().NotBeNullOrWhiteSpace();

            mapping.TryGetProperty("ticker", out var ticker).Should().BeTrue();
            ticker.ValueKind.Should().Be(JsonValueKind.String);
            ticker.GetString().Should().NotBeNullOrWhiteSpace();

            mapping.TryGetProperty("isin", out var isin).Should().BeTrue();
            isin.ValueKind.Should().Be(JsonValueKind.String);
            isin.GetString().Should().NotBeNullOrWhiteSpace();

            mapping.TryGetProperty("market", out var market).Should().BeTrue();
            market.ValueKind.Should().BeOneOf(JsonValueKind.String, JsonValueKind.Null);
        }

        foreach (var error in errorsElement.EnumerateArray())
        {
            error.TryGetProperty("securityName", out var securityName).Should().BeTrue();
            securityName.ValueKind.Should().Be(JsonValueKind.String);
            securityName.GetString().Should().NotBeNullOrWhiteSpace();

            error.TryGetProperty("errorCode", out var errorCode).Should().BeTrue();
            errorCode.ValueKind.Should().Be(JsonValueKind.String);
            errorCode.GetString().Should().NotBeNullOrWhiteSpace();

            error.TryGetProperty("message", out var message).Should().BeTrue();
            message.ValueKind.Should().Be(JsonValueKind.String);
            message.GetString().Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task SyncOnDemand_NormalizesDistinctInputNames_ForRequestedCount_AndProducesOneOutcomePerCanonicalName()
    {
        // Arrange
        var uniqueName = $"未匹配標的-{Guid.NewGuid():N}";
        var canonicalNames = new[] { "台積電", uniqueName };

        var request = new
        {
            SecurityNames = new[]
            {
                "  台積電  ",
                "台積電",
                uniqueName,
                "",
                "   "
            }
        };

        // Act
        var response = await Client.PostAsJsonAsync(Endpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(payload);

        var requested = json.RootElement.GetProperty("requested").GetInt32();
        var resolved = json.RootElement.GetProperty("resolved").GetInt32();
        var unresolved = json.RootElement.GetProperty("unresolved").GetInt32();

        requested.Should().Be(2,
            "service should normalize/trim and de-duplicate names, and ignore blank names");
        (resolved + unresolved).Should().Be(requested,
            "each canonical input name should produce one resolved/unresolved outcome");

        var mappings = json.RootElement.GetProperty("mappings");
        var errors = json.RootElement.GetProperty("errors");

        var mappedNames = mappings
            .EnumerateArray()
            .Select(x => x.GetProperty("securityName").GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .ToHashSet(StringComparer.Ordinal);

        var erroredNames = errors
            .EnumerateArray()
            .Select(x => x.GetProperty("securityName").GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var canonicalName in canonicalNames)
        {
            var appearsInMapping = mappedNames.Contains(canonicalName);
            var appearsInError = erroredNames.Contains(canonicalName);

            (appearsInMapping || appearsInError).Should().BeTrue(
                $"{canonicalName} should appear in exactly one sync outcome collection");
            (appearsInMapping && appearsInError).Should().BeFalse(
                $"{canonicalName} should not appear in both mappings and errors");
        }
    }
}
