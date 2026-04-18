public static class TerraformHabitabilityEvaluator
{
    public static bool IsHabitable(HexCell cell)
    {
        if (cell == null)
            return false;

        if (cell.terrain != null)
        {
            if (cell.terrain.terrainType == TerrainType.Vegetation)
                return true;

            if (cell.terrain.terrainType == TerrainType.Eau)
                return true;
        }

        float temperature = cell.state.tempLocale;
        float waterRatio = cell.state.waterRatio;
        return temperature >= -10f && temperature <= 50f && waterRatio >= 0.05f;
    }
}