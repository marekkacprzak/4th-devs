namespace SavethemAgent.Models;

public record VehicleSegment(string Vehicle, List<string> Moves);

public class RouteResult
{
    // Ordered segments: segment[0] = motorized vehicle, segment[1] (optional) = walk after dismount
    public List<VehicleSegment> Segments { get; set; } = new();

    public double TotalFuel { get; set; }
    public double TotalFood { get; set; }
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }

    public string VehicleName => Segments.FirstOrDefault()?.Vehicle ?? "";
    public List<string> Moves => Segments.SelectMany(s => s.Moves).ToList();

    /// <summary>
    /// Returns the answer array.
    /// Single vehicle: ["vehicle", dir1, dir2, ...]
    /// With dismount:  ["vehicle", dir1, ..., "dismount", dir_n, ...]
    /// </summary>
    public string[] ToAnswer()
    {
        var result = new List<string>();

        for (int i = 0; i < Segments.Count; i++)
        {
            if (i == 0)
            {
                // First segment: output vehicle name then directions
                result.Add(Segments[i].Vehicle);
                result.AddRange(Segments[i].Moves);
            }
            else
            {
                // Subsequent segments: output "dismount" then directions (no vehicle name)
                result.Add("dismount");
                result.AddRange(Segments[i].Moves);
            }
        }

        return result.ToArray();
    }

    public override string ToString()
    {
        if (!IsValid) return $"Route INVALID: {ErrorMessage}";

        int totalMoves = Segments.Sum(s => s.Moves.Count);
        bool hasWalk = Segments.Count > 1;
        var segDesc = Segments[0].Vehicle;
        if (hasWalk) segDesc += $"({Segments[0].Moves.Count}) → walk({Segments[1].Moves.Count})";
        else segDesc += $"({totalMoves})";

        return $"Route OK: {segDesc}, {totalMoves} moves, fuel={TotalFuel:F1}/10, food={TotalFood:F1}/10";
    }
}
