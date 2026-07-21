using MusterHubCommand.Api.Services;

namespace MusterHubCommand.Api.Tests;

public class FakeCoreDirectoryClient : ICoreDirectoryClient
{
    public static CoreDirectory? Payload;
    public static bool RejectKey;

    public Task<CoreDirectory> FetchAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        if (RejectKey) throw new CoreDirectoryAuthException();
        return Task.FromResult(Payload ?? throw new InvalidOperationException("No payload staged."));
    }
}
