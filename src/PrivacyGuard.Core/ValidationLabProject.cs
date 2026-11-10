using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PrivacyGuard.Core;

/// <summary>R8.3: independent human ground truth, never the earlier masking approval state.</summary>
public enum LabMediaKind { Image, Video }
public enum LabVisualDomain { Unknown, Illustration2D, Model3D, Photograph }
public enum LabReviewState { Unreviewed, PartiallyReviewed, FullyReviewed, Excluded }
public enum LabTruthState { Unknown, Positive, Negative, Uncertain }
public enum LabProposalVerdict { Correct, FalsePositive, Uncertain }

public sealed record LabSource(
    Guid Id, string Path, string Sha256, LabMediaKind Kind, LabVisualDomain Domain,
    LabReviewState ReviewState, LabTruthState Truth, DateTimeOffset AddedAtUtc);

public sealed record LabBookmark(Guid Id, string PostUrl, string Note, DateTimeOffset AddedAtUtc);

/// <summary>A reviewer-created region, not a detector's output or independently proven model miss.</summary>
public sealed record LabTruthRegion(
    Guid Id, Guid SourceId, NormalizedRect Bounds, string Category,
    long? StartUs, long? EndUs, DateTimeOffset CreatedAtUtc);

/// <summary>A *human* verdict on one existing ONNX proposal; model scores remain uncalibrated.</summary>
public sealed record LabProposalAssessment(
    Guid Id, Guid SourceId, Guid CandidateId, string ModelId, string CandidateCategory,
    NormalizedRect Bounds, LabProposalVerdict Verdict, Guid? TruthRegionId,
    DateTimeOffset ReviewedAtUtc);

public sealed record LabUndoEntry(Guid SourceId, LabSource Previous, Guid? AddedRegionId,
    Guid? AddedAssessmentId = null);

public sealed class ValidationLabDocument
{
    public int SchemaVersion { get; set; } = 1;
    public List<LabSource> Sources { get; set; } = [];
    public List<LabBookmark> Bookmarks { get; set; } = [];
    public List<LabTruthRegion> Regions { get; set; } = [];
    public List<LabProposalAssessment> Assessments { get; set; } = [];
    public List<LabUndoEntry> UndoHistory { get; set; } = [];
}

/// <summary>
/// Local-only, explicitly rights-confirmed annotation file. Writes use a same-directory
/// temporary file followed by replace and a recoverable .bak copy. No uploads or telemetry.
/// </summary>
public sealed class ValidationLabStore
{
    private const int MaxJsonBytes = 8 * 1024 * 1024;
    private const int MaxEntries = 10_000;
    private readonly string _projectPath;
    private ValidationLabDocument _document;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false
    };

    public IReadOnlyList<LabSource> Sources => _document.Sources.AsReadOnly();
    public IReadOnlyList<LabBookmark> Bookmarks => _document.Bookmarks.AsReadOnly();
    public IReadOnlyList<LabTruthRegion> Regions => _document.Regions.AsReadOnly();
    public IReadOnlyList<LabProposalAssessment> Assessments => _document.Assessments.AsReadOnly();
    public bool CanUndo => _document.UndoHistory.Count > 0;
    public Guid? NextUndoSourceId => _document.UndoHistory.Count == 0 ? null : _document.UndoHistory[^1].SourceId;
    public string ProjectPath => _projectPath;

    private ValidationLabStore(string path, ValidationLabDocument document)
    {
        _projectPath = Path.GetFullPath(path);
        _document = document;
    }

    public static ValidationLabStore Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Project path required.", nameof(path));
        string full = Path.GetFullPath(path);
        if (!File.Exists(full)) return new ValidationLabStore(full, new ValidationLabDocument());
        if (new FileInfo(full).Length > MaxJsonBytes) throw new InvalidDataException("Project file too large.");
        var document = JsonSerializer.Deserialize<ValidationLabDocument>(File.ReadAllText(full), JsonOptions)
            ?? throw new InvalidDataException("Empty project document.");
        ValidateDocument(document);
        return new ValidationLabStore(full, document);
    }

    private static void ValidateDocument(ValidationLabDocument doc)
    {
        if (doc.SchemaVersion != 1 || doc.Sources is null || doc.Bookmarks is null ||
            doc.Regions is null || doc.Assessments is null || doc.UndoHistory is null)
            throw new InvalidDataException("Unsupported or malformed annotation project.");
        if (doc.Sources.Count > MaxEntries || doc.Bookmarks.Count > MaxEntries ||
            doc.Regions.Count > MaxEntries || doc.Assessments.Count > MaxEntries || doc.UndoHistory.Count > 25)
            throw new InvalidDataException("Annotation project capacity exceeded.");
        if (doc.Sources.Any(s => s.Id == Guid.Empty || !VideoReviewBinding.IsWellFormedSha256(s.Sha256) ||
            !Path.IsPathFullyQualified(s.Path) || !Enum.IsDefined(s.Kind) ||
            !Enum.IsDefined(s.Domain) || !Enum.IsDefined(s.ReviewState) || !Enum.IsDefined(s.Truth)) ||
            doc.Sources.Select(s => s.Id).Distinct().Count() != doc.Sources.Count)
            throw new InvalidDataException("Invalid source reference.");
        if (doc.Bookmarks.Any(b => b.Id == Guid.Empty ||
            !XSourceLinks.TryNormalizePostUrl(b.PostUrl, out var normal) ||
            normal != b.PostUrl || b.Note is null || b.Note.Length > 500) ||
            doc.Bookmarks.Select(b => b.Id).Distinct().Count() != doc.Bookmarks.Count)
            throw new InvalidDataException("Invalid post reference.");
        var sources = doc.Sources.ToDictionary(s => s.Id);
        if (doc.Regions.Select(r => r.Id).Distinct().Count() != doc.Regions.Count ||
            doc.Regions.Any(r => r.Id == Guid.Empty || !sources.TryGetValue(r.SourceId, out var source) ||
                !ValidBounds(r.Bounds) || string.IsNullOrWhiteSpace(r.Category) ||
                r.Category.Length > 80 || !ValidTime(source.Kind, r.StartUs, r.EndUs)))
            throw new InvalidDataException("Invalid annotation region.");
        if (doc.Assessments.Select(a => a.Id).Distinct().Count() != doc.Assessments.Count ||
            doc.Assessments.Any(a => a.Id == Guid.Empty || a.CandidateId == Guid.Empty ||
                !sources.ContainsKey(a.SourceId) || !Enum.IsDefined(a.Verdict) ||
                !ValidBounds(a.Bounds) || !ValidProposalText(a.ModelId, 160) ||
                !ValidProposalText(a.CandidateCategory, 120) ||
                (a.TruthRegionId is Guid regionId && !doc.Regions.Any(r =>
                    r.Id == regionId && r.SourceId == a.SourceId)) ||
                (a.Verdict == LabProposalVerdict.Correct) != (a.TruthRegionId is not null)))
            throw new InvalidDataException("Invalid proposal assessment.");
        if (doc.UndoHistory.Any(u => u.SourceId == Guid.Empty || u.Previous is null ||
            u.SourceId != u.Previous.Id || !sources.ContainsKey(u.SourceId)))
            throw new InvalidDataException("Invalid undo history.");
    }

    private static bool ValidProposalText(string? value, int max) =>
        value is { Length: > 0 } && value.Length <= max && !value.Any(char.IsControl);

    private static bool ValidBounds(NormalizedRect r) =>
        double.IsFinite(r.X) && double.IsFinite(r.Y) &&
        double.IsFinite(r.Width) && double.IsFinite(r.Height) &&
        r.X >= 0 && r.Y >= 0 && r.Width > 0 && r.Height > 0 &&
        r.X + r.Width <= 1.000000001 && r.Y + r.Height <= 1.000000001;

    private static bool ValidTime(LabMediaKind kind, long? start, long? end) =>
        kind == LabMediaKind.Image ? start is null && end is null :
            start is >= 0 && end >= 0 && end >= start;

    public async Task<LabSource> AddLocalSourceAsync(string path, LabMediaKind kind,
        LabVisualDomain domain, bool rightsConfirmed, CancellationToken cancellationToken = default)
    {
        if (!rightsConfirmed) throw new InvalidOperationException("The reviewer must confirm source rights.");
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(domain))
            throw new ArgumentOutOfRangeException(nameof(domain));
        if (_document.Sources.Count >= MaxEntries) throw new InvalidOperationException("Project source limit reached.");
        string full = Path.GetFullPath(path);
        await using var stream = File.OpenRead(full);
        string sha = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        var existing = _document.Sources.FirstOrDefault(s =>
            s.Path.Equals(full, StringComparison.OrdinalIgnoreCase) &&
            s.Sha256.Equals(sha, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;
        var item = new LabSource(Guid.NewGuid(), full, sha, kind, domain,
            LabReviewState.Unreviewed, LabTruthState.Unknown, DateTimeOffset.UtcNow);
        _document.Sources.Add(item);
        try { Save(); }
        catch { _document.Sources.Remove(item); throw; }
        return item;
    }

    public async Task<bool> IsSourceCurrentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var source = GetSource(id);
        if (!File.Exists(source.Path)) return false;
        await using var stream = File.OpenRead(source.Path);
        var current = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        return current.Equals(source.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Only an explicitly inspected IMAGE may be labeled fully negative.
    /// Video-wide negative claims require frame/time coverage and are intentionally disallowed.</summary>
    public void MarkImageNegative(Guid id, bool explicitlyInspected)
    {
        var source = GetSource(id);
        if (source.Kind != LabMediaKind.Image || !explicitlyInspected)
            throw new InvalidOperationException("Explicit full image inspection is required.");
        if (_document.Regions.Any(r => r.SourceId == id))
            throw new InvalidOperationException("A source with positive regions cannot be marked negative.");
        ChangeSource(source, source with
        {
            Truth = LabTruthState.Negative, ReviewState = LabReviewState.FullyReviewed
        }, null);
    }

    public void MarkUncertain(Guid id)
    {
        var source = GetSource(id);
        ChangeSource(source, source with
        {
            Truth = LabTruthState.Uncertain, ReviewState = LabReviewState.PartiallyReviewed
        }, null);
    }

    public LabTruthRegion AddPositiveRegion(Guid id, NormalizedRect bounds, string category,
        long? startUs = null, long? endUs = null)
    {
        var source = GetSource(id);
        if (!ValidBounds(bounds) || string.IsNullOrWhiteSpace(category) || category.Length > 80 ||
            category.Any(char.IsControl) || !ValidTime(source.Kind, startUs, endUs))
            throw new ArgumentException("Invalid region or source timestamps.");
        if (_document.Regions.Count >= MaxEntries)
            throw new InvalidOperationException("Project region limit reached.");
        var annotation = new LabTruthRegion(Guid.NewGuid(), id, bounds, category,
            startUs, endUs, DateTimeOffset.UtcNow);
        _document.Regions.Add(annotation);
        try
        {
            ChangeSource(source, source with
            {
                Truth = LabTruthState.Positive, ReviewState = LabReviewState.PartiallyReviewed
            }, annotation.Id);
        }
        catch { _document.Regions.Remove(annotation); throw; }
        return annotation;
    }

    /// <summary>Records a human verdict separate from export-mask approvals and
    /// the model's own output. Correct requires the reviewer to choose a truth label.
    /// False positive does NOT imply the whole image is negative.</summary>
    public LabProposalAssessment AssessProposal(Guid sourceId, Guid candidateId,
        string modelId, string candidateCategory, NormalizedRect bounds,
        LabProposalVerdict verdict, string? verifiedTruthCategory = null)
    {
        var source = GetSource(sourceId);
        if (source.Kind != LabMediaKind.Image || candidateId == Guid.Empty ||
            !Enum.IsDefined(verdict) || !ValidBounds(bounds) ||
            !ValidProposalText(modelId, 160) || !modelId.StartsWith("onnx:", StringComparison.Ordinal) ||
            !ValidProposalText(candidateCategory, 120))
            throw new ArgumentException("Only a valid image ONNX candidate can be reviewed.");
        if (verdict == LabProposalVerdict.Correct &&
            !ValidProposalText(verifiedTruthCategory, 80))
            throw new ArgumentException("A human-confirmed category is required.");
        if (_document.Assessments.Count >= MaxEntries ||
            _document.Regions.Count >= MaxEntries && verdict == LabProposalVerdict.Correct)
            throw new InvalidOperationException("Project assessment limit reached.");

        if (_document.Assessments.Any(a => a.SourceId == sourceId && a.CandidateId == candidateId))
            throw new InvalidOperationException("Undo the prior candidate verdict before reassessing.");

        var newRegion = verdict == LabProposalVerdict.Correct
            ? new LabTruthRegion(Guid.NewGuid(), sourceId, bounds, verifiedTruthCategory!,
                null, null, DateTimeOffset.UtcNow) : null;
        var entry = new LabProposalAssessment(Guid.NewGuid(), sourceId, candidateId,
            modelId, candidateCategory, bounds, verdict, newRegion?.Id, DateTimeOffset.UtcNow);
        var oldUndo = _document.UndoHistory.ToArray();
        var index = _document.Sources.FindIndex(s => s.Id == sourceId);
        var newState = verdict == LabProposalVerdict.Correct
            ? source with { Truth = LabTruthState.Positive, ReviewState = LabReviewState.PartiallyReviewed }
            : source.ReviewState == LabReviewState.FullyReviewed ? source
            : source with { ReviewState = LabReviewState.PartiallyReviewed };
        _document.Sources[index] = newState;
        if (newRegion is not null) _document.Regions.Add(newRegion);
        _document.Assessments.Add(entry);
        _document.UndoHistory.Add(new LabUndoEntry(sourceId, source, newRegion?.Id, entry.Id));
        if (_document.UndoHistory.Count > 25) _document.UndoHistory.RemoveAt(0);
        try { Save(); }
        catch
        {
            _document.Sources[index] = source;
            if (newRegion is not null) _document.Regions.Remove(newRegion);
            _document.Assessments.Remove(entry);
            _document.UndoHistory.Clear();
            _document.UndoHistory.AddRange(oldUndo);
            throw;
        }
        return entry;
    }

    /// <summary>Only explicitly inspected whole images, never videos, can be fully
    /// reviewed. With positive regions this is a positive image, not a negative.</summary>
    public void CompletePositiveImage(Guid sourceId, bool explicitlyInspected)
    {
        var source = GetSource(sourceId);
        if (!explicitlyInspected || source.Kind != LabMediaKind.Image ||
            source.Truth != LabTruthState.Positive ||
            !_document.Regions.Any(r => r.SourceId == sourceId))
            throw new InvalidOperationException("Review all of an annotated image first.");
        ChangeSource(source, source with { ReviewState = LabReviewState.FullyReviewed }, null);
    }

    public LabBookmark AddPostBookmark(string url, string note = "")
    {
        if (!XSourceLinks.TryNormalizePostUrl(url, out var canonical))
            throw new ArgumentException("Only public X post permalinks are accepted.", nameof(url));
        if (note is null || note.Length > 500 || _document.Bookmarks.Count >= MaxEntries)
            throw new ArgumentException("Invalid note or project bookmark limit.");
        var previous = _document.Bookmarks.FirstOrDefault(x => x.PostUrl == canonical);
        if (previous is not null) return previous;
        var item = new LabBookmark(Guid.NewGuid(), canonical, note, DateTimeOffset.UtcNow);
        _document.Bookmarks.Add(item);
        try { Save(); }
        catch { _document.Bookmarks.Remove(item); throw; }
        return item;
    }

    public bool UndoLastSourceEdit()
    {
        if (_document.UndoHistory.Count == 0) return false;
        var edit = _document.UndoHistory[^1];
        int index = _document.Sources.FindIndex(x => x.Id == edit.SourceId);
        if (index < 0) throw new InvalidDataException("Undo source missing.");
        var current = _document.Sources[index];
        _document.Sources[index] = edit.Previous;
        var removed = edit.AddedRegionId is Guid id
            ? _document.Regions.FirstOrDefault(x => x.Id == id) : null;
        if (removed is not null) _document.Regions.Remove(removed);
        var removedAssessment = edit.AddedAssessmentId is Guid assessmentId
            ? _document.Assessments.FirstOrDefault(a => a.Id == assessmentId) : null;
        if (removedAssessment is not null) _document.Assessments.Remove(removedAssessment);
        _document.UndoHistory.RemoveAt(_document.UndoHistory.Count - 1);
        try { Save(); }
        catch
        {
            _document.Sources[index] = current;
            if (removed is not null) _document.Regions.Add(removed);
            if (removedAssessment is not null) _document.Assessments.Add(removedAssessment);
            _document.UndoHistory.Add(edit);
            throw;
        }
        return true;
    }

    private LabSource GetSource(Guid id) =>
        _document.Sources.FirstOrDefault(s => s.Id == id)
        ?? throw new KeyNotFoundException("Unknown validation source.");

    private void ChangeSource(LabSource previous, LabSource next, Guid? addedRegionId)
    {
        int index = _document.Sources.FindIndex(s => s.Id == previous.Id);
        _document.Sources[index] = next;
        var previousUndo = _document.UndoHistory.ToArray();
        _document.UndoHistory.Add(new LabUndoEntry(previous.Id, previous, addedRegionId));
        if (_document.UndoHistory.Count > 25) _document.UndoHistory.RemoveAt(0);
        try { Save(); }
        catch
        {
            _document.Sources[index] = previous;
            _document.UndoHistory.Clear();
            _document.UndoHistory.AddRange(previousUndo);
            throw;
        }
    }

    public void Save()
    {
        ValidateDocument(_document);
        string dir = Path.GetDirectoryName(_projectPath)
            ?? throw new InvalidOperationException("Project path has no directory.");
        Directory.CreateDirectory(dir);
        string temporary = _projectPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(_document, JsonOptions);
            if (Encoding.UTF8.GetByteCount(json) > MaxJsonBytes)
                throw new InvalidOperationException("Project too large.");
            File.WriteAllText(temporary, json, new UTF8Encoding(false));
            if (File.Exists(_projectPath)) File.Copy(_projectPath, _projectPath + ".bak", overwrite: true);
            File.Move(temporary, _projectPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

/// <summary>Generates regular X browsing URLs, never Twitter API URLs or media downloads.</summary>
public static partial class XSourceLinks
{
    [GeneratedRegex(@"^[A-Za-z0-9_]{1,15}$", RegexOptions.CultureInvariant)]
    private static partial Regex AccountPattern();

    [GeneratedRegex(@"^/([A-Za-z0-9_]{1,15})/status/([0-9]{1,20})/?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex PostPattern();

    public static Uri SearchUrl(string term, string media = "all")
    {
        if (string.IsNullOrWhiteSpace(term) || term.Length > 160 || term.Any(char.IsControl))
            throw new ArgumentException("Search words must be 1–160 printable characters.");
        string filter = media switch
        {
            "all" => "",
            "images" => " has:images",
            "videos" => " has:videos",
            _ => throw new ArgumentOutOfRangeException(nameof(media))
        };
        return new Uri("https://x.com/search?q=" + Uri.EscapeDataString(term.Trim() + filter) +
            "&src=typed_query");
    }

    public static Uri UserUrl(string account)
    {
        string name = account.Trim().TrimStart('@');
        if (!AccountPattern().IsMatch(name)) throw new ArgumentException("Invalid X account.");
        return new Uri("https://x.com/" + name);
    }

    public static bool TryNormalizePostUrl(string? value, out string canonical)
    {
        canonical = "";
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 ||
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort ||
            uri.UserInfo.Length > 0 || (uri.Host != "x.com" && uri.Host != "twitter.com"))
            return false;
        var match = PostPattern().Match(uri.AbsolutePath);
        if (!match.Success) return false;
        canonical = "https://x.com/" + match.Groups[1].Value + "/status/" + match.Groups[2].Value;
        return true;
    }
}
