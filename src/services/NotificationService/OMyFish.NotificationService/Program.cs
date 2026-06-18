using MassTransit;
using Microsoft.Extensions.Hosting;
using OMyFish.NotificationService.Consumers;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((ctx, services) =>
    {
        services.AddMassTransit(x =>
        {
            x.AddConsumer<FishIdentifiedConsumer>();
            x.AddConsumer<ObservationCreatedConsumer>();

            x.UsingRabbitMq((busCtx, cfg) =>
            {
                var rabbitHost = ctx.Configuration["RabbitMQ__Host"]
                              ?? ctx.Configuration["RabbitMQ:Host"]
                              ?? "rabbitmq";
                cfg.Host(rabbitHost, "/", h =>
                {
                    h.Username(ctx.Configuration["RabbitMQ__Username"]
                            ?? ctx.Configuration["RabbitMQ:Username"] ?? "guest");
                    h.Password(ctx.Configuration["RabbitMQ__Password"]
                            ?? ctx.Configuration["RabbitMQ:Password"] ?? "guest");
                });

                cfg.ReceiveEndpoint("notifications.fish-identified", e =>
                {
                    e.ConfigureConsumer<FishIdentifiedConsumer>(busCtx);
                    e.UseMessageRetry(r => r.Exponential(3,
                        TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30)));
                });

                cfg.ReceiveEndpoint("notifications.observation-created", e =>
                {
                    e.ConfigureConsumer<ObservationCreatedConsumer>(busCtx);
                    e.UseMessageRetry(r => r.Exponential(3,
                        TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30)));
                });
            });
        });
    })
    .Build();

await host.RunAsync();
