using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Atlas.Onboarding.Api.Applications;

/// <summary>
/// Gives the API description a request that is valid as it stands, so that "Try it out" works
/// without editing. Generated examples put "string" in the image fields, which is not base64.
/// </summary>
internal sealed class SubmitApplicationExample : ISchemaFilter
{
    /// <inheritdoc />
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type != typeof(SubmitApplicationRequest))
        {
            return;
        }

        // The same request as samples/approved-mb.json. A marker such as "scenario:inconclusive"
        // inside the decoded selfie, or "Possible-Match" as the last name, picks another scenario.
        schema.Example = new OpenApiObject
        {
            ["firstName"] = new OpenApiString("Ana"),
            ["lastName"] = new OpenApiString("Petrova"),
            ["dateOfBirth"] = new OpenApiString("1991-03-04"),
            ["country"] = new OpenApiString("MB"),
            ["nationalId"] = new OpenApiString("0403991450016"),
            ["email"] = new OpenApiString("ana@example.com"),
            ["phone"] = new OpenApiString("+10000000000"),
            ["documents"] = new OpenApiArray
            {
                Document("PASSPORT", "c2FtcGxlIHBhc3Nwb3J0IHBob3Rv"),
                Document("SELFIE", "c2FtcGxlIHNlbGZpZQ==")
            },
            ["termsAccepted"] = new OpenApiBoolean(true)
        };
    }

    private static OpenApiObject Document(string type, string image)
    {
        return new OpenApiObject
        {
            ["type"] = new OpenApiString(type),
            ["image"] = new OpenApiString(image)
        };
    }
}
