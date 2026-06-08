using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ChargesApi.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ChargesApi.Tests;

public class ChargesApiTests
{
    [Fact]
    public async Task CreateChargeReturns201ForAFreshKey()
    {
        // Arrange
        using var webApplicationFactory = new WebApplicationFactory<Program>();
        using var httpClient = webApplicationFactory.CreateClient();
        var chargeRequest = ChargeRequestBuilder.Valid().Build();

        // Act
        var chargeCreationResponse = await httpClient.PostAsJsonAsync("/charges", chargeRequest);

        // Assert
        Assert.Equal(HttpStatusCode.Created, chargeCreationResponse.StatusCode);
        var chargeCreationResponseBody = await chargeCreationResponse.Content.ReadFromJsonAsync<ChargeResponse>();
        Assert.NotNull(chargeCreationResponseBody);
        Assert.StartsWith("ch_", chargeCreationResponseBody.Id);
    }

    [Theory]
    [MemberData(nameof(InvalidChargeRequests))]
    public async Task InvalidChargeRequestReturns400(ChargeRequest invalidChargeRequest, string expectedErrorMessage)
    {
        // Arrange
        using var webApplicationFactory = new WebApplicationFactory<Program>();
        using var httpClient = webApplicationFactory.CreateClient();

        // Act
        var invalidChargeResponse = await httpClient.PostAsJsonAsync("/charges", invalidChargeRequest);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, invalidChargeResponse.StatusCode);
        var errorResponse = await invalidChargeResponse.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(errorResponse);
        Assert.Equal(expectedErrorMessage, errorResponse.Error);
    }

    [Fact]
    public async Task ParallelRequestsWithSameIdempotencyKeyReturnSameCharge()
    {
        // Arrange
        using var webApplicationFactory = new WebApplicationFactory<Program>();
        using var httpClient = webApplicationFactory.CreateClient();
        var idempotencyKey = $"test_parallel_key_{Guid.NewGuid():N}";
        var chargeRequest = ChargeRequestBuilder.Valid()
            .WithIdempotencyKey(idempotencyKey)
            .WithAmount(42.00m)
            .WithCustomerEmail("parallel@example.com")
            .Build();

        // Act
        var chargeCreationTasks = Enumerable.Range(0, 12)
            .Select(chargeCreationAttempt => httpClient.PostAsJsonAsync("/charges", chargeRequest))
            .ToArray();
        var chargeResponses = await Task.WhenAll(chargeCreationTasks);

        // Assert
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
    public async Task SameIdempotencyKeyWithDifferentRequestReturns409()
    {
        // Arrange
        using var webApplicationFactory = new WebApplicationFactory<Program>();
        using var httpClient = webApplicationFactory.CreateClient();
        var idempotencyKey = $"test_conflict_key_{Guid.NewGuid():N}";
        var firstChargeRequest = ChargeRequestBuilder.Valid()
            .WithIdempotencyKey(idempotencyKey)
            .WithAmount(15.00m)
            .Build();
        var conflictingChargeRequest = ChargeRequestBuilder.Valid()
            .WithIdempotencyKey(idempotencyKey)
            .WithAmount(16.00m)
            .Build();

        // Act
        var firstChargeResponse = await httpClient.PostAsJsonAsync("/charges", firstChargeRequest);
        var conflictingChargeResponse = await httpClient.PostAsJsonAsync("/charges", conflictingChargeRequest);

        // Assert
        Assert.Equal(HttpStatusCode.Created, firstChargeResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflictingChargeResponse.StatusCode);
        var errorResponse = await conflictingChargeResponse.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(errorResponse);
        Assert.Equal("idempotencyKey was already used with different request data", errorResponse.Error);
    }

    [Fact]
    public async Task CustomerSearchTreatsEmailQueryAsPlainText()
    {
        // Arrange
        using var webApplicationFactory = new WebApplicationFactory<Program>();
        using var httpClient = webApplicationFactory.CreateClient();
        var customerEmail = $"search_{Guid.NewGuid():N}@example.com";
        var chargeRequest = ChargeRequestBuilder.Valid()
            .WithCustomerEmail(customerEmail)
            .Build();
        var injectedCustomerEmail = $"{customerEmail}' OR '1'='1";
        var originalConsoleOutput = Console.Out;
        using var capturedConsoleOutput = new StringWriter();

        // Act
        var chargeCreationResponse = await httpClient.PostAsJsonAsync("/charges", chargeRequest);
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

        // Assert
        Assert.Equal(HttpStatusCode.Created, chargeCreationResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
        var searchResponseBody = await searchResponse.Content.ReadFromJsonAsync<List<ChargeResponse>>();
        Assert.NotNull(searchResponseBody);
        Assert.Empty(searchResponseBody);
        Assert.DoesNotContain("SELECT", capturedConsoleOutput.ToString());
    }

    [Fact]
    public async Task CustomerSearchMatchesEmailCaseInsensitive()
    {
        // Arrange
        using var webApplicationFactory = new WebApplicationFactory<Program>();
        using var httpClient = webApplicationFactory.CreateClient();
        var customerEmail = $"CaseSearch_{Guid.NewGuid():N}@Example.com";
        var searchEmail = customerEmail.ToLowerInvariant();
        var chargeRequest = ChargeRequestBuilder.Valid()
            .WithCustomerEmail(customerEmail)
            .Build();

        // Act
        var chargeCreationResponse = await httpClient.PostAsJsonAsync("/charges", chargeRequest);
        var searchResponse = await httpClient.GetAsync($"/customers/search?email={Uri.EscapeDataString(searchEmail)}");

        // Assert
        Assert.Equal(HttpStatusCode.Created, chargeCreationResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
        var searchResponseBody = await searchResponse.Content.ReadFromJsonAsync<List<ChargeResponse>>();
        Assert.NotNull(searchResponseBody);
        Assert.Single(searchResponseBody);
        Assert.Equal(customerEmail, searchResponseBody[0].CustomerEmail);
    }

    public static IEnumerable<object[]> InvalidChargeRequests()
    {
        yield return new object[]
        {
            ChargeRequestBuilder.Valid().WithIdempotencyKey("").Build(),
            "idempotencyKey is required"
        };
        yield return new object[]
        {
            ChargeRequestBuilder.Valid().WithAmount(0).Build(),
            "amount must be greater than zero"
        };
        yield return new object[]
        {
            ChargeRequestBuilder.Valid().WithCurrency("").Build(),
            "currency is required"
        };
        yield return new object[]
        {
            ChargeRequestBuilder.Valid().WithCustomerEmail("").Build(),
            "customerEmail is required"
        };
        yield return new object[]
        {
            ChargeRequestBuilder.Valid().WithCardToken("").Build(),
            "cardToken is required"
        };
    }

    private sealed class ChargeRequestBuilder
    {
        private string _idempotencyKey = $"test_key_{Guid.NewGuid():N}";
        private decimal _amount = 12.50m;
        private string _currency = "USD";
        private string _customerEmail = "happy@example.com";
        private string _cardToken = "tok_visa";

        public static ChargeRequestBuilder Valid() => new();

        public ChargeRequestBuilder WithIdempotencyKey(string idempotencyKey)
        {
            _idempotencyKey = idempotencyKey;
            return this;
        }

        public ChargeRequestBuilder WithAmount(decimal amount)
        {
            _amount = amount;
            return this;
        }

        public ChargeRequestBuilder WithCurrency(string currency)
        {
            _currency = currency;
            return this;
        }

        public ChargeRequestBuilder WithCustomerEmail(string customerEmail)
        {
            _customerEmail = customerEmail;
            return this;
        }

        public ChargeRequestBuilder WithCardToken(string cardToken)
        {
            _cardToken = cardToken;
            return this;
        }

        public ChargeRequest Build()
        {
            return new ChargeRequest(
                _idempotencyKey,
                _amount,
                _currency,
                _customerEmail,
                _cardToken
            );
        }
    }

    private sealed record ChargeResponse(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("customerEmail")] string CustomerEmail
    );

    private sealed record ErrorResponse([property: JsonPropertyName("error")] string Error);
}
