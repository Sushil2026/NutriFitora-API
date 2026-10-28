using Microsoft.EntityFrameworkCore;
namespace NutriApi;

public class AppDb(DbContextOptions<AppDb> o) : DbContext(o)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<EventItem> Events => Set<EventItem>();
    public DbSet<Feedback> Feedbacks => Set<Feedback>();
    public DbSet<DietPlan> DietPlans => Set<DietPlan>();
    public DbSet<DailyLog> Logs => Set<DailyLog>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<Lead> Leads => Set<Lead>();
    protected override void OnModelCreating(ModelBuilder m) => m.Entity<AppUser>().HasIndex(u => u.Email).IsUnique();
}
