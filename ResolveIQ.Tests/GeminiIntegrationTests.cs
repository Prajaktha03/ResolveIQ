using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ResolveIQ.Api.Controllers;
using ResolveIQ.Api.DTOs;
using ResolveIQ.Api.Models;
using ResolveIQ.Api.Services;
using Xunit;

namespace ResolveIQ.Tests;

public class GeminiIntegrationTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        public HttpStatusCode StatusCodeToReturn { get; set; } = HttpStatusCode.OK;
        public string ContentToReturn { get; set; } = string.Empty;
        public bool SimulateTimeout { get; set; }
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }
        public int SendCount { get; private set; }

        public Queue<HttpResponseMessage> ResponseQueue { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SendCount++;
            LastRequest = request;
            if (request.Content != null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            if (SimulateTimeout)
            {
                await Task.Delay(100, cancellationToken);
                throw new OperationCanceledException(cancellationToken);
            }

            if (ResponseQueue.Count > 0)
            {
                return ResponseQueue.Dequeue();
            }

            var response = new HttpResponseMessage(StatusCodeToReturn)
            {
                Content = new StringContent(ContentToReturn, Encoding.UTF8, "application/json")
            };
            return response;
        }
    }

    private class MockHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public MockHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler);
    }

    private static IConfiguration CreateGeminiConfig(
        bool enabled = true,
        string apiKey = "test-gemini-key-12345",
        string model = "gemini-1.5-flash",
        int timeoutSeconds = 15)
    {
        var dict = new Dictionary<string, string?>
        {
            ["Ai:Enabled"] = enabled ? "true" : "false",
            ["Ai:Provider"] = "Gemini",
            ["Ai:TimeoutSeconds"] = timeoutSeconds.ToString(),
            ["Ai:Gemini:ApiKey"] = apiKey,
            ["Ai:Gemini:Model"] = model,
            ["Ai:Gemini:Endpoint"] = "https://generativelanguage.googleapis.com/v1beta"
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build();
    }

    private static string WrapInGeminiCandidate(string contentText)
    {
        var candidateObj = new
        {
            candidates = new[]
            {
                new
                {
                    content = new
                    {
                        parts = new[]
                        {
                            new { text = contentText }
                        },
                        role = "model"
                    },
                    finishReason = "STOP"
                }
            }
        };

        return JsonSerializer.Serialize(candidateObj);
    }

    // =========================================================================
    // 1. Request Construction & Header Authentication Tests
    // =========================================================================

    [Fact]
    public async Task Gemini_RequestConstruction_UsesXGoogApiKeyHeaderAndEndpointAndSystemInstruction()
    {
        var handler = new MockHttpMessageHandler
        {
            ContentToReturn = WrapInGeminiCandidate("""
                {
                  "category": "Hardware",
                  "subcategory": "Printer",
                  "suggestedPriority": "Medium",
                  "confidenceScore": 0.89,
                  "rationale": "Paper jam on office printer.",
                  "suggestedTags": ["printer", "hardware"]
                }
                """)
        };

        var config = CreateGeminiConfig(apiKey: "AI-SECRET-KEY-99999", model: "gemini-1.5-flash");
        var slaService = new SlaService(TimeProvider.System);
        var aiService = new AiIntelligenceService(
            new MockHttpClientFactory(handler),
            config,
            NullLogger<AiIntelligenceService>.Instance,
            slaService);

        var result = await aiService.ClassifyTicketAsync("Printer is jammed", "Paper stuck in tray 2.");

        Assert.NotNull(result);
        Assert.NotNull(handler.LastRequest);

        // 1. URL must target configured model and generateContent action
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent",
            handler.LastRequest.RequestUri?.ToString());

        // 2. Authentication MUST be passed via x-goog-api-key header, not in query string
        Assert.True(handler.LastRequest.Headers.Contains("x-goog-api-key"));
        var headerVal = handler.LastRequest.Headers.GetValues("x-goog-api-key").FirstOrDefault();
        Assert.Equal("AI-SECRET-KEY-99999", headerVal);
        Assert.Null(handler.LastRequest.Headers.Authorization);
        Assert.False(handler.LastRequest.RequestUri?.Query.Contains("AI-SECRET-KEY-99999"));

        // 3. Payload must contain systemInstruction, responseMimeType="application/json", and sanitized untrusted tags
        Assert.NotNull(handler.LastRequestBody);
        Assert.Contains("systemInstruction", handler.LastRequestBody);
        Assert.Contains("responseMimeType", handler.LastRequestBody);
        Assert.Contains("application/json", handler.LastRequestBody);
        Assert.Contains("<ticket_title>", handler.LastRequestBody);
        Assert.Contains("Printer is jammed", handler.LastRequestBody);
    }

    // =========================================================================
    // 2. Classification & Uncalibrated Confidence Indicators
    // =========================================================================

    [Fact]
    public async Task Gemini_ClassifyTicket_ReturnsValidResult_WithExplicitCalibrationIndicators()
    {
        var handler = new MockHttpMessageHandler
        {
            ContentToReturn = WrapInGeminiCandidate("""
                {
                  "category": "Network",
                  "subcategory": "VPN",
                  "suggestedPriority": "High",
                  "confidenceScore": 0.94,
                  "rationale": "VPN gateway failure blocking remote workforce.",
                  "suggestedTags": ["vpn", "gateway"]
                }
                """)
        };

        var config = CreateGeminiConfig();
        var slaService = new SlaService(TimeProvider.System);
        var aiService = new AiIntelligenceService(
            new MockHttpClientFactory(handler),
            config,
            NullLogger<AiIntelligenceService>.Instance,
            slaService);

        var result = await aiService.ClassifyTicketAsync("VPN down", "Cannot reach company network.");

        Assert.Equal("Network", result.Category);
        Assert.Equal("VPN", result.Subcategory);
        Assert.Equal("High", result.SuggestedPriority);
        Assert.Equal(0.94, result.ConfidenceScore);
        Assert.False(result.IsConfidenceCalibrated);
        Assert.Contains("not statistically calibrated", result.ConfidenceCalibrationNote);
        Assert.Contains("vpn", result.SuggestedTags);
    }

    // =========================================================================
    // 3. Priority Recommendation & SLA Target Mapping
    // =========================================================================

    [Fact]
    public async Task Gemini_RecommendPriority_ReturnsCalculatedSlaTargets_AndUncalibratedIndicator()
    {
        var handler = new MockHttpMessageHandler
        {
            ContentToReturn = WrapInGeminiCandidate("""
                {
                  "recommendedPriority": "Critical",
                  "confidenceScore": 0.98,
                  "rationale": "Complete production database cluster outage.",
                  "uncertaintyOrMissingInfo": null
                }
                """)
        };

        var config = CreateGeminiConfig();
        var slaService = new SlaService(TimeProvider.System);
        var aiService = new AiIntelligenceService(
            new MockHttpClientFactory(handler),
            config,
            NullLogger<AiIntelligenceService>.Instance,
            slaService);

        var result = await aiService.RecommendPriorityAsync(
            "Production DB outage",
            "Customer orders are failing with 500 error",
            currentPriority: "Medium",
            impact: "Organization-wide",
            urgency: "Immediate");

        Assert.Equal("Critical", result.RecommendedPriority);
        Assert.Equal("Medium", result.CurrentPriority);
        Assert.Equal("15 minutes", result.SlaFirstResponseTarget);
        Assert.Equal("4 hours", result.SlaResolutionTarget);
        Assert.False(result.IsConfidenceCalibrated);
        Assert.NotNull(result.ConfidenceCalibrationNote);
    }

    // =========================================================================
    // 4. Troubleshooting Grounded Citations & Hypotheses Rules
    // =========================================================================

    [Fact]
    public async Task Gemini_TroubleshootTicket_ReturnsGroundedSources_AndHypothesesCauses()
    {
        var handler = new MockHttpMessageHandler
        {
            ContentToReturn = WrapInGeminiCandidate("""
                {
                  "summary": "VPN connection drops during authentication handshake.",
                  "hypothesizedCauses": [
                    "Hypothesis 1: Expired client certificate.",
                    "Hypothesis 2: Firewall blocking UDP port 500/4500."
                  ],
                  "troubleshootingSteps": [
                    "1. Check certificate validity in certmgr.msc.",
                    "2. Test outbound UDP connectivity to VPN gateway."
                  ],
                  "clarifyingQuestions": [
                    "Are other users on the same subnet experiencing drops?"
                  ]
                }
                """)
        };

        var config = CreateGeminiConfig();
        var slaService = new SlaService(TimeProvider.System);
        var aiService = new AiIntelligenceService(
            new MockHttpClientFactory(handler),
            config,
            NullLogger<AiIntelligenceService>.Instance,
            slaService);

        var ticket = new Ticket
        {
            Id = 55,
            Title = "VPN disconnects randomly",
            Description = "Every 10 minutes connection drops.",
            Priority = "High",
            Status = "Open"
        };

        var kb = new List<KnowledgeArticleSummaryDto>
        {
            new() { Id = 12, Title = "VPN Troubleshooting Guide", Excerpt = "Renew expired client certificates." }
        };

        var similar = new List<SimilarTicketSummaryDto>
        {
            new() { TicketId = 18, Title = "VPN handshake failure", Excerpt = "Fixed by resetting tunnel adapter." }
        };

        var result = await aiService.TroubleshootTicketAsync(ticket, kb, similar);

        Assert.Equal(55, result.TicketId);
        Assert.Contains("grounded in repository knowledge articles", result.GroundingNotice);
        Assert.Single(result.RelevantKnowledgeArticles);
        Assert.Equal(12, result.RelevantKnowledgeArticles[0].Id);
        Assert.Single(result.SimilarResolvedTickets);
        Assert.Equal(18, result.SimilarResolvedTickets[0].TicketId);
        Assert.Equal(2, result.HypothesizedCauses.Count);
        Assert.StartsWith("Hypothesis", result.HypothesizedCauses[0]);
    }

    // =========================================================================
    // 5. Invalid JSON & Malformed Output Handling
    // =========================================================================

    [Fact]
    public async Task Gemini_InvalidJsonOrMalformedOutput_ThrowsAiProviderException()
    {
        var handler = new MockHttpMessageHandler
        {
            ContentToReturn = WrapInGeminiCandidate("THIS IS NOT JSON CONTENT")
        };

        var config = CreateGeminiConfig();
        var slaService = new SlaService(TimeProvider.System);
        var aiService = new AiIntelligenceService(
            new MockHttpClientFactory(handler),
            config,
            NullLogger<AiIntelligenceService>.Instance,
            slaService);

        var ex = await Assert.ThrowsAsync<AiProviderException>(() =>
            aiService.ClassifyTicketAsync("Test title", "Test description"));

        Assert.Contains("unparseable", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // 6. Safety Policy & Blocked Generation Handling
    // =========================================================================

    [Fact]
    public async Task Gemini_WhenSafetyPolicyBlocksGeneration_ThrowsSafeAiProviderException()
    {
        var blockedJson = JsonSerializer.Serialize(new
        {
            promptFeedback = new
            {
                blockReason = "SAFETY"
            }
        });

        var handler = new MockHttpMessageHandler
        {
            ContentToReturn = blockedJson
        };

        var config = CreateGeminiConfig();
        var slaService = new SlaService(TimeProvider.System);
        var aiService = new AiIntelligenceService(
            new MockHttpClientFactory(handler),
            config,
            NullLogger<AiIntelligenceService>.Instance,
            slaService);

        var ex = await Assert.ThrowsAsync<AiProviderException>(() =>
            aiService.ClassifyTicketAsync("Malicious prompt test", "Some trigger"));

        Assert.Contains("blocked", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // 7. Missing Configuration Behavior
    // =========================================================================

    [Fact]
    public async Task Gemini_MissingApiKeyOrDisabled_ThrowsAiServiceUnavailableException()
    {
        var handler = new MockHttpMessageHandler();
        var config = CreateGeminiConfig(enabled: false, apiKey: "");
        var slaService = new SlaService(TimeProvider.System);
        var aiService = new AiIntelligenceService(
            new MockHttpClientFactory(handler),
            config,
            NullLogger<AiIntelligenceService>.Instance,
            slaService);

        Assert.False(aiService.IsConfigured);

        var ex = await Assert.ThrowsAsync<AiServiceUnavailableException>(() =>
            aiService.ClassifyTicketAsync("Title", "Description"));

        Assert.Contains("unconfigured or disabled", ex.Message);
        Assert.Equal(0, handler.SendCount); // Zero network calls made!
    }

    // =========================================================================
    // 8. HTTP Errors (401, 403, 429, 500, 503) Handled Safely
    // =========================================================================

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "authentication failed")]
    [InlineData(HttpStatusCode.Forbidden, "authentication failed")]
    [InlineData(HttpStatusCode.TooManyRequests, "rate limit or quota exceeded")]
    [InlineData(HttpStatusCode.InternalServerError, "HTTP status 500")]
    public async Task Gemini_HttpErrors_ThrowSafeAiProviderException(HttpStatusCode status, string expectedSnippet)
    {
        var handler = new MockHttpMessageHandler
        {
            StatusCodeToReturn = status,
            ContentToReturn = """{"error": {"code": 401, "message": "API key not valid."}}"""
        };

        var config = CreateGeminiConfig();
        var slaService = new SlaService(TimeProvider.System);
        var aiService = new AiIntelligenceService(
            new MockHttpClientFactory(handler),
            config,
            NullLogger<AiIntelligenceService>.Instance,
            slaService);

        var ex = await Assert.ThrowsAsync<AiProviderException>(() =>
            aiService.ClassifyTicketAsync("Title", "Description"));

        Assert.Contains(expectedSnippet, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // 9. Timeout and Cancellation Behavior
    // =========================================================================

    [Fact]
    public async Task Gemini_Timeout_ThrowsTimeoutException()
    {
        var handler = new MockHttpMessageHandler
        {
            SimulateTimeout = true
        };

        var config = CreateGeminiConfig(timeoutSeconds: 1);
        var slaService = new SlaService(TimeProvider.System);
        var aiService = new AiIntelligenceService(
            new MockHttpClientFactory(handler),
            config,
            NullLogger<AiIntelligenceService>.Instance,
            slaService);

        var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
            aiService.ClassifyTicketAsync("Title", "Description", CancellationToken.None));

        Assert.Contains("timed out", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Gemini_CallerCancellation_ThrowsOperationCanceledException()
    {
        var handler = new MockHttpMessageHandler
        {
            SimulateTimeout = true
        };

        var config = CreateGeminiConfig(timeoutSeconds: 15);
        var slaService = new SlaService(TimeProvider.System);
        var aiService = new AiIntelligenceService(
            new MockHttpClientFactory(handler),
            config,
            NullLogger<AiIntelligenceService>.Instance,
            slaService);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancel caller token

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            aiService.ClassifyTicketAsync("Title", "Description", cts.Token));
    }

    // =========================================================================
    // 10. No Secret Leakage in Exceptions or Output
    // =========================================================================

    [Fact]
    public async Task Gemini_OnFailure_NeverExposesApiKeyInExceptionMessage()
    {
        const string secretKey = "SECRET-GEMINI-TOKEN-DO-NOT-LEAK-XYZ";
        var handler = new MockHttpMessageHandler
        {
            StatusCodeToReturn = HttpStatusCode.Unauthorized,
            ContentToReturn = """{"error": {"message": "Invalid key provided."}}"""
        };

        var config = CreateGeminiConfig(apiKey: secretKey);
        var slaService = new SlaService(TimeProvider.System);
        var aiService = new AiIntelligenceService(
            new MockHttpClientFactory(handler),
            config,
            NullLogger<AiIntelligenceService>.Instance,
            slaService);

        var ex = await Assert.ThrowsAsync<AiProviderException>(() =>
            aiService.ClassifyTicketAsync("Test title", "Test description"));

        Assert.DoesNotContain(secretKey, ex.Message);
        Assert.DoesNotContain(secretKey, ex.ToString());
    }

    // =========================================================================
    // 11. Authorization Occurs Before Provider Calls
    // =========================================================================

    [Fact]
    public async Task Gemini_Controller_AuthorizationOccursBeforeProviderCalls()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, otherEmployee, _) = await TestHelpers.SeedDefaultUsersAsync(db);

        // Ticket belongs to otherEmployee
        var ticket = new Ticket
        {
            Id = 991,
            Title = "Confidential Financial Payroll Issue",
            Description = "Employee salary discrepancies in ERP.",
            Priority = "High",
            Status = "Open",
            CreatedByUserId = otherEmployee.Id,
            CreatedAt = DateTime.UtcNow
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var handler = new MockHttpMessageHandler();
        var config = CreateGeminiConfig();
        var slaService = new SlaService(TimeProvider.System);
        var aiService = new AiIntelligenceService(
            new MockHttpClientFactory(handler),
            config,
            NullLogger<AiIntelligenceService>.Instance,
            slaService);

        var retrievalService = new KnowledgeRetrievalService(db, NullLogger<KnowledgeRetrievalService>.Instance);
        var controller = new AiController(aiService, retrievalService, db, NullLogger<AiController>.Instance);

        // Caller is employee (not the owner and not admin)
        TestHelpers.SetUserContext(controller, employee.Id, "Employee");

        var response = await controller.Troubleshoot(991, CancellationToken.None);

        Assert.IsType<ForbidResult>(response);
        Assert.Equal(0, handler.SendCount); // Absolutely zero provider calls made!
    }

    // =========================================================================
    // 12. Recommendation Does Not Mutate Ticket or SLA Deadlines
    // =========================================================================

    [Fact]
    public async Task Gemini_Controller_RecommendPriority_DoesNotMutateTicketOrSlaDeadlines()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, _, _) = await TestHelpers.SeedDefaultUsersAsync(db);

        var originalFirstResponse = DateTime.UtcNow.AddHours(4);
        var originalResolution = DateTime.UtcNow.AddHours(24);

        var ticket = new Ticket
        {
            Id = 771,
            Title = "Customer facing checkout latency",
            Description = "Slow checkout process on website.",
            Priority = "Medium",
            Status = "Open",
            CreatedByUserId = employee.Id,
            FirstResponseDueAt = originalFirstResponse,
            ResolutionDueAt = originalResolution,
            CreatedAt = DateTime.UtcNow
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var handler = new MockHttpMessageHandler
        {
            ContentToReturn = WrapInGeminiCandidate("""
                {
                  "recommendedPriority": "Critical",
                  "confidenceScore": 0.95,
                  "rationale": "High business impact.",
                  "uncertaintyOrMissingInfo": null
                }
                """)
        };

        var config = CreateGeminiConfig();
        var slaService = new SlaService(TimeProvider.System);
        var aiService = new AiIntelligenceService(
            new MockHttpClientFactory(handler),
            config,
            NullLogger<AiIntelligenceService>.Instance,
            slaService);

        var retrievalService = new KnowledgeRetrievalService(db, NullLogger<KnowledgeRetrievalService>.Instance);
        var controller = new AiController(aiService, retrievalService, db, NullLogger<AiController>.Instance);
        TestHelpers.SetUserContext(controller, employee.Id, "Employee");

        var request = new RecommendPriorityRequest
        {
            TicketId = 771,
            Impact = "Company-wide",
            Urgency = "Critical"
        };

        var response = await controller.RecommendPriority(request, CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(response);
        var result = Assert.IsType<PriorityRecommendationResult>(okResult.Value);

        Assert.Equal("Critical", result.RecommendedPriority);

        // Verify ticket in database remains untouched!
        var ticketInDb = await db.Tickets.FindAsync(771);
        Assert.NotNull(ticketInDb);
        Assert.Equal("Medium", ticketInDb.Priority); // Still Medium!
        Assert.Equal(originalFirstResponse, ticketInDb.FirstResponseDueAt);
        Assert.Equal(originalResolution, ticketInDb.ResolutionDueAt);
    }

    // =========================================================================
    // 13. Transient 503 Bounded Retry Behavior
    // =========================================================================

    [Fact]
    public async Task Gemini_Transient503_RetriesOnceAndSucceeds()
    {
        var handler = new MockHttpMessageHandler();
        // First attempt returns 503
        handler.ResponseQueue.Enqueue(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("""{"error": "Temporary server error"}""", Encoding.UTF8, "application/json")
        });
        // Retry attempt returns 200 OK
        handler.ResponseQueue.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(WrapInGeminiCandidate("""
                {
                  "category": "Software",
                  "suggestedPriority": "Medium",
                  "confidenceScore": 0.85,
                  "rationale": "Transient retry succeeded."
                }
                """), Encoding.UTF8, "application/json")
        });

        var config = CreateGeminiConfig();
        var slaService = new SlaService(TimeProvider.System);
        var aiService = new AiIntelligenceService(
            new MockHttpClientFactory(handler),
            config,
            NullLogger<AiIntelligenceService>.Instance,
            slaService);

        var result = await aiService.ClassifyTicketAsync("Slow laptop", "Takes 10 minutes to boot.");

        Assert.NotNull(result);
        Assert.Equal("Software", result.Category);
        Assert.Equal(2, handler.SendCount); // Verified exactly 1 initial + 1 retry!
    }

    [Fact]
    public async Task Gemini_Repeated503_RetriesOnceAndThrowsAiProviderException()
    {
        var handler = new MockHttpMessageHandler();
        handler.ResponseQueue.Enqueue(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("""{"error": "Unavailable 1"}""", Encoding.UTF8, "application/json")
        });
        handler.ResponseQueue.Enqueue(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("""{"error": "Unavailable 2"}""", Encoding.UTF8, "application/json")
        });

        var config = CreateGeminiConfig();
        var slaService = new SlaService(TimeProvider.System);
        var aiService = new AiIntelligenceService(
            new MockHttpClientFactory(handler),
            config,
            NullLogger<AiIntelligenceService>.Instance,
            slaService);

        var ex = await Assert.ThrowsAsync<AiProviderException>(() =>
            aiService.ClassifyTicketAsync("Slow laptop", "Takes 10 minutes to boot."));

        Assert.Contains("503", ex.Message);
        Assert.Equal(2, handler.SendCount); // Verified bounded: does not retry indefinitely!
    }

    // =========================================================================
    // 14. Environment Variable Key Mapping (AI__Gemini__ApiKey)
    // =========================================================================

    [Fact]
    public async Task Gemini_ReadsApiKeyFromEnvironmentVariable_WhenConfigurationKeyMissing()
    {
        const string envKey = "ENV-GEMINI-TEST-KEY-777";
        Environment.SetEnvironmentVariable("AI__Gemini__ApiKey", envKey);

        try
        {
            // Config has empty key, but enabled is true and provider is Gemini
            var dict = new Dictionary<string, string?>
            {
                ["Ai:Enabled"] = "true",
                ["Ai:Provider"] = "Gemini",
                ["Ai:Gemini:ApiKey"] = "",
                ["Ai:Gemini:Model"] = "gemini-1.5-flash"
            };
            var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

            var handler = new MockHttpMessageHandler
            {
                ContentToReturn = WrapInGeminiCandidate("""
                    {
                      "category": "Infrastructure",
                      "suggestedPriority": "Low",
                      "confidenceScore": 0.80,
                      "rationale": "Desk power strip check."
                    }
                    """)
            };

            var slaService = new SlaService(TimeProvider.System);
            var aiService = new AiIntelligenceService(
                new MockHttpClientFactory(handler),
                config,
                NullLogger<AiIntelligenceService>.Instance,
                slaService);

            Assert.True(aiService.IsConfigured);

            var result = await aiService.ClassifyTicketAsync("Power strip", "Need another outlet.");
            Assert.NotNull(result);

            Assert.NotNull(handler.LastRequest);
            var headerKey = handler.LastRequest.Headers.GetValues("x-goog-api-key").FirstOrDefault();
            Assert.Equal(envKey, headerKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AI__Gemini__ApiKey", null);
        }
    }
}
