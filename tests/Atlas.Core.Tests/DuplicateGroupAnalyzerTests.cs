using Atlas.Core.Contracts;
using Atlas.Core.Scanning;

namespace Atlas.Core.Tests;

public sealed class DuplicateGroupAnalyzerTests
{
    // ── Confidence: full hash vs quick hash ──────────────────────────

    [Fact]
    public void Analyze_FullHashVerified_ReturnsHighMatchConfidence()
    {
        var members = TwoLowRiskMembers();
        var canonical = members[0];

        var result = DuplicateGroupAnalyzer.Analyze(members, isFullHashVerified: true, canonical);

        Assert.True(result.MatchConfidence >= 0.999);
    }

    [Fact]
    public void Analyze_QuickHashOnly_ReturnsLowerMatchConfidence()
    {
        var members = TwoLowRiskMembers();
        var canonical = members[0];

        var result = DuplicateGroupAnalyzer.Analyze(members, isFullHashVerified: false, canonical);

        Assert.True(result.MatchConfidence < 0.9);
        Assert.True(result.MatchConfidence >= 0.85);
    }

    // ── Fingerprint agreement boost ──────────────────────────────────

    [Fact]
    public void Analyze_FingerprintAgreement_BoostsMatchConfidence()
    {
        var members = new List<FileInventoryItem>
        {
            CreateItem(@"C:\A\file.pdf", contentFingerprint: "fp-same"),
            CreateItem(@"C:\B\file.pdf", contentFingerprint: "fp-same"),
        };

        var withFp = DuplicateGroupAnalyzer.Analyze(members, isFullHashVerified: true, members[0]);

        var membersNoFp = new List<FileInventoryItem>
        {
            CreateItem(@"C:\A\file.pdf", contentFingerprint: ""),
            CreateItem(@"C:\B\file.pdf", contentFingerprint: ""),
        };

        var withoutFp = DuplicateGroupAnalyzer.Analyze(membersNoFp, isFullHashVerified: true, membersNoFp[0]);

        Assert.True(withFp.MatchConfidence > withoutFp.MatchConfidence);
        Assert.Contains(withFp.Evidence, e => e.Signal == "FingerprintAgreement");
    }

    // ── Cleanup confidence: low-risk group ───────────────────────────

    [Fact]
    public void Analyze_LowRiskGroup_CleanupConfidenceEqualsMatch()
    {
        var members = TwoLowRiskMembers();
        var canonical = members[0];

        var result = DuplicateGroupAnalyzer.Analyze(members, isFullHashVerified: true, canonical);

        Assert.Equal(result.MatchConfidence, result.CleanupConfidence);
        Assert.False(result.HasSensitiveMembers);
        Assert.False(result.HasSyncManagedMembers);
        Assert.False(result.HasProtectedMembers);
    }

    // ── Cleanup confidence: sensitive members ────────────────────────

    [Fact]
    public void Analyze_SensitiveMember_ReducesCleanupConfidence()
    {
        var members = new List<FileInventoryItem>
        {
            CreateItem(@"C:\A\file.pdf", sensitivity: SensitivityLevel.High),
            CreateItem(@"C:\B\file.pdf", sensitivity: SensitivityLevel.Low),
        };

        var result = DuplicateGroupAnalyzer.Analyze(members, isFullHashVerified: true, members[0]);

        Assert.True(result.CleanupConfidence < result.MatchConfidence);
        Assert.True(result.HasSensitiveMembers);
        Assert.Equal(SensitivityLevel.High, result.MaxSensitivity);
        Assert.Contains(result.Evidence, e => e.Signal == "SensitiveMember");
    }

    [Fact]
    public void Analyze_CriticalSensitivity_AppliesLargerPenalty()
    {
        var membersHigh = new List<FileInventoryItem>
        {
            CreateItem(@"C:\A\file.pdf", sensitivity: SensitivityLevel.High),
            CreateItem(@"C:\B\file.pdf", sensitivity: SensitivityLevel.Low),
        };
        var resultHigh = DuplicateGroupAnalyzer.Analyze(membersHigh, isFullHashVerified: true, membersHigh[0]);

        var membersCritical = new List<FileInventoryItem>
        {
            CreateItem(@"C:\A\keys.kdbx", sensitivity: SensitivityLevel.Critical),
            CreateItem(@"C:\B\keys.kdbx", sensitivity: SensitivityLevel.Low),
        };
        var resultCritical = DuplicateGroupAnalyzer.Analyze(membersCritical, isFullHashVerified: true, membersCritical[0]);

        Assert.True(resultCritical.CleanupConfidence < resultHigh.CleanupConfidence);
        Assert.Contains(resultCritical.Evidence, e => e.Signal == "CriticalSensitivity");
    }

    // ── Cleanup confidence: sync-managed members ─────────────────────

    [Fact]
    public void Analyze_SyncManagedMember_ReducesCleanupConfidence()
    {
        var members = new List<FileInventoryItem>
        {
            CreateItem(@"C:\OneDrive\file.pdf", isSyncManaged: true),
            CreateItem(@"C:\Local\file.pdf"),
        };

        var result = DuplicateGroupAnalyzer.Analyze(members, isFullHashVerified: true, members[0]);

        Assert.True(result.CleanupConfidence < result.MatchConfidence);
        Assert.True(result.HasSyncManagedMembers);
        Assert.Contains(result.Evidence, e => e.Signal == "SyncManagedMember");
    }

    // ── Cleanup confidence: protected members ────────────────────────

    [Fact]
    public void Analyze_ProtectedMember_ReducesCleanupConfidence()
    {
        var members = new List<FileInventoryItem>
        {
            CreateItem(@"C:\Protected\file.pdf", isProtectedByUser: true),
            CreateItem(@"C:\Local\file.pdf"),
        };

        var result = DuplicateGroupAnalyzer.Analyze(members, isFullHashVerified: true, members[0]);

        Assert.True(result.CleanupConfidence < result.MatchConfidence);
        Assert.True(result.HasProtectedMembers);
        Assert.Contains(result.Evidence, e => e.Signal == "ProtectedMember");
    }

    // ── Cumulative risk penalties ────────────────────────────────────

    [Fact]
    public void Analyze_MultipleRiskFactors_AccumulatePenalties()
    {
        var members = new List<FileInventoryItem>
        {
            CreateItem(@"C:\OneDrive\Finance\tax.pdf",
                sensitivity: SensitivityLevel.High, isSyncManaged: true, isProtectedByUser: true),
            CreateItem(@"C:\Local\tax-copy.pdf"),
        };

        var result = DuplicateGroupAnalyzer.Analyze(members, isFullHashVerified: true, members[0]);

        // Match confidence unchanged
        Assert.True(result.MatchConfidence >= 0.999);
        // Cleanup confidence reduced by all three penalties
        Assert.True(result.CleanupConfidence < 0.80);
        Assert.True(result.HasSensitiveMembers);
        Assert.True(result.HasSyncManagedMembers);
        Assert.True(result.HasProtectedMembers);
    }

    [Fact]
    public void Analyze_CleanupConfidence_NeverGoesNegative()
    {
        // Stack as many penalties as possible
        var members = new List<FileInventoryItem>
        {
            CreateItem(@"C:\OneDrive\keys.kdbx",
                sensitivity: SensitivityLevel.Critical, isSyncManaged: true, isProtectedByUser: true),
            CreateItem(@"C:\Local\keys.kdbx",
                sensitivity: SensitivityLevel.Critical, isSyncManaged: true, isProtectedByUser: true),
        };

        var result = DuplicateGroupAnalyzer.Analyze(members, isFullHashVerified: false, members[0]);

        Assert.True(result.CleanupConfidence >= 0.0);
    }

    // ── Canonical reason ─────────────────────────────────────────────

    [Fact]
    public void Analyze_ProtectedCanonical_IncludesReasonUserProtected()
    {
        var members = new List<FileInventoryItem>
        {
            CreateItem(@"C:\Protected\file.pdf", isProtectedByUser: true),
            CreateItem(@"C:\Local\file.pdf"),
        };

        var result = DuplicateGroupAnalyzer.Analyze(members, isFullHashVerified: true, members[0]);

        Assert.Contains("user-protected", result.CanonicalReason);
    }

    [Fact]
    public void Analyze_SyncManagedCanonical_IncludesReasonSyncManaged()
    {
        var members = new List<FileInventoryItem>
        {
            CreateItem(@"C:\OneDrive\file.pdf", isSyncManaged: true),
            CreateItem(@"C:\Local\file.pdf"),
        };

        var result = DuplicateGroupAnalyzer.Analyze(members, isFullHashVerified: true, members[0]);

        Assert.Contains("sync-managed", result.CanonicalReason);
    }

    [Fact]
    public void Analyze_HighSensitivityCanonical_IncludesSensitivityReason()
    {
        var members = new List<FileInventoryItem>
        {
            CreateItem(@"C:\Finance\statement.pdf", sensitivity: SensitivityLevel.High),
            CreateItem(@"C:\Downloads\statement.pdf"),
        };

        var result = DuplicateGroupAnalyzer.Analyze(members, isFullHashVerified: true, members[0]);

        Assert.Contains("sensitivity", result.CanonicalReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyze_PreferredLocationCanonical_IncludesLocationReason()
    {
        var members = new List<FileInventoryItem>
        {
            CreateItem(@"C:\Users\Test\Documents\file.pdf"),
            CreateItem(@"C:\Users\Test\Downloads\file.pdf"),
        };

        var result = DuplicateGroupAnalyzer.Analyze(members, isFullHashVerified: true, members[0]);

        Assert.Contains("preferred location", result.CanonicalReason);
    }

    [Fact]
    public void Analyze_GenericCanonical_ReturnsFallbackReason()
    {
        var members = new List<FileInventoryItem>
        {
            CreateItem(@"C:\A\file.pdf"),
            CreateItem(@"C:\B\file.pdf"),
        };

        var result = DuplicateGroupAnalyzer.Analyze(members, isFullHashVerified: true, members[0]);

        Assert.False(string.IsNullOrWhiteSpace(result.CanonicalReason));
    }

    // ── Evidence collection ──────────────────────────────────────────

    [Fact]
    public void Analyze_FullHashGroup_AlwaysHasHashEvidence()
    {
        var members = TwoLowRiskMembers();

        var result = DuplicateGroupAnalyzer.Analyze(members, isFullHashVerified: true, members[0]);

        Assert.Contains(result.Evidence, e => e.Signal == "FullHashMatch");
    }

    [Fact]
    public void Analyze_QuickHashGroup_HasQuickHashEvidence()
    {
        var members = TwoLowRiskMembers();

        var result = DuplicateGroupAnalyzer.Analyze(members, isFullHashVerified: false, members[0]);

        Assert.Contains(result.Evidence, e => e.Signal == "QuickHashOnly");
    }

    [Fact]
    public void Analyze_SizeMatch_ReportedInEvidence()
    {
        var members = TwoLowRiskMembers();

        var result = DuplicateGroupAnalyzer.Analyze(members, isFullHashVerified: true, members[0]);

        Assert.Contains(result.Evidence, e => e.Signal == "SizeMatch");
    }

    // ── Edge cases ───────────────────────────────────────────────────

    [Fact]
    public void Analyze_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DuplicateGroupAnalyzer.Analyze(null!, isFullHashVerified: true, CreateItem(@"C:\A\file.pdf")));
    }

    [Fact]
    public void Analyze_ThrowsOnSingleMember()
    {
        var members = new List<FileInventoryItem> { CreateItem(@"C:\A\file.pdf") };

        Assert.Throws<ArgumentException>(() =>
            DuplicateGroupAnalyzer.Analyze(members, isFullHashVerified: true, members[0]));
    }

    [Fact]
    public void Analyze_ThreeMembers_WorksCorrectly()
    {
        var members = new List<FileInventoryItem>
        {
            CreateItem(@"C:\A\file.pdf"),
            CreateItem(@"C:\B\file.pdf"),
            CreateItem(@"C:\C\file.pdf"),
        };

        var result = DuplicateGroupAnalyzer.Analyze(members, isFullHashVerified: true, members[0]);

        Assert.True(result.MatchConfidence >= 0.999);
        Assert.False(result.HasSensitiveMembers);
    }

    [Fact]
    public void Analyze_MixedFingerprints_NoFingerprintBoost()
    {
        var members = new List<FileInventoryItem>
        {
            CreateItem(@"C:\A\file.pdf", contentFingerprint: "fp-a"),
            CreateItem(@"C:\B\file.pdf", contentFingerprint: "fp-b"),
        };

        var result = DuplicateGroupAnalyzer.Analyze(members, isFullHashVerified: true, members[0]);

        Assert.DoesNotContain(result.Evidence, e => e.Signal == "FingerprintAgreement");
    }

    [Fact]
    public void Analyze_PartialFingerprints_NoFingerprintBoost()
    {
        var members = new List<FileInventoryItem>
        {
            CreateItem(@"C:\A\file.pdf", contentFingerprint: "fp-same"),
            CreateItem(@"C:\B\file.pdf", contentFingerprint: ""),
        };

        var result = DuplicateGroupAnalyzer.Analyze(members, isFullHashVerified: true, members[0]);

        Assert.DoesNotContain(result.Evidence, e => e.Signal == "FingerprintAgreement");
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static List<FileInventoryItem> TwoLowRiskMembers() =>
    [
        CreateItem(@"C:\Users\Test\Documents\file.pdf"),
        CreateItem(@"C:\Users\Test\Downloads\file.pdf"),
    ];

    private static FileInventoryItem CreateItem(
        string path,
        SensitivityLevel sensitivity = SensitivityLevel.Low,
        bool isProtectedByUser = false,
        bool isSyncManaged = false,
        string contentFingerprint = "")
    {
        return new FileInventoryItem
        {
            Path = path,
            Name = Path.GetFileName(path),
            Extension = Path.GetExtension(path),
            Category = "Documents",
            MimeType = "application/pdf",
            ContentFingerprint = contentFingerprint,
            SizeBytes = 1024,
            LastModifiedUnixTimeSeconds = 100,
            Sensitivity = sensitivity,
            IsSyncManaged = isSyncManaged,
            IsProtectedByUser = isProtectedByUser,
            IsDuplicateCandidate = true
        };
    }
}
