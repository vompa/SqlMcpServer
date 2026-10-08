namespace SqlMcpServer.Data;

public sealed record TableInfo(string Name, long RowCount);

public sealed record ColumnInfo(string Name, string Type, bool NotNull, bool PrimaryKey);

/// <param name="Columns">Spaltennamen in Ergebnisreihenfolge.</param>
/// <param name="Rows">Ergebniszeilen, höchstens <see cref="DatabaseOptions.MaxRows"/>.</param>
/// <param name="Truncated">true, wenn es mehr Zeilen gab als <see cref="DatabaseOptions.MaxRows"/>.</param>
public sealed record QueryResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows,
    bool Truncated);
