public interface IHexCellStore
{
    MapRegion CurrentRegion { get; }

    HexCell[] GetCells();
    bool HasCells();
    HexCell GetCell(int q, int r);
}