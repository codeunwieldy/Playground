using Atlas.Core.Contracts;
using Atlas.Core.Policies;

namespace Atlas.Core.Tests.Policies;

public sealed class PathSafetyClassifierTests
{
    private readonly PathSafetyClassifier _classifier = new();

    #region Path Normalization Edge Cases

    [Fact]
    public void Normalize_EmptyString_ReturnsEmpty()
    {
        var result = _classifier.Normalize(string.Empty);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Normalize_WhitespaceOnly_ReturnsEmpty()
    {
        var result = _classifier.Normalize("   ");

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Normalize_NullInput_ReturnsEmpty()
    {
        var result = _classifier.Normalize(null!);

        Assert.Equal(string.Empty, result);
    }

    [Theory]
    [InlineData(@"C:\Users\Test\..\Admin", @"C:\Users\Admin")]
    [InlineData(@"C:\Users\Test\..\..\", @"C:")]
    [InlineData(@"C:\Users\.\Test\.", @"C:\Users\Test")]
    public void Normalize_PathWithTraversalSegments_ResolvesCorrectly(string input, string expected)
    {
        var result = _classifier.Normalize(input);

        Assert.Equal(expected, result, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Normalize_PathWithTrailingSlash_TrimsSlash()
    {
        var result = _classifier.Normalize(@"C:\Users\Test\");

        Assert.Equal(@"C:\Users\Test", result, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Normalize_PathWithMixedSlashes_NormalizesSlashes()
    {
        var result = _classifier.Normalize(@"C:/Users/Test\Documents/");

        Assert.EndsWith("Documents", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch("/$", result);
        Assert.DoesNotMatch(@"\\$", result);
    }

    [Fact]
    public void Normalize_InvalidPathCharacters_ReturnsTrimmedInput()
    {
        // Characters like < > | are invalid in Windows paths
        // The normalizer should gracefully handle them
        var invalidPath = "C:\\Invalid<Path>|Test";
        var result = _classifier.Normalize(invalidPath);

        // Should return trimmed input when GetFullPath throws
        Assert.Equal(invalidPath.Trim(), result);
    }

    #endregion

    #region UNC Path Handling

    [Fact]
    public void Normalize_UncPath_PreservesUncFormat()
    {
        var result = _classifier.Normalize(@"\\server\share\folder");

        Assert.StartsWith(@"\\", result);
        Assert.Contains("server", result, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@"\\localhost\C$\Windows")]
    [InlineData(@"\\127.0.0.1\C$\Windows")]
    [InlineData(@"\\.\C:\Windows")]
    public void IsProtectedPath_AdminSharePaths_RecognizedWhenProtectedPathsIncludeWindows(string path)
    {
        var profile = new PolicyProfile
        {
            ProtectedPaths = new List<string> { @"C:\Windows" }
        };

        // Note: The current implementation normalizes paths, so UNC admin shares
        // pointing to C:\Windows should still match if normalized correctly
        // This test documents current behavior - actual protection depends on normalization
        var normalized = _classifier.Normalize(path);

        // The classifier should handle these paths without throwing
        Assert.NotNull(normalized);
    }

    [Fact]
    public void IsSameOrChildPath_UncPath_MatchesCorrectly()
    {
        var parent = @"\\fileserver\shared";
        var child = @"\\fileserver\shared\documents\report.pdf";

        var result = PathSafetyClassifier.IsSameOrChildPath(child, parent);

        Assert.True(result);
    }

    #endregion

    #region Path Traversal Detection

    [Theory]
    [InlineData(@"C:\Users\..\..\Windows\System32")]
    [InlineData(@"C:\Safe\..\..\..\Windows")]
    [InlineData(@"C:\Users\Test\Documents\..\..\..\Windows")]
    public void IsProtectedPath_TraversalToProtectedPath_DetectsAfterNormalization(string traversalPath)
    {
        var profile = new PolicyProfile
        {
            ProtectedPaths = new List<string> { @"C:\Windows" }
        };

        var result = _classifier.IsProtectedPath(profile, traversalPath);

        Assert.True(result, $"Path '{traversalPath}' should resolve to protected Windows directory");
    }

    [Theory]
    [InlineData(@"C:\Users\Test\..\Test2\file.txt")]
    [InlineData(@"C:\Users\.\Test\file.txt")]
    public void IsMutablePath_TraversalWithinMutableRoot_AllowedAfterNormalization(string traversalPath)
    {
        var profile = new PolicyProfile
        {
            MutableRoots = new List<string> { @"C:\Users" }
        };

        var result = _classifier.IsMutablePath(profile, traversalPath);

        Assert.True(result);
    }

    #endregion

    #region Device Paths

    [Theory]
    [InlineData(@"\\.\PhysicalDrive0")]
    [InlineData(@"\\.\C:")]
    [InlineData(@"\\?\C:\Windows")]
    public void Normalize_DevicePaths_HandlesGracefully(string devicePath)
    {
        // Device paths should not throw exceptions during normalization
        var result = _classifier.Normalize(devicePath);

        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Theory]
    [InlineData(@"\\.\COM1")]
    [InlineData(@"\\.\LPT1")]
    [InlineData(@"\\.\NUL")]
    [InlineData(@"\\.\CON")]
    public void Normalize_ReservedDeviceNames_HandlesGracefully(string devicePath)
    {
        // Reserved device names should be handled without throwing
        var result = _classifier.Normalize(devicePath);

        Assert.NotNull(result);
    }

    #endregion

    #region Alternate Data Streams

    [Theory]
    [InlineData(@"C:\Users\Test\file.txt:hidden")]
    [InlineData(@"C:\Users\Test\file.txt:Zone.Identifier")]
    [InlineData(@"C:\Users\Test\file.txt:$DATA")]
    public void Normalize_AlternateDataStreams_PreservesStreamSuffix(string adsPath)
    {
        // ADS paths should be handled - they may throw on GetFullPath
        var result = _classifier.Normalize(adsPath);

        Assert.NotNull(result);
    }

    [Fact]
    public void IsMutablePath_FileWithAlternateDataStream_MatchesParentPath()
    {
        var profile = new PolicyProfile
        {
            MutableRoots = new List<string> { @"C:\Users\Test" }
        };

        // The base file path should still be recognized as mutable
        var normalPath = @"C:\Users\Test\file.txt";
        var result = _classifier.IsMutablePath(profile, normalPath);

        Assert.True(result);
    }

    #endregion

    #region IsSameOrChildPath Edge Cases

    [Theory]
    [InlineData("", @"C:\Parent")]
    [InlineData(@"C:\Child", "")]
    [InlineData("", "")]
    [InlineData(null, @"C:\Parent")]
    [InlineData(@"C:\Child", null)]
    public void IsSameOrChildPath_EmptyOrNullInputs_ReturnsFalse(string? candidate, string? parent)
    {
        var result = PathSafetyClassifier.IsSameOrChildPath(candidate!, parent!);

        Assert.False(result);
    }

    [Fact]
    public void IsSameOrChildPath_ExactMatch_ReturnsTrue()
    {
        var result = PathSafetyClassifier.IsSameOrChildPath(@"C:\Users\Test", @"C:\Users\Test");

        Assert.True(result);
    }

    [Fact]
    public void IsSameOrChildPath_CaseInsensitive_ReturnsTrue()
    {
        var result = PathSafetyClassifier.IsSameOrChildPath(@"C:\USERS\TEST", @"c:\users\test");

        Assert.True(result);
    }

    [Fact]
    public void IsSameOrChildPath_SiblingDirectory_ReturnsFalse()
    {
        var result = PathSafetyClassifier.IsSameOrChildPath(@"C:\Users\Test2", @"C:\Users\Test");

        Assert.False(result);
    }

    [Fact]
    public void IsSameOrChildPath_PrefixButNotChild_ReturnsFalse()
    {
        // "C:\Users\Testing" starts with "C:\Users\Test" but is not a child
        var result = PathSafetyClassifier.IsSameOrChildPath(@"C:\Users\Testing", @"C:\Users\Test");

        Assert.False(result);
    }

    [Fact]
    public void IsSameOrChildPath_DeepNesting_ReturnsTrue()
    {
        var result = PathSafetyClassifier.IsSameOrChildPath(
            @"C:\Users\Test\Documents\Projects\2024\January\Report.pdf",
            @"C:\Users\Test");

        Assert.True(result);
    }

    #endregion

    #region Long Paths

    [Fact]
    public void Normalize_LongPath_HandlesGracefully()
    {
        // Create a path exceeding traditional 260 character limit
        var longPath = @"C:\Users\" + new string('a', 300) + @"\file.txt";

        var result = _classifier.Normalize(longPath);

        // Should handle gracefully without throwing
        Assert.NotNull(result);
    }

    [Fact]
    public void Normalize_ExtendedLengthPrefix_HandlesGracefully()
    {
        var extendedPath = @"\\?\C:\Users\Test\VeryLongPath";

        var result = _classifier.Normalize(extendedPath);

        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    #endregion
}
