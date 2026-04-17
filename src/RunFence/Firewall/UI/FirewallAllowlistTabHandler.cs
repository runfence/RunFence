using RunFence.Core.Models;

namespace RunFence.Firewall.UI;

/// <summary>
/// Manages the data logic for the Internet allowlist tab in <see cref="Forms.FirewallAllowlistDialog"/>:
/// entry add/remove/validate, import parsing, and domain resolution.
/// Event handlers in the dialog read UI state and delegate to public methods here.
/// </summary>
public class FirewallAllowlistTabHandler
{
    private readonly FirewallAllowlistValidator _validator;
    private readonly FirewallDomainResolver _domainResolver;
    private readonly List<FirewallAllowlistEntry> _entries;
    private readonly List<FirewallAllowlistEntry> _initialEntries;

    public bool IsResolvingDomains { get; private set; }

    public FirewallAllowlistTabHandler(
        FirewallAllowlistValidator validator,
        FirewallDomainResolver domainResolver,
        List<FirewallAllowlistEntry> initialEntries)
    {
        _validator = validator;
        _domainResolver = domainResolver;
        _entries = initialEntries.Select(e => new FirewallAllowlistEntry { Value = e.Value, IsDomain = e.IsDomain }).ToList();
        _initialEntries = initialEntries.Select(e => new FirewallAllowlistEntry { Value = e.Value, IsDomain = e.IsDomain }).ToList();
    }

    public IReadOnlyList<FirewallAllowlistEntry> GetEntries() => _entries.AsReadOnly();

    public bool HasUnappliedChanges() =>
        _entries.Count != _initialEntries.Count ||
        _entries.Zip(_initialEntries).Any(p => p.First.Value != p.Second.Value || p.First.IsDomain != p.Second.IsDomain);

    public bool HasDomainEntries() => _entries.Any(e => e.IsDomain);

    /// <summary>
    /// Validates <paramref name="value"/>, checks for duplicates, and adds the entry.
    /// Returns an <see cref="AddEntryResult"/> describing the outcome.
    /// When <see cref="AddEntryOutcome.LicenseLimitReached"/>, <see cref="AddEntryResult.LicenseLimitMessage"/>
    /// contains the displayable restriction message.
    /// </summary>
    public AddEntryResult AddEntry(string value)
    {
        if (!_validator.CheckLicenseLimit(_entries.Count))
            return new AddEntryResult(AddEntryOutcome.LicenseLimitReached, null, _validator.GetLicenseLimitMessage(_entries.Count));

        var entry = _validator.ValidateEntry(value);
        if (entry == null)
            return new AddEntryResult(AddEntryOutcome.Invalid, null, null);

        if (_validator.HasDuplicate(value, _entries))
            return new AddEntryResult(AddEntryOutcome.Duplicate, null, null);

        _entries.Add(entry);
        return new AddEntryResult(AddEntryOutcome.Added, entry, null);
    }

    /// <summary>
    /// Removes the given entries from the internal list.
    /// </summary>
    public void RemoveEntries(IEnumerable<FirewallAllowlistEntry> entriesToRemove)
    {
        foreach (var entry in entriesToRemove)
            _entries.Remove(entry);
    }

    /// <summary>
    /// Validates an in-place cell edit of an existing entry.
    /// On success, the entry's fields are updated in-place.
    /// </summary>
    public EditEntryResult ValidateEdit(FirewallAllowlistEntry entry, string newValue)
    {
        var updatedEntry = _validator.ValidateEntry(newValue);
        if (updatedEntry == null)
            return new EditEntryResult(EditEntryOutcome.Invalid, null);

        if (_validator.HasDuplicate(newValue, _entries.Where(en => en != entry)))
            return new EditEntryResult(EditEntryOutcome.Duplicate, null);

        entry.Value = newValue;
        entry.IsDomain = updatedEntry.IsDomain;
        return new EditEntryResult(EditEntryOutcome.Updated, updatedEntry);
    }

    /// <summary>
    /// Parses <paramref name="lines"/> (already filtered to allowlist entries only) and adds valid
    /// entries to this handler's list.
    /// Returns an <see cref="AllowlistImportResult"/> containing the newly added entries and limit info.
    /// Port lines should be handled separately via <see cref="FirewallPortsTabHandler"/>.
    /// </summary>
    public AllowlistImportResult ImportLines(IReadOnlyList<string> lines)
    {
        var addedEntries = new List<FirewallAllowlistEntry>();
        bool entryLimitReached = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var entry = _validator.ValidateEntry(line);
            if (entry == null)
                continue;
            if (_validator.HasDuplicate(line, _entries))
                continue;
            if (!_validator.CheckLicenseLimit(_entries.Count))
            {
                entryLimitReached = true;
                continue;
            }

            _entries.Add(entry);
            addedEntries.Add(entry);
        }

        return new AllowlistImportResult(
            addedEntries,
            entryLimitReached,
            entryLimitReached ? _validator.GetLicenseLimitMessage(_entries.Count) : null);
    }

    /// <summary>
    /// Resolves all domain entries. Sets <see cref="IsResolvingDomains"/> to true synchronously
    /// before yielding and back to false in the finally block.
    /// Throws on resolution failure — callers should catch and display the error message.
    /// </summary>
    public async Task<Dictionary<string, List<string>>> ResolveAllDomainsAsync()
    {
        IsResolvingDomains = true;
        try
        {
            return await _domainResolver.ResolveAllAsync(_entries);
        }
        finally
        {
            IsResolvingDomains = false;
        }
    }

    /// <summary>
    /// Resolves a single domain entry to its IP addresses.
    /// </summary>
    public Task<IReadOnlyList<string>> ResolveEntryAsync(FirewallAllowlistEntry entry)
        => _domainResolver.ResolveEntryAsync(entry);

    /// <summary>
    /// Adds entries from <paramref name="selected"/> that are not already in the list and within
    /// the license limit. Returns a <see cref="BlockedConnectionAddResult"/> with the newly added
    /// entries and the count of entries that were truncated due to the license limit.
    /// </summary>
    public BlockedConnectionAddResult AddEntriesFromBlockedConnections(
        IEnumerable<FirewallAllowlistEntry> selected)
    {
        var added = new List<FirewallAllowlistEntry>();
        int truncatedCount = 0;
        foreach (var entry in selected)
        {
            if (!_validator.CheckLicenseLimit(_entries.Count))
            {
                truncatedCount++;
                continue;
            }
            if (_validator.HasDuplicate(entry.Value, _entries))
                continue;
            _entries.Add(entry);
            added.Add(entry);
        }
        return new BlockedConnectionAddResult(added, truncatedCount);
    }

    /// <summary>
    /// Returns the license restriction message for the current entry count.
    /// Used to show the license limit message after blocked-connection additions are truncated.
    /// </summary>
    public string? GetLicenseLimitMessage() => _validator.GetLicenseLimitMessage(_entries.Count);

    /// <summary>
    /// Resets the initial snapshot to the current state after a successful Apply.
    /// </summary>
    public void CommitApply()
    {
        _initialEntries.Clear();
        _initialEntries.AddRange(_entries.Select(e => new FirewallAllowlistEntry { Value = e.Value, IsDomain = e.IsDomain }));
    }
}

/// <summary>Outcome of an <see cref="FirewallAllowlistTabHandler.AddEntry"/> call.</summary>
public enum AddEntryOutcome { Added, Invalid, Duplicate, LicenseLimitReached }

/// <summary>Result of an <see cref="FirewallAllowlistTabHandler.AddEntry"/> call.</summary>
public record AddEntryResult(AddEntryOutcome Outcome, FirewallAllowlistEntry? Entry, string? LicenseLimitMessage);

/// <summary>Outcome of an <see cref="FirewallAllowlistTabHandler.ValidateEdit"/> call.</summary>
public enum EditEntryOutcome { Updated, Invalid, Duplicate }

/// <summary>Result of a <see cref="FirewallAllowlistTabHandler.ValidateEdit"/> call.</summary>
public record EditEntryResult(EditEntryOutcome Outcome, FirewallAllowlistEntry? UpdatedEntry);

/// <summary>Summarises the result of <see cref="FirewallAllowlistTabHandler.ImportLines"/>.</summary>
public record AllowlistImportResult(
    IReadOnlyList<FirewallAllowlistEntry> AddedEntries,
    bool EntryLimitReached,
    string? LicenseLimitMessage);
