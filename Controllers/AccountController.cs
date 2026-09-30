using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Onudhabon.Data;
using Onudhabon.Models;
using Onudhabon.Services;

namespace Onudhabon.Controllers
{
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IPasswordHasher<User> _passwordHasher;
        private readonly ICloudinaryService _cloudinaryService;
        private readonly IEmailService _emailService;
        private readonly IEmailValidationService _emailValidationService;
        private readonly ILogger<AccountController> _logger;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public AccountController(
            ApplicationDbContext context,
            IPasswordHasher<User> passwordHasher,
            ICloudinaryService cloudinaryService,
            IEmailService emailService,
            IEmailValidationService emailValidationService,
            ILogger<AccountController> logger,
            IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _passwordHasher = passwordHasher;
            _cloudinaryService = cloudinaryService;
            _emailService = emailService;
            _emailValidationService = emailValidationService;
            _logger = logger;
            _webHostEnvironment = webHostEnvironment;
        }

        [HttpGet]
        public IActionResult Index()
        {
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                return RedirectToAction(nameof(Profile));
            }
            return RedirectToAction(nameof(Login));
        }

        [HttpGet]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Login(string? returnUrl = null)
        {
            SetNoCacheHeaders();

            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) && !returnUrl.Contains("/Account/Login", StringComparison.OrdinalIgnoreCase))
                {
                    return Redirect(returnUrl);
                }
                return RedirectAuthenticatedUser();
            }

            ViewData["ReturnUrl"] = returnUrl;
            return View(new LoginViewModel { ReturnUrl = returnUrl });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            SetNoCacheHeaders();

            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                return RedirectAuthenticatedUser();
            }

            ViewData["ReturnUrl"] = returnUrl;

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var input = model.UsernameOrEmail?.Trim() ?? string.Empty;
            
            // Find user by Email (case-insensitive)
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == input.ToLower());

            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "Invalid login credentials. Please check your email and password.");
                return View(model);
            }

            // Verify password hash
            var verificationResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, model.Password);
            if (verificationResult == PasswordVerificationResult.Failed)
            {
                ModelState.AddModelError(string.Empty, "Invalid login credentials. Please check your password.");
                return View(model);
            }

            // Check if user account has been restricted by Admin
            if (user.IsRestricted)
            {
                ViewBag.RestrictedModal = true;
                ViewBag.ModalTitle = "Account Restricted";
                ViewBag.ModalMessage = "Your account has been restricted by the administrator. You are currently blocked from logging in. Please contact the administrator or support if you need assistance.";
                ModelState.AddModelError(string.Empty, "Your account has been restricted. You are blocked from logging in.");
                return View(model);
            }

            // Check if volunteer verification status was declined by Admin
            if (string.Equals(user.VerificationStatus, "Declined", StringComparison.OrdinalIgnoreCase))
            {
                ViewBag.DeclinedModal = true;
                ViewBag.ModalTitle = "Verification Status Declined";
                ViewBag.ModalMessage = "Your volunteer verification status has been declined by the administrator. Please contact support or the administrator for further inquiries.";
                ModelState.AddModelError(string.Empty, "Your verification status has been declined. You cannot log in.");
                return View(model);
            }

            // Check if email address has been verified
            if (!string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase) && !user.IsEmailVerified)
            {
                ViewBag.UnverifiedEmailModal = true;
                ViewBag.UnverifiedEmail = user.Email;
                ViewBag.ModalTitle = "Email Verification Required";
                ViewBag.ModalMessage = $"Your email address ({user.Email}) has not been verified yet. Please check your inbox for the verification link. If you didn't receive the email, you can request a new one below.";
                ModelState.AddModelError(string.Empty, "Your email address is not verified. Please verify your email before logging in.");
                return View(model);
            }

            // Demo mode: email verification also approves new accounts. This also
            // upgrades accounts that were verified before demo approval was enabled.
            if (!string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase) && user.IsEmailVerified &&
                (!user.IsVerified ||
                 (!string.Equals(user.VerificationStatus, "Active", StringComparison.OrdinalIgnoreCase) &&
                  !string.Equals(user.VerificationStatus, "Approved", StringComparison.OrdinalIgnoreCase))))
            {
                user.IsVerified = true;
                user.VerificationStatus = "Active";
                await _context.SaveChangesAsync();
            }

            // Check if registered user account is pending approval by Admin
            if (!string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                bool isApproved = user.IsVerified ||
                    string.Equals(user.VerificationStatus, "Active", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(user.VerificationStatus, "Approved", StringComparison.OrdinalIgnoreCase);

                if (!isApproved)
                {
                    ViewBag.PendingModal = true;
                    ViewBag.ModalTitle = "Account Pending Approval";
                    ViewBag.ModalMessage = "Your account registration has been received and is currently pending approval by an administrator. Until an administrator approves your registered account, you can only browse and visit pages as a guest.";
                    ModelState.AddModelError(string.Empty, "Your account is pending administrator approval. You can only visit pages as a guest until your account is approved.");
                    return View(model);
                }
            }

            // Create Claims for authenticated session
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.FullName),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim("PhoneNumber", user.PhoneNumber ?? ""),
                new Claim("City", user.City ?? ""),
                new Claim("Area", user.Area ?? "")
            };

            if (!string.IsNullOrEmpty(user.Picture))
            {
                claims.Add(new Claim("Picture", user.Picture));
            }

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = model.RememberMe,
                ExpiresUtc = model.RememberMe 
                    ? DateTimeOffset.UtcNow.AddDays(30) 
                    : DateTimeOffset.UtcNow.AddMinutes(30),
                AllowRefresh = true
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            // If user is Admin, direct to the Admin Dashboard
            if (user.Role == "Admin")
            {
                return RedirectToAction("Dashboard", "Admin");
            }

            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Register(string? returnUrl = null)
        {
            SetNoCacheHeaders();

            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                return RedirectAuthenticatedUser();
            }

            ViewData["ReturnUrl"] = returnUrl;
            return View(new RegisterViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> Register(RegisterViewModel model, string? returnUrl = null)
        {
            SetNoCacheHeaders();

            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                return RedirectAuthenticatedUser();
            }

            ViewData["ReturnUrl"] = returnUrl;

            // Restrict roles strictly to Educator or Local Guardian
            if (!string.IsNullOrEmpty(model.Role) && model.Role != "Educator" && model.Role != "Local Guardian")
            {
                ModelState.AddModelError(nameof(model.Role), "Please select a valid role: Educator or Local Guardian.");
            }

            if (!model.AgreeToTerms)
            {
                ModelState.AddModelError(nameof(model.AgreeToTerms), "You must agree to the Terms and Conditions to register.");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var emailNormalized = model.Email.Trim().ToLower();

            // Check if user with this email already exists
            var existingUser = await _context.Users.AnyAsync(u => u.Email.ToLower() == emailNormalized);
            if (existingUser)
            {
                ModelState.AddModelError(nameof(model.Email), "An account with this email address already exists.");
                return View(model);
            }

            // Real-time verification: check whether the email actually exists before sending OTP or registering
            var (emailExists, emailError) = await _emailValidationService.ValidateEmailExistsAsync(emailNormalized);
            if (!emailExists)
            {
                ModelState.AddModelError(nameof(model.Email), emailError ?? "The email address does not appear to exist. Please provide an active, existing email address to receive your OTP.");
                return View(model);
            }

            // Process uploaded files with Cloudinary
            string? picturePath = null;
            if (model.PictureFile != null && model.PictureFile.Length > 0)
            {
                var picResult = await _cloudinaryService.UploadProfilePictureAsync(model.PictureFile);
                if (picResult.Success)
                {
                    picturePath = picResult.SecureUrl;
                }
            }

            string? certificatePath = null;
            if (model.CertificatePictureFile != null && model.CertificatePictureFile.Length > 0)
            {
                var certResult = await _cloudinaryService.UploadEducationDocAsync(model.CertificatePictureFile);
                if (certResult.Success)
                {
                    certificatePath = certResult.SecureUrl;
                }
            }

            // If EducationLevel is HSC or SSC, clear university fields; if SSC, also clear college fields
            var eduLevel = model.EducationLevel?.ToLower() ?? string.Empty;
            if (eduLevel.Contains("ssc") || eduLevel.Contains("hsc"))
            {
                model.UniversityName = null;
                model.UniversityPassingYear = null;
            }
            if (eduLevel.Contains("ssc"))
            {
                model.HscInstitute = null;
                model.HscPassingYear = null;
            }

            // Generate 6-digit OTP and secure verification token
            var otp = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

            // Create new User entity with all submitted registration information
            var user = new User
            {
                FullName = model.FullName.Trim(),
                Email = emailNormalized,
                PhoneNumber = model.PhoneNumber.Trim(),
                Role = model.Role.Trim(),
                City = model.City.Trim(),
                Area = model.Area.Trim(),
                Location = string.IsNullOrWhiteSpace(model.Location) ? null : model.Location.Trim(),
                Age = model.Age,
                NidNumber = string.IsNullOrWhiteSpace(model.NidNumber) ? null : model.NidNumber.Trim(),
                Bio = string.IsNullOrWhiteSpace(model.Bio) ? null : model.Bio.Trim(),
                Picture = picturePath,
                VolunteerReason = string.IsNullOrWhiteSpace(model.VolunteerReason) ? null : model.VolunteerReason.Trim(),
                EducationLevel = string.IsNullOrWhiteSpace(model.EducationLevel) ? null : model.EducationLevel.Trim(),
                Institution = !string.IsNullOrWhiteSpace(model.UniversityName) ? model.UniversityName.Trim() : (!string.IsNullOrWhiteSpace(model.HscInstitute) ? model.HscInstitute.Trim() : model.SscInstitute?.Trim()),
                Major = string.IsNullOrWhiteSpace(model.Major) ? null : model.Major.Trim(),
                CurrentlyStudying = string.IsNullOrWhiteSpace(model.CurrentlyStudying) ? null : model.CurrentlyStudying.Trim(),
                SscPassingYear = string.IsNullOrWhiteSpace(model.SscPassingYear) ? null : model.SscPassingYear.Trim(),
                SscInstitute = string.IsNullOrWhiteSpace(model.SscInstitute) ? null : model.SscInstitute.Trim(),
                HscPassingYear = string.IsNullOrWhiteSpace(model.HscPassingYear) ? null : model.HscPassingYear.Trim(),
                HscInstitute = string.IsNullOrWhiteSpace(model.HscInstitute) ? null : model.HscInstitute.Trim(),
                UniversityName = string.IsNullOrWhiteSpace(model.UniversityName) ? null : model.UniversityName.Trim(),
                UniversityPassingYear = string.IsNullOrWhiteSpace(model.UniversityPassingYear) ? null : model.UniversityPassingYear.Trim(),
                CertificatePicture = certificatePath,
                AgreeToTerms = model.AgreeToTerms,
                IsRestricted = false,
                IsVerified = false,
                VerificationStatus = "Pending",
                IsEmailVerified = false,
                EmailVerificationToken = token,
                EmailVerificationTokenExpiry = DateTime.UtcNow.AddHours(24),
                EmailOtp = otp,
                EmailOtpExpiry = DateTime.UtcNow.AddMinutes(15),
                CreatedAt = DateTime.UtcNow,
                __v = 0
            };

            // Securely hash password
            user.PasswordHash = _passwordHasher.HashPassword(user, model.Password);

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            var verifyUrl = Url.Action("VerifyEmail", "Account", new { token = user.EmailVerificationToken, email = user.Email }, Request.Scheme);
            await _emailService.SendOtpEmailAsync(user.Email, user.FullName, otp, verifyUrl ?? string.Empty);

            TempData["SuccessMessage"] = $"Registration submitted successfully! We have sent a 6-digit OTP to {user.Email}. Please enter the OTP to verify your email address.";
            return RedirectToAction(nameof(VerifyOtp), new { email = user.Email, returnUrl });
        }

        [HttpGet]
        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> VerifyOtp(string? email, string? returnUrl = null)
        {
            SetNoCacheHeaders();

            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                return RedirectAuthenticatedUser();
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                return RedirectToAction(nameof(Login), new { returnUrl });
            }

            var cleanEmail = email.Trim().ToLowerInvariant();
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == cleanEmail);
            if (user == null)
            {
                TempData["ErrorMessage"] = "No account found matching this email address.";
                return RedirectToAction(nameof(Login), new { returnUrl });
            }

            if (user.IsEmailVerified)
            {
                TempData["SuccessMessage"] = "Your email address is already verified! You can now sign in.";
                return RedirectToAction(nameof(Login), new { returnUrl });
            }

            var model = new VerifyOtpViewModel
            {
                Email = user.Email,
                ReturnUrl = returnUrl
            };

            return View(model);
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> VerifyOtp(VerifyOtpViewModel model)
        {
            SetNoCacheHeaders();

            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                return RedirectAuthenticatedUser();
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var cleanEmail = model.Email.Trim().ToLowerInvariant();
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == cleanEmail);

            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "No account was found matching this email address.");
                return View(model);
            }

            if (user.IsEmailVerified)
            {
                TempData["SuccessMessage"] = "Your email address is already verified! You can now sign in.";
                return RedirectToAction(nameof(Login), new { returnUrl = model.ReturnUrl });
            }

            if (user.EmailOtpExpiry.HasValue && user.EmailOtpExpiry.Value < DateTime.UtcNow)
            {
                ModelState.AddModelError(nameof(model.Otp), "This verification OTP has expired. Please click 'Resend OTP' to receive a new code.");
                return View(model);
            }

            var enteredOtp = model.Otp?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(user.EmailOtp) || !string.Equals(user.EmailOtp.Trim(), enteredOtp, StringComparison.Ordinal))
            {
                ModelState.AddModelError(nameof(model.Otp), "Invalid OTP code. Please enter the correct 6-digit code sent to your email.");
                return View(model);
            }

            // Successfully verified via OTP
            user.IsEmailVerified = true;
            user.IsVerified = true;
            user.VerificationStatus = "Active";
            user.EmailOtp = null;
            user.EmailOtpExpiry = null;
            user.EmailVerificationToken = null;
            user.EmailVerificationTokenExpiry = null;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Email verified successfully! Your account is approved for this demonstration, and you can now sign in.";
            return RedirectToAction(nameof(Login), new { returnUrl = model.ReturnUrl });
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendOtp(string? email, string? returnUrl = null)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                TempData["ErrorMessage"] = "Please provide an email address.";
                return RedirectToAction(nameof(Login), new { returnUrl });
            }

            var cleanEmail = email.Trim().ToLowerInvariant();
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == cleanEmail);

            if (user == null)
            {
                TempData["SuccessMessage"] = "If an account with that email exists, a fresh verification OTP has been sent.";
                return RedirectToAction(nameof(VerifyOtp), new { email, returnUrl });
            }

            if (user.IsEmailVerified)
            {
                TempData["SuccessMessage"] = "Your email address is already verified! You can now sign in.";
                return RedirectToAction(nameof(Login), new { returnUrl });
            }

            // Generate fresh OTP & verification token
            var otp = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

            user.EmailOtp = otp;
            user.EmailOtpExpiry = DateTime.UtcNow.AddMinutes(15);
            user.EmailVerificationToken = token;
            user.EmailVerificationTokenExpiry = DateTime.UtcNow.AddHours(24);
            await _context.SaveChangesAsync();

            var verifyUrl = Url.Action("VerifyEmail", "Account", new { token = user.EmailVerificationToken, email = user.Email }, Request.Scheme);
            await _emailService.SendOtpEmailAsync(user.Email, user.FullName, otp, verifyUrl ?? string.Empty);

            TempData["SuccessMessage"] = $"A fresh 6-digit OTP has been sent to {user.Email}. Please check your inbox (and spam folder).";
            return RedirectToAction(nameof(VerifyOtp), new { email = user.Email, returnUrl });
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> VerifyEmail(string? token, string? email)
        {
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(email))
            {
                ViewBag.Success = false;
                ViewBag.Title = "Invalid Verification Link";
                ViewBag.Message = "The verification link is missing required verification parameters. Please use the complete link sent to your email.";
                return View();
            }

            var cleanEmail = email.Trim().ToLowerInvariant();
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == cleanEmail);

            if (user == null)
            {
                ViewBag.Success = false;
                ViewBag.Title = "Account Not Found";
                ViewBag.Message = "No account was found matching this email address.";
                return View();
            }

            if (user.IsEmailVerified)
            {
                ViewBag.Success = true;
                ViewBag.AlreadyVerified = true;
                ViewBag.Title = "Email Already Verified";
                ViewBag.Message = "Your email address has already been verified. You can now sign in.";
                return View();
            }

            if (user.EmailVerificationToken != token || (user.EmailVerificationTokenExpiry.HasValue && user.EmailVerificationTokenExpiry.Value < DateTime.UtcNow))
            {
                ViewBag.Success = false;
                ViewBag.Expired = true;
                ViewBag.Email = user.Email;
                ViewBag.Title = "Verification Link Expired or Invalid";
                ViewBag.Message = "This email verification link has expired or is invalid. Verification links are valid for 24 hours. You can request a fresh code or link below.";
                return View();
            }

            // Successfully verified!
            user.IsEmailVerified = true;
            user.IsVerified = true;
            user.VerificationStatus = "Active";
            user.EmailVerificationToken = null;
            user.EmailVerificationTokenExpiry = null;
            user.EmailOtp = null;
            user.EmailOtpExpiry = null;
            await _context.SaveChangesAsync();

            ViewBag.Success = true;
            ViewBag.Title = "Email Verified Successfully!";
            ViewBag.Message = "Your email address has been successfully verified! Your account is approved for this demonstration, and you can now log in.";
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendVerificationEmail(string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                TempData["ErrorMessage"] = "Please provide a valid email address.";
                return RedirectToAction(nameof(Login));
            }

            var cleanEmail = email.Trim().ToLowerInvariant();
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == cleanEmail);

            if (user == null)
            {
                TempData["SuccessMessage"] = "If an account with that email exists and is unverified, a fresh verification OTP has been sent.";
                return RedirectToAction(nameof(Login));
            }

            if (user.IsEmailVerified)
            {
                TempData["SuccessMessage"] = "Your email address is already verified! You can now sign in.";
                return RedirectToAction(nameof(Login));
            }

            // Generate fresh token & OTP
            var otp = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            user.EmailOtp = otp;
            user.EmailOtpExpiry = DateTime.UtcNow.AddMinutes(15);
            user.EmailVerificationToken = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            user.EmailVerificationTokenExpiry = DateTime.UtcNow.AddHours(24);
            await _context.SaveChangesAsync();

            var verifyUrl = Url.Action("VerifyEmail", "Account", new { token = user.EmailVerificationToken, email = user.Email }, Request.Scheme);
            await _emailService.SendOtpEmailAsync(user.Email, user.FullName, otp, verifyUrl ?? string.Empty);

            TempData["SuccessMessage"] = $"A fresh verification email and OTP have been sent to {user.Email}. Please check your inbox (and spam folder).";
            return RedirectToAction(nameof(VerifyOtp), new { email = user.Email });
        }

        [HttpGet]
        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult ForgotPassword()
        {
            SetNoCacheHeaders();

            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                return RedirectAuthenticatedUser();
            }

            return View(new ForgotPasswordViewModel());
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            SetNoCacheHeaders();

            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                return RedirectAuthenticatedUser();
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var cleanEmail = model.Email.Trim().ToLowerInvariant();
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == cleanEmail);

            if (user != null)
            {
                // Generate 6-digit OTP (valid for 15 minutes)
                var otp = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
                user.PasswordResetOtp = otp;
                user.PasswordResetOtpExpiry = DateTime.UtcNow.AddMinutes(15);
                // Clear any previous reset token
                user.PasswordResetToken = null;
                user.PasswordResetTokenExpiry = null;
                await _context.SaveChangesAsync();

                await _emailService.SendPasswordResetOtpEmailAsync(user.Email, user.FullName, otp);
            }

            // Always redirect to OTP page (even if user doesn't exist, to prevent email enumeration)
            TempData["SuccessMessage"] = $"If an account with that email exists, a 6-digit OTP has been sent. Please check your inbox and spam folder.";
            return RedirectToAction(nameof(VerifyResetOtp), new { email = cleanEmail });
        }

        [HttpGet]
        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult VerifyResetOtp(string? email)
        {
            SetNoCacheHeaders();

            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                return RedirectAuthenticatedUser();
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                return RedirectToAction(nameof(ForgotPassword));
            }

            var model = new VerifyResetOtpViewModel
            {
                Email = email.Trim().ToLowerInvariant()
            };

            return View(model);
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> VerifyResetOtp(VerifyResetOtpViewModel model)
        {
            SetNoCacheHeaders();

            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                return RedirectAuthenticatedUser();
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var cleanEmail = model.Email.Trim().ToLowerInvariant();
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == cleanEmail);

            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "No account was found matching this email address.");
                return View(model);
            }

            if (user.PasswordResetOtpExpiry.HasValue && user.PasswordResetOtpExpiry.Value < DateTime.UtcNow)
            {
                ModelState.AddModelError(nameof(model.Otp), "This OTP has expired. Please click 'Resend OTP' to receive a new code.");
                return View(model);
            }

            var enteredOtp = model.Otp?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(user.PasswordResetOtp) || !string.Equals(user.PasswordResetOtp.Trim(), enteredOtp, StringComparison.Ordinal))
            {
                ModelState.AddModelError(nameof(model.Otp), "Invalid OTP code. Please enter the correct 6-digit code sent to your email.");
                return View(model);
            }

            // OTP verified — generate a secure reset token for the password form
            var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            user.PasswordResetToken = token;
            user.PasswordResetTokenExpiry = DateTime.UtcNow.AddHours(1);
            user.PasswordResetOtp = null;
            user.PasswordResetOtpExpiry = null;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "OTP verified successfully! Please set your new password.";
            return RedirectToAction(nameof(ResetPassword), new { token, email = user.Email });
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendResetOtp(string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                TempData["ErrorMessage"] = "Please provide an email address.";
                return RedirectToAction(nameof(ForgotPassword));
            }

            var cleanEmail = email.Trim().ToLowerInvariant();
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == cleanEmail);

            if (user != null)
            {
                // Generate fresh OTP
                var otp = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
                user.PasswordResetOtp = otp;
                user.PasswordResetOtpExpiry = DateTime.UtcNow.AddMinutes(15);
                await _context.SaveChangesAsync();

                await _emailService.SendPasswordResetOtpEmailAsync(user.Email, user.FullName, otp);
            }

            TempData["SuccessMessage"] = "A fresh 6-digit OTP has been sent. Please check your inbox and spam folder.";
            return RedirectToAction(nameof(VerifyResetOtp), new { email = cleanEmail });
        }

        [HttpGet]
        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> ResetPassword(string? token, string? email)
        {
            SetNoCacheHeaders();

            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                return RedirectAuthenticatedUser();
            }

            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(email))
            {
                TempData["ErrorMessage"] = "Invalid password reset link. Please request a new one.";
                return RedirectToAction(nameof(ForgotPassword));
            }

            var cleanEmail = email.Trim().ToLowerInvariant();
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == cleanEmail);

            if (user == null || user.PasswordResetToken != token)
            {
                TempData["ErrorMessage"] = "Invalid password reset link. Please request a new one.";
                return RedirectToAction(nameof(ForgotPassword));
            }

            if (user.PasswordResetTokenExpiry.HasValue && user.PasswordResetTokenExpiry.Value < DateTime.UtcNow)
            {
                TempData["ErrorMessage"] = "This password reset link has expired. Please request a new one.";
                return RedirectToAction(nameof(ForgotPassword));
            }

            var model = new ResetPasswordViewModel
            {
                Token = token,
                Email = user.Email
            };

            return View(model);
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            SetNoCacheHeaders();

            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                return RedirectAuthenticatedUser();
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var cleanEmail = model.Email.Trim().ToLowerInvariant();
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == cleanEmail);

            if (user == null || user.PasswordResetToken != model.Token)
            {
                ModelState.AddModelError(string.Empty, "Invalid password reset token. Please request a new reset link.");
                return View(model);
            }

            if (user.PasswordResetTokenExpiry.HasValue && user.PasswordResetTokenExpiry.Value < DateTime.UtcNow)
            {
                ModelState.AddModelError(string.Empty, "This password reset link has expired. Please request a new one.");
                return View(model);
            }

            // Update password
            user.PasswordHash = _passwordHasher.HashPassword(user, model.Password);
            user.PasswordResetToken = null;
            user.PasswordResetTokenExpiry = null;
            user.PasswordResetOtp = null;
            user.PasswordResetOtpExpiry = null;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Your password has been reset successfully! You can now sign in with your new password.";
            return RedirectToAction(nameof(Login));
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Profile()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var email = User.FindFirstValue(ClaimTypes.Email);

            User? user = null;
            if (int.TryParse(userIdStr, out int userId))
            {
                user = await _context.Users.FindAsync(userId);
            }

            if (user == null && !string.IsNullOrEmpty(email))
            {
                user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());
            }

            if (user == null)
            {
                return RedirectToAction(nameof(Login));
            }

            return View(user);
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProfilePicture(IFormFile? profilePicture)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var email = User.FindFirstValue(ClaimTypes.Email);

            User? user = null;
            if (int.TryParse(userIdStr, out int userId))
            {
                user = await _context.Users.FindAsync(userId);
            }

            if (user == null && !string.IsNullOrEmpty(email))
            {
                user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());
            }

            if (user == null)
            {
                if (IsAjaxRequest())
                {
                    return Json(new { success = false, message = "User not found or session has expired. Please log in again." });
                }
                return RedirectToAction(nameof(Login));
            }

            if (profilePicture == null || profilePicture.Length == 0)
            {
                var msg = "Please select an image file to upload.";
                if (IsAjaxRequest())
                {
                    return Json(new { success = false, message = msg });
                }
                TempData["ErrorMessage"] = msg;
                return RedirectToAction(nameof(Profile));
            }

            // Validate file size (max 5 MB)
            const long maxFileSize = 5 * 1024 * 1024;
            if (profilePicture.Length > maxFileSize)
            {
                var msg = "Image file size exceeds the 5 MB limit. Please choose a smaller photo.";
                if (IsAjaxRequest())
                {
                    return Json(new { success = false, message = msg });
                }
                TempData["ErrorMessage"] = msg;
                return RedirectToAction(nameof(Profile));
            }

            // Validate file extension
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
            var extension = Path.GetExtension(profilePicture.FileName)?.ToLowerInvariant();
            if (string.IsNullOrEmpty(extension) || !allowedExtensions.Contains(extension))
            {
                var msg = "Invalid file type. Only JPG, JPEG, PNG, WEBP, and GIF images are permitted.";
                if (IsAjaxRequest())
                {
                    return Json(new { success = false, message = msg });
                }
                TempData["ErrorMessage"] = msg;
                return RedirectToAction(nameof(Profile));
            }

            // Validate content type
            if (!profilePicture.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                var msg = "The uploaded file does not appear to be a valid image format.";
                if (IsAjaxRequest())
                {
                    return Json(new { success = false, message = msg });
                }
                TempData["ErrorMessage"] = msg;
                return RedirectToAction(nameof(Profile));
            }

            string? newPictureUrl = null;

            // Attempt Cloudinary upload first
            try
            {
                var uploadResult = await _cloudinaryService.UploadProfilePictureAsync(profilePicture);
                if (uploadResult != null && uploadResult.Success && !string.IsNullOrWhiteSpace(uploadResult.SecureUrl))
                {
                    newPictureUrl = uploadResult.SecureUrl;
                }
                else
                {
                    _logger.LogWarning("Cloudinary profile upload returned non-success: {Error}. Falling back to local storage.", uploadResult?.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Exception during Cloudinary profile upload. Falling back to local storage.");
            }

            // Local storage fallback if Cloudinary credentials missing or upload failed
            if (string.IsNullOrWhiteSpace(newPictureUrl))
            {
                try
                {
                    var webRoot = _webHostEnvironment.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                    var uploadsDir = Path.Combine(webRoot, "uploads", "profiles");
                    if (!Directory.Exists(uploadsDir))
                    {
                        Directory.CreateDirectory(uploadsDir);
                    }

                    var uniqueFileName = $"profile_{user.Id}_{DateTime.UtcNow.Ticks}_{Guid.NewGuid():N}{extension}";
                    var filePath = Path.Combine(uploadsDir, uniqueFileName);

                    await using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await profilePicture.CopyToAsync(fileStream);
                    }

                    newPictureUrl = $"/uploads/profiles/{uniqueFileName}";
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to store profile picture locally.");
                    var msg = "Failed to upload profile picture. Please try again.";
                    if (IsAjaxRequest())
                    {
                        return Json(new { success = false, message = msg });
                    }
                    TempData["ErrorMessage"] = msg;
                    return RedirectToAction(nameof(Profile));
                }
            }

            // Update user record
            user.Picture = newPictureUrl;
            _context.Users.Update(user);
            await _context.SaveChangesAsync();

            // Refresh cookie claims so navbar & session immediately display new picture
            await RefreshUserClaimsAsync(user);

            // Use the original image URL directly so original size/framing is shown without face-zoom
            var pictureUrl = user.Picture;

            if (IsAjaxRequest())
            {
                return Json(new
                {
                    success = true,
                    message = "Profile picture updated successfully!",
                    pictureUrl = pictureUrl,
                    rawUrl = user.Picture
                });
            }

            TempData["SuccessMessage"] = "Profile picture updated successfully!";
            return RedirectToAction(nameof(Profile));
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveProfilePicture()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var email = User.FindFirstValue(ClaimTypes.Email);

            User? user = null;
            if (int.TryParse(userIdStr, out int userId))
            {
                user = await _context.Users.FindAsync(userId);
            }

            if (user == null && !string.IsNullOrEmpty(email))
            {
                user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());
            }

            if (user == null)
            {
                if (IsAjaxRequest())
                {
                    return Json(new { success = false, message = "User not found or session has expired." });
                }
                return RedirectToAction(nameof(Login));
            }

            user.Picture = null;
            _context.Users.Update(user);
            await _context.SaveChangesAsync();

            await RefreshUserClaimsAsync(user);

            var fallbackInitial = string.IsNullOrEmpty(user.FullName) ? "U" : user.FullName.Substring(0, 1).ToUpper();

            if (IsAjaxRequest())
            {
                return Json(new
                {
                    success = true,
                    message = "Profile picture removed successfully.",
                    fallbackInitial
                });
            }

            TempData["SuccessMessage"] = "Profile picture removed successfully.";
            return RedirectToAction(nameof(Profile));
        }

        private async Task RefreshUserClaimsAsync(User user)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.FullName),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim("PhoneNumber", user.PhoneNumber ?? ""),
                new Claim("City", user.City ?? ""),
                new Claim("Area", user.Area ?? "")
            };

            if (!string.IsNullOrEmpty(user.Picture))
            {
                claims.Add(new Claim("Picture", user.Picture));
            }

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30),
                AllowRefresh = true
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);
        }

        private bool IsAjaxRequest()
        {
            return Request.Headers["X-Requested-With"] == "XMLHttpRequest"
                || Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);
        }

        [HttpPost]
        [HttpGet]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            HttpContext.Session.Clear();
            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }

        private IActionResult RedirectAuthenticatedUser()
        {
            var role = User.FindFirst(ClaimTypes.Role)?.Value;
            if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) || User.IsInRole("Admin"))
            {
                return RedirectToAction("Dashboard", "Admin");
            }
            if (string.Equals(role, "Local Guardian", StringComparison.OrdinalIgnoreCase) || User.IsInRole("Local Guardian"))
            {
                return RedirectToAction("Index", "Student");
            }
            if (string.Equals(role, "Educator", StringComparison.OrdinalIgnoreCase) || User.IsInRole("Educator"))
            {
                return RedirectToAction("Index", "Lecture");
            }
            return RedirectToAction("Index", "Home");
        }

        private void SetNoCacheHeaders()
        {
            Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate, max-age=0";
            Response.Headers["Pragma"] = "no-cache";
            Response.Headers["Expires"] = "-1";
        }
    }
}
