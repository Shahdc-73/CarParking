using System.Net;

namespace ParkWise.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class HealthTests(ApiFactory factory)
{
    [Fact]
    public async Task Liveness_returns_200()
        => Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().GetAsync("/health")).StatusCode);

    [Fact]
    public async Task Readiness_returns_200_when_database_is_reachable_and_migrated()
        => Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().GetAsync("/health/ready")).StatusCode);

    [Fact]
    public async Task Unknown_route_returns_ProblemDetails_json()
    {
        var response = await factory.CreateClient().GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Swagger_document_is_served()
        => Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().GetAsync("/swagger/v1/swagger.json")).StatusCode);
}
