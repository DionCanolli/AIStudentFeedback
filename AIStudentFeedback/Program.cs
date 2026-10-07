using AIFeedbackSystem.Data;
using AIFeedbackSystem.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

// Register Supabase (PostgreSQL) DbContext
builder.Services.AddDbContext<FeedbackContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Register NLP Service
builder.Services.AddHttpClient<NlpService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(45);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.MapRazorPages();
app.MapGet("/", () => Results.Redirect("/Feedback"));

app.Run();