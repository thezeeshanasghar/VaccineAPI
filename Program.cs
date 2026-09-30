using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers(options => options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true).AddNewtonsoftJson(options => { options.UseMemberCasing(); });
builder.Services.AddCors(p => p.AddPolicy("corsapp", builder => { builder.WithOrigins("*").AllowAnyMethod().AllowAnyHeader().WithExposedHeaders("Content-Disposition"); }));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddAutoMapper(AppDomain.CurrentDomain.GetAssemblies());
builder.Services.AddScoped<VaccineAPI.Services.InventoryTransactionService>();

// Inventory safety switches (appsettings "Inventory": {...}).
//  StrictInvariants        : true = a batch whose quantity disagrees with its ledger rows aborts the
//                            request; false (default until legacy drift is corrected) = it is only logged.
//  EnforceSingleWriter     : true = any inventory write that did not come from the inventory service is
//                            refused at SaveChanges; false (default) = it is only logged.
//  ExcludeExpiredFromFefo  : true (default) = an expired batch is never picked for a give; the expiry date
//                            itself is still usable (the last valid day).
VaccineAPI.Services.InventoryTransactionService.StrictInvariants = builder.Configuration.GetValue<bool>("Inventory:StrictInvariants");
// An expiry date is the LAST day a batch may be used; an expired batch is never given. On unless config says otherwise.
VaccineAPI.Services.InventoryTransactionService.ExcludeExpiredFromFefo = builder.Configuration.GetValue<bool?>("Inventory:ExcludeExpiredFromFefo") ?? true;
VaccineAPI.Models.Context.EnforceSingleInventoryWriter = builder.Configuration.GetValue<bool>("Inventory:EnforceSingleWriter");
VaccineAPI.Services.InventoryTransactionService.OnInvariantViolation = msg => Console.Error.WriteLine("[INVENTORY-INVARIANT] " + msg);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? Environment.GetEnvironmentVariable("DefaultConnection");
var serverVersion = new MySqlServerVersion(new Version(8, 0, 31));

builder.Services.AddDbContext<VaccineAPI.Models.Context>(
    dbContextOptions => dbContextOptions
        .UseMySql(connectionString, serverVersion)
        .LogTo(Console.WriteLine, LogLevel.Information)
        .EnableSensitiveDataLogging()
        .EnableDetailedErrors()
);

var app = builder.Build();

app.UseDeveloperExceptionPage();
app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();
app.UseCors("corsapp");
app.UseAuthorization();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
           Path.Combine(builder.Environment.ContentRootPath, "Resources")),
    RequestPath = "/Resources"
});

app.MapControllers();

app.Run();
