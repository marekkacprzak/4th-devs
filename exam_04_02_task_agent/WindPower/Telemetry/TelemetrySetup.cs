using WindPower.Config;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace WindPower.Telemetry;

public sealed class TelemetrySetup : IDisposable
{
    private readonly TracerProvider? _tracerProvider;
    private readonly MeterProvider? _meterProvider;

    public TelemetrySetup(TelemetryConfig config)
    {
        if (!config.Enabled) return;

        var otlpEndpoint = new Uri(
            Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
            ?? config.OtlpEndpoint);

        var resource = ResourceBuilder
            .CreateDefault()
            .AddService(config.ServiceName);

        _tracerProvider = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(resource)
            .AddSource(config.ServiceName)
            .AddSource("WindPower.Centrala")
            .AddSource("Microsoft.Extensions.AI")
            .AddSource("Microsoft.Agents.AI")
            .AddOtlpExporter(o => o.Endpoint = otlpEndpoint)
            .Build();

        _meterProvider = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(resource)
            .AddMeter("Microsoft.Extensions.AI")
            .AddMeter("Microsoft.Agents.AI")
            .AddOtlpExporter(o => o.Endpoint = otlpEndpoint)
            .Build();
    }

    public void Dispose()
    {
        _tracerProvider?.Dispose();
        _meterProvider?.Dispose();
    }
}
