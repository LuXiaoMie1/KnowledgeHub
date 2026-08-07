namespace Microsoft.Data.SqlClient;

/// <summary>
/// Wrapper for SQL Server vector type.
/// In production, this should use the native SqlVector from Microsoft.Data.SqlClient 7.1+
/// </summary>
public class SqlVector<T> where T : struct
{
    public SqlVector() { }

    public SqlVector(T[] data)
    {
        Data = data;
    }

    public T[] Data { get; set; } = [];
}
