using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Onudhabon.Data;
using Onudhabon.Models;
using Onudhabon.Services;

namespace Onudhabon.Controllers
{
    public class LectureController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ICloudinaryService _cloudinaryService;
        private readonly ILogger<LectureController> _logger;
        private readonly IEducationalUploadAiService _uploadAi;

        public LectureController(
            ApplicationDbContext context,
            ICloudinaryService cloudinaryService,
            ILogger<LectureController> logger,
            IEducationalUploadAiService uploadAi)
        {
            _context = context;
            _cloudinaryService = cloudinaryService;
            _logger = logger;
            _uploadAi = uploadAi;
        }

        private async Task<List<string>> GetCurrentUserIdentifiersAsync()
        {
            var identifiers = new List<string>();
            var userName = User.Identity?.Name;
            var userEmail = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            if (!string.IsNullOrWhiteSpace(userName)) identifiers.Add(userName.Trim().ToLower());
            if (!string.IsNullOrWhiteSpace(userEmail)) identifiers.Add(userEmail.Trim().ToLower());

            if (int.TryParse(userIdClaim, out int uid))
            {
                var dbUser = await _context.Users.FindAsync(uid);
                if (dbUser != null)
                {
                    if (!string.IsNullOrWhiteSpace(dbUser.FullName)) identifiers.Add(dbUser.FullName.Trim().ToLower());
                    if (!string.IsNullOrWhiteSpace(dbUser.Email)) identifiers.Add(dbUser.Email.Trim().ToLower());
                }
            }

            return identifiers.Distinct().ToList();
        }

        // GET: /Lecture
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Index(string? classLevel, string? subject, string? topic)
        {
            var isAdmin = User.IsInRole("Admin") || User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value == "Admin";
            var isEducator = User.IsInRole("Educator") || User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value == "Educator";
            var query = _context.Lectures.AsQueryable();

            if (isAdmin || isEducator)
            {
                // Admins and Educators see all lectures (active, approved, pending, declined)
            }
            else if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                // Logged-in users see all approved lectures PLUS their own uploaded pending/declined lectures
                var userIdentifiers = await GetCurrentUserIdentifiersAsync();
                query = query.Where(l => l.Status == "Active" || l.Status == "Approved" || l.Status == "approved" 
                    || (l.Instructor != null && userIdentifiers.Contains(l.Instructor.ToLower())));
            }
            else
            {
                // Anonymous visitors only see approved lectures
                query = query.Where(l => l.Status == "Active" || l.Status == "Approved" || l.Status == "approved");
            }

            if (!string.IsNullOrWhiteSpace(classLevel))
            {
                query = query.Where(l => l.ClassLevel == classLevel);
            }

            if (!string.IsNullOrWhiteSpace(subject))
            {
                query = query.Where(l => l.Subject == subject);
            }

            if (!string.IsNullOrWhiteSpace(topic))
            {
                query = query.Where(l => l.Topic == topic);
            }

            var lectures = await query
                .OrderByDescending(l => l.CreatedAt)
                .ToListAsync();

            ViewBag.ClassLevel = classLevel;
            ViewBag.Subject = subject;
            ViewBag.Topic = topic;

            return View(lectures);
        }

        // GET: /Lecture/Details/5
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Details(int id)
        {
            var lecture = await _context.Lectures.FirstOrDefaultAsync(l => l.Id == id);
            if (lecture == null)
            {
                return NotFound();
            }

            var isAdmin = User.IsInRole("Admin") || User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value == "Admin";
            var isEducator = User.IsInRole("Educator") || User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value == "Educator";
            bool isApproved = lecture.Status == "Active" || lecture.Status == "Approved" || lecture.Status == "approved";
            bool isOwner = false;

            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                var userIdentifiers = await GetCurrentUserIdentifiersAsync();
                isOwner = !string.IsNullOrEmpty(lecture.Instructor) && userIdentifiers.Contains(lecture.Instructor.Trim().ToLower());
            }

            if (!isApproved && !isAdmin && !isEducator && !isOwner)
            {
                return NotFound();
            }

            return View(lecture);
        }

        // GET: /Lecture/Upload
        [HttpGet]
        [Authorize(Roles = "Educator")]
        public async Task<IActionResult> Upload()
        {
            var userFullName = User.Identity?.Name;
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdClaim, out int uid))
            {
                var dbUser = await _context.Users.FindAsync(uid);
                if (dbUser == null || dbUser.IsRestricted || 
                    (!dbUser.IsVerified && !string.Equals(dbUser.VerificationStatus, "Active", StringComparison.OrdinalIgnoreCase) && !string.Equals(dbUser.VerificationStatus, "Approved", StringComparison.OrdinalIgnoreCase)))
                {
                    TempData["ErrorMessage"] = "Your account is pending administrator approval. You can only visit pages until an administrator approves your account.";
                    return RedirectToAction("Index", "Lecture");
                }

                if (!string.IsNullOrWhiteSpace(dbUser.FullName))
                {
                    userFullName = dbUser.FullName;
                }
            }

            return View(new LectureUploadViewModel
            {
                Instructor = userFullName ?? "Educator"
            });
        }

        // POST: /Lecture/Upload
        [HttpPost]
        [Authorize(Roles = "Educator")]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("ai-upload")]
        [RequestSizeLimit(110L * 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = 110L * 1024 * 1024)]
        public async Task<IActionResult> AnalyzeUpload(IFormFile? videoFile, CancellationToken cancellationToken)
        {
            if (!await IsCurrentEducatorApprovedAsync()) return Forbid();
            if (videoFile == null) return BadRequest(new { error = "Select a video file first." });
            try
            {
                var draft = await _uploadAi.AnalyzeLectureAsync(videoFile, cancellationToken);
                return Json(new { draft });
            }
            catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
            catch (OperationCanceledException) { return StatusCode(408, new { error = "Video analysis timed out or was cancelled. Try again." }); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lecture video analysis failed for educator {Educator}.", User.Identity?.Name);
                return StatusCode(502, new { error = "AI could not analyze this video right now. Check your connection and try again." });
            }
        }

        private async Task<bool> IsCurrentEducatorApprovedAsync()
        {
            var id = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(id, out var uid)) return false;
            var user = await _context.Users.FindAsync(uid);
            return user != null && !user.IsRestricted && (user.IsVerified ||
                string.Equals(user.VerificationStatus, "Active", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(user.VerificationStatus, "Approved", StringComparison.OrdinalIgnoreCase));
        }

        // POST: /Lecture/Upload
        [HttpPost]
        [Authorize(Roles = "Educator")]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(110L * 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = 110L * 1024 * 1024)]
        public async Task<IActionResult> Upload(LectureUploadViewModel model)
        {
            var userFullName = User.Identity?.Name;
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdClaim, out int uid))
            {
                var dbUser = await _context.Users.FindAsync(uid);
                if (dbUser == null || dbUser.IsRestricted || 
                    (!dbUser.IsVerified && !string.Equals(dbUser.VerificationStatus, "Active", StringComparison.OrdinalIgnoreCase) && !string.Equals(dbUser.VerificationStatus, "Approved", StringComparison.OrdinalIgnoreCase)))
                {
                    TempData["ErrorMessage"] = "Your account is pending administrator approval. You can only visit pages until an administrator approves your account.";
                    return RedirectToAction("Index", "Lecture");
                }

                if (!string.IsNullOrWhiteSpace(dbUser.FullName))
                {
                    userFullName = dbUser.FullName;
                }
            }

            // Always enforce user profile name as Instructor
            model.Instructor = userFullName ?? (!string.IsNullOrWhiteSpace(model.Instructor) ? model.Instructor.Trim() : "Educator");

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            if (model.VideoFile == null || model.VideoFile.Length == 0)
            {
                ModelState.AddModelError(nameof(model.VideoFile), "Please select a video file to upload.");
                return View(model);
            }

            var uploadResult = await _cloudinaryService.UploadLectureVideoAsync(model.VideoFile);

            if (!uploadResult.Success)
            {
                ModelState.AddModelError(nameof(model.VideoFile), uploadResult.ErrorMessage ?? "Failed to upload video to Cloudinary.");
                return View(model);
            }

            string? videoUrl = uploadResult.SecureUrl;
            string? thumbnailUrl = uploadResult.ThumbnailUrl ?? (videoUrl != null ? _cloudinaryService.GetVideoThumbnailUrl(videoUrl, 480, 270) : null);

            var lecture = new Lecture
            {
                Title = model.Title.Trim(),
                Description = model.Description?.Trim(),
                Instructor = model.Instructor,
                Version = model.Version?.Trim() ?? "Bangla",
                ClassLevel = model.ClassLevel.Trim(),
                Subject = model.Subject.Trim(),
                Topic = model.Topic.Trim(),
                VideoUrl = videoUrl,
                Thumbnail = thumbnailUrl ?? _cloudinaryService.GetVideoThumbnailUrl(videoUrl, 480, 270),
                Status = "pending",
                CreatedAt = DateTime.UtcNow,
                __v = 0
            };

            _context.Lectures.Add(lecture);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Lecture uploaded successfully with status 'pending'!";
            return RedirectToAction(nameof(Index));
        }

        // GET: /Lecture/GetApproved
        [HttpGet]
        public async Task<IActionResult> GetApproved(string? classLevel, string? subject)
        {
            var query = _context.Lectures
                .Where(l => l.Status == "Active" || l.Status == "Approved" || l.Status == "approved");

            if (!string.IsNullOrWhiteSpace(classLevel))
            {
                query = query.Where(l => l.ClassLevel == classLevel);
            }

            if (!string.IsNullOrWhiteSpace(subject))
            {
                query = query.Where(l => l.Subject == subject);
            }

            var approvedLectures = await query
                .OrderByDescending(l => l.CreatedAt)
                .Select(l => new
                {
                    l.Id,
                    l.Title,
                    l.Description,
                    l.Instructor,
                    l.Version,
                    l.ClassLevel,
                    l.Subject,
                    l.Topic,
                    l.VideoUrl,
                    l.Thumbnail,
                    l.Status,
                    l.CreatedAt
                })
                .ToListAsync();

            return Json(approvedLectures);
        }

        // GET: /Lecture/GetTopicsBySubject
        [HttpGet]
        public async Task<IActionResult> GetTopicsBySubject(string? classLevel, string? subject)
        {
            if (string.IsNullOrWhiteSpace(subject))
            {
                return Json(Array.Empty<string>());
            }

            var isAdmin = User.IsInRole("Admin");
            var query = _context.Lectures.Where(l => l.Subject == subject);

            if (!isAdmin)
            {
                if (User.Identity != null && User.Identity.IsAuthenticated)
                {
                    var userIdentifiers = await GetCurrentUserIdentifiersAsync();
                    query = query.Where(l => l.Status == "Active" || l.Status == "Approved" || l.Status == "approved" 
                        || (l.Instructor != null && userIdentifiers.Contains(l.Instructor.ToLower())));
                }
                else
                {
                    query = query.Where(l => l.Status == "Active" || l.Status == "Approved" || l.Status == "approved");
                }
            }

            if (!string.IsNullOrWhiteSpace(classLevel))
            {
                query = query.Where(l => l.ClassLevel == classLevel);
            }

            var topics = await query
                .Where(l => !string.IsNullOrEmpty(l.Topic))
                .Select(l => l.Topic!)
                .Distinct()
                .OrderBy(t => t)
                .ToListAsync();

            return Json(topics);
        }
    }
}
