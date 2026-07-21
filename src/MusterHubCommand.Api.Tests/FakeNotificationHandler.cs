using System.Net;
using System.Text;
using System.Text.Json;

namespace MusterHubCommand.Api.Tests;

// Stands in for core's own NotificationRelayController -- records every
// outbound relay call CoreNotificationService makes instead of hitting a
// real core instance, so NotificationTests can assert exactly who got
// paged (and who didn't) without any network dependency.
public record CapturedRelayRequest(Guid UserId, string Title, string Body, string? ApiKeyHeader);

public class FakeNotificationHandler : HttpMessageHandler
{
    // Static, not instance state: a fresh handler instance is created per
    // HttpClientFactory-managed HttpMessageHandler lifetime, but tests need
    // one list spanning the whole run -- same convention as
    // FakeCoreDirectoryClient's static Payload.
    public static readonly List<CapturedRelayRequest> Requests = [];

    // Property lookup is case-insensitive: PostAsJsonAsync's implicit
    // default (no explicit JsonSerializerOptions passed, same as
    // CoreNotificationService.SendAsync's own call) serializes with
    // camelCase names ("userId", not "UserId") -- confirmed by inspecting
    // the raw request body directly, since GetProperty's default
    // case-sensitive lookup was silently throwing KeyNotFoundException on
    // every real call from CoreNotificationService, masking the actual
    // notification-scoping bug behind what looked like an unrelated test
    // failure.
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var json = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Requests.Add(new CapturedRelayRequest(
            GetCaseInsensitive(root, "userId").GetGuid(),
            GetCaseInsensitive(root, "title").GetString()!,
            GetCaseInsensitive(root, "body").GetString()!,
            request.Headers.TryGetValues("X-Api-Key", out var values) ? values.FirstOrDefault() : null));

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"devicesNotified":0}""", Encoding.UTF8, "application/json"),
        };
    }

    private static JsonElement GetCaseInsensitive(JsonElement element, string propertyName)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        }
        throw new KeyNotFoundException($"Property '{propertyName}' not found in {element}");
    }
}
