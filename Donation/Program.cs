using Donation.Repositories;
using Donation.Services;
using Npgsql;
using System.Diagnostics;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddHttpClient(
    "OpenRouter",
    client =>
    {
        client.BaseAddress = new Uri(
            "https://openrouter.ai/api/v1/"
        );

        client.Timeout = TimeSpan.FromSeconds(120);
    }
);
builder.Services.AddScoped<AiDonationReviewService>();
builder.Services.AddScoped<AiDonationRepository>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var logger = sp.GetRequiredService<ILogger<AiDonationRepository>>();

    string baseUrl = config["Supabase:Url"];
    string serviceRoleKey = config["Supabase:ServiceRoleKey"];

    var httpClient = httpClientFactory.CreateClient();

    return new AiDonationRepository(
        httpClient,
        baseUrl,
        serviceRoleKey,
        logger
    );
});
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
    {
        policy.WithOrigins(
                "http://localhost:4200", 
                "https://localhost:4200",
                "https://ruby-chiou.github.io" // <-- 補上這一行你的前端正式網域
              )
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("找不到 DefaultConnection 連線字串。");

builder.Services.AddSingleton<NpgsqlDataSource>(
    NpgsqlDataSource.Create(connectionString)
);


builder.Services.AddHttpClient<IOpenRouterService, OpenRouterService>();


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("AllowAngular");

app.UseAuthorization();

app.MapControllers();

app.Run();
