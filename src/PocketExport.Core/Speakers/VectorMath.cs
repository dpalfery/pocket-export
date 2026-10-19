namespace PocketExport.Core.Speakers;

/// <summary>
/// Provides high-performance vector mathematics for speaker biometric embeddings.
/// </summary>
public static class VectorMath
{
    /// <summary>
    /// Computes the cosine similarity between two vectors.
    /// </summary>
    /// <param name="a">First vector.</param>
    /// <param name="b">Second vector.</param>
    /// <returns>Cosine similarity value between -1.0 and 1.0.</returns>
    /// <exception cref="ArgumentNullException">Thrown if either vector is null.</exception>
    /// <exception cref="ArgumentException">Thrown if vectors have different dimensions.</exception>
    public static double CosineSimilarity(IReadOnlyList<float> a, IReadOnlyList<float> b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        if (a.Count != b.Count)
        {
            throw new ArgumentException($"Vectors must have the same length. Vector 'a' has length {a.Count}, but vector 'b' has length {b.Count}.", nameof(b));
        }

        if (a.Count == 0)
        {
            return 0.0;
        }

        double dotProduct = 0.0;
        double normA = 0.0;
        double normB = 0.0;

        for (var i = 0; i < a.Count; i++)
        {
            var valA = a[i];
            var valB = b[i];
            dotProduct += valA * valB;
            normA += valA * valA;
            normB += valB * valB;
        }

        if (normA <= 0.0 || normB <= 0.0)
        {
            return 0.0;
        }

        var similarity = dotProduct / (Math.Sqrt(normA) * Math.Sqrt(normB));
        return Math.Clamp(similarity, -1.0, 1.0);
    }

    /// <summary>
    /// Computes the cosine similarity between two float arrays.
    /// </summary>
    /// <param name="a">First vector array.</param>
    /// <param name="b">Second vector array.</param>
    /// <returns>Cosine similarity value between -1.0 and 1.0.</returns>
    public static double CosineSimilarity(float[] a, float[] b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        if (a.Length != b.Length)
        {
            throw new ArgumentException($"Vectors must have the same length. Vector 'a' has length {a.Length}, but vector 'b' has length {b.Length}.", nameof(b));
        }

        if (a.Length == 0)
        {
            return 0.0;
        }

        double dotProduct = 0.0;
        double normA = 0.0;
        double normB = 0.0;

        for (var i = 0; i < a.Length; i++)
        {
            var valA = a[i];
            var valB = b[i];
            dotProduct += valA * valB;
            normA += valA * valA;
            normB += valB * valB;
        }

        if (normA <= 0.0 || normB <= 0.0)
        {
            return 0.0;
        }

        var similarity = dotProduct / (Math.Sqrt(normA) * Math.Sqrt(normB));
        return Math.Clamp(similarity, -1.0, 1.0);
    }
}
