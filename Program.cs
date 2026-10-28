using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NutriApi;

var b = WebApplication.CreateBuilder(args);
b.Configuration.AddJsonFile("appsettings.Local.json", optional: true);   // local secrets, not committed to Git
b.Services.AddDbContext<AppDb>(o => o.UseNpgsql(b.Configuration.GetConnectionString("Default")));
var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(b.Configuration["Jwt:Key"]!));
b.Services.AddAuthentication("Bearer").AddJwtBearer(o =>
    o.TokenValidationParameters = new() { IssuerSigningKey = key, ValidateIssuer = false, ValidateAudience = false });
b.Services.AddAuthorization();
var origins = (b.Configuration["Cors:Origins"] ?? "http://localhost:5173").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
b.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));
var app = b.Build();
app.UseCors(); app.UseAuthentication(); app.UseAuthorization();

// ---- migrate + seed first admin
using (var s = app.Services.CreateScope())
{
    var db = s.ServiceProvider.GetRequiredService<AppDb>();
    db.Database.Migrate();
    if (!db.Users.Any(u => u.Role == "Admin"))
    {
        db.Users.Add(new AppUser { Email = "admin@nutri.com", FullName = "Administrator", Role = "Admin", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123") });
        db.SaveChanges();
    }
    if (!db.Services.Any() && !db.Plans.Any())   // starter content for the home page (edit in Admin)
    {
        db.Services.AddRange(
            new Service { Name = "Weight management", Description = "Sustainable fat loss or healthy weight gain with a plan that fits your routine.", Icon = "⚖️" },
            new Service { Name = "Clinical diets", Description = "Diet support for diabetes, PCOS, thyroid, cholesterol and more.", Icon = "🩺" },
            new Service { Name = "Sports nutrition", Description = "Fuel and recovery plans for training days and rest days.", Icon = "🏃" },
            new Service { Name = "Daily accountability", Description = "Daily check-ins and reminders so you never drift alone.", Icon = "✅" });
        db.Plans.AddRange(
            new Plan { Name = "Starter", Description = "A month to build the basics.", Price = 1999, DurationDays = 30, Features = "Personal diet plan,Weekly review,Chat support" },
            new Plan { Name = "Transform", Description = "Our most popular programme.", Price = 4999, DurationDays = 90, IsPopular = true, Features = "Personal diet plan,Daily check-ins,Chat with your nutritionist,Fortnightly plan updates" },
            new Plan { Name = "Clinical", Description = "For medical conditions, with extra care.", Price = 6499, DurationDays = 90, Features = "Condition-specific plan,Priority chat,Monthly progress report" });
        db.SaveChanges();
    }
}

Guid Uid(ClaimsPrincipal u) => Guid.Parse(u.FindFirstValue(ClaimTypes.NameIdentifier)!);
string Token(AppUser u) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
    claims: [new(ClaimTypes.NameIdentifier, u.Id.ToString()), new(ClaimTypes.Role, u.Role)],
    expires: DateTime.UtcNow.AddDays(1), signingCredentials: new(key, SecurityAlgorithms.HmacSha256)));

// ---- AUTH
app.MapPost("/api/auth/login", async (LoginDto d, AppDb db) =>
{
    var u = await db.Users.FirstOrDefaultAsync(x => x.Email == d.Email.ToLower());
    if (u is null || !u.IsActive || !BCrypt.Net.BCrypt.Verify(d.Password, u.PasswordHash)) return Results.Unauthorized();
    return Results.Ok(new { token = Token(u), role = u.Role, name = u.FullName });
});
app.MapPost("/api/auth/register", async (RegisterDto d, AppDb db) =>
{
    var email = d.Email.ToLower();
    if (await db.Users.AnyAsync(x => x.Email == email)) return Results.BadRequest("Email already registered");
    db.Users.Add(new AppUser { Email = email, FullName = d.FullName, Phone = d.Phone, PlanId = d.PlanId, PasswordHash = BCrypt.Net.BCrypt.HashPassword(d.Password) });
    await db.SaveChangesAsync();
    return Results.Ok();
});

// ---- PUBLIC
app.MapGet("/api/public/services", (AppDb db) => db.Services.Where(x => x.IsActive).OrderBy(x => x.Id).ToListAsync());
app.MapGet("/api/public/plans", (AppDb db) => db.Plans.Where(x => x.IsActive).OrderBy(x => x.Price).ToListAsync());
app.MapGet("/api/public/events", (AppDb db) => db.Events.Where(x => x.IsActive).OrderBy(x => x.EventDate).ToListAsync());
app.MapGet("/api/public/feedback", (AppDb db) => db.Feedbacks.Where(x => x.IsActive).OrderByDescending(x => x.Id).Take(12).ToListAsync());

app.MapPost("/api/public/consultation", async (LeadDto d, AppDb db) =>
{
    if (string.IsNullOrWhiteSpace(d.Name) || string.IsNullOrWhiteSpace(d.Phone)) return Results.BadRequest("Name and phone are required");
    db.Leads.Add(new Lead { Name = d.Name.Trim(), Phone = d.Phone.Trim(), Goal = d.Goal ?? "", Notes = d.Notes ?? "" });
    await db.SaveChangesAsync(); return Results.Ok();
});

// ---- ADMIN: generic CRUD + enable/disable
void Crud<T>(string path) where T : Entity
{
    var g = app.MapGroup($"/api/admin/{path}").RequireAuthorization(p => p.RequireRole("Admin"));
    g.MapGet("", (AppDb db) => db.Set<T>().OrderBy(x => x.Id).ToListAsync());
    g.MapPost("", async (AppDb db, T e) => { e.Id = 0; db.Add(e); await db.SaveChangesAsync(); return Results.Ok(e); });
    g.MapPut("/{id:int}", async (int id, AppDb db, T e) =>
    {
        var x = await db.Set<T>().FindAsync(id); if (x is null) return Results.NotFound();
        e.Id = id; db.Entry(x).CurrentValues.SetValues(e); await db.SaveChangesAsync(); return Results.Ok(x);
    });
    g.MapPatch("/{id:int}/toggle", async (int id, AppDb db) =>
    {
        var x = await db.Set<T>().FindAsync(id); if (x is null) return Results.NotFound();
        x.IsActive = !x.IsActive; await db.SaveChangesAsync(); return Results.Ok(x);
    });
}
Crud<Service>("services"); Crud<Plan>("plans"); Crud<EventItem>("events"); Crud<Feedback>("feedback");

var adm = app.MapGroup("/api/admin").RequireAuthorization(p => p.RequireRole("Admin"));
adm.MapGet("/users/{role}", (string role, AppDb db) => db.Users.Where(x => x.Role == role)
    .Select(x => new { x.Id, x.Email, x.FullName, x.Phone, x.Specialization, x.IsActive, x.NutritionistId }).ToListAsync());
adm.MapPost("/nutritionists", async (NutriDto d, AppDb db) =>
{
    var email = d.Email.ToLower();
    if (await db.Users.AnyAsync(x => x.Email == email)) return Results.BadRequest("Email already exists");
    db.Users.Add(new AppUser { Email = email, FullName = d.FullName, Phone = d.Phone, Specialization = d.Specialization, Role = "Nutritionist", PasswordHash = BCrypt.Net.BCrypt.HashPassword(d.Password) });
    await db.SaveChangesAsync(); return Results.Ok();
});
adm.MapPatch("/users/{id:guid}/toggle", async (Guid id, AppDb db) =>
{
    var u = await db.Users.FindAsync(id); if (u is null) return Results.NotFound();
    u.IsActive = !u.IsActive; await db.SaveChangesAsync(); return Results.Ok();
});
adm.MapPut("/customers/{id:guid}/assign/{nid:guid}", async (Guid id, Guid nid, AppDb db) =>
{
    var u = await db.Users.FindAsync(id); if (u is null) return Results.NotFound();
    u.NutritionistId = nid; await db.SaveChangesAsync(); return Results.Ok();
});
adm.MapGet("/tickets", (AppDb db) => db.Tickets.OrderByDescending(x => x.Id).ToListAsync());
adm.MapPatch("/tickets/{id:int}/close", async (int id, AppDb db) =>
{
    var t = await db.Tickets.FindAsync(id); if (t is null) return Results.NotFound();
    t.Status = "Closed"; await db.SaveChangesAsync(); return Results.Ok();
});

adm.MapGet("/leads", (AppDb db) => db.Leads.OrderByDescending(x => x.Id).ToListAsync());
adm.MapPatch("/leads/{id:int}/done", async (int id, AppDb db) =>
{
    var l = await db.Leads.FindAsync(id); if (l is null) return Results.NotFound();
    l.Status = "Contacted"; await db.SaveChangesAsync(); return Results.Ok();
});

// ---- NUTRITIONIST
var nu = app.MapGroup("/api/nutri").RequireAuthorization(p => p.RequireRole("Nutritionist"));
nu.MapGet("/clients", (ClaimsPrincipal c, AppDb db) => { var id = Uid(c);
    return db.Users.Where(x => x.NutritionistId == id).Select(x => new { x.Id, x.FullName, x.Email, x.Phone }).ToListAsync(); });
nu.MapGet("/clients/{cid:guid}/logs", async (Guid cid, ClaimsPrincipal c, AppDb db) =>
    await Owns(db, Uid(c), cid) ? Results.Ok(await db.Logs.Where(x => x.ClientId == cid).OrderByDescending(x => x.LogDate).Take(14).ToListAsync()) : Results.Forbid());
nu.MapGet("/clients/{cid:guid}/diet", async (Guid cid, ClaimsPrincipal c, AppDb db) =>
    await Owns(db, Uid(c), cid) ? Results.Ok(await db.DietPlans.Where(x => x.ClientId == cid).OrderByDescending(x => x.Id).ToListAsync()) : Results.Forbid());
nu.MapPost("/clients/{cid:guid}/diet", async (Guid cid, DietDto d, ClaimsPrincipal c, AppDb db) =>
{
    if (!await Owns(db, Uid(c), cid)) return Results.Forbid();
    db.DietPlans.Add(new DietPlan { ClientId = cid, NutritionistId = Uid(c), Title = d.Title, Details = d.Details });
    await db.SaveChangesAsync(); return Results.Ok();
});
Task<bool> Owns(AppDb db, Guid nid, Guid cid) => db.Users.AnyAsync(x => x.Id == cid && x.NutritionistId == nid);

// ---- CUSTOMER
var me = app.MapGroup("/api/me").RequireAuthorization(p => p.RequireRole("Customer"));
me.MapGet("/profile", async (ClaimsPrincipal c, AppDb db) =>
{
    var u = await db.Users.FindAsync(Uid(c))!;
    var n = u!.NutritionistId is null ? null : await db.Users.FindAsync(u.NutritionistId);
    var plan = u.PlanId is null ? null : await db.Plans.FindAsync(u.PlanId);
    return new { u.FullName, u.Email, plan = plan?.Name, nutritionist = n is null ? null : new { n.Id, n.FullName, n.Phone, n.Email } };
});
me.MapGet("/diet", (ClaimsPrincipal c, AppDb db) => { var id = Uid(c); return db.DietPlans.Where(x => x.ClientId == id).OrderByDescending(x => x.Id).ToListAsync(); });
me.MapGet("/logs", (ClaimsPrincipal c, AppDb db) => { var id = Uid(c); return db.Logs.Where(x => x.ClientId == id).OrderByDescending(x => x.LogDate).Take(30).ToListAsync(); });
me.MapPost("/logs", async (LogDto d, ClaimsPrincipal c, AppDb db) =>
{
    var id = Uid(c);
    var l = await db.Logs.FirstOrDefaultAsync(x => x.ClientId == id && x.LogDate == d.LogDate);
    if (l is null) { l = new DailyLog { ClientId = id, LogDate = d.LogDate }; db.Logs.Add(l); }
    l.Meals = d.Meals; l.Calories = d.Calories; l.WaterMl = d.WaterMl; l.WeightKg = d.WeightKg; l.Notes = d.Notes;
    await db.SaveChangesAsync(); return Results.Ok();
});
me.MapGet("/tickets", (ClaimsPrincipal c, AppDb db) => { var id = Uid(c); return db.Tickets.Where(x => x.ClientId == id).OrderByDescending(x => x.Id).ToListAsync(); });
me.MapPost("/tickets", async (TicketDto d, ClaimsPrincipal c, AppDb db) =>
{ db.Tickets.Add(new Ticket { ClientId = Uid(c), Subject = d.Subject, Body = d.Body }); await db.SaveChangesAsync(); return Results.Ok(); });
me.MapPost("/feedback", async (Feedback f, ClaimsPrincipal c, AppDb db) =>
{   // goes to admin for approval (IsActive=false) before showing publicly
    var u = await db.Users.FindAsync(Uid(c));
    db.Feedbacks.Add(new Feedback { CustomerName = u!.FullName, Rating = f.Rating, Comment = f.Comment, IsActive = false });
    await db.SaveChangesAsync(); return Results.Ok();
});

// ---- CHAT (customer <-> assigned nutritionist)
var chat = app.MapGroup("/api/chat").RequireAuthorization();
Task<bool> Related(AppDb db, Guid a, Guid b2) => db.Users.AnyAsync(x => (x.Id == a && x.NutritionistId == b2) || (x.Id == b2 && x.NutritionistId == a));
chat.MapGet("/{oid:guid}", async (Guid oid, ClaimsPrincipal c, AppDb db) =>
{
    var m = Uid(c); if (!await Related(db, m, oid)) return Results.Forbid();
    return Results.Ok(await db.Messages.Where(x => (x.FromId == m && x.ToId == oid) || (x.FromId == oid && x.ToId == m)).OrderBy(x => x.SentAt).ToListAsync());
});
chat.MapPost("/{oid:guid}", async (Guid oid, MsgDto d, ClaimsPrincipal c, AppDb db) =>
{
    var m = Uid(c); if (!await Related(db, m, oid)) return Results.Forbid();
    db.Messages.Add(new Message { FromId = m, ToId = oid, Body = d.Body }); await db.SaveChangesAsync(); return Results.Ok();
});

app.Run($"http://0.0.0.0:{Environment.GetEnvironmentVariable("PORT") ?? "5000"}");

record LoginDto(string Email, string Password);
record RegisterDto(string FullName, string Email, string Password, string Phone, int? PlanId);
record NutriDto(string FullName, string Email, string Password, string Phone, string Specialization);
record DietDto(string Title, string Details);
record LogDto(DateOnly LogDate, string Meals, int Calories, int WaterMl, double WeightKg, string Notes);
record TicketDto(string Subject, string Body);
record MsgDto(string Body);
record LeadDto(string Name, string Phone, string? Goal, string? Notes);
