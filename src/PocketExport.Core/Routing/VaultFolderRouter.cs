using System.Globalization;
using PocketExport.Core.Models;
using PocketExport.Core.Storage;

namespace PocketExport.Core.Routing;

/// <summary>
/// Routes classified recordings to structured hierarchy paths within the vault,
/// diverting low-confidence classifications to a review queue and resolving filename collisions monotonically.
/// </summary>
public sealed class VaultFolderRouter : IVaultFolderRouter
{
    private const double ConfidenceThreshold = 0.75;
    private readonly IVaultPathResolver _pathResolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="VaultFolderRouter"/> class.
    /// </summary>
    /// <param name="pathResolver">The vault path resolver service.</param>
    public VaultFolderRouter(IVaultPathResolver pathResolver)
    {
        ArgumentNullException.ThrowIfNull(pathResolver);
        _pathResolver = pathResolver;
    }

    /// <inheritdoc />
    public string DetermineRoutedPath(
        ClassificationResult classification,
        string recordingId,
        DateTime createdAt,
        string titleSlug)
    {
        DateTime utc = createdAt.Kind switch
        {
            DateTimeKind.Unspecified => DateTime.SpecifyKind(createdAt, DateTimeKind.Utc),
            DateTimeKind.Local => createdAt.ToUniversalTime(),
            _ => createdAt
        };

        return DetermineRoutedPath(
            classification,
            recordingId,
            new DateTimeOffset(utc, TimeSpan.Zero),
            titleSlug);
    }

    /// <inheritdoc />
    public string DetermineRoutedPath(
        ClassificationResult classification,
        string recordingId,
        DateTimeOffset createdAt,
        string titleSlug)
    {
        ArgumentNullException.ThrowIfNull(classification);
        ArgumentException.ThrowIfNullOrWhiteSpace(recordingId);
        ArgumentNullException.ThrowIfNull(titleSlug);

        string relativeDirectory;
        if (classification.Confidence >= ConfidenceThreshold)
        {
            var segments = new List<string> { "routed" };

            string domain = _pathResolver.SanitizePathSegment(classification.Domain);
            if (!string.IsNullOrWhiteSpace(domain))
            {
                segments.Add(domain);
            }

            string customer = _pathResolver.SanitizePathSegment(classification.Customer);
            if (!string.IsNullOrWhiteSpace(customer))
            {
                segments.Add(customer);
            }

            string project = _pathResolver.SanitizePathSegment(classification.Project);
            if (!string.IsNullOrWhiteSpace(project))
            {
                segments.Add(project);
            }

            relativeDirectory = string.Join('/', segments);
        }
        else
        {
            relativeDirectory = "_needs_review";
        }

        string datePrefix = createdAt.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string sanitizedId = _pathResolver.SanitizePathSegment(recordingId);
        string sanitizedSlug = _pathResolver.SanitizePathSegment(titleSlug);

        string baseFileName = string.IsNullOrWhiteSpace(sanitizedSlug)
            ? $"{datePrefix}_{sanitizedId}"
            : $"{datePrefix}_{sanitizedId}_{sanitizedSlug}";

        string candidateRelativePath = $"{relativeDirectory}/{baseFileName}.md";
        string candidatePlatformPath = _pathResolver.ResolvePlatformPath(candidateRelativePath);

        if (File.Exists(candidatePlatformPath))
        {
            for (int suffix = 1; ; suffix++)
            {
                string nextCandidate = $"{relativeDirectory}/{baseFileName}_{suffix}.md";
                string nextPlatformPath = _pathResolver.ResolvePlatformPath(nextCandidate);
                if (!File.Exists(nextPlatformPath))
                {
                    candidateRelativePath = nextCandidate;
                    candidatePlatformPath = nextPlatformPath;
                    break;
                }
            }
        }

        string? destinationDirectory = Path.GetDirectoryName(candidatePlatformPath);
        if (!string.IsNullOrEmpty(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        return candidateRelativePath;
    }
}
