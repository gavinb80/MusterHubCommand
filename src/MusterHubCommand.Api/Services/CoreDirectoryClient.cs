using Microsoft.Extensions.Options;
using MusterHubCommand.Api.Configuration;

namespace MusterHubCommand.Api.Services;

// Command's own copy of core's DirectoryExport contracts -- deliberately not
// a shared project reference, same as the JWT trust boundary: the two
// codebases integrate over a wire format, never over each other's types.
// Only Groups/Stations/Users are pulled in -- Command has no use for core's
// Appliances or station memberships (the web console's Control Room
// Operator role is org-wide, not station-scoped, so per-employee station
// assignment isn't needed for V1).
public record CoreDirectory(
    Guid ServiceId,
    string ServiceName,
    List<CoreGroup> Groups,
    List<CoreStation> Stations,
    List<CoreUser> Users);

public record CoreGroup(Guid Id, string Name);

public record CoreStation(Guid Id, string Code, string Name, Guid GroupId);

public record CoreUser(Guid Id, string FullName, string Rank, string? EmployeeNumber);

// Interface so the import's upsert logic is testable against a canned
// payload without a running core instance.
public interface ICoreDirectoryClient
{
    Task<CoreDirectory> FetchAsync(string apiKey, CancellationToken cancellationToken = default);
}

public class HttpCoreDirectoryClient(HttpClient httpClient, IOptions<CoreAuthOptions> coreOptions) : ICoreDirectoryClient
{
    public async Task<CoreDirectory> FetchAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        // Core's origin is already configured for the JWKS fetch; the
        // directory endpoint lives on the same host, so no second base-URL
        // setting to keep in sync.
        var origin = new Uri(coreOptions.Value.JwksUri).GetLeftPart(UriPartial.Authority);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{origin}/api/integrations/directory");
        request.Headers.Add("X-Api-Key", apiKey);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new CoreDirectoryAuthException();
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CoreDirectory>(cancellationToken))
            ?? throw new InvalidOperationException("Core returned an empty directory response.");
    }
}

// Distinct exception so the controller can map "core rejected the key" to a
// clean 400 with a human message rather than a generic 500.
public class CoreDirectoryAuthException : Exception
{
    public CoreDirectoryAuthException() : base("MusterHub rejected the API key. Check it was created with the Directory Export permission and hasn't expired.") { }
}
