namespace SavethemAgent.Models;

public class Vehicle
{
    public string Name { get; set; } = "";
    // Fuel consumed per one grid step
    public double FuelPerStep { get; set; } = 1.0;
    // Food consumed per one grid step (default 1 for all vehicles)
    public double FoodPerStep { get; set; } = 1.0;
    // Whether this vehicle can cross water (W) cells
    public bool CanCrossWater { get; set; } = false;
    // Whether this vehicle can cross rough terrain (T=trees, R=rocks)
    public bool CanCrossRough { get; set; } = false;

    public override string ToString() =>
        $"{Name} (fuel/step={FuelPerStep:F2}, food/step={FoodPerStep:F2}, water={CanCrossWater}, rough={CanCrossRough})";
}
