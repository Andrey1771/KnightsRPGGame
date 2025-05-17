using KnightsRPGGame.Service.GameAPI.GameComponents;
using KnightsRPGGame.Service.GameAPI.Hubs;
using KnightsRPGGame.Service.GameAPI.Repository;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);


// Добавление CORS с конкретной политикой
builder.Services.AddCors(options =>
{
    var frontendConfigSection = builder.Configuration.GetSection("FrontendConfiguration:Uri");
    options.AddPolicy("AllowSpecificOrigin",
        builder =>
        {
            builder.WithOrigins(frontendConfigSection.Value)
                   .AllowAnyHeader()
                   .AllowAnyMethod()
                   .AllowCredentials(); // Если необходимы куки/учетные данные
        });
});

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddSignalR();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddScoped<IGameRepository, GameRepository>();
builder.Services.AddSingleton<GameManager>();
builder.Services.AddSingleton<FrameStreamer>();
builder.Services.AddSingleton<RoomManager>();

builder.Services.AddSingleton<IGameResultRepository, GameResultRepository>();
builder.Services.AddSingleton<IMongoClient>(sp =>
    new MongoClient(builder.Configuration["ConnectionStrings:MongoDb"]));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("AllowSpecificOrigin"); // Включаем CORS

app.UseAuthorization();

app.MapControllers();

app.MapHub<GameHub>("/gamehub");

app.Run();
