using Atlas.Core.Contracts;

namespace Atlas.Core.Scanning;

/// <summary>
/// A single signal that contributed to a sensitivity classification.
/// </summary>
public sealed record SensitivityEvidence(string Signal, string Detail);

/// <summary>
/// The outcome of sensitivity classification: a level and the evidence that produced it.
/// </summary>
public sealed record SensitivityResult(
    SensitivityLevel Level,
    IReadOnlyList<SensitivityEvidence> Evidence);

/// <summary>
/// Bounded, evidence-friendly sensitivity scorer that combines path, filename,
/// extension, category, and MIME signals to classify file sensitivity.
/// All rules are evaluated and all matching evidence is collected;
/// the maximum severity level wins.
/// </summary>
public static class SensitivityScorer
{
    private static readonly HashSet<string> CriticalExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".kdbx",      // KeePass 2.x database
        ".kdb",       // KeePass 1.x database
        ".pfx",       // PKCS#12 certificate bundle
        ".p12",       // PKCS#12 alternate extension
        ".pem",       // PEM-encoded key/certificate
        ".key",       // Private key file
        ".jks",       // Java KeyStore
        ".keystore",  // Android/generic keystore
        ".ppk",       // PuTTY private key
    };

    private static readonly string[] SensitivePathSegments =
    [
        "finance",
        "legal",
        "medical",
        "payroll",
        "identity",
        "personnel",
        "insurance",
        "accounting",
        "confidential",
    ];

    private static readonly string[] SensitiveFilenameTerms =
    [
        "w-2",
        "w2",
        "1099",
        "ssn",
        "paystub",
        "pay-stub",
        "pay_stub",
        "bank-statement",
        "bank_statement",
        "tax-return",
        "tax_return",
        "nda",
        "deed",
        "mortgage",
    ];

    private static readonly HashSet<string> DocumentCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Documents",
        "Spreadsheets",
        "Presentations",
    };

    private static readonly HashSet<string> DatabaseExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".db",
        ".sqlite",
        ".sqlite3",
        ".mdb",
        ".accdb",
    };

    public static SensitivityResult Classify(
        PolicyProfile profile,
        string fullPath,
        string fileName,
        string extension,
        string? category,
        string? mimeType)
    {
        var evidence = new List<SensitivityEvidence>();
        var maxLevel = SensitivityLevel.Low;

        // 1. Critical: credential/key material by extension
        if (!string.IsNullOrEmpty(extension) && CriticalExtensions.Contains(extension))
        {
            evidence.Add(new SensitivityEvidence("CriticalExtension", extension));
            maxLevel = SensitivityLevel.Critical;
        }

        // 2a. High: policy-configured protected keywords in full path
        if (!string.IsNullOrEmpty(fullPath))
        {
            foreach (var keyword in profile.ProtectedKeywords)
            {
                if (fullPath.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    evidence.Add(new SensitivityEvidence("PolicyKeyword", keyword));
                    if (maxLevel < SensitivityLevel.High)
                        maxLevel = SensitivityLevel.High;
                }
            }
        }

        // 2b. High: built-in sensitive path segments
        if (!string.IsNullOrEmpty(fullPath))
        {
            foreach (var segment in SensitivePathSegments)
            {
                if (fullPath.Contains(segment, StringComparison.OrdinalIgnoreCase))
                {
                    evidence.Add(new SensitivityEvidence("SensitivePathSegment", segment));
                    if (maxLevel < SensitivityLevel.High)
                        maxLevel = SensitivityLevel.High;
                }
            }
        }

        // 2c. High: sensitive filename terms
        if (!string.IsNullOrEmpty(fileName))
        {
            var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            foreach (var term in SensitiveFilenameTerms)
            {
                if (nameWithoutExt.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    evidence.Add(new SensitivityEvidence("SensitiveFilenameTerm", term));
                    if (maxLevel < SensitivityLevel.High)
                        maxLevel = SensitivityLevel.High;
                }
            }
        }

        // 3a. Medium: document-family categories
        if (!string.IsNullOrEmpty(category) && DocumentCategories.Contains(category))
        {
            evidence.Add(new SensitivityEvidence("DocumentCategory", category));
            if (maxLevel < SensitivityLevel.Medium)
                maxLevel = SensitivityLevel.Medium;
        }

        // 3b. Medium: archives (can't see inside, conservative)
        if (string.Equals(category, "Archives", StringComparison.OrdinalIgnoreCase))
        {
            evidence.Add(new SensitivityEvidence("ArchiveCategory", "Archives"));
            if (maxLevel < SensitivityLevel.Medium)
                maxLevel = SensitivityLevel.Medium;
        }

        // 3c. Medium: database extensions
        if (!string.IsNullOrEmpty(extension) && DatabaseExtensions.Contains(extension))
        {
            evidence.Add(new SensitivityEvidence("DatabaseExtension", extension));
            if (maxLevel < SensitivityLevel.Medium)
                maxLevel = SensitivityLevel.Medium;
        }

        return new SensitivityResult(maxLevel, evidence);
    }
}
