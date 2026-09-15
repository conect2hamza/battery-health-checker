using System.Diagnostics;
using BatteryHealthChecker.Collectors;
using BatteryHealthChecker.Models;

namespace BatteryHealthChecker.Services;

/// <summary>
/// Orchestrates the collectors and produces snapshots (SRS 4, 21).
///
/// Everything here runs off the UI thread: SRS 30 requires the interface never to
/// freeze during a hardware query, and a WMI call on a cold namespace can take the
/// better part of a second.
/// </summary>
public sealed class BatteryService
{
    private readonly IReadOnlyList<IBatteryDataCollector> _collectors;
    private readonly LogService _log;
    private readonly SemaphoreSlim _scanGate = new(1, 1);

    private BatterySnapshot? _lastFullSnapshot;

    public BatteryService(LogService log)
    {
        _log = log;
        // Ordered strongest-first for readability; the merge ranks by DataSource, not by
        // position, so this order only affects which collector runs first.
        _collectors = new IBatteryDataCollector[]
        {
            new IoctlBatteryCollector(),
            new WmiAcpiBatteryCollector(),
            new Win32BatteryCollector(),
            new SystemPowerStatusCollector(),
        };
    }

    /// <summary>Names of the configured sources, for the diagnostics section of a report.</summary>
    public IEnumerable<string> CollectorNames => _collectors.Select(c => c.Name);

    /// <summary>
    /// Runs a scan on a background thread.
    /// </summary>
    /// <param name="statusOnly">
    /// When true, only the fast status-capable collectors run and the static battery
    /// facts are carried over from the last full scan. This is what keeps the
    /// one-second auto-refresh option from costing real CPU (SRS 15, 24).
    /// </param>
    public Task<BatterySnapshot> ScanAsync(bool statusOnly, CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(statusOnly, cancellationToken), cancellationToken);

    private async Task<BatterySnapshot> Scan(bool statusOnly, CancellationToken cancellationToken)
    {
        await _scanGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // A status-only scan is meaningless before a full one has established what
            // the machine's batteries actually are.
            bool fast = statusOnly && _lastFullSnapshot is { HasBattery: true };

            var stopwatch = Stopwatch.StartNew();
            var results = new List<CollectionResult>();

            foreach (IBatteryDataCollector collector in _collectors)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (fast && !collector.SupportsStatusOnly) continue;

                try
                {
                    results.Add(collector.Collect(fast, cancellationToken));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // A collector is not allowed to throw, but if one does the scan must
                    // still complete using the remaining sources (SRS 19).
                    _log.Error($"Collector '{collector.Name}' failed.", ex);
                    var failure = new CollectionResult();
                    failure.Issues.Add(new CollectionIssue(
                        collector.Source, $"{collector.Name} could not be read.", false));
                    results.Add(failure);
                }
            }

            BatterySnapshot snapshot = BatteryRepository.Merge(results);

            if (fast)
            {
                snapshot = CarryOverStaticData(snapshot, _lastFullSnapshot!);
            }
            else if (snapshot.HasBattery)
            {
                _lastFullSnapshot = snapshot;
            }
            else
            {
                _lastFullSnapshot = null;
            }

            _log.Info($"Scan completed in {stopwatch.ElapsedMilliseconds} ms "
                      + $"({(fast ? "status" : "full")}, {snapshot.Batteries.Count} batteries).");

            return snapshot;
        }
        finally
        {
            _scanGate.Release();
        }
    }

    /// <summary>
    /// Re-attaches the static facts (identity, capacities, cycle count) from the last
    /// full scan to a status-only snapshot.
    ///
    /// If the set of batteries changed - a pack was removed or hot-swapped - the cached
    /// data no longer describes this hardware, so the cache is dropped and the caller
    /// gets the status-only result rather than stale numbers attributed to new hardware.
    /// </summary>
    private BatterySnapshot CarryOverStaticData(BatterySnapshot fresh, BatterySnapshot cached)
    {
        if (fresh.Batteries.Count != cached.Batteries.Count)
        {
            _lastFullSnapshot = null;
            return fresh;
        }

        var readings = new List<BatteryReading>(fresh.Batteries.Count);
        for (int i = 0; i < fresh.Batteries.Count; i++)
        {
            BatteryReading current = fresh.Batteries[i];
            BatteryReading previous = cached.Batteries[i];

            bool sameDevice =
                string.Equals(current.Info.DeviceKey, previous.Info.DeviceKey, StringComparison.OrdinalIgnoreCase)
                || (current.Info.UniqueId is not null
                    && string.Equals(current.Info.UniqueId, previous.Info.UniqueId, StringComparison.OrdinalIgnoreCase));

            if (!sameDevice)
            {
                _lastFullSnapshot = null;
                return fresh;
            }

            BatteryInfo info = current.Info;
            info.Name ??= previous.Info.Name;
            info.Manufacturer ??= previous.Info.Manufacturer;
            info.Model ??= previous.Info.Model;
            info.SerialNumber ??= previous.Info.SerialNumber;
            info.UniqueId ??= previous.Info.UniqueId;
            info.ChemistryRaw ??= previous.Info.ChemistryRaw;
            info.ManufactureDate ??= previous.Info.ManufactureDate;
            if (info.Chemistry == BatteryChemistry.Unknown) info.Chemistry = previous.Info.Chemistry;
            info.IsSystemBattery ??= previous.Info.IsSystemBattery;
            if (info.CapacityUnit == CapacityUnit.Unknown) info.CapacityUnit = previous.Info.CapacityUnit;

            info.DesignCapacity = info.DesignCapacity.Or(previous.Info.DesignCapacity);
            info.FullChargeCapacity = info.FullChargeCapacity.Or(previous.Info.FullChargeCapacity);
            info.CycleCount = info.CycleCount.Or(previous.Info.CycleCount);
            info.DesignVoltageMillivolts = info.DesignVoltageMillivolts.Or(previous.Info.DesignVoltageMillivolts);

            foreach (DataSource source in previous.Info.ContributingSources)
            {
                if (!info.ContributingSources.Contains(source)) info.ContributingSources.Add(source);
            }

            readings.Add(new BatteryReading
            {
                Info = info,
                Status = current.Status,
                Health = BatteryHealthCalculator.Calculate(info.DesignCapacity, info.FullChargeCapacity),
            });
        }

        return new BatterySnapshot
        {
            TimestampUtc = fresh.TimestampUtc,
            Batteries = readings,
            AcPower = fresh.AcPower,
            Issues = fresh.Issues,
        };
    }
}
