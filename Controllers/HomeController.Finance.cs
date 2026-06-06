using System.Diagnostics;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BCrypt.Net;
using MyMvcApp.Models;
using MyMvcApp.Services;
using MyMvcApp.Data;

namespace MyMvcApp.Controllers;

public partial class HomeController : AppController
{
    [HttpGet]
    public async Task<IActionResult> GetSchoolYears()
    {
        try
        {
            var schoolYears = await _context.SchoolYears
                .OrderByDescending(sy => sy.YearStart)
                .ToListAsync();

            var feeRecords = await _context.FullAmounts
                .Where(f => f.Amount > 0)
                .Select(f => new { f.SchoolYearId, f.Semester })
                .ToListAsync();

            var result = schoolYears.Select(sy => new {
                sy.SchoolYearId,
                sy.YearStart,
                sy.YearEnd,
                yearStatus     = sy.YearStatus.ToString(),
                hasFirst       = sy.FirstSemStart != null || feeRecords.Any(f => f.SchoolYearId == sy.SchoolYearId && f.Semester == Semester.First),
                hasSecond      = sy.SecondSemStart != null || feeRecords.Any(f => f.SchoolYearId == sy.SchoolYearId && f.Semester == Semester.Second),
                firstSemStart  = (object)sy.FirstSemStart,
                firstSemEnd    = (object)sy.FirstSemEnd,
                secondSemStart = (object)sy.SecondSemStart,
                secondSemEnd   = (object)sy.SecondSemEnd,
            });

            return Json(new { success = true, schoolYears = result });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetSchoolYearDateRange(string schoolYear)
    {
        try
        {
            var parts = schoolYear.Replace("–", "-").Split('-');
            if (parts.Length != 2 || !int.TryParse(parts[0].Trim(), out var ys) || !int.TryParse(parts[1].Trim(), out var ye))
                return Json(new { success = false, message = "Invalid school year format." });

            var sy = await _context.SchoolYears
                .FirstOrDefaultAsync(s => s.YearStart == ys && s.YearEnd == ye);

            if (sy == null)
                return Json(new { success = false, message = "School year not found." });

            return Json(new
            {
                success = true,
                firstSemStart  = sy.FirstSemStart,
                firstSemEnd    = sy.FirstSemEnd,
                secondSemStart = sy.SecondSemStart,
                secondSemEnd   = sy.SecondSemEnd
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddSchoolYear([FromBody] AddSchoolYearRequest request)

    {
        var guard = RequireAnyRole("Treasurer", "Admin");
        if (guard != null) return guard;

        try
        {

            if (request.YearEnd != request.YearStart + 1)
                return Json(new { success = false, message = "Year end must be exactly year start + 1." });

            var duplicate = await _context.SchoolYears
                .AnyAsync(sy => sy.YearStart == request.YearStart && sy.YearEnd == request.YearEnd);
            if (duplicate)
                return Json(new { success = false, message = "This school year already exists." });

            var existing = await _context.SchoolYears.ToListAsync();
            foreach (var sy in existing)
                sy.YearStatus = YearStatus.Ended;

            _context.SchoolYears.Add(new SchoolYear
            {
                YearStart      = request.YearStart,
                YearEnd        = request.YearEnd,
                YearStatus     = YearStatus.Current,
                FirstSemStart  = request.FirstSemStart,
                FirstSemEnd    = request.FirstSemEnd,
                SecondSemStart = request.SecondSemStart,
                SecondSemEnd   = request.SecondSemEnd
            });

            // Auto-advance year level when a new school year starts.
            // Only currently ENROLLED students advance. A student advances up to
            // year level 5, which represents completion of the 4-year program — at
            // that point they are marked Graduated. Transferred/Graduated/Dropped
            // students and those with no year level are left untouched.
            const int graduatingYearLevel = 5;

            var enrolledStudents = await _context.AcademicProfiles
                .Where(ap => ap.AcademicStatus == AcademicStatus.Enrolled
                          && ap.YearLevel != null)
                .ToListAsync();

            foreach (var profile in enrolledStudents)
            {
                if (profile.YearLevel < graduatingYearLevel)
                {
                    profile.YearLevel += 1;

                    // Reaching level 5 means they've completed the program.
                    if (profile.YearLevel == graduatingYearLevel)
                        profile.AcademicStatus = AcademicStatus.Graduated;
                }
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "School year added successfully." });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSchoolYear([FromBody] DeleteSchoolYearRequest request)

    {
        var guard = RequireAnyRole("Treasurer", "Admin");
        if (guard != null) return guard;

        try
        {

            var sy = await _context.SchoolYears
                .Include(s => s.FullAmounts)
                .FirstOrDefaultAsync(s => s.SchoolYearId == request.SchoolYearId);

            if (sy == null)
                return Json(new { success = false, message = "School year not found." });

            // Check for payments tied to this school year's fees
            var feeIds = sy.FullAmounts.Select(f => f.FullAmountId).ToList();
            var hasPayments = feeIds.Any() && await _context.OrgFeePayments
                .AnyAsync(p => feeIds.Contains(p.FullAmountId));

            if (hasPayments)
                return Json(new { success = false, message = "Cannot delete — this school year has existing payment records. Delete the payments first." });

            // Check for funds or expenses
            var hasFunds = await _context.OtherFunds
                .AnyAsync(f => f.SchoolYearId == request.SchoolYearId);

            var hasExpenses = await _context.Expenses
                .AnyAsync(e => e.SchoolYearId == request.SchoolYearId);

            if (hasFunds || hasExpenses)
                return Json(new { success = false, message = "Cannot delete — this school year has existing fund or expense records." });

            if (sy.FullAmounts.Any())
                _context.FullAmounts.RemoveRange(sy.FullAmounts);

            // If the CURRENT school year is being deleted, reverse the promotion that
            // happened when it was added: decrement year levels by 1. Students who had
            // graduated by reaching level 5 are returned to level 4 and re-enrolled.
            // Old/ended years don't trigger this, since they didn't cause the latest promotion.
            if (sy.YearStatus == YearStatus.Current)
            {
                const int graduatingYearLevel = 5;

                var profilesToRevert = await _context.AcademicProfiles
                    .Where(ap => ap.YearLevel != null
                              && ap.YearLevel > 1
                              && (ap.AcademicStatus == AcademicStatus.Enrolled
                               || ap.AcademicStatus == AcademicStatus.Graduated))
                    .ToListAsync();

                foreach (var profile in profilesToRevert)
                {
                    // A graduated (level 5) student goes back to level 4 and becomes Enrolled again.
                    if (profile.YearLevel == graduatingYearLevel
                        && profile.AcademicStatus == AcademicStatus.Graduated)
                    {
                        profile.YearLevel -= 1;
                        profile.AcademicStatus = AcademicStatus.Enrolled;
                    }
                    // Otherwise only enrolled students step back a year.
                    else if (profile.AcademicStatus == AcademicStatus.Enrolled)
                    {
                        profile.YearLevel -= 1;
                    }
                }
            }

            _context.SchoolYears.Remove(sy);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "School year deleted successfully." });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetSchoolYearStatus([FromBody] SetSchoolYearStatusRequest request)

    {
        var guard = RequireAnyRole("Treasurer", "Admin");
        if (guard != null) return guard;

        try
        {

            if (request.Status == "Current")
            {
                var allYears = await _context.SchoolYears.ToListAsync();
                foreach (var sy in allYears)
                    sy.YearStatus = YearStatus.Ended;
            }

            var schoolYear = await _context.SchoolYears.FindAsync(request.SchoolYearId);
            if (schoolYear == null)
                return Json(new { success = false, message = "School year not found." });

            schoolYear.YearStatus = request.Status == "Current"
                ? YearStatus.Current
                : YearStatus.Ended;

            await _context.SaveChangesAsync();
            return Json(new { success = true, message = $"School year set as {request.Status}." });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateSchoolYearDates([FromBody] UpdateSchoolYearDatesRequest request)

    {
        var guard = RequireAnyRole("Treasurer", "Admin");
        if (guard != null) return guard;

        try
        {

            var sy = await _context.SchoolYears.FindAsync(request.SchoolYearId);
            if (sy == null)
                return Json(new { success = false, message = "School year not found." });

            sy.FirstSemStart  = request.FirstSemStart;
            sy.FirstSemEnd    = request.FirstSemEnd;
            sy.SecondSemStart = request.SecondSemStart;
            sy.SecondSemEnd   = request.SecondSemEnd;

            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Semester dates updated successfully." });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> AddCourse([FromBody] AddCourseRequest request)
    {
        var guard = RequireAnyRole("Treasurer", "Admin");
        if (guard != null) return guard;

        try
        {

            if (string.IsNullOrWhiteSpace(request.CourseCode))
                return Json(new { success = false, message = "Course code is required." });

            var existing = await _context.Courses
                .FirstOrDefaultAsync(c => c.CourseCode.ToLower() == request.CourseCode.ToLower());

            if (existing != null)
                return Json(new { success = false, message = "That course code already exists." });

            _context.Courses.Add(new Course {
                CourseCode = request.CourseCode.ToUpper(),
                CourseName = request.CourseName
            });

            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Course added successfully." });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteCourse([FromBody] DeleteCourseRequest request)
    {
        var guard = RequireAnyRole("Treasurer", "Admin");
        if (guard != null) return guard;

        try
        {

            var course = await _context.Courses
                .FirstOrDefaultAsync(c => c.CourseId == request.CourseId);

            if (course == null)
                return Json(new { success = false, message = "Course not found." });

            var inUse = await _context.AcademicProfiles
                .AnyAsync(ap => ap.CourseId == request.CourseId);

            if (inUse)
                return Json(new { success = false, message = "Cannot delete — students are currently assigned to this course." });

            _context.Courses.Remove(course);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Course deleted successfully." });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetFees()
    {
        var guard = RequireAnyRole("Treasurer", "Admin", "Professor");
        if (guard != null) return guard;

        try
        {
            var fees = await _context.FullAmounts
                .Include(f => f.SchoolYear)
                .Where(f => f.Amount > 0)
                .OrderByDescending(f => f.SchoolYear.YearStart)
                .ThenByDescending(f => f.Semester)
                .ToListAsync();

            var latestFirst = fees
                .Where(f => f.Semester == Semester.First)
                .OrderByDescending(f => f.SchoolYear.YearStart)
                .FirstOrDefault();

            var latestSecond = fees
                .Where(f => f.Semester == Semester.Second)
                .OrderByDescending(f => f.SchoolYear.YearStart)
                .FirstOrDefault();

            var result = fees.Select(f => new {
                f.FullAmountId,
                schoolYear     = f.SchoolYear.YearStart + " – " + f.SchoolYear.YearEnd,
                semester       = f.Semester.ToString(),
                amount         = f.Amount,
                semesterStatus = f.SemesterStatus.ToString(),
                // used by the summary cards: latest record per semester (not the "Current/Ended" status)
                isLatest       = f.FullAmountId == latestFirst?.FullAmountId ||
                                 f.FullAmountId == latestSecond?.FullAmountId
            });

            return Json(new { success = true, fees = result });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> SetFeeAmount([FromBody] SetFeeAmountRequest request)
    {
        var guard = RequireAnyRole("Treasurer", "Admin");
        if (guard != null) return guard;

        try
        {

            if (request.Amount <= 0)
                return Json(new { success = false, message = "Amount must be greater than zero." });

            var semesterInput = (request.Semester ?? string.Empty).Trim();



            Semester semester;
            if (semesterInput.Equals("1st", StringComparison.OrdinalIgnoreCase) ||
                semesterInput.Equals("First", StringComparison.OrdinalIgnoreCase) ||
                semesterInput.Equals("1st Semester", StringComparison.OrdinalIgnoreCase))
            {
                semester = Semester.First;

            }
            else if (semesterInput.Equals("2nd", StringComparison.OrdinalIgnoreCase) ||
                     semesterInput.Equals("Second", StringComparison.OrdinalIgnoreCase) ||
                     semesterInput.Equals("2nd Semester", StringComparison.OrdinalIgnoreCase))
            {
                semester = Semester.Second;

            }
            else
            {

                return Json(new { success = false, message = $"Invalid semester value received: '{semesterInput}'" });
            }

            var sy = await _context.SchoolYears
                .FirstOrDefaultAsync(s => s.SchoolYearId == request.SchoolYearId);

            if (sy == null)
                return Json(new { success = false, message = "School year not found." });

            // Enforce exactly one Current semester overall.
            var currentFees = await _context.FullAmounts
                .Where(f => f.SemesterStatus == SemesterStatus.Current)
                .ToListAsync();
            foreach (var f in currentFees)
                f.SemesterStatus = SemesterStatus.Ended;

            // Upsert per (SchoolYearId, Semester) so we don't violate the unique key.
            var existing = await _context.FullAmounts.FirstOrDefaultAsync(f =>
                f.SchoolYearId == request.SchoolYearId && f.Semester == semester);

            if (existing != null)
            {
                existing.Amount = request.Amount;
                existing.SemesterStatus = SemesterStatus.Current;
            }
            else
            {
                _context.FullAmounts.Add(new FullAmount
                {
                    SchoolYearId   = request.SchoolYearId,
                    Semester       = semester,
                    Amount         = request.Amount,
                    SemesterStatus = SemesterStatus.Current
                });
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Fee amount set successfully." });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> ChangeAdminPassword([FromBody] ChangePasswordRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.CurrentPassword) ||
                string.IsNullOrWhiteSpace(request.NewPassword) ||
                string.IsNullOrWhiteSpace(request.ConfirmPassword))
            {
                return Json(new { success = false, message = "All password fields are required." });
            }

            if (request.NewPassword != request.ConfirmPassword)
                return Json(new { success = false, message = "New password and confirmation do not match." });

            if (!IsPasswordCompliant(request.NewPassword, out var passwordPolicyError))
                return Json(new { success = false, message = passwordPolicyError ?? "Password does not meet policy requirements." });


            var accountIdStr = HttpContext.Session.GetString("AccountId");
            if (!int.TryParse(accountIdStr, out var accountId))
                return Json(new { success = false, message = "Unable to determine your account. Please sign in again." });

            var account = await _context.Accounts.FindAsync(accountId);
            if (account == null)
                return Json(new { success = false, message = "Account not found." });

            if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, account.PasswordHash))
                return Json(new { success = false, message = "Current password is incorrect." });

            account.PasswordHash = AuthService.HashPassword(request.NewPassword);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Password changed successfully." });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteFee([FromBody] DeleteFeeRequest request)
    {
        var guard = RequireAnyRole("Treasurer", "Admin");
        if (guard != null) return guard;

        try
        {

            var fee = await _context.FullAmounts
                .FirstOrDefaultAsync(f => f.FullAmountId == request.FullAmountId);

            if (fee == null)
                return Json(new { success = false, message = "Fee record not found." });

            // Block if payments exist
            var hasPayments = await _context.OrgFeePayments
                .AnyAsync(p => p.FullAmountId == request.FullAmountId);

            if (hasPayments)
                return Json(new { success = false, message = "Cannot delete — this semester has existing payment records." });

            var deletedWasCurrent = fee.SemesterStatus == SemesterStatus.Current;
            _context.FullAmounts.Remove(fee);
            await _context.SaveChangesAsync();

            if (deletedWasCurrent)
            {
                var nextCurrent = await _context.FullAmounts
                    .Include(f => f.SchoolYear)
                    .OrderByDescending(f => f.SchoolYear.YearStart)
                    .ThenByDescending(f => f.Semester)
                    .FirstOrDefaultAsync();

                if (nextCurrent != null)
                {
                    nextCurrent.SemesterStatus = SemesterStatus.Current;
                    await _context.SaveChangesAsync();
                }
            }

            return Json(new { success = true, message = "Fee record deleted successfully." });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStudentPaymentStart([FromBody] SetStudentPaymentStartRequest request)
    {
        var guard = RequireAnyRole("Treasurer", "Admin");
        if (guard != null) return guard;

        try
        {
            if (!Enum.TryParse<Semester>(request.Semester, out var semester))
                return Json(new { success = false, message = "Invalid semester value." });

            var sy = await _context.SchoolYears.FindAsync(request.SchoolYearId);
            if (sy == null)
                return Json(new { success = false, message = "School year not found." });

            var profile = await _context.AcademicProfiles
                .FirstOrDefaultAsync(ap => ap.UserId == request.UserId);
            if (profile == null)
                return Json(new { success = false, message = "Student academic profile not found." });

            profile.SchoolYearId    = request.SchoolYearId;
            profile.SemesterEntered = semester;
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Payment start updated." });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    // ----------------------------------------------------------------
    // STUDENT FEE EXEMPTIONS
    // ----------------------------------------------------------------

    [HttpGet]
    public async Task<IActionResult> GetStudentExemptions(int userId)
    {
        var guard = RequireAnyRole("Treasurer", "Admin");
        if (guard != null) return guard;

        try
        {
            var exemptions = await _context.StudentFeeExemptions
                .Include(e => e.SchoolYear)
                .Where(e => e.UserId == userId)
                .OrderBy(e => e.SchoolYear.YearStart)
                .ThenBy(e => e.Semester)
                .Select(e => new
                {
                    e.ExemptionId,
                    e.SchoolYearId,
                    yearLabel = $"{e.SchoolYear.YearStart}–{e.SchoolYear.YearEnd}",
                    semester  = e.Semester.ToString()
                })
                .ToListAsync();

            return Json(new { success = true, exemptions });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddStudentExemption([FromBody] StudentExemptionRequest request)
    {
        var guard = RequireAnyRole("Treasurer", "Admin");
        if (guard != null) return guard;

        try
        {
            if (!Enum.TryParse<Semester>(request.Semester, out var semester))
                return Json(new { success = false, message = "Invalid semester." });

            var exists = await _context.StudentFeeExemptions
                .AnyAsync(e => e.UserId == request.UserId
                            && e.SchoolYearId == request.SchoolYearId
                            && e.Semester == semester);
            if (exists)
                return Json(new { success = false, message = "Exemption already exists for that semester." });

            _context.StudentFeeExemptions.Add(new StudentFeeExemption
            {
                UserId       = request.UserId,
                SchoolYearId = request.SchoolYearId,
                Semester     = semester
            });
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Exemption added." });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveStudentExemption([FromBody] RemoveStudentExemptionRequest request)
    {
        var guard = RequireAnyRole("Treasurer", "Admin");
        if (guard != null) return guard;

        try
        {
            var exemption = await _context.StudentFeeExemptions
                .FirstOrDefaultAsync(e => e.ExemptionId == request.ExemptionId);
            if (exemption == null)
                return Json(new { success = false, message = "Exemption not found." });

            _context.StudentFeeExemptions.Remove(exemption);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Exemption removed." });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred." });
        }
    }

    // ----------------------------------------------------------------
    // TREASURER FINANCIAL MANAGEMENT
    // ----------------------------------------------------------------

    [HttpGet]
    public async Task<IActionResult> GetCurrentSchoolYearAndSemester()
    {
        var guard = RequireAnyRole("Treasurer", "Admin", "Professor");
        if (guard != null) return guard;

        try
        {
            var currentSchoolYear = await _context.SchoolYears
                .FirstOrDefaultAsync(sy => sy.YearStatus == YearStatus.Current);

            var currentSemester = await _context.FullAmounts
                .Include(f => f.SchoolYear)
                .FirstOrDefaultAsync(f => f.SemesterStatus == SemesterStatus.Current);

            if (currentSchoolYear == null)
                return Json(new { success = false, message = "No current school year found." });

            return Json(new
            {
                success = true,
                schoolYear = new
                {
                    currentSchoolYear.SchoolYearId,
                    currentSchoolYear.YearStart,
                    currentSchoolYear.YearEnd,
                    yearStatus = currentSchoolYear.YearStatus.ToString()
                },
                semester = currentSemester != null ? new
                {
                    currentSemester.FullAmountId,
                    currentSemester.SchoolYearId,
                    semester = currentSemester.Semester.ToString(),
                    amount = currentSemester.Amount,
                    semesterStatus = currentSemester.SemesterStatus.ToString()
                } : null
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetOrgFeePayments()
    {
        var guard = RequireAnyRole("Treasurer", "Admin");
        if (guard != null) return guard;

        try
        {

            var payments = await _context.OrgFeePayments
                .Include(p => p.User)
                    .ThenInclude(u => u.AcademicProfile)
                        .ThenInclude(ap => ap.Course)
                .Include(p => p.User)
                    .ThenInclude(u => u.Account)
                .Include(p => p.FullAmount)
                    .ThenInclude(f => f.SchoolYear)
                .Include(p => p.Receipts)
                .OrderByDescending(p => p.PaymentDate)
                .Select(p => new
                {
                    p.PaymentId,
                    p.UserId,
                    studentName = p.User != null
                        ? $"{(p.User.LastName  != null ? p.User.LastName.ToUpper()  : "")}, " +
                          $"{(p.User.FirstName != null ? p.User.FirstName.ToUpper() : "")}"
                        : "Unknown",
                    schoolId = p.User != null && p.User.Account != null ? p.User.Account.SchoolId ?? "" : "",
                    courseCode = p.User.AcademicProfile != null && p.User.AcademicProfile.Course != null
                        ? p.User.AcademicProfile.Course.CourseCode : "N/A",
                    yearSection = p.User.AcademicProfile != null
                        ? $"{(p.User.AcademicProfile.YearLevel.HasValue ? p.User.AcademicProfile.YearLevel.Value.ToString() : "N/A")}" +
                          $"-{(p.User.AcademicProfile.Section ?? "N/A")}"
                        : "N/A",
                    // school year and semester now come from FullAmount
                    schoolYear     = p.FullAmount.SchoolYear != null
                        ? $"{p.FullAmount.SchoolYear.YearStart} – {p.FullAmount.SchoolYear.YearEnd}" : "N/A",
                    semester       = p.FullAmount.Semester.ToString(),
                    amountRequired = p.FullAmount.Amount,    // required amount lives in full_amount table
                    p.Amount,                                // amount actually paid
                    paymentStatus  = p.PaymentStatus.ToString(),
                    p.PaymentDate,
                    receiptNumber  = p.Receipts.FirstOrDefault() != null
                        ? p.Receipts.FirstOrDefault()!.ReceiptNumber : null
                })
                .ToListAsync();

            return Json(new { success = true, payments });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetRecentPayments()
    {
        var guard = RequireAnyRole("Treasurer", "Admin", "Professor");
        if (guard != null) return guard;

        try
        {

            var recentPayments = await _context.OrgFeePayments
                .Include(p => p.User)
                    .ThenInclude(u => u.Account)
                .Include(p => p.User)
                    .ThenInclude(u => u.AcademicProfile)
                        .ThenInclude(ap => ap!.Course)
                .Where(p => p.PaymentStatus == PaymentStatus.Paid
                         || p.PaymentStatus == PaymentStatus.Partial)
                .OrderByDescending(p => p.PaymentDate)
                .Take(10)
                .ToListAsync();

            var result = recentPayments.Select(p => new
            {
                p.PaymentId,
                p.UserId,
                studentName = p.User != null
                    ? $"{(p.User.LastName ?? "").ToUpper()}, {(p.User.FirstName ?? "") }"
                      + (!string.IsNullOrWhiteSpace(p.User.MiddleName)
                          ? " " + p.User.MiddleName.Substring(0, 1) + "."
                          : "")
                    : "Unknown",
                schoolId = p.User?.Account?.SchoolId ?? "",
                courseCode = p.User?.AcademicProfile?.Course?.CourseCode ?? "N/A",
                yearSection = p.User?.AcademicProfile != null
                    ? $"{(p.User.AcademicProfile.YearLevel?.ToString() ?? "N/A")}-{(p.User.AcademicProfile.Section ?? "N/A")}"
                    : "N/A",
                amount = p.Amount,
                paymentStatus = p.PaymentStatus.ToString(),
                paymentDate = p.PaymentDate
            }).ToList();

            return Json(new { success = true, payments = result });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetProfessorStudentPayments()
    {
        var guard = RequireAnyRole("Treasurer", "Admin", "Professor");
        if (guard != null) return guard;

        try
        {

            var users = await _context.Users
                .Include(u => u.Account)
                .Include(u => u.AcademicProfile)
                    .ThenInclude(ap => ap!.Course)
                .Include(u => u.AcademicProfile)
                    .ThenInclude(ap => ap!.SchoolYear)
                .Where(u => u.Account != null
                         && (u.Account.Role == UserRole.Student || u.Account.Role == UserRole.Treasurer)
                         && u.Account.IsActive
                         && u.Account.RequestStatus == RequestStatus.Approved)
                .ToListAsync();

            var currentFee = await _context.FullAmounts
                .Include(f => f.SchoolYear)
                .FirstOrDefaultAsync(f => f.SemesterStatus == SemesterStatus.Current);

            var payments = currentFee != null
                ? await _context.OrgFeePayments
                    .Where(p => p.FullAmountId == currentFee.FullAmountId)
                    .ToListAsync()
                : new List<OrgFeePayment>();

            var allExemptions = await GetAllExemptionsAsync();

            var result = users.Select(u =>
            {
                var studentPayments = payments
                    .Where(p => p.UserId == u.UserId)
                    .ToList();

                allExemptions.TryGetValue(u.UserId, out var userExemptions);
                var totalPaid = studentPayments.Sum(p => p.Amount);
                var isApplicable = currentFee != null && FeeRules.IsFeeApplicableToStudent(u.AcademicProfile, currentFee, userExemptions);
                var requiredAmount = isApplicable ? currentFee?.Amount ?? 0 : 0;
                var lastPayment = studentPayments.OrderByDescending(p => p.PaymentDate).FirstOrDefault();

                string status;
                if (!isApplicable)
                    status = "N/A";
                else if (totalPaid <= 0)
                    status = "Unpaid";
                else if (totalPaid >= requiredAmount)
                    status = "Paid";
                else
                    status = "Partial";

                return new
                {
                    userId = u.UserId,
                    schoolId = u.Account!.SchoolId,
                    name = $"{(u.LastName ?? "").ToUpper()}, {(u.FirstName ?? "") }"
                          + (!string.IsNullOrWhiteSpace(u.MiddleName)
                              ? " " + u.MiddleName.Substring(0, 1) + "."
                              : ""),
                    courseCode = u.AcademicProfile?.Course?.CourseCode ?? "N/A",
                    yearSection = u.AcademicProfile != null
                        ? $"{(u.AcademicProfile.YearLevel?.ToString() ?? "N/A")}-{(u.AcademicProfile.Section ?? "N/A")}"
                        : "N/A",
                    totalPaid,
                    requiredAmount,
                    status,
                    hasPaid = status == "Paid",
                    lastPaymentDate = lastPayment?.PaymentDate
                };
            })
            .OrderBy(s => s.name)
            .ToList();

            return Json(new { success = true, students = result });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    private static string ComputeOrgFeeStatus(decimal totalPaid, decimal requiredAmount)
    {
        if (requiredAmount <= 0) return "N/A";
        if (totalPaid <= 0) return "Unpaid";
        if (totalPaid >= requiredAmount) return "Paid";
        return "Partial";
    }

    private static object? BuildSemesterFeeSummary(FullAmount? fee, IEnumerable<OrgFeePayment> payments, bool isApplicable = true)
    {
        if (fee == null) return null;

        var totalPaid = payments.Sum(p => p.Amount);
        var requiredAmount = isApplicable ? fee.Amount : 0m;

        return new
        {
            requiredAmount,
            totalPaid,
            balance = Math.Max(0, requiredAmount - totalPaid),
            feeStatus = isApplicable ? ComputeOrgFeeStatus(totalPaid, requiredAmount) : "N/A",
            semesterStatus = fee.SemesterStatus.ToString()
        };
    }

    [HttpGet]
    public async Task<IActionResult> GetTreasurerStudentsWithFees(string? schoolYear = null)
    {
        var guard = RequireAnyRole("Treasurer", "Admin", "Professor");
        if (guard != null) return guard;

        try
        {

            var students = (await GetStudentsAsync())
                .Where(s => string.Equals(s.AcademicStatus, "Enrolled", StringComparison.OrdinalIgnoreCase))
                .ToList();

            SchoolYear? currentSchoolYear;

            if (!string.IsNullOrWhiteSpace(schoolYear))
            {
                // Parse "2025–2026" or "2025-2026"
                var parts = schoolYear.Replace("–", "-").Split('-');
                if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out var ys) && int.TryParse(parts[1].Trim(), out var ye))
                {
                    currentSchoolYear = await _context.SchoolYears
                        .FirstOrDefaultAsync(sy => sy.YearStart == ys && sy.YearEnd == ye);
                }
                else
                {
                    currentSchoolYear = await _context.SchoolYears
                        .FirstOrDefaultAsync(sy => sy.YearStatus == YearStatus.Current);
                }
            }
            else
            {
                currentSchoolYear = await _context.SchoolYears
                    .FirstOrDefaultAsync(sy => sy.YearStatus == YearStatus.Current);
            }

            var semesterFees = currentSchoolYear != null
                ? await _context.FullAmounts
                    .Where(f => f.SchoolYearId == currentSchoolYear.SchoolYearId)
                    .ToListAsync()
                : new List<FullAmount>();

            var firstSemFee  = semesterFees.FirstOrDefault(f => f.Semester == Semester.First);
            var secondSemFee = semesterFees.FirstOrDefault(f => f.Semester == Semester.Second);

            var feeIds = semesterFees.Select(f => f.FullAmountId).ToList();
            var allSemPayments = feeIds.Count > 0
                ? await _context.OrgFeePayments
                    .Include(p => p.Receipts)
                    .Where(p => feeIds.Contains(p.FullAmountId))
                    .ToListAsync()
                : new List<OrgFeePayment>();

            var schoolYearLabel = currentSchoolYear != null
                ? $"{currentSchoolYear.YearStart}–{currentSchoolYear.YearEnd}"
                : "N/A";

            var studentsPageExemptions = await GetAllExemptionsAsync();

            var result = students.Select(s =>
            {
                var studentPayments = allSemPayments.Where(p => p.UserId == s.StudentId).ToList();
                studentsPageExemptions.TryGetValue(s.StudentId, out var sExemptions);
                // Inactive students no longer owe any organizational fees.
                var isInactive = string.Equals(s.AcademicStatus, "Graduated", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(s.AcademicStatus, "Dropped",   StringComparison.OrdinalIgnoreCase)
                              || string.Equals(s.AcademicStatus, "Transferred", StringComparison.OrdinalIgnoreCase);
                var firstApplicable = !isInactive && firstSemFee != null
                    && FeeRules.IsFeeApplicableToStudent(s.SchoolYearId, s.SemesterEntered, firstSemFee, sExemptions);
                var secondApplicable = !isInactive && secondSemFee != null
                    && FeeRules.IsFeeApplicableToStudent(s.SchoolYearId, s.SemesterEntered, secondSemFee, sExemptions);

                var firstPayments = firstSemFee != null
                    ? studentPayments.Where(p => p.FullAmountId == firstSemFee.FullAmountId).ToList()
                    : new List<OrgFeePayment>();
                var secondPayments = secondSemFee != null
                    ? studentPayments.Where(p => p.FullAmountId == secondSemFee.FullAmountId).ToList()
                    : new List<OrgFeePayment>();

                var firstPaid = firstPayments.Sum(p => p.Amount);
                var secondPaid = secondPayments.Sum(p => p.Amount);

                // Most recent payment across both semesters, used by the UI to stack
                // the latest-paid students at the top of the list by default.
                DateTime? lastPaymentDate = studentPayments.Count > 0
                    ? studentPayments.Max(p => p.PaymentDate)
                    : (DateTime?)null;

                // A semester can carry more than one receipt (e.g. two partial payments),
                // mirroring the per-payment receipts shown in the org-fee modal.
                var firstReceipts = firstPayments
                    .SelectMany(p => p.Receipts
                        .Where(r => !string.IsNullOrWhiteSpace(r.ReceiptNumber))
                        .Select(r => new { paymentId = p.PaymentId, receiptNumber = r.ReceiptNumber }))
                    .ToList();
                var secondReceipts = secondPayments
                    .SelectMany(p => p.Receipts
                        .Where(r => !string.IsNullOrWhiteSpace(r.ReceiptNumber))
                        .Select(r => new { paymentId = p.PaymentId, receiptNumber = r.ReceiptNumber }))
                    .ToList();

                var firstLatest  = firstPayments .OrderByDescending(p => p.PaymentDate).FirstOrDefault();
                var secondLatest = secondPayments.OrderByDescending(p => p.PaymentDate).FirstOrDefault();

                return new
                {
                    userId = s.StudentId,
                    accountId = s.AccountId,
                    schoolId = s.SchoolId,
                    name = s.FullName,
                    courseCode = s.CourseCode,
                    yearSection = s.YearSection,
                    avatarPath = s.AvatarPath ?? "",
                    role = s.Role,
                    isActive = s.IsActive,
                    academicStatus = s.AcademicStatus,
                    schoolYear = schoolYearLabel,
                    firstSemStatus  = firstApplicable ? ComputeOrgFeeStatus(firstPaid,  firstSemFee?.Amount  ?? 0) : "N/A",
                    secondSemStatus = secondApplicable ? ComputeOrgFeeStatus(secondPaid, secondSemFee?.Amount ?? 0) : "N/A",
                    firstSemPaid    = firstPaid,
                    secondSemPaid   = secondPaid,
                    firstSemRequired  = firstApplicable ? firstSemFee?.Amount  ?? 0 : 0,
                    secondSemRequired = secondApplicable ? secondSemFee?.Amount ?? 0 : 0,
                    firstSemIsCurrent  = firstSemFee  != null && firstSemFee.SemesterStatus  == SemesterStatus.Current,
                    secondSemIsCurrent = secondSemFee != null && secondSemFee.SemesterStatus == SemesterStatus.Current,
                    firstSemReceipts  = firstReceipts,
                    secondSemReceipts = secondReceipts,
                    lastPaymentDate,
                    firstSemLastPayDate       = firstLatest?.PaymentDate,
                    secondSemLastPayDate      = secondLatest?.PaymentDate,
                    firstSemLatestPaymentId   = firstLatest?.PaymentId,
                    secondSemLatestPaymentId  = secondLatest?.PaymentId,
                    firstSemFullAmount        = firstApplicable  ? firstSemFee?.Amount  ?? 0 : 0,
                    secondSemFullAmount       = secondApplicable ? secondSemFee?.Amount ?? 0 : 0,
                    schoolYearId              = s.SchoolYearId,
                    semesterEntered           = s.SemesterEntered?.ToString(),
                };
            }).ToList();

            return Json(new { success = true, students = result, schoolYear = schoolYearLabel });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetStudentOrgFeeDetails(int userId)
    {
        var guard = RequireAnyRole("Treasurer", "Admin", "Professor");
        if (guard != null) return guard;

        try
        {
            var student = await _context.Users
                .Include(u => u.Account)
                .Include(u => u.AcademicProfile)
                    .ThenInclude(ap => ap!.Course)
                .FirstOrDefaultAsync(u => u.UserId == userId
                                       && u.Account != null
                                       && (u.Account.Role == UserRole.Student || u.Account.Role == UserRole.Treasurer)
                                       && u.Account.RequestStatus == RequestStatus.Approved);

            if (student == null)
                return Json(new { success = false, message = "Student not found." });

            var currentSchoolYear = await _context.SchoolYears
                .FirstOrDefaultAsync(sy => sy.YearStatus == YearStatus.Current);

            // Pull ALL fees across every school year, then filter to the ones
            // that actually apply to this student (respecting SchoolYearId + SemesterEntered).
            var allFees = await _context.FullAmounts
                .Include(f => f.SchoolYear)
                .ToListAsync();

            var exemptions     = await GetExemptionsForUserAsync(userId);
            var applicableFees = allFees
                .Where(f => FeeRules.IsFeeApplicableToStudent(student.AcademicProfile, f, exemptions))
                .OrderBy(f => f.SchoolYear != null ? f.SchoolYear.YearStart : 0)
                .ThenBy(f => FeeRules.GetSemesterOrder(f.Semester))
                .ToList();

            var payments = await _context.OrgFeePayments
                .Include(p => p.FullAmount)
                    .ThenInclude(f => f.SchoolYear)
                .Include(p => p.Receipts)
                .Where(p => p.UserId == userId)
                .OrderByDescending(p => p.PaymentDate)
                .ToListAsync();

            var schoolYearLabel = currentSchoolYear != null
                ? $"{currentSchoolYear.YearStart}–{currentSchoolYear.YearEnd}"
                : "N/A";

            // Build one row per applicable fee across all years.
            // Include fees that still have a balance OR belong to the current year,
            // so the table shows current-year status plus any prior-year unpaid carryover.
            var fees = applicableFees
                .Select(f =>
                {
                    var feePayments = payments.Where(p => p.FullAmountId == f.FullAmountId).ToList();
                    var totalPaid = feePayments.Sum(p => p.Amount);
                    var balance = Math.Max(0, f.Amount - totalPaid);
                    var isCurrentYear = currentSchoolYear != null && f.SchoolYearId == currentSchoolYear.SchoolYearId;
                    return new
                    {
                        schoolYear = f.SchoolYear != null
                            ? $"{f.SchoolYear.YearStart}–{f.SchoolYear.YearEnd}"
                            : "N/A",
                        semester = f.Semester.ToString(),
                        requiredAmount = f.Amount,
                        totalPaid,
                        balance,
                        feeStatus = ComputeOrgFeeStatus(totalPaid, f.Amount),
                        isCurrentYear,
                        // True only for the live school-year + live semester, so the modal
                        // can float the current term to the very top of the list.
                        isCurrent = isCurrentYear && f.SemesterStatus == SemesterStatus.Current
                    };
                })
                .Where(x => x.balance > 0 || x.isCurrentYear)
                .ToList();

            var transactions = payments.Select(p => new
            {
                paymentId = p.PaymentId,
                date = p.PaymentDate,
                amount = p.Amount,
                paymentStatus = p.PaymentStatus.ToString(),
                receiptNumber = p.Receipts.FirstOrDefault()?.ReceiptNumber,
                hasReceipt = p.Receipts.Any(r => !string.IsNullOrWhiteSpace(r.ReceiptNumber)),
                schoolYear = p.FullAmount.SchoolYear != null
                    ? $"{p.FullAmount.SchoolYear.YearStart}–{p.FullAmount.SchoolYear.YearEnd}"
                    : "N/A",
                semester = p.FullAmount.Semester.ToString(),
                amountRequired = p.FullAmount.Amount
            }).ToList();

            return Json(new
            {
                success = true,
                student = new
                {
                    userId = student.UserId,
                    schoolId = student.Account!.SchoolId,
                    name = student.LastName != null && student.FirstName != null
                        ? $"{student.LastName.ToUpper()}, {student.FirstName.ToUpper()}"
                          + (!string.IsNullOrWhiteSpace(student.MiddleName) ? " " + student.MiddleName.Substring(0, 1).ToUpper() + "." : "")
                        : "N/A",
                    courseCode = student.AcademicProfile?.Course?.CourseCode ?? "N/A",
                    yearSection = student.AcademicProfile != null
                        ? $"{(student.AcademicProfile.YearLevel?.ToString() ?? "N/A")}-{(student.AcademicProfile.Section ?? "N/A")}"
                        : "N/A",
                    role = student.Account.Role.ToString()
                },
                schoolYear = schoolYearLabel,
                fees,
                transactions
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetOrgFeeReceipt(int paymentId)
    {
        try
        {
            var role = HttpContext.Session.GetString("UserRole");
            var accountIdStr = HttpContext.Session.GetString("AccountId");
            if (string.IsNullOrWhiteSpace(role) || !int.TryParse(accountIdStr, out var accountId))
                return Json(new { success = false, message = "Unauthorized." });

            var studentUserId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrWhiteSpace(studentUserId) || !int.TryParse(studentUserId, out var requesterUserId))
                return Json(new { success = false, message = "Unauthorized." });

            var payment = await _context.OrgFeePayments
                .Include(p => p.User)
                    .ThenInclude(u => u.Account)
                .Include(p => p.User)
                    .ThenInclude(u => u.AcademicProfile)
                        .ThenInclude(ap => ap.Course)
                .Include(p => p.FullAmount)
                    .ThenInclude(f => f.SchoolYear)
                .Include(p => p.Receipts)
                .FirstOrDefaultAsync(p => p.PaymentId == paymentId);

            if (payment == null)
                return Json(new { success = false, message = "Receipt not available." });

            // Access control: staff can view any; a student may only view their own.
            var isStaff =
                string.Equals(role, UserRole.Admin.ToString(), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(role, UserRole.Treasurer.ToString(), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(role, UserRole.Professor.ToString(), StringComparison.OrdinalIgnoreCase);

            if (!isStaff && payment.UserId != requesterUserId)
                return Json(new { success = false, message = "Receipt not available." });

            var receipt = payment.Receipts.OrderBy(r => r.ReceiptId).FirstOrDefault();
            if (receipt == null || string.IsNullOrWhiteSpace(receipt.ReceiptNumber))
                return Json(new { success = false, message = "Receipt not available." });

            var semester = payment.FullAmount?.Semester.ToString() ?? "";
            var schoolYear = payment.FullAmount?.SchoolYear != null
                ? $"{payment.FullAmount.SchoolYear.YearStart}–{payment.FullAmount.SchoolYear.YearEnd}"
                : "";

            // Students get the minimal payload.
            if (!isStaff)
            {
                return Json(new
                {
                    success = true,
                    receipt = new
                    {
                        receiptNumber = receipt.ReceiptNumber,
                        issueDate = payment.PaymentDate,
                        amount = payment.Amount,
                        status = payment.PaymentStatus.ToString(),
                        semester,
                        schoolYear
                    }
                });
            }

            // Staff get the full payload needed to render the printable receipt.
            var studentName = payment.User?.LastName != null && payment.User?.FirstName != null
                ? $"{payment.User.LastName}, {payment.User.FirstName}"
                : (payment.User?.FirstName ?? "");
            var studentSchoolId = payment.User?.Account?.SchoolId ?? "";
            var courseCode = payment.User?.AcademicProfile?.Course?.CourseCode ?? "";

            // Prefer the year level/section captured at the time of payment so the receipt
            // reflects the student's standing then, not now. Fall back to the current
            // profile only for old payments that have no snapshot. Level 5 = "4 Completed".
            int? frozenYear = payment.YearLevelAtPayment ?? payment.User?.AcademicProfile?.YearLevel;
            string frozenSection = payment.SectionAtPayment
                                   ?? payment.User?.AcademicProfile?.Section
                                   ?? "";
            string yearSection;
            if (frozenYear == 5)
                yearSection = "4 Completed";
            else
                yearSection = $"{(frozenYear?.ToString() ?? "")}-{frozenSection}".Trim('-');

            // IssuedBy stores the treasurer's account_id.
            string issuedByName = "Treasurer";
            string? signatureData = null;
            if (receipt.IssuedBy != 0)
            {
                var issuerUser = await _context.Users
                    .FirstOrDefaultAsync(u => u.AccountId == receipt.IssuedBy);
                if (issuerUser != null)
                {
                    issuedByName = $"{issuerUser.FirstName ?? ""} {issuerUser.LastName ?? ""}".Trim();
                    if (string.IsNullOrWhiteSpace(issuedByName)) issuedByName = "Treasurer";
                }

                var signature = await GetActiveTreasurerSignatureAsync(receipt.IssuedBy);
                signatureData = signature?.SignatureData;
            }

            return Json(new
            {
                success = true,
                receipt = new
                {
                    receiptNumber = receipt.ReceiptNumber,
                    issueDate = payment.PaymentDate,
                    paymentDate = payment.PaymentDate,
                    amount = payment.Amount,
                    status = payment.PaymentStatus.ToString(),
                    semester,
                    schoolYear,
                    studentName,
                    studentId = studentSchoolId,
                    course = courseCode,
                    yearSection,
                    issuedByName,
                    signatureData
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "Receipt retrieval failed." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetOtherFunds()
    {
        var guard = RequireAnyRole("Treasurer", "Admin", "Professor");
        if (guard != null) return guard;

        try
        {
            var funds = await _context.OtherFunds
                .Include(f => f.Receiver)
                    .ThenInclude(r => r.User)
                .Include(f => f.SchoolYear)
                .OrderByDescending(f => f.ReceivedDate)
                .Select(f => new
                {
                    f.FundId,
                    f.Source,
                    f.Description,
                    f.Category,
                    f.Amount,
                    f.ReceivedDate,
                    schoolYear = f.SchoolYear != null
                        ? $"{f.SchoolYear.YearStart} – {f.SchoolYear.YearEnd}"
                        : null,
                    receivedBy = f.Receiver != null && f.Receiver.User != null
                        ? $"{(f.Receiver.User.LastName ?? "").ToUpper()}, {(f.Receiver.User.FirstName ?? "").ToUpper()}"
                          + (!string.IsNullOrWhiteSpace(f.Receiver.User.MiddleName) ? " " + f.Receiver.User.MiddleName.Substring(0, 1).ToUpper() + "." : "")
                        : "Unknown"
                })
                .ToListAsync();

            return Json(new { success = true, funds });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetExpenses()
    {
        var guard = RequireAnyRole("Treasurer", "Admin", "Professor");
        if (guard != null) return guard;

        try
        {
            var expenses = await _context.Expenses
                .Include(e => e.Recorder)
                    .ThenInclude(r => r.User)
                .Include(e => e.SchoolYear)
                .OrderByDescending(e => e.ExpenseDate)
                .Select(e => new
                {
                    e.ExpenseId,
                    e.Description,
                    e.Amount,
                    e.ExpenseDate,
                    schoolYear = e.SchoolYear != null
                        ? $"{e.SchoolYear.YearStart} – {e.SchoolYear.YearEnd}"
                        : null,
                    recordedBy = e.Recorder != null && e.Recorder.User != null
                        ? $"{(e.Recorder.User.LastName != null ? e.Recorder.User.LastName.ToUpper() : "")}, {(e.Recorder.User.FirstName != null ? e.Recorder.User.FirstName.ToUpper() : "")}"
                          + (!string.IsNullOrWhiteSpace(e.Recorder.User.MiddleName) ? " " + e.Recorder.User.MiddleName.Substring(0, 1).ToUpper() + "." : "")
                        : "Unknown"
                })
                .ToListAsync();

            return Json(new { success = true, expenses });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetTreasurerDashboardStats()
    {
        var guard = RequireAnyRole("Treasurer", "Admin", "Professor");
        if (guard != null) return guard;

        try
        {

            var orgFeeTotal = await _context.OrgFeePayments
                .SumAsync(p => p.Amount);

            var otherFundsTotal = await _context.OtherFunds
                .SumAsync(f => f.Amount);

            var expensesTotal = await _context.Expenses
                .SumAsync(e => e.Amount);

            var totalIncome = orgFeeTotal + otherFundsTotal;
            var balance = totalIncome - expensesTotal;

            var currentSchoolYear = await _context.SchoolYears
                .FirstOrDefaultAsync(sy => sy.YearStatus == YearStatus.Current);

            var currentSemester = await _context.FullAmounts
                .FirstOrDefaultAsync(f => f.SemesterStatus == SemesterStatus.Current);

            var recentTransactions = new List<object>();

            var recentPayments = await _context.OrgFeePayments
                .Include(p => p.User)
                .OrderByDescending(p => p.PaymentDate)
                .Take(5)
                .Select(p => new
                {
                    type = "income",
                    category = "Org Fee",
                    description = p.User != null
                        ? $"Org Fee – {(p.User.LastName != null ? p.User.LastName.ToUpper() : "")}, {(p.User.FirstName != null ? p.User.FirstName.ToUpper() : "")}"
                          + (!string.IsNullOrWhiteSpace(p.User.MiddleName) ? " " + p.User.MiddleName.Substring(0, 1).ToUpper() + "." : "")
                        : "Org Fee",
                    amount = p.Amount,
                    date = p.PaymentDate,
                    receipt = p.Receipts.FirstOrDefault() != null ? p.Receipts.FirstOrDefault().ReceiptNumber : "—"
                })
                .ToListAsync();

            var recentFunds = await _context.OtherFunds
                .OrderByDescending(f => f.ReceivedDate)
                .Take(5)
                .Select(f => new
                {
                    type = "income",
                    category = "Other Fund",
                    description = f.Description ?? f.Source ?? "Other Fund",
                    amount = f.Amount,
                    date = f.ReceivedDate,
                    receipt = "—"
                })
                .ToListAsync();

            var recentExpenses = await _context.Expenses
                .OrderByDescending(e => e.ExpenseDate)
                .Take(5)
                .Select(e => new
                {
                    type = "expense",
                    category = "Expense",
                    description = e.Description ?? "Expense",
                    amount = e.Amount,
                    date = e.ExpenseDate,
                    receipt = "—"
                })
                .ToListAsync();

            recentTransactions.AddRange(recentPayments);
            recentTransactions.AddRange(recentFunds);
            recentTransactions.AddRange(recentExpenses);
            recentTransactions = recentTransactions.OrderByDescending(t => ((DateTime)(t.GetType().GetProperty("date")?.GetValue(t) ?? DateTime.MinValue))).Take(6).ToList();

            return Json(new
            {
                success = true,
                stats = new
                {
                    totalIncome,
                    orgFeeTotal,
                    otherFundsTotal,
                    expensesTotal,
                    balance,
                    expenseCount = await _context.Expenses.CountAsync(),
                    largestExpense = await _context.Expenses.AnyAsync()
                        ? await _context.Expenses.MaxAsync(e => e.Amount)
                        : 0
                },
                schoolYear = currentSchoolYear != null
                    ? $"{currentSchoolYear.YearStart} – {currentSchoolYear.YearEnd}"
                    : "Not Set",
                semester = currentSemester != null
                    ? (currentSemester.Semester == Semester.First ? "1st" : "2nd")
                    : "Not Set",
                recentTransactions
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> AddOrgFeePayment([FromBody] AddOrgFeePaymentRequest request)
    {
        var guard = RequireAnyRole("Treasurer", "Admin");
        if (guard != null) return guard;

        try
        {
            // Parse semester from request
            var semesterInput = (request.Semester ?? string.Empty).Trim();
            Semester semester;
            if (semesterInput.Equals("First", StringComparison.OrdinalIgnoreCase) ||
                semesterInput.Equals("1st",   StringComparison.OrdinalIgnoreCase))
                semester = Semester.First;
            else if (semesterInput.Equals("Second", StringComparison.OrdinalIgnoreCase) ||
                     semesterInput.Equals("2nd",    StringComparison.OrdinalIgnoreCase))
                semester = Semester.Second;
            else
                return Json(new { success = false, message = "Invalid semester selected." });

            FullAmount? targetFee = null;
            SchoolYear? currentSchoolYear = null;

            if (request.FullAmountId > 0)
            {
                targetFee = await _context.FullAmounts
                    .Include(f => f.SchoolYear)
                    .FirstOrDefaultAsync(f => f.FullAmountId == request.FullAmountId);
            }
            else
            {
                currentSchoolYear = await _context.SchoolYears
                    .FirstOrDefaultAsync(sy => sy.YearStatus == YearStatus.Current);

                if (currentSchoolYear == null)
                    return Json(new { success = false, message = "No active school year found. Please contact the administrator." });

                targetFee = await _context.FullAmounts
                    .Include(f => f.SchoolYear)
                    .FirstOrDefaultAsync(f => f.SchoolYearId == currentSchoolYear.SchoolYearId
                                           && f.Semester     == semester);
            }

            if (targetFee == null && request.FullAmountId > 0)
                return Json(new { success = false, message = "Fee not found." });

            if (targetFee == null)
                return Json(new { success = false, message = $"No fee set for {semesterInput} Semester of {currentSchoolYear.YearStart}–{currentSchoolYear.YearEnd}. Please set the fee in Settings first." });

            var student = await _context.Users
                .Include(u => u.AcademicProfile)
                    .ThenInclude(ap => ap!.SchoolYear)
                .Include(u => u.Account)
                .FirstOrDefaultAsync(u => u.UserId == request.UserId
                                       && u.Account != null
                                       && (u.Account.Role == UserRole.Student || u.Account.Role == UserRole.Treasurer)
                                       && u.Account.RequestStatus == RequestStatus.Approved);

            if (student == null)
                return Json(new { success = false, message = "Student not found." });

            var studentExemptions = await GetExemptionsForUserAsync(request.UserId);

            if (!FeeRules.IsFeeApplicableToStudent(student.AcademicProfile, targetFee, studentExemptions))
                return Json(new { success = false, message = "This student is not charged for this semester because they entered after it." });

            // RULE: Can't pay for the selected school year until ALL applicable fees from
            // PREVIOUS school years are fully paid. Only fees that actually apply to this
            // student (per IsFeeApplicableToStudent) are considered, so a student is never
            // blocked by fees from years before they enrolled.
            if (targetFee.SchoolYear != null)
            {
                var previousYearFees = await _context.FullAmounts
                    .Include(f => f.SchoolYear)
                    .Where(f => f.SchoolYear != null
                             && f.SchoolYear.YearStart < targetFee.SchoolYear.YearStart)
                    .ToListAsync();

                var applicablePrevFees = previousYearFees
                    .Where(f => FeeRules.IsFeeApplicableToStudent(student.AcademicProfile, f, studentExemptions))
                    .OrderBy(f => f.SchoolYear!.YearStart)
                    .ThenBy(f => f.Semester)
                    .ToList();

                if (applicablePrevFees.Any())
                {
                    var prevFeeIds = applicablePrevFees.Select(f => f.FullAmountId).ToList();

                    var prevPayments = await _context.OrgFeePayments
                        .Where(p => p.UserId == request.UserId
                                 && prevFeeIds.Contains(p.FullAmountId))
                        .ToListAsync();

                    foreach (var prevFee in applicablePrevFees)
                    {
                        var paidForPrevFee = prevPayments
                            .Where(p => p.FullAmountId == prevFee.FullAmountId)
                            .Sum(p => p.Amount);

                        if (paidForPrevFee < prevFee.Amount)
                        {
                            var semLabel = prevFee.Semester == Semester.First ? "1st" : "2nd";
                            var yrLabel  = prevFee.SchoolYear != null
                                ? $"{prevFee.SchoolYear.YearStart}–{prevFee.SchoolYear.YearEnd}"
                                : "a previous school year";
                            return Json(new
                            {
                                success = false,
                                message = $"Cannot pay for this school year. The student still has an unpaid balance for the {semLabel} Semester of {yrLabel}. Previous school year balances must be settled first."
                            });
                        }
                    }
                }
            }

            // RULE: Can't pay 2nd semester until 1st semester (of the same school year) is fully paid —
            // but only if the 1st semester fee actually applies to this student.
            if (targetFee.Semester == Semester.Second)
            {
                var firstSemFee = await _context.FullAmounts
                    .FirstOrDefaultAsync(f => f.SchoolYearId == targetFee.SchoolYearId
                                           && f.Semester == Semester.First);

                if (firstSemFee != null
                    && FeeRules.IsFeeApplicableToStudent(student.AcademicProfile, firstSemFee, studentExemptions))
                {
                    var firstSemPaid = await _context.OrgFeePayments
                        .Where(p => p.UserId == request.UserId
                                 && p.FullAmountId == firstSemFee.FullAmountId)
                        .SumAsync(p => p.Amount);

                    if (firstSemPaid < firstSemFee.Amount)
                        return Json(new { success = false, message = "Cannot pay 2nd semester until the 1st semester is fully paid." });
                }
            }

            var accountIdStr = HttpContext.Session.GetString("AccountId");
            if (!int.TryParse(accountIdStr, out var receivedBy))
                return Json(new { success = false, message = "Invalid session." });

            // Check if student already has a Paid record for this fee
            var existingPayment = await _context.OrgFeePayments
                .FirstOrDefaultAsync(p => p.UserId       == request.UserId
                                       && p.FullAmountId == targetFee.FullAmountId
                                       && p.PaymentStatus == PaymentStatus.Paid);

            if (existingPayment != null)
                return Json(new { success = false, message = $"This student has already fully paid for the {semesterInput} Semester." });

            // ── KEY FIX: sum all previous partial payments first ──
            var previouslyPaid = await _context.OrgFeePayments
                .Where(p => p.UserId == request.UserId && p.FullAmountId == targetFee.FullAmountId)
                .SumAsync(p => p.Amount);

            var cumulativeTotal = previouslyPaid + request.Amount;

            var payment = new OrgFeePayment
            {
                UserId        = request.UserId,
                FullAmountId  = targetFee.FullAmountId,
                Amount        = request.Amount,
                PaymentStatus = cumulativeTotal >= targetFee.Amount
                                    ? PaymentStatus.Paid
                                    : PaymentStatus.Partial,
                ReceivedBy    = receivedBy,
                PaymentDate   = DateTime.Now,
                // Freeze the student's standing at the time of payment so the receipt
                // never changes when the student advances in later school years.
                YearLevelAtPayment = student.AcademicProfile?.YearLevel,
                SectionAtPayment   = student.AcademicProfile?.Section
            };

            _context.OrgFeePayments.Add(payment);
            await _context.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(request.ReceiptNumber))
            {
                _context.Receipts.Add(new Receipt
                {
                    ReceiptNumber = request.ReceiptNumber,
                    PaymentId     = payment.PaymentId,
                    IssuedBy      = receivedBy
                });
                await _context.SaveChangesAsync();
            }

            return Json(new
            {
                success   = true,
                message   = "Payment recorded successfully.",
                paymentId = payment.PaymentId
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> AddOtherFund([FromBody] AddOtherFundRequest request)
    {
        var guard = RequireAnyRole("Treasurer", "Admin");
        if (guard != null) return guard;

        try
        {
            var accountIdStr = HttpContext.Session.GetString("AccountId");
            if (!int.TryParse(accountIdStr, out var receivedBy))
                return Json(new { success = false, message = "Invalid session." });

            var receivedDate = request.ReceivedDate?.ToLocalTime() ?? DateTime.Now;

            // Find the school year that matches the fund date
            // School years run August-June, so Jan-July belongs to previous year_start
            var targetYearStart = receivedDate.Month >= 8 ? receivedDate.Year : receivedDate.Year - 1;
            var matchedSchoolYear = await _context.SchoolYears
                .FirstOrDefaultAsync(sy => sy.YearStart == targetYearStart);

            if (matchedSchoolYear == null)
                matchedSchoolYear = await _context.SchoolYears
                    .FirstOrDefaultAsync(sy => sy.YearStatus == YearStatus.Current);

            var fund = new OtherFund
            {
                Source       = request.Source,
                Description  = request.Description,
                Category     = request.Category,
                Amount       = request.Amount,
                ReceivedBy   = receivedBy,
                ReceivedDate = receivedDate,
                SchoolYearId = matchedSchoolYear?.SchoolYearId
            };

            _context.OtherFunds.Add(fund);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Fund recorded successfully.", fundId = fund.FundId });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> AddExpense([FromBody] AddExpenseRequest request)
    {
        var guard = RequireAnyRole("Treasurer", "Admin");
        if (guard != null) return guard;

        try
        {
            var accountIdStr = HttpContext.Session.GetString("AccountId");
            if (!int.TryParse(accountIdStr, out var recordedBy))
                return Json(new { success = false, message = "Invalid session." });

            if (request.Amount <= 0)
                return Json(new { success = false, message = "Expense amount must be greater than zero." });

            // Enforce spending limit: an expense cannot exceed the remaining balance
            // (total income from org fees + other funds, minus existing expenses).
            var orgFeeTotal      = await _context.OrgFeePayments.SumAsync(p => p.Amount);
            var otherFundsTotal  = await _context.OtherFunds.SumAsync(f => f.Amount);
            var expensesTotal    = await _context.Expenses.SumAsync(e => e.Amount);
            var remainingBalance = (orgFeeTotal + otherFundsTotal) - expensesTotal;

            if (request.Amount > remainingBalance)
                return Json(new
                {
                    success = false,
                    message = $"Remaining balance is not enough for this new expense. Available: ₱{remainingBalance:N2}."
                });

            var expenseDate = request.ExpenseDate?.ToLocalTime() ?? DateTime.Now;

            // Find the school year that matches the expense date
            // School years run August-June, so Jan-July belongs to previous year_start
            var targetYearStart = expenseDate.Month >= 8 ? expenseDate.Year : expenseDate.Year - 1;
            var matchedSchoolYear = await _context.SchoolYears
                .FirstOrDefaultAsync(sy => sy.YearStart == targetYearStart);

            // Fall back to current if no match found
            if (matchedSchoolYear == null)
                matchedSchoolYear = await _context.SchoolYears
                    .FirstOrDefaultAsync(sy => sy.YearStatus == YearStatus.Current);

            var expense = new Expense
            {
                Description  = request.Description,
                Amount       = request.Amount,
                RecordedBy   = recordedBy,
                ExpenseDate  = expenseDate,
                SchoolYearId = matchedSchoolYear?.SchoolYearId
            };

            _context.Expenses.Add(expense);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Expense recorded successfully.", expenseId = expense.ExpenseId });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteOrgFeePayment([FromBody] DeleteTransactionRequest request)
    {
        var guard = RequireAnyRole("Treasurer", "Admin");
        if (guard != null) return guard;

        try
        {
            var payment = await _context.OrgFeePayments
                .Include(p => p.Receipts)
                .FirstOrDefaultAsync(p => p.PaymentId == request.Id);

            if (payment == null)
                return Json(new { success = false, message = "Payment not found." });

            if (payment.Receipts.Any())
                _context.Receipts.RemoveRange(payment.Receipts);

            _context.OrgFeePayments.Remove(payment);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Payment deleted successfully." });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteOtherFund([FromBody] DeleteTransactionRequest request)
    {
        var guard = RequireAnyRole("Treasurer", "Admin");
        if (guard != null) return guard;

        try
        {
            var fund = await _context.OtherFunds.FindAsync(request.Id);
            if (fund == null)
                return Json(new { success = false, message = "Fund not found." });

            _context.OtherFunds.Remove(fund);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Fund deleted successfully." });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> UpdateOtherFund([FromBody] UpdateOtherFundRequest request)
    {
        var guard = RequireAnyRole("Treasurer", "Admin");
        if (guard != null) return guard;

        try
        {
            var fund = await _context.OtherFunds.FindAsync(request.Id);
            if (fund == null)
                return Json(new { success = false, message = "Fund not found." });

            fund.Source      = request.Source;
            fund.Description = request.Description;
            fund.Category    = request.Category;
            fund.Amount      = request.Amount;
            fund.ReceivedDate = request.ReceivedDate ?? fund.ReceivedDate;

            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Fund updated successfully." });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> UpdateOrgFeePayment([FromBody] UpdateOrgFeePaymentRequest request)
    {
        var guard = RequireAnyRole("Treasurer", "Admin");
        if (guard != null) return guard;

        try
        {
            var payment = await _context.OrgFeePayments
                .Include(p => p.Receipts)
                .FirstOrDefaultAsync(p => p.PaymentId == request.Id);

            if (payment == null)
                return Json(new { success = false, message = "Payment not found." });

            payment.Amount = request.Amount;
            payment.PaymentStatus = request.Amount >= request.FullAmount
                ? PaymentStatus.Paid
                : PaymentStatus.Partial;

            if (!string.IsNullOrWhiteSpace(request.ReceiptNumber))
            {
                var receipt = payment.Receipts.FirstOrDefault();
                if (receipt != null)
                    receipt.ReceiptNumber = request.ReceiptNumber;
                else
                {
                    var accountIdStr = HttpContext.Session.GetString("AccountId");
                    int.TryParse(accountIdStr, out var issuedBy);
                    _context.Receipts.Add(new Receipt
                    {
                        ReceiptNumber = request.ReceiptNumber,
                        PaymentId     = payment.PaymentId,
                        IssuedBy      = issuedBy
                    });
                }
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Payment updated successfully." });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteExpense([FromBody] DeleteTransactionRequest request)
    {
        var guard = RequireAnyRole("Treasurer", "Admin");
        if (guard != null) return guard;

        try
        {
            var expense = await _context.Expenses.FindAsync(request.Id);
            if (expense == null)
                return Json(new { success = false, message = "Expense not found." });

            _context.Expenses.Remove(expense);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Expense deleted successfully." });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }       
    }

    [HttpPost]
    public async Task<IActionResult> UpdateExpense([FromBody] UpdateExpenseRequest request)
    {
        var guard = RequireAnyRole("Treasurer", "Admin");
        if (guard != null) return guard;

        try
        {
            var expense = await _context.Expenses.FindAsync(request.Id);
            if (expense == null)
                return Json(new { success = false, message = "Expense not found." });

            expense.Description = request.Description;
            expense.Amount      = request.Amount;
            expense.ExpenseDate = request.ExpenseDate ?? expense.ExpenseDate;

            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Expense updated successfully." });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetStudentsForPayment(string? q = null, string? semester = null, int? fullAmountId = null)
    {
        var guard = RequireAnyRole("Treasurer", "Admin", "Professor");
        if (guard != null) return guard;

        try
        {

            FullAmount? targetFee = null;

            if (fullAmountId.HasValue && fullAmountId.Value > 0)
            {
                targetFee = await _context.FullAmounts
                    .Include(f => f.SchoolYear)
                    .FirstOrDefaultAsync(f => f.FullAmountId == fullAmountId.Value);
            }
            else if (!string.IsNullOrWhiteSpace(semester))
            {
                Semester? semFilter = semester.Equals("First", StringComparison.OrdinalIgnoreCase)
                    ? Semester.First
                    : semester.Equals("Second", StringComparison.OrdinalIgnoreCase)
                        ? Semester.Second
                        : null;

                var currentSchoolYear = await _context.SchoolYears
                    .FirstOrDefaultAsync(sy => sy.YearStatus == YearStatus.Current);

                if (currentSchoolYear != null && semFilter.HasValue)
                {
                    targetFee = await _context.FullAmounts
                        .Include(f => f.SchoolYear)
                        .FirstOrDefaultAsync(f => f.SchoolYearId == currentSchoolYear.SchoolYearId
                                               && f.Semester     == semFilter.Value);
                }
            }

            var query = (q ?? string.Empty).Trim().ToLower();

            var users = await _context.Users
                .Include(u => u.Account)
                .Include(u => u.AcademicProfile)
                    .ThenInclude(ap => ap!.Course)
                .Include(u => u.AcademicProfile)
                    .ThenInclude(ap => ap!.SchoolYear)
                .Where(u => u.Account != null
                         && (u.Account.Role == UserRole.Student || u.Account.Role == UserRole.Treasurer)
                         && u.Account.IsActive
                         && u.Account.RequestStatus == RequestStatus.Approved
                         && (string.IsNullOrEmpty(query)
                             || (u.Account.SchoolId != null && u.Account.SchoolId.ToLower().Contains(query))
                             || (u.FirstName != null && u.FirstName.ToLower().Contains(query))
                             || (u.LastName  != null && u.LastName.ToLower().Contains(query))))
                .ToListAsync();

            var paidUserIds = targetFee != null
                ? await _context.OrgFeePayments
                    .Where(p => p.FullAmountId == targetFee.FullAmountId
                             && p.PaymentStatus == PaymentStatus.Paid)
                    .Select(p => p.UserId)
                    .ToListAsync()
                : new List<int>();

            var partialUserIds = targetFee != null
                ? await _context.OrgFeePayments
                    .Where(p => p.FullAmountId == targetFee.FullAmountId
                             && p.PaymentStatus == PaymentStatus.Partial)
                    .Select(p => p.UserId)
                    .Distinct()
                    .ToListAsync()
                : new List<int>();

            var orgFeeExemptions = await GetAllExemptionsAsync();

            var students = users
                .Where(u => { orgFeeExemptions.TryGetValue(u.UserId, out var ex); return targetFee == null || FeeRules.IsFeeApplicableToStudent(u.AcademicProfile, targetFee, ex); })
                .Select(u => new {
                    userId      = u.UserId,
                    schoolId    = u.Account!.SchoolId,
                    name        = (u.LastName  ?? "") + ", " + (u.FirstName ?? "")
                                  + (!string.IsNullOrWhiteSpace(u.MiddleName)
                                      ? " " + u.MiddleName.Substring(0, 1) + "."
                                      : ""),
                    courseCode  = u.AcademicProfile != null && u.AcademicProfile.Course != null
                                  ? u.AcademicProfile.Course.CourseCode : "N/A",
                    yearSection = u.AcademicProfile != null
                                  ? (u.AcademicProfile.YearLevel != null
                                      ? u.AcademicProfile.YearLevel.ToString() : "")
                                + "-" + (u.AcademicProfile.Section ?? "")
                                  : "N/A",
                    // hasPaid is now specific to selected semester's fee
                    hasPaid = targetFee != null
                              && paidUserIds.Contains(u.UserId),
                    hasPartial = targetFee != null
                              && partialUserIds.Contains(u.UserId)
                              && !paidUserIds.Contains(u.UserId)
                })
                .OrderBy(s => s.name)
                .ToList();

            return Json(new { success = true, students });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetStudentFeeStatus(int userId, int fullAmountId)
    {
        var guard = RequireAnyRole("Treasurer", "Admin", "Professor");
        if (guard != null) return guard;

        try
        {
            var fee = await _context.FullAmounts
                .FirstOrDefaultAsync(f => f.FullAmountId == fullAmountId);

            if (fee == null)
                return Json(new { success = false, message = "Fee not found." });

            var totalPaid = await _context.OrgFeePayments
                .Where(p => p.UserId == userId && p.FullAmountId == fullAmountId)
                .SumAsync(p => p.Amount);

            var balance = Math.Max(0, fee.Amount - totalPaid);
            var status = totalPaid <= 0
                ? "Unpaid"
                : totalPaid >= fee.Amount
                    ? "Paid"
                    : "Partial";

            return Json(new
            {
                success = true,
                totalPaid,
                balance,
                required = fee.Amount,
                status
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetAvailableSemesters()
    {
        var guard = RequireAnyRole("Treasurer", "Admin", "Professor");
        if (guard != null) return guard;

        try
        {
            var fees = await _context.FullAmounts
                .Include(f => f.SchoolYear)
                .OrderByDescending(f => f.SemesterStatus == SemesterStatus.Current) // Current first
                .ThenByDescending(f => f.SchoolYear.YearStart)
                .ThenByDescending(f => f.Semester)
                .Select(f => new {
                    fullAmountId   = f.FullAmountId,
                    schoolYearId   = f.SchoolYearId,
                    yearStart      = f.SchoolYear.YearStart,
                    yearEnd        = f.SchoolYear.YearEnd,
                    semester       = f.Semester.ToString(),
                    semesterStatus = f.SemesterStatus.ToString(),
                    amount         = f.Amount,
                    label          = $"{f.SchoolYear.YearStart}–{f.SchoolYear.YearEnd} · " +
                                     $"{(f.Semester == Semester.First ? "1st" : "2nd")} Semester · " +
                                     $"₱{f.Amount:N2} · {f.SemesterStatus}"
                })
                .ToListAsync();

            return Json(new { success = true, fees });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpGet]
public async Task<IActionResult> GetCollectableOrgFee(string? schoolYear = null)
    {
        var guard = RequireAnyRole("Treasurer", "Admin", "Professor");
        if (guard != null) return guard;

        try
        {
            // Determine which fees to total. If a school year is passed (e.g. "2025–2026"),
            // sum collectable across BOTH semesters of that year. Otherwise use the current active fee.
            List<FullAmount> targetFees;

            // In: GetCollectableOrgFee

            if (!string.IsNullOrWhiteSpace(schoolYear))
            {
                var parts = schoolYear.Replace("—", "–").Split('–');
                int.TryParse(parts.ElementAtOrDefault(0)?.Trim(), out var yStart);

                targetFees = await _context.FullAmounts
                    .Include(f => f.SchoolYear)
                    .Where(f => f.SchoolYear != null && f.SchoolYear.YearStart == yStart)
                    .ToListAsync();
            }
            else
            {
                // No year specified = "All School Years": total collectable across every year.
                targetFees = await _context.FullAmounts
                    .Include(f => f.SchoolYear)
                    .ToListAsync();
            }

            if (!targetFees.Any())
                return Json(new { success = true, collectable = 0 });

            // Only ENROLLED active students
            var students = await _context.Users
                .Include(u => u.Account)
                .Include(u => u.AcademicProfile)
                    .ThenInclude(ap => ap!.SchoolYear)
                .Where(u => u.Account != null
                         && (u.Account.Role == UserRole.Student || u.Account.Role == UserRole.Treasurer)
                         && u.Account.IsActive
                         && u.Account.RequestStatus == RequestStatus.Approved
                         && (u.AcademicProfile == null 
                             || u.AcademicProfile.AcademicStatus == AcademicStatus.Enrolled))
                .ToListAsync();

var feeIds = targetFees.Select(f => f.FullAmountId).ToList();

            // All payments toward any of the target fees
            var payments = await _context.OrgFeePayments
                .Where(p => feeIds.Contains(p.FullAmountId))
                .ToListAsync();

            var collectableExemptions = await GetAllExemptionsAsync();
            decimal totalCollectable = 0;
            var debtorIds = new HashSet<int>();

            // For each target fee, add up what each applicable student still owes on it.
            foreach (var fee in targetFees)
            {
                foreach (var student in students.Where(u => { collectableExemptions.TryGetValue(u.UserId, out var ex); return FeeRules.IsFeeApplicableToStudent(u.AcademicProfile, fee, ex); }))
                {
                    var totalPaid = payments
                        .Where(p => p.UserId == student.UserId && p.FullAmountId == fee.FullAmountId)
                        .Sum(p => p.Amount);

                    if (totalPaid < fee.Amount)
                    {
                        totalCollectable += fee.Amount - totalPaid;
                        debtorIds.Add(student.UserId);
                    }
                }
            }

            return Json(new { success = true, collectable = totalCollectable, membersCount = debtorIds.Count });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    // ----------------------------------------------------------------
    // EMAIL OTP FOR SIGNUP
    // ----------------------------------------------------------------

    [HttpPost]
    public async Task<IActionResult> SendEmailOTP([FromBody] SendOtpRequest req)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(req.Email))
                return Json(new { success = false, message = "Email is required." });

            if (!IsValidEmail(req.Email))
                return Json(new { success = false, message = "Invalid email format." });

            // Check if email already exists
            var existingAccount = await _context.Accounts
                .FirstOrDefaultAsync(a => a.Email != null && a.Email.ToLower() == req.Email.ToLower());

            if (existingAccount != null)
                return Json(new { success = false, message = "Email already registered." });

            // Generate 6-digit OTP
            var otp = new Random().Next(100000, 999999).ToString();
            var cacheKey = $"signup_otp_{req.Email.ToLower()}";
            
            // Store OTP in session
            HttpContext.Session.SetString(cacheKey, otp);
            HttpContext.Session.SetString($"{cacheKey}_expires", DateTime.UtcNow.AddMinutes(10).ToString("O"));

            // Send email
            var emailBody = $@"Hello,<br><br>
Your SSG verification code is:<br><br>
<strong>{otp}</strong><br><br>
This code expires in 10 minutes.<br><br>
If you did not request this, please ignore this email.<br><br>
Best regards,<br>SSG Financial Management System";

            await _emailService.SendEmailAsync(req.Email, "Your SSG Verification Code", emailBody);

            return Json(new { success = true, message = "Code sent to your email." });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> VerifyEmailOTP([FromBody] VerifyOtpRequest req)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Code))
                return Json(new { success = false, message = "Email and code are required." });

            var cacheKey = $"signup_otp_{req.Email.ToLower()}";
            var storedOtp = HttpContext.Session.GetString(cacheKey);
            var expiresStr = HttpContext.Session.GetString($"{cacheKey}_expires");

            if (string.IsNullOrEmpty(storedOtp) || string.IsNullOrEmpty(expiresStr))
                return Json(new { success = false, message = "No code found for this email." });

            if (DateTime.UtcNow > DateTime.Parse(expiresStr))
            {
                HttpContext.Session.Remove(cacheKey);
                HttpContext.Session.Remove($"{cacheKey}_expires");
                return Json(new { success = false, message = "Code has expired." });
            }

            if (storedOtp != req.Code)
                return Json(new { success = false, message = "Invalid code." });

            // Clear OTP after successful verification
            HttpContext.Session.Remove(cacheKey);
            HttpContext.Session.Remove($"{cacheKey}_expires");

            return Json(new { success = true, message = "Email verified successfully." });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetNextReceiptNumber()
    {
        try
        {
            var last = await _context.Receipts
                .OrderByDescending(r => r.ReceiptId)
                .FirstOrDefaultAsync();

            int nextNum = 1;
            if (last != null)
            {
                // Extract number from format "OR-2026-001"
                var parts = last.ReceiptNumber.Split('-');
                if (parts.Length == 3 && int.TryParse(parts[2], out int lastNum))
                    nextNum = lastNum + 1;
            }

            var year     = DateTime.Now.Year;
            var receipt  = $"OR-{year}-{nextNum:D3}";

            return Json(new { success = true, receiptNumber = receipt });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    private async Task<TreasurerSignature?> GetActiveTreasurerSignatureAsync(int accountOrUserId)
    {
        var signature = await _context.TreasurerSignatures
            .Where(s => s.AccountId == accountOrUserId && s.IsActive)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync();

        if (signature != null)
            return signature;

        var mappedAccountId = await _context.Users
            .Where(u => u.UserId == accountOrUserId)
            .Select(u => (int?)u.AccountId)
            .FirstOrDefaultAsync();

        if (mappedAccountId == null || mappedAccountId.Value == accountOrUserId)
            return null;

        return await _context.TreasurerSignatures
            .Where(s => s.AccountId == mappedAccountId.Value && s.IsActive)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync();
    }

    // ----------------------------------------------------------------
    // TREASURER SIGNATURE
    // ----------------------------------------------------------------

    [HttpPost]
    public async Task<IActionResult> SaveTreasurerSignature([FromBody] SaveSignatureRequest request)
    {
        try
        {
            var role = HttpContext.Session.GetString("UserRole");
            if (!string.Equals(role, UserRole.Treasurer.ToString(), StringComparison.OrdinalIgnoreCase))
                return Json(new { success = false, message = "Only treasurers can save signatures." });

            var accountIdStr = HttpContext.Session.GetString("AccountId");
            if (!int.TryParse(accountIdStr, out var accountId))
                return Json(new { success = false, message = "Invalid session." });

            if (string.IsNullOrWhiteSpace(request.SignatureData))
                return Json(new { success = false, message = "Signature data is required." });

            if (!request.SignatureData.StartsWith("data:image/png;base64,", StringComparison.OrdinalIgnoreCase))
                return Json(new { success = false, message = "Invalid signature image format." });

            var previous = await _context.TreasurerSignatures
                .Where(s => s.AccountId == accountId && s.IsActive)
                .ToListAsync();

            foreach (var signature in previous)
                signature.IsActive = false;

            _context.TreasurerSignatures.Add(new TreasurerSignature
            {
                AccountId = accountId,
                SignatureData = request.SignatureData,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });

            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Signature saved successfully." });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetMySignature()
    {
        try
        {
            var accountIdStr = HttpContext.Session.GetString("AccountId");
            if (!int.TryParse(accountIdStr, out var accountId))
                return Json(new { success = false, message = "Invalid session." });

            var signature = await GetActiveTreasurerSignatureAsync(accountId);

            return Json(new
            {
                success = signature != null,
                signatureData = signature?.SignatureData,
                createdAt = signature?.CreatedAt
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetSignatureByAccountId(int accountId)
    {
        // Any authenticated user may read a signature (students need it to render the
        // signature on their own receipts), but anonymous access is not allowed.
        if (string.IsNullOrEmpty(GetSessionRole()))
            return Json(new { success = false, message = "Unauthorized." });

        try
        {
            var signature = await GetActiveTreasurerSignatureAsync(accountId);

            return Json(new
            {
                success = signature != null,
                signatureData = signature?.SignatureData,
                createdAt = signature?.CreatedAt
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetAllTreasurerSignatures()
    {
        var guard = RequireAnyRole("Treasurer", "Admin");
        if (guard != null) return guard;

        try
        {
            var signatures = await _context.TreasurerSignatures
                .Include(s => s.Account)
                    .ThenInclude(a => a!.User)
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => new
                {
                    s.SignatureId,
                    s.AccountId,
                    s.SignatureData,
                    s.CreatedAt,
                    s.IsActive,
                    treasurerName = s.Account != null && s.Account.User != null
                        ? ((s.Account.User.FirstName ?? "") + " " + (s.Account.User.LastName ?? "")).Trim()
                        : "Unknown"
                })
                .ToListAsync();

            return Json(new { success = true, signatures });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> EditFee([FromBody] EditFeeRequest request)
    {
        var guard = RequireAnyRole("Treasurer", "Admin");
        if (guard != null) return guard;

        try
        {

            var fee = await _context.FullAmounts.FindAsync(request.FullAmountId);
            if (fee == null)
                return Json(new { success = false, message = "Fee record not found." });

            fee.Amount = request.Amount;
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Fee amount updated successfully." });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> SetFeeStatus([FromBody] SetFeeStatusRequest request)
    {
        var guard = RequireAnyRole("Treasurer", "Admin");
        if (guard != null) return guard;

        try
        {

            // If marking as Current, demote all others first
            if (request.Status == "Current")
            {
                var currentFees = await _context.FullAmounts
                    .Where(f => f.SemesterStatus == SemesterStatus.Current)
                    .ToListAsync();
                foreach (var f in currentFees)
                    f.SemesterStatus = SemesterStatus.Ended;
            }

            var fee = await _context.FullAmounts.FindAsync(request.FullAmountId);
            if (fee == null)
                return Json(new { success = false, message = "Fee record not found." });

            fee.SemesterStatus = request.Status == "Current"
                ? SemesterStatus.Current
                : SemesterStatus.Ended;

            await _context.SaveChangesAsync();
            return Json(new { success = true, message = $"Status changed to {request.Status}." });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> SearchAllStudentsWithPaymentStatus(string? q = null)
    {
        var guard = RequireAnyRole("Treasurer", "Admin", "Professor");
        if (guard != null) return guard;

        try
        {

            var currentFee = await _context.FullAmounts
                .Include(f => f.SchoolYear)
                .FirstOrDefaultAsync(f => f.SemesterStatus == SemesterStatus.Current);

            var studentsQuery = _context.Users
                .Include(u => u.Account)
                .Include(u => u.AcademicProfile)
                    .ThenInclude(ap => ap!.Course)
                .Where(u => u.Account != null
                         && (u.Account.Role == UserRole.Student || u.Account.Role == UserRole.Treasurer)
                         && u.Account.IsActive
                         && u.Account.RequestStatus == RequestStatus.Approved);

            var query = (q ?? string.Empty).Trim().ToLower();
            if (!string.IsNullOrEmpty(query))
            {
                studentsQuery = studentsQuery.Where(u =>
                    (u.FirstName != null && u.FirstName.ToLower().Contains(query)) ||
                    (u.LastName  != null && u.LastName.ToLower().Contains(query))  ||
                    (u.Account!.SchoolId != null && u.Account.SchoolId.ToLower().Contains(query)) ||
                    (u.AcademicProfile != null && u.AcademicProfile.Course != null &&
                     u.AcademicProfile.Course.CourseCode.ToLower().Contains(query)));
            }

            var students = await studentsQuery.ToListAsync();

            var payments = currentFee != null
                ? await _context.OrgFeePayments
                    .Include(p => p.Receipts)
                    .Where(p => p.FullAmountId == currentFee.FullAmountId)
                    .ToListAsync()
                : new List<OrgFeePayment>();

            var profDashExemptions = await GetAllExemptionsAsync();

            var result = students.Select(u =>
            {
                var studentPayments = payments.Where(p => p.UserId == u.UserId).ToList();
                profDashExemptions.TryGetValue(u.UserId, out var uExemptions);
                var totalPaid       = studentPayments.Sum(p => p.Amount);
                var isApplicable    = currentFee != null && FeeRules.IsFeeApplicableToStudent(u.AcademicProfile, currentFee, uExemptions);
                var required        = isApplicable ? currentFee?.Amount ?? 0 : 0;
                var lastPayment     = studentPayments.OrderByDescending(p => p.PaymentDate).FirstOrDefault();

                string status = !isApplicable        ? "N/A"
                              : totalPaid <= 0       ? "Unpaid"
                              : totalPaid >= required ? "Paid"
                                                      : "Partial";

                var receipt = lastPayment?.Receipts?.OrderBy(r => r.ReceiptId).FirstOrDefault();

                return new
                {
                    userId         = u.UserId,
                    schoolId       = u.Account!.SchoolId,
                    name           = $"{(u.LastName ?? "").ToUpper()}, {(u.FirstName ?? "").ToUpper()}"
                                   + (!string.IsNullOrWhiteSpace(u.MiddleName) ? " " + u.MiddleName.Substring(0, 1).ToUpper() + "." : ""),
                    courseCode     = u.AcademicProfile?.Course?.CourseCode ?? "N/A",
                    yearSection    = u.AcademicProfile != null
                        ? $"{u.AcademicProfile.YearLevel?.ToString() ?? "N/A"}-{u.AcademicProfile.Section ?? "N/A"}"
                        : "N/A",
                    role           = u.Account.Role.ToString(),
                    status,
                    totalPaid,
                    requiredAmount = required,
                    paymentDate    = lastPayment?.PaymentDate,
                    receiptNumber  = receipt?.ReceiptNumber,
                    schoolYear     = currentFee != null
                        ? $"{currentFee.SchoolYear.YearStart}–{currentFee.SchoolYear.YearEnd}" : null,
                    semester       = currentFee?.Semester.ToString()
                };
            })
            .OrderBy(s => s.name)
            .ToList();

            return Json(new { success = true, students = result });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return Json(new { success = false, message = "An error occurred. Please try again." });
        }
    }
}
