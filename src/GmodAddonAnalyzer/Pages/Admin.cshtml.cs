using GmodAddonAnalyzer.Configuration;
using GmodAddonAnalyzer.Data;
using GmodAddonAnalyzer.Data.Entities;
using GmodAddonAnalyzer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GmodAddonAnalyzer.Pages;

public sealed class AdminModel(IDbContextFactory<AnalyzerDbContext> dbFactory, AnalyzerOptions options,
    CleanupService cleanup, AdminGuardService guard) : PageModel
{
    public int TtlDays { get; } = options.CleanupTtlDays;
    public List<Addon> Addons { get; private set; } = [];

    public bool NeedsLogin { get; private set; }
    public bool IsLockedOut { get; private set; }
    public DateTime LockoutUntil { get; private set; }
    public string? LoginError { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!guard.IsConfigured)
        {
            NeedsLogin = true;
            LoginError = "Admin password is not configured. Set Analyzer:AdminPassword.";
            return Page();
        }

        if (!IsAuthorized())
        {
            NeedsLogin = true;
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
            if (guard.IsLockedOut(ip, out var until))
            {
                IsLockedOut = true;
                LockoutUntil = until;
            }
            return Page();
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        Addons = await db.Addons
            .OrderByDescending(a => a.LastFetchedAt)
            .Take(200)
            .ToListAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostLoginAsync(string password)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        if (guard.IsLockedOut(ip, out var until))
        {
            IsLockedOut = true;
            LockoutUntil = until;
            LoginError = "Too many attempts. Try again later.";
            NeedsLogin = true;
            return Page();
        }
        if (!guard.IsConfigured)
        {
            LoginError = "Admin password is not configured. Set Analyzer:AdminPassword.";
            NeedsLogin = true;
            return Page();
        }
        if (guard.TryLogin(password ?? "", ip))
        {
            var minutes = Math.Max(30, options.AdminSessionMinutes);
            Response.Cookies.Append(AdminGuardService.CookieName, guard.IssueToken(),
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Strict,
                    MaxAge = TimeSpan.FromMinutes(minutes),
                });
            return RedirectToPage();
        }
        LoginError = "Incorrect password.";
        NeedsLogin = true;
        return Page();
    }

    public async Task<IActionResult> OnPostLogoutAsync()
    {
        Response.Cookies.Delete(AdminGuardService.CookieName);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(long id)
    {
        if (!IsAuthorized()) return Forbid();
        await cleanup.DeleteArtifactsForAsync(id);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSweepAsync()
    {
        if (!IsAuthorized()) return Forbid();
        await cleanup.SweepAsync();
        return RedirectToPage();
    }

    private bool IsAuthorized() =>
        guard.IsConfigured && guard.ValidateToken(Request.Cookies[AdminGuardService.CookieName]);
}