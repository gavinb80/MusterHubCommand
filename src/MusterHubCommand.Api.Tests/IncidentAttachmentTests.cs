using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using MusterHubCommand.Api.Contracts;

namespace MusterHubCommand.Api.Tests;

// Attachment bytes land in LocalFileStorage (the factory points
// Storage:LocalPath at a temp dir); these cover the metadata,
// authorisation and validation rules around them, on both the web
// (operator/JWT) and tablet (device-token/attendance) surfaces.
[Collection("Api")]
public class IncidentAttachmentTests(CommandApiFactory factory)
{
    private readonly HttpClient _operatorA = factory.AsUser(Seed.OrgA, Seed.OperatorAPerson);
    private readonly HttpClient _operatorB = factory.AsUser(Seed.OrgB, Seed.OperatorBPerson);

    private static async Task<HttpResponseMessage> UploadRawAsync(HttpClient client, string path, string name, string contentType, string body)
    {
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(content, "file", name);
        return await client.PostAsync(path, form);
    }

    [Fact]
    public async Task Web_upload_download_and_delete_round_trip()
    {
        var uploadResponse = await UploadRawAsync(_operatorA, $"/api/incidents/{Seed.IncidentA}/attachments", "scene.jpg", "image/jpeg", "fake jpeg bytes");
        uploadResponse.EnsureSuccessStatusCode();
        var uploaded = (await uploadResponse.Content.ReadFromJsonAsync<IncidentAttachmentDto>(ClientExtensions.Json))!;
        Assert.Equal("scene.jpg", uploaded.FileName);
        Assert.Equal("Operator A", uploaded.UploadedByName);

        var incident = (await (await _operatorA.GetAsync($"/api/incidents/{Seed.IncidentA}")).Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
        Assert.Single(incident.Attachments);

        var download = await _operatorA.GetAsync($"/api/incident-attachments/{uploaded.Id}");
        download.EnsureSuccessStatusCode();
        Assert.Equal("image/jpeg", download.Content.Headers.ContentType!.MediaType);
        Assert.Equal("fake jpeg bytes", await download.Content.ReadAsStringAsync());

        var delete = await _operatorA.DeleteAsync($"/api/incident-attachments/{uploaded.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _operatorA.GetAsync($"/api/incident-attachments/{uploaded.Id}")).StatusCode);
    }

    [Fact]
    public async Task Unsupported_content_type_is_rejected_with_a_clean_400()
    {
        var response = await UploadRawAsync(_operatorA, $"/api/incidents/{Seed.IncidentA}/attachments", "malware.exe", "application/x-msdownload", "MZ");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Non_operator_cannot_upload_via_the_web_console()
    {
        var crew = factory.AsUser(Seed.OrgA, Seed.CrewAPerson);
        var response = await UploadRawAsync(crew, $"/api/incidents/{Seed.IncidentA}/attachments", "photo.png", "image/png", "png bytes");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Org_B_cannot_download_org_As_attachment()
    {
        var uploadResponse = await UploadRawAsync(_operatorA, $"/api/incidents/{Seed.IncidentA}/attachments", "scene.jpg", "image/jpeg", "fake jpeg bytes");
        var uploaded = (await uploadResponse.Content.ReadFromJsonAsync<IncidentAttachmentDto>(ClientExtensions.Json))!;

        var crossOrgDownload = await _operatorB.GetAsync($"/api/incident-attachments/{uploaded.Id}");
        Assert.Equal(HttpStatusCode.NotFound, crossOrgDownload.StatusCode);

        var crossOrgDelete = await _operatorB.DeleteAsync($"/api/incident-attachments/{uploaded.Id}");
        Assert.Equal(HttpStatusCode.NotFound, crossOrgDelete.StatusCode);
    }

    [Fact]
    public async Task Tablet_upload_requires_the_device_to_be_attending_and_is_attributed_to_the_device()
    {
        // Seed's own comment: DeviceA is attending IncidentA via its callsign.
        var tabletOnScene = factory.AsDevice(Seed.DeviceATokenPlaintext);
        var uploadResponse = await UploadRawAsync(tabletOnScene, $"/api/tablet/incidents/{Seed.IncidentA}/attachments", "scene.jpg", "image/jpeg", "tablet bytes");
        uploadResponse.EnsureSuccessStatusCode();
        var uploaded = (await uploadResponse.Content.ReadFromJsonAsync<IncidentAttachmentDto>(ClientExtensions.Json))!;
        Assert.Equal("Engine 1 (A)", uploaded.UploadedByName);

        var download = await tabletOnScene.GetAsync($"/api/tablet/incident-attachments/{uploaded.Id}");
        download.EnsureSuccessStatusCode();
        Assert.Equal("tablet bytes", await download.Content.ReadAsStringAsync());

        // DeviceB is a different org's tablet and isn't attending IncidentA either way.
        var otherDevice = factory.AsDevice(Seed.DeviceBTokenPlaintext);
        var deniedUpload = await UploadRawAsync(otherDevice, $"/api/tablet/incidents/{Seed.IncidentA}/attachments", "scene.jpg", "image/jpeg", "nope");
        Assert.Equal(HttpStatusCode.NotFound, deniedUpload.StatusCode);
        var deniedDownload = await otherDevice.GetAsync($"/api/tablet/incident-attachments/{uploaded.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deniedDownload.StatusCode);
    }
}
