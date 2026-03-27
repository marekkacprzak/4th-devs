using System.Diagnostics;
using SavethemAgent.Models;

namespace SavethemAgent.Services;

/// <summary>
/// Finds the optimal route on a 10x10 grid.
///
/// Answer format supported by the hub API:
///   Single vehicle:  ["vehicle", dir1, dir2, ...]
///   With dismount:   ["vehicle", dir1, ..., "dismount", dir_n+1, ...]
///
/// Terrain:
///   Walkable (.) — all vehicles
///   Rough    (T=trees, R=rocks) — horse + walk only
///   Water    (W) — walk only (maybe horse too, depends on notes)
///   Obstacle (#) — nobody
///
/// Strategy:
///   For each vehicle, try:
///   A) Full path using that vehicle only.
///   B) Vehicle on its accessible terrain, dismount at best point → walk rest.
///   Walk always uses both canCrossWater=true and canCrossRough=true.
/// </summary>
public class RoutePlanner
{
    private static readonly ActivitySource Activity = new("SavethemAgent.Planner");

    private const double MaxFood = 10.0;
    private const double MaxFuel = 10.0;
    private const double WalkFoodPerStep = 2.5;

    private static readonly (int dr, int dc, string name)[] Directions =
    {
        (0,  1, "right"),
        (1,  0, "down"),
        (0, -1, "left"),
        (-1, 0, "up")
    };

    public RouteResult Plan(GridMap map, IReadOnlyList<Vehicle> vehicles)
    {
        using var span = Activity.StartActivity("planner.plan");
        span?.SetTag("vehicles.count", vehicles.Count);

        // Walk is always available as the on-foot fallback (can cross all terrain)
        var allVehicles = new List<Vehicle>(vehicles);
        if (!allVehicles.Any(v => IsWalking(v.Name)))
            allVehicles.Add(new Vehicle { Name = "walk", FuelPerStep = 0.0, FoodPerStep = 2.5, CanCrossWater = true, CanCrossRough = true });

        // Pre-compute BFS backwards from goal with walk capabilities (used for dismount planning)
        var walkDistFromGoal = BfsDistance(map, map.GoalPosition, canCrossWater: true, canCrossRough: true);

        RouteResult? best = null;
        double bestMargin = double.MinValue;

        foreach (var vehicle in allVehicles)
        {
            // ── Option A: use this vehicle for the FULL path ─────────────────
            {
                var dists = BfsDistance(map, map.StartPosition, vehicle.CanCrossWater, vehicle.CanCrossRough);
                var (gr, gc) = map.GoalPosition;

                if (dists[gr, gc] >= 0)
                {
                    int steps = dists[gr, gc];
                    double fuel = IsWalking(vehicle.Name) ? 0.0 : steps * vehicle.FuelPerStep;
                    double food = steps * vehicle.FoodPerStep;

                    if (fuel <= MaxFuel + 1e-9 && food <= MaxFood + 1e-9)
                    {
                        double margin = Math.Min(
                            vehicle.FuelPerStep <= 0 ? 1.0 : (MaxFuel - fuel) / MaxFuel,
                            (MaxFood - food) / MaxFood);

                        if (margin > bestMargin)
                        {
                            var path = ReconstructPath(map, map.StartPosition, map.GoalPosition,
                                vehicle.CanCrossWater, vehicle.CanCrossRough);
                            if (path != null)
                            {
                                bestMargin = margin;
                                best = new RouteResult
                                {
                                    Segments = new List<VehicleSegment>
                                    {
                                        new(vehicle.Name, PathToMoves(path))
                                    },
                                    TotalFuel = fuel,
                                    TotalFood = food,
                                    IsValid = true
                                };
                            }
                        }
                    }
                }
            }

            // ── Option B: vehicle on accessible terrain → dismount → walk ───
            if (!IsWalking(vehicle.Name))
            {
                var dryDist = BfsDistance(map, map.StartPosition, vehicle.CanCrossWater, vehicle.CanCrossRough);
                int maxDrySteps = vehicle.FuelPerStep > 0
                    ? (int)Math.Floor(MaxFuel / vehicle.FuelPerStep)
                    : int.MaxValue;

                int bestDrySteps = -1;
                (int row, int col) bestD = (-1, -1);
                double bestSplitMargin = double.MinValue;

                for (int r = 0; r < GridMap.Height; r++)
                {
                    for (int c = 0; c < GridMap.Width; c++)
                    {
                        int d = dryDist[r, c];
                        if (d < 0 || d > maxDrySteps) continue;

                        int w = walkDistFromGoal[r, c];
                        if (w < 0) continue;

                        double fuel = IsWalking(vehicle.Name) ? 0.0 : d * vehicle.FuelPerStep;
                        double food = d * vehicle.FoodPerStep + w * WalkFoodPerStep;

                        if (fuel > MaxFuel + 1e-9 || food > MaxFood + 1e-9) continue;

                        double fuelMargin = vehicle.FuelPerStep <= 0 ? 1.0 : (MaxFuel - fuel) / MaxFuel;
                        double foodMargin = (MaxFood - food) / MaxFood;
                        double margin = Math.Min(fuelMargin, foodMargin);

                        if (margin > bestSplitMargin)
                        {
                            bestSplitMargin = margin;
                            bestDrySteps = d;
                            bestD = (r, c);
                        }
                    }
                }

                if (bestD.row >= 0 && bestSplitMargin > bestMargin)
                {
                    var pathSD = ReconstructPath(map, map.StartPosition, bestD,
                        vehicle.CanCrossWater, vehicle.CanCrossRough);
                    var pathDG = ReconstructPath(map, bestD, map.GoalPosition,
                        canCrossWater: true, canCrossRough: true);

                    if (pathSD != null && pathDG != null)
                    {
                        var dryMoves = PathToMoves(pathSD);
                        var walkMoves = PathToMoves(pathDG);

                        double fuel = IsWalking(vehicle.Name) ? 0.0 : bestDrySteps * vehicle.FuelPerStep;
                        double food = bestDrySteps * vehicle.FoodPerStep + walkMoves.Count * WalkFoodPerStep;

                        bestMargin = bestSplitMargin;

                        var segments = new List<VehicleSegment> { new(vehicle.Name, dryMoves) };
                        if (walkMoves.Count > 0)
                            segments.Add(new("walk", walkMoves));

                        best = new RouteResult
                        {
                            Segments = segments,
                            TotalFuel = fuel,
                            TotalFood = food,
                            IsValid = true
                        };
                    }
                }
            }
        }

        if (best != null)
        {
            span?.SetTag("result", "success");
            span?.SetTag("vehicle", best.VehicleName);
            span?.SetTag("fuel", best.TotalFuel);
            span?.SetTag("food", best.TotalFood);
            return best;
        }

        // Failure: report diagnostics
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"No valid route within resources (fuel≤{MaxFuel}, food≤{MaxFood}).");
        foreach (var v in allVehicles)
        {
            var d = BfsDistance(map, map.StartPosition, v.CanCrossWater, v.CanCrossRough);
            var (gr2, gc2) = map.GoalPosition;
            int steps = d[gr2, gc2];
            double f = steps >= 0 && !IsWalking(v.Name) ? steps * v.FuelPerStep : 0;
            double fo = steps >= 0 ? steps * v.FoodPerStep : -1;
            sb.AppendLine($"  {v.Name}: {steps} steps, fuel={f:F1}, food={fo:F1}, water={v.CanCrossWater}, rough={v.CanCrossRough}");
        }

        span?.SetTag("result", "no_valid_route");
        return new RouteResult { IsValid = false, ErrorMessage = sb.ToString() };
    }

    private int[,] BfsDistance(GridMap map, (int row, int col) start, bool canCrossWater, bool canCrossRough = false)
    {
        var dist = new int[GridMap.Height, GridMap.Width];
        for (int r = 0; r < GridMap.Height; r++)
            for (int c = 0; c < GridMap.Width; c++)
                dist[r, c] = -1;

        dist[start.row, start.col] = 0;
        var queue = new Queue<(int row, int col)>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var (r, c) = queue.Dequeue();
            foreach (var (dr, dc, _) in Directions)
            {
                int nr = r + dr, nc = c + dc;
                if (nr < 0 || nr >= GridMap.Height || nc < 0 || nc >= GridMap.Width) continue;
                if (dist[nr, nc] >= 0) continue;
                if (!map.IsWalkable(nr, nc, canCrossWater, canCrossRough)) continue;

                dist[nr, nc] = dist[r, c] + 1;
                queue.Enqueue((nr, nc));
            }
        }

        return dist;
    }

    private List<(int row, int col)>? ReconstructPath(
        GridMap map,
        (int row, int col) start,
        (int row, int col) goal,
        bool canCrossWater,
        bool canCrossRough = false)
    {
        if (start == goal) return new List<(int, int)> { start };

        var parent = new (int row, int col)?[GridMap.Height, GridMap.Width];
        var visited = new bool[GridMap.Height, GridMap.Width];

        visited[start.row, start.col] = true;
        var queue = new Queue<(int row, int col)>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var (r, c) = queue.Dequeue();
            if (r == goal.row && c == goal.col)
            {
                var path = new List<(int row, int col)>();
                int cr = r, cc = c;
                while (!(cr == start.row && cc == start.col))
                {
                    path.Add((cr, cc));
                    var p = parent[cr, cc]!.Value;
                    cr = p.row; cc = p.col;
                }
                path.Add(start);
                path.Reverse();
                return path;
            }

            foreach (var (dr, dc, _) in Directions)
            {
                int nr = r + dr, nc = c + dc;
                if (nr < 0 || nr >= GridMap.Height || nc < 0 || nc >= GridMap.Width) continue;
                if (visited[nr, nc]) continue;
                if (!map.IsWalkable(nr, nc, canCrossWater, canCrossRough)) continue;

                visited[nr, nc] = true;
                parent[nr, nc] = (r, c);
                queue.Enqueue((nr, nc));
            }
        }

        return null;
    }

    private List<string> PathToMoves(List<(int row, int col)> path)
    {
        var moves = new List<string>();
        for (int i = 1; i < path.Count; i++)
        {
            int dr = path[i].row - path[i - 1].row;
            int dc = path[i].col - path[i - 1].col;
            moves.Add((dr, dc) switch
            {
                (0,  1) => "right",
                (0, -1) => "left",
                (1,  0) => "down",
                (-1, 0) => "up",
                _ => "right"
            });
        }
        return moves;
    }

    private static bool IsWalking(string vehicleName) =>
        vehicleName.Equals("on_foot", StringComparison.OrdinalIgnoreCase) ||
        vehicleName.Equals("walking", StringComparison.OrdinalIgnoreCase) ||
        vehicleName.Equals("foot", StringComparison.OrdinalIgnoreCase) ||
        vehicleName.Equals("walk", StringComparison.OrdinalIgnoreCase);
}
