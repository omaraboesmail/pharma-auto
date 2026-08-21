using System.Data;
using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;
using PharmaAuto.Connector.Application;

namespace PharmaAuto.Connector.Infrastructure;

public sealed class SqlGeniusCatalogReader : IGeniusCatalogReader
{
    private readonly string connectionString;

    public SqlGeniusCatalogReader(string configuredConnectionString)
    {
        var builder = new SqlConnectionStringBuilder(configuredConnectionString)
        {
            ApplicationName = "PharmaAuto.Connector.ReadOnlyCatalog",
            ApplicationIntent = ApplicationIntent.ReadOnly,
            Encrypt = SqlConnectionEncryptOption.Optional,
            TrustServerCertificate = true,
            ConnectTimeout = 15,
            Pooling = true
        };
        connectionString = builder.ConnectionString;
    }

    public async IAsyncEnumerable<GeniusItemRow> ReadItemsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        const string sql = """
            SET NOCOUNT ON;
            SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
            SELECT itm_id, itm_code, itm_code2, itm_int_code,
                   itm_name_ar_encrypt, itm_name_en_encrypt,
                   itm_scientific_n1, itm_effictive_perc,
                   itm_has_expire, itm_active
            FROM dbo.Item_Catalog;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = Command(connection, sql);
        await using var reader = await command.ExecuteReaderAsync(
            CommandBehavior.SequentialAccess,
            cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            yield return new GeniusItemRow(
                reader.GetDecimal(0),
                String(reader, 1),
                String(reader, 2),
                String(reader, 3),
                Bytes(reader, 4),
                Bytes(reader, 5),
                String(reader, 6),
                String(reader, 7),
                !reader.IsDBNull(8) && reader.GetInt32(8) != 0,
                Active(String(reader, 9)));
        }
    }

    public async IAsyncEnumerable<GeniusItemBarcodeRow> ReadBarcodesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        const string sql = """
            SET NOCOUNT ON;
            SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
            SELECT io_itm_id, io_itm_int
            FROM dbo.Item_Objects
            WHERE io_itm_int IS NOT NULL AND LTRIM(RTRIM(io_itm_int)) <> '';
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = Command(connection, sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            yield return new GeniusItemBarcodeRow(reader.GetDecimal(0), reader.GetString(1));
        }
    }

    public async IAsyncEnumerable<GeniusItemVendorCodeRow> ReadVendorCodesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        const string sql = """
            SET NOCOUNT ON;
            SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
            SELECT itm_id, ven_id, itm_ven_code
            FROM dbo.Item_Vendor
            WHERE itm_ven_code IS NOT NULL AND LTRIM(RTRIM(itm_ven_code)) <> '';
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = Command(connection, sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            yield return new GeniusItemVendorCodeRow(
                reader.GetDecimal(0),
                reader.GetDecimal(1),
                reader.GetString(2));
        }
    }

    public async IAsyncEnumerable<GeniusVendorRow> ReadVendorsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        const string sql = """
            SET NOCOUNT ON;
            SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
            SELECT ven_id, ven_code, ven_name_ar, ven_name_en, ven_active
            FROM dbo.Vendor;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = Command(connection, sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            yield return new GeniusVendorRow(
                reader.GetDecimal(0),
                String(reader, 1),
                String(reader, 2),
                String(reader, 3),
                Active(String(reader, 4)));
        }
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static SqlCommand Command(SqlConnection connection, string sql) => new(sql, connection)
    {
        CommandType = CommandType.Text,
        CommandTimeout = 120
    };

    private static string? String(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static byte[]? Bytes(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<byte[]>(ordinal);

    private static bool Active(string? value) =>
        value is null || value.Trim() is not ("0" or "N" or "F" or "NO");
}
