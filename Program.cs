using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Onudhabon.Data;
using Onudhabon.Models;
using Onudhabon.Services;

// Enable legacy timestamp behavior for Npgsql to handle DateTime conversions smoothly
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

// Load environment variables from .env file if present
if (File.Exists(".env"))
{
    DotNetEnv.Env.Load();
}

var builder = WebApplication.CreateBuilder(args);

// Dynamically bind port if provided by container/cloud hosting (e.g., Render, Railway, Docker)
var envPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(envPort))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{envPort}");
}

// Support reverse-proxy forwarded headers (Render, Cloudflare, AWS)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Add Database Context (PostgreSQL via DATABASE_URL from .env or configuration)
var rawConnectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? builder.Configuration["DATABASE_URL"]
    ?? throw new InvalidOperationException("DATABASE_URL not found in .env or configuration.");

var npgsqlConnectionString = PostgresConnectionHelper.ConvertUrlToConnectionString(rawConnectionString);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(npgsqlConnectionString));

// Cloudinary & Storage Services
builder.Services.AddScoped<ICloudinaryService, CloudinaryService>();
builder.Services.AddScoped<IVolunteerRankingService, VolunteerRankingService>();

// Payment Gateway Services (Sandbox & Production)
builder.Services.AddScoped<ISSLCommerzService, SSLCommerzService>();

// Email Services (Gmail SMTP & Real-time Validation)
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IEmailValidationService, EmailValidationService>();

// HttpClient and Memory Cache
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();

// AI & Knowledge Services (Gemini Cloud API & PDF Knowledge Base)
builder.Services.AddSingleton<ILlmChatService, GeminiChatService>();
builder.Services.AddScoped<IPdfKnowledgeService, PdfKnowledgeService>();
builder.Services.AddScoped<IEducationalUploadAiService, EducationalUploadAiService>();
builder.Services.AddScoped<IChatOrchestrator, ChatOrchestrator>();
builder.Services.AddHostedService<ExpiredStudyDocumentCleanupService>();

// Password Hasher for User
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();

// Cookie Authentication with Session Expiration
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "Onudhabon.AuthCookie";
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30); // Session timeout duration
        options.SlidingExpiration = true;                 // Resets expiration window on user activity
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Events = new CookieAuthenticationEvents
        {
            OnValidatePrincipal = async context =>
            {
                var userPrincipal = context.Principal;
                var userIdStr = userPrincipal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!int.TryParse(userIdStr, out int userId))
                {
                    return;
                }

                var dbContext = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
                var user = await dbContext.Users.FindAsync(userId);
                if (user == null || user.IsRestricted || string.Equals(user.VerificationStatus, "Declined", StringComparison.OrdinalIgnoreCase))
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    return;
                }

                if (!string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase))
                {
                    if (!user.IsEmailVerified)
                    {
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                        return;
                    }

                    bool isApproved = user.IsVerified ||
                        string.Equals(user.VerificationStatus, "Active", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(user.VerificationStatus, "Approved", StringComparison.OrdinalIgnoreCase);

                    if (!isApproved)
                    {
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                        return;
                    }
                }
            }
        };
    });

// Session State Services (for HttpContext.Session)
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.Name = "Onudhabon.Session";
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

// Add MVC Services
builder.Services.AddControllersWithViews();
builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { error = "Too many AI requests. Please wait a moment and try again." },
            cancellationToken);
    };

    options.AddPolicy("ai-chat", context => RateLimitPartition.GetFixedWindowLimiter(
        GetAiRateLimitPartition(context),
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 12,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));

    options.AddPolicy("ai-upload", context => RateLimitPartition.GetFixedWindowLimiter(
        GetAiRateLimitPartition(context),
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 3,
            Window = TimeSpan.FromHours(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

static string GetAiRateLimitPartition(HttpContext context) =>
    context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? context.Session.Id;

// Add CORS for Gateway Callbacks
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseStaticFiles();
app.UseRouting();

app.UseCors();

// Private Network Access (PNA) header support for gateway callbacks from public HTTPS to localhost
app.Use(async (context, next) =>
{
    if (context.Request.Headers.ContainsKey("Access-Control-Request-Private-Network"))
    {
        context.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
    }
    await next();
});

app.UseAuthentication();
app.UseAuthorization();
app.UseSession();
app.UseRateLimiter();

// Prevent browser from caching auth pages or authenticated pages (prevents back-button access after login)
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";
    if (path.StartsWith("/account/login") || 
        path.StartsWith("/account/register") || 
        path.StartsWith("/admin") || 
        context.User.Identity?.IsAuthenticated == true)
    {
        context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate, max-age=0";
        context.Response.Headers["Pragma"] = "no-cache";
        context.Response.Headers["Expires"] = "-1";
    }
    await next();
});

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

// Seed initial database data
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        context.Database.Migrate();
        var hasher = services.GetRequiredService<IPasswordHasher<User>>();
        DbInitializer.SeedAdminUser(context, hasher);
        DbInitializer.SeedClassPlans(context);
        DbInitializer.SeedStudents(context);
        DbInitializer.SeedForumPosts(context);
        DbInitializer.SeedDonations(context);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the database.");
    }
}

app.Run();
