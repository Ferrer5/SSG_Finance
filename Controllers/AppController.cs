using Microsoft.AspNetCore.Mvc;

namespace MyMvcApp.Controllers;

/// <summary>
/// Base controller for cross-cutting concerns shared across the app:
/// session-role authorization guards and the in-memory login lockout.
/// Logic is identical to the original HomeController helpers — only relocated.
/// </summary>
public abstract class AppController : Controller
{
    // ------------------------------
    // SESSION ROLE / AUTHORIZATION GUARDS
    // ------------------------------
    protected string? GetSessionRole() => HttpContext.Session.GetString("UserRole");

    protected IActionResult? RequireRole(string expectedRole)
    {
        var role = GetSessionRole();
        if (string.IsNullOrEmpty(role))
            return RedirectToAction("Login", "Home");

        if (!string.Equals(role, expectedRole, StringComparison.OrdinalIgnoreCase))
            return new ObjectResult(new { success = false, message = "You are not authorized to perform this action." })
                { StatusCode = StatusCodes.Status403Forbidden };

        return null!;
    }

    protected IActionResult? RequireAnyRole(params string[] expectedRoles)
    {
        var role = GetSessionRole();
        if (string.IsNullOrEmpty(role))
            return RedirectToAction("Login", "Home");

        foreach (var expectedRole in expectedRoles)
        {
            if (string.Equals(role, expectedRole, StringComparison.OrdinalIgnoreCase))
                return null!;
        }

        return new ObjectResult(new { success = false, message = "You are not authorized to perform this action." })
            { StatusCode = StatusCodes.Status403Forbidden };
    }

    // ------------------------------
    // LOGIN PROTECTION (simple in-memory lockout)
    // ------------------------------
    private static readonly object _loginAttemptLock = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, LoginAttemptState> _loginAttempts = new();

    private sealed class LoginAttemptState
    {
        public int FailedAttempts { get; set; }
        public DateTimeOffset? LockoutUntil { get; set; }
        public DateTimeOffset LastFailedAt { get; set; }
    }

    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    protected static bool IsTemporarilyLocked(string key)
    {
        if (!_loginAttempts.TryGetValue(key, out var state))
            return false;

        if (state.LockoutUntil == null)
            return false;

        return DateTimeOffset.UtcNow < state.LockoutUntil.Value;
    }

    protected static void RecordFailedAttempt(string key)
    {
        lock (_loginAttemptLock)
        {
            var state = _loginAttempts.GetOrAdd(key, _ => new LoginAttemptState());
            state.FailedAttempts++;
            state.LastFailedAt = DateTimeOffset.UtcNow;

            if (state.FailedAttempts >= MaxFailedAttempts)
            {
                state.LockoutUntil = DateTimeOffset.UtcNow.Add(LockoutDuration);
            }
        }
    }

    protected static void RecordSuccessfulLogin(string key)
    {
        lock (_loginAttemptLock)
        {
            _loginAttempts.TryRemove(key, out _);
        }
    }

    protected static string BuildLoginKey(string? schoolId, string? ipAddress)
    {
        // Keyed by SchoolId + IP to reduce lockouts from unrelated users sharing an IP.
        // If IP is missing, fallback to SchoolId only.
        var sid = string.IsNullOrWhiteSpace(schoolId) ? "" : schoolId.Trim().ToLowerInvariant();
        var ip = string.IsNullOrWhiteSpace(ipAddress) ? "" : ipAddress.Trim();
        return $"{sid}|{ip}";
    }
}
