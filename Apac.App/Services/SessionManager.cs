using Apac.App.Database;
using Apac.App.Models;
using System.Text.Json;

namespace Apac.App.Services;

public class SessionManager
{
    private readonly DatabaseManager _db;
    private User? _currentUser;
    private AccessProfile? _currentProfile;
    private DateTime _sessionStart;
    private DateTime _pauseStart;
    private bool _isPaused;
    private int _totalPausedSeconds;
    private Timer? _pauseTimer;
    private Timer? _sessionTimer;
    private Action<string>? _onTimeUpdate;
    private Action? _onSessionEnd;
    private Action<string>? _onPauseStart;
    private Action? _onPauseEnd;
    private bool _pauseInProgress;
    private bool _autoPauseTriggered;

    public User? CurrentUser => _currentUser;
    public AccessProfile? CurrentProfile => _currentProfile;

    public SessionManager(DatabaseManager db)
    {
        _db = db;
    }

    public void SetCallbacks(Action<string> onTimeUpdate, Action? onSessionEnd, Action<string>? onPauseStart, Action? onPauseEnd)
    {
        _onTimeUpdate = onTimeUpdate;
        _onSessionEnd = onSessionEnd;
        _onPauseStart = onPauseStart;
        _onPauseEnd = onPauseEnd;
    }

    public bool StartSession(User user, AccessProfile? profile)
    {
        if (!user.IsActive) return false;

        if (profile != null && !IsWithinAllowedHours(profile)) return false;

        _currentUser = user;
        _currentProfile = profile;
        _sessionStart = DateTime.Now;
        _isPaused = false;
        _totalPausedSeconds = 0;
        _pauseInProgress = false;
        _autoPauseTriggered = false;

        _db.StartSession(user.Id, user.Username);
        _db.InsertLog("login", null, user.Id, user.Username, "Usuário iniciou sessão");

        _sessionTimer = new Timer(OnSessionTimerTick, null, 1000, 1000);

        if (profile != null && profile.MandatoryPauseAfterMinutes > 0)
        {
            var autoPauseMs = profile.MandatoryPauseAfterMinutes * 60 * 1000;
            _pauseTimer = new Timer(OnAutoPauseTrigger, null, autoPauseMs, Timeout.Infinite);
            _autoPauseTriggered = false;
        }

        return true;
    }

    private void OnSessionTimerTick(object? state)
    {
        if (_isPaused || _pauseInProgress) return;
        var remaining = GetRemainingTime();
        if (_currentProfile != null && _currentProfile.MaxSessionMinutes > 0 && remaining <= TimeSpan.Zero)
        {
            _onSessionEnd?.Invoke();
            return;
        }
        _onTimeUpdate?.Invoke(FormatRemainingTime());
    }

    private void OnAutoPauseTrigger(object? state)
    {
        _autoPauseTriggered = true;
        StartPause(force: true);
    }

    public void StartPause(bool force = false)
    {
        if (_isPaused) return;
        _isPaused = true;
        _pauseStart = DateTime.Now;
        _pauseInProgress = true;

        var reason = _autoPauseTriggered ? "pausa_obrigatoria" : "pausa_manual";
        _db.InsertLog(reason, null, _currentUser?.Id, _currentUser?.Username,
            _autoPauseTriggered ? "Pausa obrigatória iniciada" : "Pausa iniciada");
        _onPauseStart?.Invoke(_autoPauseTriggered
            ? "Pausa obrigatória! Seu tempo de uso contínuo atingiu o limite."
            : "Sessão pausada.");
    }

    public void EndPause()
    {
        if (!_isPaused) return;
        _isPaused = false;
        _totalPausedSeconds += (int)(DateTime.Now - _pauseStart).TotalSeconds;

        _db.InsertLog("pause_end", null, _currentUser?.Id, _currentUser?.Username, "Pausa finalizada");
        _onPauseEnd?.Invoke();

        if (_autoPauseTriggered)
        {
            _autoPauseTriggered = false;
        }

        if (_currentProfile != null && _currentProfile.MandatoryPauseAfterMinutes > 0)
        {
            _pauseTimer?.Dispose();
            _pauseTimer = new Timer(OnAutoPauseTrigger, null,
                _currentProfile.MandatoryPauseAfterMinutes * 60 * 1000, Timeout.Infinite);
        }

        _pauseInProgress = false;
    }

    public TimeSpan GetElapsedActiveTime()
    {
        var now = DateTime.Now;
        if (_isPaused) now = _pauseStart;
        var elapsed = (now - _sessionStart).TotalSeconds - _totalPausedSeconds;
        return TimeSpan.FromSeconds(Math.Max(0, elapsed));
    }

    public TimeSpan GetRemainingTime()
    {
        if (_currentProfile == null || _currentProfile.MaxSessionMinutes <= 0)
            return TimeSpan.MaxValue;

        var elapsed = GetElapsedActiveTime();
        var max = TimeSpan.FromMinutes(_currentProfile.MaxSessionMinutes);
        var remaining = max - elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    public string FormatRemainingTime()
    {
        var remaining = GetRemainingTime();
        if (remaining == TimeSpan.MaxValue)
            return "Ilimitado";
        if (remaining <= TimeSpan.Zero)
            return "00:00";
        return $"{(int)remaining.TotalHours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
    }

    public void EndSession()
    {
        _sessionTimer?.Dispose();
        _pauseTimer?.Dispose();

        if (_currentUser != null)
        {
            _db.EndSession(_currentUser.Id);
            _db.InsertLog("logout", null, _currentUser.Id, _currentUser.Username, "Usuário encerrou sessão");
        }

        _currentUser = null;
        _currentProfile = null;
        _isPaused = false;
        _pauseInProgress = false;
        _autoPauseTriggered = false;
    }

    public static bool IsWithinAllowedHours(AccessProfile profile)
    {
        try
        {
            var schedules = JsonSerializer.Deserialize<List<AllowedSchedule>>(profile.AllowedHoursJson);
            if (schedules == null || schedules.Count == 0) return true;

            var now = DateTime.Now;
            foreach (var s in schedules)
            {
                if (s.Day == now.DayOfWeek)
                {
                    var start = now.Date + s.Start;
                    var end = now.Date + s.End;
                    var currentTime = now.TimeOfDay;
                    if (currentTime >= s.Start && currentTime <= s.End)
                        return true;
                }
            }
            return false;
        }
        catch { return true; }
    }

    public static string GetNextAvailableTime(AccessProfile profile)
    {
        try
        {
            var schedules = JsonSerializer.Deserialize<List<AllowedSchedule>>(profile.AllowedHoursJson);
            if (schedules == null || schedules.Count == 0) return "";

            var now = DateTime.Now;
            for (int days = 0; days < 7; days++)
            {
                var checkDate = now.AddDays(days);
                foreach (var s in schedules)
                {
                    if (s.Day == checkDate.DayOfWeek)
                    {
                        var sTime = checkDate.Date + s.Start;
                        if (sTime > now)
                            return sTime.ToString("dd/MM/yyyy HH:mm");
                    }
                }
            }
            return "Nenhum horário disponível esta semana";
        }
        catch { return ""; }
    }

    public bool IsPaused => _isPaused;
    public DateTime SessionStart => _sessionStart;
}
