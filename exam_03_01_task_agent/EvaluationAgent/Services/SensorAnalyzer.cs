using System.Diagnostics;
using EvaluationAgent.Models;
using EvaluationAgent.UI;

namespace EvaluationAgent.Services;

public static class SensorAnalyzer
{
    private static readonly ActivitySource Activity = new("EvaluationAgent.SensorAnalyzer");

    // Maps sensor_type keyword → set of active field names
    private static readonly Dictionary<string, string[]> SensorFieldMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["temperature"] = ["temperature_K"],
        ["pressure"]    = ["pressure_bar"],
        ["water"]       = ["water_level_meters"],
        ["voltage"]     = ["voltage_supply_v"],
        ["humidity"]    = ["humidity_percent"],
    };

    // Valid ranges for each active field [min, max] (inclusive)
    private static readonly Dictionary<string, (double Min, double Max)> ValidRanges = new()
    {
        ["temperature_K"]      = (553.0, 873.0),
        ["pressure_bar"]       = (60.0, 160.0),
        ["water_level_meters"] = (5.0, 15.0),
        ["voltage_supply_v"]   = (229.0, 231.0),
        ["humidity_percent"]   = (40.0, 80.0),
    };

    // All known measurement fields
    private static readonly string[] AllFields =
    [
        "temperature_K", "pressure_bar", "water_level_meters", "voltage_supply_v", "humidity_percent"
    ];

    public static List<SensorReading> AnalyzeAll(List<SensorReading> readings)
    {
        using var span = Activity.StartActivity("sensor.analyze_all");
        span?.SetTag("readings.count", readings.Count);

        int outOfRange = 0;
        int inactiveSensorNonZero = 0;

        foreach (var reading in readings)
        {
            Analyze(reading);
            if (reading.Anomalies.HasFlag(AnomalyType.OutOfRange)) outOfRange++;
            if (reading.Anomalies.HasFlag(AnomalyType.InactiveSensorNonZero)) inactiveSensorNonZero++;
        }

        int invalidCount = readings.Count(r => !r.DataIsValid);

        ConsoleUI.PrintInfo($"Programmatic analysis: {outOfRange} out-of-range, {inactiveSensorNonZero} inactive-sensor-non-zero, {invalidCount} total invalid data");

        span?.SetTag("anomaly.out_of_range", outOfRange);
        span?.SetTag("anomaly.inactive_sensor", inactiveSensorNonZero);
        span?.SetTag("readings.invalid", invalidCount);

        return readings;
    }

    private static void Analyze(SensorReading reading)
    {
        var activeFields = GetActiveFields(reading.Data.SensorType);
        var inactiveFields = AllFields.Except(activeFields).ToArray();

        // Anomaly 1: active fields out of valid range
        foreach (var field in activeFields)
        {
            var value = GetFieldValue(reading.Data, field);
            if (ValidRanges.TryGetValue(field, out var range))
            {
                if (value < range.Min || value > range.Max)
                {
                    reading.Anomalies |= AnomalyType.OutOfRange;
                }
            }
        }

        // Anomaly 2: inactive fields should be 0
        foreach (var field in inactiveFields)
        {
            var value = GetFieldValue(reading.Data, field);
            if (value != 0.0)
            {
                reading.Anomalies |= AnomalyType.InactiveSensorNonZero;
            }
        }

        reading.DataIsValid = reading.Anomalies == AnomalyType.None;
    }

    private static HashSet<string> GetActiveFields(string sensorType)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // sensor_type can be "temperature", "voltage/temperature", etc.
        var parts = sensorType.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (SensorFieldMap.TryGetValue(part, out var fields))
            {
                foreach (var f in fields)
                    result.Add(f);
            }
            // Unknown part: no active fields added (treat all as inactive)
        }

        return result;
    }

    private static double GetFieldValue(SensorData data, string fieldName) => fieldName switch
    {
        "temperature_K"      => data.TemperatureK,
        "pressure_bar"       => data.PressureBar,
        "water_level_meters" => data.WaterLevelMeters,
        "voltage_supply_v"   => data.VoltageSupplyV,
        "humidity_percent"   => data.HumidityPercent,
        _                    => 0.0
    };
}
