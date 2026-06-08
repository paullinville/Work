public class SchemaValidator
{
    private readonly DbContext _context;
    private readonly string _connectionString;

    public SchemaValidator(DbContext context)
    {
        _context = context;
        _connectionString = context.Database.GetConnectionString()
            ?? throw new InvalidOperationException("No connection string found.");
    }

    public async Task<SchemaValidationResult> ValidateAsync()
    {
        var efModel = LoadEfModel();
        var dbModel = await LoadDatabaseModelAsync();

        return Compare(efModel, dbModel);
    }

    // -------------------------------
    // 1. Load EF Core metadata
    // -------------------------------
    private List<EfTable> LoadEfModel()
    {
        return _context.Model.GetEntityTypes()
            .Where(e => !e.IsOwned())
            .Select(e => new EfTable
            {
                Schema = e.GetSchema() ?? "dbo",
                Name = e.GetTableName()!,
                Columns = e.GetProperties().Select(p => new EfColumn
                {
                    Name = p.GetColumnName()!,
                    StoreType = p.GetColumnType()!,
                    IsNullable = p.IsColumnNullable()
                }).ToList()
            })
            .ToList();
    }

    // -------------------------------
    // 2. Load SQL Server schema
    // -------------------------------
    private async Task<List<DbTable>> LoadDatabaseModelAsync()
    {
        const string sql = @"
SELECT 
    s.name AS SchemaName,
    t.name AS TableName,
    c.name AS ColumnName,
    ty.name AS DataType,
    c.max_length,
    c.precision,
    c.scale,
    c.is_nullable
FROM sys.objects t
JOIN sys.schemas s ON t.schema_id = s.schema_id
JOIN sys.columns c ON t.object_id = c.object_id
JOIN sys.types ty ON c.user_type_id = ty.user_type_id
WHERE t.type IN ('U', 'V'); -- tables + views
";

        var result = new List<DbTable>();

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = new SqlCommand(sql, conn);
        using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var schema = reader.GetString(0);
            var table = reader.GetString(1);
            var column = reader.GetString(2);

            var dbTable = result.FirstOrDefault(x => x.Schema == schema && x.Name == table);
            if (dbTable == null)
            {
                dbTable = new DbTable
                {
                    Schema = schema,
                    Name = table,
                    Columns = new List<DbColumn>()
                };
                result.Add(dbTable);
            }

            dbTable.Columns.Add(new DbColumn
            {
                Name = column,
                DataType = reader.GetString(3),
                MaxLength = reader.GetInt16(4),
                Precision = reader.GetByte(5),
                Scale = reader.GetByte(6),
                IsNullable = reader.GetBoolean(7)
            });
        }

        return result;
    }

    // -------------------------------
    // 3. Compare EF vs DB
    // -------------------------------
    private SchemaValidationResult Compare(List<EfTable> ef, List<DbTable> db)
    {
        var result = new SchemaValidationResult();

        // Missing tables
        foreach (var efTable in ef)
        {
            if (!db.Any(t => t.Schema == efTable.Schema && t.Name == efTable.Name))
            {
                result.MissingTables.Add($"{efTable.Schema}.{efTable.Name}");
                continue;
            }

            var dbTable = db.First(t => t.Schema == efTable.Schema && t.Name == efTable.Name);

            // Column-level comparison
            foreach (var efCol in efTable.Columns)
            {
                var dbCol = dbTable.Columns.FirstOrDefault(c => c.Name == efCol.Name);
                if (dbCol == null)
                {
                    result.MissingColumns.Add($"{efTable.Schema}.{efTable.Name}.{efCol.Name}");
                    continue;
                }

                // Type mismatch
                if (!dbCol.DataType.Equals(ExtractBaseType(efCol.StoreType), StringComparison.OrdinalIgnoreCase))
                {
                    result.TypeMismatches.Add(
                        $"{efTable.Schema}.{efTable.Name}.{efCol.Name}: EF={efCol.StoreType}, DB={dbCol.DataType}");
                }

                // Nullability mismatch
                if (efCol.IsNullable != dbCol.IsNullable)
                {
                    result.NullabilityMismatches.Add(
                        $"{efTable.Schema}.{efTable.Name}.{efCol.Name}: EF Nullable={efCol.IsNullable}, DB Nullable={dbCol.IsNullable}");
                }
            }

            // Extra columns
            foreach (var dbCol in dbTable.Columns)
            {
                if (!efTable.Columns.Any(c => c.Name == dbCol.Name))
                {
                    result.ExtraColumns.Add($"{dbTable.Schema}.{dbTable.Name}.{dbCol.Name}");
                }
            }
        }

        // Extra tables
        foreach (var dbTable in db)
        {
            if (!ef.Any(t => t.Schema == dbTable.Schema && t.Name == dbTable.Name))
            {
                result.ExtraTables.Add($"{dbTable.Schema}.{dbTable.Name}");
            }
        }

        return result;
    }

    private string ExtractBaseType(string storeType)
    {
        var idx = storeType.IndexOf('(');
        return idx > 0 ? storeType[..idx] : storeType;
    }
}
