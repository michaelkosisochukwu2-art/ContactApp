using Microsoft.EntityFrameworkCore;
using MyOnlineCv.Models;

namespace MyOnlineCv.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public DbSet<EducationItem> EducationItems { get; set; }
        public DbSet<ExperienceItem> ExperienceItems { get; set; }
        public DbSet<SkillItem> SkillItems { get; set; }
        public DbSet<ContactRequest> ContactRequests { get; set; }
        public DbSet<GalleryItem> GalleryItems { get; set; }

        // Register TaxCalculationResult table in SQL Database
        public DbSet<TaxCalculationResult> TaxCalculationResults { get; set; }
    }
}