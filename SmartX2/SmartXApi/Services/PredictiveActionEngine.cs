using SmartXApi.Models;
using SmartXApi.Services;

namespace SmartXApi.Services
{
    public class PredictiveActionEngine
    {
        public const string NodeDisconnectedType = "NodeDisconnected";

        private const int MinSupport = 2;
        private const double MinConfidence = 0.5;
        private const double TrendMinRSquared = 0.6;
        private const double TrendHorizonMinutes = 30;
        private static readonly TimeSpan TriggerWindow = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan SequenceWindow = TimeSpan.FromMinutes(5);

        private readonly StreamDataStructures _data;

        public PredictiveActionEngine(StreamDataStructures data) => _data = data;

        public static string Signature(string alertType, int deviceId, int? sensorId) =>
            $"{alertType}|{deviceId}|{(sensorId.HasValue ? sensorId.Value.ToString() : "-")}";

        public void OnAlertProcessed(StreamAlert alert)
        {
            lock (_data.SyncRoot)
            {
                _data.TriggerOccurrences.TryGetValue(alert.Signature, out var current);
                _data.TriggerOccurrences[alert.Signature] = current + 1;
                _data.TriggerLabels[alert.Signature] = string.IsNullOrEmpty(alert.SensorName)
                    ? $"{alert.AlertType} on {alert.DeviceIdentifier}"
                    : $"{alert.AlertType} on {alert.DeviceIdentifier} ({alert.SensorName})";
            }
        }

        public void RecordSearch(string user, DeviceFilter filter)
        {
            if (filter.IsEmpty)
            {
                return;
            }

            var text = (filter.Text ?? string.Empty).Trim().ToLowerInvariant();
            var key = $"Search|{text}|{filter.Category}|{filter.AlertState}|{filter.Status}";
            var payload = new SuggestedAction
            {
                Type = "Search",
                SearchText = string.IsNullOrEmpty(text) ? null : text,
                Category = filter.Category,
                AlertState = filter.AlertState,
                Status = filter.Status
            };

            Register(user, UserActionKind.Search, key, payload, true);
        }

        public void RecordCommand(string user, int deviceId, int sensorId, OverrideCommandType command)
        {
            var key = $"Command|{deviceId}|{sensorId}|{command}";
            var payload = new SuggestedAction
            {
                Type = "Command",
                DeviceId = deviceId,
                SensorId = sensorId,
                Command = command
            };

            Register(user, UserActionKind.Command, key, payload, true);
        }

        public void RecordUndo(string user, OverrideCommand undone)
        {
            var key = $"Undo|{undone.DeviceId}|{undone.SensorId}|{undone.Command}";
            var payload = new SuggestedAction
            {
                Type = "Command",
                DeviceId = undone.DeviceId,
                SensorId = undone.SensorId,
                Command = undone.Command
            };

            Register(user, UserActionKind.Undo, key, payload, false);
        }

        public InsightsResponse BuildInsights(string user)
        {
            lock (_data.SyncRoot)
            {
                var now = DateTime.UtcNow;
                var suggestions = new List<AutomatedInsight>();
                var patterns = new List<LearnedPattern>();

                AddPatternInsights(now, suggestions, patterns);
                AddSequenceInsights(user, now, suggestions);
                AddActiveAlertInsights(now, suggestions);
                AddTelemetryInsights(now, suggestions);

                var ordered = suggestions
                    .GroupBy(s => s.Id)
                    .Select(g => g.First())
                    .OrderBy(s => s.Severity)
                    .ThenByDescending(s => s.Confidence)
                    .ToList();

                var highlighted = ordered
                    .Where(s => s.DeviceId.HasValue && s.Severity <= AlertSeverity.Warning)
                    .Select(s => s.DeviceId!.Value)
                    .Distinct()
                    .ToList();

                return new InsightsResponse
                {
                    GeneratedAt = now,
                    Suggestions = ordered,
                    LearnedPatterns = patterns
                        .OrderByDescending(p => p.IsTriggerActive)
                        .ThenByDescending(p => p.Confidence)
                        .ThenByDescending(p => p.Count)
                        .ToList(),
                    HighlightedDeviceIds = highlighted,
                    RecentActivity = _data.UserActivity.TakeLast(20).Reverse().ToList(),
                    RecordedActions = _data.UserActivity.Count
                };
            }
        }

        private void Register(string user, UserActionKind kind, string key, SuggestedAction payload, bool learn)
        {
            lock (_data.SyncRoot)
            {
                var now = DateTime.UtcNow;

                _data.UserActivity.Add(new UserActionEvent
                {
                    Id = _data.NextSequence(),
                    Timestamp = now,
                    User = user,
                    Kind = kind,
                    ActionKey = key,
                    Description = DescribeAction(payload, kind == UserActionKind.Undo)
                });

                if (_data.UserActivity.Count > StreamDataStructures.MaxUserActivity)
                {
                    _data.UserActivity.RemoveRange(0, _data.UserActivity.Count - StreamDataStructures.MaxUserActivity);
                }

                _data.Touch();

                if (!learn)
                {
                    return;
                }

                _data.ActionPayloads[key] = payload;

                if (_data.LastActionByUser.TryGetValue(user, out var last) && now - last.At <= SequenceWindow)
                {
                    if (!_data.ActionTransitions.TryGetValue(last.ActionKey, out var next))
                    {
                        next = new Dictionary<string, int>(StringComparer.Ordinal);
                        _data.ActionTransitions[last.ActionKey] = next;
                    }

                    next.TryGetValue(key, out var seen);
                    next[key] = seen + 1;
                }

                _data.LastActionByUser[user] = (key, now);

                for (var i = _data.AlertLog.Count - 1; i >= 0; i--)
                {
                    var alert = _data.AlertLog[i];
                    if (now - alert.RaisedAt > TriggerWindow)
                    {
                        break;
                    }

                    if (!alert.CreditedActions.Add(key))
                    {
                        continue;
                    }

                    var patternKey = $"{alert.Signature}=>{key}";
                    if (!_data.TriggerActionPatterns.TryGetValue(patternKey, out var stat))
                    {
                        stat = new PatternStat { Trigger = alert.Signature, ActionKey = key };
                        _data.TriggerActionPatterns[patternKey] = stat;
                    }

                    stat.Count++;
                    stat.LastSeen = now;
                }
            }
        }

        private void AddPatternInsights(DateTime now, List<AutomatedInsight> suggestions, List<LearnedPattern> patterns)
        {
            foreach (var stat in _data.TriggerActionPatterns.Values)
            {
                if (stat.Count < MinSupport)
                {
                    continue;
                }

                if (!_data.TriggerOccurrences.TryGetValue(stat.Trigger, out var occurrences) || occurrences <= 0)
                {
                    continue;
                }

                var confidence = Math.Min(1.0, (double)stat.Count / occurrences);
                if (confidence < MinConfidence)
                {
                    continue;
                }

                if (!_data.ActionPayloads.TryGetValue(stat.ActionKey, out var payload))
                {
                    continue;
                }

                var label = _data.TriggerLabels.TryGetValue(stat.Trigger, out var storedLabel) ? storedLabel : stat.Trigger;
                var description = DescribeAction(payload, false);
                var active = _data.ActiveAlertIndex.TryGetValue(stat.Trigger, out var activeAlert);

                patterns.Add(new LearnedPattern
                {
                    Trigger = stat.Trigger,
                    TriggerLabel = label,
                    ActionKey = stat.ActionKey,
                    ActionDescription = description,
                    Count = stat.Count,
                    TriggerOccurrences = occurrences,
                    Confidence = confidence,
                    LastSeen = stat.LastSeen,
                    IsTriggerActive = active
                });

                if (!active || activeAlert is null || IsRedundant(payload))
                {
                    continue;
                }

                suggestions.Add(new AutomatedInsight
                {
                    Id = $"pattern:{stat.Trigger}=>{stat.ActionKey}",
                    Category = "SuggestedAction",
                    Severity = activeAlert.Severity,
                    Title = description,
                    Reason = $"{label} is active. After this alert you performed this action {stat.Count} of {occurrences} times ({Percent(confidence)}).",
                    Confidence = confidence,
                    Support = stat.Count,
                    DeviceId = payload.DeviceId ?? activeAlert.DeviceId,
                    SensorId = payload.SensorId ?? activeAlert.SensorId,
                    DeviceIdentifier = activeAlert.DeviceIdentifier,
                    Action = payload,
                    GeneratedAt = now
                });
            }
        }

        private void AddSequenceInsights(string user, DateTime now, List<AutomatedInsight> suggestions)
        {
            if (!_data.LastActionByUser.TryGetValue(user, out var last) || now - last.At > SequenceWindow)
            {
                return;
            }

            if (!_data.ActionTransitions.TryGetValue(last.ActionKey, out var next))
            {
                return;
            }

            var total = next.Values.Sum();
            if (total <= 0)
            {
                return;
            }

            _data.ActionPayloads.TryGetValue(last.ActionKey, out var lastPayload);
            var lastDescription = lastPayload is null ? last.ActionKey : DescribeAction(lastPayload, false);

            foreach (var pair in next)
            {
                if (pair.Value < MinSupport || pair.Key == last.ActionKey)
                {
                    continue;
                }

                var confidence = (double)pair.Value / total;
                if (confidence < MinConfidence || !_data.ActionPayloads.TryGetValue(pair.Key, out var payload) || IsRedundant(payload))
                {
                    continue;
                }

                suggestions.Add(new AutomatedInsight
                {
                    Id = $"sequence:{last.ActionKey}=>{pair.Key}",
                    Category = "SequenceSuggestion",
                    Severity = AlertSeverity.Info,
                    Title = DescribeAction(payload, false),
                    Reason = $"After \"{lastDescription}\" you followed up with this {pair.Value} of {total} times ({Percent(confidence)}).",
                    Confidence = confidence,
                    Support = pair.Value,
                    DeviceId = payload.DeviceId,
                    SensorId = payload.SensorId,
                    DeviceIdentifier = payload.DeviceId.HasValue && _data.TryGetDevice(payload.DeviceId.Value, out var device)
                        ? device.DeviceIdentifier
                        : null,
                    Action = payload,
                    GeneratedAt = now
                });
            }
        }

        private void AddActiveAlertInsights(DateTime now, List<AutomatedInsight> suggestions)
        {
            foreach (var alert in _data.ActiveAlertIndex.Values)
            {
                var isNode = alert.AlertType == NodeDisconnectedType;
                var age = now - alert.RaisedAt;

                suggestions.Add(new AutomatedInsight
                {
                    Id = $"alert:{alert.Signature}",
                    Category = isNode ? "FaultyDevice" : "OutOfRange",
                    Severity = alert.Severity,
                    Title = isNode
                        ? $"{alert.DeviceIdentifier} is disconnected"
                        : $"{alert.SensorName} on {alert.DeviceIdentifier} is out of range",
                    Reason = $"{alert.Message} (active for {FormatAge(age)}).",
                    Confidence = 1.0,
                    Support = 1,
                    DeviceId = alert.DeviceId,
                    SensorId = alert.SensorId,
                    DeviceIdentifier = alert.DeviceIdentifier,
                    Action = new SuggestedAction { Type = "Search", SearchText = alert.DeviceIdentifier.ToLowerInvariant() },
                    GeneratedAt = now
                });
            }
        }

        private void AddTelemetryInsights(DateTime now, List<AutomatedInsight> suggestions)
        {
            foreach (var state in _data.DeviceRegistry.Values)
            {
                foreach (var sensor in state.Sensors.Values)
                {
                    if (!_data.SensorTimeline.TryGetValue((state.DeviceId, sensor.Id), out var series))
                    {
                        continue;
                    }

                    AddTrendInsight(state, sensor, series, now, suggestions);
                    AddFlatlineInsight(state, sensor, series, now, suggestions);
                }
            }
        }

        private void AddTrendInsight(
            DeviceState state,
            SensorInfo sensor,
            SortedDictionary<DateTime, SensorTimelineEntry> series,
            DateTime now,
            List<AutomatedInsight> suggestions)
        {
            var rules = ThresholdCatalog.RulesFor(sensor);
            if (rules.Count == 0 || series.Count < 5)
            {
                return;
            }

            var points = series.Values.Where(p => p.Numeric.HasValue).TakeLast(8).ToList();
            if (points.Count < 5)
            {
                return;
            }

            var origin = points[0].Timestamp;
            var xs = points.Select(p => (p.Timestamp - origin).TotalMinutes).ToArray();
            var ys = points.Select(p => p.Numeric!.Value).ToArray();
            var meanX = xs.Average();
            var meanY = ys.Average();

            double sxx = 0, sxy = 0, syy = 0;
            for (var i = 0; i < xs.Length; i++)
            {
                var dx = xs[i] - meanX;
                var dy = ys[i] - meanY;
                sxx += dx * dx;
                sxy += dx * dy;
                syy += dy * dy;
            }

            if (sxx < 1e-9 || syy < 1e-9)
            {
                return;
            }

            var slope = sxy / sxx;
            var rSquared = (sxy * sxy) / (sxx * syy);
            if (rSquared < TrendMinRSquared || Math.Abs(slope) < 1e-6)
            {
                return;
            }

            var current = ys[^1];
            ThresholdRule? bestRule = null;
            double bestBound = 0;
            var bestMinutes = double.MaxValue;

            foreach (var rule in rules)
            {
                var bound = slope < 0 ? rule.Min : rule.Max;
                if (!bound.HasValue)
                {
                    continue;
                }

                if ((slope < 0 && current <= bound.Value) || (slope > 0 && current >= bound.Value))
                {
                    continue;
                }

                var minutes = (bound.Value - current) / slope;
                if (minutes <= 0 || minutes > TrendHorizonMinutes || minutes >= bestMinutes)
                {
                    continue;
                }

                bestRule = rule;
                bestBound = bound.Value;
                bestMinutes = minutes;
            }

            if (bestRule is null)
            {
                return;
            }

            var direction = slope < 0 ? "falling" : "rising";
            suggestions.Add(new AutomatedInsight
            {
                Id = $"trend:{state.DeviceId}|{sensor.Id}|{bestRule.AlertType}",
                Category = "PredictedBreach",
                Severity = bestRule.Severity == AlertSeverity.Critical ? AlertSeverity.High : AlertSeverity.Warning,
                Title = $"{sensor.Name} on {state.DeviceIdentifier} is heading toward {bestRule.AlertType}",
                Reason = $"Readings are {direction} at {ThresholdCatalog.Format(Math.Abs(slope))}{sensor.Unit} per minute (fit R2 {ThresholdCatalog.Format(rSquared)}); the {ThresholdCatalog.Format(bestBound)}{sensor.Unit} limit is projected to be crossed in about {Math.Ceiling(bestMinutes)} min.",
                Confidence = rSquared,
                Support = points.Count,
                DeviceId = state.DeviceId,
                SensorId = sensor.Id,
                DeviceIdentifier = state.DeviceIdentifier,
                Action = new SuggestedAction { Type = "Search", SearchText = state.DeviceIdentifier.ToLowerInvariant() },
                GeneratedAt = now
            });
        }

        private void AddFlatlineInsight(
            DeviceState state,
            SensorInfo sensor,
            SortedDictionary<DateTime, SensorTimelineEntry> series,
            DateTime now,
            List<AutomatedInsight> suggestions)
        {
            if (sensor.Category != SensorCategory.Environmental || series.Count < 10)
            {
                return;
            }

            var recent = series.Values.TakeLast(10).ToList();
            if (recent.Any(p => !p.Numeric.HasValue))
            {
                return;
            }

            var spread = recent.Max(p => p.Numeric!.Value) - recent.Min(p => p.Numeric!.Value);
            var span = recent[^1].Timestamp - recent[0].Timestamp;
            if (spread > 1e-9 || span < TimeSpan.FromSeconds(30))
            {
                return;
            }

            suggestions.Add(new AutomatedInsight
            {
                Id = $"flatline:{state.DeviceId}|{sensor.Id}",
                Category = "FaultyDevice",
                Severity = AlertSeverity.Warning,
                Title = $"{sensor.Name} on {state.DeviceIdentifier} may be stuck",
                Reason = $"The last 10 readings over {FormatAge(span)} were identical ({ThresholdCatalog.Format(recent[^1].Numeric!.Value)}{sensor.Unit}).",
                Confidence = Math.Min(1.0, series.Count / 20.0),
                Support = recent.Count,
                DeviceId = state.DeviceId,
                SensorId = sensor.Id,
                DeviceIdentifier = state.DeviceIdentifier,
                Action = new SuggestedAction { Type = "Search", SearchText = state.DeviceIdentifier.ToLowerInvariant() },
                GeneratedAt = now
            });
        }

        private bool IsRedundant(SuggestedAction payload)
        {
            if (payload.Type != "Command" || !payload.DeviceId.HasValue || !payload.SensorId.HasValue || !payload.Command.HasValue)
            {
                return false;
            }

            if (!_data.TryGetDevice(payload.DeviceId.Value, out var state) || !state.TryGetBool(payload.SensorId.Value, out var current))
            {
                return false;
            }

            return payload.Command.Value switch
            {
                OverrideCommandType.TurnOn => current,
                OverrideCommandType.TurnOff => !current,
                _ => false
            };
        }

        private string DescribeAction(SuggestedAction payload, bool undo)
        {
            if (payload.Type == "Command" && payload.DeviceId.HasValue)
            {
                var deviceName = $"device {payload.DeviceId.Value}";
                var sensorName = "actuator";

                if (_data.TryGetDevice(payload.DeviceId.Value, out var state))
                {
                    deviceName = state.DeviceIdentifier;
                    if (payload.SensorId.HasValue && state.Sensors.TryGetValue(payload.SensorId.Value, out var sensor))
                    {
                        sensorName = sensor.Name;
                    }
                }

                var verb = payload.Command switch
                {
                    OverrideCommandType.TurnOn => "Turn on",
                    OverrideCommandType.TurnOff => "Turn off",
                    _ => "Toggle"
                };

                return undo
                    ? $"Undo: {verb.ToLowerInvariant()} {sensorName} on {deviceName}"
                    : $"{verb} {sensorName} on {deviceName}";
            }

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(payload.SearchText))
            {
                parts.Add($"\"{payload.SearchText}\"");
            }

            if (payload.Category.HasValue)
            {
                parts.Add($"category {payload.Category.Value}");
            }

            if (payload.AlertState.HasValue)
            {
                parts.Add($"alert state {payload.AlertState.Value}");
            }

            if (payload.Status.HasValue)
            {
                parts.Add($"status {payload.Status.Value}");
            }

            return $"Search for {string.Join(", ", parts)}";
        }

        private static string Percent(double value) => $"{Math.Round(value * 100)}%";

        private static string FormatAge(TimeSpan age)
        {
            if (age.TotalSeconds < 90)
            {
                return $"{Math.Max(0, (int)age.TotalSeconds)}s";
            }

            return age.TotalMinutes < 90 ? $"{(int)age.TotalMinutes}min" : $"{(int)age.TotalHours}h";
        }
    }
}
