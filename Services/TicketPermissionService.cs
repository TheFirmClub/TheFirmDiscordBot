using MySql.Data.MySqlClient;
using System;
using System.Linq;
using System.Threading.Tasks;

public class TicketPermissionService
{
    private const string ConnectionString =
        "Server=nw26472-001.eu.clouddb.ovh.net;Port=35666;Database=thefirm_qbcore2;Uid=thefirmprod;Pwd=edr6BYZqmq7eud0mwm;CharSet=utf8mb4;SslMode=Preferred;";

    public async Task SaveAsync(ulong channelId, ulong ownerId, ulong[] roles)
    {
        using var conn = new MySqlConnection(ConnectionString);
        await conn.OpenAsync();

        var cmd = new MySqlCommand(@"
            INSERT INTO ticket_permissions (channel_id, owner_id, allowed_roles)
            VALUES (@channel, @owner, @roles)
            ON DUPLICATE KEY UPDATE allowed_roles = @roles;
        ", conn);

        cmd.Parameters.AddWithValue("@channel", channelId);
        cmd.Parameters.AddWithValue("@owner", ownerId);
        cmd.Parameters.AddWithValue("@roles", string.Join(",", roles));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<ulong[]> GetRolesAsync(ulong channelId)
    {
        using var conn = new MySqlConnection(ConnectionString);
        await conn.OpenAsync();

        var cmd = new MySqlCommand(
            "SELECT allowed_roles FROM ticket_permissions WHERE channel_id = @id",
            conn);

        cmd.Parameters.AddWithValue("@id", channelId);

        var result = await cmd.ExecuteScalarAsync();

        if (result == null) return Array.Empty<ulong>();

        return result.ToString()
            .Split(',')
            .Select(x => ulong.TryParse(x, out var r) ? r : 0)
            .Where(x => x != 0)
            .ToArray();
    }
    
    public async Task<ulong?> GetOwnerAsync(ulong channelId)
    {
        using var conn = new MySqlConnection(ConnectionString);
        await conn.OpenAsync();

        var cmd = new MySqlCommand(
            "SELECT owner_id FROM ticket_permissions WHERE channel_id = @id LIMIT 1",
            conn);

        cmd.Parameters.AddWithValue("@id", channelId);

        var result = await cmd.ExecuteScalarAsync();

        if (result == null || result == DBNull.Value)
            return null;

        return ulong.TryParse(result.ToString(), out var ownerId) ? ownerId : null;
    }
    
    public async Task UpdateRolesAsync(ulong channelId, ulong[] roles)
    {
        using var conn = new MySqlConnection(ConnectionString);
        await conn.OpenAsync();

        var cmd = new MySqlCommand(@"
        UPDATE ticket_permissions
        SET allowed_roles = @roles
        WHERE channel_id = @channel;
    ", conn);

        cmd.Parameters.AddWithValue("@channel", channelId);
        cmd.Parameters.AddWithValue("@roles", string.Join(",", roles));

        await cmd.ExecuteNonQueryAsync();
    }
}