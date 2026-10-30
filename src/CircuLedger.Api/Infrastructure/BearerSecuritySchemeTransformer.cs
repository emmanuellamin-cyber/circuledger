using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace CircuLedger.Api.Infrastructure;

/// <summary>Adds the JWT bearer scheme to the OpenAPI document so Swagger UI shows an "Authorize" button.</summary>
internal sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "CircuLedger API",
            Version = "v1",
            Description = "Smart Youth Mobile Circularity Hub: intake, data sanitization certificates, resource accounting, marketplace and reinvestment, on a tamper-evident ledger."
        };
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Paste the accessToken from POST /api/v1/auth/login"
        };
        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] });
        return Task.CompletedTask;
    }
}
