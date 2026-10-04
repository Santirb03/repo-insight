using RepoInsight.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddRepositoryScanning(builder.Configuration);
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options => options.AddPolicy("LocalWeb", policy =>
        policy.WithOrigins("http://localhost:3000").WithMethods("POST").AllowAnyHeader()));
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors("LocalWeb");
}

app.UseHttpsRedirection();

app.MapRepositoryEndpoints();

app.Run();
