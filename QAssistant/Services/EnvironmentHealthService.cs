// Copyright (C) 2026 Lewandowskista
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using QAssistant.Models;

namespace QAssistant.Services
{
    public enum HealthStatus { Unknown, Healthy, Unhealthy }

    public sealed class EnvironmentHealthService : IDisposable
    {
        private static readonly HttpClient s_client = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        private readonly ConcurrentDictionary<Guid, HealthStatus> _statuses = new();
        private CancellationTokenSource? _cts;
        private Timer? _timer;

        /// <summary>Interval between health-check rounds (default 30 s).</summary>
        public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Raised on the caller's thread-pool when any status changes.</summary>
        public event Action? StatusChanged;

        public HealthStatus GetStatus(Guid envId) =>
            _statuses.GetValueOrDefault(envId, HealthStatus.Unknown);

        /// <summary>Start periodic health checks for the given environments.</summary>
        public void Start(IReadOnlyList<QaEnvironment> environments)
        {
            Stop();
            _cts = new CancellationTokenSource();
            _timer = new Timer(_ => _ = PingAllAsync(environments, _cts.Token),
                null, TimeSpan.Zero, Interval);
        }

        /// <summary>Stop all periodic checks.</summary>
        public void Stop()
        {
            _cts?.Cancel();
            _timer?.Dispose();
            _timer = null;
            _cts?.Dispose();
            _cts = null;
        }

        /// <summary>Run a single health-check round immediately.</summary>
        public Task CheckNowAsync(IReadOnlyList<QaEnvironment> environments) =>
            PingAllAsync(environments, CancellationToken.None);

        private async Task PingAllAsync(IReadOnlyList<QaEnvironment> environments, CancellationToken ct)
        {
            var tasks = new List<Task>();
            foreach (var env in environments)
                tasks.Add(PingOneAsync(env, ct));

            await Task.WhenAll(tasks);
            StatusChanged?.Invoke();
        }

        private async Task PingOneAsync(QaEnvironment env, CancellationToken ct)
        {
            var url = !string.IsNullOrWhiteSpace(env.HealthCheckUrl) ? env.HealthCheckUrl : env.BaseUrl;
            if (string.IsNullOrWhiteSpace(url))
            {
                _statuses[env.Id] = HealthStatus.Unknown;
                return;
            }

            try
            {
                using var response = await s_client.GetAsync(url, ct);
                _statuses[env.Id] = response.IsSuccessStatusCode ? HealthStatus.Healthy : HealthStatus.Unhealthy;
            }
            catch
            {
                _statuses[env.Id] = HealthStatus.Unhealthy;
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
