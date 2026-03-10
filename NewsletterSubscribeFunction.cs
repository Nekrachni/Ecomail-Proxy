using System.Collections.Concurrent;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Ecomail_Proxy;

public class NewsletterSubscribeFunction(IHttpClientFactory httpClientFactory, ILogger<NewsletterSubscribeFunction> logger)
{
    private const string AllowedOrigin = "https://redflags.cz";
    private const int MaxBodySize = 1024;
    private const int RateLimitWindowSeconds = 60;
    private const int RateLimitMaxRequests = 5;

    private static readonly ConcurrentDictionary<string, (int Count, DateTime WindowStart)> RateLimitStore = new();

    [Function("newsletter-subscribe")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options")] HttpRequest req)
    {
        var origin = req.Headers.Origin.ToString();

        if (req.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            return origin == AllowedOrigin
                ? Cors(new StatusCodeResult(204))
                : new StatusCodeResult(403);

        if (origin != AllowedOrigin)
            return new ObjectResult(new { ok = false, error = "Forbidden." }) { StatusCode = 403 };

        var clientIp = req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (IsRateLimited(clientIp))
            return Cors(new ObjectResult(new { ok = false, error = "Too many requests. Try again later." }) { StatusCode = 429 });

        if (req.ContentLength > MaxBodySize)
            return Cors(new BadRequestObjectResult(new { ok = false, error = "Request too large." }));

        string body;
        using (var reader = new StreamReader(req.Body))
            body = await reader.ReadToEndAsync();

        if (body.Length > MaxBodySize)
            return Cors(new BadRequestObjectResult(new { ok = false, error = "Request too large." }));

        string? email;
        try
        {
            var doc = JsonDocument.Parse(body);
            email = doc.RootElement.GetProperty("email").GetString();
        }
        catch
        {
            return Cors(new BadRequestObjectResult(new { ok = false, error = "Invalid request body." }));
        }

        if (string.IsNullOrWhiteSpace(email) || !MailAddress.TryCreate(email, out _))
            return Cors(new BadRequestObjectResult(new { ok = false, error = "Invalid email address." }));

        var apiKey = Environment.GetEnvironmentVariable("ECOMAIL_API_KEY");
        var listId = Environment.GetEnvironmentVariable("ECOMAIL_LIST_ID") ?? "28";

        if (string.IsNullOrEmpty(apiKey))
        {
            logger.LogError("ECOMAIL_API_KEY is not configured");
            return Cors(new ObjectResult(new { ok = false, error = "Server misconfiguration." }) { StatusCode = 500 });
        }

        try
        {
            var client = httpClientFactory.CreateClient();
            var payload = JsonSerializer.Serialize(new
            {
                subscriber_data = new { email },
                trigger_autoresponders = false,
                update_existing = true,
                resubscribe = false
            });

            var request = new HttpRequestMessage(HttpMethod.Post,
                $"https://api2.ecomailapp.cz/lists/{listId}/subscribe")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("key", apiKey);

            var response = await client.SendAsync(request);
            if (response.IsSuccessStatusCode)
                return Cors(new OkObjectResult(new { ok = true }));

            var errorBody = await response.Content.ReadAsStringAsync();
            logger.LogWarning("Ecomail API error {StatusCode}: {Body}", (int)response.StatusCode, errorBody);
            return Cors(new ObjectResult(new { ok = false, error = "Subscription failed." }) { StatusCode = 500 });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error calling Ecomail API");
            return Cors(new ObjectResult(new { ok = false, error = "Internal server error." }) { StatusCode = 500 });
        }
    }

    private static bool IsRateLimited(string ip)
    {
        var now = DateTime.UtcNow;
        var entry = RateLimitStore.AddOrUpdate(ip,
            _ => (1, now),
            (_, existing) =>
            {
                if ((now - existing.WindowStart).TotalSeconds > RateLimitWindowSeconds)
                    return (1, now);
                return (existing.Count + 1, existing.WindowStart);
            });
        return entry.Count > RateLimitMaxRequests;
    }

    private static IActionResult Cors(IActionResult inner)
    {
        return new CorsWrappedResult(inner);
    }

    private class CorsWrappedResult(IActionResult inner) : IActionResult
    {
        public async Task ExecuteResultAsync(ActionContext context)
        {
            var response = context.HttpContext.Response;
            response.Headers["Access-Control-Allow-Origin"] = AllowedOrigin;
            response.Headers["Access-Control-Allow-Methods"] = "POST, OPTIONS";
            response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
            await inner.ExecuteResultAsync(context);
        }
    }
}
