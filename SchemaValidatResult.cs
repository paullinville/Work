public class SchemaValidationResult
{
    public List<string> MissingTables { get; } = new();
    public List<string> ExtraTables { get; } = new();
    public List<string> MissingColumns { get; } = new();
    public List<string> ExtraColumns { get; } = new();
    public List<string> TypeMismatches { get; } = new();
    public List<string> NullabilityMismatches { get; } = new();

    public bool IsValid =>
        !MissingTables.Any() &&
        !ExtraTables.Any() &&
        !MissingColumns.Any() &&
        !ExtraColumns.Any() &&
        !TypeMismatches.Any() &&
        !NullabilityMismatches.Any();
}

public class EfTable
{
    public string Schema { get; set; } = default!;
    public string Name { get; set; } = default!;
    public List<EfColumn> Columns { get; set; } = new();
}

public class EfColumn
{
    public string Name { get; set; } = default!;
    public string StoreType { get; set; } = default!;
    public bool IsNullable { get; set; }
}

public class DbTable
{
    public string Schema { get; set; } = default!;
    public string Name { get; set; } = default!;
    public List<DbColumn> Columns { get; set; } = new();
}

public class DbColumn
{
    public string Name { get; set; } = default!;
    public string DataType { get; set; } = default!;
    public short MaxLength { get; set; }
    public byte Precision { get; set; }
    public byte Scale { get; set; }
    public bool IsNullable { get; set; }
}
