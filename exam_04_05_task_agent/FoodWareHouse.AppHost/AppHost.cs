// When running via 'aspire run' CLI, dashboard env vars are not pre-set.
// Provide defaults so the Aspire dashboard can start.
if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
    Environment.SetEnvironmentVariable("ASPNETCORE_URLS", "https://localhost:15001;http://localhost:15000");

if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL")))
    Environment.SetEnvironmentVariable("ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL", "http://localhost:4317");

if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL")))
    Environment.SetEnvironmentVariable("ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL", "http://localhost:4318");

if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPIRE_ALLOW_UNSECURED_TRANSPORT")))
    Environment.SetEnvironmentVariable("ASPIRE_ALLOW_UNSECURED_TRANSPORT", "true");

var builder = DistributedApplication.CreateBuilder(args);
builder.AddProject<Projects.FoodWareHouse>("foodwarehouse").WithHttpEndpoint(port: 5013);
builder.Build().Run();
