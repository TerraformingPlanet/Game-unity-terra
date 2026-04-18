public interface IClientSnapshotSource
{
    bool TryBuildProjectionState(out ProjectionState state);
    bool TryBuildRegionState(out RegionState state);
    ClientSnapshot BuildClientSnapshot();
    WorldState BuildWorldState();
}