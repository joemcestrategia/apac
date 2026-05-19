using System;
using System.Collections.Generic;
using System.Linq;
using Apac.Database;
using Apac.Models;

namespace Apac.Services
{
    public class SessionManager
    {
        public User CurrentUser { get; private set; }
        public ActiveSession CurrentSession { get; private set; }
        public DateTime SessionStartTime { get; private set; }
        public TimeSpan TotalPausedTime { get; private set; }
        public DateTime? PauseStartTime { get; private set; }
        public bool IsPaused { get; private set; }
        public bool IsEmergencyUnlocked { get; set; }

        private System.Windows.Forms.Timer _pauseTimer;
        private int _pauseRemainingSeconds;

        public event Action<int> PauseRemainingChanged;
        public event Action PauseCompleted;

        public SessionManager(User user)
        {
            CurrentUser = user;
            SessionStartTime = DateTime.Now;
            CurrentSession = DatabaseService.Instance.StartSession(user.Id);
        }

        public void EndSession()
        {
            if (CurrentSession != null)
            {
                DatabaseService.Instance.EndSession(CurrentSession.Id);
                CurrentSession = null;
            }
            if (_pauseTimer != null)
            {
                _pauseTimer.Stop();
                _pauseTimer.Dispose();
                _pauseTimer = null;
            }
        }

        public TimeSpan GetElapsedSessionTime()
        {
            var elapsed = DateTime.Now - SessionStartTime - TotalPausedTime;
            return elapsed;
        }

        public bool IsWithinTimeRules()
        {
            if (CurrentUser.ProfileId == null) return true;

            var profile = DatabaseService.Instance.GetProfileById(CurrentUser.ProfileId.Value);
            if (profile == null) return true;

            var rules = DatabaseService.Instance.GetTimeRulesForProfile(profile.Id);
            if (rules.Count == 0) return true;

            var now = DateTime.Now;
            var currentDayOfWeek = (int)now.DayOfWeek;
            var currentTime = now.ToString("HH:mm");

            var applicableRules = rules.Where(r =>
                r.DayOfWeek == null || r.DayOfWeek == currentDayOfWeek
            ).ToList();

            if (applicableRules.Count == 0) return true;

            return applicableRules.Any(r =>
                string.Compare(currentTime, r.StartTime) >= 0 &&
                string.Compare(currentTime, r.EndTime) <= 0
            );
        }

        public DateTime? GetNextAvailableTime()
        {
            if (CurrentUser.ProfileId == null) return null;

            var rules = DatabaseService.Instance.GetTimeRulesForProfile(CurrentUser.ProfileId.Value);
            if (rules.Count == 0) return null;

            var now = DateTime.Now;
            var currentDayOfWeek = (int)now.DayOfWeek;
            var currentTime = now.ToString("HH:mm");

            DateTime? nextTime = null;

            for (int dayOffset = 0; dayOffset <= 7; dayOffset++)
            {
                var checkDate = now.Date.AddDays(dayOffset);
                var checkDayOfWeek = (int)checkDate.DayOfWeek;

                var daysRules = rules.Where(r =>
                    r.DayOfWeek == null || r.DayOfWeek == checkDayOfWeek
                ).OrderBy(r => r.StartTime);

                foreach (var rule in daysRules)
                {
                    var startTime = rule.StartTime;
                    if (dayOffset == 0 && string.Compare(currentTime, startTime) >= 0)
                    {
                        if (string.Compare(currentTime, rule.EndTime) <= 0)
                            return now;
                        continue;
                    }

                    var next = checkDate.AddHours(
                        int.Parse(startTime.Split(':')[0]))
                        .AddMinutes(int.Parse(startTime.Split(':')[1]));

                    if (next < now) continue;
                    if (nextTime == null || next < nextTime)
                        nextTime = next;
                }
            }

            return nextTime;
        }

        public bool CheckSessionLimit(out int remainingMinutes)
        {
            remainingMinutes = int.MaxValue;
            if (CurrentUser.ProfileId == null) return true;

            var profile = DatabaseService.Instance.GetProfileById(CurrentUser.ProfileId.Value);
            if (profile == null) return true;

            if (profile.MaxSessionMinutes > 0)
            {
                var elapsed = GetElapsedSessionTime().TotalMinutes;
                remainingMinutes = profile.MaxSessionMinutes - (int)elapsed;
                if (remainingMinutes <= 0)
                    return false;
            }

            return true;
        }

        public bool CheckDailyLimit(out int remainingMinutes)
        {
            remainingMinutes = int.MaxValue;
            if (CurrentUser.ProfileId == null) return true;

            var profile = DatabaseService.Instance.GetProfileById(CurrentUser.ProfileId.Value);
            if (profile == null) return true;

            if (profile.MaxDailyMinutes > 0)
            {
                var used = DatabaseService.Instance.GetDailyUsageMinutes(CurrentUser.Id);
                remainingMinutes = profile.MaxDailyMinutes - used;
                if (remainingMinutes <= 0)
                    return false;
            }

            return true;
        }

        public bool CheckMandatoryPause(out int pauseAfterMinutes, out int pauseDurationMinutes)
        {
            pauseAfterMinutes = 0;
            pauseDurationMinutes = 0;

            if (CurrentUser.ProfileId == null) return false;

            var profile = DatabaseService.Instance.GetProfileById(CurrentUser.ProfileId.Value);
            if (profile == null) return false;

            pauseAfterMinutes = profile.MandatoryPauseAfterMinutes;
            pauseDurationMinutes = profile.MandatoryPauseMinutes;

            if (pauseAfterMinutes <= 0 || pauseDurationMinutes <= 0)
                return false;

            var elapsed = GetElapsedSessionTime().TotalMinutes;
            return elapsed >= pauseAfterMinutes;
        }

        public void StartMandatoryPause(int durationMinutes)
        {
            IsPaused = true;
            PauseStartTime = DateTime.Now;
            _pauseRemainingSeconds = durationMinutes * 60;

            _pauseTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _pauseTimer.Tick += (s, e) =>
            {
                _pauseRemainingSeconds--;
                PauseRemainingChanged?.Invoke(_pauseRemainingSeconds);

                if (_pauseRemainingSeconds <= 0)
                {
                    _pauseTimer.Stop();
                    IsPaused = false;
                    TotalPausedTime += (DateTime.Now - PauseStartTime.Value);
                    PauseStartTime = null;
                    PauseCompleted?.Invoke();
                }
            };
            _pauseTimer.Start();
        }

        public string GetRemainingTimeDisplay()
        {
            if (!CheckSessionLimit(out int sessionRemaining))
                return "00:00";
            if (!CheckDailyLimit(out int dailyRemaining))
                return "00:00";

            int remaining = Math.Min(sessionRemaining, dailyRemaining);
            if (remaining == int.MaxValue) return "--:--";

            return $"{(remaining / 60):D2}:{(remaining % 60):D2}";
        }
    }
}