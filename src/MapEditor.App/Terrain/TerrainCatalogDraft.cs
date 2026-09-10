using MapEditor.Core.Terrain;
using MapEditor.Rendering;
using MapEditor.Rendering.Terrain;

namespace MapEditor.App.Terrain;

internal sealed class TerrainCatalogDraft
{
    private readonly SpriteManifest _manifest;
    private readonly int _creatingThreadId;
    private readonly int _schemaVersion;
    private readonly string _generatorVersion;
    private readonly string _corpusFingerprint;
    private readonly TerrainGenerationSettings _settings;
    private readonly IReadOnlyList<TerrainDiagnostic> _diagnostics;
    private readonly List<SetState> _states;
    private string _baseline;
    private IReadOnlyList<TerrainSetDraft> _sets = Array.Empty<TerrainSetDraft>();
    private IReadOnlyList<TerrainValidationIssue> _issues = Array.Empty<TerrainValidationIssue>();

    internal event EventHandler? Changed;

    internal IReadOnlyList<TerrainSetDraft> Sets => _sets;
    internal IReadOnlyList<TerrainValidationIssue> Issues => _issues;
    internal bool IsDirty => !string.Equals(Snapshot(), _baseline, StringComparison.Ordinal);
    internal bool CanSave => IsDirty && Issues.Count == 0;

    internal TerrainCatalogDraft(TerrainCatalog source, SpriteManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(source);
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        _creatingThreadId = Environment.CurrentManagedThreadId;
        _schemaVersion = source.SchemaVersion;
        _generatorVersion = source.GeneratorVersion;
        _corpusFingerprint = source.CorpusFingerprint;
        _settings = Clone(source.Settings);
        _diagnostics = Array.AsReadOnly(source.Diagnostics.Select(Clone).ToArray());
        _states = source.Sets.Select((set, index) => new SetState(new TerrainDraftKey(index), Clone(set))).ToList();
        var enabledIdCounts = _states
            .Where(state => state.Definition.Status == TerrainReviewStatus.Enabled && !string.IsNullOrWhiteSpace(state.Definition.Id))
            .GroupBy(state => state.Definition.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        foreach (SetState state in _states)
            if (state.Definition.Status == TerrainReviewStatus.Enabled &&
                enabledIdCounts.TryGetValue(state.Definition.Id, out int count) && count == 1)
                state.OriginalPublishedId = state.Definition.Id;
        Recompute();
        _baseline = Snapshot();
    }

    internal void Rename(TerrainDraftKey key, string displayName)
    {
        EnsureCreatingThread();
        ArgumentNullException.ThrowIfNull(displayName);
        SetState state = Require(key);
        if (string.Equals(state.Definition.DisplayName, displayName, StringComparison.Ordinal))
            return;
        state.Definition = Copy(state.Definition, displayName: displayName);
        Accepted();
    }

    internal TerrainDraftMutationResult RegenerateId(TerrainDraftKey key)
    {
        EnsureCreatingThread();
        if (!TryGet(key, out SetState state))
            return Invalid(key, "draft-key-invalid", $"Terrain draft key {key.Value} does not exist.");
        if (!TryGeneratedId(state.Definition, out string generated))
            return Failure(key, TerrainCatalogValidator.Validate(Build()).Where(issue => issue.TerrainId == state.Definition.Id));
        return ReplaceIdentity(state, Copy(state.Definition, id: generated));
    }

    internal TerrainDraftMutationResult ChangeTopology(TerrainDraftKey key, TerrainTopology topology)
    {
        EnsureCreatingThread();
        if (!TryGet(key, out SetState state))
            return Invalid(key, "draft-key-invalid", $"Terrain draft key {key.Value} does not exist.");
        if (topology is not (TerrainTopology.FourWay or TerrainTopology.EightWay))
            return Invalid(key, "terrain-topology-invalid", $"Terrain topology value {(int)topology} is invalid.");
        if (state.Definition.Topology == topology)
            return Success(key);

        var available = state.Definition.Masks.Concat(state.Orphans).GroupBy(mask => mask.Mask).ToDictionary(group => group.Key, group => new Queue<TerrainMaskDefinition>(group.Select(Clone)));
        var masks = new List<TerrainMaskDefinition>();
        foreach (int mask in TerrainMasks.Required(topology))
        {
            masks.Add(available.TryGetValue(mask, out Queue<TerrainMaskDefinition>? rows) && rows.Count > 0
                ? rows.Dequeue()
                : new TerrainMaskDefinition(mask, Array.Empty<TerrainGraphicReference>()));
        }

        var orphans = new List<TerrainMaskDefinition>();
        foreach (TerrainMaskDefinition mask in state.Definition.Masks.Concat(state.Orphans))
        {
            if (!TerrainMasks.IsReachable(mask.Mask, topology))
                orphans.Add(Clone(mask));
        }

        foreach (Queue<TerrainMaskDefinition> rows in available.Values)
            while (rows.Count > 0)
            {
                TerrainMaskDefinition row = rows.Dequeue();
                if (!masks.Any(mask => ReferenceEquals(mask, row)) && TerrainMasks.IsReachable(row.Mask, topology))
                    orphans.Add(row);
            }

        TerrainSetDefinition staged = Copy(state.Definition, topology: topology, masks: masks);
        if (!TryGeneratedId(staged, out string id))
            return Failure(key, TerrainCatalogValidator.Validate(BuildReplacing(key, staged)).Where(issue => issue.TerrainId == staged.Id));
        return ReplaceIdentity(state, Copy(staged, id: id), orphans);
    }

    internal TerrainDraftMutationResult AddVariant(TerrainDraftKey key, int mask, TerrainGraphicReference reference)
    {
        EnsureCreatingThread();
        if (!TryGet(key, out SetState state))
            return Invalid(key, "draft-key-invalid", $"Terrain draft key {key.Value} does not exist.");
        if (state.Definition.Topology is not (TerrainTopology.FourWay or TerrainTopology.EightWay))
            return Invalid(key, "terrain-topology-invalid", $"Terrain topology value {(int)state.Definition.Topology} is invalid.");
        int rowIndex = FindMask(state.Definition.Masks, mask);
        var masks = state.Definition.Masks.Select(Clone).ToList();
        if (rowIndex < 0)
        {
            if (!TerrainMasks.Required(state.Definition.Topology).Contains(mask))
                return Invalid(key, "draft-mask-invalid", $"Terrain draft mask {mask} does not exist.");
            rowIndex = masks.Count;
            masks.Add(new TerrainMaskDefinition(mask, Array.Empty<TerrainGraphicReference>()));
        }
        TerrainMaskDefinition row = masks[rowIndex];
        masks[rowIndex] = new TerrainMaskDefinition(row.Mask, row.Variants.Append(reference));
        var members = state.Definition.Members.Select(Clone).ToList();
        bool identityChanged = members.All(member => member.Reference != reference);
        if (identityChanged)
            members.Add(new TerrainMemberDefinition(reference, TerrainMemberProvenance.ImageOnly));
        TerrainSetDefinition staged = Copy(state.Definition, masks: masks, members: members);
        if (!identityChanged)
            return Replace(state, staged);
        if (!TryGeneratedId(staged, out string id))
            return Failure(key, TerrainCatalogValidator.Validate(BuildReplacing(key, staged)).Where(issue => issue.TerrainId == staged.Id));
        return ReplaceIdentity(state, Copy(staged, id: id));
    }

    internal TerrainDraftMutationResult RemoveVariant(TerrainDraftKey key, int mask, int index)
    {
        EnsureCreatingThread();
        if (!TryGet(key, out SetState state))
            return Invalid(key, "draft-key-invalid", $"Terrain draft key {key.Value} does not exist.");
        int rowIndex = FindMask(state.Definition.Masks, mask);
        if (rowIndex < 0)
            return Invalid(key, "draft-mask-invalid", $"Terrain draft mask {mask} does not exist.");
        TerrainMaskDefinition row = state.Definition.Masks[rowIndex];
        if (index < 0 || index >= row.Variants.Count)
            return Invalid(key, "draft-variant-index-invalid", $"Terrain variant index {index} does not exist.");

        TerrainGraphicReference removed = row.Variants[index];
        var variants = row.Variants.ToList();
        variants.RemoveAt(index);
        var masks = state.Definition.Masks.Select(Clone).ToList();
        masks[rowIndex] = new TerrainMaskDefinition(mask, variants);
        var members = state.Definition.Members.Select(Clone).ToList();
        bool stillUsed = masks.Concat(state.Orphans).SelectMany(item => item.Variants).Contains(removed);
        int memberIndex = members.FindIndex(member => member.Reference == removed && member.Provenance == TerrainMemberProvenance.ImageOnly);
        bool identityChanged = !stillUsed && memberIndex >= 0;
        if (identityChanged)
            members.RemoveAt(memberIndex);
        TerrainSetDefinition staged = Copy(state.Definition, masks: masks, members: members);
        if (!identityChanged)
            return Replace(state, staged);
        if (members.Count == 0)
            return ReplaceIdentity(state, staged);
        if (!TryGeneratedId(staged, out string id))
            return Failure(key, TerrainCatalogValidator.Validate(BuildReplacing(key, staged)).Where(issue => issue.TerrainId == staged.Id));
        return ReplaceIdentity(state, Copy(staged, id: id));
    }

    internal TerrainDraftMutationResult ReorderVariant(TerrainDraftKey key, int mask, int fromIndex, int toIndex)
    {
        EnsureCreatingThread();
        if (!TryGet(key, out SetState state))
            return Invalid(key, "draft-key-invalid", $"Terrain draft key {key.Value} does not exist.");
        int rowIndex = FindMask(state.Definition.Masks, mask);
        if (rowIndex < 0)
            return Invalid(key, "draft-mask-invalid", $"Terrain draft mask {mask} does not exist.");
        TerrainMaskDefinition row = state.Definition.Masks[rowIndex];
        if (fromIndex < 0 || fromIndex >= row.Variants.Count || toIndex < 0 || toIndex >= row.Variants.Count)
            return Invalid(key, "draft-variant-index-invalid", "Terrain variant reorder index does not exist.");
        if (fromIndex == toIndex)
            return Success(key);
        var variants = row.Variants.ToList();
        TerrainGraphicReference moved = variants[fromIndex];
        variants.RemoveAt(fromIndex);
        variants.Insert(toIndex, moved);
        var masks = state.Definition.Masks.Select(Clone).ToList();
        masks[rowIndex] = new TerrainMaskDefinition(mask, variants);
        return Replace(state, Copy(state.Definition, masks: masks));
    }

    internal TerrainDraftMutationResult RemoveOrphanMask(TerrainDraftKey key, int mask)
    {
        EnsureCreatingThread();
        if (!TryGet(key, out SetState state))
            return Invalid(key, "draft-key-invalid", $"Terrain draft key {key.Value} does not exist.");
        int index = FindMask(state.Orphans, mask);
        if (index < 0)
            return Invalid(key, "draft-orphan-mask-invalid", $"Terrain orphan mask {mask} does not exist.");
        var orphans = state.Orphans.Select(Clone).ToList();
        orphans.RemoveAt(index);
        return RemoveOrphans(state, orphans);
    }

    internal TerrainDraftMutationResult RemoveAllOrphanMasks(TerrainDraftKey key)
    {
        EnsureCreatingThread();
        if (!TryGet(key, out SetState state))
            return Invalid(key, "draft-key-invalid", $"Terrain draft key {key.Value} does not exist.");
        if (state.Orphans.Count == 0)
            return Success(key);
        return RemoveOrphans(state, Array.Empty<TerrainMaskDefinition>());
    }

    internal bool TrySetStatus(TerrainDraftKey key, TerrainReviewStatus status, out IReadOnlyList<TerrainValidationIssue> issues)
    {
        EnsureCreatingThread();
        if (!TryGet(key, out SetState state))
        {
            issues = Invalid(key, "draft-key-invalid", $"Terrain draft key {key.Value} does not exist.").Issues;
            return false;
        }
        if (status is not (TerrainReviewStatus.Enabled or TerrainReviewStatus.Pending or TerrainReviewStatus.Disabled))
        {
            issues = Invalid(key, "terrain-status-invalid", $"Terrain review status value {(int)status} is invalid.").Issues;
            return false;
        }
        if (state.Definition.Status == status && status != TerrainReviewStatus.Enabled)
        {
            issues = Array.Empty<TerrainValidationIssue>();
            return true;
        }

        TerrainSetDefinition staged = Copy(state.Definition, status: status);
        if (status == TerrainReviewStatus.Enabled)
        {
            TerrainCatalog catalog = BuildReplacing(state.Key, staged);
            IReadOnlyList<TerrainValidationIssue> validation = WithOrphanIssues(TerrainAssetCatalog.Validate(catalog, _manifest).Issues);
            if (validation.Count > 0)
            {
                issues = Array.AsReadOnly(validation.ToArray());
                return false;
            }
        }

        state.Definition = staged;
        Accepted();
        issues = Array.Empty<TerrainValidationIssue>();
        return true;
    }

    internal TerrainCatalog Build() => new(
        _schemaVersion,
        _generatorVersion,
        _corpusFingerprint,
        Clone(_settings),
        _states.Select(state => Clone(state.Definition)),
        _diagnostics.Select(Clone));

    internal IReadOnlyDictionary<string, string> BuildPublishedIdRekeys()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (SetState state in _states)
        {
            if (state.OriginalPublishedId is null || state.Definition.Status != TerrainReviewStatus.Enabled || !TryGeneratedId(state.Definition, out string generated))
                continue;
            result[state.OriginalPublishedId] = generated;
        }
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(result);
    }

    internal TerrainDraftAcceptance PlanAcceptChanges()
    {
        var counts = _states
            .Where(state => state.Definition.Status == TerrainReviewStatus.Enabled && !string.IsNullOrWhiteSpace(state.Definition.Id))
            .GroupBy(state => state.Definition.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        string?[] publishedIds = _states.Select(state => state.Definition.Status == TerrainReviewStatus.Enabled &&
            counts.TryGetValue(state.Definition.Id, out int count) && count == 1 ? state.Definition.Id : null).ToArray();
        return new TerrainDraftAcceptance(Snapshot(), publishedIds);
    }

    internal void CommitAcceptChanges(TerrainDraftAcceptance acceptance)
    {
        for (var i = 0; i < _states.Count; i++)
            _states[i].OriginalPublishedId = acceptance.PublishedIds[i];
        _baseline = acceptance.Baseline;
    }

    internal void AcceptChanges()
    {
        CommitAcceptChanges(PlanAcceptChanges());
        Recompute();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void EnsureCreatingThread()
    {
        if (Environment.CurrentManagedThreadId != _creatingThreadId)
            throw new InvalidOperationException("Terrain manager operations must run on the creating thread.");
    }

    private TerrainDraftMutationResult RemoveOrphans(SetState state, IEnumerable<TerrainMaskDefinition> remainingOrphans)
    {
        List<TerrainMaskDefinition> orphans = remainingOrphans.Select(Clone).ToList();
        var used = new HashSet<TerrainGraphicReference>(state.Definition.Masks.Concat(orphans).SelectMany(mask => mask.Variants));
        List<TerrainMemberDefinition> members = state.Definition.Members
            .Where(member => member.Provenance != TerrainMemberProvenance.ImageOnly || used.Contains(member.Reference))
            .Select(Clone)
            .ToList();
        TerrainSetDefinition staged = Copy(state.Definition, members: members);
        if (members.Count == 0)
            return ReplaceWithOrphans(state, staged, orphans);
        if (!TryGeneratedId(staged, out string id))
            return Failure(state.Key, TerrainCatalogValidator.Validate(BuildReplacing(state.Key, staged)).Where(issue => issue.TerrainId == staged.Id));
        staged = Copy(staged, id: id);
        return id == state.Definition.Id ? ReplaceWithOrphans(state, staged, orphans) : ReplaceIdentity(state, staged, orphans);
    }

    private TerrainDraftMutationResult ReplaceWithOrphans(SetState state, TerrainSetDefinition staged, IEnumerable<TerrainMaskDefinition> orphans)
    {
        state.Definition = staged;
        state.Orphans = orphans.Select(Clone).ToList();
        Accepted();
        return Success(state.Key);
    }

    private TerrainDraftMutationResult ReplaceIdentity(SetState state, TerrainSetDefinition staged, IEnumerable<TerrainMaskDefinition>? orphans = null)
    {
        List<SetState> collisions = _states.Where(other => other != state && TryGeneratedId(other.Definition, out string id) && id == staged.Id).ToList();
        if (collisions.Count > 0)
        {
            TerrainSetDefinition[] definitions = _states.Select(item => item == state ? staged : collisions.Contains(item) ? Copy(item.Definition, id: staged.Id) : item.Definition).ToArray();
            TerrainCatalog collisionCatalog = NewCatalog(definitions);
            return Failure(state.Key, TerrainCatalogValidator.Validate(collisionCatalog).Where(issue => issue.Code == "terrain-id-duplicate" && issue.TerrainId == staged.Id));
        }
        state.Definition = staged;
        if (orphans is not null)
            state.Orphans = orphans.Select(Clone).ToList();
        Accepted();
        return Success(state.Key);
    }

    private TerrainDraftMutationResult Replace(SetState state, TerrainSetDefinition staged)
    {
        state.Definition = staged;
        Accepted();
        return Success(state.Key);
    }

    private void Accepted()
    {
        Recompute();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Recompute()
    {
        IReadOnlyList<TerrainValidationIssue> validation = WithOrphanIssues(TerrainAssetCatalog.Validate(Build(), _manifest).Issues);
        _issues = Array.AsReadOnly(validation.ToArray());
        _sets = Array.AsReadOnly(_states.Select(state =>
        {
            IReadOnlyList<TerrainValidationIssue> setIssues = validation.Where(issue => issue.TerrainId is null || issue.TerrainId == state.Definition.Id).ToArray();
            TerrainSetDefinition enabled = Copy(state.Definition, status: TerrainReviewStatus.Enabled);
            bool canEnable = WithOrphanIssues(TerrainAssetCatalog.Validate(BuildReplacing(state.Key, enabled), _manifest).Issues).Count == 0;
            return new TerrainSetDraft(state.Key, Clone(state.Definition), state.Orphans.Select(Clone), _manifest, setIssues, canEnable);
        }).ToArray());
    }

    private IReadOnlyList<TerrainValidationIssue> WithOrphanIssues(IEnumerable<TerrainValidationIssue> source)
    {
        var issues = source.ToList();
        foreach (SetState state in _states)
            foreach (TerrainMaskDefinition orphan in state.Orphans)
                issues.Add(new TerrainValidationIssue("mask-orphan", $"Terrain '{state.Definition.Id}' has draft orphan mask 0x{orphan.Mask:X2}.", state.Definition.Id, orphan.Mask));
        return Array.AsReadOnly(issues.ToArray());
    }

    private TerrainCatalog BuildReplacing(TerrainDraftKey key, TerrainSetDefinition definition) =>
        NewCatalog(_states.Select(state => state.Key == key ? definition : state.Definition));

    private TerrainCatalog NewCatalog(IEnumerable<TerrainSetDefinition> definitions) => new(
        _schemaVersion, _generatorVersion, _corpusFingerprint, Clone(_settings), definitions.Select(Clone), _diagnostics.Select(Clone));

    private string Snapshot()
    {
        string orphanText = string.Join("|", _states.SelectMany(state => state.Orphans.Select(mask =>
            $"{state.Key.Value}:{mask.Mask}:{string.Join(',', mask.Variants.Select(reference => $"{reference.Sheet}/{reference.Graphic}"))}")));
        return TerrainCatalogJson.Serialize(Build()) + orphanText;
    }

    private SetState Require(TerrainDraftKey key) => TryGet(key, out SetState state) ? state : throw new ArgumentOutOfRangeException(nameof(key));

    private bool TryGet(TerrainDraftKey key, out SetState state)
    {
        if (key.Value >= 0 && key.Value < _states.Count && _states[key.Value].Key == key)
        {
            state = _states[key.Value];
            return true;
        }
        state = null!;
        return false;
    }

    private static int FindMask(IReadOnlyList<TerrainMaskDefinition> masks, int mask)
    {
        for (var i = 0; i < masks.Count; i++)
            if (masks[i].Mask == mask)
                return i;
        return -1;
    }

    private static bool TryGeneratedId(TerrainSetDefinition definition, out string id)
    {
        try
        {
            id = TerrainGeneratedId.Create(definition.Topology, definition.Members.Select(member => member.Reference));
            return true;
        }
        catch (ArgumentException)
        {
            id = null!;
            return false;
        }
    }

    private static TerrainDraftMutationResult Success(TerrainDraftKey key) => new(true, key, Array.Empty<TerrainValidationIssue>());
    private static TerrainDraftMutationResult Failure(TerrainDraftKey key, IEnumerable<TerrainValidationIssue> issues) => new(false, key, issues);
    private static TerrainDraftMutationResult Invalid(TerrainDraftKey key, string code, string message) => Failure(key, new[] { new TerrainValidationIssue(code, message) });

    private static TerrainCatalog Clone(TerrainCatalog source) => new(source.SchemaVersion, source.GeneratorVersion, source.CorpusFingerprint, Clone(source.Settings), source.Sets.Select(Clone), source.Diagnostics.Select(Clone));
    private static TerrainGenerationSettings Clone(TerrainGenerationSettings value) => new(value.HoldoutModulo, value.MinimumMapSupport, value.MinimumRegionSupport, value.MinimumObservationSupport, value.MinimumDiagonalSupport, value.MinimumEightWayAccuracyGain, value.MapMemberCompatibility, value.ImageOnlyCompatibility, value.MinimumClassificationMargin, value.MaximumAmbiguity, value.EnabledConfidence, value.PendingConfidence);
    private static TerrainSetMetrics Clone(TerrainSetMetrics value) => new(value.MapSupport, value.RegionSupport, value.ObservationSupport, value.DiagonalSupport, value.MaskEntropy, value.Completeness, value.Ambiguity, value.VisualCompatibility, value.HoldoutAccuracy, value.EightWayAccuracyGain, value.Confidence);
    private static TerrainDiagnostic Clone(TerrainDiagnostic value) => new(value.Code, value.Message, value.Mask, value.Reference);
    private static TerrainMaskDefinition Clone(TerrainMaskDefinition value) => new(value.Mask, value.Variants.ToArray());
    private static TerrainMemberDefinition Clone(TerrainMemberDefinition value) => new(value.Reference, value.Provenance);
    private static TerrainSetDefinition Clone(TerrainSetDefinition value) => new(value.Id, value.DisplayName, value.Status, value.Topology, Clone(value.Metrics), value.Masks.Select(Clone), value.Members.Select(Clone), value.Diagnostics.Select(Clone));

    private static TerrainSetDefinition Copy(TerrainSetDefinition value, string? id = null, string? displayName = null, TerrainReviewStatus? status = null, TerrainTopology? topology = null, IEnumerable<TerrainMaskDefinition>? masks = null, IEnumerable<TerrainMemberDefinition>? members = null) =>
        new(id ?? value.Id, displayName ?? value.DisplayName, status ?? value.Status, topology ?? value.Topology, Clone(value.Metrics), masks ?? value.Masks.Select(Clone), members ?? value.Members.Select(Clone), value.Diagnostics.Select(Clone));

    private sealed class SetState
    {
        internal TerrainDraftKey Key { get; }
        internal TerrainSetDefinition Definition { get; set; }
        internal List<TerrainMaskDefinition> Orphans { get; set; } = new();
        internal string? OriginalPublishedId { get; set; }

        internal SetState(TerrainDraftKey key, TerrainSetDefinition definition)
        {
            Key = key;
            Definition = definition;
        }
    }
}

internal sealed record TerrainDraftAcceptance(string Baseline, string?[] PublishedIds);
