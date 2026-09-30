using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Onudhabon.Data;
using Onudhabon.Models;
using Onudhabon.Services;

namespace Onudhabon.Controllers
{
    public class MaterialController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ICloudinaryService _cloudinaryService;
        private readonly ILogger<MaterialController> _logger;
        private readonly IEducationalUploadAiService _uploadAi;

        public MaterialController(
            ApplicationDbContext context,
            ICloudinaryService cloudinaryService,
            ILogger<MaterialController> logger,
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

        // GET: /Material
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Index(string? classLevel, string? subject, string? topic)
        {
            var isAdmin = User.IsInRole("Admin") || User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value == "Admin";
            var isEducator = User.IsInRole("Educator") || User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value == "Educator";
            var query = _context.Materials.AsQueryable();

            if (isAdmin || isEducator)
            {
                // Admins and Educators see all materials (active, approved, pending, declined)
            }
            else if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                // Logged-in users see all active materials plus their own uploads (including pending/declined)
                var userIdentifiers = await GetCurrentUserIdentifiersAsync();
                query = query.Where(m => m.Status == "Active" || m.Status == "Approved" || m.Status == "approved" 
                    || (m.Instructor != null && userIdentifiers.Contains(m.Instructor.ToLower())));
            }
            else
            {
                // Anonymous visitors only see approved materials
                query = query.Where(m => m.Status == "Active" || m.Status == "Approved" || m.Status == "approved");
            }

            if (!string.IsNullOrWhiteSpace(classLevel))
            {
                query = query.Where(m => m.ClassLevel == classLevel);
            }

            if (!string.IsNullOrWhiteSpace(subject))
            {
                query = query.Where(m => m.Subject == subject);
            }

            if (!string.IsNullOrWhiteSpace(topic))
            {
                query = query.Where(m => m.Topic == topic);
            }

            var materials = await query
                .OrderByDescending(m => m.Date)
                .ToListAsync();

            ViewBag.ClassLevel = classLevel;
            ViewBag.Subject = subject;
            ViewBag.Topic = topic;

            return View(materials);
        }

        // GET: /Material/Details/5
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Details(int id)
        {
            var material = await _context.Materials.FirstOrDefaultAsync(m => m.Id == id);
            if (material == null)
            {
                return NotFound();
            }

            var isAdmin = User.IsInRole("Admin") || User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value == "Admin";
            var isEducator = User.IsInRole("Educator") || User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value == "Educator";
            bool isApproved = material.Status == "Active" || material.Status == "Approved" || material.Status == "approved";
            bool isOwner = false;

            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                var userIdentifiers = await GetCurrentUserIdentifiersAsync();
                isOwner = !string.IsNullOrEmpty(material.Instructor) && userIdentifiers.Contains(material.Instructor.Trim().ToLower());
            }

            if (!isApproved && !isAdmin && !isEducator && !isOwner)
            {
                return NotFound();
            }

            return View(material);
        }

        // GET: /Material/Upload
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
                    return RedirectToAction("Index", "Material");
                }

                if (!string.IsNullOrWhiteSpace(dbUser.FullName))
                {
                    userFullName = dbUser.FullName;
                }
            }

            return View(new MaterialUploadViewModel
            {
                Instructor = userFullName ?? "Educator"
            });
        }

        [HttpPost]
        [Authorize(Roles = "Educator")]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("ai-upload")]
        [RequestSizeLimit(26L * 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = 26L * 1024 * 1024)]
        public async Task<IActionResult> AnalyzeUpload(IFormFile? materialFile, CancellationToken cancellationToken)
        {
            if (!await IsCurrentEducatorApprovedAsync()) return Forbid();
            if (materialFile == null) return BadRequest(new { error = "Select a document first." });
            try
            {
                var draft = await _uploadAi.AnalyzeMaterialAsync(materialFile, cancellationToken);
                return Json(new { draft });
            }
            catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
            catch (OperationCanceledException) { return StatusCode(408, new { error = "Document analysis was cancelled. Try again." }); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Study material analysis failed for educator {Educator}.", User.Identity?.Name);
                return StatusCode(502, new { error = "AI could not analyze this document right now. Check your connection and try again." });
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

        // POST: /Material/Upload
        [HttpPost]
        [Authorize(Roles = "Educator")]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(26L * 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = 26L * 1024 * 1024)]
        public async Task<IActionResult> Upload(MaterialUploadViewModel model)
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
                    return RedirectToAction("Index", "Material");
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

            if (model.MaterialFile == null || model.MaterialFile.Length == 0)
            {
                ModelState.AddModelError(nameof(model.MaterialFile), "Please select a material document file (PDF/DOCX) to upload.");
                return View(model);
            }

            var uploadResult = await _cloudinaryService.UploadMaterialPdfAsync(model.MaterialFile);

            if (!uploadResult.Success)
            {
                ModelState.AddModelError(nameof(model.MaterialFile), uploadResult.ErrorMessage ?? "Failed to upload document to Cloudinary.");
                return View(model);
            }

            string? fileUrl = uploadResult.SecureUrl;
            string? size = uploadResult.FormattedSize;

            var material = new Material
            {
                Title = model.Title.Trim(),
                Description = model.Description?.Trim(),
                Instructor = model.Instructor,
                Version = model.Version?.Trim() ?? "Bangla",
                ClassLevel = model.ClassLevel.Trim(),
                Subject = model.Subject.Trim(),
                Topic = model.Topic.Trim(),
                FileUrl = fileUrl,
                Size = size,
                Status = "pending",
                Downloads = 0,
                Date = DateTime.UtcNow,
                __v = 0
            };

            _context.Materials.Add(material);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Material uploaded successfully with status 'pending'!";
            return RedirectToAction(nameof(Index));
        }

        // GET: /Material/Download/5
        [HttpGet]
        public async Task<IActionResult> Download(int id)
        {
            var material = await _context.Materials.FirstOrDefaultAsync(m => m.Id == id);
            if (material == null || string.IsNullOrWhiteSpace(material.FileUrl))
            {
                return NotFound();
            }

            var isAdmin = User.IsInRole("Admin");
            bool isApproved = material.Status == "Active" || material.Status == "Approved" || material.Status == "approved";
            bool isOwner = false;

            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                var userIdentifiers = await GetCurrentUserIdentifiersAsync();
                isOwner = !string.IsNullOrEmpty(material.Instructor) && userIdentifiers.Contains(material.Instructor.Trim().ToLower());
            }

            if (!isApproved && !isAdmin && !isOwner)
            {
                return NotFound();
            }

            // Increment download count
            material.Downloads++;
            await _context.SaveChangesAsync();

            return Redirect(material.FileUrl);
        }

        // GET: /Material/GetApproved
        [HttpGet]
        public async Task<IActionResult> GetApproved(string? classLevel, string? subject)
        {
            var query = _context.Materials
                .Where(m => m.Status == "Active" || m.Status == "Approved" || m.Status == "approved");

            if (!string.IsNullOrWhiteSpace(classLevel))
            {
                query = query.Where(m => m.ClassLevel == classLevel);
            }

            if (!string.IsNullOrWhiteSpace(subject))
            {
                query = query.Where(m => m.Subject == subject);
            }

            var approvedMaterials = await query
                .OrderByDescending(m => m.Date)
                .Select(m => new
                {
                    m.Id,
                    m.Title,
                    m.Description,
                    m.Instructor,
                    m.Version,
                    m.ClassLevel,
                    m.Subject,
                    m.Topic,
                    m.FileUrl,
                    m.Size,
                    m.Downloads,
                    m.Status,
                    m.Date
                })
                .ToListAsync();

            return Json(approvedMaterials);
        }

        // GET: /Material/GetTopicsBySubject
        [HttpGet]
        public async Task<IActionResult> GetTopicsBySubject(string? classLevel, string? subject)
        {
            if (string.IsNullOrWhiteSpace(subject))
            {
                return Json(Array.Empty<string>());
            }

            var isAdmin = User.IsInRole("Admin");
            var query = _context.Materials.Where(m => m.Subject == subject);

            if (!isAdmin)
            {
                if (User.Identity != null && User.Identity.IsAuthenticated)
                {
                    var userIdentifiers = await GetCurrentUserIdentifiersAsync();
                    query = query.Where(m => m.Status == "Active" || m.Status == "Approved" || m.Status == "approved" 
                        || (m.Instructor != null && userIdentifiers.Contains(m.Instructor.ToLower())));
                }
                else
                {
                    query = query.Where(m => m.Status == "Active" || m.Status == "Approved" || m.Status == "approved");
                }
            }

            if (!string.IsNullOrWhiteSpace(classLevel))
            {
                query = query.Where(m => m.ClassLevel == classLevel);
            }

            var topics = await query
                .Where(m => !string.IsNullOrEmpty(m.Topic))
                .Select(m => m.Topic!)
                .Distinct()
                .OrderBy(t => t)
                .ToListAsync();

            return Json(topics);
        }
    }
}
