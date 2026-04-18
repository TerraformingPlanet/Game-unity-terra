public interface IGridRefreshSink
{
    void RefreshCell(HexCell cell);
    void RefreshAllCells();
}