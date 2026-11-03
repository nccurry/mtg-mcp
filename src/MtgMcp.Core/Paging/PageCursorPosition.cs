namespace MtgMcp.Core.Paging;

/// <summary>
/// Says what the numbers in a <see cref="PageCursorPosition"/> mean.
/// </summary>
public enum PageCursorShape
{
    /// <summary>
    /// One row offset into one list.
    /// </summary>
    Offset,

    /// <summary>
    /// Two row offsets, one into each of two lists that a result pages in turn.
    /// </summary>
    TwoOffsets,

    /// <summary>
    /// A provider page number and the count of that page's rows already shown.
    /// </summary>
    ProviderPage,
}

/// <summary>
/// Names where the next page of a paged result starts. The shape says which numbers the position holds, and the
/// reader of a cursor names the shape it expects, so a cursor of another shape is refused.
/// </summary>
public readonly record struct PageCursorPosition
{
    /// <summary>
    /// Holds the first number, whose meaning depends on the shape.
    /// </summary>
    private readonly int first;

    /// <summary>
    /// Holds the second number, whose meaning depends on the shape, or zero for one offset.
    /// </summary>
    private readonly int second;

    /// <summary>
    /// Creates a position from its shape and numbers.
    /// </summary>
    private PageCursorPosition(PageCursorShape shape, int first, int second)
    {
        Shape = shape;
        this.first = first;
        this.second = second;
    }

    /// <summary>
    /// Gets what the numbers of this position mean.
    /// </summary>
    public PageCursorShape Shape { get; }

    /// <summary>
    /// Gets the row offset of a position that has one offset.
    /// </summary>
    public int Offset => first;

    /// <summary>
    /// Gets the row offset in the first list of a position that has two offsets.
    /// </summary>
    public int FirstOffset => first;

    /// <summary>
    /// Gets the row offset in the second list of a position that has two offsets.
    /// </summary>
    public int SecondOffset => second;

    /// <summary>
    /// Gets the provider page number of a provider-page position.
    /// </summary>
    public int Page => first;

    /// <summary>
    /// Gets the count of rows of the provider page that were already shown, for a provider-page position.
    /// </summary>
    public int RowsShown => second;

    /// <summary>
    /// Creates a position with one row offset.
    /// </summary>
    public static PageCursorPosition AtOffset(int offset)
    {
        return new PageCursorPosition(PageCursorShape.Offset, offset, 0);
    }

    /// <summary>
    /// Creates a position with one row offset in each of two lists.
    /// </summary>
    public static PageCursorPosition AtOffsets(int firstOffset, int secondOffset)
    {
        return new PageCursorPosition(PageCursorShape.TwoOffsets, firstOffset, secondOffset);
    }

    /// <summary>
    /// Creates a position inside a provider page, after the rows of that page already shown.
    /// </summary>
    public static PageCursorPosition AtProviderPage(int page, int rowsShown)
    {
        return new PageCursorPosition(PageCursorShape.ProviderPage, page, rowsShown);
    }

    /// <summary>
    /// Creates the position of the first page for a shape.
    /// </summary>
    public static PageCursorPosition Start(PageCursorShape shape)
    {
        return new PageCursorPosition(shape, 0, 0);
    }

    /// <summary>
    /// Tells whether both numbers are zero or more.
    /// </summary>
    internal bool IsNonNegative => first >= 0 && second >= 0;
}
