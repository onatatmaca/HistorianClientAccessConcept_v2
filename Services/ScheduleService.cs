using System;
using System.Threading.Tasks;

namespace HistorianSyncTool.Services
{
    /// <summary>
    /// Owns a polling timer (15s) that fires <see cref="RunActionAsync"/> when the
    /// next-run instant elapses. The work itself is delegated — this service only
    /// handles cadence, gating, and next-run bookkeeping so MainForm stays in charge
    /// of what an actual scheduled run does.
    ///
    /// Gating rules: ignore ticks while disabled, while a run is already in flight,
    /// or before the next-run instant. On any run completion (success or failure)
    /// the next-run is recomputed from "now + interval" so a failure doesn't peg
    /// the system into an immediate retry loop.
    /// </summary>
    public class ScheduleService : IDisposable
    {
        public delegate Task RunActionAsync();

        private readonly System.Windows.Forms.Timer _timer;
        private readonly RunActionAsync _runAction;

        private bool _enabled;
        private int _intervalMinutes = 60;
        private DateTime _nextRunUtc = DateTime.MaxValue;
        private DateTime _lastRunUtc = DateTime.MinValue;
        private bool _running;

        public event EventHandler StatusChanged;

        public ScheduleService(RunActionAsync runAction)
        {
            _runAction = runAction ?? throw new ArgumentNullException(nameof(runAction));
            _timer = new System.Windows.Forms.Timer { Interval = 15000 };
            _timer.Tick += OnTick;
        }

        public bool Enabled         => _enabled;
        public int  IntervalMinutes => _intervalMinutes;
        public DateTime NextRunUtc  => _nextRunUtc;
        public DateTime LastRunUtc  => _lastRunUtc;
        public bool RunInProgress   => _running;

        public DateTime NextRunLocal => _nextRunUtc == DateTime.MaxValue
            ? DateTime.MaxValue
            : _nextRunUtc.ToLocalTime();

        /// <summary>
        /// Apply persisted configuration. Pass <paramref name="lastRunUtc"/> = MinValue
        /// to indicate the schedule has never run; a fresh next-run is scheduled
        /// one interval out from "now".
        /// </summary>
        public void Configure(bool enabled, int intervalMinutes, DateTime lastRunUtc)
        {
            _enabled = enabled;
            _intervalMinutes = Math.Max(1, intervalMinutes);
            _lastRunUtc = lastRunUtc;

            if (_enabled)
            {
                ComputeNextRun();
                _timer.Start();
            }
            else
            {
                _nextRunUtc = DateTime.MaxValue;
                _timer.Stop();
            }
            RaiseStatusChanged();
        }

        /// <summary>Run immediately, regardless of the next-run schedule.</summary>
        public async Task TriggerNowAsync()
        {
            if (_running) return;
            _running = true;
            RaiseStatusChanged();
            try
            {
                await _runAction();
            }
            finally
            {
                _lastRunUtc = DateTime.UtcNow;
                if (_enabled) ComputeNextRun();
                _running = false;
                RaiseStatusChanged();
            }
        }

        private async void OnTick(object sender, EventArgs e)
        {
            if (!_enabled || _running) return;
            if (DateTime.UtcNow < _nextRunUtc) return;

            _running = true;
            RaiseStatusChanged();
            try
            {
                await _runAction();
            }
            catch (Exception ex)
            {
                ScheduleLogger.Append($"Scheduled run threw: {ex.Message}");
            }
            finally
            {
                _lastRunUtc = DateTime.UtcNow;
                if (_enabled) ComputeNextRun();
                _running = false;
                RaiseStatusChanged();
            }
        }

        private void ComputeNextRun()
        {
            var nowUtc = DateTime.UtcNow;
            // Never ran (or last-run is corrupt/ancient) → first run one interval out.
            if (_lastRunUtc < nowUtc.AddDays(-7))
            {
                _nextRunUtc = nowUtc.AddMinutes(_intervalMinutes);
                return;
            }

            // Schedule the next interval after the last completed run. If we missed
            // several intervals (laptop asleep, etc.), don't run a backlog — just kick
            // one off in 30 seconds.
            _nextRunUtc = _lastRunUtc.AddMinutes(_intervalMinutes);
            if (_nextRunUtc < nowUtc) _nextRunUtc = nowUtc.AddSeconds(30);
        }

        private void RaiseStatusChanged()
        {
            try { StatusChanged?.Invoke(this, EventArgs.Empty); }
            catch { /* never let UI handlers crash the schedule */ }
        }

        public void Dispose()
        {
            try { _timer.Stop(); _timer.Dispose(); } catch { }
        }
    }
}
