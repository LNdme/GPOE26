using GPOE26.ApiService.Model;
using Microsoft.EntityFrameworkCore;

namespace GPOE26.ApiService.Data
{
    public class ApiServiceContext : DbContext
    {
        public ApiServiceContext(DbContextOptions<ApiServiceContext> options) : base(options)
        {
        }
        public DbSet<NewArticle> NewsArticles { get; set; }
        public DbSet<SchoolEvent> Events { get; set; }
        public DbSet<Speech> Speeches { get; set; }
        public DbSet<SchoolActivity> Activities { get; set; }
        public DbSet<Hierarchy> Hierarchies { get; set; } 
        public DbSet<Contact> Contacts { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
        }
    }
}
