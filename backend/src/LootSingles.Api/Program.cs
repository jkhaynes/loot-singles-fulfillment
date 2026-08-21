using LootSingles.Application.Import;
using LootSingles.Infrastructure.Import;
using LootSingles.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddDbContext<LootSinglesDbContext>(options => options.UseSqlServer(
    builder.Configuration.GetConnectionString("LootSingles")
        ?? throw new InvalidOperationException("Connection string 'LootSingles' is required.")));
builder.Services.AddScoped<IImportPersistence>(provider =>
    provider.GetRequiredService<LootSinglesDbContext>());
builder.Services.AddScoped<IPackingSlipParser, PdfPigPackingSlipParser>();
builder.Services.AddScoped<IPackingSlipImportService, PackingSlipImportService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
