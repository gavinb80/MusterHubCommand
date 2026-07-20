using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;

namespace MusterHubCommand.Api.Services;

// MusterHub core has no OIDC discovery document, just a bare JWKS at
// /.well-known/jwks.json -- same retriever as Rota/Skills, fetching it
// directly rather than expecting a full discovery document.
public class CoreJwksConfigurationRetriever : IConfigurationRetriever<JsonWebKeySet>
{
    public async Task<JsonWebKeySet> GetConfigurationAsync(string address, IDocumentRetriever retriever, CancellationToken cancel)
    {
        var json = await retriever.GetDocumentAsync(address, cancel);
        return new JsonWebKeySet(json);
    }
}
