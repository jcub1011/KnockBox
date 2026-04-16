using KnockBox.Platform;
using MyGame;

var builder = WebApplication.CreateBuilder(args);

builder.AddKnockBoxPlatform(options =>
{
    options.AddGameModule<MyGameModule>();
});

var app = builder.Build();

app.UseKnockBoxPlatform();

app.Run();
