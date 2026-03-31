namespace WindPower.Models;

/// <summary>One hourly entry from the weather forecast API.</summary>
public record WeatherEntry(DateTime Timestamp, double WindSpeed);

/// <summary>Turbine operational parameters from the turbineSpecs API call.</summary>
public record TurbineSpecsData(
    double MaxWindSpeed,
    double CutInSpeed,
    int OptimalPitchAngle);

/// <summary>Power requirements from the powerRequirements API call.</summary>
public record PowerRequirementsData(
    double RequiredPower,
    string Unit = "kW");

/// <summary>A single turbine schedule configuration entry.</summary>
public record ConfigEntry(
    string StartDate,   // "yyyy-MM-dd"
    string StartHour,   // "HH:00:00"
    int PitchAngle,
    string TurbineMode, // "idle" | "production"
    double WindMs)      // wind speed in m/s at this hour (required by unlockCodeGenerator)
{
    /// <summary>Key used in the batch config dictionary.</summary>
    public string DateTimeKey => $"{StartDate} {StartHour}";
}
