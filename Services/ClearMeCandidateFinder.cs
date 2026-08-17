using System.IO;
using System.Text.RegularExpressions;

namespace NexusProgrammer;

internal static partial class ClearMeCandidateFinder
{
    public static ClearMeCandidates Find(AppSettings settings, MeaFirmwareInfo info)
    {
        return new ClearMeCandidates(
            FindMeRegions(settings.MeRegionRoot, info),
            FindFitTools(settings.FitRoot, info));
    }

    private static List<string> FindMeRegions(string root, MeaFirmwareInfo info)
    {
        var targetVersion = !string.IsNullOrWhiteSpace(info.Version) ? info.Version : info.Fit;
        var target = MeaAnalyzer.VersionParts(targetVersion);
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root) || target.Major == 0)
        {
            return [];
        }

        var candidates = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(path => IsMeRegionFile(path) && Path.GetFileName(path).Contains($"{target.Major}.", StringComparison.OrdinalIgnoreCase))
            .Select(path => new { Path = path, Score = ScoreMeRegion(path, info, targetVersion, target.Major, target.Minor) })
            .Where(item => item.Score > 0)
            .Where(item => IsSameMinorAndNotOlder(item.Path, target))
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Path)
            .ToList();
        return candidates.Count > 0
            ? candidates
            : FindMeRegionsRelaxed(root, info, targetVersion, target.Major, target.Minor);
    }

    private static List<string> FindMeRegionsRelaxed(string root, MeaFirmwareInfo info, string targetVersion, int targetMajor, int targetMinor) =>
        Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(path => IsMeRegionFile(path))
            .Select(path => new { Path = path, Score = ScoreMeRegionRelaxed(path, info, targetVersion, targetMajor, targetMinor) })
            .Where(item => item.Score > 0)
            .Where(item => IsSameMinorAndNotOlder(item.Path, MeaAnalyzer.VersionParts(targetVersion)))
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Path)
            .ToList();

    private static List<string> FindFitTools(string root, MeaFirmwareInfo info)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return [];
        }

        var targetVersion = !string.IsNullOrWhiteSpace(info.Fit) ? info.Fit : info.Version;
        var target = MeaAnalyzer.VersionParts(targetVersion);
        var candidates = Directory.EnumerateFiles(root, "*.exe", SearchOption.AllDirectories)
            .Where(IsFitTool)
            .ToList();

        if (target.Major > 0)
        {
            var sameMajor = candidates
                .Where(path => FitVersionParts(path).Major == target.Major ||
                               path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                   .Any(part => part.Equals(target.Major.ToString(), StringComparison.OrdinalIgnoreCase) ||
                                                part.StartsWith($"{target.Major}.", StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (sameMajor.Count > 0)
            {
                candidates = sameMajor;
            }
        }

        var ranked = candidates
            .Select(path => new { Path = path, Score = ScoreFit(path, targetVersion) })
            .Where(item => item.Score > 0)
            .Where(item => IsSameMajorAndNotOlderFit(item.Path, target))
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Path)
            .ToList();
        return ranked.Count > 0
            ? ranked
            : candidates
                .Where(path => IsSameMajorAndNotOlderFit(path, target))
                .OrderBy(path => Math.Abs(FitVersionRank(path) - MeaAnalyzer.VersionRank(targetVersion)))
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
    }

    private static double ScoreMeRegion(string path, MeaFirmwareInfo input, string targetVersion, int targetMajor, int targetMinor)
    {
        var candidate = ParseFilenameInfo(path);
        if (candidate.Major != targetMajor || candidate.Minor != targetMinor)
        {
            return 0;
        }

        var score = 150d;
        var inputSku = NormalizeSku(input.Sku);
        var candidateSku = NormalizeSku(candidate.Sku);
        if (string.IsNullOrWhiteSpace(inputSku) || !SkuMatches(inputSku, candidateSku))
        {
            return 0;
        }

        var (inputFamily, inputPlatform) = SkuKey(inputSku);
        var (candidateFamily, candidatePlatform) = SkuKey(candidateSku);
        if (!string.IsNullOrWhiteSpace(inputFamily) && candidateFamily == inputFamily)
        {
            score += 50;
        }

        if (!string.IsNullOrWhiteSpace(inputPlatform) && candidatePlatform == inputPlatform)
        {
            score += 45;
        }
        else if (!string.IsNullOrWhiteSpace(inputPlatform) && !string.IsNullOrWhiteSpace(candidatePlatform))
        {
            return 0;
        }

        var name = Path.GetFileName(path).ToLowerInvariant();
        if (name.Contains("rgn", StringComparison.Ordinal))
        {
            score += 80;
        }
        else if (name.Contains("extr", StringComparison.Ordinal))
        {
            score -= 120;
        }
        else
        {
            score += 35;
        }

        if (name.Contains("prd", StringComparison.Ordinal))
        {
            score += 25;
        }

        if (MeaAnalyzer.VersionParts(path) == MeaAnalyzer.VersionParts(targetVersion))
        {
            return score + 1000;
        }

        var distance = Math.Abs(MeaAnalyzer.VersionRank(path) - MeaAnalyzer.VersionRank(targetVersion));
        return score + Math.Max(0, 100 - distance / 10_000d);
    }

    private static double ScoreMeRegionRelaxed(string path, MeaFirmwareInfo input, string targetVersion, int targetMajor, int targetMinor)
    {
        var candidate = ParseFilenameInfo(path);
        if (candidate.Major != targetMajor || candidate.Minor != targetMinor)
        {
            return 0;
        }

        var score = 150d;
        var inputSku = NormalizeSku(input.Sku);
        var candidateSku = NormalizeSku(candidate.Sku);
        if (!string.IsNullOrWhiteSpace(inputSku) && !string.IsNullOrWhiteSpace(candidateSku))
        {
            var inputKey = SkuKey(inputSku);
            var candidateKey = SkuKey(candidateSku);
            if (!string.IsNullOrWhiteSpace(inputKey.Family) && inputKey.Family == candidateKey.Family)
            {
                score += 50;
            }

            if (!string.IsNullOrWhiteSpace(inputKey.Platform) && inputKey.Platform == candidateKey.Platform)
            {
                score += 45;
            }
        }

        var name = Path.GetFileName(path).ToLowerInvariant();
        if (name.Contains("rgn", StringComparison.Ordinal))
        {
            score += 80;
        }
        else if (name.Contains("extr", StringComparison.Ordinal))
        {
            score -= 40;
        }
        else
        {
            score += 35;
        }

        if (name.Contains("prd", StringComparison.Ordinal))
        {
            score += 25;
        }

        if (MeaAnalyzer.VersionParts(path) == MeaAnalyzer.VersionParts(targetVersion))
        {
            return score + 1000;
        }

        var distance = Math.Abs(MeaAnalyzer.VersionRank(path) - MeaAnalyzer.VersionRank(targetVersion));
        return score + Math.Max(0, 100 - distance / 10_000d);
    }

    private static double ScoreFit(string path, string targetVersion)
    {
        var candidate = FitVersionParts(path);
        var target = MeaAnalyzer.VersionParts(targetVersion);
        var score = 0d;
        if (target.Major > 0)
        {
            if (candidate.Major != target.Major)
            {
                return 0;
            }

            score += 150;
        }

        if (target.Minor > 0 && candidate.Minor == target.Minor)
        {
            score += 80;
        }

        if (FitVersionParts(path) == target)
        {
            return score + 500;
        }

        var distance = Math.Abs(FitVersionRank(path) - MeaAnalyzer.VersionRank(targetVersion));
        return score + Math.Max(0, 100 - distance / 10_000d);
    }

    private static MeaFirmwareInfo ParseFilenameInfo(string path)
    {
        var version = MeaAnalyzer.VersionParts(Path.GetFileName(path));
        return new MeaFirmwareInfo(
            Major: version.Major,
            Minor: version.Minor,
            Sku: SkuFromFilename(Path.GetFileName(path)));
    }

    private static string SkuFromFilename(string name)
    {
        var tokens = TokenRegex().Split(name.ToUpperInvariant()).Where(token => token.Length > 0).ToArray();
        var families = new Dictionary<string, string>
        {
            ["CON"] = "consumer",
            ["COR"] = "corporate",
            ["SLM"] = "slim"
        };
        var platforms = new Dictionary<string, string>
        {
            ["LP"] = "lp",
            ["H"] = "h",
            ["N"] = "n",
            ["P"] = "p"
        };

        for (var i = 0; i < tokens.Length; i++)
        {
            if (!families.TryGetValue(tokens[i], out var sku))
            {
                continue;
            }

            foreach (var token in tokens.Skip(i + 1).Take(4))
            {
                if (platforms.TryGetValue(token, out var platform))
                {
                    return $"{sku} {platform}";
                }
            }

            return sku;
        }

        return string.Empty;
    }

    private static string NormalizeSku(string value)
    {
        var text = NonSkuRegex().Replace(value.ToLowerInvariant(), " ").Trim();
        text = Regex.Replace(text, @"\bcon\b", "consumer");
        text = Regex.Replace(text, @"\bcor\b", "corporate");
        text = Regex.Replace(text, @"\bslm\b", "slim");
        text = Regex.Replace(text, @"\bnopdm\b|\bnpdm\b", "npdm");
        foreach (var (key, normalized) in new[]
        {
            ("consumer h d", "consumer h"),
            ("consumer lp c", "consumer lp"),
            ("corporate h d", "corporate h"),
            ("corporate lp c", "corporate lp"),
            ("consumer h", "consumer h"),
            ("consumer lp", "consumer lp"),
            ("consumer n", "consumer n"),
            ("consumer p", "consumer p"),
            ("corporate h", "corporate h"),
            ("corporate lp", "corporate lp"),
            ("corporate n", "corporate n"),
            ("corporate p", "corporate p"),
            ("slim h", "slim h"),
            ("slim lp", "slim lp"),
            ("slim n", "slim n"),
            ("slim p", "slim p"),
            ("consumer", "consumer"),
            ("corporate", "corporate"),
            ("slim", "slim")
        })
        {
            if (Regex.IsMatch(text, $@"(?<![a-z0-9.]){Regex.Escape(key)}(?![a-z0-9.])"))
            {
                return normalized;
            }
        }

        return text;
    }

    private static (string Family, string Platform) SkuKey(string value)
    {
        var normalized = NormalizeSku(value);
        var family = normalized.Contains("consumer", StringComparison.Ordinal) ? "consumer"
            : normalized.Contains("corporate", StringComparison.Ordinal) ? "corporate"
            : normalized.Contains("slim", StringComparison.Ordinal) ? "slim"
            : normalized.Contains("sps", StringComparison.Ordinal) ? "sps"
            : normalized.Contains("cstxe", StringComparison.Ordinal) ? "cstxe"
            : normalized.Contains("txe", StringComparison.Ordinal) ? "txe"
            : string.Empty;
        var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var platform = parts.Contains("lp") ? "lp"
            : parts.Contains("h") ? "h"
            : parts.Contains("n") ? "n"
            : parts.Contains("p") ? "p"
            : string.Empty;
        return (family, platform);
    }

    private static bool SkuMatches(string inputSku, string candidateSku)
    {
        var input = SkuKey(inputSku);
        var candidate = SkuKey(candidateSku);
        return !string.IsNullOrWhiteSpace(input.Family) &&
               !string.IsNullOrWhiteSpace(candidate.Family) &&
               input.Family == candidate.Family &&
               (string.IsNullOrWhiteSpace(input.Platform) ||
                string.IsNullOrWhiteSpace(candidate.Platform) ||
                input.Platform == candidate.Platform);
    }

    private static bool IsMeRegionFile(string path) =>
        string.Equals(Path.GetExtension(path), ".bin", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Path.GetExtension(path), ".rgn", StringComparison.OrdinalIgnoreCase);

    private static bool IsFitTool(string path)
    {
        var name = Path.GetFileName(path);
        return name.Equals("fitc.exe", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("fit.exe", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Flash Image Tool.exe", StringComparison.OrdinalIgnoreCase) ||
               Regex.IsMatch(name, @"^\d+\.\d+(?:\.\d+){0,2}\.exe$", RegexOptions.IgnoreCase) ||
               FitVersionParts(path).Major > 0;
    }

    private static (int Major, int Minor, int Hotfix, int Build) FitVersionParts(string path)
    {
        var fileVersion = MeaAnalyzer.VersionParts(Path.GetFileName(path));
        return fileVersion.Major > 0 ? fileVersion : MeaAnalyzer.VersionParts(path);
    }

    private static long FitVersionRank(string path)
    {
        var version = FitVersionParts(path);
        return VersionRank(version);
    }

    private static bool IsSameMinorAndNotOlder(string path, (int Major, int Minor, int Hotfix, int Build) target)
    {
        var candidate = MeaAnalyzer.VersionParts(Path.GetFileName(path));
        if (candidate.Major != target.Major || candidate.Minor != target.Minor)
        {
            return false;
        }

        return VersionRank(candidate) >= VersionRank(target);
    }

    private static bool IsSameMajorAndNotOlderFit(string path, (int Major, int Minor, int Hotfix, int Build) target)
    {
        var candidate = FitVersionParts(path);
        if (candidate.Major != target.Major)
        {
            return false;
        }

        return VersionRank(candidate) >= VersionRank(target);
    }

    private static long VersionRank((int Major, int Minor, int Hotfix, int Build) version) =>
        version.Major * 1_000_000_000L + version.Minor * 1_000_000L + version.Hotfix * 10_000L + version.Build;

    [GeneratedRegex(@"[^A-Z0-9]+", RegexOptions.IgnoreCase)]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"[^a-z0-9.]+")]
    private static partial Regex NonSkuRegex();
}

public sealed record ClearMeCandidates(
    IReadOnlyList<string> MeRegions,
    IReadOnlyList<string> FitTools,
    string AnalysisSummary = "");
