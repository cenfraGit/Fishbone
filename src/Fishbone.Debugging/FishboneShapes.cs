namespace Fishbone.Debugging;

/// <summary>
/// A region a debugger draws over an image: runs of pixels, each on one row from a start to an
/// end column, both included. The same runs HALCON's <c>get_region_runs</c> gives.
/// </summary>
public sealed class FishboneRegion
{
    public FishboneRegion(int[] rows, int[] columnStarts, int[] columnEnds)
    {
        if (columnStarts.Length != rows.Length || columnEnds.Length != rows.Length)
            throw new ArgumentException(
                $"A region needs a column start and end for every row, not {rows.Length} rows, {columnStarts.Length} starts and {columnEnds.Length} ends.");
        Rows = rows;
        ColumnStarts = columnStarts;
        ColumnEnds = columnEnds;
    }

    public int[] Rows { get; }
    public int[] ColumnStarts { get; }
    public int[] ColumnEnds { get; }
}

/// <summary>
/// A contour a debugger draws over an image: a line through points in subpixel coordinates,
/// where a pixel's center sits at its whole row and column. The same points HALCON's
/// <c>get_contour_xld</c> gives.
/// </summary>
public sealed class FishboneContour
{
    public FishboneContour(double[] rows, double[] columns)
    {
        if (columns.Length != rows.Length)
            throw new ArgumentException($"A contour needs a column for every row, not {rows.Length} rows and {columns.Length} columns.");
        Rows = rows;
        Columns = columns;
    }

    public double[] Rows { get; }
    public double[] Columns { get; }
}
