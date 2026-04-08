using System.ComponentModel;
using RadioMonitoring.Services;
using RadioMonitoring.UI;

namespace RadioMonitoring.Tools;

public class RadioTools
{
    private readonly CentralaApiClient _centrala;

    public RadioTools(CentralaApiClient centrala)
    {
        _centrala = centrala;
    }

    [Description("Submit the final radio monitoring report to headquarters. Call this once you have identified all required data from the intercepted intelligence.")]
    public async Task<string> TransmitReport(
        [Description("The real name of the city referred to as 'Syjon' in intercepted communications")] string cityName,
        [Description("The area of the city in square kilometers, rounded to exactly 2 decimal places (e.g. '12.34')")] string cityArea,
        [Description("The number of warehouses present in the city")] int warehousesCount,
        [Description("The contact phone number for the city (digits only, no dashes or spaces)")] string phoneNumber)
    {
        ConsoleUI.PrintStep($"TransmitReport: cityName={cityName}, cityArea={cityArea}, warehousesCount={warehousesCount}, phoneNumber={phoneNumber}");

        var answer = new
        {
            action = "transmit",
            cityName,
            cityArea,
            warehousesCount,
            phoneNumber
        };

        return await _centrala.VerifyAsync(answer);
    }
}
