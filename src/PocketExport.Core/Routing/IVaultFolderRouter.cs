using PocketExport.Core.Models;

namespace PocketExport.Core.Routing;

/// <summary>
/// Defines the routing contract for determining destination vault paths for classified recordings.
/// </summary>
public interface IVaultFolderRouter
{
    /// <summary>
    /// Determines the vault-relative routed path for a recording based on classification confidence and taxonomy hierarchy,
    /// resolving file collisions monotonically and ensuring destination directories are created.
    /// </summary>
    /// <param name="classification">The classification result for the recording.</param>
    /// <param name="recordingId">The unique recording identifier.</param>
    /// <param name="createdAt">The recording creation timestamp.</param>
    /// <param name="titleSlug">The sanitized slug representing the recording title.</param>
    /// <returns>A POSIX relative path to the routed markdown document.</returns>
    string DetermineRoutedPath(
        ClassificationResult classification,
        string recordingId,
        DateTime createdAt,
        string titleSlug);

    /// <summary>
    /// Determines the vault-relative routed path for a recording based on classification confidence and taxonomy hierarchy,
    /// resolving file collisions monotonically and ensuring destination directories are created.
    /// </summary>
    /// <param name="classification">The classification result for the recording.</param>
    /// <param name="recordingId">The unique recording identifier.</param>
    /// <param name="createdAt">The recording creation timestamp offset.</param>
    /// <param name="titleSlug">The sanitized slug representing the recording title.</param>
    /// <returns>A POSIX relative path to the routed markdown document.</returns>
    string DetermineRoutedPath(
        ClassificationResult classification,
        string recordingId,
        DateTimeOffset createdAt,
        string titleSlug);
}
