using System.ComponentModel;
using SpkAgent.UI;

namespace SpkAgent.Tools;

public class DeclarationTools
{
    [Description("Build a complete SPK transport declaration in the exact required format. Returns the full declaration text ready for submission.")]
    public string BuildDeclaration(
        [Description("Date in YYYY-MM-DD format")] string date,
        [Description("Dispatch point city name, e.g. 'Gda\u0144sk'")] string origin,
        [Description("Destination city name, e.g. '\u017barnowiec'")] string destination,
        [Description("Sender identifier number")] string senderId,
        [Description("Route code, e.g. 'X-01'")] string routeCode,
        [Description("Shipment category: A, B, C, D, or E")] string category,
        [Description("Content description, max 200 characters")] string contents,
        [Description("Declared weight in kilograms")] int weightKg,
        [Description("Number of additional paid wagons (WDP). Use 0 for categories A and B as they get free wagons.")] int wdp,
        [Description("Special notes. Use 'BRAK' if none.")] string specialNotes,
        [Description("Payment amount in PP, e.g. '0 PP'")] string paymentAmount)
    {
        ConsoleUI.PrintToolCall("BuildDeclaration", $"route={routeCode}, cat={category}, weight={weightKg}kg");

        var declaration =
            "SYSTEM PRZESY\u0141EK KONDUKTORSKICH - DEKLARACJA ZAWARTO\u015aCI\n" +
            "======================================================\n" +
            $"DATA: {date}\n" +
            $"PUNKT NADAWCZY: {origin}\n" +
            "------------------------------------------------------\n" +
            $"NADAWCA: {senderId}\n" +
            $"PUNKT DOCELOWY: {destination}\n" +
            $"TRASA: {routeCode}\n" +
            "------------------------------------------------------\n" +
            $"KATEGORIA PRZESY\u0141KI: {category}\n" +
            "------------------------------------------------------\n" +
            $"OPIS ZAWARTO\u015aCI (max 200 znak\u00f3w): {contents}\n" +
            "------------------------------------------------------\n" +
            $"DEKLAROWANA MASA (kg): {weightKg}\n" +
            "------------------------------------------------------\n" +
            $"WDP: {wdp}\n" +
            "------------------------------------------------------\n" +
            $"UWAGI SPECJALNE: {specialNotes}\n" +
            "------------------------------------------------------\n" +
            $"KWOTA DO ZAP\u0141ATY: {paymentAmount}\n" +
            "------------------------------------------------------\n" +
            "O\u015aWIADCZAM, \u017bE PODANE INFORMACJE S\u0104 PRAWDZIWE.\n" +
            "BIOR\u0118 NA SIEBIE KONSEKWENCJ\u0118 ZA FA\u0141SZYWE O\u015aWIADCZENIE.\n" +
            "======================================================\n";

        ConsoleUI.PrintInfo($"Declaration built ({declaration.Length} chars)");
        return declaration;
    }

    [Description("Calculate the number of additional paid wagons (WDP) needed for a shipment. Categories A and B get free wagons, so WDP is always 0 for them.")]
    public string CalculateWDP(
        [Description("Shipment category: A, B, C, D, or E")] string category,
        [Description("Weight in kilograms")] int weightKg)
    {
        ConsoleUI.PrintToolCall("CalculateWDP", $"category={category}, weight={weightKg}kg");

        // Categories A (Strategic) and B (Medical) get complimentary additional wagons
        if (category is "A" or "B")
        {
            int wagonsNeeded = weightKg <= 1000 ? 0 : (int)Math.Ceiling((weightKg - 1000) / 500.0);
            return $"WDP = 0 (category {category} gets {wagonsNeeded} free additional wagons from System)";
        }

        // Paid categories
        if (weightKg <= 1000)
            return "WDP = 0 (fits in base 2-wagon configuration, 1000 kg capacity)";

        int extraWagons = (int)Math.Ceiling((weightKg - 1000) / 500.0);
        int cost = extraWagons * 55;
        return $"WDP = {extraWagons} (need {extraWagons} additional wagons at 55 PP each = {cost} PP total)";
    }

    [Description("Calculate the total fee for a shipment based on category and weight.")]
    public string CalculateFee(
        [Description("Shipment category: A, B, C, D, or E")] string category,
        [Description("Weight in kilograms")] int weightKg)
    {
        ConsoleUI.PrintToolCall("CalculateFee", $"category={category}, weight={weightKg}kg");

        // Categories A and B are fully System-funded
        if (category is "A" or "B")
            return $"Total fee = 0 PP (category {category} is fully funded by System, including additional wagons)";

        int baseFee = category switch
        {
            "C" => 2,
            "D" => 5,
            "E" => 10,
            _ => 0
        };

        double weightFee = weightKg switch
        {
            <= 5 => weightKg * 0.5,
            <= 25 => weightKg * 1.0,
            <= 100 => weightKg * 2.0,
            <= 500 => weightKg * 3.0,
            <= 1000 => weightKg * 5.0,
            _ => weightKg * 7.0
        };

        return $"Base fee: {baseFee} PP + Weight fee: {weightFee} PP = {baseFee + weightFee} PP (plus distance fee if applicable)";
    }
}
