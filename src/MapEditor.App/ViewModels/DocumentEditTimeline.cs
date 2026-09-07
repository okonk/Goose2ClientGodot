using System;
using System.Collections.Generic;
using MapEditor.Core;
using MapEditor.GameData.Editing;

namespace MapEditor.App.ViewModels;

internal sealed class DocumentEditTimeline
{
    private readonly MapEditSession _map;
    private readonly SheetEditSession _sheet;
    private readonly DomainState _mapState = new();
    private readonly DomainState _sheetState = new();
    private readonly List<TimelineEntry> _undo = new();
    private readonly List<TimelineEntry> _redo = new();
    private bool _recording = true;

    public DocumentEditTimeline(MapEditSession map, SheetEditSession sheet)
    {
        _map = map ?? throw new ArgumentNullException(nameof(map));
        _sheet = sheet ?? throw new ArgumentNullException(nameof(sheet));
        _map.HistoryChanged += OnMapHistoryChanged;
        _sheet.HistoryChanged += OnSheetHistoryChanged;
    }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public void Detach()
    {
        _map.HistoryChanged -= OnMapHistoryChanged;
        _sheet.HistoryChanged -= OnSheetHistoryChanged;
    }

    public bool Undo()
    {
        if (_undo.Count == 0)
        {
            return false;
        }

        TimelineEntry entry = _undo[^1];
        if (entry.IsMap && !Aligned(_mapState, entry.MapTouchedVersion, entry.MapTouchedInBand))
        {
            return false;
        }

        if (entry.IsSheet && !Aligned(_sheetState, entry.SheetTouchedVersion, entry.SheetTouchedInBand))
        {
            return false;
        }

        _undo.RemoveAt(_undo.Count - 1);
        bool sheetReplayed = false;
        bool mapReplayed = false;
        WithRecordingSuppressed(() =>
        {
            if (entry.IsSheet)
            {
                sheetReplayed = _sheet.Undo();
            }

            if (entry.IsMap && (!entry.IsSheet || sheetReplayed))
            {
                mapReplayed = _map.Undo();
            }
        });

        // Cap eviction drops map commands without bumping HistoryVersion, so the
        // alignment check above can miss it; a failed replay means the entry is stale.
        if ((entry.IsSheet && !sheetReplayed) || (entry.IsMap && !mapReplayed))
        {
            WithRecordingSuppressed(() =>
            {
                if (mapReplayed)
                {
                    _map.Redo();
                }

                if (sheetReplayed)
                {
                    _sheet.Redo();
                }
            });
            // The replay and the rollback are each in-band history steps.
            if (mapReplayed)
            {
                _mapState.InBandVersion += 2;
            }

            if (sheetReplayed)
            {
                _sheetState.InBandVersion += 2;
            }
            return false;
        }

        if (entry.IsSheet)
        {
            _sheetState.InBandVersion++;
        }

        if (entry.IsMap)
        {
            _mapState.InBandVersion++;
        }

        Touch(entry);
        _redo.Add(entry);
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0)
        {
            return false;
        }

        TimelineEntry entry = _redo[^1];
        if (entry.IsMap && !Aligned(_mapState, entry.MapTouchedVersion, entry.MapTouchedInBand))
        {
            return false;
        }

        if (entry.IsSheet && !Aligned(_sheetState, entry.SheetTouchedVersion, entry.SheetTouchedInBand))
        {
            return false;
        }

        _redo.RemoveAt(_redo.Count - 1);
        bool mapReplayed = false;
        bool sheetReplayed = false;
        WithRecordingSuppressed(() =>
        {
            if (entry.IsMap)
            {
                mapReplayed = _map.Redo();
            }

            if (entry.IsSheet && (!entry.IsMap || mapReplayed))
            {
                sheetReplayed = _sheet.Redo();
            }
        });

        if ((entry.IsMap && !mapReplayed) || (entry.IsSheet && !sheetReplayed))
        {
            WithRecordingSuppressed(() =>
            {
                if (sheetReplayed)
                {
                    _sheet.Undo();
                }

                if (mapReplayed)
                {
                    _map.Undo();
                }
            });
            // The replay and the rollback are each in-band history steps.
            if (mapReplayed)
            {
                _mapState.InBandVersion += 2;
            }

            if (sheetReplayed)
            {
                _sheetState.InBandVersion += 2;
            }
            return false;
        }

        if (entry.IsMap)
        {
            _mapState.InBandVersion++;
        }

        if (entry.IsSheet)
        {
            _sheetState.InBandVersion++;
        }

        Touch(entry);
        _undo.Add(entry);
        return true;
    }

    public bool ApplyCompound(Func<MapEditSession, bool> mapOperation, Func<SheetEditSession, bool> sheetOperation)
    {
        if (mapOperation is null)
        {
            throw new ArgumentNullException(nameof(mapOperation));
        }

        if (sheetOperation is null)
        {
            throw new ArgumentNullException(nameof(sheetOperation));
        }

        long mapVersionBefore = _map.HistoryVersion;
        long sheetVersionBefore = _sheet.HistoryVersion;
        bool mapApplied = false;
        bool sheetApplied = false;
        WithRecordingSuppressed(() =>
        {
            mapApplied = mapOperation(_map);
            sheetApplied = mapApplied && sheetOperation(_sheet);
        });
        long mapDelta = _map.HistoryVersion - mapVersionBefore;
        long sheetDelta = _sheet.HistoryVersion - sheetVersionBefore;
        // A truthful operation pushes exactly one command per applied domain;
        // anything else would desync the rollback and the entry bookkeeping.
        if (mapDelta != (mapApplied ? 1 : 0) || sheetDelta != (sheetApplied ? 1 : 0))
        {
            throw new InvalidOperationException(
                "ApplyCompound operation violated the history contract: expected exactly one history push per applied domain.");
        }

        if (!mapApplied || !sheetApplied)
        {
            if (mapDelta > 0 || sheetDelta > 0)
            {
                WithRecordingSuppressed(() =>
                {
                    for (long i = 0; i < sheetDelta; i++)
                    {
                        _sheet.Undo();
                    }

                    for (long i = 0; i < mapDelta; i++)
                    {
                        _map.Undo();
                    }
                });
                // The op's push and the rollback undo are each in-band history steps.
                _sheetState.InBandVersion += sheetDelta * 2;
                _mapState.InBandVersion += mapDelta * 2;
                RemoveRedoEntries(mapDelta > 0, sheetDelta > 0);
            }

            return false;
        }

        _mapState.InBandVersion++;
        _sheetState.InBandVersion++;
        long mapVersion = _map.HistoryVersion;
        long sheetVersion = _sheet.HistoryVersion;
        WithRecordingSuppressed(() =>
        {
            _map.DiscardRedo();
            _sheet.DiscardRedo();
        });
        if (_map.HistoryVersion != mapVersion)
        {
            _mapState.InBandVersion++;
        }

        if (_sheet.HistoryVersion != sheetVersion)
        {
            _sheetState.InBandVersion++;
        }

        var entry = new TimelineEntry { IsMap = true, IsSheet = true };
        Touch(entry);
        _undo.Add(entry);
        _redo.Clear();
        return true;
    }

    internal void WithRecordingSuppressed(Action action)
    {
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        bool previous = _recording;
        _recording = false;
        try
        {
            action();
        }
        finally
        {
            _recording = previous;
        }
    }

    private void OnMapHistoryChanged()
    {
        _mapState.CurrentVersion = _map.HistoryVersion;
        if (!_recording)
        {
            return;
        }

        _mapState.InBandVersion++;
        long sheetVersion = _sheet.HistoryVersion;
        WithRecordingSuppressed(() => _sheet.DiscardRedo());
        if (_sheet.HistoryVersion != sheetVersion)
        {
            _sheetState.InBandVersion++;
        }

        var entry = new TimelineEntry { IsMap = true };
        Touch(entry);
        _undo.Add(entry);
        _redo.Clear();
    }

    private void OnSheetHistoryChanged()
    {
        _sheetState.CurrentVersion = _sheet.HistoryVersion;
        if (!_recording)
        {
            return;
        }

        _sheetState.InBandVersion++;
        long mapVersion = _map.HistoryVersion;
        WithRecordingSuppressed(() => _map.DiscardRedo());
        if (_map.HistoryVersion != mapVersion)
        {
            _mapState.InBandVersion++;
        }

        var entry = new TimelineEntry { IsSheet = true };
        Touch(entry);
        _undo.Add(entry);
        _redo.Clear();
    }

    private void RemoveRedoEntries(bool mapTouched, bool sheetTouched)
    {
        for (int i = _redo.Count - 1; i >= 0; i--)
        {
            TimelineEntry entry = _redo[i];
            if ((mapTouched && entry.IsMap) || (sheetTouched && entry.IsSheet))
            {
                _redo.RemoveAt(i);
            }
        }
    }

    private static bool Aligned(DomainState state, long touchedVersion, long touchedInBand)
        => state.CurrentVersion - touchedVersion == state.InBandVersion - touchedInBand;

    private void Touch(TimelineEntry entry)
    {
        if (entry.IsMap)
        {
            entry.MapTouchedVersion = _mapState.CurrentVersion;
            entry.MapTouchedInBand = _mapState.InBandVersion;
        }

        if (entry.IsSheet)
        {
            entry.SheetTouchedVersion = _sheetState.CurrentVersion;
            entry.SheetTouchedInBand = _sheetState.InBandVersion;
        }
    }

    private sealed class DomainState
    {
        public long CurrentVersion;

        public long InBandVersion;
    }

    private sealed class TimelineEntry
    {
        public bool IsMap;

        public bool IsSheet;

        public long MapTouchedVersion;

        public long MapTouchedInBand;

        public long SheetTouchedVersion;

        public long SheetTouchedInBand;
    }
}
