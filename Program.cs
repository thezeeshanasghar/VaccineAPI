using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers(options => options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true)
    .AddNewtonsoftJson(options => { options.UseMemberCasing(); });

builder.Services.AddCors(options =>
{
    options.AddPolicy("corsapp", builder =>
    {
        builder.AllowAnyOrigin() // Allow any origin
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddAutoMapper(AppDomain.CurrentDomain.GetAssemblies());

var connectionString = builder.Environment.IsDevelopment() ?
                        builder.Configuration.GetConnectionString("DefaultConnection") : Environment.GetEnvironmentVariable("DefaultDBConnection");
var serverVersion = new MySqlServerVersion(new Version(8, 0, 31));

builder.Services.AddDbContext<VaccineAPI.Models.Context>(
    dbContextOptions => dbContextOptions
        .UseMySql(connectionString, serverVersion, options => options.EnableRetryOnFailure())
        // The following three options help with debugging, but should
        // be changed or removed for production.
        .LogTo(Console.WriteLine, LogLevel.Information)
        .EnableSensitiveDataLogging()
        .EnableDetailedErrors()
);

var environment = builder.Environment.EnvironmentName;
Console.WriteLine($"Current environment: {environment}");


// if (!builder.Environment.IsDevelopment())
// {
//     builder.WebHost.ConfigureKestrel(serverOptions =>
//     {
//         serverOptions.ListenAnyIP(80); // HTTP
//         serverOptions.ListenAnyIP(443, listenOptions => // HTTPS
//         {
//             listenOptions.UseHttps("/app/certs/myapi.pfx", "Ae!8bfb666");
//         });
//     });
// }
// else
// {
//     // Development environment - use development certificate
//     builder.WebHost.ConfigureKestrel(serverOptions =>
//     {
//         // serverOptions.ListenAnyIP(5000); // HTTP
//         serverOptions.ListenAnyIP(5001, listenOptions => // HTTPS
//         {
//             // Use ASP.NET Core's development certificate
//             listenOptions.UseHttps();
//         });
//     });
// }

var app = builder.Build();

// Configure the HTTP request pipeline.
// if (app.Environment.IsDevelopment())
// {
app.UseDeveloperExceptionPage();
app.UseSwagger();
app.UseSwaggerUI();
// }

app.UseHttpsRedirection();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.Combine(builder.Environment.ContentRootPath, "Resources")),
    RequestPath = "/Resources"
});

app.UseCors("corsapp");
app.UseAuthorization();
using (var scope = app.Services.CreateScope())
{
    var serviceProvider = scope.ServiceProvider;
    var dbContext = serviceProvider.GetRequiredService<VaccineAPI.Models.Context>();
    dbContext.Database.EnsureCreated(); // Optional: Ensure the database is created before applying the changes
    dbContext.Database.Migrate(); // Optional: Apply pending migrations before applying the changes
}
app.Run();
