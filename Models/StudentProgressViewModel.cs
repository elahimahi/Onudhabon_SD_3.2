using System.Text.Json.Serialization;

namespace Onudhabon.Models
{
    public class SubjectProgressItem
    {
        private string _subjectName = string.Empty;

        [JsonPropertyName("subjectName")]
        public string SubjectName
        {
            get => _subjectName;
            set => _subjectName = value ?? string.Empty;
        }

        [JsonPropertyName("name")]
        public string? NameAlias
        {
            get => _subjectName;
            set { if (!string.IsNullOrWhiteSpace(value)) _subjectName = value; }
        }

        [JsonPropertyName("totalLectures")]
        public int TotalLectures { get; set; } = 12;

        [JsonPropertyName("completedLectures")]
        public int CompletedLectures { get; set; } = 0;

        [JsonPropertyName("syllabus")]
        public string? Syllabus { get; set; }

        [JsonPropertyName("marks")]
        public double? Marks { get; set; }

        [JsonPropertyName("grade")]
        public string? Grade { get; set; }

        [JsonPropertyName("lectures")]
        public List<LectureEvaluationItem> LectureEvaluations { get; set; } = new();

        [JsonPropertyName("progressPercentage")]
        public double ProgressPercentage => TotalLectures > 0 
            ? Math.Round((double)CompletedLectures / TotalLectures * 100.0, 1) 
            : 0.0;

        [JsonIgnore]
        public double CalculatedGPA
        {
            get
            {
                if (LectureEvaluations != null && LectureEvaluations.Any())
                {
                    var validPoints = LectureEvaluations
                        .Select(l => GradeToPoint(l.Grade))
                        .Where(p => p >= 0)
                        .ToList();
                    if (validPoints.Any())
                    {
                        return Math.Round(validPoints.Average(), 2);
                    }
                }
                return GradePoint >= 0 ? GradePoint : -1.0;
            }
        }

        [JsonIgnore]
        public string CalculatedGrade => DisplayGrade;

        public static double GradeToPoint(string? g) => g?.Trim().ToUpperInvariant() switch
        {
            "A+" => 5.0,
            "A" => 4.0,
            "A-" => 3.5,
            "B" => 3.0,
            "C" => 2.0,
            "D" => 1.0,
            "F" => 0.0,
            _ => -1.0
        };

        [JsonIgnore]
        public double GradePoint => Grade?.Trim().ToUpperInvariant() switch
        {
            "A+" => 5.0,
            "A" => 4.0,
            "A-" => 3.5,
            "B" => 3.0,
            "C" => 2.0,
            "D" => 1.0,
            "F" => 0.0,
            _ => -1.0
        };

        [JsonIgnore]
        public string DisplayGrade
        {
            get
            {
                if (LectureEvaluations != null && LectureEvaluations.Any())
                {
                    if (LectureEvaluations.Any(l => l.Grade?.Trim().ToUpperInvariant() == "F")) return "F";
                    double gpa = CalculatedGPA;
                    if (gpa >= 5.0) return "A+";
                    if (gpa >= 4.0) return "A";
                    if (gpa >= 3.5) return "A-";
                    if (gpa >= 3.0) return "B";
                    if (gpa >= 2.0) return "C";
                    if (gpa >= 1.0) return "D";
                    if (gpa >= 0.0) return "F";
                }
                return string.IsNullOrWhiteSpace(Grade) ? "Pending" : Grade.Trim().ToUpperInvariant();
            }
        }

        [JsonIgnore]
        public string GradeBadgeClass => DisplayGrade switch
        {
            "A+" => "bg-success text-white shadow-sm",
            "A" => "bg-success-subtle text-success border border-success-subtle",
            "A-" => "bg-primary-subtle text-primary border border-primary-subtle",
            "B" => "bg-info-subtle text-info-emphasis border border-info-subtle",
            "C" => "bg-warning-subtle text-warning-emphasis border border-warning-subtle",
            "D" => "bg-secondary-subtle text-secondary border border-secondary-subtle",
            "F" => "bg-danger text-white",
            _ => "bg-light text-muted border"
        };
    }

    public class StudentProgressCardViewModel
    {
        public Student Student { get; set; } = new();
        public List<SubjectProgressItem> SubjectProgress { get; set; } = new();
        public int TotalLectures => SubjectProgress.Sum(s => s.TotalLectures);
        public int CompletedLectures => SubjectProgress.Sum(s => s.CompletedLectures);

        public double OverallProgressPercent
        {
            get
            {
                if (TotalLectures > 0)
                {
                    return Math.Round((double)CompletedLectures / TotalLectures * 100.0, 1);
                }
                return Student.ProgressPercentage;
            }
        }

        public bool IsDeclined => Student.Status?.Trim().Equals("declined", StringComparison.OrdinalIgnoreCase) == true;

        public bool CanUpdateProgress => !IsDeclined;

        public bool HasPassingAssessment => SubjectProgress.Count > 0 && SubjectProgress.All(s =>
            !string.IsNullOrWhiteSpace(s.DisplayGrade) &&
            !string.Equals(s.DisplayGrade, "Pending", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(s.DisplayGrade, "Not Evaluated", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(s.DisplayGrade, "F", StringComparison.OrdinalIgnoreCase));

        public bool CanPromote => OverallProgressPercent >= 100.0 && HasPassingAssessment && !IsDeclined;

        public string? NextClassLevel
        {
            get
            {
                if (int.TryParse(Student.ClassLevel?.Replace("Class", "", StringComparison.OrdinalIgnoreCase).Trim(), out int current))
                {
                    return (current + 1).ToString();
                }
                return null;
            }
        }

        public string StatusDisplay
        {
            get
            {
                var st = Student.Status?.ToLower();
                if (st == "active" || st == "approved" || st == "verified") return "verified";
                if (st == "declined") return "declined";
                return "pending";
            }
        }

        public bool HasExamEvaluation => SubjectProgress.Any(s => (s.LectureEvaluations != null && s.LectureEvaluations.Any()) || !string.IsNullOrWhiteSpace(s.Grade) || s.Marks.HasValue);

        public int EvaluatedSubjectsCount => SubjectProgress.Count(s => (s.LectureEvaluations != null && s.LectureEvaluations.Any()) || !string.IsNullOrWhiteSpace(s.Grade));

        public int TotalEvaluatedLecturesCount => SubjectProgress.Sum(s => s.LectureEvaluations?.Count ?? 0);

        public string OverallGrade
        {
            get
            {
                var allEvaluatedLectures = SubjectProgress
                    .SelectMany(s => s.LectureEvaluations ?? new List<LectureEvaluationItem>())
                    .Where(l => !string.IsNullOrWhiteSpace(l.Grade))
                    .ToList();

                if (allEvaluatedLectures.Any())
                {
                    if (allEvaluatedLectures.Any(l => l.Grade.Trim().ToUpperInvariant() == "F"))
                    {
                        return "F";
                    }
                    var lecturePoints = allEvaluatedLectures
                        .Select(l => SubjectProgressItem.GradeToPoint(l.Grade))
                        .Where(p => p >= 0)
                        .ToList();
                    if (lecturePoints.Any())
                    {
                        double avg = lecturePoints.Average();
                        if (avg >= 5.0) return "A+";
                        if (avg >= 4.0) return "A";
                        if (avg >= 3.5) return "A-";
                        if (avg >= 3.0) return "B";
                        if (avg >= 2.0) return "C";
                        if (avg >= 1.0) return "D";
                        return "F";
                    }
                }

                var evaluated = SubjectProgress.Where(s => !string.IsNullOrWhiteSpace(s.Grade)).ToList();
                if (!evaluated.Any()) return "Not Evaluated";

                if (evaluated.Any(s => s.Grade?.Trim().ToUpperInvariant() == "F"))
                {
                    return "F";
                }

                var validPoints = evaluated.Select(s => s.GradePoint).Where(gp => gp >= 0).ToList();
                if (!validPoints.Any()) return "Not Evaluated";

                double avgGpa = validPoints.Average();
                if (avgGpa >= 5.0) return "A+";
                if (avgGpa >= 4.0) return "A";
                if (avgGpa >= 3.5) return "A-";
                if (avgGpa >= 3.0) return "B";
                if (avgGpa >= 2.0) return "C";
                if (avgGpa >= 1.0) return "D";
                return "F";
            }
        }

        public string? OverallGPA
        {
            get
            {
                var allEvaluatedLectures = SubjectProgress
                    .SelectMany(s => s.LectureEvaluations ?? new List<LectureEvaluationItem>())
                    .Where(l => !string.IsNullOrWhiteSpace(l.Grade))
                    .ToList();

                if (allEvaluatedLectures.Any())
                {
                    if (allEvaluatedLectures.Any(l => l.Grade.Trim().ToUpperInvariant() == "F"))
                    {
                        return "0.00";
                    }
                    var lecturePoints = allEvaluatedLectures
                        .Select(l => SubjectProgressItem.GradeToPoint(l.Grade))
                        .Where(p => p >= 0)
                        .ToList();
                    if (lecturePoints.Any())
                    {
                        return lecturePoints.Average().ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                    }
                }

                var evaluated = SubjectProgress.Where(s => !string.IsNullOrWhiteSpace(s.Grade)).ToList();
                if (!evaluated.Any()) return null;

                if (evaluated.Any(s => s.Grade?.Trim().ToUpperInvariant() == "F"))
                {
                    return "0.00";
                }

                var validPoints = evaluated.Select(s => s.GradePoint).Where(gp => gp >= 0).ToList();
                if (!validPoints.Any()) return null;

                return validPoints.Average().ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        public string OverallGradeBadgeClass => OverallGrade switch
        {
            "A+" => "bg-success text-white shadow-sm",
            "A" => "bg-success-subtle text-success border border-success-subtle",
            "A-" => "bg-primary-subtle text-primary border border-primary-subtle",
            "B" => "bg-info-subtle text-info-emphasis border border-info-subtle",
            "C" => "bg-warning-subtle text-warning-emphasis border border-warning-subtle",
            "D" => "bg-secondary-subtle text-secondary border border-secondary-subtle",
            "F" => "bg-danger text-white",
            _ => "bg-light text-muted border"
        };
    }

    public class StudentProgressViewModel
    {
        public List<StudentProgressCardViewModel> Cards { get; set; } = new();
        public List<Student> Students { get; set; } = new();
        public int TotalStudents => Cards.Count;
        public double AvgProgress => Cards.Any() ? Math.Round(Cards.Average(c => c.OverallProgressPercent), 1) : 0.0;
        public int AvgAttendance => Cards.Any() ? (int)Math.Round(Cards.Average(c => c.Student.AttendancePercentage)) : 0;
        public string? SearchQuery { get; set; }
        public string? SelectedClass { get; set; }
        public string? SelectedSubject { get; set; }
        public string? ProgressStatus { get; set; }
        public List<string> AvailableClasses { get; set; } = new();
        public List<string> AvailableSubjects { get; set; } = new();
        public List<StudentProgressChange> RecentChanges { get; set; } = new();
        public bool IsAdminView { get; set; }
    }

    public class UpdateStudentProgressInput
    {
        public int StudentId { get; set; }
        public int AttendancePercentage { get; set; } = 90;
        public string? Notes { get; set; }
        public List<SubjectLectureProgressInput> SubjectProgress { get; set; } = new();
    }

    public class SubjectLectureProgressInput
    {
        private string _subjectName = string.Empty;

        public string SubjectName
        {
            get => _subjectName;
            set => _subjectName = value ?? string.Empty;
        }

        public string? Name
        {
            get => _subjectName;
            set { if (!string.IsNullOrWhiteSpace(value)) _subjectName = value; }
        }

        public int TotalLectures { get; set; } = 12;
        public int CompletedLectures { get; set; } = 0;
    }

    public class UpdateExamEvaluationInput
    {
        public int StudentId { get; set; }
        public string? Remarks { get; set; }
        public List<SubjectExamEvaluationInput> Evaluations { get; set; } = new();
    }

    public class SubjectExamEvaluationInput
    {
        public string SubjectName { get; set; } = string.Empty;
        public string? Syllabus { get; set; }
        public double? Marks { get; set; }
        public string? Grade { get; set; }
    }

    public class EvaluateLectureInput
    {
        public int StudentId { get; set; }
        public string SubjectName { get; set; } = string.Empty;
        public int LectureNumber { get; set; }
        public string Topic { get; set; } = string.Empty;
        public double? Marks { get; set; }
        public string Grade { get; set; } = "A+";
        public string? Remarks { get; set; }
        public bool IsDelete { get; set; } = false;
    }
}
