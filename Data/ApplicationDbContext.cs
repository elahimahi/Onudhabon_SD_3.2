using Microsoft.EntityFrameworkCore;
using Onudhabon.Models;

namespace Onudhabon.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Student> Students { get; set; }
        public DbSet<StudentProgressChange> StudentProgressChanges { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<Material> Materials { get; set; }
        public DbSet<Lecture> Lectures { get; set; }
        public DbSet<Forum> Forums { get; set; }
        public DbSet<ForumPost> ForumPosts { get; set; }
        public DbSet<ForumComment> ForumComments { get; set; }
        public DbSet<ForumPostReaction> ForumPostReactions { get; set; }
        public DbSet<ClassPlan> ClassPlans { get; set; }
        public DbSet<Donation> Donations { get; set; }
        public DbSet<StudyDocument> StudyDocuments { get; set; }
        public DbSet<StudyDocumentChunk> StudyDocumentChunks { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<StudyDocument>(entity =>
            {
                entity.ToTable("StudyDocuments");
                entity.HasKey(document => document.Id);
                entity.Property(document => document.OwnerKey).IsRequired().HasMaxLength(128);
                entity.Property(document => document.FileName).IsRequired().HasMaxLength(255);
                entity.Property(document => document.CreatedAt).HasColumnType("timestamp without time zone").HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(document => document.ExpiresAt).HasColumnType("timestamp without time zone");
                entity.HasIndex(document => new { document.OwnerKey, document.ExpiresAt });
            });

            modelBuilder.Entity<StudyDocumentChunk>(entity =>
            {
                entity.ToTable("StudyDocumentChunks");
                entity.HasKey(chunk => chunk.Id);
                entity.Property(chunk => chunk.Content).IsRequired();
                entity.HasIndex(chunk => new { chunk.StudyDocumentId, chunk.PageNumber, chunk.ChunkIndex }).IsUnique();
                entity.HasOne(chunk => chunk.StudyDocument)
                    .WithMany(document => document.Chunks)
                    .HasForeignKey(chunk => chunk.StudyDocumentId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // User Entity Configuration
            modelBuilder.Entity<User>(entity =>
            {
                entity.ToTable("Users");
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Email).IsUnique();
                entity.Property(e => e.FullName).IsRequired().HasMaxLength(150);
                entity.Property(e => e.Email).IsRequired().HasMaxLength(256);
                entity.Property(e => e.PhoneNumber).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Role).IsRequired().HasMaxLength(50);
                entity.Property(e => e.City).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Area).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Location).HasMaxLength(255);
                entity.Property(e => e.VolunteerReason).HasMaxLength(1000);
                entity.Property(e => e.PasswordHash).IsRequired();
                entity.Property(e => e.AgreeToTerms).IsRequired();
                entity.Property(e => e.Bio).HasMaxLength(1000);
                entity.Property(e => e.Picture).HasMaxLength(500);
                entity.Property(e => e.IsRestricted).HasDefaultValue(false);
                entity.Property(e => e.IsVerified).HasDefaultValue(false);
                entity.Property(e => e.VerificationStatus).HasMaxLength(50).HasDefaultValue("Pending");
                entity.Property(e => e.IsEmailVerified).HasDefaultValue(false);
                entity.Property(e => e.EmailVerificationToken).HasMaxLength(100);
                entity.Property(e => e.EmailVerificationTokenExpiry);
                entity.Property(e => e.EmailOtp).HasMaxLength(10);
                entity.Property(e => e.EmailOtpExpiry);
                entity.Property(e => e.NidNumber).HasMaxLength(50);
                entity.Property(e => e.CertificatePicture).HasMaxLength(500);
                entity.Property(e => e.EducationLevel).HasMaxLength(100);
                entity.Property(e => e.Institution).HasMaxLength(200);
                entity.Property(e => e.Major).HasMaxLength(150);
                entity.Property(e => e.Age);
                entity.Property(e => e.SscPassingYear).HasMaxLength(20);
                entity.Property(e => e.SscInstitute).HasMaxLength(200);
                entity.Property(e => e.HscPassingYear).HasMaxLength(20);
                entity.Property(e => e.HscInstitute).HasMaxLength(200);
                entity.Property(e => e.UniversityName).HasMaxLength(200);
                entity.Property(e => e.UniversityPassingYear).HasMaxLength(20);
                entity.Property(e => e.CurrentlyStudying).HasMaxLength(100);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.__v).HasDefaultValue(0);
            });

            // Student Entity Configuration
            modelBuilder.Entity<Student>(entity =>
            {
                entity.ToTable("Students");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.FullName).IsRequired().HasMaxLength(150);
                entity.Property(e => e.BirthCertificateId).HasMaxLength(100);
                entity.Property(e => e.Address).HasMaxLength(300);
                entity.Property(e => e.FatherName).HasMaxLength(150);
                entity.Property(e => e.MotherName).HasMaxLength(150);
                entity.Property(e => e.ClassLevel).HasMaxLength(50);
                entity.Property(e => e.Subjects).HasMaxLength(500);
                entity.Property(e => e.EnrollmentYear).HasMaxLength(20);
                entity.Property(e => e.ConsentLetterUrl).HasMaxLength(500);
                entity.Property(e => e.GuardianId).HasMaxLength(50);
                entity.Property(e => e.GuardianName).HasMaxLength(150);
                entity.Property(e => e.Status).HasMaxLength(50).HasDefaultValue("Pending");
                entity.Property(e => e.CompletedClasses).HasDefaultValue(0);
                entity.Property(e => e.Age).HasDefaultValue(14);
                entity.Property(e => e.AttendancePercentage).HasDefaultValue(90);
                entity.Property(e => e.ProgressPercentage).HasDefaultValue(75);
                entity.Property(e => e.Notes).HasMaxLength(2000);
                entity.Property(e => e.PhotoUrl).HasMaxLength(500);
                entity.Property(e => e.SubjectProgressJson).HasMaxLength(4000);
                entity.Property(e => e.LastActivityDate).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.__v).HasDefaultValue(0);
            });

            modelBuilder.Entity<StudentProgressChange>(entity =>
            {
                entity.ToTable("StudentProgressChanges");
                entity.HasKey(change => change.Id);
                entity.Property(change => change.ActionType).IsRequired().HasMaxLength(40);
                entity.Property(change => change.Summary).IsRequired().HasMaxLength(500);
                entity.Property(change => change.ChangedBy).IsRequired().HasMaxLength(150);
                entity.Property(change => change.ChangedByRole).IsRequired().HasMaxLength(50);
                entity.Property(change => change.CreatedAt).HasColumnType("timestamp without time zone").HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasOne(change => change.Student).WithMany().HasForeignKey(change => change.StudentId).OnDelete(DeleteBehavior.Cascade);
                entity.HasIndex(change => new { change.StudentId, change.CreatedAt });
            });

            // Notification Entity Configuration
            modelBuilder.Entity<Notification>(entity =>
            {
                entity.ToTable("Notifications");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.User).HasMaxLength(100);
                entity.Property(e => e.Sender).HasMaxLength(100);
                entity.Property(e => e.Post).HasMaxLength(100);
                entity.Property(e => e.Type).HasMaxLength(50);
                entity.Property(e => e.IsRead).HasDefaultValue(false);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.__v).HasDefaultValue(0);
            });

            // Material Entity Configuration
            modelBuilder.Entity<Material>(entity =>
            {
                entity.ToTable("Materials");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Title).IsRequired().HasMaxLength(255);
                entity.Property(e => e.Description).HasMaxLength(2000);
                entity.Property(e => e.Instructor).HasMaxLength(150);
                entity.Property(e => e.Version).HasMaxLength(50);
                entity.Property(e => e.ClassLevel).HasMaxLength(50);
                entity.Property(e => e.Subject).HasMaxLength(100);
                entity.Property(e => e.Topic).HasMaxLength(150);
                entity.Property(e => e.FileUrl).HasMaxLength(500);
                entity.Property(e => e.Size).HasMaxLength(50);
                entity.Property(e => e.Downloads).HasDefaultValue(0);
                entity.Property(e => e.Status).HasMaxLength(50).HasDefaultValue("Active");
                entity.Property(e => e.Date).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.__v).HasDefaultValue(0);
            });

            // Lecture Entity Configuration
            modelBuilder.Entity<Lecture>(entity =>
            {
                entity.ToTable("Lectures");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Title).IsRequired().HasMaxLength(255);
                entity.Property(e => e.Description).HasMaxLength(2000);
                entity.Property(e => e.Instructor).HasMaxLength(150);
                entity.Property(e => e.Version).HasMaxLength(50);
                entity.Property(e => e.ClassLevel).HasMaxLength(50);
                entity.Property(e => e.Subject).HasMaxLength(100);
                entity.Property(e => e.Topic).HasMaxLength(150);
                entity.Property(e => e.Thumbnail).HasMaxLength(500);
                entity.Property(e => e.VideoUrl).HasMaxLength(500);
                entity.Property(e => e.Status).HasMaxLength(50).HasDefaultValue("Active");
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.__v).HasDefaultValue(0);
            });

            // Forum Entity Configuration
            modelBuilder.Entity<Forum>(entity =>
            {
                entity.ToTable("Forums");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Title).IsRequired().HasMaxLength(255);
                entity.Property(e => e.Content).HasMaxLength(4000);
                entity.Property(e => e.Tags).HasMaxLength(255);
                entity.Property(e => e.Author).HasMaxLength(150);
                entity.Property(e => e.Replies).HasDefaultValue(0);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.__v).HasDefaultValue(0);
            });

            // ForumPost Entity Configuration
            modelBuilder.Entity<ForumPost>(entity =>
            {
                entity.ToTable("ForumPosts");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Title).IsRequired().HasMaxLength(255);
                entity.Property(e => e.Content).HasMaxLength(4000);
                entity.Property(e => e.Author).HasMaxLength(150);
                entity.Property(e => e.AuthorRole).HasMaxLength(50);
                entity.Property(e => e.Tags).HasMaxLength(255);
                entity.Property(e => e.Category).HasMaxLength(100);
                entity.Property(e => e.Likes).HasDefaultValue(0);
                entity.Property(e => e.Dislikes).HasDefaultValue(0);
                entity.Property(e => e.Replies).HasDefaultValue(0);
                entity.Property(e => e.Status).HasMaxLength(50).HasDefaultValue("Pending");
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.__v).HasDefaultValue(0);
            });

            // ForumComment Entity Configuration
            modelBuilder.Entity<ForumComment>(entity =>
            {
                entity.ToTable("ForumComments");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Content).IsRequired().HasMaxLength(2000);
                entity.Property(e => e.Author).HasMaxLength(150);
                entity.Property(e => e.AuthorRole).HasMaxLength(50);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            });

            // ForumPostReaction Entity Configuration
            modelBuilder.Entity<ForumPostReaction>(entity =>
            {
                entity.ToTable("ForumPostReactions");
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.PostId, e.UserId }).IsUnique();
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            });

            // ClassPlan Entity Configuration
            modelBuilder.Entity<ClassPlan>(entity =>
            {
                entity.ToTable("ClassPlans");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ClassLevel).IsRequired().HasMaxLength(100);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.__v).HasDefaultValue(0);

                entity.OwnsMany(e => e.Subjects, subject =>
                {
                    subject.ToJson();
                    subject.Property(s => s.Name).HasJsonPropertyName("name");
                    subject.Property(s => s.TotalLectures).HasJsonPropertyName("totalLectures");
                });
            });

            // Donation Entity Configuration
            modelBuilder.Entity<Donation>(entity =>
            {
                entity.ToTable("Donations");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.DonorName).IsRequired().HasMaxLength(150);
                entity.Property(e => e.DonorEmail).HasMaxLength(200);
                entity.Property(e => e.DonorPhone).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Amount).HasColumnType("decimal(18,2)").IsRequired();
                entity.Property(e => e.Currency).HasMaxLength(10).HasDefaultValue("BDT");
                entity.Property(e => e.Purpose).IsRequired().HasMaxLength(150);
                entity.Property(e => e.Message).HasMaxLength(1000);
                entity.Property(e => e.PaymentMethod).HasMaxLength(50).HasDefaultValue("bKash");
                entity.Property(e => e.BkashWalletNumber).HasMaxLength(30);
                entity.Property(e => e.BkashTransactionId).HasMaxLength(100);
                entity.Property(e => e.BkashPaymentId).HasMaxLength(100);
                entity.Property(e => e.Status).HasMaxLength(50).HasDefaultValue("Completed");
                entity.Property(e => e.IsAnonymous).HasDefaultValue(false);
                entity.Property(e => e.UserId).HasMaxLength(100);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.__v).HasDefaultValue(0);
            });
        }
    }
}
