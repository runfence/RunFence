namespace RunFence.Firewall.UI;

/// <summary>
/// Manages the data logic for the Localhost ports tab in <see cref="Forms.FirewallAllowlistDialog"/>:
/// port entry add/remove/validate and duplicate detection.
/// Event handlers in the dialog read UI state and delegate to public methods here.
/// </summary>
public class FirewallPortsTabHandler
{
    private readonly FirewallPortValidator _validator;
    private readonly List<string> _portEntries;
    private readonly List<string> _initialPortEntries;

    public FirewallPortsTabHandler(FirewallPortValidator validator, IReadOnlyList<string>? initialPorts)
    {
        _validator = validator;
        _portEntries = initialPorts?.ToList() ?? [];
        _initialPortEntries = _portEntries.ToList();
    }

    public IReadOnlyList<string> GetPortEntries() => _portEntries.AsReadOnly();

    public bool HasUnappliedChanges() =>
        _portEntries.Count != _initialPortEntries.Count ||
        _portEntries.Zip(_initialPortEntries).Any(p =>
            !string.Equals(p.First, p.Second, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Validates <paramref name="portOrRange"/>, checks limit and duplicates, adds the entry.
    /// Returns an <see cref="AddPortResult"/> describing the outcome.
    /// </summary>
    public AddPortResult AddPort(string portOrRange)
    {
        if (!_validator.CheckLimit(_portEntries.Count))
            return new AddPortResult(AddPortOutcome.LimitReached, null);

        var parsed = _validator.ParsePortOrRange(portOrRange);
        if (parsed == null)
            return new AddPortResult(AddPortOutcome.Invalid, null);

        var entry = parsed.Value.ToString();
        if (_validator.HasDuplicate(entry, _portEntries))
            return new AddPortResult(AddPortOutcome.Duplicate, null);

        _portEntries.Add(entry);
        return new AddPortResult(AddPortOutcome.Added, entry);
    }

    /// <summary>
    /// Removes the given port entry strings from the internal list.
    /// </summary>
    public void RemovePorts(IEnumerable<string> entriesToRemove)
    {
        foreach (var entry in entriesToRemove)
            _portEntries.Remove(entry);
    }

    /// <summary>
    /// Validates an in-place cell edit of a port entry.
    /// On success, updates the list entry at <paramref name="oldValue"/> to the normalized value.
    /// </summary>
    public EditPortResult ValidateEdit(string? oldValue, string newValue)
    {
        var parsed = _validator.ParsePortOrRange(newValue);
        if (parsed == null)
            return new EditPortResult(EditPortOutcome.Invalid, null);

        var normalized = parsed.Value.ToString();
        if (_validator.HasDuplicate(normalized, _portEntries, excluding: oldValue))
            return new EditPortResult(EditPortOutcome.Duplicate, null);

        var idx = oldValue != null ? _portEntries.IndexOf(oldValue) : -1;
        if (idx >= 0)
            _portEntries[idx] = normalized;

        return new EditPortResult(EditPortOutcome.Updated, normalized);
    }

    /// <summary>
    /// Attempts to parse and add a port from a <c>localhost:N</c> or <c>localhost:N-M</c> formatted
    /// import line. Returns the outcome and the normalized entry string when
    /// <see cref="ImportPortLineResult.Added"/>. The import caller handles batching.
    /// </summary>
    public (ImportPortLineResult Outcome, string? Entry) TryAddFromImportLine(string line)
    {
        var parsed = _validator.ParseLocalhostPort(line);
        if (parsed == null)
            return (ImportPortLineResult.NotAPort, null);

        var entry = parsed.Value.ToString();
        if (_validator.HasDuplicate(entry, _portEntries))
            return (ImportPortLineResult.Duplicate, null);

        if (!_validator.CheckLimit(_portEntries.Count))
            return (ImportPortLineResult.LimitReached, null);

        _portEntries.Add(entry);
        return (ImportPortLineResult.Added, entry);
    }

    /// <summary>
    /// Parses <paramref name="portLines"/> (pre-classified as <c>localhost:N</c> or <c>localhost:N-M</c> lines)
    /// and adds valid port entries to this handler's list.
    /// Returns a <see cref="PortImportResult"/> containing the newly added ports and whether the limit was reached.
    /// </summary>
    public PortImportResult ImportLines(IReadOnlyList<string> portLines)
    {
        var addedPorts = new List<string>();
        bool portLimitReached = false;

        foreach (var line in portLines)
        {
            var (outcome, entry) = TryAddFromImportLine(line.Trim());
            switch (outcome)
            {
                case ImportPortLineResult.Added:
                    addedPorts.Add(entry!);
                    break;
                case ImportPortLineResult.LimitReached:
                    portLimitReached = true;
                    break;
            }
        }

        return new PortImportResult(addedPorts, portLimitReached);
    }

    /// <summary>
    /// Resets the initial snapshot to the current state after a successful Apply.
    /// </summary>
    public void CommitApply()
    {
        _initialPortEntries.Clear();
        _initialPortEntries.AddRange(_portEntries);
    }
}

/// <summary>Outcome of a <see cref="FirewallPortsTabHandler.AddPort"/> call.</summary>
public enum AddPortOutcome { Added, Invalid, Duplicate, LimitReached }

/// <summary>Result of a <see cref="FirewallPortsTabHandler.AddPort"/> call.</summary>
public record AddPortResult(AddPortOutcome Outcome, string? Entry);

/// <summary>Outcome of a <see cref="FirewallPortsTabHandler.ValidateEdit"/> call.</summary>
public enum EditPortOutcome { Updated, Invalid, Duplicate }

/// <summary>Result of a <see cref="FirewallPortsTabHandler.ValidateEdit"/> call.</summary>
public record EditPortResult(EditPortOutcome Outcome, string? NormalizedValue);

/// <summary>Outcome of a single import line attempted as a port entry.</summary>
public enum ImportPortLineResult { NotAPort, Added, Duplicate, LimitReached }

/// <summary>Summarises the result of <see cref="FirewallPortsTabHandler.ImportLines"/>.</summary>
public record PortImportResult(
    IReadOnlyList<string> AddedPorts,
    bool PortLimitReached);
