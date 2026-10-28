namespace NutriApi;

public abstract class Entity { public int Id { get; set; } public bool IsActive { get; set; } = true; }
public class Service : Entity { public string Name { get; set; } = ""; public string Description { get; set; } = ""; public string Icon { get; set; } = "🥗"; }
public class Plan : Entity { public string Name { get; set; } = ""; public string Description { get; set; } = ""; public decimal Price { get; set; } public int DurationDays { get; set; } = 30; public string Features { get; set; } = ""; public bool IsPopular { get; set; } }
public class EventItem : Entity { public string Title { get; set; } = ""; public string Description { get; set; } = ""; public DateTime EventDate { get; set; } = DateTime.UtcNow; public string Location { get; set; } = ""; }
public class Feedback : Entity { public string CustomerName { get; set; } = ""; public int Rating { get; set; } = 5; public string Comment { get; set; } = ""; }

public class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = ""; public string PasswordHash { get; set; } = "";
    public string FullName { get; set; } = ""; public string Phone { get; set; } = "";
    public string Role { get; set; } = "Customer";       // Admin | Nutritionist | Customer
    public bool IsActive { get; set; } = true;
    public string Specialization { get; set; } = "";     // nutritionists
    public Guid? NutritionistId { get; set; }            // customers
    public int? PlanId { get; set; }
}
public class DietPlan { public int Id { get; set; } public Guid ClientId { get; set; } public Guid NutritionistId { get; set; } public string Title { get; set; } = ""; public string Details { get; set; } = ""; public DateTime CreatedAt { get; set; } = DateTime.UtcNow; }
public class DailyLog { public int Id { get; set; } public Guid ClientId { get; set; } public DateOnly LogDate { get; set; } public string Meals { get; set; } = ""; public int Calories { get; set; } public int WaterMl { get; set; } public double WeightKg { get; set; } public string Notes { get; set; } = ""; }
public class Message { public int Id { get; set; } public Guid FromId { get; set; } public Guid ToId { get; set; } public string Body { get; set; } = ""; public DateTime SentAt { get; set; } = DateTime.UtcNow; }
public class Ticket { public int Id { get; set; } public Guid ClientId { get; set; } public string Subject { get; set; } = ""; public string Body { get; set; } = ""; public string Status { get; set; } = "Open"; public DateTime CreatedAt { get; set; } = DateTime.UtcNow; }
public class Lead { public int Id { get; set; } public string Name { get; set; } = ""; public string Phone { get; set; } = ""; public string Goal { get; set; } = ""; public string Notes { get; set; } = ""; public string Status { get; set; } = "New"; public DateTime CreatedAt { get; set; } = DateTime.UtcNow; }
