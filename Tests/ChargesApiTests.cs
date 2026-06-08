using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ChargesApi.Tests;

public class ChargesApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ChargesApiTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task CreateChargeReturns201ForAFreshKey()
    {
        var httpClient = _factory.CreateClient();
        var idempotencyKey = $"test_fresh_key_{Guid.NewGuid():N}";
        var chargeCreationResponse = await httpClient.PostAsJsonAsync("/charges", new {
            idempotencyKey,
            amount = 12.50m,
            currency = "USD",
            customerEmail = "happy@example.com",
            cardToken = "tok_visa"
        });

        Assert.Equal(HttpStatusCode.Created, chargeCreationResponse.StatusCode);
        var chargeCreationResponseBody = await chargeCreationResponse.Content.ReadAsStringAsync();
        Assert.Contains("\"id\":\"ch_", chargeCreationResponseBody);
    }

    [Fact]
    public async Task MissingIdempotencyKeyReturns400()
    {
        var httpClient = _factory.CreateClient();
        var missingIdempotencyKeyResponse = await httpClient.PostAsJsonAsync("/charges", new {
            idempotencyKey = "",
            amount = 1.00m,
            currency = "USD",
            customerEmail = "x@y.com",
            cardToken = "tok_visa"
        });

        Assert.Equal(HttpStatusCode.BadRequest, missingIdempotencyKeyResponse.StatusCode);
    }

    [Fact]
    public async Task ParallelRequestsWithSameIdempotencyKeyReturnSameCharge()
    {
        var httpClient = _factory.CreateClient();
        var idempotencyKey = $"test_parallel_key_{Guid.NewGuid():N}";
        var chargeRequest = new {
            idempotencyKey,
            amount = 42.00m,
            currency = "USD",
            customerEmail = "parallel@example.com",
            cardToken = "tok_visa"
        };

        var chargeCreationTasks = Enumerable.Range(0, 12)
            .Select(chargeCreationAttempt => httpClient.PostAsJsonAsync("/charges", chargeRequest))
            .ToArray();

        var chargeResponses = await Task.WhenAll(chargeCreationTasks);
        var chargeIds = new List<string>();

        foreach (var chargeResponse in chargeResponses)
        {
            Assert.Equal(HttpStatusCode.Created, chargeResponse.StatusCode);
            var chargeResponseBody = await chargeResponse.Content.ReadFromJsonAsync<ChargeResponse>();
            Assert.NotNull(chargeResponseBody);
            chargeIds.Add(chargeResponseBody.Id);
        }

        Assert.Single(chargeIds.Distinct());
    }

    [Fact]
    public async Task CustomerSearchTreatsEmailQueryAsPlainText()
    {
        var httpClient = _factory.CreateClient();
        var customerEmail = $"search_{Guid.NewGuid():N}@example.com";
        var idempotencyKey = $"test_search_key_{Guid.NewGuid():N}";

        var chargeCreationResponse = await httpClient.PostAsJsonAsync("/charges", new {
            idempotencyKey,
            amount = 15.00m,
            currency = "USD",
            customerEmail,
            cardToken = "tok_visa"
        });

        Assert.Equal(HttpStatusCode.Created, chargeCreationResponse.StatusCode);

        var injectedCustomerEmail = $"{customerEmail}' OR '1'='1";
        var originalConsoleOutput = Console.Out;
        using var capturedConsoleOutput = new StringWriter();

        Console.SetOut(capturedConsoleOutput);
        HttpResponseMessage searchResponse;
        try
        {
            searchResponse = await httpClient.GetAsync($"/customers/search?email={Uri.EscapeDataString(injectedCustomerEmail)}");
        }
        finally
        {
            Console.SetOut(originalConsoleOutput);
        }

        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
        var searchResponseBody = await searchResponse.Content.ReadFromJsonAsync<List<ChargeResponse>>();
        Assert.NotNull(searchResponseBody);
        Assert.Empty(searchResponseBody);
        Assert.DoesNotContain("SELECT", capturedConsoleOutput.ToString());
    }

    private sealed record ChargeResponse([property: JsonPropertyName("id")] string Id);
}
