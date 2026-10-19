namespace PocketExport.Core.Database;

using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using PocketExport.Core.Ledger;
using PocketExport.Core.Models;
using PocketExport.Core.Storage;

/// <summary>
/// Hydrates and incrementally synchronizes the local SQLite index from filesystem ledger manifests.
/// </summary>
public sealed class LedgerProjectionService : IDisposable
{
    private readonly IVaultPathResolver _vaultPathResolver;
    private readonly ILedgerManager _ledgerManager;
    private readonly SqliteIndexContext _context;
    private readonly ILogger<LedgerProjectionService>? _logger;
    private FileSystemWatcher? _recordingsWatcher;
    private FileSystemWatcher? _speakersWatcher;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="LedgerProjectionService"/> class.
    /// </summary>
    /// <param name="vaultPathResolver">Resolver for platform and vault-relative filesystem paths.</param>
    /// <param name="ledgerManager">Ledger persistence manager.</param>
    /// <param name="context">SQLite metadata index context.</param>
    /// <param name="logger">Optional logger instance.</param>
    public LedgerProjectionService(
        IVaultPathResolver vaultPathResolver,
        ILedgerManager ledgerManager,
        SqliteIndexContext context,
        ILogger<LedgerProjectionService>? logger = null)
    {
        _vaultPathResolver = vaultPathResolver ?? throw new ArgumentNullException(nameof(vaultPathResolver));
        _ledgerManager = ledgerManager ?? throw new ArgumentNullException(nameof(ledgerManager));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger;
    }

    /// <summary>
    /// Scans all recording and speaker manifests in the ledger and populates the SQLite index from scratch.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    public async Task HydrateAllAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _context.InitializeDatabaseAsync(cancellationToken);

        var recordings = await _ledgerManager.GetAllRecordingManifestsAsync(cancellationToken);
        var speakers = await _ledgerManager.GetAllSpeakerProfilesAsync(cancellationToken);

        using var connection = _context.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        // Prune SQLite records no longer present on disk
        var recordingIds = recordings.Select(r => r.Id).ToList();
        if (recordingIds.Count > 0)
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    "DELETE FROM recordings WHERE id NOT IN @Ids;",
                    new { Ids = recordingIds },
                    transaction: transaction,
                    cancellationToken: cancellationToken));
        }
        else
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    "DELETE FROM recordings;",
                    transaction: transaction,
                    cancellationToken: cancellationToken));
        }

        var speakerIds = speakers.Select(s => s.Id).ToList();
        if (speakerIds.Count > 0)
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    "DELETE FROM speakers WHERE id NOT IN @Ids;",
                    new { Ids = speakerIds },
                    transaction: transaction,
                    cancellationToken: cancellationToken));
        }
        else
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    "DELETE FROM speakers;",
                    transaction: transaction,
                    cancellationToken: cancellationToken));
        }

        // Upsert all speakers
        foreach (var speaker in speakers)
        {
            await UpsertSpeakerAsync(connection, transaction, speaker, cancellationToken);
        }

        // Upsert all recordings and their associated speaker turns
        foreach (var recording in recordings)
        {
            await UpsertRecordingAsync(connection, transaction, recording, cancellationToken);
        }

        transaction.Commit();
    }

    /// <summary>
    /// Projects a recording manifest into the SQLite index, updating existing records without duplication.
    /// </summary>
    /// <param name="manifest">The recording manifest to project.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    public async Task ProjectRecordingAsync(RecordingManifest manifest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var connection = _context.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        await UpsertRecordingAsync(connection, transaction, manifest, cancellationToken);

        transaction.Commit();
    }

    /// <summary>
    /// Projects a recording manifest by identifier into SQLite by loading it from the ledger.
    /// </summary>
    /// <param name="recordingId">The unique recording identifier.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    public async Task ProjectRecordingAsync(string recordingId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordingId);

        var manifest = await _ledgerManager.GetRecordingManifestAsync(recordingId, cancellationToken);
        if (manifest is not null)
        {
            await ProjectRecordingAsync(manifest, cancellationToken);
        }
    }

    /// <summary>
    /// Projects a speaker profile into the SQLite index, updating existing records without duplication.
    /// </summary>
    /// <param name="profile">The speaker profile to project.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    public async Task ProjectSpeakerAsync(SpeakerProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var connection = _context.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        await UpsertSpeakerAsync(connection, transaction, profile, cancellationToken);

        transaction.Commit();
    }

    /// <summary>
    /// Projects a speaker profile by identifier into SQLite by loading it from the ledger.
    /// </summary>
    /// <param name="speakerId">The unique speaker identifier.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    public async Task ProjectSpeakerAsync(string speakerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speakerId);

        var profile = await _ledgerManager.GetSpeakerProfileAsync(speakerId, cancellationToken);
        if (profile is not null)
        {
            await ProjectSpeakerAsync(profile, cancellationToken);
        }
    }

    /// <summary>
    /// Removes a recording and its associated speaker mappings from the SQLite index.
    /// </summary>
    /// <param name="recordingId">The unique recording identifier.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    public async Task DeleteRecordingAsync(string recordingId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordingId);
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var connection = _context.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        await connection.ExecuteAsync(
            new CommandDefinition(
                "DELETE FROM recording_speakers WHERE recording_id = @Id;",
                new { Id = recordingId },
                transaction: transaction,
                cancellationToken: cancellationToken));

        await connection.ExecuteAsync(
            new CommandDefinition(
                "DELETE FROM recordings WHERE id = @Id;",
                new { Id = recordingId },
                transaction: transaction,
                cancellationToken: cancellationToken));

        transaction.Commit();
    }

    /// <summary>
    /// Removes a speaker profile from the SQLite index.
    /// </summary>
    /// <param name="speakerId">The unique speaker identifier.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    public async Task DeleteSpeakerAsync(string speakerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speakerId);
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var connection = _context.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await connection.ExecuteAsync(
            new CommandDefinition(
                "DELETE FROM speakers WHERE id = @Id;",
                new { Id = speakerId },
                cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Starts filesystem watchers on ledger manifest directories to trigger incremental projections.
    /// </summary>
    public void StartWatching()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        StopWatching();

        var recordingsPath = _vaultPathResolver.ResolvePlatformPath(".ledger/recordings");
        if (Directory.Exists(recordingsPath))
        {
            _recordingsWatcher = new FileSystemWatcher(recordingsPath, "*.json")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };
            _recordingsWatcher.Created += OnRecordingFileEvent;
            _recordingsWatcher.Changed += OnRecordingFileEvent;
            _recordingsWatcher.Renamed += OnRecordingFileRenamed;
            _recordingsWatcher.Deleted += OnRecordingFileDeleted;
        }

        var speakersPath = _vaultPathResolver.ResolvePlatformPath(".ledger/speakers");
        if (Directory.Exists(speakersPath))
        {
            _speakersWatcher = new FileSystemWatcher(speakersPath, "*.json")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };
            _speakersWatcher.Created += OnSpeakerFileEvent;
            _speakersWatcher.Changed += OnSpeakerFileEvent;
            _speakersWatcher.Renamed += OnSpeakerFileRenamed;
            _speakersWatcher.Deleted += OnSpeakerFileDeleted;
        }
    }

    /// <summary>
    /// Stops and disposes active filesystem watchers.
    /// </summary>
    public void StopWatching()
    {
        if (_recordingsWatcher is not null)
        {
            _recordingsWatcher.EnableRaisingEvents = false;
            _recordingsWatcher.Created -= OnRecordingFileEvent;
            _recordingsWatcher.Changed -= OnRecordingFileEvent;
            _recordingsWatcher.Renamed -= OnRecordingFileRenamed;
            _recordingsWatcher.Deleted -= OnRecordingFileDeleted;
            _recordingsWatcher.Dispose();
            _recordingsWatcher = null;
        }

        if (_speakersWatcher is not null)
        {
            _speakersWatcher.EnableRaisingEvents = false;
            _speakersWatcher.Created -= OnSpeakerFileEvent;
            _speakersWatcher.Changed -= OnSpeakerFileEvent;
            _speakersWatcher.Renamed -= OnSpeakerFileRenamed;
            _speakersWatcher.Deleted -= OnSpeakerFileDeleted;
            _speakersWatcher.Dispose();
            _speakersWatcher = null;
        }
    }

    private void OnRecordingFileRenamed(object sender, RenamedEventArgs e) => OnRecordingFileEvent(sender, e);

    private void OnSpeakerFileRenamed(object sender, RenamedEventArgs e) => OnSpeakerFileEvent(sender, e);

    private void OnRecordingFileEvent(object sender, FileSystemEventArgs e)
    {
        if (e.Name is null || e.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var id = Path.GetFileNameWithoutExtension(e.Name);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(100);
                await ProjectRecordingAsync(id);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to incrementally project recording manifest {Id}", id);
            }
        });
    }

    private void OnRecordingFileDeleted(object sender, FileSystemEventArgs e)
    {
        if (e.Name is null || e.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var id = Path.GetFileNameWithoutExtension(e.Name);
        _ = Task.Run(async () =>
        {
            try
            {
                await DeleteRecordingAsync(id);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to incrementally delete recording projection {Id}", id);
            }
        });
    }

    private void OnSpeakerFileEvent(object sender, FileSystemEventArgs e)
    {
        if (e.Name is null || e.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var id = Path.GetFileNameWithoutExtension(e.Name);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(100);
                await ProjectSpeakerAsync(id);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to incrementally project speaker profile {Id}", id);
            }
        });
    }

    private void OnSpeakerFileDeleted(object sender, FileSystemEventArgs e)
    {
        if (e.Name is null || e.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var id = Path.GetFileNameWithoutExtension(e.Name);
        _ = Task.Run(async () =>
        {
            try
            {
                await DeleteSpeakerAsync(id);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to incrementally delete speaker projection {Id}", id);
            }
        });
    }

    private static async Task UpsertRecordingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        RecordingManifest manifest,
        CancellationToken cancellationToken)
    {
        const string upsertRecordingSql = """
            INSERT INTO recordings (
                id, pocket_id, created_at, title, duration_seconds,
                staging_audio_path, pocket_metadata_path, routed_transcript_path,
                routed_summary_path, status, error_message, domain,
                customer, project, initiative, confidence, template_id, updated_at
            ) VALUES (
                @Id, @PocketId, @CreatedAt, @Title, @DurationSeconds,
                @StagingAudioPath, @PocketMetadataPath, @RoutedTranscriptPath,
                @RoutedSummaryPath, @Status, @ErrorMessage, @Domain,
                @Customer, @Project, @Initiative, @Confidence, @TemplateId, @UpdatedAt
            )
            ON CONFLICT(id) DO UPDATE SET
                pocket_id = excluded.pocket_id,
                created_at = excluded.created_at,
                title = excluded.title,
                duration_seconds = excluded.duration_seconds,
                staging_audio_path = excluded.staging_audio_path,
                pocket_metadata_path = excluded.pocket_metadata_path,
                routed_transcript_path = excluded.routed_transcript_path,
                routed_summary_path = excluded.routed_summary_path,
                status = excluded.status,
                error_message = excluded.error_message,
                domain = excluded.domain,
                customer = excluded.customer,
                project = excluded.project,
                initiative = excluded.initiative,
                confidence = excluded.confidence,
                template_id = excluded.template_id,
                updated_at = excluded.updated_at;
            """;

        var command = new CommandDefinition(
            upsertRecordingSql,
            new
            {
                manifest.Id,
                manifest.PocketId,
                CreatedAt = manifest.CreatedAt.ToString("O"),
                manifest.Title,
                manifest.DurationSeconds,
                manifest.StagingAudioPath,
                manifest.PocketMetadataPath,
                manifest.RoutedTranscriptPath,
                manifest.RoutedSummaryPath,
                manifest.Status,
                manifest.ErrorMessage,
                Domain = manifest.Classification?.Domain,
                Customer = manifest.Classification?.Customer,
                Project = manifest.Classification?.Project,
                Initiative = manifest.Classification?.Initiative,
                Confidence = manifest.Classification?.Confidence,
                TemplateId = manifest.Classification?.TemplateId,
                UpdatedAt = DateTimeOffset.UtcNow.ToString("O")
            },
            transaction: transaction,
            cancellationToken: cancellationToken);

        await connection.ExecuteAsync(command);

        // Delete existing recording_speakers and re-insert current set
        await connection.ExecuteAsync(
            new CommandDefinition(
                "DELETE FROM recording_speakers WHERE recording_id = @Id;",
                new { manifest.Id },
                transaction: transaction,
                cancellationToken: cancellationToken));

        if (manifest.Speakers is not null && manifest.Speakers.Count > 0)
        {
            const string insertSpeakerTurnSql = """
                INSERT INTO recording_speakers (
                    recording_id, cluster_id, enrolled_speaker_id, display_name,
                    match_confidence, needs_disambiguation, sample_start_ms, sample_end_ms, embedding_vector
                ) VALUES (
                    @RecordingId, @ClusterId, @EnrolledSpeakerId, @DisplayName,
                    @MatchConfidence, @NeedsDisambiguation, @SampleStartMs, @SampleEndMs, @EmbeddingVector
                );
                """;

            foreach (var speaker in manifest.Speakers)
            {
                var speakerTurnCmd = new CommandDefinition(
                    insertSpeakerTurnSql,
                    new
                    {
                        RecordingId = manifest.Id,
                        speaker.ClusterId,
                        speaker.EnrolledSpeakerId,
                        speaker.DisplayName,
                        speaker.MatchConfidence,
                        NeedsDisambiguation = speaker.NeedsDisambiguation ? 1 : 0,
                        SampleStartMs = speaker.SoloSnippet?.StartMs,
                        SampleEndMs = speaker.SoloSnippet?.EndMs,
                        EmbeddingVector = speaker.EmbeddingVector is not null
                            ? JsonSerializer.Serialize(speaker.EmbeddingVector)
                            : null
                    },
                    transaction: transaction,
                    cancellationToken: cancellationToken);

                await connection.ExecuteAsync(speakerTurnCmd);
            }
        }
    }

    private static async Task UpsertSpeakerAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SpeakerProfile profile,
        CancellationToken cancellationToken)
    {
        const string upsertSpeakerSql = """
            INSERT INTO speakers (
                id, display_name, organization, enrolled_at,
                source_recording_id, sample_start_ms, sample_end_ms, embedding
            ) VALUES (
                @Id, @DisplayName, @Organization, @EnrolledAt,
                @SourceRecordingId, @SampleStartMs, @SampleEndMs, @Embedding
            )
            ON CONFLICT(id) DO UPDATE SET
                display_name = excluded.display_name,
                organization = excluded.organization,
                enrolled_at = excluded.enrolled_at,
                source_recording_id = excluded.source_recording_id,
                sample_start_ms = excluded.sample_start_ms,
                sample_end_ms = excluded.sample_end_ms,
                embedding = excluded.embedding;
            """;

        var command = new CommandDefinition(
            upsertSpeakerSql,
            new
            {
                profile.Id,
                profile.DisplayName,
                profile.Organization,
                EnrolledAt = profile.EnrolledAt.ToString("O"),
                profile.SourceRecordingId,
                SampleStartMs = profile.SampleSnippetBounds.StartMs,
                SampleEndMs = profile.SampleSnippetBounds.EndMs,
                Embedding = JsonSerializer.Serialize(profile.Embedding)
            },
            transaction: transaction,
            cancellationToken: cancellationToken);

        await connection.ExecuteAsync(command);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        StopWatching();
        _disposed = true;
    }
}
