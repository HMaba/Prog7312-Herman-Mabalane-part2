using SmartXApi.Data;
using SmartXApi.Models;
using System.Diagnostics.CodeAnalysis;

namespace SmartXApi.Services
{
    public interface ITelemetryStreamProcessor
    {
        long CurrentVersion { get; }
        bool Ingest(TelemetryPacketBase packet);
        int ProcessPending(int maxBatch);
        void CheckDisconnected(TimeSpan timeout);
        Task ReloadAllAsync();
        Task ReloadDeviceAsync(int deviceId);
        void RemoveDevice(int deviceId);
        DashboardSnapshot GetDashboard(string user);
        InsightsResponse GetInsights(string user);
        List<DeviceSnapshot> FilterDevices(DeviceFilter filter);
        List<DeviceSnapshot> GetDevices(IEnumerable<int> deviceIds);
        DeviceSnapshot? GetDevice(int deviceId);
        List<SensorTimelineEntry> GetTimeline(int deviceId, int sensorId, int take);
        List<StreamAlert> GetRecentAlerts(int take);
        List<OverrideCommand> GetCommandHistory(int take);
        CommandOutcome ApplyOverride(CommandRequest request, string operatorName);
        CommandOutcome UndoLastOverride(string operatorName);
        void RecordSearch(string user, DeviceFilter filter);
    }

    public class TelemetryStreamProcessor : ITelemetryStreamProcessor
    {
        private readonly StreamDataStructures _data;
        private readonly PredictiveActionEngine _engine;
        private readonly IDataStore _store;
        private readonly ILogger<TelemetryStreamProcessor> _logger;

        public TelemetryStreamProcessor(
            StreamDataStructures data,
            PredictiveActionEngine engine,
            IDataStore store,
            ILogger<TelemetryStreamProcessor> logger)
        {
            _data = data;
            _engine = engine;
            _store = store;
            _logger = logger;
        }

        public long CurrentVersion => _data.Version;

        public static string NormalizeMac(string? mac) =>
            string.IsNullOrWhiteSpace(mac) ? string.Empty : mac.Trim().ToUpperInvariant().Replace('-', ':');

        public bool Ingest(TelemetryPacketBase packet)
        {
            lock (_data.SyncRoot)
            {
                if (!_data.TryGetDevice(packet.DeviceId, out var state))
                {
                    _data.UnknownDevicePackets++;
                    return false;
                }

                var receivedAt = DateTime.UtcNow;

                if (IsCritical(state, packet))
                {
                    ApplyReading(state, packet, receivedAt);
                    _data.CriticalReadingsBypassed++;
                    DrainCriticalAlerts();
                }
                else
                {
                    _data.TelemetryQueue.Enqueue(new TelemetryEnvelope
                    {
                        Packet = packet,
                        ReceivedAt = receivedAt,
                        Sequence = _data.NextSequence()
                    });
                }

                _data.Touch();
                return true;
            }
        }

        public int ProcessPending(int maxBatch)
        {
            lock (_data.SyncRoot)
            {
                DrainCriticalAlerts();

                var processed = 0;
                while (processed < maxBatch && _data.TelemetryQueue.TryDequeue(out var envelope))
                {
                    if (_data.TryGetDevice(envelope.Packet.DeviceId, out var state))
                    {
                        ApplyReading(state, envelope.Packet, envelope.ReceivedAt);
                    }

                    _data.ProcessedPackets++;
                    processed++;
                    DrainCriticalAlerts();
                }

                if (processed > 0)
                {
                    _data.Touch();
                }

                return processed;
            }
        }

        public void CheckDisconnected(TimeSpan timeout)
        {
            lock (_data.SyncRoot)
            {
                var now = DateTime.UtcNow;

                foreach (var state in _data.DeviceRegistry.Values)
                {
                    if (!state.LastSeenUtc.HasValue || now - state.LastSeenUtc.Value <= timeout)
                    {
                        continue;
                    }

                    if (!_data.DisconnectedNodes.Add(state.RegistryKey))
                    {
                        continue;
                    }

                    var silentFor = now - state.LastSeenUtc.Value;
                    var alert = new StreamAlert
                    {
                        Id = _data.NextSequence(),
                        Signature = PredictiveActionEngine.Signature(PredictiveActionEngine.NodeDisconnectedType, state.DeviceId, null),
                        AlertType = PredictiveActionEngine.NodeDisconnectedType,
                        Severity = AlertSeverity.High,
                        DeviceId = state.DeviceId,
                        DeviceIdentifier = state.DeviceIdentifier,
                        MacAddress = state.MacAddress,
                        Message = $"{state.DeviceIdentifier} has not reported for {(int)silentFor.TotalSeconds}s",
                        RaisedAt = now
                    };

                    if (TryActivateAlert(state, alert))
                    {
                        _data.CriticalAlertQueue.Enqueue(alert, ((int)alert.Severity, alert.Id));
                    }
                }

                DrainCriticalAlerts();
                _data.Touch();
            }
        }

        public Task ReloadAllAsync()
        {
            var devices = _store.GetDevices();

            lock (_data.SyncRoot)
            {
                var knownIds = devices.Select(d => d.Id).ToHashSet();

                foreach (var device in devices)
                {
                    UpsertDevice(device);
                }

                foreach (var staleId in _data.DeviceIdIndex.Keys.Where(id => !knownIds.Contains(id)).ToList())
                {
                    RemoveDevice(staleId);
                }
            }

            return Task.CompletedTask;
        }

        public Task ReloadDeviceAsync(int deviceId)
        {
            var device = _store.GetDevice(deviceId);

            if (device is null)
            {
                RemoveDevice(deviceId);
                return Task.CompletedTask;
            }

            UpsertDevice(device);
            return Task.CompletedTask;
        }

        public void RemoveDevice(int deviceId)
        {
            lock (_data.SyncRoot)
            {
                if (!_data.DeviceIdIndex.Remove(deviceId, out var key))
                {
                    return;
                }

                _data.DeviceRegistry.Remove(key);
                _data.DisconnectedNodes.Remove(key);

                foreach (var alert in _data.ActiveAlertIndex.Values.Where(a => a.DeviceId == deviceId).ToList())
                {
                    _data.ActiveErrorStates.Remove(alert.Signature);
                    _data.ActiveAlertIndex.Remove(alert.Signature);
                }

                foreach (var timelineKey in _data.SensorTimeline.Keys.Where(k => k.DeviceId == deviceId).ToList())
                {
                    _data.SensorTimeline.Remove(timelineKey);
                }

                _data.Touch();
            }
        }

        public DashboardSnapshot GetDashboard(string user)
        {
            lock (_data.SyncRoot)
            {
                var insights = _engine.BuildInsights(user);

                return new DashboardSnapshot
                {
                    Version = _data.Version,
                    GeneratedAt = DateTime.UtcNow,
                    Stats = BuildStats(),
                    Devices = _data.DeviceRegistry.Values
                        .OrderBy(d => d.DeviceIdentifier, StringComparer.OrdinalIgnoreCase)
                        .Select(ToSnapshot)
                        .ToList(),
                    ActiveAlerts = _data.ActiveAlertIndex.Values.OrderBy(a => a.Severity).ThenByDescending(a => a.RaisedAt).ToList(),
                    RecentAlerts = _data.AlertLog.TakeLast(25).Reverse().ToList(),
                    RecentCommands = _data.CommandHistory.TakeLast(15).Reverse().ToList(),
                    NextUndo = _data.CommandUndoStack.TryPeek(out var next) ? next : null,
                    Insights = insights.Suggestions,
                    HighlightedDeviceIds = insights.HighlightedDeviceIds
                };
            }
        }

        public InsightsResponse GetInsights(string user)
        {
            lock (_data.SyncRoot)
            {
                return _engine.BuildInsights(user);
            }
        }

        public List<DeviceSnapshot> FilterDevices(DeviceFilter filter)
        {
            lock (_data.SyncRoot)
            {
                var text = filter.Text?.Trim();
                IEnumerable<DeviceState> query = _data.DeviceRegistry.Values;

                if (!string.IsNullOrEmpty(text))
                {
                    query = query.Where(d => MatchesText(d, text));
                }

                if (filter.Category.HasValue)
                {
                    var category = filter.Category.Value;
                    query = query.Where(d => d.Sensors.Values.Any(s => s.Category == category));
                }

                if (filter.AlertState.HasValue)
                {
                    var wanted = filter.AlertState.Value;
                    query = query.Where(d => MatchesAlertState(d, wanted));
                }

                if (filter.Status.HasValue)
                {
                    var status = filter.Status.Value;
                    query = query.Where(d => ResolveStatus(d) == status);
                }

                return query
                    .OrderBy(d => d.DeviceIdentifier, StringComparer.OrdinalIgnoreCase)
                    .Select(ToSnapshot)
                    .ToList();
            }
        }

        public List<DeviceSnapshot> GetDevices(IEnumerable<int> deviceIds)
        {
            lock (_data.SyncRoot)
            {
                var result = new List<DeviceSnapshot>();
                foreach (var id in deviceIds)
                {
                    if (_data.TryGetDevice(id, out var state))
                    {
                        result.Add(ToSnapshot(state));
                    }
                }

                return result;
            }
        }

        public DeviceSnapshot? GetDevice(int deviceId)
        {
            lock (_data.SyncRoot)
            {
                return _data.TryGetDevice(deviceId, out var state) ? ToSnapshot(state) : null;
            }
        }

        public List<SensorTimelineEntry> GetTimeline(int deviceId, int sensorId, int take)
        {
            lock (_data.SyncRoot)
            {
                if (!_data.SensorTimeline.TryGetValue((deviceId, sensorId), out var series))
                {
                    return new List<SensorTimelineEntry>();
                }

                return series.Values.TakeLast(Math.Clamp(take, 1, StreamDataStructures.MaxTimelinePoints)).ToList();
            }
        }

        public List<StreamAlert> GetRecentAlerts(int take)
        {
            lock (_data.SyncRoot)
            {
                return _data.AlertLog.TakeLast(Math.Clamp(take, 1, StreamDataStructures.MaxAlertLog)).Reverse().ToList();
            }
        }

        public List<OverrideCommand> GetCommandHistory(int take)
        {
            lock (_data.SyncRoot)
            {
                return _data.CommandHistory.TakeLast(Math.Clamp(take, 1, StreamDataStructures.MaxCommandHistory)).Reverse().ToList();
            }
        }

        public void RecordSearch(string user, DeviceFilter filter) => _engine.RecordSearch(user, filter);

        public CommandOutcome ApplyOverride(CommandRequest request, string operatorName)
        {
            lock (_data.SyncRoot)
            {
                if (!Enum.IsDefined(request.Command))
                {
                    return Outcome(CommandOutcomeKind.Invalid, "Unknown command.");
                }

                if (!_data.TryGetDevice(request.DeviceId, out var state))
                {
                    return Outcome(CommandOutcomeKind.NotFound, $"Device {request.DeviceId} is not registered.");
                }

                if (_data.DisconnectedNodes.Contains(state.RegistryKey))
                {
                    return Outcome(CommandOutcomeKind.Conflict, $"{state.DeviceIdentifier} is disconnected and cannot accept commands.");
                }

                var actuators = state.Sensors.Values
                    .Where(s => s.Category == SensorCategory.Actuator && s.ValueType == TelemetryValueType.Bool)
                    .OrderBy(s => s.Id)
                    .ToList();

                if (actuators.Count == 0)
                {
                    return Outcome(CommandOutcomeKind.Invalid, $"{state.DeviceIdentifier} has no controllable actuator.");
                }

                SensorInfo? target;
                if (request.SensorId.HasValue)
                {
                    target = actuators.FirstOrDefault(s => s.Id == request.SensorId.Value);
                    if (target is null)
                    {
                        return Outcome(CommandOutcomeKind.Invalid, $"Sensor {request.SensorId.Value} is not an actuator on {state.DeviceIdentifier}.");
                    }
                }
                else if (actuators.Count == 1)
                {
                    target = actuators[0];
                }
                else
                {
                    return Outcome(CommandOutcomeKind.Invalid, $"{state.DeviceIdentifier} has several actuators; specify sensorId.");
                }

                var previousKnown = state.TryGetBool(target.Id, out var previous);
                var next = request.Command switch
                {
                    OverrideCommandType.TurnOn => true,
                    OverrideCommandType.TurnOff => false,
                    _ => !previous
                };

                var now = DateTime.UtcNow;
                var command = new OverrideCommand
                {
                    Id = _data.NextSequence(),
                    DeviceId = state.DeviceId,
                    DeviceIdentifier = state.DeviceIdentifier,
                    SensorId = target.Id,
                    SensorName = target.Name,
                    Command = request.Command,
                    PreviousState = previous,
                    PreviousStateKnown = previousKnown,
                    NewState = next,
                    IssuedBy = operatorName,
                    IssuedAt = now
                };

                _data.CommandUndoStack.Push(command);
                AddCommandHistory(command);
                SetActuatorState(state, target, next, now);
                _engine.RecordCommand(operatorName, state.DeviceId, target.Id, request.Command);
                _data.Touch();

                return new CommandOutcome
                {
                    Kind = CommandOutcomeKind.Success,
                    Command = command,
                    Message = $"{target.Name} on {state.DeviceIdentifier} is now {(next ? "ON" : "OFF")}."
                };
            }
        }

        public CommandOutcome UndoLastOverride(string operatorName)
        {
            lock (_data.SyncRoot)
            {
                var discarded = 0;

                while (_data.CommandUndoStack.TryPeek(out var original))
                {
                    if (!_data.TryGetDevice(original.DeviceId, out var state) ||
                        !state.Sensors.TryGetValue(original.SensorId, out var sensor))
                    {
                        _data.CommandUndoStack.Pop();
                        original.Undone = true;
                        discarded++;
                        continue;
                    }

                    if (_data.DisconnectedNodes.Contains(state.RegistryKey))
                    {
                        return Outcome(CommandOutcomeKind.Conflict, $"{state.DeviceIdentifier} is disconnected; the command was kept on the undo stack.");
                    }

                    _data.CommandUndoStack.Pop();
                    original.Undone = true;

                    var now = DateTime.UtcNow;
                    var restored = original.PreviousState;
                    var undo = new OverrideCommand
                    {
                        Id = _data.NextSequence(),
                        DeviceId = state.DeviceId,
                        DeviceIdentifier = state.DeviceIdentifier,
                        SensorId = sensor.Id,
                        SensorName = sensor.Name,
                        Command = restored ? OverrideCommandType.TurnOn : OverrideCommandType.TurnOff,
                        PreviousState = original.NewState,
                        PreviousStateKnown = true,
                        NewState = restored,
                        IssuedBy = operatorName,
                        IssuedAt = now,
                        IsUndo = true,
                        UndoOfCommandId = original.Id
                    };

                    AddCommandHistory(undo);
                    SetActuatorState(state, sensor, restored, now);
                    _engine.RecordUndo(operatorName, original);
                    _data.Touch();

                    return new CommandOutcome
                    {
                        Kind = CommandOutcomeKind.Success,
                        Command = undo,
                        Message = $"Undid the last command: {sensor.Name} on {state.DeviceIdentifier} restored to {(restored ? "ON" : "OFF")}."
                    };
                }

                var message = discarded > 0
                    ? $"There are no commands left to undo ({discarded} stale command(s) for removed devices were discarded)."
                    : "There are no commands to undo.";

                return Outcome(CommandOutcomeKind.NothingToUndo, message);
            }
        }

        private static CommandOutcome Outcome(CommandOutcomeKind kind, string message) =>
            new() { Kind = kind, Message = message };

        private void AddCommandHistory(OverrideCommand command)
        {
            _data.CommandHistory.Add(command);
            if (_data.CommandHistory.Count > StreamDataStructures.MaxCommandHistory)
            {
                _data.CommandHistory.RemoveRange(0, _data.CommandHistory.Count - StreamDataStructures.MaxCommandHistory);
            }
        }

        private void SetActuatorState(DeviceState state, SensorInfo sensor, bool value, DateTime timestamp)
        {
            state.LatestReadings[sensor.Id] = new SensorReading
            {
                SensorId = sensor.Id,
                Value = value,
                Numeric = null,
                Timestamp = timestamp,
                ReceivedAt = timestamp
            };

            AppendTimeline(state.DeviceId, sensor.Id, timestamp, value, null);
        }

        private bool IsCritical(DeviceState state, TelemetryPacketBase packet)
        {
            if (!state.Sensors.TryGetValue(packet.SensorId, out var sensor))
            {
                return false;
            }

            var numeric = ToNumeric(packet.GetValue());
            if (!numeric.HasValue)
            {
                return false;
            }

            var rule = ThresholdCatalog.Worst(sensor, numeric.Value);
            return rule is not null && rule.Severity == AlertSeverity.Critical;
        }

        private void ApplyReading(DeviceState state, TelemetryPacketBase packet, DateTime receivedAt)
        {
            state.LastSeenUtc = receivedAt;
            state.PacketsReceived++;

            if (_data.DisconnectedNodes.Remove(state.RegistryKey))
            {
                var signature = PredictiveActionEngine.Signature(PredictiveActionEngine.NodeDisconnectedType, state.DeviceId, null);
                ClearActiveAlert(state, signature);
                LogInfo(state, "NodeRecovered", $"{state.DeviceIdentifier} is reporting again");
            }

            if (!state.Sensors.TryGetValue(packet.SensorId, out var sensor))
            {
                return;
            }

            var value = packet.GetValue();
            var numeric = ToNumeric(value);

            if (!state.LatestReadings.TryGetValue(sensor.Id, out var existing) || packet.Timestamp >= existing.Timestamp)
            {
                state.LatestReadings[sensor.Id] = new SensorReading
                {
                    SensorId = sensor.Id,
                    Value = value,
                    Numeric = numeric,
                    Timestamp = packet.Timestamp,
                    ReceivedAt = receivedAt
                };
            }

            AppendTimeline(state.DeviceId, sensor.Id, packet.Timestamp, value, numeric);

            if (numeric.HasValue)
            {
                EvaluateThresholds(state, sensor, numeric.Value);
            }
        }

        private void EvaluateThresholds(DeviceState state, SensorInfo sensor, double value)
        {
            var rules = ThresholdCatalog.RulesFor(sensor);
            if (rules.Count == 0)
            {
                return;
            }

            var violated = ThresholdCatalog.Worst(sensor, value);

            foreach (var rule in rules)
            {
                if (violated is not null && rule.AlertType == violated.AlertType)
                {
                    continue;
                }

                ClearActiveAlert(state, PredictiveActionEngine.Signature(rule.AlertType, state.DeviceId, sensor.Id));
            }

            if (violated is null)
            {
                return;
            }

            var alert = new StreamAlert
            {
                Id = _data.NextSequence(),
                Signature = PredictiveActionEngine.Signature(violated.AlertType, state.DeviceId, sensor.Id),
                AlertType = violated.AlertType,
                Severity = violated.Severity,
                DeviceId = state.DeviceId,
                DeviceIdentifier = state.DeviceIdentifier,
                MacAddress = state.MacAddress,
                SensorId = sensor.Id,
                SensorName = sensor.Name,
                Value = value,
                Message = $"{sensor.Name} on {state.DeviceIdentifier} reported {ThresholdCatalog.Format(value)}{sensor.Unit}, {violated.Describe(sensor.Unit)}",
                RaisedAt = DateTime.UtcNow
            };

            if (!TryActivateAlert(state, alert))
            {
                return;
            }

            if (alert.Severity <= AlertSeverity.High)
            {
                _data.CriticalAlertQueue.Enqueue(alert, ((int)alert.Severity, alert.Id));
            }
            else
            {
                FinalizeAlert(alert, false);
            }
        }

        private bool TryActivateAlert(DeviceState state, StreamAlert alert)
        {
            if (!_data.ActiveErrorStates.Add(alert.Signature))
            {
                _data.DuplicateAlertsSuppressed++;
                return false;
            }

            state.ActiveAlerts[alert.Signature] = alert.Severity;
            _data.ActiveAlertIndex[alert.Signature] = alert;
            return true;
        }

        private void ClearActiveAlert(DeviceState state, string signature)
        {
            if (_data.ActiveErrorStates.Remove(signature))
            {
                state.ActiveAlerts.Remove(signature);
                _data.ActiveAlertIndex.Remove(signature);
            }
        }

        private void DrainCriticalAlerts()
        {
            while (_data.CriticalAlertQueue.TryDequeue(out var alert, out _))
            {
                FinalizeAlert(alert, true);
            }
        }

        private void FinalizeAlert(StreamAlert alert, bool bypassedQueue)
        {
            alert.ProcessedAt = DateTime.UtcNow;
            alert.BypassedQueue = bypassedQueue;
            AppendAlertLog(alert);

            if (bypassedQueue)
            {
                _data.CriticalAlertsProcessed++;
            }

            _engine.OnAlertProcessed(alert);
            _data.Touch();
        }

        private void LogInfo(DeviceState state, string type, string message)
        {
            var now = DateTime.UtcNow;
            AppendAlertLog(new StreamAlert
            {
                Id = _data.NextSequence(),
                Signature = PredictiveActionEngine.Signature(type, state.DeviceId, null),
                AlertType = type,
                Severity = AlertSeverity.Info,
                DeviceId = state.DeviceId,
                DeviceIdentifier = state.DeviceIdentifier,
                MacAddress = state.MacAddress,
                Message = message,
                RaisedAt = now,
                ProcessedAt = now
            });
        }

        private void AppendAlertLog(StreamAlert alert)
        {
            _data.AlertLog.Add(alert);
            if (_data.AlertLog.Count > StreamDataStructures.MaxAlertLog)
            {
                _data.AlertLog.RemoveRange(0, _data.AlertLog.Count - StreamDataStructures.MaxAlertLog);
            }
        }

        private void AppendTimeline(int deviceId, int sensorId, DateTime timestamp, object? value, double? numeric)
        {
            var key = (deviceId, sensorId);
            if (!_data.SensorTimeline.TryGetValue(key, out var series))
            {
                series = new SortedDictionary<DateTime, SensorTimelineEntry>();
                _data.SensorTimeline[key] = series;
            }

            var at = timestamp;
            while (series.ContainsKey(at))
            {
                at = at.AddTicks(1);
            }

            series[at] = new SensorTimelineEntry { Timestamp = at, Value = value, Numeric = numeric };

            while (series.Count > StreamDataStructures.MaxTimelinePoints)
            {
                series.Remove(series.Keys.First());
            }
        }

        private void UpsertDevice(Device device)
        {
            lock (_data.SyncRoot)
            {
                var mac = NormalizeMac(device.MacAddress);
                var key = !string.IsNullOrEmpty(mac)
                    ? mac
                    : !string.IsNullOrWhiteSpace(device.NodeId) ? device.NodeId.Trim() : $"DEVICE-{device.Id}";

                if (_data.DeviceRegistry.TryGetValue(key, out var holder) && holder.DeviceId != device.Id)
                {
                    _logger.LogWarning("Device {DeviceId} shares registry key {Key} with device {HolderId} and was not registered for streaming.", device.Id, key, holder.DeviceId);
                    return;
                }

                if (_data.DeviceIdIndex.TryGetValue(device.Id, out var oldKey) && !string.Equals(oldKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    if (_data.DeviceRegistry.Remove(oldKey, out var moved))
                    {
                        _data.DeviceRegistry[key] = moved;
                    }

                    if (_data.DisconnectedNodes.Remove(oldKey))
                    {
                        _data.DisconnectedNodes.Add(key);
                    }
                }

                if (!_data.DeviceRegistry.TryGetValue(key, out var state))
                {
                    state = new DeviceState();
                    _data.DeviceRegistry[key] = state;
                }

                state.DeviceId = device.Id;
                state.RegistryKey = key;
                state.DeviceIdentifier = device.DeviceIdentifier;
                state.MacAddress = mac;
                state.NodeId = device.NodeId;
                state.Location = device.Location;
                state.Room = device.Room;
                state.Sensors = device.Sensors.ToDictionary(
                    s => s.Id,
                    s => new SensorInfo
                    {
                        Id = s.Id,
                        DeviceId = s.DeviceId,
                        Name = s.Name,
                        Category = s.Category,
                        ValueType = s.ValueType,
                        Unit = s.Unit
                    });

                foreach (var staleSensor in state.LatestReadings.Keys.Where(id => !state.Sensors.ContainsKey(id)).ToList())
                {
                    state.LatestReadings.Remove(staleSensor);
                }

                var deviceId = device.Id;
                foreach (var timelineKey in _data.SensorTimeline.Keys
                    .Where(k => k.DeviceId == deviceId && !state.Sensors.ContainsKey(k.SensorId))
                    .ToList())
                {
                    _data.SensorTimeline.Remove(timelineKey);
                }

                _data.DeviceIdIndex[device.Id] = key;
                _data.Touch();
            }
        }

        private StreamStats BuildStats() => new()
        {
            RegisteredDevices = _data.DeviceRegistry.Count,
            TelemetryQueueDepth = _data.TelemetryQueue.Count,
            CriticalQueueDepth = _data.CriticalAlertQueue.Count,
            UndoStackDepth = _data.CommandUndoStack.Count,
            ActiveErrorStates = _data.ActiveErrorStates.Count,
            DisconnectedNodes = _data.DisconnectedNodes.Count,
            ProcessedPackets = _data.ProcessedPackets,
            CriticalAlertsProcessed = _data.CriticalAlertsProcessed,
            CriticalReadingsBypassed = _data.CriticalReadingsBypassed,
            DuplicateAlertsSuppressed = _data.DuplicateAlertsSuppressed,
            UnknownDevicePackets = _data.UnknownDevicePackets
        };

        private DeviceStatus ResolveStatus(DeviceState state)
        {
            if (_data.DisconnectedNodes.Contains(state.RegistryKey))
            {
                return DeviceStatus.Disconnected;
            }

            return state.LastSeenUtc.HasValue ? DeviceStatus.Online : DeviceStatus.AwaitingData;
        }

        private DeviceAlertState ResolveAlertState(DeviceState state)
        {
            if (_data.DisconnectedNodes.Contains(state.RegistryKey))
            {
                return DeviceAlertState.Disconnected;
            }

            if (state.ActiveAlerts.Values.Any(s => s == AlertSeverity.Critical))
            {
                return DeviceAlertState.Critical;
            }

            return state.ActiveAlerts.Count > 0 ? DeviceAlertState.Warning : DeviceAlertState.Normal;
        }

        private bool MatchesAlertState(DeviceState state, DeviceAlertState wanted)
        {
            var actual = ResolveAlertState(state);
            return wanted == DeviceAlertState.Alerting ? actual != DeviceAlertState.Normal : actual == wanted;
        }

        private static bool MatchesText(DeviceState state, string text)
        {
            var comparison = StringComparison.OrdinalIgnoreCase;
            return state.DeviceIdentifier.Contains(text, comparison)
                || state.MacAddress.Contains(text, comparison)
                || state.NodeId.Contains(text, comparison)
                || state.Location.Contains(text, comparison)
                || state.Room.Contains(text, comparison)
                || state.Sensors.Values.Any(s => s.Name.Contains(text, comparison));
        }

        private DeviceSnapshot ToSnapshot(DeviceState state)
        {
            var sensorAlerts = new Dictionary<int, StreamAlert>();
            foreach (var signature in state.ActiveAlerts.Keys)
            {
                if (_data.ActiveAlertIndex.TryGetValue(signature, out var alert) && alert.SensorId.HasValue)
                {
                    sensorAlerts[alert.SensorId.Value] = alert;
                }
            }

            var sensors = new List<SensorSnapshot>();
            foreach (var sensor in state.Sensors.Values.OrderBy(s => s.Id))
            {
                state.LatestReadings.TryGetValue(sensor.Id, out var reading);
                sensorAlerts.TryGetValue(sensor.Id, out var sensorAlert);

                sensors.Add(new SensorSnapshot
                {
                    Id = sensor.Id,
                    Name = sensor.Name,
                    Category = sensor.Category,
                    ValueType = sensor.ValueType,
                    Unit = sensor.Unit,
                    Value = reading?.Value,
                    Timestamp = reading?.Timestamp,
                    IsOutOfRange = sensorAlert is not null,
                    ActiveAlertType = sensorAlert?.AlertType,
                    ActiveAlertSeverity = sensorAlert?.Severity
                });
            }

            return new DeviceSnapshot
            {
                DeviceId = state.DeviceId,
                DeviceIdentifier = state.DeviceIdentifier,
                MacAddress = state.MacAddress,
                NodeId = state.NodeId,
                Location = state.Location,
                Room = state.Room,
                Status = ResolveStatus(state),
                AlertState = ResolveAlertState(state),
                Categories = state.Sensors.Values.Select(s => s.Category).Distinct().ToList(),
                Sensors = sensors,
                LastSeenUtc = state.LastSeenUtc,
                PacketsReceived = state.PacketsReceived
            };
        }

        private static double? ToNumeric(object? value) => value switch
        {
            float f => f,
            double d => d,
            int i => i,
            long l => l,
            _ => null
        };
    }
}
