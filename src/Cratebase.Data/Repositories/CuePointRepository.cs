using Cratebase.Core.Abstractions;
using Cratebase.Core.Models;
using Dapper;

namespace Cratebase.Data.Repositories;

public sealed class CuePointRepository(SqliteConnectionFactory connectionFactory) : ICuePointRepository
{
    private const string SelectColumns = """
        id AS Id, track_id AS TrackId, type AS Type, slot_index AS SlotIndex,
        position_ms AS PositionMs, loop_length_ms AS LoopLengthMs, label AS Label, source AS Source
        """;

    public async Task<IReadOnlyList<CuePoint>> GetForTrackAsync(long trackId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<CuePoint>(new CommandDefinition(
            $"SELECT {SelectColumns} FROM cue_point WHERE track_id = @trackId ORDER BY position_ms;",
            new { trackId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task ReplaceForTrackAsync(long trackId, string source, IReadOnlyCollection<CuePoint> cuePoints, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        using var transaction = connection.BeginTransaction();

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM cue_point WHERE track_id = @trackId AND source = @source;",
            new { trackId, source }, transaction, cancellationToken: ct));

        if (cuePoints.Count > 0)
        {
            var rows = cuePoints.Select(c => new
            {
                trackId,
                type = c.Type,
                slotIndex = c.SlotIndex,
                positionMs = c.PositionMs,
                loopLengthMs = c.LoopLengthMs,
                label = c.Label,
                source,
            });

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO cue_point (track_id, type, slot_index, position_ms, loop_length_ms, label, source)
                VALUES (@trackId, @type, @slotIndex, @positionMs, @loopLengthMs, @label, @source);
                """,
                rows, transaction, cancellationToken: ct));
        }

        transaction.Commit();
    }
}
